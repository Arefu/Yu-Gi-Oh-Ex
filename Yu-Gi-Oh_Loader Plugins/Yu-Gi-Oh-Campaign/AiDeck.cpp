#include "AiDeck.h"
#include "Common.h"

#include <Windows.h>
#include <cstdint>
#include <format>
#include <map>

#include "Detours.h"
#include "Logger.h"
#include "YuGiOh/YuGiOh-RIX.h"

namespace
{
    namespace R = YGO::RIX;
    namespace List = YGO::RIX::DeckSelectList;

    // ---- the game (names in the IDB)

    constexpr int kConfirm = 0x1000, kCancel = 0x2000;
    constexpr int kSoundDecide = 39, kSoundCancel = 18, kSoundUnable = 71;
    constexpr unsigned int kCurrentProfile = 0xFFFFFFFD;
    constexpr int kSaveDecks = 32, kDeckRecords = 700, kFirstRecordDeck = 32;   // deck id = 32 + deckdata index

    // Free Duel (YGO::UI::FreeDuel_*): state +4064 (1 opponents, 2 your deck, 3 start), the picker +1456, your deck panel +2864, the
    // opponent's +3448, the opponent list +904.
    constexpr size_t kFreeState = 4064, kFreeList = 1456, kFreePlayerPanel = 2864, kFreeOpponentPanel = 3448, kFreeOpponents = 904;
    constexpr int kFreeOpponentList = 1, kFreeChoosingDeck = 2, kFreeStart = 3;
    // The campaign's deck screen (CampaignDeck_*): state +2656 (0 the Story deck / your deck menu, 1 your deck, 2 start), the picker +664,
    // the deck panel +2072. Its menu items are g_CampaignDeckMenuItems (id, label, description; -1 ends).
    constexpr size_t kCampaignState = 2656, kCampaignList = 664, kCampaignPanel = 2072;
    constexpr int kCampaignMenu = 0, kCampaignChoosingDeck = 1;
    constexpr uintptr_t kCampaignMenuItems = 0x140A72A40;
    constexpr size_t kHelpBar = 264;
    constexpr size_t kHeader = 152, kHeaderText = 96, kTextId = 232;   // screen+152 widget_Header, its text widget, the text's id/pointer

    struct CharacterRecord { uint32_t Id; int32_t Series; int32_t Deck; uint8_t Rest[0x68 - 12]; };   // g_CharacterRecords entry (0x68)
    static_assert(sizeof(CharacterRecord) == 0x68, "CharacterRecord layout drifted");
    constexpr uintptr_t kCharacterRecords = 0x142913470;
    constexpr int kCharacterCount = 240;

    struct DeckCards { wchar_t Name[33]; int16_t MainCount; };   // the start of YGO::SAVE::DeckListItem

    using FreeInput_t = void(__fastcall*)(void* screen, void* ui);
    using CampaignInput_t = void(__fastcall*)(void* screen);
    using FreeSetState_t = void(__fastcall*)(void* screen, int state);
    using GotoDuel_t = char(__fastcall*)(void* scheduler, void* ui, int flags);
    using SetupSeat_t = void(__fastcall*)(int seat, int character, const wchar_t* name, void* deck, int profile, void* src, void* extra);

    FreeInput_t orig_FreeDuelInput = reinterpret_cast<FreeInput_t>(0x140840B10);              // YGO::UI::FreeDuel_HandleInput
    FreeSetState_t orig_FreeDuelSetState = reinterpret_cast<FreeSetState_t>(0x140841700);     // YGO::UI::FreeDuel_SetState
    CampaignInput_t orig_CampaignInput = reinterpret_cast<CampaignInput_t>(0x140831490);      // CampaignDeck_HandleInput
    GotoDuel_t orig_GotoDuel = reinterpret_cast<GotoDuel_t>(0x14083CA10);                     // Screen_GotoDuel: fade to screen 9
    SetupSeat_t orig_SetupSeat = reinterpret_cast<SetupSeat_t>(0x14076A7B0);                  // Duel_SetupSeat

