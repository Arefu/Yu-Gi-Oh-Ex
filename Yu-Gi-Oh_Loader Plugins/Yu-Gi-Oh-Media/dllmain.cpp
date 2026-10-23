#include <Windows.h>

#include "Logger.h"
#include "Music.h"
#include "Video.h"

// Yu-Gi-Oh-Media: music the plugin plays itself (miniaudio) in place of the game's Wwise music - see Music.h and docs/MediaPlugin.md.
// Step 1 is per-slot overrides from Yu-Gi-Oh-Ex\music.json; per-arena music, duel-event music and VOX build on it.

extern "C" __declspec(dllexport) void __cdecl Music_Reload()
{
    Logger::Log("[export] Music_Reload()", MODULE_NAME, 69);
    Music::Reload();
}

// Plays movies/<name>.mp4 over the whole game (anywhere: menus, duels, story). Esc or a click skips it.
extern "C" __declspec(dllexport) void __cdecl Video_Play(const char* name)
{
    if (name && *name)
        Video::PlayFullScreen(name);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        Logger::SetupLogger();
        Logger::WriteLog("Yu-Gi-Oh-Media starting", MODULE_NAME, 0);
        Music::Setup();
        break;
    }
    return TRUE;
}
