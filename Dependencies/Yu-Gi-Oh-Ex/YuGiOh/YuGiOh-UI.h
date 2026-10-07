#include <string>

namespace YGO
{
    namespace UI
    {
        // The duel presentation dispatcher's ids (enum DuelAnimId in the IDB, switch in YGO::UI::Draw_DuelAnimationFromId 0x1407C1450).
        // The engine's message queue (Duel__MsgQueue__ExecuteFront) turns each engine message into one of these. Only the ids listed
        // here have a case; every other id (3, 17, 34, 37, 54, 65-68, 71, 73-79, 85, 86, 91+) falls to the default and does nothing.
        // Write-up with every argument: docs/DuelAnimations.md.
        enum DuelAnimId : int
        {
            DuelAnim_DuelStart = 1,             // "DUEL" splash (EffectHandle_DuelStart)
            DuelAnim_DuelEnd = 2,               // a2 1 = local side won, 2 = other side won, 3 = draw: round result + result banner + music
            DuelAnim_FlowStep = 4,              // a2 step, a3, a4: prompt/cursor step of the duel flow (tutorial aware)
            DuelAnim_PhaseChange = 5,           // a2 side, a3 phase 0-5 (Draw, Standby, Main 1, Battle, Main 2, End) (EffectHandle_PhaseChange)
            DuelAnim_TurnStart = 6,             // a2 side whose turn starts (EffectHandle_TurnChange); clears g_DuelStatBlock's per-turn part
            DuelAnim_FieldChange = 7,           // a2, a3: field background change + SND_FIELD_CHANGE
            DuelAnim_CursorToCard = 8,          // a2 side, a3 zone, a4 index: card cursor + highlight
            DuelAnim_UpdateMusicForLP = 9,      // switch the duel music to the LP-based track
            DuelAnim_AttackReset = 10,          // clears the attack state
            DuelAnim_AttackConfirm = 11,        // attack state = confirmed, cursor to the attacker
            DuelAnim_AttackDeclare = 12,        // a2 attacker side | zone << 8, a3 target side, a4 target zone (<0 cancel, >=7 direct: EffectHandle_DirectAttack)
            DuelAnim_Battle = 13,               // a2, a3, a4 battle result: attacker vs target (EffectHandle_Battle)
            DuelAnim_BattleEnd = 14,            // attack state = 3
            DuelAnim_LP_Set = 15,               // a2 side, a3 LP: sets the shown LP (and the engine LP before the duel starts)
            DuelAnim_LP_Change = 16,            // a2 side, a3 delta, a4 byte0 must be 0, byte1 cause: LP popup (EffectHandle_DamagePoint) + LP stats
            DuelAnim_HandShuffle = 18,          // a2 side: hand shuffle (needs 2+ cards in hand)
            DuelAnim_HandCardReveal = 19,       // a2 side, a3 hand index
            DuelAnim_ExodiaHand = 20,           // a2 side, a3 face up 0/1, a4 card instance (0 = the whole hand; the Exodia win reveal)
            DuelAnim_DeckShuffle = 21,          // a2 side (EffectHandle_DeckShuffle)
            DuelAnim_PileRefresh = 22,          // a2 side, a3 location 0-17: redraw a zone/pile
            DuelAnim_DrawFromPile = 23,         // a2, a3: card object leaves a pile (shows it in the card info panel)
            DuelAnim_PileUpdate = 24,           // a2, a3, a4 card
            DuelAnim_CursorSelect = 25,         // a2 side, a3 zone | index << 8
            DuelAnim_CardMove = 26,             // YGO__UI__OnCardMove(a2 card | kind << 16, a3 from, a4 to)
            DuelAnim_CardSwap = 27,             // a2, a3 packed card refs: two cards trade places
            DuelAnim_CardReturn = 28,           // a2 packed card, a3 0 = to the Extra Deck / 1 = to the hand
            DuelAnim_CardChangeId = 29,         // a2 packed, a3 card id, a4 instance: a card turns into another card
            DuelAnim_CardSet = 30,              // a2, a3, a4 (EffActField_SetCard + EffectHandle_CardFlash)
            DuelAnim_CardDestroy = 31,          // a2, a3, a4 (EffActField_DestroyCard)
            DuelAnim_CardDestroy2 = 32,         // same as 31, other completion bit
            DuelAnim_CardDestroy3 = 33,         // same as 31 without the field refresh
            DuelAnim_CardActivate = 35,         // a2 packed card, a3, a4 card id (EffectHandle_CardHappen)
            DuelAnim_CardNegate = 36,           // a2, a3, a4 instance (EffectHandle_CardDisable)
            DuelAnim_CounterChange = 38,        // a2 side | zone << 8, a3 count, a4 instance (EffectHandle_TurnCounter)
            DuelAnim_ActionEnd = 39,            // end of an action: clears the cursor/selection
            DuelAnim_SpellCounter = 40,         // a2 side | zone << 8, a3, a4 (EffectHandle_MagicCounter)
            DuelAnim_CounterSet = 41,           // a2, a3, a4 (EffectHandle_TurnCounter)
            DuelAnim_MonsterShuffle = 42,       // a2, a3 (EffectHandle_MonstShuffle)
            DuelAnim_TributeMark = 43,          // a4 card instance: mark a material (material kind 1 = Tribute)
            DuelAnim_TributeClear = 44,
            DuelAnim_TributePlay = 45,          // EffectHandle_CardSacrifice
            DuelAnim_FusionMark = 46,           // material kind 3
            DuelAnim_FusionClear = 47,
            DuelAnim_FusionPlay = 48,           // EffActField_Fusion
            DuelAnim_SynchroMark = 49,          // material kind 4 (Synchro: inferred, see docs)
            DuelAnim_SynchroClear = 50,
            DuelAnim_SynchroPlay = 51,          // clears the marks; the scene itself is anim 59
            DuelAnim_ZoneHighlight = 52,        // a2 side, a3 zone, a4 1 = new list / 2 = last: selectable zones
            DuelAnim_ChainResolve = 53,         // YGO__UI__OnChainResolve(a2 side, a3, a4 chain length) (EffectHandle_Chain)
            DuelAnim_Message = 55,              // a2 kind, a3 string, a4: duel dialog, waits for the player
            DuelAnim_SelectOption = 56,         // a2, a3: option/number select, waits for the player
            DuelAnim_SummonNormal = 57,         // a2 packed, a3 card id (EffActField_SummonMonster)
            DuelAnim_SummonSpecial = 58,        // a2 packed, a3 instance (EffActField_SummonMonster)
            DuelAnim_ExtraSummonScene = 59,     // a2 card instance, a3, a4 0/1 Fusion, 2 Synchro, 4 Xyz, 5 Pendulum, 6 Link (needs materials)
            DuelAnim_ShowCardInfo = 60,         // a2 card id: the card info panel
            DuelAnim_Coin = 61,                 // a2 side | zone << 1 | index << 6, a3 card id, a4 bit0 result (EffectHandle_Coin)
            DuelAnim_Dice = 62,                 // a2 side, a3 roll, a4 (EffectHandle_Dice)
            DuelAnim_Yujyo = 63,                // a2 side, a3 must be non-zero (EffectHandle_Yujyo, "friendship")
            DuelAnim_Finish_Generic = 64,       // a2 side, a3 win reason: reason 6 (Exodia) unlocks achievement 23. No visual
            DuelAnim_TributeOrRitualSummon = 69,// save stats only: SUMMONS_TRIBUTE / SUMMONS_RITUAL. No visual
            DuelAnim_ExtraDeckSummon = 70,      // save stats only: SUMMONS_FUSION/SYNCHRO/XYZ/PENDULUM/Link. No visual
            DuelAnim_StatSpellTrap = 72,        // compiled-out telemetry. No visual
            DuelAnim_LPPanelFlash = 80,         // a2 side, a3 0 = on / 1 = off: LP panel highlight
            DuelAnim_HandRandom = 81,           // a2 side (EffectHandle_HandRandom)
            DuelAnim_XyzMark = 82,              // material kind 5 (Xyz: inferred, see docs)
            DuelAnim_XyzClear = 83,
            DuelAnim_XyzPlay = 84,              // clears the marks; the scene itself is anim 59
            DuelAnim_LinkMark = 87,             // material kind 6 (Link: inferred, see docs) (EffectHandle_MaterialMark)
            DuelAnim_LinkClear = 88,
            DuelAnim_LinkPlay = 89,             // clears the marks; the scene itself is anim 59
            DuelAnim_Janken = 90,               // a2, a3 (EffectHandle_Janken, rock-paper-scissors)
        };