    const auto FreeDuelHelp = reinterpret_cast<void(__fastcall*)(void* screen)>(0x140841A40);        // FreeDuel_UpdateHelpBar
    const auto CampaignHelp = reinterpret_cast<void(__fastcall*)(void* screen)>(0x140831C90);        // CampaignDeck_UpdateHelpBar
    const auto CampaignSetState = reinterpret_cast<void(__fastcall*)(void* screen, int state)>(0x140831B50);
    const auto OpponentListSelected = reinterpret_cast<unsigned int(__fastcall*)(void* list)>(0x1408A52A0);
    const auto StoryDuelSideCharacter = reinterpret_cast<int(__fastcall*)(int side)>(0x1407FF430);   // CurrentStoryDuel_GetSideCharacter
    const auto StoryDuelSideDeck = reinterpret_cast<int(__fastcall*)(int side)>(0x1407FF650);        // CurrentStoryDuel_GetSideDeck (deckdata index)
    const auto DeckFromId = reinterpret_cast<DeckCards*(__fastcall*)(unsigned int profile, unsigned int deckId)>(0x14081AB40);
    const auto DeckIsDefined = reinterpret_cast<bool(__fastcall*)(int index)>(0x1407FE4D0);
    const auto SetDescription = reinterpret_cast<void(__fastcall*)(void* help, int64_t text)>(0x14089F6E0);   // widget_Description::SetTextById
    const auto GetUI = reinterpret_cast<void*(__fastcall*)()>(0x1408121B0);                         // RIX::App::GetUI
    const auto UIScheduler = reinterpret_cast<void*(__fastcall*)(void* ui)>(0x140807F60);

    template <typename T> T CallGame(uintptr_t address) { return reinterpret_cast<T(__fastcall*)()>(address)(); }
    bool IsMultiplayer() { return CallGame<uint8_t>(0x1407691D0) != 0; }
    bool IsBattlePack() { return CallGame<uint8_t>(0x140769190) != 0; }
    bool IsChallenge() { return CallGame<uint8_t>(0x1407691B0) != 0; }
    bool IsTutorial() { return CallGame<uint8_t>(0x1407694F0) != 0; }

    const wchar_t* const kTitlePlayer = L"Your Deck";
    const wchar_t* const kTitleOpponent = L"Opponent's Deck";
    const R::HelpEntry kHelpDuel{ kConfirm, 0, reinterpret_cast<int64_t>(L"Duel") };
    const R::HelpEntry kHelpBack{ kCancel, 1, reinterpret_cast<int64_t>(L"Back") };

    // ---- what was chosen

    std::map<int, unsigned int> g_Remembered;   // character -> the deck picked last time (the picker starts on it)
    int g_PendingCharacter = -1;                // the next duel's opponent plays g_PendingDeck (set when that duel is started)
    unsigned int g_PendingDeck = 0;

    // ---- the opponent's deck step

    struct Picker
    {
        bool Active = false;
        bool Campaign = false;
        char* Screen = nullptr;
        void* List = nullptr;
        void* Panel = nullptr;          // where the highlighted deck is shown
        int Character = -1;
        int StoryDeck = -1;             // the opponent's own deck (listed first)
        int PlayerDeck = -1;            // your deck, picked on the step before
    } g_Picker;

    int64_t g_SavedTitle = 0;           // the screen's own title, put back when its deck steps are left
    char* g_TitledScreen = nullptr;

    void* HeaderText(char* screen) { return *reinterpret_cast<void**>(screen + kHeader + kHeaderText); }

    void SetTitle(char* screen, const wchar_t* title)
    {
        void* text = HeaderText(screen);
        if (!text)
            return;
        if (g_TitledScreen != screen)
        {
            g_SavedTitle = *reinterpret_cast<int64_t*>(static_cast<char*>(text) + kTextId);
            g_TitledScreen = screen;
        }
        R::SetHeaderText(screen, reinterpret_cast<int64_t>(title));
    }

