#include <Windows.h>
#include <algorithm>
#include <cstdint>
#include <format>
#include <string>
#include <vector>

#include "Logger.h"
#include "TagDuel.h"

#include "Yu-Gi-Oh-RIX.h"
#include "YuGiOh/YuGiOh-RIX.h"   // the game's widgets (Dfx::AddImage / AddText) for the review page

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
        opponentCharacter, opponentDeck), MODULE_NAME, 69);
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
    // Every duel mode off: a plain free duel. Also needed right before the review's Let's Duel! - the review page sits on the Battle Pack
    // screen, whose OnEnter turns Battle Pack mode on, and a duel started in it takes its decks from the Battle Pack slots (empty decks on
    // both sides: an instant draw).
    void ClearDuelModes()
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
    }

    void StartFreeDuelFlow()
    {
        ClearDuelModes();
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
            MODULE_NAME, 69);
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

    // ---- the review page: who plays which seat with which deck, then "Let's Duel!" (Back returns to Free Duel).

    struct Review { int YourDeck = -1; int YourCharacter = -1; int OpponentCharacter = -1; int OpponentDeck = -1; } g_Review;

    const auto CharacterName = reinterpret_cast<const wchar_t*(__fastcall*)(int)>(0x1407FEC70);          // YGO::GAME::Get_CharacterName
    // Deck_FromId (0x14081AB40) -> YGO::SAVE::DeckListItem: +0 name (wchar_t[33]), +0x120 RecordSlot (the deck's character).
    const auto DeckFromId = reinterpret_cast<const char*(__fastcall*)(unsigned int profile, unsigned int deck)>(0x14081AB40);
    constexpr unsigned int kCurrentProfile = 0xFFFFFFFD;

    std::wstring DeckName(int deck)
    {
        const char* item = deck >= 0 ? DeckFromId(kCurrentProfile, static_cast<unsigned int>(deck)) : nullptr;
        return item ? std::wstring(reinterpret_cast<const wchar_t*>(item)) : L"(no deck)";
    }

    // The character you picked for yourself, else your deck's (what Free Duel itself uses).
    int YourCharacter()
    {
        if (g_Review.YourCharacter > 0)
            return g_Review.YourCharacter;
        const char* item = g_Review.YourDeck >= 0 ? DeckFromId(kCurrentProfile, static_cast<unsigned int>(g_Review.YourDeck)) : nullptr;
        return item ? *reinterpret_cast<const int*>(item + 0x120) : -1;
    }

    // What Free Duel's start step (YGO::UI::FreeDuel_HandleInput state 3) does - with your picked character on side 0 - then
    // Screen_GotoDuel. Partners are set up by Hook_AssignSeatDecks from TagDuel_SetPartners.
    void StartReviewedDuel()
    {
        using SetSide_t = void(__fastcall*)(int side, unsigned int value);
        const auto setCharacter = reinterpret_cast<SetSide_t>(0x140769670);                            // Set_DuelSideCharacter
        const auto setDeck = reinterpret_cast<SetSide_t>(0x140769690);                                 // Set_DuelSideDeck
        ClearDuelModes();   // a plain free duel (and the match results reset, as Free Duel's own start does)
        setCharacter(0, static_cast<unsigned int>(YourCharacter()));
        setDeck(0, static_cast<unsigned int>(g_Review.YourDeck));
        reinterpret_cast<void(__fastcall*)(unsigned int)>(0x140769A20)(static_cast<unsigned int>(g_Review.YourDeck));   // remember it as last used
        if (g_Review.OpponentCharacter >= 0 && g_Review.OpponentCharacter < 240)                      // g_CharacterRecords +0x14 = arena
            reinterpret_cast<void(__fastcall*)(int)>(0x140769540)(*reinterpret_cast<const int*>(0x142913470 + 0x68 * g_Review.OpponentCharacter + 0x14));
        setCharacter(1, static_cast<unsigned int>(g_Review.OpponentCharacter));
        setDeck(1, static_cast<unsigned int>(g_Review.OpponentDeck));

        Logger::WriteLog("Tag Duel review: Let's Duel!", MODULE_NAME, 0);
        void* ui = reinterpret_cast<void*(__fastcall*)()>(0x1408121B0)();                              // RIX::App::GetUI
        void* scheduler = reinterpret_cast<void*(__fastcall*)(void*)>(0x140807F60)(ui);                // RIX::UI::GetScheduler
        reinterpret_cast<char(__fastcall*)(void*, void*, int)>(0x14083CA10)(scheduler, ui, 0);        // Screen_GotoDuel
    }

    // The review's widgets, lobby style: the two teams side by side (blue yours, red the opponent's), each seat a portrait
    // (pdui/chars "<character key>_neutral", as YGO::GAME::Character_GetPortraitName builds it) over its name and deck. Drawn on the
    // screen Yu-Gi-Oh-Campaign hands over (Free Duel, screen 21) - its last step - and taken off again (YuGiOh-RIX.h YGO::RIX::Dfx).
    namespace Dfx = YGO::RIX::Dfx;

    struct ReviewSeat
    {
        std::wstring Name, Role, Deck, Control;
        std::string Portrait;   // sprite in pdui/chars
        bool Blue = true;
        float X = 0;            // left edge of the seat's column
    };
    ReviewSeat g_Seats[4];
    std::vector<YGO::RIX::SharedNode> g_ReviewNodes;
    void* g_ReviewScreen = nullptr;

    // Layout (1920 x 1080): each team is a 600 px block of two 280 px columns 40 apart; the blocks sit either side of a 200 px "VS" gap
    // centred on x 960 (blue 260-860, red 1060-1660). Everything in a column shares its left edge and width.
    constexpr float kColumnWidth = 280.0f, kColumnGap = 40.0f, kCentreGap = 200.0f;
    constexpr float kBlockWidth = 2 * kColumnWidth + kColumnGap;
    constexpr float kBlueX = 960.0f - kCentreGap / 2 - kBlockWidth, kRedX = 960.0f + kCentreGap / 2;
    constexpr float kPortraitSize = 240.0f, kPortraitY = 260.0f, kPlateY = kPortraitY + kPortraitSize + 12.0f, kPlateHeight = 64.0f;
    constexpr int kZ = 20;   // above the Battle Pack screen's own panels (RIX page elements use 20 too)

    ReviewSeat MakeSeat(const wchar_t* role, TagDuel::Role which, int character, int deck, bool blue, float x)
    {
        ReviewSeat seat;
        const wchar_t* name = character >= 0 ? CharacterName(character) : nullptr;
        seat.Name = name ? name : L"?";
        seat.Role = role;
        // Human seats: "YOU" on your own seat, "PLAYER n" on the other local players' (numbered in seat order).
        if (!TagDuel::IsSeatHuman(which))
            seat.Control = L"AI";
        else if (which == TagDuel::RoleYou)
            seat.Control = L"YOU";
        else
        {
            int number = TagDuel::IsSeatHuman(TagDuel::RoleYou) ? 2 : 1;
            for (int role = TagDuel::RolePartner; role < which; ++role)
                number += TagDuel::IsSeatHuman(static_cast<TagDuel::Role>(role)) ? 1 : 0;
            seat.Control = L"PLAYER " + std::to_wstring(number);
        }
        seat.Deck = DeckName(deck);
        if (character >= 0 && character < 240)
            if (const char* key = *reinterpret_cast<const char* const*>(0x142913470 + 0x68 * character + 0x20))   // CharacterRecord.Key
                seat.Portrait = std::string(key) + "_neutral";
        seat.Blue = blue;
        seat.X = x;
        return seat;
    }

    void AddNode(YGO::RIX::SharedNode node)
    {
        if (node.Node)
            g_ReviewNodes.push_back(node);
    }

    // One line of text centred on a seat's column, never wrapped: Width 0 = no box, X is the centre. Long text gets a smaller size so it
    // fits the column - the game's PD font is wide, about 1.0 x the pixel size per character (measured on the review: "Yami Yugi" at 32).
    void AddFittedLine(const YGO::RIX::SharedNode* root, const std::wstring& text, float columnX, float y, float size, uint32_t colour)
    {
        constexpr float kCharWidth = 1.0f, kMinSize = 14.0f;
        const float fit = 0.95f * kColumnWidth / (kCharWidth * static_cast<float>((std::max)(text.size(), size_t{ 1 })));
        const float fitted = (std::max)(kMinSize, (std::min)(size, fit));
        AddNode(Dfx::AddText(root, text.c_str(), kZ + 1, columnX + kColumnWidth / 2, y + (size - fitted) / 2, 0.0f, fitted, Dfx::TextAlign::Centre,
            colour));
    }

    void __cdecl ShowReview(void* screen, void* user)
    {
        if (!g_ReviewNodes.empty())
            return;
        g_ReviewScreen = screen;
        const YGO::RIX::SharedNode* root = YGO::RIX::ScreenRoot(screen);
        constexpr uint32_t kBlue = 0xFF7FC4FF, kRed = 0xFFFF7F7F, kWhite = 0xFFFFFFFF, kGrey = 0xFFC8C8C8;

        AddNode(Dfx::AddText(root, L"VS", kZ, 960.0f - kCentreGap / 2, kPortraitY + kPortraitSize / 2 - 44, kCentreGap, 88, Dfx::TextAlign::Centre, kWhite));

        for (const ReviewSeat& seat : g_Seats)
        {
            AddNode(Dfx::AddText(root, seat.Control.c_str(), kZ + 1, seat.X, kPortraitY - 64, kColumnWidth, 32, Dfx::TextAlign::Centre,
                seat.Control != L"AI" ? 0xFFFFE066 : kGrey));   // gold for a human seat
            if (!seat.Portrait.empty())
            {
                const YGO::RIX::SharedNode face = Dfx::AddImage(root, "pdui/chars", seat.Portrait.c_str(), kZ, seat.X + (kColumnWidth - kPortraitSize) / 2,
                    kPortraitY, kPortraitSize, kPortraitSize);
                if (!face.Node)
                    Logger::WriteLog(std::format("Tag Duel review: portrait {} not found in pdui/chars", seat.Portrait), MODULE_NAME, 1);
                AddNode(face);
            }
            // The name plate the game uses for player names, blue or red by team (text only when the sheet lacks it).
            AddNode(Dfx::AddImage(root, "pdui/shared", seat.Blue ? "gamertag_blue" : "gamertag_red", kZ, seat.X, kPlateY, kColumnWidth, kPlateHeight));
            // Name, then the deck a fixed 56 px below it: each one line, shrunk to fit when long.
            AddFittedLine(root, seat.Name, seat.X, kPlateY + (kPlateHeight - 32) / 2, 32.0f, kWhite);
            AddFittedLine(root, seat.Deck, seat.X, kPlateY + (kPlateHeight - 32) / 2 + 56, 23.0f, kGrey);
        }
    }

    void RemoveReviewNodes(void* screen)
    {
        const YGO::RIX::SharedNode* root = YGO::RIX::ScreenRoot(g_ReviewScreen ? g_ReviewScreen : screen);
        for (auto& node : g_ReviewNodes)
            Dfx::RemoveImage(root, node);
        g_ReviewNodes.clear();
        g_ReviewScreen = nullptr;
    }

    void BuildSeats()
    {
        g_Seats[0] = MakeSeat(L"You", TagDuel::RoleYou, YourCharacter(), g_Review.YourDeck, true, kBlueX);
        g_Seats[1] = MakeSeat(L"Your Partner", TagDuel::RolePartner, TagDuel::PartnerCharacter(0), TagDuel::PartnerDeck(0), true,
            kBlueX + kColumnWidth + kColumnGap);
        g_Seats[2] = MakeSeat(L"Opponent", TagDuel::RoleOpponent, g_Review.OpponentCharacter, g_Review.OpponentDeck, false, kRedX);
        g_Seats[3] = MakeSeat(L"Opponent's Partner", TagDuel::RoleOpponentPartner, TagDuel::PartnerCharacter(1), TagDuel::PartnerDeck(1), false,
            kRedX + kColumnWidth + kColumnGap);
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

// The review, as the last step of a tag Free Duel (Yu-Gi-Oh-Campaign runs the step: it hides Free Duel's deck widgets, takes the input,
// and calls these). After TagDuel_SetPartners.
//   TagDuel_ShowReview: draws the four seats on `screen` (the Free Duel screen). yourCharacter: the portrait / name you picked (-1 = your
//                       deck's character, as Free Duel does).
//   TagDuel_HideReview: takes them off again (Back, or before the duel starts).
//   TagDuel_StartReviewedDuel: sets both sides up and starts the duel (Let's Duel!).
extern "C" __declspec(dllexport) void __cdecl TagDuel_ShowReview(void* screen, int yourDeck, int yourCharacter, int opponentCharacter, int opponentDeck)
{
    RemoveReviewNodes(screen);
    g_Review = { yourDeck, yourCharacter, opponentCharacter, opponentDeck };
    BuildSeats();
    ShowReview(screen, nullptr);
}

extern "C" __declspec(dllexport) void __cdecl TagDuel_HideReview()
{
    RemoveReviewNodes(nullptr);
}

extern "C" __declspec(dllexport) void __cdecl TagDuel_StartReviewedDuel()
{
    RemoveReviewNodes(nullptr);
    StartReviewedDuel();
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
