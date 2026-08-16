#include <string>

namespace YGO
{
    namespace UI
    {
        enum Animations {
            DUEL = 0x1,
            YOU_WIN = 0x2,
            YOU_LOOSE = 0x2,
            YOU_DRAW = 0x2,
            TURN_CHANGE = 0x6
        };

        inline auto Draw_DuelAnimationFromId = reinterpret_cast<void(__fastcall*)(Animations Animation, int OptionOne, int a3, int a4)>(0x1407EB7E0);
        inline int& g_iActiveDuelAnimation = *reinterpret_cast<int*>(0x1427D0C08);
        inline int& g_iCurrentDuelAnimation = *reinterpret_cast<int*>(0x1427D0C18);
        inline int& g_iPreviousDuelAnimation = *reinterpret_cast<int*>(0x1427D0C1C);

        const char* g_sPendulumEffectE = reinterpret_cast<const char*>(0x140A52068);


        namespace RIX
        {
            enum ScreenID : int
            {
                SCREEN_TITLE_SCREEN = 0x5,
                SCREEN_MAIN_MENU = 0x6,
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
