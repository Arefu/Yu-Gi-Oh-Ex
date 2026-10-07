#include <Windows.h>
#include <algorithm>
#include <cstdint>
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
    return TagDuel::IsEnabled();
}

// Who plays each of the 4 seats (0 human, 1 AI, 2 network); nullptr = back to the engine's own choice.
extern "C" __declspec(dllexport) void __cdecl TagDuel_SetSeatControllers(const int* controllers)
{
    if (!controllers)
    {
        TagDuel::ClearSeatControllers();
        return;
    }
    const int seats[4] = { controllers[0], controllers[1], controllers[2], controllers[3] };
    TagDuel::SetSeatControllers(seats);
}

// For Yu-Gi-Oh-MP: true = it handles tag duels in online matches (and sets the seat controllers); until then TagDuel stays out of them.
extern "C" __declspec(dllexport) void __cdecl TagDuel_AllowInMultiplayer(bool on)
{
    TagDuel::AllowInMultiplayer(on);
}

// Local seating when no plugin set seat controllers: role 0 you, 1 your partner, 2 the opponent, 3 the opponent's partner; human = played
// on this machine, else AI. Lasts until changed.
extern "C" __declspec(dllexport) void __cdecl TagDuel_SetSeatHuman(int role, bool human)
{
    TagDuel::SetSeatHuman(static_cast<TagDuel::Role>(role), human);
}

// Free Duel's partner picks (Yu-Gi-Oh-Campaign): characters and game deck ids; arms the next duel as a tag duel.
extern "C" __declspec(dllexport) void __cdecl TagDuel_SetPartners(int yourCharacter, int yourDeck, int opponentCharacter, int opponentDeck)
{
    Logger::WriteLog(std::format("[export] TagDuel_SetPartners(you: character {} deck {}, opponent: character {} deck {})", yourCharacter, yourDeck,
        opponentCharacter, opponentDeck), MODULE_NAME, 0);
    TagDuel::SetPartners(yourCharacter, yourDeck, opponentCharacter, opponentDeck);
}

namespace
{
    // The Free Duel picker (series -> opponent -> your deck, YGO::UI::FreeDuel_*) is screen 21, RIX::ScreenHardChallenge. The main menu
    // opens it as Duelist Challenge (challenge mode on, behind an unlock bit); with every mode flag off it is a plain free duel against any
    // character with their normal deck - what a tag duel wants. Done by hand because the arrangement page sits on the Battle Pack screen,
    // where RIX_PressMainMenuItem doesn't work.
    using SetFlag_t = void(__fastcall*)(bool);
    using Void_t = void(__fastcall*)();
    void StartFreeDuelFlow()
    {
        reinterpret_cast<SetFlag_t>(0x140769590)(false);   // YGO__DUEL__Set_IsBattlePackMode
        reinterpret_cast<SetFlag_t>(0x140769630)(false);   // YGO__DUEL__Set_IsCampaignMode
        reinterpret_cast<SetFlag_t>(0x1407696A0)(false);   // YGO__DUEL__Set_IsChallengeMode
        reinterpret_cast<SetFlag_t>(0x140769720)(false);   // YGO::DUEL::Set_IsDuelMultiplayer
        reinterpret_cast<SetFlag_t>(0x1407696C0)(false);   // YGO__DUEL__Set_IsLiveSession
        reinterpret_cast<SetFlag_t>(0x140769B00)(false);   // YGO::DUEL::Set_IsTutorialDuel
        reinterpret_cast<SetFlag_t>(0x140769AD0)(false);   // Duel_Set_UseSeatControllerTable
        reinterpret_cast<Void_t>(0x140769500)();           // YGO__DUEL__ResetMatchResults
        reinterpret_cast<Void_t>(0x1407696E0)();           // YGO::DuelSetup::Setup_RuleAndLP
        reinterpret_cast<Void_t>(0x14083CBB0)();           // YGO__Stats__ResetForNewSession
        RIX::Functions().GotoScreen(RIX_SCREEN_DUELIST_CHALLENGE);
    }

    // ---- the Local seat screen: four AI / Human toggles (pressing one flips it in place), then Continue.

