#include <Windows.h>
#include <detours.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <format>
#include <fstream>
#include <string>
#include <utility>
#include <vector>

#include <json.hpp>

#include "Ritual.h"
#include "CodePatch.h"
#include "SummonJson.h"
#include "Logger.h"

namespace
{
    constexpr uintptr_t kImageBase = CodePatch::kImageBase;
    constexpr uintptr_t kGameTable = 0x140AD07C0;    // RitualMonsterSpellTable
    constexpr int kGameRows = 96;
    constexpr int kMaxRows = 127;                     // the readers compare the row index with an imm8 (cmp esi, 60h)
    constexpr int kFirstExtraCardId = 0x3BC4;         // 15300, as in Yu-Gi-Oh-Cards/Card.h
    constexpr int kLastExtraCardId = 0xFFFE;          // 65534, as in Yu-Gi-Oh-Cards/Card.h

    struct Row
    {
        uint16_t monster;
        uint16_t spell;
    };

    // A Ritual Monster and the Ritual Spells that summon it (real ids). The table has one row per monster (the readers stop at its first row),
    // so a monster with several spells has its row's spell column set to the spell being asked about when a reader is entered (PickSpell).
    struct Pairing
    {
        uint16_t monster;
        std::vector<uint16_t> own;       // "ritualSpell" on the monster (the last entry wins), empty = only the generic spells
        bool hasOwn = false;
        std::vector<uint16_t> added;     // "ritualMonsters" on spells naming it (they add up)
        std::vector<uint16_t> spells;    // what it ends up with: own (or the game row's spell) then added, no repeats
        int gameRow = -1;                // a game monster's row
        int row = -1;                    // its row in this duel (-1: not shown, see RefreshRows)
    };
    std::vector<Pairing> g_Pairs;
    Row* g_Table = nullptr;

    // Instructions that address the table through the image base register (`movzx eax, word ptr [r9+rax*4+0xAD07C0]` and friends):
    // the disp32 is the table's RVA (+2 for the spell column). Offset = where the disp32 starts in the instruction.
    struct RvaSite { uintptr_t address; int offset; uint32_t rva; };
    constexpr RvaSite kRvaSites[] = {
        { 0x140069393, 5, 0xAD07C0 },   // Ritual_CanSummonMonsterWithSpell: find the monster's row
        { 0x1400693CD, 5, 0xAD07C2 },   // Ritual_CanSummonMonsterWithSpell: compare the spell column
        { 0x14006A493, 5, 0xAD07C0 },   // sub_140069EC0: find the monster's row
        { 0x140308E3A, 5, 0xAD07C0 },   // sub_140308DC0: the monster of row a1[17]
        { 0x14069C466, 4, 0xAD07C0 },   // sub_14069BF50: find the monster's row (no bound: it is always there)
    };
    // lea r14, [rip+disp32] -> the spell column (Ritual_GetNthMonsterForSpell walks rows with r14).
    constexpr uintptr_t kLeaSite = 0x14006CB3F;
    constexpr int kLeaLength = 7;
    // cmp <reg>, 60h: the row count, as the last byte of each.
    constexpr uintptr_t kBoundSites[] = { 0x1400693A4, 0x1400693A9, 0x14006A4A4, 0x14006CE2A };
    // The readers that can be first to look at the table in a duel: the custom rows are refreshed on entry. The first two are the only readers
    // of the spell column and both are handed the spell: Ritual_CanSummonMonsterWithSpell(EffectRecord* rcx: +0 = the spell's id, ...) and
    // Ritual_GetNthMonsterForSpell(char, int edx = the spell's id, ...). sub_140069EC0 only finds a monster's row (no spell).
    constexpr uintptr_t kCanSummonEntry = 0x1400692E0, kNthMonsterEntry = 0x14006CB20, kFindRowEntry = 0x140069EC0;

    uint16_t EngineId(uint16_t id)
    {
        return SummonJson::EngineId(id);
    }

    int g_ShownRows = kGameRows;   // the row count the readers' bounds hold now
    bool PointBounds(int rows);

    // Called from the entry stubs: the custom rows with this duel's ids (a custom card's own id outside a duel or when it borrows none).
    // The readers' row count is a signed byte (127 rows, 31 custom), so not every pair fits at once; but a pair only matters in a duel
    // its cards are in, and a custom card past 0x3FFF plays under a borrowed id below 0x4000 exactly then. So the custom rows are the
    // pairs whose monster or spell is in this duel (or has an id the engine uses as it is), up to the room left; the bound follows.
    // A row's spell column holds the monster's first spell; PickSpell swaps in another of its spells.
    void __cdecl RefreshRows()
    {
        constexpr int kRoom = kMaxRows - kGameRows;
        int rows = 0;
        for (Pairing& pair : g_Pairs)
        {
            const uint16_t spell = pair.spells.empty() ? 0 : EngineId(pair.spells[0]);
            if (pair.gameRow >= 0)
            {
                pair.row = pair.gameRow;
                g_Table[pair.row].spell = spell;
                continue;
            }
            const uint16_t monster = EngineId(pair.monster);
            if (monster > 0x3FFF || rows >= kRoom)
            {
                pair.row = -1;   // not in this duel (or no room left)
                continue;
            }
            pair.row = kGameRows + rows++;
            g_Table[pair.row] = { monster, spell };
        }
        if (kGameRows + rows != g_ShownRows)
            PointBounds(kGameRows + rows);
    }

