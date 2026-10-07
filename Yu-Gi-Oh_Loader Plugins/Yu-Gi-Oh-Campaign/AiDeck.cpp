#include "AiDeck.h"
#include "Common.h"

#include <Windows.h>
#include <cstdint>
#include <format>
#include <map>
#include <string>

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

    // Yu-Gi-Oh-TagDuel (optional): a Free Duel with tag on picks both partners (characters on the opponent list, then their decks).
    // Campaign duels are never tag duels.
    bool TagDuelOn()
    {
        static auto isEnabled = reinterpret_cast<bool(__cdecl*)()>(GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-TagDuel.dll"), "TagDuel_IsEnabled"));
        return isEnabled && isEnabled();
    }

    void SetTagPartners(int yourCharacter, int yourDeck, int opponentCharacter, int opponentDeck)
    {
        static auto setPartners = reinterpret_cast<void(__cdecl*)(int, int, int, int)>(GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-TagDuel.dll"),
            "TagDuel_SetPartners"));
        if (setPartners)
            setPartners(yourCharacter, yourDeck, opponentCharacter, opponentDeck);
    }

    // The opponent list's steps in a tag Free Duel: your partner, the opponent, the opponent's partner. The game reads the list's last
    // pick (the opponent's partner) as seat 1 when the duel starts, so Hook_SetupSeat puts g_TagOpponent back in seat 1.
    enum CharacterStep { kPickYourPartner, kPickOpponent, kPickOpponentPartner };
    const wchar_t* const kCharacterTitles[] = { L"Your Partner", L"Opponent", L"Opponent's Partner" };
    int g_CharacterStep = kPickYourPartner;
    int g_PartnerCharacters[2] = { -1, -1 };   // [0] your partner, [1] the opponent's partner
    int g_TagOpponent = -1;                     // the opponent picked on the list (-1 = not a tag Free Duel)

    // YGO::GAME::Get_CharacterName (0x1407FEC70), as UTF-8 for the log.
    std::string CharacterName(int character)
    {
        const auto name = reinterpret_cast<const wchar_t*(__fastcall*)(int)>(0x1407FEC70)(character);
        if (!name)
            return "?";
        char out[128]{};
        WideCharToMultiByte(CP_UTF8, 0, name, -1, out, sizeof(out) - 1, nullptr, nullptr);
        return out;
    }
    const auto SetDuelSideCharacter = reinterpret_cast<void(__fastcall*)(int side, unsigned int character)>(0x140769670);
    int* const g_FreeDuelOpponentDeckId = reinterpret_cast<int*>(0x140C8E818);   // what the opponent's deck panel (+3448) shows

    // The picker's steps after your deck. Partner steps only in a tag duel.
    enum Step { kStepOpponent, kStepPartner, kStepOpponentPartner };
    struct StepText { const wchar_t* Title; const wchar_t* Help; };
    const StepText kSteps[] = {
        { L"Opponent's Deck", L"Choose the deck your opponent plays. They keep their portrait and name." },
        { L"Your Partner's Deck", L"Choose the deck your tag partner plays." },
        { L"Opponent's Partner's Deck", L"Choose the deck your opponent's tag partner plays." },
    };
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
        int Step = kStepOpponent;
        int OpponentDeck = -1;          // picked on the opponent's step (tag duels go on from there)
        int PartnerDeck = -1;           // picked on your partner's step
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

    // Every deck the game has: storyDeck first (the opponent's own, or the teammate's on a partner step), your save decks, then every
    // deckdata deck with cards.
    void FillAllDecks(void* list, int storyDeck)
    {
        R::Vector* ids = List::Ids(list);
        ids->End = ids->Begin;
        if (storyDeck >= 0)
            List::Push(ids, static_cast<unsigned int>(storyDeck));
        for (unsigned int id = 0; id < kSaveDecks; ++id)
            if (static_cast<int>(id) != storyDeck && HasCards(id))
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
        SetDescription(help, reinterpret_cast<int64_t>(kSteps[g_Picker.Step].Help));
        R::HelpLayout(help);
    }

    // Fills the picker for the current step: the deck most likely wanted first (opponent: their own; your partner: yours; the
    // opponent's partner: the opponent's), and starts on it.
    void ShowStep()
    {
        Picker& p = g_Picker;
        int first = p.StoryDeck;
        if (p.Step == kStepPartner)
            first = CharacterDeck(g_PartnerCharacters[0]);
        else if (p.Step == kStepOpponentPartner)
            first = CharacterDeck(g_PartnerCharacters[1]);
        FillAllDecks(p.List, first);
        int selected = first;
        if (p.Step == kStepOpponent)
            if (auto remembered = g_Remembered.find(p.Character); remembered != g_Remembered.end())
                selected = static_cast<int>(remembered->second);
        List::SelectDeckId(p.List, selected);
        if (!p.Campaign)
        {
            // Free Duel's two panels: left (blue) is your team, right (red) the opponent's. Your partner's step browses on the left and
            // keeps the opponent's pick on the right; the opponent's steps browse on the right and keep your deck on the left.
            char* left = p.Screen + kFreePlayerPanel;
            char* right = p.Screen + kFreeOpponentPanel;
            p.Panel = p.Step == kStepPartner ? left : right;
            static unsigned int kept = 0;
            kept = static_cast<unsigned int>(p.Step == kStepPartner ? p.OpponentDeck : p.PlayerDeck);
            R::DeckInfoPanel::ShowDeck(p.Step == kStepPartner ? right : left, static_cast<int>(kept) >= 0 ? &kept : nullptr);
        }
        R::DeckInfoPanel::ShowDeck(p.Panel, List::GetSelectedDeckIdPtr(p.List));
        SetTitle(p.Screen, kSteps[p.Step].Title);
        ShowPickerHelp();
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
            p.Character = g_TagOpponent >= 0 ? g_TagOpponent : static_cast<int>(OpponentListSelected(screen + kFreeOpponents));
            p.StoryDeck = CharacterDeck(p.Character);
        }
        p.Active = true;
        p.Step = kStepOpponent;
        p.OpponentDeck = p.PartnerDeck = -1;
        ShowStep();
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
            if (p.Step == kStepOpponent && !p.Campaign && TagDuelOn())
            {
                p.OpponentDeck = deck;
                p.Step = kStepPartner;
                ShowStep();
            }
            else if (p.Step == kStepPartner)
            {
                p.PartnerDeck = deck;
                p.Step = kStepOpponentPartner;
                ShowStep();
            }
            else if (p.Step == kStepOpponentPartner)
            {
                SetTagPartners(g_PartnerCharacters[0], p.PartnerDeck, g_PartnerCharacters[1], deck);
                StartDuel(static_cast<unsigned int>(p.OpponentDeck));
            }
            else
                StartDuel(static_cast<unsigned int>(deck));
        }
        else if (mask & kCancel)
        {
            R::PlayUISound(kSoundCancel);
            if (p.Step > kStepOpponent)
            {
                --p.Step;
                ShowStep();
            }
            else
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
        // The highlighted character as the player confirms, read before the game's confirm handling moves on.
        const int highlighted = before == kFreeOpponentList ? static_cast<int>(OpponentListSelected(s + kFreeOpponents)) : 0;
        orig_FreeDuelInput(screen, ui);

        // Tag Free Duel: the opponent list is walked three times (your partner, the opponent, the opponent's partner). Confirm on the
        // first two goes back to the list (FreeDuel_SetState(1) is the game's own way back to it); Back steps back a pick.
        if (before == kFreeOpponentList && TagDuelOn() && !IsMultiplayer())
        {
            if (state == kFreeChoosingDeck)
            {
                const int picked = highlighted;
                static const char* const kWho[] = { "your partner", "opponent", "opponent's partner" };
                Logger::WriteLog(std::format("Tag Free Duel: {} = character {} ({}), title was \"{}\"", kWho[g_CharacterStep], picked, CharacterName(picked),
                    g_CharacterStep == kPickYourPartner ? "Your Partner" : g_CharacterStep == kPickOpponent ? "Opponent" : "Opponent's Partner"),
                    MODULE_NAME, 0);
                if (g_CharacterStep == kPickYourPartner)
                    g_PartnerCharacters[0] = picked;
                else if (g_CharacterStep == kPickOpponent)
                    g_TagOpponent = picked;
                else
                    g_PartnerCharacters[1] = picked;

                if (g_CharacterStep < kPickOpponentPartner)
                {
                    ++g_CharacterStep;
                    orig_FreeDuelSetState(screen, kFreeOpponentList);
                }
                else
                {
                    // On to your deck: SetState(2) showed the list's pick (the opponent's partner) as the opponent's deck - show theirs.
                    *g_FreeDuelOpponentDeckId = CharacterDeck(g_TagOpponent);
                    R::DeckInfoPanel::ShowDeck(s + kFreeOpponentPanel, *g_FreeDuelOpponentDeckId >= 0 ? reinterpret_cast<unsigned int*>(g_FreeDuelOpponentDeckId) : nullptr);
                }
            }
            else if (state < kFreeOpponentList && g_CharacterStep > kPickYourPartner)
            {
                --g_CharacterStep;
                orig_FreeDuelSetState(screen, kFreeOpponentList);
            }
        }
        if (state == kFreeOpponentList && TagDuelOn() && !IsMultiplayer())
            SetTitle(s, kCharacterTitles[g_CharacterStep]);
        else if (state < kFreeOpponentList)
        {
            g_CharacterStep = kPickYourPartner;   // left the list: start the picks over next time
            g_TagOpponent = -1;
        }

        if (before == kFreeChoosingDeck && state == kFreeStart && !IsMultiplayer())
        {
            // Your deck is confirmed (Set_DuelSideDeck runs on the next frame, in state 3): ask for the opponent's first.
            state = kFreeChoosingDeck;
            Enter(s, false, List::GetSelectedDeckId(s + kFreeList));
        }
        else if (state == kFreeChoosingDeck && !g_Picker.Active)
            SetTitle(s, kTitlePlayer);
        else if (state != kFreeChoosingDeck && state != kFreeStart && !(state == kFreeOpponentList && TagDuelOn()))
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
        // Tag Free Duel: the game took seat 1 from the list's last pick (the opponent's partner); the opponent goes back in.
        if (seat == 1 && g_TagOpponent >= 0 && TagDuelOn() && !IsMultiplayer() && !IsChallenge())
        {
            Logger::WriteLog(std::format("Tag Free Duel: seat 1 = opponent {} ({}) (the list ended on {} ({}))", g_TagOpponent, CharacterName(g_TagOpponent),
                character, CharacterName(character)), MODULE_NAME, 0);
            character = g_TagOpponent;
            SetDuelSideCharacter(1, static_cast<unsigned int>(character));
            if (const int own = CharacterDeck(character); own >= 0)
                if (DeckCards* ownDeck = DeckFromId(kCurrentProfile, static_cast<unsigned int>(own)); ownDeck && ownDeck->MainCount > 0)
                    deck = ownDeck;   // replaced below when another deck was picked for them
        }
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