    void RestoreTitle(char* screen)
    {
        if (g_TitledScreen != screen)
            return;
        R::SetHeaderText(screen, g_SavedTitle);
        g_TitledScreen = nullptr;
    }

    int CharacterDeck(int character)
    {
        if (character < 0 || character >= kCharacterCount)
            return -1;
        const int deck = reinterpret_cast<const CharacterRecord*>(kCharacterRecords)[character].Deck;
        return deck >= 0 ? deck + kFirstRecordDeck : -1;
    }

    bool HasCards(unsigned int deckId)
    {
        const DeckCards* deck = DeckFromId(kCurrentProfile, deckId);
        return deck && deck->MainCount > 0;
    }

    // Every deck the game has: the opponent's own first, your save decks, then every deckdata deck with cards.
    void FillAllDecks(void* list, int storyDeck)
    {
        R::Vector* ids = List::Ids(list);
        ids->End = ids->Begin;
        if (storyDeck >= 0)
            List::Push(ids, static_cast<unsigned int>(storyDeck));
        for (unsigned int id = 0; id < kSaveDecks; ++id)
            if (HasCards(id))
                List::Push(ids, id);
        for (int index = 0; index < kDeckRecords; ++index)
        {
            const unsigned int id = kFirstRecordDeck + index;
            if (static_cast<int>(id) != storyDeck && DeckIsDefined(index) && HasCards(id))
                List::Push(ids, id);
        }
        List::RebuildTabs(list);
        List::Refresh(list, false);
    }

    void ShowPickerHelp()
    {
        void* help = g_Picker.Screen + kHelpBar;
        R::HelpClear(help);
        R::HelpAdd(help, &kHelpDuel, 1);
        R::HelpAdd(help, &kHelpBack, 1);
        SetDescription(help, reinterpret_cast<int64_t>(L"Choose the deck your opponent plays. They keep their portrait and name."));
        R::HelpLayout(help);
    }

    // Your deck is chosen: the same picker becomes the opponent's.
    void Enter(char* screen, bool campaign, int playerDeck)
    {
        Picker& p = g_Picker;
        p.Campaign = campaign;
        p.Screen = screen;
        p.PlayerDeck = playerDeck;
        if (campaign)
        {
            p.List = screen + kCampaignList;
            p.Panel = screen + kCampaignPanel;
            p.Character = StoryDuelSideCharacter(1);
            const int deck = StoryDuelSideDeck(1);
            p.StoryDeck = deck >= 0 ? deck + kFirstRecordDeck : -1;
        }
        else
        {
            p.List = screen + kFreeList;
            p.Panel = screen + kFreeOpponentPanel;
            p.Character = static_cast<int>(OpponentListSelected(screen + kFreeOpponents));
            p.StoryDeck = CharacterDeck(p.Character);
        }
        p.Active = true;

        FillAllDecks(p.List, p.StoryDeck);
        auto remembered = g_Remembered.find(p.Character);
        List::SelectDeckId(p.List, remembered != g_Remembered.end() ? static_cast<int>(remembered->second) : p.StoryDeck);
        R::DeckInfoPanel::ShowDeck(p.Panel, List::GetSelectedDeckIdPtr(p.List));
        SetTitle(screen, kTitleOpponent);
        ShowPickerHelp();
    }