    // `spell` is the id the reader is asked about (as the duel sees it): every monster that lists it among several spells gets it in its row.
    void PickSpell(uint16_t spell)
    {
        if (spell == 0)
            return;
        for (const Pairing& pair : g_Pairs)
        {
            if (pair.row < 0 || pair.spells.size() < 2)
                continue;
            for (uint16_t s : pair.spells)
            {
                if (EngineId(s) == spell)
                {
                    g_Table[pair.row].spell = spell;
                    break;
                }
            }
        }
    }

    void __cdecl RefreshForCanSummon(uintptr_t effect, uintptr_t)
    {
        RefreshRows();
        if (effect)
            PickSpell(*reinterpret_cast<const uint16_t*>(effect));
    }

    void __cdecl RefreshForNthMonster(uintptr_t, uintptr_t spell)
    {
        RefreshRows();
        PickSpell(static_cast<uint16_t>(spell));
    }

    // ---------------------------------------------------------------- cards.json

    bool ValidId(const nlohmann::json& value, int& id)
    {
        if (!value.is_number_integer())
            return false;
        id = value.get<int>();
        return id > 0 && id <= kLastExtraCardId;
    }

    Pairing& PairingFor(int monster)
    {
        for (Pairing& pair : g_Pairs)
            if (pair.monster == monster)
                return pair;
        return g_Pairs.emplace_back(Pairing{ static_cast<uint16_t>(monster) });
    }

    void AddUnique(std::vector<uint16_t>& spells, int spell)
    {
        if (spell != 0 && std::find(spells.begin(), spells.end(), spell) == spells.end())
            spells.push_back(static_cast<uint16_t>(spell));
    }

    // "ritualSpell": an id (0 = only the generic spells) or a list of ids. The later entry wins (summoning.json is read after cards.json).
    void SetOwnSpells(int monster, const nlohmann::json& value, const std::string& label)
    {
        std::vector<uint16_t> spells;
        int spell = 0;
        if (value.is_number_integer() && value.get<int>() == 0)
            ;
        else if (ValidId(value, spell))
            AddUnique(spells, spell);
        else if (value.is_array())
        {
            for (const auto& s : value)
            {
                if (ValidId(s, spell))
                    AddUnique(spells, spell);
                else if (!(s.is_number_integer() && s.get<int>() == 0))
                    Logger::WriteLog(std::format("{}: \"ritualSpell\" entry {} is not a card id, skipped", label, s.dump()), MODULE_NAME, 2);
            }
        }
        else
        {
            Logger::WriteLog(std::format("{}: \"ritualSpell\" must be a card id or a list of card ids, skipped", label), MODULE_NAME, 2);
            return;
        }
        Pairing& pair = PairingFor(monster);
        pair.own = std::move(spells);
        pair.hasOwn = true;
    }

    void LoadPairs()
    {
        // Explicit pairs first ("ritualSpell" on monsters, "ritualMonsters" on spells), then a spell-0 row for every other custom Ritual Monster.
        std::vector<int> ritualMonsters;
        for (const SummonJson::Entry& source : SummonJson::Entries())
        {
            const nlohmann::json& entry = *source.card;
            const int id = source.id;
            const std::string& label = source.label;
            const std::string kind = entry.contains("kind") && entry["kind"].is_string() ? entry["kind"].get<std::string>() : "";
            if (source.custom && kind.find("Ritual") != std::string::npos)
                ritualMonsters.push_back(id);

            if (entry.contains("ritualSpell"))
                SetOwnSpells(id, entry["ritualSpell"], label);
            if (entry.contains("ritualMonsters"))
            {
                if (!entry["ritualMonsters"].is_array())
                {
                    Logger::WriteLog(std::format("{}: \"ritualMonsters\" must be a list of card ids, skipped", label), MODULE_NAME, 2);
                    continue;
                }
                for (const auto& m : entry["ritualMonsters"])
                {
                    int monster = 0;
                    if (ValidId(m, monster))
                        AddUnique(PairingFor(monster).added, id);
                    else
                        Logger::WriteLog(std::format("{}: \"ritualMonsters\" entry {} is not a card id, skipped", label, m.dump()), MODULE_NAME, 2);
                }
            }
        }
        for (int monster : ritualMonsters)
            PairingFor(monster);   // a row with spell 0 when nothing names a spell for it

        // every pair is kept: RefreshRows shows the game only the ones whose cards are in the duel (31 at most at once)
    }

