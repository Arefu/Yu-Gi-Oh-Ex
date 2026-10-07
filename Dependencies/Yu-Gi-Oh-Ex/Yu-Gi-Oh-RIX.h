#pragma once
/*
    Yu-Gi-Oh-RIX - the public interface of the menu plugin (Yu-Gi-Oh-RIX.dll).

    The game builds its main menu from a fixed table of 13 buttons. Yu-Gi-Oh-RIX lets a plugin add, change and remove more of them.
    Nothing here needs the game's headers: the functions are plain C exports, so any language that can call a DLL can use them.

      - Two menus can be extended: the main menu (RIX_MENU_MAIN) and the Help & Options menu (RIX_MENU_OPTIONS).
      - The game's own main menu buttons can be given a new action (RIX_SetMainMenuItemAction), and a pages of your own (menus or your own widgets)
        can be shown on the Battle Pack screen's background (RIX_OpenPage).
      - A button added BEFORE the main menu is first built (at plugin start-up) appears the first time the menu opens.
      - A button added later appears the next time the game builds the main menu again (a restart is always enough).
      - Changing or removing a button takes effect straight away, on the next frame of the main menu.
      - Button ids are 13 and up and are never reused, so an id stays valid after other buttons are removed.
      - Callbacks run on the game's thread, inside the menu's own update, so they may call the game.

    From C++ include this header, call RIX::Load() once (it finds the DLL that is already loaded) and then use the functions:

        if (RIX::Load())
        {
            RIX_ButtonDesc button = RIX::Describe(L"My Mod", L"What it does", RIX_PAGE_MAIN, &MyCallback, nullptr);
            int id = RIX::AddMainMenuButton(&button);
        }
*/
#include <stdint.h>
#include <wchar.h>

