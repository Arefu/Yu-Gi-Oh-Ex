#include <Windows.h>
#include <algorithm>
#include <array>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <format>
#include <map>
#include <string>
#include <vector>

#include <detours.h>
#include <json.hpp>

#include "SynchroXyz.h"
#include "CodePatch.h"
#include "Fusion.h"
#include "SummonJson.h"
#include "Logger.h"

namespace
{
    using Row = std::array<int16_t, 4>;   // id + up to three values (Xyz rows use two)
    constexpr int16_t kUnusedId = 0x7FFF;  // sorts after every card id, so the binary searches never stop on it
    constexpr int kFirstCardCode = 3000;

    enum class SiteKind { RipLea, ImageRva, Bound };

    // One instruction operand that addresses the table or bounds its search. RipLea: `lea reg, [rip+disp32]` (disp32 at +3, 7 bytes).
    // ImageRva: `[base+index+rva]` / `lea reg, [base+rva]` with the image base in a register (disp32 at `offset`). Bound: `mov r32, imm32`
    // with the last row index (imm32 at +2).
    struct Site
    {
        uintptr_t address;
        SiteKind kind;
        int offset;
    };

    struct Table
    {
        const char* name;
        uintptr_t game;
        int gameRows;
        int words;                       // i16 per row: 3 (Xyz) or 4 (Synchro)
        std::vector<Site> sites;
        std::vector<uintptr_t> entries;  // functions that read the table: refreshed on entry

        struct Override
        {
            uint16_t id;                 // the card's real id
            bool custom;
            bool hasRow;                 // false: a custom card of this kind without requirements (only matters when it borrows an id)
            Row row;                     // values with real card ids
        };
        std::vector<Override> overrides;

        std::vector<Row> gameRows_;
        int16_t* memory = nullptr;
        int capacity = 0;
        std::vector<uint16_t> lastIds;   // the engine ids the rows were last built with
        std::vector<uint16_t> ids;       // reused by Rebuild (it runs on every reader call)
        bool built = false;
    };

    Table g_Xyz{
        "Xyz", 0x140ACF2E0, 219, 3,
        {
            { 0x14003EB9C, SiteKind::RipLea, 3 },     // Get_XyzSummonRequirementCode_ByCardId
            { 0x14003EBA9, SiteKind::Bound, 2 },
            { 0x14003F57C, SiteKind::Bound, 2 },      // Xyz_GetMaterialLevelIfUsable (0x14003F3C0)
            { 0x14003F5A4, SiteKind::ImageRva, 5 },
            { 0x14003F5C4, SiteKind::ImageRva, 3 },
            { 0x1404FBF2D, SiteKind::Bound, 2 },      // sub_1404FBE00
            { 0x1404FBF54, SiteKind::ImageRva, 4 },
            { 0x1404FBF73, SiteKind::ImageRva, 3 },
        },
        { 0x14003EB90, 0x14003F3C0, 0x1404FBE00 },
    };

    Table g_Synchro{
        "Synchro", 0x140AD0BA0, 194, 4,
        {
            { 0x14007C9AC, SiteKind::RipLea, 3 },     // Get_SynchroSummonRequirementCode_ByCardId
            { 0x14007C9B9, SiteKind::Bound, 2 },
            { 0x14007CA51, SiteKind::RipLea, 3 },     // Synchro_GetMaterialCode (0x14007CA40)
            { 0x14007CA60, SiteKind::Bound, 2 },
            { 0x14007CB4C, SiteKind::RipLea, 3 },     // sub_14007CB40
            { 0x14007CB59, SiteKind::Bound, 2 },
            { 0x14007D4F9, SiteKind::Bound, 2 },      // Synchro_CardMatchesMaterialSlot (0x14007D4B0)
            { 0x14007D516, SiteKind::ImageRva, 5 },
            { 0x14007D53B, SiteKind::ImageRva, 3 },
            { 0x14007F06D, SiteKind::Bound, 2 },      // Synchro_CanSummonWithSelection (0x14007EF20)
            { 0x14007F090, SiteKind::ImageRva, 5 },
            { 0x14007F0BA, SiteKind::ImageRva, 3 },
            { 0x14007F3D0, SiteKind::ImageRva, 5 },
            { 0x14007F3FA, SiteKind::ImageRva, 3 },
        },
        { 0x14007C9A0, 0x14007CA40, 0x14007CB40, 0x14007D4B0, 0x14007EF20 },
    };