        // What Funky (or anything else) may fire on its own, outside the engine's message flow.
        enum class DuelAnimUse
        {
            Play,           // cosmetic, the arguments are plain numbers
            PlayWithCare,   // plays, but also changes duel or UI state (see Note)
            NeedsCards,     // the arguments must name live card instances/zones; the engine sends these
            WaitsForInput,  // opens a prompt the engine expects to answer; firing it alone leaves it hanging
            ChangesState,   // no animation of its own; changes duel/UI state
            WritesSave,     // writes save stats or unlocks an achievement
        };

        struct DuelAnimInfo
        {
            DuelAnimId Id;
            const char* Name;
            DuelAnimUse Use;
            const char* Args;   // labels for a2, a3, a4 separated by '|'; empty = unused
            const char* Note;
        };

        inline constexpr DuelAnimInfo DuelAnims[] = {
            { DuelAnim_DuelStart, "Duel start", DuelAnimUse::Play, "||", "The DUEL splash at the start of a duel." },
            { DuelAnim_DuelEnd, "Duel end", DuelAnimUse::PlayWithCare, "Outcome (1 win, 2 lose, 3 draw)||", "Result banner and music. Records the round result and the duel finishes after it." },
            { DuelAnim_FlowStep, "Flow step", DuelAnimUse::ChangesState, "Step|a3|a4", "Advances the duel's prompt/cursor flow." },
            { DuelAnim_PhaseChange, "Phase change", DuelAnimUse::PlayWithCare, "Side (0/1)|Phase (0 Draw..5 End)|", "Phase banner. Also moves the on-screen phase marker until the engine's next phase." },
            { DuelAnim_TurnStart, "Turn change", DuelAnimUse::PlayWithCare, "Side (0/1)||", "Turn banner. Also moves the on-screen turn owner and clears this turn's duel stat counters." },
            { DuelAnim_FieldChange, "Field change", DuelAnimUse::ChangesState, "a2|a3|", "Changes the field background (arguments not decoded)." },
            { DuelAnim_CursorToCard, "Cursor to card", DuelAnimUse::NeedsCards, "Side|Zone|Index", "" },
            { DuelAnim_UpdateMusicForLP, "Music for LP", DuelAnimUse::Play, "||", "Re-picks the duel music from the life points." },
            { DuelAnim_AttackReset, "Attack reset", DuelAnimUse::ChangesState, "||", "" },
            { DuelAnim_AttackConfirm, "Attack confirm", DuelAnimUse::ChangesState, "||", "" },
            { DuelAnim_AttackDeclare, "Attack declare", DuelAnimUse::NeedsCards, "Attacker side|zone<<8|Target side|Target zone (>=7 direct)", "" },
            { DuelAnim_Battle, "Battle", DuelAnimUse::NeedsCards, "a2|a3|a4", "Uses the attack state set by Attack declare." },
            { DuelAnim_BattleEnd, "Battle end", DuelAnimUse::ChangesState, "||", "" },
            { DuelAnim_LP_Set, "LP set", DuelAnimUse::ChangesState, "Side|LP|", "Before the duel starts it writes the engine's LP too." },
            { DuelAnim_LP_Change, "LP change", DuelAnimUse::WritesSave, "Side|Delta|0 | cause<<8", "LP popup, but it also counts damage into the save's stats." },
            { DuelAnim_HandShuffle, "Hand shuffle", DuelAnimUse::Play, "Side (0/1)||", "Needs 2 or more cards in that hand." },
            { DuelAnim_HandCardReveal, "Hand card reveal", DuelAnimUse::NeedsCards, "Side|Hand index|", "" },
            { DuelAnim_ExodiaHand, "Hand face up", DuelAnimUse::NeedsCards, "Side|Face up|Instance (0 = hand)", "" },
            { DuelAnim_DeckShuffle, "Deck shuffle", DuelAnimUse::Play, "Side (0/1)||", "" },
            { DuelAnim_PileRefresh, "Pile refresh", DuelAnimUse::ChangesState, "Side|Location 0-17|", "" },
            { DuelAnim_DrawFromPile, "Draw from pile", DuelAnimUse::NeedsCards, "a2|a3|", "" },
            { DuelAnim_PileUpdate, "Pile update", DuelAnimUse::NeedsCards, "a2|a3|Card", "" },
            { DuelAnim_CursorSelect, "Cursor select", DuelAnimUse::NeedsCards, "Side|Zone | index<<8|", "" },
            { DuelAnim_CardMove, "Card move", DuelAnimUse::NeedsCards, "Card | kind<<16|From|To", "" },
            { DuelAnim_CardSwap, "Card swap", DuelAnimUse::NeedsCards, "Card A|Card B|", "" },
            { DuelAnim_CardReturn, "Card return", DuelAnimUse::NeedsCards, "Card|To hand|", "" },
            { DuelAnim_CardChangeId, "Card change id", DuelAnimUse::NeedsCards, "Card|Card id|Instance", "Also counts the card as used." },
            { DuelAnim_CardSet, "Card set", DuelAnimUse::NeedsCards, "a2|a3|a4", "" },
            { DuelAnim_CardDestroy, "Card destroy", DuelAnimUse::NeedsCards, "a2|a3|a4", "" },
            { DuelAnim_CardDestroy2, "Card destroy 2", DuelAnimUse::NeedsCards, "a2|a3|a4", "" },
            { DuelAnim_CardDestroy3, "Card destroy 3", DuelAnimUse::NeedsCards, "a2|a3|a4", "" },
            { DuelAnim_CardActivate, "Card activate", DuelAnimUse::NeedsCards, "Card|a3|Card id", "Also counts the card as used." },
            { DuelAnim_CardNegate, "Card negate", DuelAnimUse::NeedsCards, "a2|a3|Instance", "" },
            { DuelAnim_CounterChange, "Counter change", DuelAnimUse::NeedsCards, "Side | zone<<8|Count|Instance", "" },
            { DuelAnim_ActionEnd, "Action end", DuelAnimUse::ChangesState, "||", "" },
            { DuelAnim_SpellCounter, "Spell counter", DuelAnimUse::NeedsCards, "Side | zone<<8|a3|a4", "" },
            { DuelAnim_CounterSet, "Counter set", DuelAnimUse::NeedsCards, "a2|a3|a4", "" },
            { DuelAnim_MonsterShuffle, "Monster shuffle", DuelAnimUse::NeedsCards, "a2|a3|", "" },
            { DuelAnim_TributeMark, "Tribute mark", DuelAnimUse::NeedsCards, "||Instance", "" },
            { DuelAnim_TributeClear, "Tribute clear", DuelAnimUse::ChangesState, "||", "" },
            { DuelAnim_TributePlay, "Tribute play", DuelAnimUse::NeedsCards, "Side|Zone|Instance", "" },
            { DuelAnim_FusionMark, "Fusion mark", DuelAnimUse::NeedsCards, "||Instance", "" },
            { DuelAnim_FusionClear, "Fusion clear", DuelAnimUse::ChangesState, "||", "" },
            { DuelAnim_FusionPlay, "Fusion play", DuelAnimUse::NeedsCards, "Side|Zone|Instance", "" },
            { DuelAnim_SynchroMark, "Synchro mark", DuelAnimUse::NeedsCards, "||Instance", "" },
            { DuelAnim_SynchroClear, "Synchro clear", DuelAnimUse::ChangesState, "||", "" },
            { DuelAnim_SynchroPlay, "Synchro play", DuelAnimUse::ChangesState, "||Instance", "" },
            { DuelAnim_ZoneHighlight, "Zone highlight", DuelAnimUse::WaitsForInput, "Side|Zone|1 new / 2 last", "" },
            { DuelAnim_ChainResolve, "Chain resolve", DuelAnimUse::NeedsCards, "Side|a3|Chain length", "" },
            { DuelAnim_Message, "Message", DuelAnimUse::WaitsForInput, "Kind|String|a4", "" },
            { DuelAnim_SelectOption, "Select option", DuelAnimUse::WaitsForInput, "a2|a3|", "" },
            { DuelAnim_SummonNormal, "Normal summon", DuelAnimUse::NeedsCards, "Card|Card id|", "Also counts the card as used." },
            { DuelAnim_SummonSpecial, "Special summon", DuelAnimUse::NeedsCards, "Card|Instance|", "Also counts the card as used." },
            { DuelAnim_ExtraSummonScene, "Extra summon scene", DuelAnimUse::NeedsCards, "Instance|a3|Kind (0/1 Fus, 2 Syn, 4 Xyz, 5 Pend, 6 Link)", "Does nothing unless the card has materials." },
            { DuelAnim_ShowCardInfo, "Show card info", DuelAnimUse::Play, "Card id||", "Shows the card in the info panel." },
            { DuelAnim_Coin, "Coin toss", DuelAnimUse::Play, "Side (0/1)|Card id|Result (0/1)", "" },
            { DuelAnim_Dice, "Dice roll", DuelAnimUse::Play, "Side (0/1)|Roll (1-6)|a4", "" },
            { DuelAnim_Yujyo, "Yujyo (friendship)", DuelAnimUse::Play, "Side (0/1)|On (non-zero)|", "" },
            { DuelAnim_Finish_Generic, "Special win", DuelAnimUse::WritesSave, "Side|Win reason|", "Reason 6 (Exodia) unlocks Steam achievement 23." },
            { DuelAnim_TributeOrRitualSummon, "Stat: tribute/ritual", DuelAnimUse::WritesSave, "Side|Card|Kind", "" },
            { DuelAnim_ExtraDeckSummon, "Stat: extra summon", DuelAnimUse::WritesSave, "Side|Card|Kind", "" },
            { DuelAnim_StatSpellTrap, "Stat: spell/trap", DuelAnimUse::ChangesState, "Side|Card|Kind", "Compiled out; does nothing." },
            { DuelAnim_LPPanelFlash, "LP panel flash", DuelAnimUse::Play, "Side (0/1)|0 on / 1 off|", "" },
            { DuelAnim_HandRandom, "Hand random", DuelAnimUse::Play, "Side (0/1)||", "" },
            { DuelAnim_XyzMark, "Xyz mark", DuelAnimUse::NeedsCards, "||Instance", "" },
            { DuelAnim_XyzClear, "Xyz clear", DuelAnimUse::ChangesState, "||", "" },
            { DuelAnim_XyzPlay, "Xyz play", DuelAnimUse::ChangesState, "||Instance", "" },
            { DuelAnim_LinkMark, "Link mark", DuelAnimUse::NeedsCards, "||Instance", "" },
            { DuelAnim_LinkClear, "Link clear", DuelAnimUse::ChangesState, "||", "" },
            { DuelAnim_LinkPlay, "Link play", DuelAnimUse::ChangesState, "||Instance", "" },
            { DuelAnim_Janken, "Janken", DuelAnimUse::WaitsForInput, "a2|a3|", "Rock-paper-scissors; arguments not decoded." },
        };

