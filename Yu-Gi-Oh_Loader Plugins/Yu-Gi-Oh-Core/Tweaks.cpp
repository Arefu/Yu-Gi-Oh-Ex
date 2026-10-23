#include "Tweaks.h"
#include "HookDiagnostics.h"

#include <Windows.h>
#include <format>

#include "Detours.h"
#include "Logger.h"
#include "Save.h"
#include "YuGiOh/YuGiOh-Addresses.h"

namespace
{
    void* orig_PauseWhenUnfocused = reinterpret_cast<void*>(0x14083C9F0);
    void* orig_UseJPLogo = reinterpret_cast<void*>(0x1408734C0);

    void __fastcall Hook_PauseWhenUnfocused()
    {
    }

    void __fastcall Hook_UseJPLogo(__int64)
    {
        *reinterpret_cast<volatile unsigned char*>(YGO::Addr::g_bIsJpVersion) = 255;
    }
}

namespace Tweaks
{
    void Install()
    {
        const std::string ini = Save::GameFolder() + "Config.ini";
        const bool pause = GetPrivateProfileIntA("Yu-Gi-Oh-Core", "PauseInBackground", 0, ini.c_str()) != 0;
        const bool japanese = GetPrivateProfileIntA("Yu-Gi-Oh-Core", "JapaneseVersion", 0, ini.c_str()) != 0;
        if (!HookDiagnostics::Begin("Core tweaks", MODULE_NAME, Logger::WriteLog))
            return;
        if (!pause)
            DetourAttach(&orig_PauseWhenUnfocused, Hook_PauseWhenUnfocused);
        if (japanese)
            DetourAttach(&orig_UseJPLogo, Hook_UseJPLogo);
        if (HookDiagnostics::Commit("Core tweaks", MODULE_NAME, Logger::WriteLog) != NO_ERROR)
            return;
        Logger::WriteLog(std::format("Pause in background: {}, Japanese version: {}", pause ? "on" : "off", japanese ? "on" : "off"), MODULE_NAME, 0);
    }
}