    // g_LinkMaterialRequirements, 281 rows of {i16 Link Monster, i16 Requirement[3]} (named in the IDB). The player's check
    // Link_CardIsValidMaterial reads it through the image base (IDA lists no xref there), the AI through Link_GetMaterialRequirement.
    Table g_Link{
        "Link", 0x140BCA0D0, 281, 4,
        {
            { 0x1405B1E78, SiteKind::RipLea, 3 },     // Link_GetMaterialRequirement (0x1405B1E60)
            { 0x1405B1E81, SiteKind::Bound, 2 },
            { 0x1405B259E, SiteKind::Bound, 2 },      // Link_CardIsValidMaterial (0x1405B2500)
            { 0x1405B25C6, SiteKind::ImageRva, 5 },
            { 0x1405B25EB, SiteKind::ImageRva, 3 },
        },
        { 0x1405B1E60, 0x1405B2500 },
    };

    uintptr_t SiteTarget(const Site& site)
    {
        switch (site.kind)
        {
        case SiteKind::RipLea:
            return site.address + 7 + *reinterpret_cast<const int32_t*>(site.address + site.offset);
        case SiteKind::ImageRva:
            return CodePatch::kImageBase + *reinterpret_cast<const uint32_t*>(site.address + site.offset);
        default:
            return 0;
        }
    }

    // Every operand is checked before anything is written, so a different exe is left alone.
    bool OperandsAreTheGames(const Table& table)
    {
        for (const Site& site : table.sites)
        {
            if (site.kind == SiteKind::Bound)
            {
                if (*reinterpret_cast<const int32_t*>(site.address + site.offset) != table.gameRows - 1)
                    return false;
            }
            else if (SiteTarget(site) != table.game)
                return false;
        }
        return true;
    }

    bool PointOperandsAtTable(const Table& table)
    {
        const uintptr_t address = reinterpret_cast<uintptr_t>(table.memory);
        bool ok = true;
        for (const Site& site : table.sites)
        {
            switch (site.kind)
            {
            case SiteKind::RipLea:
            {
                const int32_t disp = static_cast<int32_t>(static_cast<int64_t>(address) - static_cast<int64_t>(site.address + 7));
                ok &= CodePatch::WriteBytes(site.address + site.offset, &disp, sizeof(disp));
                break;
            }
            case SiteKind::ImageRva:
            {
                const uint32_t rva = static_cast<uint32_t>(address - CodePatch::kImageBase);
                ok &= CodePatch::WriteBytes(site.address + site.offset, &rva, sizeof(rva));
                break;
            }
            case SiteKind::Bound:
            {
                const int32_t last = table.capacity - 1;
                ok &= CodePatch::WriteBytes(site.address + site.offset, &last, sizeof(last));
                break;
            }
            }
        }
        return ok;
    }

    // ---------------------------------------------------------------- rows

    // A material value as the duel sees it: a custom card id becomes the id it plays under.
    int16_t EngineValue(int16_t value)
    {
        return value >= SummonJson::kFirstExtraCardId ? static_cast<int16_t>(SummonJson::EngineId(static_cast<uint16_t>(value))) : value;
    }

    void Rebuild(Table& table)
    {
        // engine ids of every custom override and of every card id inside a row: when none changed the rows are already right
        std::vector<uint16_t>& ids = table.ids;
        ids.clear();
        for (const auto& o : table.overrides)
        {
            ids.push_back(o.custom ? SummonJson::EngineId(o.id) : o.id);
            for (int w = 1; w < table.words; ++w)
                ids.push_back(static_cast<uint16_t>(EngineValue(o.row[w])));
        }
        if (table.built && ids == table.lastIds)
            return;
        table.lastIds = ids;
        table.built = true;

        std::map<int16_t, Row> rows;
        for (const Row& row : table.gameRows_)
            rows[row[0]] = row;
        for (const auto& o : table.overrides)
        {
            const uint16_t engineId = o.custom ? SummonJson::EngineId(o.id) : o.id;
            if (!o.hasRow)
            {
                if (engineId != o.id || !o.custom)
                    rows.erase(static_cast<int16_t>(engineId));   // a borrowed id's own requirements are not this card's; a game card set to none
                continue;
            }
            Row row = o.row;
            row[0] = static_cast<int16_t>(engineId);
            for (int w = 1; w < table.words; ++w)
                row[w] = EngineValue(row[w]);
            rows[row[0]] = row;
        }

        int index = 0;
        for (const auto& [id, row] : rows)
        {
            if (index >= table.capacity)
                break;
            std::memcpy(table.memory + index * table.words, row.data(), sizeof(int16_t) * table.words);
            ++index;
        }
        for (; index < table.capacity; ++index)
        {
            Row unused{ kUnusedId, 0, 0, 0 };
            std::memcpy(table.memory + index * table.words, unused.data(), sizeof(int16_t) * table.words);
        }
    }