#ifdef __cplusplus
extern "C" {
#endif

#define RIX_API_VERSION 5   /* 5: pages hold 5 buttons (RIX_PageDesc grew), RIX_UpdatePageButton */

/* The plugin itself defines RIX_EXPORTS; everyone else only declares the functions (and normally uses RIX::Load below instead). */
#ifdef RIX_EXPORTS
#define RIX_API __declspec(dllexport)
#else
#define RIX_API __declspec(dllimport)
#endif

/* The menus buttons can be added to. */
#define RIX_MENU_MAIN 0     /* the main menu (ScreenMainMenu); button ids are 13 to 99 */
#define RIX_MENU_OPTIONS 1  /* the Help & Options menu (ScreenHelp); button ids are 100 and up. Skin is one of RIX_OPTION_* */

/* The three pages of the main menu (RIX_MENU_MAIN only). A button is listed on exactly one page. */
#define RIX_PAGE_MAIN 0         /* the first screen: Single Player, Multiplayer, Battle Pack, Deck Editor, Card Shop, Options, Quit */
#define RIX_PAGE_SINGLE_PLAYER 1
#define RIX_PAGE_MULTIPLAYER 2

/* The buttons the game ships. A new button borrows the artwork of one of these (RIX_ButtonDesc::Skin). */
#define RIX_ITEM_SINGLE_PLAYER_MENU 0
#define RIX_ITEM_SOLO_DUEL 1
#define RIX_ITEM_DUELIST_CHALLENGE 2
#define RIX_ITEM_MULTIPLAYER_MENU 3
#define RIX_ITEM_MULTIPLAYER_A 4
#define RIX_ITEM_MULTIPLAYER_B 5
#define RIX_ITEM_LEADERBOARD 6
#define RIX_ITEM_BATTLE_PACK 7
#define RIX_ITEM_DECK_EDITOR 8
#define RIX_ITEM_CARD_SHOP 9
#define RIX_ITEM_HELP_AND_OPTIONS 10
#define RIX_ITEM_TUTORIALS 11
#define RIX_ITEM_QUIT_GAME 12

/* The buttons the options menu ships (artwork to borrow). */
#define RIX_OPTION_HOW_TO_PLAY 0
#define RIX_OPTION_CONTROLLER_SETTINGS 1
#define RIX_OPTION_SETTINGS 2
#define RIX_OPTION_VIDEO_SETTINGS 3
#define RIX_OPTION_CREDITS 6

/* Screens for RIX_GotoScreen / RIX_GetCurrentScreenId. The numbers are the ids the game gives its screen classes (RIX::ScreenBase::SetScreenId),
   in the order of its MainState_* names. */
#define RIX_SCREEN_TITLE 5
#define RIX_SCREEN_SIGN_IN 6
#define RIX_SCREEN_COMMON_BG 7
#define RIX_SCREEN_MAIN_MENU 8
#define RIX_SCREEN_LOADING 9                /* ScreenGameLoading */
#define RIX_SCREEN_GAME_BEGIN 10
#define RIX_SCREEN_EX_GAME_DUEL 11
#define RIX_SCREEN_HELP_AND_OPTIONS 12      /* ScreenHelp */
#define RIX_SCREEN_SETTINGS 13              /* ScreenHelpSetting */
#define RIX_SCREEN_VIDEO_SETTINGS 14
#define RIX_SCREEN_GAME_CREDITS 15
#define RIX_SCREEN_CONTROLLER_SETTINGS 16   /* ScreenHelpControls */
#define RIX_SCREEN_HOW_TO_PLAY 17
#define RIX_SCREEN_STATISTICS 18
#define RIX_SCREEN_VOICES 19
#define RIX_SCREEN_PAUSE_MENU 20
#define RIX_SCREEN_DUELIST_CHALLENGE 21     /* ScreenHardChallenge: the Free Duel picker (series, opponent, deck); challenge mode only when the main menu opens it */
#define RIX_SCREEN_FREE_DUEL 21             /* the same screen; open it with every duel mode off for a plain free duel */
#define RIX_SCREEN_CAMPAIGN_DIALOG 22
#define RIX_SCREEN_CAMPAIGN_SELECT_DECK 23
#define RIX_SCREEN_TUTORIAL_LIST 24         /* ScreenSelectTutorial */
#define RIX_SCREEN_DECK_EDITOR 25
#define RIX_SCREEN_SWAP_CARDS 26            /* ScreenSideDeckSwap */
#define RIX_SCREEN_MATCH_RESULT 27
#define RIX_SCREEN_GAME_RESULT 28
#define RIX_SCREEN_CARD_SHOP 29             /* ScreenRandomPackStore */
#define RIX_SCREEN_BATTLEPACK 30            /* ScreenBattlePackStore */
#define RIX_SCREEN_BATTLEPACK_DRAFT 31
#define RIX_SCREEN_BATTLEPACK_EDIT 32
#define RIX_SCREEN_PLAYER_MATCH 33          /* ScreenLiveMenu */
#define RIX_SCREEN_LIVE_SETTING 34
#define RIX_SCREEN_LIVE_SESSION 35
#define RIX_SCREEN_LIVE_LOBBY 36
#define RIX_SCREEN_LIVE_LOADING 37
#define RIX_SCREEN_LEADERBOARD 38           /* ScreenLiveLeaderboard */
#define RIX_SCREEN_INVITE_LANDING 39
#define RIX_SCREEN_SAFETY_ZONE 40
#define RIX_SCREEN_DUEL_SELECT 41           /* ScreenSelectSeries */
#define RIX_SCREEN_SELECT_RUNG 42
#define RIX_SCREEN_SCORE_REVIEW 43

/* Called on the game's thread when the button is pressed. */
typedef void(__cdecl* RIX_ButtonCallback)(int ButtonId, void* User);

typedef struct RIX_ButtonDesc
{
    uint32_t Size;                  /* sizeof(RIX_ButtonDesc); lets later versions add fields */
    const wchar_t* Label;           /* the text on the button (copied) */
    const wchar_t* Description;     /* shown while the button is highlighted (copied); may be NULL */
    int32_t Menu;                   /* RIX_MENU_* */
    int32_t Page;                   /* RIX_PAGE_* (main menu only) */
    int32_t Skin;                   /* RIX_ITEM_* (main menu) or RIX_OPTION_* (options): whose artwork the button uses; read when the menu is built */
    RIX_ButtonCallback OnPress;     /* may be NULL */
    void* User;                     /* handed back to OnPress */
} RIX_ButtonDesc;

/* All functions return 0 / -1 on failure, and are safe to call from any thread. */

RIX_API int __cdecl RIX_GetVersion(void);

/* Adds a button to the menu named by Button->Menu. Returns its id (13 to 99 on the main menu, 100 and up on the options menu),
   or -1 when the description is invalid. */
RIX_API int __cdecl RIX_AddMainMenuButton(const RIX_ButtonDesc* Button);

/* Removes a button (1) or reports there is no such button (0). The id is not reused. */
RIX_API int __cdecl RIX_RemoveMainMenuButton(int Id);

/* Replaces label, description, page and callback of a button (1), or 0 when there is no such button. */
RIX_API int __cdecl RIX_UpdateMainMenuButton(int Id, const RIX_ButtonDesc* Button);

/* Reads a button back. Label and Description point at storage that stays valid until the next update of that button. */
RIX_API int __cdecl RIX_GetMainMenuButton(int Id, RIX_ButtonDesc* Out);

/* Names an action so menu files (Yu-Gi-Oh-Ex/menus/*.json) can run your code: { "action": { "call": "mymod.doThing" } }.
   Registering the same name again replaces it. The callback gets ButtonId = -1 when it is not run by a button. */
RIX_API int __cdecl RIX_RegisterAction(const char* Name, RIX_ButtonCallback Callback, void* User);

/* The buttons that are currently added (not removed), by position 0..count-1. */
RIX_API int __cdecl RIX_GetMainMenuButtonCount(void);
RIX_API int __cdecl RIX_GetMainMenuButtonIdAt(int Index);

/* Presses one of the 13 buttons the game ships (RIX_ITEM_*) as if the player had; only works while the main menu is open. */
RIX_API int __cdecl RIX_PressMainMenuItem(int Item);

RIX_API int __cdecl RIX_IsMainMenuOpen(void);

/* The screen the game is showing (RIX_SCREEN_*), or -1 when unknown. */
RIX_API int __cdecl RIX_GetCurrentScreenId(void);

/* Changes screen with the game's fade. Best called from a button callback (the game's thread). */
RIX_API int __cdecl RIX_GotoScreen(int ScreenId);

/* Makes one of the 13 buttons the game ships (RIX_ITEM_*) run Callback instead of what it normally does. A locked button (Duelist
   Challenge, Battle Pack, Card Shop before they are unlocked) still shows the game's "locked" message. Callback NULL restores the game's action. */
RIX_API int __cdecl RIX_SetMainMenuItemAction(int Item, RIX_ButtonCallback Callback, void* User);

/* Pages: screens of your own, shown on the Battle Pack screen (its background and look). A page is a menu (a header and up to
   RIX_PAGE_MAX_BUTTONS buttons), a page of your own widgets (ButtonCount 0: you build them on the Screen handed to OnShow, and
   OnFrame runs every frame with the input), or both (your widgets with buttons under them: the buttons take confirm and Back, OnFrame
   gets the input first; take a mouse click yourself only when it is on one of your widgets). Opening a page from a page puts it on top, in place; Back (Esc / Backspace / the pad's cancel)
   closes the top page, and closing the last one returns to the screen the first was opened from. A screen opened from a page (with
   RIX_GotoScreen) comes back to that page. */
#define RIX_PAGE_MAX_BUTTONS 5

typedef struct RIX_PageButton
{
    const wchar_t* Label;           /* copied */
    const wchar_t* Description;     /* shown while the button is highlighted (copied); may be NULL */
    RIX_ButtonCallback OnPress;     /* ButtonId is the button's index; may be NULL */
    void* User;
} RIX_PageButton;

/* Screen is the game's screen object the page is on (RIX::ScreenBattlePackStore). Your widgets hang off its root node (see YuGiOh-RIX.h). */
typedef void(__cdecl* RIX_PageCallback)(void* Screen, void* User);
/* Pressed: buttons pressed this frame (with auto-repeat), Held: buttons held; masks as the game uses them (1 up, 2 down, 4 left, 8 right,
   0x1000 confirm). Seconds: the frame time. Cancel is handled by RIX (it closes the page) and never reaches OnFrame. */
typedef void(__cdecl* RIX_PageFrameCallback)(void* Screen, int Pressed, int Held, float Seconds, void* User);

typedef struct RIX_PageDesc
{
    uint32_t Size;                  /* sizeof(RIX_PageDesc) */
    const wchar_t* Header;          /* the title at the top (copied) */
    int32_t ButtonCount;            /* 1..RIX_PAGE_MAX_BUTTONS for a menu, 0 for a page of your own widgets */
    RIX_PageButton Buttons[RIX_PAGE_MAX_BUTTONS];
    RIX_PageCallback OnShow;        /* the page is showing (again): build your widgets the first time, show them; may be NULL */
    RIX_PageCallback OnHide;        /* covered by another page, closed, or the screen is left: hide them; may be NULL */
    RIX_PageFrameCallback OnFrame;  /* every frame while the page is on top; may be NULL */
    void* User;                     /* handed to the three callbacks */
    float ButtonsY;                 /* where the first button is (screen pixels, 1080 high); 0 = the buttons centred on the screen */
    float ButtonsX;                 /* the buttons' centre (screen pixels, 1920 wide); 0 = the middle of the screen */
} RIX_PageDesc;

/* Opens a page (1) or fails (0). From a main menu button (or any screen) it goes to the Battle Pack screen; from a page's button or
   callback it opens on top of that page. Call it on the game's thread (a callback). */
RIX_API int __cdecl RIX_OpenPage(const RIX_PageDesc* Page);

/* Closes the top page, like Back (1), or 0 when no page is open. */
RIX_API int __cdecl RIX_ClosePage(void);

/* Changes button Index (0-based) of the top page - label, description, callback - and redraws it, keeping that button highlighted (1), or 0
   when no page is open or there is no such button. For toggles such as "Partner: AI" -> "Partner: Human". Call it on the game's thread. */
RIX_API int __cdecl RIX_UpdatePageButton(int Index, const RIX_PageButton* Button);

#ifdef __cplusplus
}

