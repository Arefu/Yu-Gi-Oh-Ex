#include <Windows.h>

#include "Logger.h"
#include "MainMenu.h"
#include "PluginMenu.h"
#include "VideoScreen.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        Logger::SetupLogger();
        Menu::Install();
        Menu::LoadMenuFiles();
        PluginMenu::Install();
        VideoScreen::Install();
        break;
    }
    return TRUE;
}