    void __cdecl RefreshXyz() { Rebuild(g_Xyz); }
    void __cdecl RefreshSynchro() { Rebuild(g_Synchro); }
    void __cdecl RefreshLink() { Rebuild(g_Link); }

    // ---------------------------------------------------------------- named-card materials (Xyz, Link "condition" / "material")
    //
    // The game's Xyz check and Link's per-material slots have no case for a card id: a code >= 3000 falls through to "usable", so on
    // its own a named card would accept any monster. Only Link's "including" slot calls Card_IsSameName. These wrap the two checks:
    // when the card's row names a card, the material must also have that card's name.

    using MaterialCheck_t = int64_t(__fastcall*)(int engine, int summoned, unsigned int player, int zone, int index);
    using CardAt_t = uint16_t(__fastcall*)(unsigned int player, int zone, int index);
    using SameName_t = int(__fastcall*)(int a, int b);

    MaterialCheck_t orig_XyzMaterialLevel = reinterpret_cast<MaterialCheck_t>(0x14003F3C0);   // Xyz_GetMaterialLevelIfUsable
    MaterialCheck_t orig_LinkValidMaterial = reinterpret_cast<MaterialCheck_t>(0x1405B2500);  // Link_CardIsValidMaterial
    const CardAt_t CardIdAt = reinterpret_cast<CardAt_t>(0x14003D3F0);                        // the card id at (player, zone, index)
    const SameName_t CardIsSameName = reinterpret_cast<SameName_t>(0x1407EBA00);              // j_Card_IsSameName

    const int16_t* RowOf(const Table& table, int id)
    {
        for (int r = 0; r < table.capacity; ++r)
        {
            const int16_t* row = table.memory + r * table.words;
            if (row[0] == id)
                return row;
            if (row[0] > id)
                break;   // the rows are sorted by id
        }
        return nullptr;
    }

    int64_t __fastcall Hook_XyzMaterialLevel(int engine, int summoned, unsigned int player, int zone, int index)
    {
        Rebuild(g_Xyz);
        const int64_t level = orig_XyzMaterialLevel(engine, summoned, player, zone, index);
        if (level == 0)
            return 0;
        const int16_t* row = RowOf(g_Xyz, summoned);
        if (row && row[1] >= kFirstCardCode && !CardIsSameName(CardIdAt(player, zone, index), row[1]))
            return 0;
        return level;
    }

    int64_t __fastcall Hook_LinkValidMaterial(int engine, int summoned, unsigned int player, int zone, int index)
    {
        Rebuild(g_Link);
        const int64_t valid = orig_LinkValidMaterial(engine, summoned, player, zone, index);
        if (valid == 0)
            return 0;
        const int16_t* row = RowOf(g_Link, summoned);
        if (!row || (row[1] < kFirstCardCode && row[2] < kFirstCardCode))
            return valid;
        // on the field a non-zero index is a card id the material counts as (the game's own second pass); elsewhere it is the index
        const int card = zone <= 6 && index != 0 ? index : CardIdAt(player, zone, index);
        for (int slot = 1; slot <= 2; ++slot)
            if (row[slot] >= kFirstCardCode && !CardIsSameName(card, row[slot]))
                return 0;
        return valid;
    }