    // ---------------------------------------------------------------- moving the table

    // Every operand is checked before anything is written, so a different exe is left alone.
    bool OperandsAreTheGames()
    {
        for (const RvaSite& site : kRvaSites)
        {
            if (*reinterpret_cast<const uint32_t*>(site.address + site.offset) != site.rva)
                return false;
        }
        const int32_t lea = *reinterpret_cast<const int32_t*>(kLeaSite + 3);
        if (kLeaSite + kLeaLength + lea != kGameTable + 2)
            return false;
        for (uintptr_t site : kBoundSites)
        {
            if (*reinterpret_cast<const uint8_t*>(site) != kGameRows)
                return false;
        }
        return true;
    }

    // The readers' row count (cmp <reg>, imm8).
    bool PointBounds(int rows)
    {
        const uint8_t count = static_cast<uint8_t>(rows);
        bool ok = true;
        for (uintptr_t site : kBoundSites)
            ok &= CodePatch::WriteBytes(site, &count, sizeof(count));
        if (ok)
            g_ShownRows = rows;
        return ok;
    }

    bool PointOperandsAtTable(int rows)
    {
        const uintptr_t table = reinterpret_cast<uintptr_t>(g_Table);
        bool ok = true;
        for (const RvaSite& site : kRvaSites)
        {
            const uint32_t rva = static_cast<uint32_t>(table - kImageBase + (site.rva - (kGameTable - kImageBase)));
            ok &= CodePatch::WriteBytes(site.address + site.offset, &rva, sizeof(rva));
        }
        const int32_t lea = static_cast<int32_t>(static_cast<int64_t>(table + 2) - static_cast<int64_t>(kLeaSite + kLeaLength));
        ok &= CodePatch::WriteBytes(kLeaSite + 3, &lea, sizeof(lea));
        return PointBounds(rows) && ok;
    }

}

size_t Ritual::Count()
{
    return g_Pairs.size();
}

void Ritual::Setup()
{
    LoadPairs();
    if (g_Pairs.empty())
        return;

    if (!OperandsAreTheGames())
    {
        Logger::WriteLog("Ritual: the table's readers are not the expected instructions (different exe?), custom Ritual pairs are off", MODULE_NAME, 2);
        g_Pairs.clear();
        return;
    }
    g_Table = static_cast<Row*>(CodePatch::AllocNearImage(sizeof(Row) * kMaxRows));
    if (!g_Table)
    {
        Logger::WriteLog("Ritual: no memory near the game image for the table, custom Ritual pairs are off", MODULE_NAME, 2);
        g_Pairs.clear();
        return;
    }

    // The game's rows, then the custom ones. A monster that already has a game row keeps that row (the readers stop at the first row of a
    // monster): its spells start from the game's spell unless "ritualSpell" replaces it.
    std::memcpy(g_Table, reinterpret_cast<const void*>(kGameTable), sizeof(Row) * kGameRows);
    int customRows = 0;
    for (Pairing& pair : g_Pairs)
    {
        for (int r = 0; r < kGameRows && pair.gameRow < 0; ++r)
            if (g_Table[r].monster == pair.monster)
                pair.gameRow = r;
        if (pair.hasOwn)
            pair.spells = pair.own;
        else if (pair.gameRow >= 0)
            AddUnique(pair.spells, g_Table[pair.gameRow].spell);
        for (uint16_t spell : pair.added)
            AddUnique(pair.spells, spell);
        if (pair.gameRow < 0)
            ++customRows;
    }
    const int rows = kGameRows + customRows;
    RefreshRows();

    if (!PointOperandsAtTable(rows))
    {
        Logger::WriteLog("Ritual: could not write the table's operands", MODULE_NAME, 2);
        return;
    }
    CodePatch::AttachRefresh(kCanSummonEntry, RefreshForCanSummon, "Ritual");
    CodePatch::AttachRefresh(kNthMonsterEntry, RefreshForNthMonster, "Ritual");
    CodePatch::AttachRefresh(kFindRowEntry, RefreshRows, "Ritual");

    std::string list;
    for (const Pairing& pair : g_Pairs)
    {
        std::string spells;
        for (uint16_t spell : pair.spells)
            spells += std::format("{}{}", spells.empty() ? "" : "/", spell);
        list += std::format("{}{}{}->{}", list.empty() ? "" : ", ", pair.gameRow >= 0 ? "game " : "", pair.monster, spells.empty() ? "0" : spells);
    }
    Logger::WriteLog(std::format("Ritual: table moved to 0x{:X}, {} rows ({} custom; pairs: {})", reinterpret_cast<uintptr_t>(g_Table), rows, customRows, list), MODULE_NAME, 0);
}
