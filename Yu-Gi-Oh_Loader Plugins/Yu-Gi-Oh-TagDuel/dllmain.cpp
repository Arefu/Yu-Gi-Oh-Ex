#include <Windows.h>
#include <format>

#include "Logger.h"
#include "TagDuel.h"

#include "Yu-Gi-Oh-RIX.h"

// Yu-Gi-Oh-TagDuel: forces the game's own (otherwise unreachable on PC) tag-duel mode on for the next duel -
// see TagDuel.h/docs/MultiplayerSystem.md. Two ways in:
//   - a "Tag Duel" button on the Single Player page of the main menu (added below, via Yu-Gi-Oh-RIX) that enables
//     it and then presses the game's own Solo Duel button, so deck/opponent selection is entirely the game's own UI;
//   - the exported TagDuel_SetEnabled(bool), for another plugin (Yu-Gi-Oh-MP's Session::Create(Mode::Tag2v2)) to
//     call before it hands off to the engine's duel-launch flow.

extern "C" __declspec(dllexport) void __cdecl TagDuel_SetEnabled(bool on)
{
    Logger::WriteLog(std::format("[export] TagDuel_SetEnabled({})", on), MODULE_NAME, 69);
    TagDuel::SetEnabled(on);
}

extern "C" __declspec(dllexport) bool __cdecl TagDuel_IsEnabled()
{
    Logger::WriteLog("[export] TagDuel_IsEnabled()", MODULE_NAME, 69);
    return TagDuel::IsEnabled();
}

namespace
{
    void __cdecl OnTagDuelButtonPressed(int buttonId, void* user)
    {
        Logger::WriteLog("Tag Duel button pressed", MODULE_NAME, 0);
        TagDuel::SetEnabled(true);
        RIX::Functions().PressMainMenuItem(RIX_ITEM_SOLO_DUEL);
    }

    // Requires Yu-Gi-Oh-RIX (list it in this plugin's manifest "requires" so the loader starts RIX first). If RIX
    // isn't loaded for some reason, this just logs and skips the button - TagDuel_SetEnabled is still exported and
    // usable from elsewhere (e.g. Yu-Gi-Oh-MP) either way.
    void AddMenuButton()
    {
        if (!RIX::Load())
        {
            Logger::WriteLog("Yu-Gi-Oh-RIX not loaded - no Tag Duel menu button added (TagDuel_SetEnabled is still available to other plugins)", MODULE_NAME, 1);
            return;
        }
        RIX_ButtonDesc button = RIX::Describe(L"Tag Duel", L"Force the next duel into the game's own tag-duel mode (experimental).",
            RIX_PAGE_SINGLE_PLAYER, &OnTagDuelButtonPressed, nullptr, RIX_ITEM_SOLO_DUEL);
        const int id = RIX::AddMainMenuButton(&button);
        Logger::WriteLog(std::format("Tag Duel menu button added (id {})", id), MODULE_NAME, 0);
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        Logger::SetupLogger();
        Logger::WriteLog("Yu-Gi-Oh-TagDuel starting", MODULE_NAME, 0);
        TagDuel::Setup();
        AddMenuButton();
        break;
    }
    return TRUE;
}