    // ---------------------------------------------------------------- how many Tuners
    //
    // Synchro_GetTunerCount (0x14007CAF0) is a hardcoded switch: 2 for Red Nova Dragon, Vylon Omega, Hot Red Dragon Archfiend King Calamity,
    // Tyrant Red Dragon Archfiend and Crystron Quariongandrax, else 1. Synchro_CanSelectMaterial makes the first that many picks Tuners
    // (the row's Tuner code) and the rest non-Tuners; the row's material count already counts every Tuner. The AI and the summon
    // animation read the same function, so "tuners" is answered here. Every custom Synchro card is listed (1 by default) so one that
    // borrows a 2-Tuner card's id isn't given that card's count. Capped at 2: Duel__Msg__Handle_4C only tallies a second Tuner's Level.
    using TunerCount_t = int(__fastcall*)(int synchroCardId);
    TunerCount_t orig_TunerCount = reinterpret_cast<TunerCount_t>(0x14007CAF0);   // Synchro_GetTunerCount
    constexpr int kMaxTuners = 2;

    struct TunerCount
    {
        uint16_t id;
        bool custom;
        int tuners;
    };
    std::vector<TunerCount> g_TunerCounts;

    int __fastcall Hook_TunerCount(int synchroCardId)
    {
        for (const TunerCount& t : g_TunerCounts)
            if ((t.custom ? SummonJson::EngineId(t.id) : t.id) == synchroCardId)
                return t.tuners;
        return orig_TunerCount(synchroCardId);
    }

    bool HasNamedMaterial(const Table& table, int lastSlot)
    {
        for (const auto& o : table.overrides)
            for (int slot = 1; o.hasRow && slot <= lastSlot; ++slot)
                if (o.row[slot] >= kFirstCardCode)
                    return true;
        return false;
    }

    // ---------------------------------------------------------------- custom archetypes
    //
    // Each check tests an archetype with `lea r, [code-98]; cmp r, 1A2h; ja` before Is_CardInNamedArchetype (which the Cards plugin's hook
    // answers for custom archetypes, 419 up). A code past 516 misses the test and falls through: the Synchro check's switch then answers
    // "usable" for any monster. When a requirement names a custom archetype the bound is widened to Fusion's range (code <= 2496).
    constexpr int kFirstArchetypeCode = 98, kLastGameArchetypeCode = 516, kLastArchetypeCode = 2496;
    constexpr uint32_t kGameArchetypeBound = kLastGameArchetypeCode - kFirstArchetypeCode;   // 0x1A2

    struct BoundSite
    {
        uintptr_t address;
        int offset;   // the imm32 in the cmp
    };
    const BoundSite kArchetypeBounds[] = {
        { 0x14007D8C9, 2 },   // Synchro_CardMatchesMaterialSlot (0x14007D4B0): cmp edx, 1A2h
        { 0x14003F761, 1 },   // Xyz_GetMaterialLevelIfUsable (0x14003F3C0): cmp eax, 1A2h
        { 0x1405B295F, 2 },   // Link_CardIsValidMaterial (0x1405B2500), condition / material: cmp edx, 1A2h
        { 0x1405B35CC, 3 },   // Link_CardIsValidMaterial, including: cmp r15d, 1A2h
    };

    bool IsCustomArchetype(int code) { return code > kLastGameArchetypeCode && code <= kLastArchetypeCode; }

    bool UsesCustomArchetype(const Table& table)
    {
        for (const auto& o : table.overrides)
            for (int slot = 1; o.hasRow && slot < table.words; ++slot)
                if (IsCustomArchetype(o.row[slot]))
                    return true;
        return false;
    }

    void WidenArchetypeChecks()
    {
        // the game's own rows never hold 517..2496 (they would change meaning); checked so a different exe is left alone
        for (const Table* table : { &g_Synchro, &g_Xyz, &g_Link })
            for (int r = 0; r < table->gameRows; ++r)
                for (int w = 1; w < table->words; ++w)
                {
                    const int code = reinterpret_cast<const volatile int16_t*>(table->game)[r * table->words + w];
                    if (IsCustomArchetype(code))
                    {
                        Logger::WriteLog(std::format("{}: a game row uses material code {}, custom archetypes in Synchro/Xyz/Link stay off", table->name,
                            code), MODULE_NAME, 2);
                        return;
                    }
                }
        for (const BoundSite& site : kArchetypeBounds)
            if (*reinterpret_cast<const volatile uint32_t*>(site.address + site.offset) != kGameArchetypeBound)
            {
                Logger::WriteLog(std::format("The archetype test at 0x{:X} is not the expected instruction (different exe?), custom archetypes in "
                    "Synchro/Xyz/Link stay off", site.address), MODULE_NAME, 2);
                return;
            }
        const uint32_t bound = kLastArchetypeCode - kFirstArchetypeCode;
        bool ok = true;
        for (const BoundSite& site : kArchetypeBounds)
            ok &= CodePatch::WriteBytes(site.address + site.offset, &bound, sizeof(bound));
        Logger::WriteLog(ok ? "Synchro/Xyz/Link archetype tests widened to custom archetypes" : "Could not widen the Synchro/Xyz/Link archetype tests",
            MODULE_NAME, ok ? 0 : 2);
    }

