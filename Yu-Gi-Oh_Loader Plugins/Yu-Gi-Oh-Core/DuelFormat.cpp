#include "DuelFormat.h"

#include <Windows.h>
#include <format>
#include <string>

#include "Detours.h"
#include "Logger.h"
#include "Save.h"
#include "YuGiOh/YuGiOh-Addresses.h"

namespace
{
    constexpr uintptr_t kDuelFormat = 0x14349B3E4;         // Duel::PlayerState +0x37A4 (IDB: DuelFormat)
    constexpr int kSpeed = 1;
    constexpr int kRush = 2;

    using ClearState_t = void(__fastcall*)();
    using IsMultiplayer_t = bool(__fastcall*)();
    uintptr_t orig_DuelSetup_ClearState = YGO::Addr::DuelSetup_ClearState;

    int g_format = 0;            // offline duels ([Yu-Gi-Oh-Core] DuelFormat)
    volatile int g_online = 0;   // online duels (Core_SetOnlineDuelFormat, Yu-Gi-Oh-MP's lobby)

    void __fastcall Hook_DuelSetup_ClearState()
    {
        reinterpret_cast<ClearState_t>(orig_DuelSetup_ClearState)();
        if (reinterpret_cast<IsMultiplayer_t>(YGO::Addr::Duel_Get_IsDuelMultiplayer)())
        {
            // the starting LP is the lobby's LP setting, which both players' games already share
            *reinterpret_cast<volatile int*>(kDuelFormat) = g_online;
            return;
        }
        *reinterpret_cast<volatile int*>(kDuelFormat) = g_format;
        volatile int* startLp = reinterpret_cast<volatile int*>(YGO::Addr::g_iStartLifePoints);
        if (g_format == kSpeed && *startLp == 8000)
            *startLp = 4000;
    }

    int ParseFormat(std::string value)
    {
        for (char& c : value)
            c = static_cast<char>(tolower(static_cast<unsigned char>(c)));
        if (value == "speed" || value == "1")
            return kSpeed;
        if (value == "rush" || value == "2")
            return kRush;
        return 0;
    }
}

namespace DuelFormat
{
    void SetOnline(int format)
    {
        g_online = format == kSpeed || format == kRush ? format : 0;
        Logger::WriteLog(std::format("Online duel format: {}", g_online == kSpeed ? "Speed Duel" : g_online == kRush ? "Rush Duel" : "Master"), MODULE_NAME, 0);
    }

    void Install()
    {
        const std::string ini = Save::GameFolder() + "Config.ini";
        char value[32] = {};
        GetPrivateProfileStringA("Yu-Gi-Oh-Core", "DuelFormat", "master", value, sizeof(value), ini.c_str());
        g_format = ParseFormat(value);

        // Always hooked: Core_SetOnlineDuelFormat can turn the format on for online duels later.
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_DuelSetup_ClearState, Hook_DuelSetup_ClearState);
        const LONG error = DetourTransactionCommit();
        Logger::WriteLog(std::format("Duel format: {} (offline duels){}", g_format == kSpeed ? "Speed Duel" : g_format == kRush ? "Rush Duel" : "Master",
            error == NO_ERROR ? "" : std::format(" - hook failed ({})", error)), MODULE_NAME, error == NO_ERROR ? 0 : 2);
    }
}