#ifndef RIX_NO_CLIENT
#include <windows.h>

// A small client for C++ plugins that would rather not link to the DLL: it looks the functions up in the module the loader already loaded.
namespace RIX
{
    struct Api
    {
        int(__cdecl* GetVersion)() = nullptr;
        int(__cdecl* AddMainMenuButton)(const RIX_ButtonDesc*) = nullptr;
        int(__cdecl* RemoveMainMenuButton)(int) = nullptr;
        int(__cdecl* UpdateMainMenuButton)(int, const RIX_ButtonDesc*) = nullptr;
        int(__cdecl* GetMainMenuButton)(int, RIX_ButtonDesc*) = nullptr;
        int(__cdecl* RegisterAction)(const char*, RIX_ButtonCallback, void*) = nullptr;
        int(__cdecl* GetMainMenuButtonCount)() = nullptr;
        int(__cdecl* GetMainMenuButtonIdAt)(int) = nullptr;
        int(__cdecl* PressMainMenuItem)(int) = nullptr;
        int(__cdecl* IsMainMenuOpen)() = nullptr;
        int(__cdecl* GetCurrentScreenId)() = nullptr;
        int(__cdecl* GotoScreen)(int) = nullptr;
        int(__cdecl* SetMainMenuItemAction)(int, RIX_ButtonCallback, void*) = nullptr;
        int(__cdecl* OpenPage)(const RIX_PageDesc*) = nullptr;
        int(__cdecl* ClosePage)() = nullptr;
        int(__cdecl* UpdatePageButton)(int, const RIX_PageButton*) = nullptr;
    };