    struct SeatText { const wchar_t* Human; const wchar_t* AI; };
    const SeatText kSeatText[4] = {
        { L"You: Human", L"You: AI" },
        { L"Your Partner: Human", L"Your Partner: AI" },
        { L"Opponent: Human", L"Opponent: AI" },
        { L"Opponent's Partner: Human", L"Opponent's Partner: AI" },
    };
    constexpr const wchar_t* kSeatHelp = L"Press to switch between AI and Human. Every human seat is played on this machine, taking turns.";

    void __cdecl OnSeatPressed(int buttonId, void* user);

    RIX_PageButton SeatButton(TagDuel::Role role)
    {
        const bool human = TagDuel::IsSeatHuman(role);
        return { human ? kSeatText[role].Human : kSeatText[role].AI, kSeatHelp, &OnSeatPressed, reinterpret_cast<void*>(static_cast<intptr_t>(role)) };
    }

    // user = the seat's TagDuel::Role; the button index is the role too.
    void __cdecl OnSeatPressed(int buttonId, void* user)
    {
        const auto role = static_cast<TagDuel::Role>(reinterpret_cast<intptr_t>(user));
        TagDuel::SetSeatHuman(role, !TagDuel::IsSeatHuman(role));
        const RIX_PageButton button = SeatButton(role);
        RIX::Functions().UpdatePageButton(static_cast<int>(role), &button);
    }

    void __cdecl OnContinuePressed(int buttonId, void* user)
    {
        Logger::WriteLog(std::format("Local tag duel: you {}, partner {}, opponent {}, opponent's partner {}",
            TagDuel::IsSeatHuman(TagDuel::RoleYou) ? "human" : "AI", TagDuel::IsSeatHuman(TagDuel::RolePartner) ? "human" : "AI",
            TagDuel::IsSeatHuman(TagDuel::RoleOpponent) ? "human" : "AI", TagDuel::IsSeatHuman(TagDuel::RoleOpponentPartner) ? "human" : "AI"),
            MODULE_NAME, 0);
        TagDuel::SetEnabled(true);
        StartFreeDuelFlow();
    }

    void __cdecl OnLocalPressed(int buttonId, void* user)
    {
        RIX_PageDesc page{};
        page.Size = sizeof(page);
        page.Header = L"Local Tag Duel";
        page.ButtonCount = 5;
        for (int role = TagDuel::RoleYou; role <= TagDuel::RoleOpponentPartner; ++role)
            page.Buttons[role] = SeatButton(static_cast<TagDuel::Role>(role));
        page.Buttons[4] = { L"Continue", L"Pick the characters and decks for each seat.", &OnContinuePressed, nullptr };
        RIX::Functions().OpenPage(&page);   // from a page button: opens on top, Back returns here
    }

    // Co-op isn't ready: the game's "unable" sound (YGO::UI::PlayUISound 0x14086C280, slot 71) and nothing else.
    void __cdecl OnComingSoonPressed(int buttonId, void* user)
    {
        reinterpret_cast<void(__fastcall*)(int)>(0x14086C280)(71);
    }

    // The main menu's Tag Duel button: Local (the seat screen, then Free Duel) or Co-Op (online, not ready).
    void __cdecl OnTagDuelButtonPressed(int buttonId, void* user)
    {
        RIX_PageDesc page{};
        page.Size = sizeof(page);
        page.Header = L"Tag Duel";
        page.ButtonCount = 2;
        page.Buttons[0] = { L"Local", L"Two-on-two on this machine: choose who is AI and who is human.", &OnLocalPressed, nullptr };
        page.Buttons[1] = { L"Co-Op (Coming Soon)", L"Coming soon: tag duels with other players.", &OnComingSoonPressed, nullptr };
        const int opened = RIX::Functions().OpenPage(&page);
        Logger::WriteLog(std::format("Tag Duel page {}", opened ? "opened" : "FAILED to open"), MODULE_NAME, opened ? 0 : 2);
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
        RIX_ButtonDesc button = RIX::Describe(L"Tag Duel", L"Two-on-two duels: pick who plays each seat (experimental).",
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
        if (TagDuel::MultiplayerPluginLoaded())
            Logger::WriteLog("Yu-Gi-Oh-MP is loaded: online duels stay 1v1 until it takes tag duels on (TagDuel_AllowInMultiplayer)", MODULE_NAME, 0);
        break;
    }
    return TRUE;
}
