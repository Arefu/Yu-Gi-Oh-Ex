#include <Windows.h>
#include <detours.h>
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
    constexpr int kLastExtraCardId = 0x4E1F;

    struct Row
    {
        uint16_t monster;
        uint16_t spell;
    };

    // The custom pairs as cards.json gives them (real ids); g_Table rows kGameRows.. are these as the duel sees them.
    std::vector<Row> g_Pairs;
    Row* g_Table = nullptr;
    // A pair for a monster that already has a game row changes that row (the readers stop at a monster's first row): row index -> real spell id.
    std::vector<std::pair<int, uint16_t>> g_GameRowSpells;

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
    // The readers that can be first to look at the table in a duel: the custom rows are refreshed on entry.
    constexpr uintptr_t kRefreshEntries[] = { 0x1400692E0, 0x14006CB20, 0x140069EC0 };

    uint16_t EngineId(uint16_t id)
    {
        return SummonJson::EngineId(id);
    }

    // Called from the entry stubs: the custom rows with this duel's ids (a custom card's own id outside a duel or when it borrows none).
    void __cdecl RefreshRows()
    {
        for (size_t i = 0; i < g_Pairs.size(); ++i)
        {
            g_Table[kGameRows + i].monster = EngineId(g_Pairs[i].monster);
            g_Table[kGameRows + i].spell = EngineId(g_Pairs[i].spell);
        }
        for (const auto& [row, spell] : g_GameRowSpells)
            g_Table[row].spell = EngineId(spell);
    }

    // ---------------------------------------------------------------- cards.json

    bool ValidId(const nlohmann::json& value, int& id)
    {
        if (!value.is_number_integer())
            return false;
        id = value.get<int>();
        return id > 0 && id <= kLastExtraCardId;
    }

    void AddPair(int monster, int spell, const std::string& label)
    {
        // A monster has one Ritual Spell: the later entry wins (summoning.json is read after cards.json).
        for (Row& row : g_Pairs)
        {
            if (row.monster == monster)
            {
                if (row.spell != spell)
                    Logger::WriteLog(std::format("{}: Ritual Monster {} had Ritual Spell {} (a monster has one), now {}", label, monster, row.spell, spell), MODULE_NAME, 2);
                row.spell = static_cast<uint16_t>(spell);
                return;
            }
        }
        g_Pairs.push_back({ static_cast<uint16_t>(monster), static_cast<uint16_t>(spell) });
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
            {
                int spell = 0;
                if (ValidId(entry["ritualSpell"], spell) || (entry["ritualSpell"].is_number_integer() && entry["ritualSpell"].get<int>() == 0))
                    AddPair(id, spell, label);
                else
                    Logger::WriteLog(std::format("{}: \"ritualSpell\" must be a card id, skipped", label), MODULE_NAME, 2);
            }
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
                        AddPair(monster, id, label);
                    else
                        Logger::WriteLog(std::format("{}: \"ritualMonsters\" entry {} is not a card id, skipped", label, m.dump()), MODULE_NAME, 2);
                }
            }
        }
        for (int monster : ritualMonsters)
        {
            bool has = false;
            for (const Row& row : g_Pairs)
                has |= row.monster == monster;
            if (!has)
                g_Pairs.push_back({ static_cast<uint16_t>(monster), 0 });
        }

        constexpr size_t kRoom = kMaxRows - kGameRows;
        if (g_Pairs.size() > kRoom)
        {
            Logger::WriteLog(std::format("{} custom Ritual pairs, the table has room for {}: the rest are left out", g_Pairs.size(), kRoom), MODULE_NAME, 2);
            g_Pairs.resize(kRoom);
        }
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
        const uint8_t count = static_cast<uint8_t>(rows);
        for (uintptr_t site : kBoundSites)
            ok &= CodePatch::WriteBytes(site, &count, sizeof(count));
        return ok;
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

    // The game's rows, then the custom ones. A pair for a monster that already has a game row changes that row instead (the readers stop at
    // the first row of a monster), and is dropped from the custom rows.
    std::memcpy(g_Table, reinterpret_cast<const void*>(kGameTable), sizeof(Row) * kGameRows);
    for (auto it = g_Pairs.begin(); it != g_Pairs.end();)
    {
        int gameRow = -1;
        for (int r = 0; r < kGameRows && gameRow < 0; ++r)
            if (g_Table[r].monster == it->monster)
                gameRow = r;
        if (gameRow >= 0)
        {
            Logger::WriteLog(std::format("Ritual: game monster {} now uses Ritual Spell {} (was {})", it->monster, it->spell, g_Table[gameRow].spell), MODULE_NAME, 0);
            g_GameRowSpells.emplace_back(gameRow, it->spell);
            it = g_Pairs.erase(it);
        }
        else
            ++it;
    }
    const int rows = kGameRows + static_cast<int>(g_Pairs.size());
    RefreshRows();

    if (!PointOperandsAtTable(rows))
    {
        Logger::WriteLog("Ritual: could not write the table's operands", MODULE_NAME, 2);
        return;
    }
    for (uintptr_t entry : kRefreshEntries)
        CodePatch::AttachRefresh(entry, RefreshRows, "Ritual");

    std::string list;
    for (const Row& row : g_Pairs)
        list += std::format("{}{}->{}", list.empty() ? "" : ", ", row.monster, row.spell);
    Logger::WriteLog(std::format("Ritual: table moved to 0x{:X}, {} rows ({} custom: {})", reinterpret_cast<uintptr_t>(g_Table), rows, g_Pairs.size(), list), MODULE_NAME, 0);
}