    // ---------------------------------------------------------------- JSON

    bool AllowedCode(int code, bool synchro)
    {
        if (code == 0 || (code >= 1 && code <= 31) || (code >= kFirstArchetypeCode && code <= kLastArchetypeCode))
            return true;
        if (code == 72 || code == 73 || code == 83 || code == 94 || code == 97)   // 97: the game's per-card rule (kept for game cards)
            return true;
        return code >= kFirstCardCode || (synchro && code == 75);   // a card by name: Xyz through Hook_XyzMaterialLevel
    }

    // A material: absent / null / "any" / 0 = any; a number = a game code (or, for Synchro, a card id >= 3000); a word = as Fusion's.
    bool ParseCode(const nlohmann::json& entry, const char* key, bool synchro, int16_t& out, std::string& error)
    {
        out = 0;
        if (!entry.contains(key) || entry[key].is_null())
            return true;
        const nlohmann::json& value = entry[key];
        int code = -1;
        if (value.is_number_integer())
            code = value.get<int>();
        else if (value.is_string())
        {
            const std::string word = value.get<std::string>();
            code = word.empty() || _stricmp(word.c_str(), "any") == 0 ? 0 : Fusion::MaterialCodeOf(word);
            if (code == 0 && !(word.empty() || _stricmp(word.c_str(), "any") == 0))
                code = -1;
        }
        if (code < 0 || code > SummonJson::kLastExtraCardId || !AllowedCode(code, synchro))
        {
            error = std::format("\"{}\" {} is not a material the game's {} check knows (race, attribute, archetype, normal, gemini, pendulum{})",
                key, value.dump(), synchro ? "Synchro" : "Xyz", synchro ? ", synchro, a card" : ", a card");
            return false;
        }
        out = static_cast<int16_t>(code);
        return true;
    }

    // Link_CardIsValidMaterial (0x1405B2500): "condition" and "material" are tested on every material, "including" on the whole selection
    // when its last material is picked (one of them must match). Codes the check knows, anything else in "including" can never be met.
    bool AllowedLinkCode(int code, bool including)
    {
        if (code == 0 || (code >= 1 && code <= 31) || (code >= kFirstArchetypeCode && code <= kLastArchetypeCode) || code == 95 || code == 97 ||
            code >= kFirstCardCode)
            return true;   // a card by name: "including" is the game's, the other two through Hook_LinkValidMaterial
        if (including)
            return code == 75 || code == 90;                                      // Synchro, Tuner
        return (code >= 32 && code <= 67) || code == 73 || code == 74 || code == 82 || code == 83 || code == 96;   // Levels, Normal, Effect, Xyz, Pendulum, not a Token
    }

    bool ParseLinkCode(const nlohmann::json& entry, const char* key, bool including, int16_t& out, std::string& error)
    {
        out = 0;
        if (!entry.contains(key) || entry[key].is_null())
            return true;
        const nlohmann::json& value = entry[key];
        int code = -1;
        if (value.is_number_integer())
            code = value.get<int>();
        else if (value.is_string())
        {
            const std::string word = value.get<std::string>();
            std::string squashed;
            for (char c : word)
                if (c != ' ' && c != '-' && c != '_')
                    squashed.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
            if (squashed.empty() || squashed == "any")
                code = 0;
            else if (squashed == "nottoken" || squashed == "excepttoken" || squashed == "excepttokens")
                code = 96;
            else
                code = Fusion::MaterialCodeOf(word) > 0 ? Fusion::MaterialCodeOf(word) : -1;
        }
        if (code < 0 || code > SummonJson::kLastExtraCardId || !AllowedLinkCode(code, including))
        {
            error = std::format("\"{}\" {} is not a material the game's Link check knows ({})", key, value.dump(),
                including ? "race, attribute, archetype, link, synchro, tuner, a card" :
                            "race, attribute, archetype, level, normal, effect, xyz, pendulum, link, notToken, a card");
            return false;
        }
        out = static_cast<int16_t>(code);
        return true;
    }

