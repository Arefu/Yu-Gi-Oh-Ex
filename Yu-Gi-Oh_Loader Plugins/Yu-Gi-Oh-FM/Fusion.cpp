#include "Fusion.h"

#include <Windows.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <format>
#include <map>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

#include <json.hpp>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Memory.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    // ---- card commands (g_CardCommandTable) ----
    constexpr int kFuse = 19;                       // the new command
    constexpr int kSummon = 4, kSetMonster = 6, kShowInfo = 14;
    constexpr int kHandZone = 13;
    constexpr int kSoundDecide = 39, kSoundCancel = 18, kSoundUnable = 71;

    constexpr uintptr_t kCommandTable = 0x140A52DF0;      // g_CardCommandTable: 19 x CommandEntry
    constexpr uintptr_t kTableLea = 0x14077868D;          // lea rsi, g_CardCommandTable (48 8D 35 rel32)
    constexpr uintptr_t kCountLea = 0x1407786A2;          // lea r14d, [rbp+13h]          (44 8D 75 13)

    struct CommandEntry { uint32_t Command; uint32_t Pad; int64_t Label; int64_t AltLabel; };
    static_assert(sizeof(CommandEntry) == 24, "CommandEntry layout drifted");
    CommandEntry g_GameTable[19]{};                       // the game's table, in its order
    CommandEntry* g_Table = nullptr;                      // the 20 entries the menu reads; rearranged per menu (the menu lists them in order)

    using BuildMenu_t = char(__fastcall*)(int64_t dialog, int mask);
    using RunCommand_t = int64_t(__fastcall*)(uint16_t card, unsigned int command, int mask, int frames);
    using IconOf_t = int64_t(__fastcall*)(int command);
    using UiFlags_t = unsigned int(__fastcall*)(int side, unsigned int zone, int index);
    using Submit_t = int64_t(__fastcall*)(int side, int zone, int index, int command);
    using MovedTriggers_t = int64_t(__fastcall*)(int eventType, void* moveRef, unsigned int flags, int param);

    uintptr_t orig_BuildMenu = 0x140778610;               // YGO::DUELUI::CardCommandMenu_Build
    uintptr_t orig_RunCommand = 0x140778270;              // YGO::DUELUI::CardCommandMenu_RunCommand
    uintptr_t orig_IconOf = 0x1407FD360;                  // YGO::DUELUI::CardCommand_IconId (command + 229)
    uintptr_t orig_UiFlags = 0x1407AD660;                 // YGO::DUELUI::Get_CardCommandFlags (scripted duels: cached array)
    uintptr_t orig_Submit = 0x140063840;                  // Duel_SubmitPick: the UI's answer (side, zone, index, command) -> the engine
    uintptr_t orig_MovedTriggers = 0x1400A2F70;           // YGO::Effects::Eval_CardMovedTriggers
    const auto CardIdAt = reinterpret_cast<int(__fastcall*)(uint16_t card)>(0x14076FC60);        // YGO::DUELUI::CardIdAtPosition
    const auto PlaySound = reinterpret_cast<void(__fastcall*)(int sound)>(0x14086C280);          // YGO::UI::PlayUISound
    // YGO::Duel::Move_HandCardToGraveOrBanish_Ev32(player, card instance, flags, 16 GY / 17 banished, by effect): what Hand_DiscardAt
    // (0x140089250) calls with (player, instance, 0x4000, 16, 0). FM has no Graveyard - used materials are simply gone - so they are
    // banished (17), the nearest this engine has.
    const auto MoveHandCard = reinterpret_cast<int64_t(__fastcall*)(int player, int instance, unsigned int flags, unsigned int to, int byEffect)>(0x140088D90);
    constexpr unsigned int kToBanished = 17;
    const auto DuelAnim = reinterpret_cast<int(__fastcall*)(int anim, int a2, int a3, int a4)>(0x1407EB7E0); // YGO::UI::Draw_DuelAnimationFromId
    constexpr int kAnimFusionMark = 46, kAnimFusionClear = 47, kAnimFusionPlay = 48;

    // ---- the duel state ----
    // The IDB's PlayerState (stru_143497C40) names the fields past ~+0x1B00 6 bytes short (field_1B52 = the card instances at +0x1B58).
    constexpr uintptr_t kDuel = 0x143497C40;
    constexpr size_t kPlayerBlock = 0xD94;
    constexpr size_t kHandCards = 0x19C;                 // u32[120] per player: id low 14 bits, instance = ((v & 0x4000) != 0) + 2 * (v >> 23)
    constexpr size_t kHandCount = 0x0C;
    constexpr uintptr_t kInstances = 0x143499798;        // 8 bytes per card instance, id = low 14 bits of the first u16
    constexpr size_t kBanishedCards = 0xA0C;             // u32[226] per player (zone 17), entries like the hand's
    constexpr size_t kBanishedCount = 0x1C;              // u32 per player

    template <typename T> T& At(size_t offset) { return *reinterpret_cast<T*>(kDuel + offset); }
    uint32_t& HandCard(int player, int index) { return At<uint32_t>((player & 1) * kPlayerBlock + kHandCards + 4 * index); }
    uint32_t HandCount(int player) { return At<uint32_t>((player & 1) * kPlayerBlock + kHandCount); }
    int InstanceOf(uint32_t handCard) { return ((handCard & 0x4000) != 0) + 2 * static_cast<int>(handCard >> 23); }
    uint16_t& InstanceId(int instance) { return *reinterpret_cast<uint16_t*>(kInstances + 8 * instance); }

    // ---- fm.json ----
    std::unordered_map<int, int> g_FmOfCard, g_CardOfFm;
    std::map<std::pair<int, int>, int> g_Fusions;
    std::unordered_map<int, std::string> g_Names;

    std::string Name(int id)
    {
        auto it = g_Names.find(id);
        return it != g_Names.end() ? std::format("{} ({})", it->second, id) : std::to_string(id);
    }

    int FuseIds(int a, int b)   // the result card id, 0 = no fusion
    {
        auto fa = g_FmOfCard.find(a), fb = g_FmOfCard.find(b);
        if (fa == g_FmOfCard.end() || fb == g_FmOfCard.end())
            return 0;
        auto pair = g_Fusions.find({ (std::min)(fa->second, fb->second), (std::max)(fa->second, fb->second) });
        if (pair == g_Fusions.end())
            return 0;
        auto card = g_CardOfFm.find(pair->second);
        return card != g_CardOfFm.end() ? card->second : 0;
    }

    bool Load()
    {
        const auto path = YGO::Mods::Find("fm.json");
        if (path.empty())
        {
            Logger::WriteLog("No fm.json in a mod or Yu-Gi-Oh-Ex: fusion from the hand is off", MODULE_NAME, 2);
            return false;
        }
        const nlohmann::json root = YGO::Mods::ReadJson(path);
        if (!root.is_object() || !root.contains("cards") || !root.contains("fusions"))
        {
            Logger::WriteLog("fm.json has no cards / fusions: fusion from the hand is off", MODULE_NAME, 2);
            return false;
        }
        for (const auto& card : root["cards"])
        {
            if (!card.contains("fm") || !card.contains("card") || !card["card"].is_number_integer())
                continue;
            const int fm = card["fm"].get<int>(), id = card["card"].get<int>();
            g_FmOfCard[id] = fm;
            g_CardOfFm[fm] = id;
            g_Names[id] = card.value("name", std::string());
        }
        for (const auto& fusion : root["fusions"])
        {
            if (!fusion.is_array() || fusion.size() != 3)
                continue;
            const int a = fusion[0].get<int>(), b = fusion[1].get<int>();
            g_Fusions[{ (std::min)(a, b), (std::max)(a, b) }] = fusion[2].get<int>();
        }
        Logger::WriteLog(std::format("fm.json: {} cards, {} fusions", g_FmOfCard.size(), g_Fusions.size()), MODULE_NAME, 0);
        return true;
    }

    // ---- the chain ----
    //
    // FM's fusion from the hand. Fuse on a hand monster starts a chain; while it is open every card of that hand is highlighted and offers
    // Fuse (add it) / Summon / Set (add it, then finish) / Show Details; a card already in the chain offers Summon / Set (finish) / Cancel
    // fusion. Each card added fuses with the chain so far (fm.json's pairs, either order) or, if they don't fuse, the chain so far is lost
    // and the new card carries on. Finishing: the UI's Summon / Set goes to the engine through Duel_SubmitPick; just before, the materials
    // are gone (banished: FM has no Graveyard; no card-moved triggers: FM has no such effects), the fusions play the game's fusion
    // animation, and the LAST card becomes the result, which the engine then Summons / Sets (no Tributes: Rules.cpp).
    struct Material { int Index; int Id; bool Fused; };   // Fused: this card fused with the chain (else the chain before it was lost)
    struct Chain
    {
        int Side = -1;                 // -1 = no chain
        std::vector<Material> Materials;
        int Result = 0;
        bool Finishing = false;        // Summon / Set chosen: applied when it is submitted
    } g_Chain;

    bool g_NoMoveTriggers = false;     // Apply's discards: Eval_CardMovedTriggers skipped

    // FM: used materials are gone for good. They are banished (the engine's own move, queued), and once the move has run they are taken
    // out of the banished pile as well: removed from the duel. Checked from the UI hooks, which run every frame.
    std::vector<std::pair<int, int>> g_Gone;   // (side, card instance) waiting to leave the banished pile

    void RemoveGone()
    {
        if (g_Gone.empty())
            return;
        for (auto it = g_Gone.begin(); it != g_Gone.end();)
        {
            const auto [side, instance] = *it;
            uint32_t& count = At<uint32_t>((side & 1) * kPlayerBlock + kBanishedCount);
            uint32_t* pile = &At<uint32_t>((side & 1) * kPlayerBlock + kBanishedCards);
            bool removed = false;
            for (uint32_t i = 0; i < count && i < 226; ++i)
                if ((pile[i] & 0x3FFF) != 0 && InstanceOf(pile[i]) == instance)
                {
                    Logger::WriteLog(std::format("Fuse: instance {} ({}) removed from the duel (banished pile {} of {})", instance, Name(pile[i] & 0x3FFF), i, count),
                        MODULE_NAME, 0);
                    std::memmove(pile + i, pile + i + 1, (count - i - 1) * sizeof(uint32_t));
                    pile[--count] = 0;
                    removed = true;
                    break;
                }
            it = removed ? g_Gone.erase(it) : it + 1;
        }
    }

    // The duel screen's per-instance card state (0x1427A569C, 148 bytes each; the dword at +0x70 = 0x1427A570C holds flags): bit 0x10 is
    // the "chosen" mark Polymerization's material pick puts on a card (YGO::DUELUI::SendCardCommand). The chain's cards carry it.
    constexpr uintptr_t kCardViewFlags = 0x1427A570C;
    constexpr size_t kCardViewSize = 148;
    constexpr uint32_t kChosenMark = 0x10;

    void Mark(int side, int index, bool on)
    {
        if (index < 0 || index >= static_cast<int>(HandCount(side)))
            return;
        const int instance = InstanceOf(HandCard(side, index));
        uint32_t& flags = *reinterpret_cast<uint32_t*>(kCardViewFlags + kCardViewSize * instance);
        flags = on ? (flags | kChosenMark) : (flags & ~kChosenMark);
    }

    void ClearChain()
    {
        for (const auto& m : g_Chain.Materials)
            Mark(g_Chain.Side, m.Index, false);
        g_Chain = {};
    }

    bool ChainOpen(int side) { return g_Chain.Side == side && !g_Chain.Finishing; }

    std::string Describe()
    {
        std::string text;
        for (const Material& m : g_Chain.Materials)
            text += (text.empty() ? "" : " + ") + Name(m.Id) + std::format(" [hand {}]", m.Index);
        return text + " => " + Name(g_Chain.Result);
    }

    bool InChain(int index)
    {
        return std::any_of(g_Chain.Materials.begin(), g_Chain.Materials.end(), [&](const Material& m) { return m.Index == index; });
    }

    void AddMaterial(int index, int id)
    {
        const int result = FuseIds(g_Chain.Result, id);
        if (result)
            Logger::WriteLog(std::format("Fuse: {} + {} -> {}", Name(g_Chain.Result), Name(id), Name(result)), MODULE_NAME, 0);
        else
            Logger::WriteLog(std::format("Fuse: {} + {} don't fuse: {} is lost, {} carries on", Name(g_Chain.Result), Name(id), Name(g_Chain.Result), Name(id)),
                MODULE_NAME, 0);
        g_Chain.Materials.push_back({ index, id, result != 0 });
        g_Chain.Result = result ? result : id;
        Mark(g_Chain.Side, index, true);
        Logger::WriteLog(std::format("Fuse: chain now {}", Describe()), MODULE_NAME, 0);
    }

    // ---- the menu ----
    CommandEntry* AllocateNear(uintptr_t target, size_t size)
    {
        SYSTEM_INFO info;
        GetSystemInfo(&info);
        const uintptr_t step = info.dwAllocationGranularity;
        for (uintptr_t address = (target & ~(step - 1)) - step; address > target - 0x7FF00000; address -= step)
            if (void* block = VirtualAlloc(reinterpret_cast<void*>(address), size, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE))
                return static_cast<CommandEntry*>(block);
        return nullptr;
    }

    // The menu lists the set bits in table order: the order for this menu (commands first in `front`, the rest as the game has them).
    void ArrangeTable(std::initializer_list<int> front, const wchar_t* fuseLabel)
    {
        int at = 0;
        auto put = [&](int command)
        {
            if (command == kFuse)
                g_Table[at++] = { static_cast<uint32_t>(kFuse), 0, reinterpret_cast<int64_t>(fuseLabel), 0 };
            else
                for (const CommandEntry& e : g_GameTable)
                    if (static_cast<int>(e.Command) == command)
                        g_Table[at++] = e;
        };
        for (int command : front)
            put(command);
        for (const CommandEntry& e : g_GameTable)
            if (std::find(front.begin(), front.end(), static_cast<int>(e.Command)) == front.end())
                g_Table[at++] = e;
        if (std::find(front.begin(), front.end(), kFuse) == front.end())
            put(kFuse);
    }

    bool PatchCommandTable()
    {
        const uint8_t leaTable[] = { 0x48, 0x8D, 0x35 }, leaCount[] = { 0x44, 0x8D, 0x75, 0x13 };
        if (std::memcmp(reinterpret_cast<const void*>(kTableLea), leaTable, 3) != 0
            || *reinterpret_cast<const int32_t*>(kTableLea + 3) != static_cast<int32_t>(kCommandTable - (kTableLea + 7))
            || std::memcmp(reinterpret_cast<const void*>(kCountLea), leaCount, 4) != 0)
        {
            Logger::WriteLog("The command menu code differs from the expected: no Fuse command", MODULE_NAME, 2);
            return false;
        }
        g_Table = AllocateNear(kCommandTable, 20 * sizeof(CommandEntry));
        if (!g_Table)
        {
            Logger::WriteLog("No memory near the game for the command table: no Fuse command", MODULE_NAME, 2);
            return false;
        }
        std::memcpy(g_GameTable, reinterpret_cast<const void*>(kCommandTable), sizeof(g_GameTable));
        ArrangeTable({}, L"Fuse");
        const int32_t rel = static_cast<int32_t>(reinterpret_cast<uintptr_t>(g_Table) - (kTableLea + 7));
        YGO::Memory::WriteBytes(kTableLea + 3, &rel, sizeof(rel));
        const uint8_t count = 20;
        YGO::Memory::WriteBytes(kCountLea + 3, &count, 1);
        Logger::WriteLog(std::format("Command table moved to {:#x} (20 entries, 19 = Fuse)", reinterpret_cast<uintptr_t>(g_Table)), MODULE_NAME, 0);
        return true;
    }

    int64_t __fastcall Hook_IconOf(int command)
    {
        return reinterpret_cast<IconOf_t>(orig_IconOf)(command == kFuse ? kSummon : command);
    }

    // While a chain is open every card of that hand can be picked (highlighted) and carries the Fuse bit.
    unsigned int __fastcall Hook_UiFlags(int side, unsigned int zone, int index)
    {
        RemoveGone();
        unsigned int flags = reinterpret_cast<UiFlags_t>(orig_UiFlags)(side, zone, index);
        if (ChainOpen(side) && zone == static_cast<unsigned int>(kHandZone))
            flags |= 1u << kFuse;
        return flags;
    }

    char __fastcall Hook_BuildMenu(int64_t dialog, int mask)
    {
        const uint16_t card = *reinterpret_cast<const uint16_t*>(dialog + 1488);
        const int side = card & 1, zone = (card >> 1) & 0x1F, index = card >> 6;
        const int before = mask;
        if (zone == kHandZone && ChainOpen(side) && InChain(index))
        {
            mask = (mask & (1 << kShowInfo)) | (1 << kSummon) | (1 << kSetMonster) | (1 << kFuse);
            ArrangeTable({ kSummon, kSetMonster, kFuse }, L"Cancel fusion");
        }
        else if (zone == kHandZone && ChainOpen(side))
        {
            mask = (mask & (1 << kShowInfo)) | (1 << kFuse) | (1 << kSummon) | (1 << kSetMonster);
            ArrangeTable({ kFuse, kSummon, kSetMonster }, L"Fuse");
        }
        else if (zone == kHandZone && (mask & (1 << kSummon)))
        {
            mask |= 1 << kFuse;
            ArrangeTable({ kSummon, kFuse, kSetMonster }, L"Fuse");
        }
        else
            ArrangeTable({}, L"Fuse");
        Logger::WriteLog(std::format("Menu for card {:#x} (side {}, zone {}, index {}, id {}): mask {:#x} -> {:#x}", card, side, zone, index, CardIdAt(card),
            before, mask), MODULE_NAME, 0);
        return reinterpret_cast<BuildMenu_t>(orig_BuildMenu)(dialog, mask);
    }

    int64_t __fastcall Hook_RunCommand(uint16_t card, unsigned int command, int mask, int frames)
    {
        const int side = card & 1, zone = (card >> 1) & 0x1F, index = card >> 6;
        const int id = zone == kHandZone && index < static_cast<int>(HandCount(side)) ? static_cast<int>(HandCard(side, index) & 0x3FFF) : CardIdAt(card);
        Logger::WriteLog(std::format("Command {} on card {:#x} (side {}, zone {}, index {}, id {}), mask {:#x}", command, card, side, zone, index, id, mask),
            MODULE_NAME, 0);
        const bool finish = command == kSummon || command == kSetMonster;
        if (command == static_cast<unsigned int>(kFuse))
        {
            if (!ChainOpen(side))
            {
                g_Chain = {};
                g_Chain.Side = side;
                g_Chain.Materials = { { index, id, false } };
                g_Chain.Result = id;
                Mark(side, index, true);
                Logger::WriteLog(std::format("Fuse: chain started: {}", Describe()), MODULE_NAME, 0);
            }
            else if (InChain(index))
            {
                Logger::WriteLog(std::format("Fuse: chain cancelled ({})", Describe()), MODULE_NAME, 0);
                ClearChain();
                PlaySound(kSoundCancel);
                return 0;
            }
            else if (g_Chain.Materials.size() >= 5)
            {
                Logger::WriteLog("Fuse: the chain already has 5 cards", MODULE_NAME, 1);
                PlaySound(kSoundUnable);
                return 0;
            }
            else
                AddMaterial(index, id);
            PlaySound(kSoundDecide);
            return 0;   // not an engine command: the menu closes and the engine keeps waiting
        }
        if (ChainOpen(side) && zone == kHandZone && finish)
        {
            if (!InChain(index))
                AddMaterial(index, id);   // Summon / Set on a new card: it is the last material
            g_Chain.Finishing = true;
            const Material last = g_Chain.Materials.back();
            Logger::WriteLog(std::format("Fuse: {} finishes the chain {} - sent for the last card (hand {})", command == kSummon ? "Summon" : "Set", Describe(),
                last.Index), MODULE_NAME, 0);
            card = static_cast<uint16_t>(side | (kHandZone << 1) | (last.Index << 6));
        }
        else if (g_Chain.Side >= 0 && !g_Chain.Finishing)
        {
            Logger::WriteLog(std::format("Fuse: command {} while a chain was open: chain dropped ({})", command, Describe()), MODULE_NAME, 1);
            ClearChain();
        }
        return reinterpret_cast<RunCommand_t>(orig_RunCommand)(card, command, mask, frames);
    }

    int64_t __fastcall Hook_MovedTriggers(int eventType, void* moveRef, unsigned int flags, int param)
    {
        if (g_NoMoveTriggers)
        {
            Logger::WriteLog(std::format("Fuse:   card-moved triggers skipped (event {})", eventType), MODULE_NAME, 0);
            return 0;
        }
        return reinterpret_cast<MovedTriggers_t>(orig_MovedTriggers)(eventType, moveRef, flags, param);
    }

    // The chain, applied just before the engine gets the Summon / Set. Returns the carrier's (the last card's) hand index (-1 = not applied).
    int Apply()
    {
        const int side = g_Chain.Side;
        Logger::WriteLog(std::format("Fuse: applying {} (hand count {})", Describe(), HandCount(side)), MODULE_NAME, 0);
        for (const Material& m : g_Chain.Materials)
        {
            const int now = m.Index < static_cast<int>(HandCount(side)) ? static_cast<int>(HandCard(side, m.Index) & 0x3FFF) : -1;
            if (now != m.Id)
            {
                Logger::WriteLog(std::format("Fuse: hand {} is now {} not {}: the hand changed, chain dropped", m.Index, Name(now), Name(m.Id)), MODULE_NAME, 2);
                return -1;
            }
        }
        // FM: the LAST card carries the chain. Every other card is gone (banished); the last becomes the result if its step fused (if not, it
        // is played as itself: the cards before it were lost).
        const Material last = g_Chain.Materials.back();
        const int lastInstance = InstanceOf(HandCard(side, last.Index));

        // the fusion animation for every step that fused (the two cards marked, then played on the carrier)
        for (size_t i = 1; i < g_Chain.Materials.size(); ++i)
        {
            if (!g_Chain.Materials[i].Fused)
                continue;
            const int a = InstanceOf(HandCard(side, g_Chain.Materials[i - 1].Index)), b = InstanceOf(HandCard(side, g_Chain.Materials[i].Index));
            Logger::WriteLog(std::format("Fuse: fusion animation: mark instances {} and {}, play on {}", a, b, lastInstance), MODULE_NAME, 0);
            DuelAnim(kAnimFusionMark, 0, 0, a);
            DuelAnim(kAnimFusionMark, 0, 0, b);
            DuelAnim(kAnimFusionPlay, side, kHandZone, lastInstance);
            DuelAnim(kAnimFusionClear, 0, 0, 0);   // takes the marks off again
        }

        for (const Material& m : g_Chain.Materials)
            Mark(side, m.Index, false);

        // the transform first, while the hand still has every card at its index
        if (g_Chain.Result != last.Id)
        {
            uint32_t& entry = HandCard(side, last.Index);
            if (g_Chain.Result <= 0 || g_Chain.Result > 0x3FFF)
                Logger::WriteLog(std::format("Fuse: {} doesn't fit a duel id (14 bits): {} stays", Name(g_Chain.Result), Name(last.Id)), MODULE_NAME, 2);
            else
            {
                entry = (entry & ~0x3FFFu) | static_cast<uint32_t>(g_Chain.Result);
                InstanceId(lastInstance) = static_cast<uint16_t>((InstanceId(lastInstance) & ~0x3FFF) | g_Chain.Result);
                Logger::WriteLog(std::format("Fuse: hand {} (instance {}) is now {} (entry {:#x}, instance id {:#x})", last.Index, lastInstance,
                    Name(g_Chain.Result), entry, InstanceId(lastInstance)), MODULE_NAME, 0);
            }
        }

        // the rest banished, highest index first. The move is queued (the hand count drops when the engine's messages run),
        // so the carrier keeps its index unless the count drops at once.
        int lastIndex = last.Index;
        std::vector<Material> rest(g_Chain.Materials.begin(), g_Chain.Materials.end() - 1);
        std::sort(rest.begin(), rest.end(), [](const Material& a, const Material& b) { return a.Index > b.Index; });
        g_NoMoveTriggers = true;
        for (const Material& m : rest)
        {
            const uint32_t entry = HandCard(side, m.Index);
            const uint32_t before = HandCount(side);
            Logger::WriteLog(std::format("Fuse: gone (banished): {} at hand {} (entry {:#x}, instance {})", Name(m.Id), m.Index, entry, InstanceOf(entry)),
                MODULE_NAME, 0);
            const int64_t r = MoveHandCard(side, InstanceOf(entry), 0x4000, kToBanished, 0);
            g_Gone.push_back({ side, InstanceOf(entry) });
            Logger::WriteLog(std::format("Fuse:   Move_HandCardToGraveOrBanish -> {}; hand count {} -> {}", r, before, HandCount(side)), MODULE_NAME, 0);
            if (HandCount(side) < before && m.Index < lastIndex)
                --lastIndex;
        }
        g_NoMoveTriggers = false;
        Logger::WriteLog(std::format("Fuse: the carrier is at hand {}: entry {:#x} (instance {})", lastIndex, HandCard(side, lastIndex), InstanceOf(HandCard(side, lastIndex))),
            MODULE_NAME, 0);
        return lastIndex;
    }

    int64_t __fastcall Hook_Submit(int side, int zone, int index, int command)
    {
        RemoveGone();
        Logger::WriteLog(std::format("Submit to the engine: side {}, zone {}, index {}, command {}", side, zone, index, command), MODULE_NAME, 0);
        if (g_Chain.Finishing && g_Chain.Side == side && zone == kHandZone && (command == kSummon || command == kSetMonster))
        {
            const int carrier = Apply();
            ClearChain();
            if (carrier >= 0)
            {
                Logger::WriteLog(std::format("Fuse: submitting {} for hand {}", command == kSummon ? "Summon" : "Set", carrier), MODULE_NAME, 0);
                index = carrier;
            }
        }
        return reinterpret_cast<Submit_t>(orig_Submit)(side, zone, index, command);
    }
}

namespace Fusion
{
    bool Attach()
    {
        if (!Load() || !PatchCommandTable())
            return false;
        DetourAttach(&(PVOID&)orig_IconOf, Hook_IconOf);
        DetourAttach(&(PVOID&)orig_BuildMenu, Hook_BuildMenu);
        DetourAttach(&(PVOID&)orig_RunCommand, Hook_RunCommand);
        DetourAttach(&(PVOID&)orig_UiFlags, Hook_UiFlags);
        DetourAttach(&(PVOID&)orig_Submit, Hook_Submit);
        DetourAttach(&(PVOID&)orig_MovedTriggers, Hook_MovedTriggers);
        Logger::WriteLog("Fusion from the hand: FM chain (menu, highlight, apply at submit; verbose)", MODULE_NAME, 0);
        return true;
    }
}
