#include <Windows.h>
#include <format>

#include "AiDeck.h"
#include "Characters.h"
#include "Decks.h"
#include "Detours.h"
#include "StoryDuels.h"
#include "StoryScripts.h"
#include "Tutorials.h"
#include "Logger.h"

// Yu-Gi-Oh-Campaign: the game's story and duelist content from Yu-Gi-Oh-Ex JSON written by WolfEx:
//   characters.json   -> the character table (Characters.cpp)
//   decks.json        -> the deck table and the decks' cards (Decks.cpp)
//   storyduels.json   -> the campaign's duel table (StoryDuels.cpp)
//   storyscripts.json -> the story scenes' dialog (StoryScripts.cpp)
//   tutorials\*.json   -> new tutorials (27-99) in Help > Tutorial (Tutorials.cpp)
// Free Duel uses the same character and deck data.
// AI deck: the opponent plays a deck you pick on the deck picker, keeping their portrait (AiDeck.cpp).

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    {
        Logger::SetupLogger();
        Logger::WriteLog("Yu-Gi-Oh-Campaign starting", MODULE_NAME, 0);

        DetourRestoreAfterWith();
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        Characters::Attach();
        Decks::Attach();
        StoryDuels::Attach();
        StoryScripts::Attach();
        Tutorials::Attach();
        AiDeck::Attach();
        LONG error = DetourTransactionCommit();
        Logger::WriteLog(std::format("Campaign hooks attached: {}", error), MODULE_NAME, error == 0 ? 0 : 2);

        Characters::ApplyIfLoaded();
        Decks::ApplyIfLoaded();
        StoryDuels::ApplyIfLoaded();
        StoryScripts::ApplyIfLoaded();
        break;
    }
    }
    return TRUE;
}
