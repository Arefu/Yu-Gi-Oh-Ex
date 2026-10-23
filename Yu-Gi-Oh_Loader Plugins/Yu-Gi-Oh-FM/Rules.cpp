#include "Rules.h"

#include <Windows.h>
#include <cstdint>
#include <format>
#include <intrin.h>
#include <set>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Memory.h"

namespace
{
    using HandLevel_t = int64_t(__fastcall*)(int side, int instance);
    uintptr_t orig_HandLevel = 0x140011C30;        // sub_140011C30: a hand card's Level (side, card instance)

    constexpr int kTributeFreeLevel = 4;
    constexpr uintptr_t kInstances = 0x143499798;   // card instance table: 8 bytes each, id = low 14 bits of the first u16
                                                    // (the IDB's PlayerState names this area field_1B52: its names there are 6 bytes short)

    bool g_NoTributes = true;

    int64_t __fastcall Hook_HandLevel(int side, int instance)
    {
        const int64_t level = reinterpret_cast<HandLevel_t>(orig_HandLevel)(side, instance);
        if (!g_NoTributes || level <= kTributeFreeLevel)
            return level;
        // logged once per card id and caller, so the log shows every check that asked
        static std::set<std::pair<int, uintptr_t>> logged;
        const int id = *reinterpret_cast<const uint16_t*>(kInstances + 8 * instance) & 0x3FFF;
        const uintptr_t caller = reinterpret_cast<uintptr_t>(_ReturnAddress());
        if (logged.insert({ id, caller }).second)
            Logger::WriteLog(std::format("NoTributes: card {} (side {}, instance {}) is Level {} in the hand, answered {} (caller {:#x})", id, side,
                instance, level, kTributeFreeLevel, caller), MODULE_NAME, 0);
        return kTributeFreeLevel;
    }
}

namespace
{
    // FusionsInMainDeck: FM has no Extra Deck; Fusion monsters are deck / hand cards played like any other. The game's kind table
    // (YGO::CARDS::KIND_TABLE 0x140BF7820, 46 rows of 6 u16: Frame, BaseFrame, Category, DeckSection, Tuner, Pendulum) gives the Fusion
    // kinds (BaseFrame 3) DeckSection 2 = Extra Deck, and HandCard_GetCommandFlags refuses a Normal Summon / Set for any DeckSection != 0
    // (reason 13). Those rows get DeckSection 0: Fusion monsters can be in the main deck and Summoned / Set from the hand.
    constexpr uintptr_t kKindTable = 0x140BF7820;
    constexpr int kKindRows = 46, kFusionBaseFrame = 3, kExtraDeck = 2;

    void FusionsInMainDeck()
    {
        int changed = 0;
        for (int row = 0; row < kKindRows; ++row)
        {
            const uintptr_t at = kKindTable + row * 12;
            const uint16_t* r = reinterpret_cast<const uint16_t*>(at);
            if (r[1] != kFusionBaseFrame || r[3] != kExtraDeck)
                continue;
            const uint16_t mainDeck = 0;
            YGO::Memory::WriteBytes(at + 6, &mainDeck, sizeof(mainDeck));
            Logger::WriteLog(std::format("FusionsInMainDeck: kind {} (frame {}, category {}) is now a main deck kind", row, r[0], r[2]), MODULE_NAME, 0);
            ++changed;
        }
        Logger::WriteLog(std::format("FusionsInMainDeck: {} Fusion kind(s) moved to the main deck", changed), MODULE_NAME, changed ? 0 : 2);
    }
}

namespace Rules
{
    void Attach()
    {
        if (GetPrivateProfileIntW(L"Yu-Gi-Oh-FM", L"FusionsInMainDeck", 1, L".\\Config.ini") != 0)
            FusionsInMainDeck();
        g_NoTributes = GetPrivateProfileIntW(L"Yu-Gi-Oh-FM", L"NoTributes", 1, L".\\Config.ini") != 0;
        Logger::WriteLog(std::format("Rules: NoTributes {}", g_NoTributes ? "on" : "off"), MODULE_NAME, 0);
        if (g_NoTributes)
            DetourAttach(&(PVOID&)orig_HandLevel, Hook_HandLevel);
    }
}
