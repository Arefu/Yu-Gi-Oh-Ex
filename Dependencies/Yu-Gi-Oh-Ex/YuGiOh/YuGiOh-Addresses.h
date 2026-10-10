#pragma once
#include <cstdint>

// Game addresses used by more than one plugin, by their YuGiOh.exe.i64 (IDB) names. A plugin that needs one of these uses the name here,
// never the number, so a correction is made once. Addresses only one plugin uses can stay in that plugin (with the IDB name beside them);
// once a second plugin needs one, it moves here. Functions are addresses to cast or detour; data are addresses to read.
namespace YGO::Addr
{
    inline constexpr uintptr_t ImageBase = 0x140000000;

    // ---- duel engine
    inline constexpr uintptr_t Duel_MsgQueue_PumpAndMirror = 0x1400117E0;         // Duel__MsgQueue__PumpAndMirror
    inline constexpr uintptr_t DuelSetup_ClearState = 0x14005FC90;                // YGO::DUEL::DuelSetup_ClearState
    inline constexpr uintptr_t Duel_LoadDeck = 0x1400822F0;
    inline constexpr uintptr_t Duel_LoadEngineFromFrontBlock = 0x140082960;       // the tag-duel partner swap
    inline constexpr uintptr_t Duel_Engine_Init = 0x1407BC4C0;                    // YGO::DUEL::Engine_Init
    inline constexpr uintptr_t Duel_FinishAndUpdateSave = 0x14087F250;            // YGO::DUEL::FinishAndUpdateSave
    inline constexpr uintptr_t DuelSetup_AssignSeatDecksAndDuelists = 0x14081E530;
    inline constexpr uintptr_t DuelSetup_Setup_RuleAndLP = 0x1407696E0;           // YGO::DuelSetup::Setup_RuleAndLP

    // ---- duel mode / seat getters and setters (YGO::DUEL::*)
    inline constexpr uintptr_t Duel_Get_LocalPlayerSeat = 0x140768F80;
    inline constexpr uintptr_t Duel_Get_IsBattlePackMode = 0x140769190;
    inline constexpr uintptr_t Duel_Get_IsChallengeMode = 0x1407691B0;
    inline constexpr uintptr_t Duel_Get_IsDuelMultiplayer = 0x1407691D0;
    inline constexpr uintptr_t Duel_ResetMatchResults = 0x140769500;
    inline constexpr uintptr_t Duel_Set_CurrentArenaId = 0x140769540;
    inline constexpr uintptr_t Duel_Set_DuelSideCharacter = 0x140769670;
    inline constexpr uintptr_t Duel_Set_IsLiveSession = 0x1407696C0;
    inline constexpr uintptr_t Duel_Set_IsDuelMultiplayer = 0x140769720;
    inline constexpr uintptr_t Duel_Set_StartingPlayer = 0x140769740;
    inline constexpr uintptr_t Duel_Set_IsTagDuel = 0x140769AC0;

    // ---- duel data
    inline constexpr uintptr_t g_bEngineRules = 0x140C8D1C9;
    inline constexpr uintptr_t g_CurrentArenaId = 0x140C8D1F0;                    // int
    inline constexpr uintptr_t g_DuelSideCharacters = 0x140C8D1F8;                // int[4] (chardata ids per seat)
    inline constexpr uintptr_t g_iStartLifePoints = 0x140C8D370;                  // int
    inline constexpr uintptr_t Duel_DuelEngine = 0x143330280;
    inline constexpr uintptr_t Duel_DuelEngine_Player1Deck = 0x14333036C;         // Duel_DuelEngine + 0xEC
    inline constexpr uintptr_t Duel_PlayerStates = 0x143497C40;                   // stru_143497C40: Duel::PlayerState x2 (0xD94 bytes each)