        inline const DuelAnimInfo* FindDuelAnim(int id)
        {
            for (const auto& info : DuelAnims)
                if (info.Id == id)
                    return &info;
            return nullptr;
        }

        // The game's thunk to the dispatcher (0x1407C1450). Returns 1.
        inline auto Draw_DuelAnimationFromId = reinterpret_cast<int(__fastcall*)(DuelAnimId Animation, int a2, int a3, int a4)>(0x1407EB7E0);
        inline int& g_iActiveDuelAnimation = *reinterpret_cast<int*>(0x1427D0C08);
        inline int& g_iCurrentDuelAnimation = *reinterpret_cast<int*>(0x1427D0C18);
        inline int& g_iPreviousDuelAnimation = *reinterpret_cast<int*>(0x1427D0C1C);

        inline const char* g_sPendulumEffectE = reinterpret_cast<const char*>(0x140A52068);


        namespace RIX
        {
            enum ScreenID : int
            {
                SCREEN_TITLE_SCREEN = 0x5,
                SCREEN_SIGN_IN = 0x6,               // RIX::ScreenSignIn: Play on the title goes here; it reads the save, then opens the main menu
                SCREEN_COMMON_BG = 0x7,             // draws every screen's backdrop (by the current screen's ScreenType)
                SCREEN_MAIN_MENU = 0x8,             // RIX::ScreenMainMenu (was listed as 6, which is SignIn)
                SCREEN_SAVE_SELECT = 100,           // Yu-Gi-Oh-Core's save-select screen (not the game's)
                SCREEN_LOADING_SCREEN = 0x9,
                SCREEN_CAMPAIGN_SELECTION = 0xA,
                SCREEN_HELP_AND_OPTIONS = 12,
                SCREEN_SETTINGS = 13,
                SCREEN_VIDEO_SETTINGS = 14,
                SCREEN_GAME_CREDITS = 15,
                SCREEN_CONTROLLER_SETTINGS = 16,
                SCREEN_HOW_TO_PLAY = 17,
                SCREEN_STATISTICS = 18,
                SCREEN_MULTIPLAYER_PLAYER_LIST = 19,
                SCREEN_PAUSE_MENU = 20,
                SCREEN_DUELIST_CHALLENGE = 21,
                SCREEN_CHOOSE_A_DECK = 23,
                SCREEN_TUTORIAL_LIST = 24,
                SCREEN_DECK_EDITOR = 25,
                SCREEN_SWAP_CARDS = 26,
                SCREEN_INTERMISSION_RESULTS = 27,
                SCREEN_DUEL_RESULTS = 28,
                SCREEN_CARD_SHOP = 29,
                SCREEN_BATTLEPACK = 30,
                SCREEN_PLAYER_MATCH = 33,
                SCREEN_CREATE_MULTIPLAYER = 34,
                SCREEN_FIND_MATCH = 35,
                SCREEN_LOBBY = 36,
                SCREEN_LEADERBOARD = 38,
                SCREEN_JOIN_GAME = 39,
                SCREEN_DUEL_SELECT = 41,
                SCREEN_STORY_SELECTION = 42,
                SCREEN_SCORE_REVIEW = 43,
            };
        }
    }

}
