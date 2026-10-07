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

    // ---------------------------------------------------------------- JSON

    bool AllowedCode(int code, bool synchro)
    {
        if (code == 0 || (code >= 1 && code <= 31) || (code >= 98 && code <= 516))
            return true;
        if (code == 72 || code == 73 || code == 83 || code == 94 || code == 97)   // 97: the game's per-card rule (kept for game cards)
            return true;
        return synchro && (code == 75 || code >= kFirstCardCode);
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
            error = std::format("\"{}\" {} is not a material the game's {} check knows (race, attribute, archetype up to 418, normal, gemini, pendulum{})",
                key, value.dump(), synchro ? "Synchro" : "Xyz", synchro ? ", synchro, a card" : "");
            return false;
        }
        out = static_cast<int16_t>(code);
        return true;
    }

    // Link_CardIsValidMaterial (0x1405B2500): "condition" and "material" are tested on every material, "including" on the whole selection
    // when its last material is picked (one of them must match). Codes the check knows, anything else in "including" can never be met.
    bool AllowedLinkCode(int code, bool including)
    {
        if (code == 0 || (code >= 1 && code <= 31) || (code >= 98 && code <= 516) || code == 95 || code == 97)
            return true;
        if (including)
            return code == 75 || code == 90 || code >= kFirstCardCode;           // Synchro, Tuner, a card by name
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
                including ? "race, attribute, archetype up to 418, link, synchro, tuner, a card" :
                            "race, attribute, archetype up to 418, level, normal, effect, xyz, pendulum, link, notToken");
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
                if (!ParseCode(synchro, "tuner", true, o.row[1], error) || !ParseCode(synchro, "nonTuner", true, o.row[2], error))
                    Logger::WriteLog(std::format("{}: {}, skipped", source.label, error), MODULE_NAME, 2);
                else if (count < 2 || count > 20)
                    Logger::WriteLog(std::format("{}: \"synchro\" materials (the Tuner plus the non-Tuners) must be 2 to 20, skipped", source.label), MODULE_NAME, 2);
                else
                {
                    o.row[3] = static_cast<int16_t>(exactly ? -count : count);
                    put(g_Synchro, synchroAt, o);
                }
            }
            else if (source.custom && kind.find("Synchro") != std::string::npos)
                put(g_Synchro, synchroAt, Table::Override{ id, true, false, {} });

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

    void Install(Table& table, void(__cdecl* refresh)())
    {
        size_t withRows = 0;
        for (const auto& o : table.overrides)
            withRows += o.hasRow;
        if (table.overrides.empty())
            return;
        if (!OperandsAreTheGames(table))
        {
            Logger::WriteLog(std::format("{}: the table's readers are not the expected instructions (different exe?), custom {} requirements are off",
                table.name, table.name), MODULE_NAME, 2);
            return;
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
            return;
        }
        Rebuild(table);
        if (!PointOperandsAtTable(table))
        {
            Logger::WriteLog(std::format("{}: could not write the table's operands", table.name), MODULE_NAME, 2);
            return;
        }
        for (uintptr_t entry : table.entries)
            CodePatch::AttachRefresh(entry, refresh, table.name);
        Logger::WriteLog(std::format("{}: table moved to 0x{:X}, {} game rows + {} requirement(s) ({} more cards use the generic rule if they borrow an id)",
            table.name, reinterpret_cast<uintptr_t>(table.memory), table.gameRows, withRows, table.overrides.size() - withRows), MODULE_NAME, 0);
    }
}

void SynchroXyz::Setup()
{
    Load();
    Install(g_Xyz, RefreshXyz);
    Install(g_Synchro, RefreshSynchro);
    Install(g_Link, RefreshLink);
}