    // ---- cards
    inline constexpr uintptr_t Card_IsInNamedArchetype = 0x14076CFF0;             // Is_CardInNamedArchetype
    inline constexpr uintptr_t Card_GetNameFromKonamiId = 0x14076D0F0;            // YGO::CARDS::Get_CardNameFromKonamiId
    inline constexpr uintptr_t Card_GetTypeFromFullCardPropsByKonamiId = 0x14081A650;
    inline constexpr uintptr_t CardFrameTable = 0x140BF7820;                      // 12 bytes per card type
    inline constexpr uintptr_t g_pCardDataFiles = 0x140C8D3A0;
    inline constexpr uintptr_t g_KonamiIds = 0x140D50510;
    inline constexpr uintptr_t g_iInternalIDs = 0x140D55480;                      // YGO::CARDS::g_iInternalIDs
    inline constexpr uintptr_t INTERNAL_ID_CARD_PROPS = 0x1427D0C30;              // YGO::CARDS::INTERNAL_ID_CARD_PROPS
    inline constexpr uintptr_t KONAMI_ID_CARD_PROPS = 0x142847E50;                // YGO::CARDS::KONAMI_ID_CARD_PROPS
    inline constexpr uintptr_t FULL_CARD_PROPS = 0x142927600;                     // YGO::CARDS::FULL_CARD_PROPS (0xA0 per Konami id)

    // ---- decks, characters, story, packs
    inline constexpr uintptr_t Deck_FromId = 0x14081AB40;                         // (profile, deck id) -> YGO::SAVE::DeckListItem*
    inline constexpr uintptr_t DeckTemplateList = 0x14275AC50;                    // YGO::GAME::DeckTemplateList[700], 0x130 each
    inline constexpr uintptr_t Game_Get_CharacterName = 0x1407FEC70;              // YGO::GAME::Get_CharacterName
    inline constexpr uintptr_t g_CharacterRecords = 0x142913470;                  // CharacterRecord[240], 0x68 each
    inline constexpr uintptr_t g_StoryDuelRecords = 0x142919DA0;                  // [226]
    inline constexpr uintptr_t g_pStoryScriptFile = 0x140D4DEF0;
    inline constexpr uintptr_t g_PackRecords = 0x1429241C0;                       // [128]

    // ---- save
    inline constexpr uintptr_t Save_Get_PlayerSection = 0x1407F80A0;              // YGO::SAVE::Get_PlayerSection(0xFFFFFFFD = current)

    // ---- UI (RIX)
    inline constexpr uintptr_t App_GetUI = 0x1408121B0;                           // RIX::App::GetUI
    inline constexpr uintptr_t UI_GetScheduler = 0x140807F60;                     // RIX::UI::GetScheduler
    inline constexpr uintptr_t Screen_GotoDuel = 0x14083CA10;                     // fade to the duel (screen 9)
    inline constexpr uintptr_t MainMenu_ActivateItem = 0x140856C40;               // RIX::ScreenMainMenu::ActivateItem
    inline constexpr uintptr_t Widget_SetEnabled = 0x14075A490;                   // RIX::widget_Base::SetEnabled
    inline constexpr uintptr_t WidgetText_SetTextById = 0x14075E000;              // RIX::widget_Text::SetTextById
    inline constexpr uintptr_t WidgetDescription_SetTextById = 0x14089F6E0;       // widget_Description::SetTextById
    inline constexpr uintptr_t HelpEntry_Select = 0x140A7A1A0;                    // the game's help bar entry (confirm "Select")
    inline constexpr uintptr_t UI_Draw_DuelAnimationFromId = 0x1407C1450;         // YGO::UI::Draw_DuelAnimationFromId

    // ---- text, language, misc
    inline constexpr uintptr_t WString_Assign = 0x140751310;                      // the game's std::wstring::assign (its allocator)
    inline constexpr uintptr_t LanguageLetterTable = 0x140A51D30;                 // g_LanguageLetterTable: {int id, char letter}, ended by -1
    inline constexpr uintptr_t g_iGameLanguageID = 0x14332A344;                   // int
    inline constexpr uintptr_t g_bIsJpVersion = 0x14332A348;                      // bool
    inline constexpr uintptr_t g_Archives = 0x1429241A0;
    inline constexpr uintptr_t g_SteamRemoteStorageContext = 0x140C8EA50;

    // ---- sound
    inline constexpr uintptr_t g_SoundSlots = 0x143329500;                        // int64[73] (Wwise sound objects)
    inline constexpr uintptr_t g_MusicVolume = 0x143329748;                       // float 0..1
    inline constexpr uintptr_t g_SfxVolume = 0x143329750;                         // float 0..1
    inline constexpr uintptr_t g_bSoundSlotsReady = 0x143329758;                  // bool
    inline constexpr uintptr_t g_CurrentMusicSlot = 0x14332975C;                  // int, -1 = none
}