    int ParseCount(const nlohmann::json& entry, int fallback)
    {
        return entry.contains("materials") && entry["materials"].is_number_integer() ? entry["materials"].get<int>() : fallback;
    }

    void Load()
    {
        std::map<int, size_t> xyzAt, synchroAt, linkAt;   // id -> index in overrides: a later entry (summoning.json) replaces an earlier one
        auto put = [](Table& table, std::map<int, size_t>& at, const Table::Override& o)
        {
            if (auto it = at.find(o.id); it != at.end())
                table.overrides[it->second] = o;
            else
            {
                at[o.id] = table.overrides.size();
                table.overrides.push_back(o);
            }
        };

        for (const SummonJson::Entry& source : SummonJson::Entries())
        {
            const nlohmann::json& entry = *source.card;
            const std::string kind = entry.contains("kind") && entry["kind"].is_string() ? entry["kind"].get<std::string>() : "";
            const uint16_t id = static_cast<uint16_t>(source.id);

            if (entry.contains("xyz") && entry["xyz"].is_object())
            {
                const nlohmann::json& xyz = entry["xyz"];
                Table::Override o{ id, source.custom, true, {} };
                std::string error;
                const int count = ParseCount(xyz, 2);
                if (!ParseCode(xyz, "material", false, o.row[1], error))
                    Logger::WriteLog(std::format("{}: {}, skipped", source.label, error), MODULE_NAME, 2);
                else if (count < 1 || count > 20)
                    Logger::WriteLog(std::format("{}: \"xyz\" materials must be 1 to 20, skipped", source.label), MODULE_NAME, 2);
                else
                {
                    o.row[2] = static_cast<int16_t>(count);
                    put(g_Xyz, xyzAt, o);
                }
            }
            else if (source.custom && kind.find("Xyz") != std::string::npos)
                put(g_Xyz, xyzAt, Table::Override{ id, true, false, {} });

            if (entry.contains("synchro") && entry["synchro"].is_object())
            {
                const nlohmann::json& synchro = entry["synchro"];
                Table::Override o{ id, source.custom, true, {} };
                std::string error;
                const int count = ParseCount(synchro, 2);
                const bool exactly = synchro.value("exactly", false);
                const int tuners = synchro.contains("tuners") && synchro["tuners"].is_number_integer() ? synchro["tuners"].get<int>() : 1;
                if (!ParseCode(synchro, "tuner", true, o.row[1], error) || !ParseCode(synchro, "nonTuner", true, o.row[2], error))
                    Logger::WriteLog(std::format("{}: {}, skipped", source.label, error), MODULE_NAME, 2);
                else if (count < 2 || count > 20)
                    Logger::WriteLog(std::format("{}: \"synchro\" materials (the Tuners plus the non-Tuners) must be 2 to 20, skipped", source.label), MODULE_NAME, 2);
                else if (tuners < 1 || tuners > kMaxTuners || tuners >= count)
                    Logger::WriteLog(std::format("{}: \"synchro\" tuners must be 1 to {} and fewer than the materials ({}), skipped", source.label,
                        kMaxTuners, count), MODULE_NAME, 2);
                else
                {
                    o.row[3] = static_cast<int16_t>(exactly ? -count : count);
                    put(g_Synchro, synchroAt, o);
                    if (source.custom || synchro.contains("tuners"))
                    {
                        std::erase_if(g_TunerCounts, [&](const TunerCount& t) { return t.id == id && t.custom == source.custom; });
                        g_TunerCounts.push_back({ id, source.custom, tuners });
                    }
                }
            }
            else if (source.custom && kind.find("Synchro") != std::string::npos)
            {
                put(g_Synchro, synchroAt, Table::Override{ id, true, false, {} });
                std::erase_if(g_TunerCounts, [&](const TunerCount& t) { return t.id == id && t.custom; });
                g_TunerCounts.push_back({ id, true, 1 });
            }

            if (entry.contains("link") && entry["link"].is_object())
            {
                const nlohmann::json& link = entry["link"];
                Table::Override o{ id, source.custom, true, {} };
                std::string error;
                if (!ParseLinkCode(link, "condition", false, o.row[1], error) || !ParseLinkCode(link, "material", false, o.row[2], error) ||
                    !ParseLinkCode(link, "including", true, o.row[3], error))
                    Logger::WriteLog(std::format("{}: {}, skipped", source.label, error), MODULE_NAME, 2);
                else if (o.row[1] == 0 && o.row[2] == 0 && o.row[3] == 0)
                    put(g_Link, linkAt, Table::Override{ id, source.custom, false, {} });   // "any monsters": no row
                else
                    put(g_Link, linkAt, o);
            }
            else if (source.custom && kind.find("Link") != std::string::npos)
                put(g_Link, linkAt, Table::Override{ id, true, false, {} });
        }
    }

