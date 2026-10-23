#include "Progress.h"
#include "Common.h"

#include <Windows.h>
#include <cstdint>
#include <format>
#include <string>

#include <json.hpp>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"
#include "YuGiOh/YuGiOh-Addresses.h"

namespace
{
    using namespace Campaign;

    constexpr size_t kUnlockedCharacters = 0x18;   // player section: 240-bit set, bit = character id
    constexpr size_t kModeBits = 0xB94;            // player section: bit 0 Duelist Challenges, 1 Battle Packs, 2 Card Shop
    constexpr uint32_t kCharacterSlots = 240;
    constexpr uintptr_t kCharacterRecords = YGO::Addr::g_CharacterRecords;   // CharacterRecord 0x68, Key at +0x20
    constexpr size_t kCharacterRecordSize = 0x68, kCharacterKey = 0x20, kCharacterDeck = 0x8, kCharacterSelectable = 0xC;
    constexpr size_t kDeckStates = 0x38;           // player section: u32 per deck id, nonzero = unlocked (beaten)
    constexpr int32_t kDeckSlots = 700;

    using LayoutPage_t = int64_t(__fastcall*)(void* screen, char animate);
    uintptr_t orig_LayoutPage = 0x1408583D0;       // RIX::ScreenMainMenu::LayoutPage

    bool g_UnlockAllCharacters = false;
    uint32_t g_ModeBits = 0;

    bool Defined(uint32_t id)
    {
        return *reinterpret_cast<const char* const*>(kCharacterRecords + id * kCharacterRecordSize + kCharacterKey) != nullptr;
    }

    void Apply()
    {
        uint8_t* player = GetPlayerSection(0xFFFFFFFD);
        if (!player)
            return;
        auto& modes = *reinterpret_cast<uint32_t*>(player + kModeBits);
        if ((modes & g_ModeBits) != g_ModeBits)
        {
            modes |= g_ModeBits;
            Logger::WriteLog(std::format("progress.json: main menu modes unlocked (bits {:#x})", g_ModeBits), MODULE_NAME, 0);
        }
        if (g_UnlockAllCharacters)
        {
            // Free Duel / Duelist Challenges list an opponent as unlocked by their deck's state (player section +0x38 + 4 * deck, set when
            // all their Campaign duels are won; FreeDuel_BuildOpponentList), so the character's own deck is unlocked as well.
            auto* bits = reinterpret_cast<uint32_t*>(player + kUnlockedCharacters);
            auto* deckStates = reinterpret_cast<uint32_t*>(player + kDeckStates);
            int added = 0;
            for (uint32_t id = 0; id < kCharacterSlots; ++id)
            {
                if (!Defined(id))
                    continue;
                if (!(bits[id >> 5] >> (id & 31) & 1))
                {
                    bits[id >> 5] |= 1u << (id & 31);
                    ++added;
                }
                const int32_t deck = *reinterpret_cast<const int32_t*>(kCharacterRecords + id * kCharacterRecordSize + kCharacterDeck);
                const uint32_t selectable = *reinterpret_cast<const uint32_t*>(kCharacterRecords + id * kCharacterRecordSize + kCharacterSelectable);
                if (selectable && deck >= 0 && deck < kDeckSlots && deckStates[deck] == 0)
                {
                    deckStates[deck] = 1;
                    ++added;
                }
            }
            if (added)
                Logger::WriteLog(std::format("progress.json: {} character / character deck unlock(s) set", added), MODULE_NAME, 0);
        }
    }

    int64_t __fastcall Hook_LayoutPage(void* screen, char animate)
    {
        Apply();
        return reinterpret_cast<LayoutPage_t>(orig_LayoutPage)(screen, animate);
    }
}

namespace Progress
{
    void Attach()
    {
        std::vector<std::string> problems;
        const nlohmann::json root = YGO::Mods::ReadMerged("progress.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", it is left out", MODULE_NAME, 2);
        if (!root.is_object())
            return; // progress.json is optional
        g_UnlockAllCharacters = root.value("unlockAllCharacters", false);
        if (auto modes = root.find("modes"); modes != root.end() && modes->is_object())
        {
            if (modes->value("duelistChallenges", false)) g_ModeBits |= 1;
            if (modes->value("battlePacks", false)) g_ModeBits |= 2;
            if (modes->value("cardShop", false)) g_ModeBits |= 4;
        }
        if (!g_UnlockAllCharacters && !g_ModeBits)
            return;
        DetourAttach(&(PVOID&)orig_LayoutPage, Hook_LayoutPage);
        Logger::WriteLog(std::format("progress.json: all characters {}, mode bits {:#x}", g_UnlockAllCharacters ? "unlocked" : "as saved", g_ModeBits), MODULE_NAME, 0);
    }
}