    // Back to your deck (Back on the opponent's step).
    void Leave()
    {
        Picker& p = g_Picker;
        p.Active = false;
        List::FillPlayerDecks(p.List);
        List::Refresh(p.List, false);
        if (p.PlayerDeck >= 0)
            List::SelectDeckId(p.List, p.PlayerDeck);
        SetTitle(p.Screen, kTitlePlayer);
        if (p.Campaign)
        {
            R::DeckInfoPanel::ShowDeck(p.Panel, List::GetSelectedDeckIdPtr(p.List));
            CampaignHelp(p.Screen);
        }
        else
        {
            R::DeckInfoPanel::ShowDeck(p.Screen + kFreePlayerPanel, List::GetSelectedDeckIdPtr(p.List));
            const unsigned int own = static_cast<unsigned int>(p.StoryDeck);
            R::DeckInfoPanel::ShowDeck(p.Panel, p.StoryDeck >= 0 ? &own : nullptr);
            FreeDuelHelp(p.Screen);
        }
    }

    // The opponent's deck is chosen: the duel starts the way the game starts it from your deck.
    void StartDuel(unsigned int deck)
    {
        Picker& p = g_Picker;
        p.Active = false;
        g_Remembered[p.Character] = deck;
        g_PendingCharacter = p.Character;
        g_PendingDeck = deck;
        Logger::WriteLog(std::format("Opponent {} will play deck {}{}", p.Character, deck, static_cast<int>(deck) == p.StoryDeck ? " (their own)" : ""),
            MODULE_NAME, 0);
        RestoreTitle(p.Screen);
        if (p.Campaign)
        {
            *reinterpret_cast<int*>(p.Screen + kCampaignState) = 2;
            void* ui = GetUI();
            orig_GotoDuel(UIScheduler(ui), ui, 0);
        }
        else
            *reinterpret_cast<int*>(p.Screen + kFreeState) = kFreeStart;   // FreeDuel_HandleInput starts it next frame
    }

    void PickerFrame()
    {
        Picker& p = g_Picker;
        int pressed = R::Input::GetPressed(R::InputState) | R::Input::GetRepeat(R::InputState);
        pressed |= R::Input::HelpBarPressed(p.Screen + kHelpBar);
        if (R::InputCancelPressed(R::InputState, kCancel))
            pressed |= kCancel;

        List::Tick(p.List);
        int clicked = -1;
        const int mask = pressed | List::TabButtons(R::InputState);
        if (List::HandleInput(p.List, mask, &clicked))
            R::DeckInfoPanel::ShowDeck(p.Panel, List::GetSelectedDeckIdPtr(p.List));

        if (clicked >= 0 || (mask & kConfirm))
        {
            const int deck = List::GetSelectedDeckId(p.List);
            if (deck < 0)
            {
                R::PlayUISound(kSoundUnable);
                return;
            }
            R::PlayUISound(kSoundDecide);
            StartDuel(static_cast<unsigned int>(deck));
        }
        else if (mask & kCancel)
        {
            R::PlayUISound(kSoundCancel);
            Leave();
        }
    }

    // ---- hooks

    bool g_HoldDuel = false;     // set around the game's "your deck" confirm: the duel waits for the opponent's deck step
    bool g_DuelHeld = false;

    char __fastcall Hook_GotoDuel(void* scheduler, void* ui, int flags)
    {
        if (g_HoldDuel)
        {
            g_DuelHeld = true;
            return 0;
        }
        return orig_GotoDuel(scheduler, ui, flags);
    }

    void __fastcall Hook_FreeDuelInput(void* screen, void* ui)
    {
        char* s = static_cast<char*>(screen);
        int& state = *reinterpret_cast<int*>(s + kFreeState);
        if (g_Picker.Active && g_Picker.Screen == s)
        {
            if (state == kFreeChoosingDeck)
                return PickerFrame();
            g_Picker.Active = false;
        }

        const int before = state;
        orig_FreeDuelInput(screen, ui);
        if (before == kFreeChoosingDeck && state == kFreeStart && !IsMultiplayer())
        {
            // Your deck is confirmed (Set_DuelSideDeck runs on the next frame, in state 3): ask for the opponent's first.
            state = kFreeChoosingDeck;
            Enter(s, false, List::GetSelectedDeckId(s + kFreeList));
        }
        else if (state == kFreeChoosingDeck && !g_Picker.Active)
            SetTitle(s, kTitlePlayer);
        else if (state != kFreeChoosingDeck && state != kFreeStart)
            RestoreTitle(s);
    }