    inline Api& Functions()
    {
        static Api api;
        return api;
    }

    // True when Yu-Gi-Oh-RIX.dll is loaded and speaks this version of the interface.
    inline bool Load()
    {
        Api& api = Functions();
        if (api.GetVersion)
            return true;

        HMODULE module = GetModuleHandleA("Yu-Gi-Oh-RIX.dll");
        if (!module)
            return false;

#define RIX_BIND(name) api.name = reinterpret_cast<decltype(api.name)>(GetProcAddress(module, "RIX_" #name))
        RIX_BIND(GetVersion);
        if (!api.GetVersion || api.GetVersion() != RIX_API_VERSION)
        {
            api.GetVersion = nullptr;
            return false;
        }
        RIX_BIND(AddMainMenuButton);
        RIX_BIND(RemoveMainMenuButton);
        RIX_BIND(UpdateMainMenuButton);
        RIX_BIND(GetMainMenuButton);
        RIX_BIND(RegisterAction);
        RIX_BIND(GetMainMenuButtonCount);
        RIX_BIND(GetMainMenuButtonIdAt);
        RIX_BIND(PressMainMenuItem);
        RIX_BIND(IsMainMenuOpen);
        RIX_BIND(GetCurrentScreenId);
        RIX_BIND(GotoScreen);
        RIX_BIND(SetMainMenuItemAction);
        RIX_BIND(OpenPage);
        RIX_BIND(ClosePage);
        RIX_BIND(UpdatePageButton);
#undef RIX_BIND
        return true;
    }

    inline RIX_ButtonDesc Describe(const wchar_t* label, const wchar_t* description, int page, RIX_ButtonCallback onPress, void* user = nullptr, int skin = RIX_ITEM_DECK_EDITOR, int menu = RIX_MENU_MAIN)
    {
        RIX_ButtonDesc button{};
        button.Size = sizeof(button);
        button.Label = label;
        button.Description = description;
        button.Menu = menu;
        button.Page = page;
        button.Skin = skin;
        button.OnPress = onPress;
        button.User = user;
        return button;
    }

    inline int AddMainMenuButton(const RIX_ButtonDesc* button) { return Functions().AddMainMenuButton(button); }
    inline int RemoveMainMenuButton(int id) { return Functions().RemoveMainMenuButton(id); }
    inline int UpdateMainMenuButton(int id, const RIX_ButtonDesc* button) { return Functions().UpdateMainMenuButton(id, button); }
}
#endif
#endif