    // `hooked`: an entry a named-card hook wraps (the hook refreshes the table itself, so no refresh stub there)
    bool Install(Table& table, void(__cdecl* refresh)(), uintptr_t hooked = 0)
    {
        size_t withRows = 0;
        for (const auto& o : table.overrides)
            withRows += o.hasRow;
        if (table.overrides.empty())
            return false;
        if (!OperandsAreTheGames(table))
        {
            Logger::WriteLog(std::format("{}: the table's readers are not the expected instructions (different exe?), custom {} requirements are off",
                table.name, table.name), MODULE_NAME, 2);
            return false;
        }
        table.gameRows_.resize(table.gameRows);
        for (int r = 0; r < table.gameRows; ++r)
        {
            Row row{};
            std::memcpy(row.data(), reinterpret_cast<const int16_t*>(table.game) + r * table.words, sizeof(int16_t) * table.words);
            table.gameRows_[r] = row;
        }
        table.capacity = table.gameRows + static_cast<int>(table.overrides.size());
        table.memory = static_cast<int16_t*>(CodePatch::AllocNearImage(sizeof(int16_t) * table.words * table.capacity));
        if (!table.memory)
        {
            Logger::WriteLog(std::format("{}: no memory near the game image for the table, custom requirements are off", table.name), MODULE_NAME, 2);
            return false;
        }
        Rebuild(table);
        if (!PointOperandsAtTable(table))
        {
            Logger::WriteLog(std::format("{}: could not write the table's operands", table.name), MODULE_NAME, 2);
            return false;
        }
        for (uintptr_t entry : table.entries)
            if (entry != hooked)
                CodePatch::AttachRefresh(entry, refresh, table.name);
        Logger::WriteLog(std::format("{}: table moved to 0x{:X}, {} game rows + {} requirement(s) ({} more cards use the generic rule if they borrow an id)",
            table.name, reinterpret_cast<uintptr_t>(table.memory), table.gameRows, withRows, table.overrides.size() - withRows), MODULE_NAME, 0);
        return true;
    }
}

void SynchroXyz::Setup()
{
    Load();
    if (UsesCustomArchetype(g_Synchro) || UsesCustomArchetype(g_Xyz) || UsesCustomArchetype(g_Link))
        WidenArchetypeChecks();
    const bool xyzNamed = HasNamedMaterial(g_Xyz, 1), linkNamed = HasNamedMaterial(g_Link, 2);
    if (Install(g_Xyz, RefreshXyz, xyzNamed ? reinterpret_cast<uintptr_t>(orig_XyzMaterialLevel) : 0) && xyzNamed)
        DetourAttach(&(PVOID&)orig_XyzMaterialLevel, Hook_XyzMaterialLevel);
    Install(g_Synchro, RefreshSynchro);
    if (!g_TunerCounts.empty())
    {
        DetourAttach(&(PVOID&)orig_TunerCount, Hook_TunerCount);
        Logger::WriteLog(std::format("Synchro: Tuner counts for {} card(s) (Synchro_GetTunerCount hooked)", g_TunerCounts.size()), MODULE_NAME, 0);
    }
    if (Install(g_Link, RefreshLink, linkNamed ? reinterpret_cast<uintptr_t>(orig_LinkValidMaterial) : 0) && linkNamed)
        DetourAttach(&(PVOID&)orig_LinkValidMaterial, Hook_LinkValidMaterial);
}