    void __fastcall Hook_FreeDuelSetState(void* screen, int state)
    {
        g_Picker.Active = false;
        if (state <= kFreeOpponentList)
            g_PendingCharacter = -1;   // a new choice is on its way
        orig_FreeDuelSetState(screen, state);
    }

    void __fastcall Hook_CampaignInput(void* screen)
    {
        char* s = static_cast<char*>(screen);
        int& state = *reinterpret_cast<int*>(s + kCampaignState);
        if (g_Picker.Active && g_Picker.Screen == s)
        {
            if (state == kCampaignChoosingDeck)
                return PickerFrame();
            g_Picker.Active = false;
        }

        if (state == kCampaignMenu)
        {
            g_PendingCharacter = -1;   // Story Deck: the story's decks for both sides
            RestoreTitle(s);
            return orig_CampaignInput(screen);
        }
        if (state != kCampaignChoosingDeck)
            return orig_CampaignInput(screen);

        SetTitle(s, kTitlePlayer);
        g_HoldDuel = true;
        g_DuelHeld = false;
        orig_CampaignInput(screen);
        g_HoldDuel = false;
        if (g_DuelHeld)
        {
            // Your deck is confirmed (Duel_SetCampaignPlayerDeck has it): ask for the opponent's before the duel starts.
            state = kCampaignChoosingDeck;
            Enter(s, true, List::GetSelectedDeckId(s + kCampaignList));
        }
    }

    // Seat 1 is the opponent in Free Duel and the campaign. Its character (portrait, name) stays; only the cards change.
    void __fastcall Hook_SetupSeat(int seat, int character, const wchar_t* name, void* deck, int profile, void* src, void* extra)
    {
        if (seat == 1 && character == g_PendingCharacter && !IsMultiplayer() && !IsBattlePack() && !IsChallenge() && !IsTutorial())
        {
            if (DeckCards* chosen = DeckFromId(kCurrentProfile, g_PendingDeck); chosen && chosen->MainCount > 0)
            {
                deck = chosen;
                Logger::WriteLog(std::format("Opponent {} plays deck {}", character, g_PendingDeck), MODULE_NAME, 0);
            }
        }
        orig_SetupSeat(seat, character, name, deck, profile, src, extra);
    }

    // The campaign's deck menu: "Story Deck" (the story's decks for both sides) and "Custom Decks" (yours, then the opponent's).
    void RelabelCampaignMenu()
    {
        static const wchar_t* const storyText = L"You and your opponent use the story's decks.";
        static const wchar_t* const customLabel = L"Custom Decks";
        static const wchar_t* const customText = L"Choose your deck, then the deck your opponent plays.";
        auto* items = reinterpret_cast<int64_t*>(kCampaignMenuItems);   // {id, label, description} x2, then id -1
        DWORD old = 0;
        if (!VirtualProtect(items, 6 * sizeof(int64_t), PAGE_READWRITE, &old))
            return;
        items[2] = reinterpret_cast<int64_t>(storyText);
        items[4] = reinterpret_cast<int64_t>(customLabel);
        items[5] = reinterpret_cast<int64_t>(customText);
        VirtualProtect(items, 6 * sizeof(int64_t), old, &old);
    }
}

namespace AiDeck
{
    void Attach()
    {
        RelabelCampaignMenu();
        DetourAttach(&reinterpret_cast<PVOID&>(orig_FreeDuelInput), Hook_FreeDuelInput);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_FreeDuelSetState), Hook_FreeDuelSetState);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_CampaignInput), Hook_CampaignInput);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_GotoDuel), Hook_GotoDuel);
        DetourAttach(&reinterpret_cast<PVOID&>(orig_SetupSeat), Hook_SetupSeat);
    }
}
