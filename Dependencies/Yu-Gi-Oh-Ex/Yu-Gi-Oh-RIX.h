#pragma once
/*
    Yu-Gi-Oh-RIX - the public interface of the menu plugin (Yu-Gi-Oh-RIX.dll).

    The one RIX header. The first part is the DLL's API - functions Yu-Gi-Oh-RIX.dll exports, found at run time with GetProcAddress
    (RIX::Load below) - and the one plugins use for menus, pages and widgets on screens. The last part (YGO::RIX::, C++ only) is raw
    addresses of the game's own UI code ("RIX" is the game's UI framework), which Yu-Gi-Oh-RIX itself is built on; use it only to reach
    into a game screen's internals that the API doesn't cover (Yu-Gi-Oh-Campaign's deck picker steps, for one).

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

#define RIX_API_VERSION 7   /* 5: pages hold 5 buttons (RIX_PageDesc grew), RIX_UpdatePageButton. 6: RIX_AddImage / RIX_AddText / RIX_RemoveNode. 7: the page engine (RIX_OpenPageFile, page variables, list sources) */

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

/* Adds an options entry beside a loaded plugin in Help & Options > Plugins.
   Key is the Core plugin key. Strings are copied; registering again replaces the entry.
   Call on the game thread before opening the plugin list. Returns 1 on success. */
RIX_API int __cdecl RIX_RegisterPluginOptions(const char* Key, const wchar_t* Label, const wchar_t* Description, RIX_ButtonCallback Callback, void* User);

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

/* Screen is the game's screen object the page is on (RIX::ScreenBattlePackStore). Your widgets hang off its root node (see the game part at the end of this header). */
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

/* Widgets on any of the game's screens (Screen = the game's screen object, e.g. the one a page callback or a screen hook gets), in screen
   pixels (1920 x 1080). Each returns a handle (NULL on failure: no such sheet / sprite) that owns the widget: take it off with
   RIX_RemoveNode before the screen goes. Call them on the game's thread.
   RIX_AddImage: one sprite of a sheet (e.g. "pdui/chars", "<character key>_neutral"), top-left at X, Y, stretched to Width x Height
                 (0 = its own size). A negative Width / Height flips the sprite that way and keeps it in the same box (X..X+|Width|,
                 Y..Y+|Height|), e.g. an up arrow from "pdui/doShared" "arrow_1" (which points down). Added in version 6 without a version
                 change: an older RIX ignores a negative size (the sprite is not flipped).
                 Only sprites of a .dfymoo sheet load: a loose PNG (pdui\shared\scrollbar_*.png) is not found, and passing a PNG's path as
                 the Resource crashes the game's layer loader (YuGiOh.exe+0x747A98).
   RIX_AddText:  text in the game's UI font, PixelSize high. Width 0 = one line, X is its left / centre / right edge (Align); Width > 0 =
                 a box Width wide, text wrapped and aligned in it. Colour is ARGB. The text is copied. */
#define RIX_ALIGN_LEFT 0
#define RIX_ALIGN_CENTRE 1
#define RIX_ALIGN_RIGHT 2
RIX_API void* __cdecl RIX_AddImage(void* Screen, const char* Resource, const char* Sprite, int Z, float X, float Y, float Width, float Height);
RIX_API void* __cdecl RIX_AddText(void* Screen, const wchar_t* Text, int Z, float X, float Y, float Width, float PixelSize, int Align, uint32_t Colour);
RIX_API void __cdecl RIX_RemoveNode(void* Node);

/* The page engine (version 7, docs/PageEngine.md): page files with "version": 2 open on RIX's own screen, built from the game's widgets.
   RIX_OpenPageFile: opens Yu-Gi-Oh-Ex/pages/<Name>.json (a version 1 page opens the old way). Call it on the game's thread.
   Page variables: what "var:<name>" bindings and "{var:<name>}" texts read; a menu with an "id" sets the variable of that name to the
   chosen item's value. Values are UTF-8 strings. RIX_GetPageVar copies the value into Buffer and returns its length (0 when it is empty or not set).
   List sources: a menu with "source": "<Name>" gets one item per RIX_PageListAdd the callback makes (called each time the page is built). */
typedef void(__cdecl* RIX_PageListCallback)(void* List, void* User);
RIX_API int __cdecl RIX_OpenPageFile(const char* Name);
RIX_API void __cdecl RIX_SetPageVar(const char* Name, const char* Value);
RIX_API int __cdecl RIX_GetPageVar(const char* Name, char* Buffer, int BufferSize);
RIX_API int __cdecl RIX_RegisterPageList(const char* Name, RIX_PageListCallback Callback, void* User);
RIX_API void __cdecl RIX_PageListAdd(void* List, const wchar_t* Label, const wchar_t* Description, const char* Value);

/* Page values (added to version 7 on 2026-10-19, so an older RIX may lack it: RIX::Functions().RegisterPageValue can be null).
   "{game:<Name>.<Argument>}" in a page (and "game:<Name>.<Argument>" in conditions) calls Callback(Argument, Buffer, BufferSize, User),
   which writes the value as UTF-8 and returns its length (0 = no value). Called on the game's thread, every frame a page shows it.
   BufferSize is 16384 since 2026-10-25 (512 before): enough for a comma list of a few thousand card ids. */
typedef int(__cdecl* RIX_PageValueCallback)(const char* Argument, char* Buffer, int BufferSize, void* User);
RIX_API int __cdecl RIX_RegisterPageValue(const char* Name, RIX_PageValueCallback Callback, void* User);

/* Page folders (added to version 7 on 2026-10-25, so an older RIX may lack it: RIX::Functions().AddPageFolder can be null). A plugin that
   ships its own pages (e.g. Plugins\<plugin>\pages) adds that folder once. A page name is looked for in the active mods' pages, then
   Yu-Gi-Oh-Ex/pages, then these folders in the order they were added: a copy in Yu-Gi-Oh-Ex/pages or a mod overrides a plugin's page.
   Returns 1, or 0 for an empty name. */
RIX_API int __cdecl RIX_AddPageFolder(const wchar_t* Folder);

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
        int(__cdecl* RegisterPluginOptions)(const char*, const wchar_t*, const wchar_t*, RIX_ButtonCallback, void*) = nullptr;
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
        void*(__cdecl* AddImage)(void*, const char*, const char*, int, float, float, float, float) = nullptr;
        void*(__cdecl* AddText)(void*, const wchar_t*, int, float, float, float, float, int, uint32_t) = nullptr;
        void(__cdecl* RemoveNode)(void*) = nullptr;
        int(__cdecl* OpenPageFile)(const char*) = nullptr;
        void(__cdecl* SetPageVar)(const char*, const char*) = nullptr;
        int(__cdecl* GetPageVar)(const char*, char*, int) = nullptr;
        int(__cdecl* RegisterPageList)(const char*, RIX_PageListCallback, void*) = nullptr;
        void(__cdecl* PageListAdd)(void*, const wchar_t*, const wchar_t*, const char*) = nullptr;
        int(__cdecl* RegisterPageValue)(const char*, RIX_PageValueCallback, void*) = nullptr;   // may be null (an older RIX)
        int(__cdecl* AddPageFolder)(const wchar_t*) = nullptr;                                     // may be null (an older RIX)
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
        RIX_BIND(RegisterPluginOptions);
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
        RIX_BIND(AddImage);
        RIX_BIND(AddText);
        RIX_BIND(RemoveNode);
        RIX_BIND(OpenPageFile);
        RIX_BIND(SetPageVar);
        RIX_BIND(GetPageVar);
        RIX_BIND(RegisterPageList);
        RIX_BIND(PageListAdd);
        RIX_BIND(RegisterPageValue);
        RIX_BIND(AddPageFolder);
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

/*
    ---- The game's own UI framework (C++ only) ----

    Everything above is Yu-Gi-Oh-RIX.dll's API (RIX:: and the RIX_ C exports): what plugins use for menus, pages and widgets on
    screens. Everything below (YGO::RIX::) is raw game addresses of the game's own UI classes, which Yu-Gi-Oh-RIX itself is built on.
    Reach for it only for a game screen's internals the API doesn't cover (a screen's state machine, its deck picker, its help bar).
    It was its own header, Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-RIX.h, until 2026-10-21. Define RIX_NO_GAME to leave it out.
*/
#if defined(__cplusplus) && !defined(RIX_NO_GAME)
#include <cstddef>
#include <cstdint>
#include <intrin.h>
#include <functional>
#include <iterator>

// The game's UI framework ("RIX"): a screen manager that switches between screens (ScreenMainMenu, ScreenHelp...) and a small widget
// kit (MenuKit) the menus are made of. Everything here was traced in YuGiOh.exe.i64 (names and types are in the IDB too).
//
namespace YGO
{
    namespace RIX
    {
        // ScreenIds are in YuGiOh-UI.h (YGO::UI::RIX::ScreenID).

        // std::vector as the game lays it out (MSVC): begin, end, end of capacity.
        struct Vector
        {
            void* Begin;
            void* End;
            void* Capacity;
        };

        // One button of the main menu, see g_MainMenuItemTable (0x140A76680). LabelText and DescriptionText are a string id (1..1213) or,
        // when larger than 2214, a raw `const wchar_t*` that is shown as it is.
        struct MainMenuItemDef
        {
            int64_t Id;
            int64_t LabelText;
            int64_t DescriptionText;
        };

        // The ids of the 13 buttons the game ships.
        enum MainMenuItem : int
        {
            MMI_SINGLE_PLAYER_MENU = 0, // opens page 1
            MMI_SOLO_DUEL = 1,          // -> DUEL_SELECT
            MMI_DUELIST_CHALLENGE = 2,  // -> DUELIST_CHALLENGE, needs save flag DUELIST_CHALLENGE
            MMI_MULTIPLAYER_MENU = 3,   // opens page 2
            MMI_MULTIPLAYER_A = 4,      // -> PLAYER_MATCH
            MMI_MULTIPLAYER_B = 5,      // -> PLAYER_MATCH
            MMI_LEADERBOARD = 6,        // -> LEADERBOARD
            MMI_BATTLE_PACK = 7,        // -> BATTLEPACK, needs save flag BATTLE_PACKS
            MMI_DECK_EDITOR = 8,        // -> DECK_EDITOR
            MMI_CARD_SHOP = 9,          // -> CARD_SHOP, needs save flag CARD_SHOP
            MMI_HELP_AND_OPTIONS = 10,  // -> HELP_AND_OPTIONS
            MMI_TUTORIALS = 11,         // -> TUTORIAL_LIST
            MMI_QUIT_GAME = 12,
            MMI_VANILLA_COUNT = 13,
        };

        // The three pages of the main menu (each is a vector<int> of item ids, in on-screen order).
        enum MainMenuPage : int
        {
            MMP_MAIN = 0,          // 0, 3, 7, 8, 9, 10, 12
            MMP_SINGLE_PLAYER = 1, // 1, 2, 11
            MMP_MULTIPLAYER = 2,   // 4, 5, 6
            MMP_COUNT = 3,
        };

        // A menu button widget (424 bytes). Only the traced fields are named.
        struct WidgetItem
        {
            void* VFTable;             // +0x00
            char pad_08[8];
            void* Node;                // +0x10 layout node (its Y is set by LayoutPage)
            char pad_18[24];
            char Block_30[16];
            void* LabelText;           // +0x40 the label's text widget
            char pad_48[40];
            void* LockIcon;            // +0x70
            char pad_78[296];
            char Locked;               // +0x1A0
            char pad_1A1[7];
        };
        static_assert(sizeof(WidgetItem) == 424);

        // What has been traced of RIX::ScreenMainMenu.
        struct ScreenMainMenu
        {
            void* VFTable;             // +0x000 RIX::ScreenMainMenu::vftable (0x140A765C0)
            char pad_008[144];
            char HeaderWidget[112];    // +0x098
            char DescriptionWidget[392]; // +0x108 (the text shown for the highlighted button)
            Vector Items;              // +0x290 vector<WidgetItem>, 13 of them
            char Cursor[56];           // +0x2A8 the selected slot is the first int
            Vector Defs;               // +0x2E0 vector<MainMenuItemDef>, 13 of them
            Vector Pages[MMP_COUNT];   // +0x2F8 vector<int> of item ids per page
            int CurrentPage;           // +0x340
            int PendingItem;           // +0x344 item pressed this frame, -1 when none
        };
        static_assert(offsetof(ScreenMainMenu, Items) == 0x290);
        static_assert(offsetof(ScreenMainMenu, Defs) == 0x2E0);
        static_assert(offsetof(ScreenMainMenu, Pages) == 0x2F8);
        static_assert(offsetof(ScreenMainMenu, CurrentPage) == 0x340);
        static_assert(offsetof(ScreenMainMenu, PendingItem) == 0x344);

        // ---- functions ----

        inline auto GetUI = reinterpret_cast<int64_t(__fastcall*)()>(0x1408121B0);

        // Switch to a screen (a YGO::UI::RIX::ScreenID) with a fade of `FadeSeconds`. The game itself passes 0.15, 273, 1.
        inline auto NavigateToScreen = reinterpret_cast<char(__fastcall*)(int64_t UI, int ScreenId, double FadeSeconds, int Flags, char Force)>(0x1408087A0);

        // Switch to a screen on the next frame with no fade (RIX::UI::SetPendingScreen): how the title's Play hands off to ScreenSignIn (6),
        // which then fades into the main menu itself. Going to SignIn with NavigateToScreen / GotoScreen fades twice (out, then in again).
        inline auto SetPendingScreen = reinterpret_cast<void(__fastcall*)(int64_t UI, int ScreenId)>(0x1408085E0);

        inline auto PlayUISound = reinterpret_cast<void(__fastcall*)(int SoundId)>(0x14086C280);

        // Changes screen the way the game's own buttons do (RIX::Screen::GotoScreen): `FromScreen` is the screen object you are leaving. Besides
        // navigating it records where you came from in the target screen, which is what its Back button returns to. Calling NavigateToScreen
        // alone skips that, so Back leads nowhere.
        // The screen's own Yes/No box (see docs/Widgets.md): opens it on Screen with the given text. Text is a string id or a wide string that
        // must stay valid while the box is open. OnYes is taken over by the game (moved from and freed by it), so hand it a std::function
        // that fits in the small buffer (a plain function pointer does); No only closes the box. Sfx -1 = default.
        inline auto ShowYesNo = reinterpret_cast<void(__fastcall*)(void* Screen, int Sfx, const wchar_t* Text, std::function<void()>* OnYes)>(0x1408229D0);
        // The help bar at the bottom of a screen (widget_Help, at Screen + 264): a list of button prompts. An entry is the input mask of the button
        // (0x2000 cancel, 0x1000 confirm), 1, and a string id. Clear, add entries, then Layout to show them.
        struct HelpEntry
        {
            int32_t Mask;
            int32_t Alt;
            int64_t TextId;
        };
        inline auto HelpClear = reinterpret_cast<void(__fastcall*)(void* Help)>(0x14089F630);
        inline auto HelpAdd = reinterpret_cast<void(__fastcall*)(void* Help, const HelpEntry* Entry, unsigned char Flag)>(0x14089E9D0);
        inline auto HelpLayout = reinterpret_cast<void(__fastcall*)(void* Help)>(0x14089EC90);

        // The game's input state and its "cancel pressed" query (Esc / Backspace / the pad's cancel): returns the mask when it was pressed.
        inline void* const InputState = reinterpret_cast<void*>(0x142924010);
        inline auto InputCancelPressed = reinterpret_cast<int64_t(__fastcall*)(void* Input, unsigned int Mask)>(0x1408007E0);

        // The pieces ShowYesNo is made of, for a box whose No button does something too (the dialog object is Screen + 432).
        // Order: Clear, SetMode(1), SetText, AddItem(918 = Yes) and AddItem(900 = No), then Show(true) and Screen + 48 = 1. Both AddItem calls take
        // over their std::function like ShowYesNo does.
        inline auto DialogClear = reinterpret_cast<void(__fastcall*)(void* Dialog)>(0x1408987F0);
        inline auto DialogSetMode = reinterpret_cast<void(__fastcall*)(void* Dialog, int Mode)>(0x1408988A0);
        inline auto DialogSetText = reinterpret_cast<void(__fastcall*)(void* Dialog, const wchar_t* Text)>(0x1408988E0);
        inline auto DialogAddItem = reinterpret_cast<void(__fastcall*)(void* Dialog, int64_t LabelId, std::function<void()>* OnPress, int Sfx)>(0x140897BC0);
        constexpr int DialogLabelYes = 918;
        constexpr int DialogLabelNo = 900;
        inline auto GotoScreenFrom = reinterpret_cast<char(__fastcall*)(void* FromScreen, int ScreenId)>(0x1408227A0);

        // std::map<int screenId, ScreenBase*> of every screen object (g_ScreenMap); the variable holds the map's head node. MSVC node layout:
        // left +0, parent +8, right +16, isnil +25, key +32, value +40.
        inline void* FindScreenObject(int ScreenId)
        {
            auto head = *reinterpret_cast<uint8_t**>(0x143327FE8);
            if (!head)
                return nullptr;

            uint8_t* best = head;
            uint8_t* node = *reinterpret_cast<uint8_t**>(head + 8);
            while (node && !node[25])
            {
                if (*reinterpret_cast<int*>(node + 32) >= ScreenId)
                {
                    best = node;
                    node = *reinterpret_cast<uint8_t**>(node);
                }
                else
                    node = *reinterpret_cast<uint8_t**>(node + 16);
            }

            if (best == head || ScreenId < *reinterpret_cast<int*>(best + 32))
                return nullptr;
            return *reinterpret_cast<void**>(best + 40);
        }

        // Quitting: the main menu's Quit button just calls RequestQuit(GetGlobalInstance()), which sets a flag (instance + 0xB98) the main loop watches.
        inline auto GetGlobalInstance = reinterpret_cast<void*(__fastcall*)()>(0x1408121A0);
        inline auto RequestQuit = reinterpret_cast<void(__fastcall*)(void* Instance)>(0x1408189C0);

        // The state of the screen manager: index into the record array, records are 48 bytes with the screen id at +8.
        inline int CurrentScreenId()
        {
            int64_t ui = GetUI();
            int index = *reinterpret_cast<int*>(ui + 852);
            int64_t records = *reinterpret_cast<int64_t*>(ui + 856);
            // the screen record: +0 current id, +4 previous id, +8 pending id (RIX::UI::GetCurrentScreenId 0x140807F80 reads +0;
            // this read +8 until 2026-10-14, which is 0 once a screen change has finished)
            return records ? *reinterpret_cast<int*>(records + 48LL * index) : -1;
        }

        // The MenuKit menu object every ScreenBaseMenu (options, pause, live menu) owns at screen+664.
        namespace MenuKit
        {
            inline uintptr_t SetDefinition = 0x14080A210;   // (menu, MainMenuItemDef* table): items from a table that ends with id -1
            inline uintptr_t ShowItem = 0x140809580;        // (menu, id): appends to the visible list (vector<int> at menu+88)
            inline uintptr_t ItemCreateFromLayout = 0x140809CC0;
            inline uintptr_t CreateItems = 0x140809E90;
            constexpr uintptr_t CreateItemsEnd = 0x140809FE4;

            inline auto ShowItemNow = reinterpret_cast<uint64_t(__fastcall*)(void* Menu, unsigned int Id)>(ShowItem);
        }

        // The Help & Options screen (RIX::ScreenHelp, screen id 12): a ScreenBaseMenu with 9 defined items (0..8).
        namespace HelpMenu
        {
            constexpr uintptr_t ItemTable = 0x140A74200;    // g_HelpMenuItemTable
            constexpr int VanillaItemCount = 9;
            constexpr int BackItem = 7;                     // always shown last
            inline uintptr_t OnEnter = 0x140841EE0;         // decides which items are shown, in order
            constexpr uintptr_t OnEnterEnd = 0x140841FC0;
            inline uintptr_t OnItemActivated = 0x140841CD0; // (listener, ui, itemId, profile); -2 = back
        }

        namespace MainMenu
        {
            inline uintptr_t SetupArrays = 0x140857FA0;      // fills the three page vectors
            inline uintptr_t LoadScreenTable = 0x140857DA0;  // g_MainMenuItemTable -> Defs
            inline uintptr_t ActivateItem = 0x140856C40;     // a button was pressed
            inline uintptr_t Update = 0x140857840;           // per frame
            inline uintptr_t LayoutPage = 0x1408583D0;
            inline uintptr_t CreateFromLayout = 0x1408A1260; // RIX::MenuKit::widget_Item::CreateFromLayout

            // Grow helpers (they allocate, move the old items and set End to Begin + NewSize).
            // ReallocateItems takes a third argument: a pointer to a byte the game reads for every new item (it passes the address of a
            // zero byte). Leaving it out crashes on the read of garbage (found from the crash log at 0x14084E1D0).
            inline auto ReallocateItems = reinterpret_cast<void(__fastcall*)(Vector* Items, size_t NewSize, const char* Value)>(0x14084E0C0);
            inline auto ReallocateDefs = reinterpret_cast<void(__fastcall*)(Vector* Defs, size_t NewSize)>(0x1407AE2A0);
            inline auto VectorIntEmplaceReallocate = reinterpret_cast<void(__fastcall*)(Vector* Vec, void* Where, const int* Value)>(0x140746A20);

            inline auto SetEnabled = reinterpret_cast<void(__fastcall*)(void* Widget, char Enabled)>(0x14075A490);

            // `cmp r15d, 0Dh` closing the widget creation loop of ScreenMainMenu::SetupWidgets: the 13 is the byte at +3.
            constexpr uintptr_t SetupWidgetsLoopCount = 0x140857D17;
            constexpr uintptr_t SetupWidgetsBegin = 0x1408579B0;
            constexpr uintptr_t SetupWidgetsEnd = 0x140857DA0;

            inline void LayoutPageNow(ScreenMainMenu* Screen, bool Animate)
            {
                reinterpret_cast<int64_t(__fastcall*)(ScreenMainMenu*, char)>(LayoutPage)(Screen, Animate);
            }
        }

        // ---- more MenuKit menu functions (menu = the object a ScreenBaseMenu or ScreenBattlePackStore owns)
        namespace MenuKit
        {
            inline auto ClearVisible = reinterpret_cast<void(__fastcall*)(void* Menu)>(0x14080A4C0);        // empties the visible list (menu+88)
            inline auto Layout = reinterpret_cast<void(__fastcall*)(void* Menu)>(0x14080A930);              // hides every item, then shows and labels the visible ones
            inline auto SelectIndex = reinterpret_cast<void(__fastcall*)(void* Menu, unsigned int Index)>(0x14080A5D0);
            inline auto ResetItems = reinterpret_cast<void(__fastcall*)(void* Menu)>(0x14080A000);          // resets the item animations, then Layout
        }

        // RIX::Screen::GoBack: back to the screen this one was entered from (screen+60). RIX::Screen::SetHeaderText: the title at the top.
        inline auto GoBack = reinterpret_cast<void(__fastcall*)(void* Screen)>(0x140822750);

        // RIX::Screen::ShowMessageText: the screen's message box with a text (kept by the caller while the box is open). Sfx -1 = none (71 is
        // the game's error sound). OnOk is a std::function<void()>* the game takes over (moves from):
        //  - a real one (MSVC's std::function is 64 bytes with its callable at +56, the same in the game and our builds) adds an OK button
        //    (text 901) that highlights under the mouse and runs the function when pressed; Back also closes the box;
        //  - an empty one (EmptyFunction: 64 zero bytes) adds no button: the box shows OK as a prompt only, and any confirm / cancel / click
        //    closes it (the game's own locked-button messages are like that - nothing highlights).
        // Screen::Tick sends the input to the box while it is open, so the screen's own Update is paused.
        struct EmptyFunction
        {
            uint8_t Storage[56] = {};
            void* Impl = nullptr;
        };
        static_assert(sizeof(std::function<void()>) == sizeof(EmptyFunction), "std::function is not the layout the game uses");
        inline auto ShowMessageText = reinterpret_cast<void(__fastcall*)(void* Screen, int Sfx, const wchar_t* Text, void* OnOk)>(0x140822BA0);
        inline auto SetHeaderText = reinterpret_cast<void(__fastcall*)(void* Screen, int64_t Text)>(0x140822F00);

        // ---- input (the object is InputState). Masks: 1 up, 2 down, 4 left, 8 right, 0x1000 confirm, 0x2000 cancel.
        namespace Input
        {
            inline auto GetPressed = reinterpret_cast<int(__fastcall*)(void* Input)>(0x1408001B0);    // pressed this frame
            inline auto GetRepeat = reinterpret_cast<int(__fastcall*)(void* Input)>(0x140800290);     // auto-repeat of held directions
            inline auto GetHeld = reinterpret_cast<int(__fastcall*)(void* Input)>(0x140800340);       // held (the trunk and card info take it as their second mask)
            inline auto HelpBarPressed = reinterpret_cast<int(__fastcall*)(void* Help)>(0x14089F410);  // a prompt on the help bar (screen+264) was clicked

            // The mouse as the game keeps it (in InputState): x and y (two ints) at +0x60, "the mouse is in use" at +0x68 (the game selects what
            // is under it then - hover, not a click), a click this frame at bit 0 of +0x6C.
            inline int64_t MousePosition() { return *reinterpret_cast<int64_t*>(0x142924070); }
            inline bool MouseActive() { return *reinterpret_cast<int*>(0x142924078) != 0; }
            inline bool MouseClickPending() { return (*reinterpret_cast<uint8_t*>(0x14292407C) & 1) != 0; }
            // Returns Mask when the mouse was clicked this frame and consumes the click (menus use it with 0x1000 as "confirm by mouse").
            inline auto TakeMouseClick = reinterpret_cast<int(__fastcall*)(void* Input, unsigned int Mask)>(0x1408006B0);
        }

        // ---- widgets. Every widget is built the same way: construct it in memory of your own (zeroed, 16 byte aligned, kept while the screen
        // lives), then CreateFromLayout(widget, &parent, z, owner, x, y[, screenData]) where parent is a counted copy of the screen's root node (the
        // shared_ptr at screen+72), owner is screen+88 and screenData screen+120. Nothing is read from a layout file
        // (RIX::widget_Base::CreateNode, 0x14087C030). vftable slot 1 = SetFocused(bool) (highlight), slot 3 = SetVisible(bool).
        struct SharedNode
        {
            void* Node;
            void* Control;
        };

        // The parent is a std::shared_ptr passed BY VALUE: CreateFromLayout releases it before returning, exactly as the game's callers expect
        // (they always pass a fresh copy). So never pass the screen's own shared_ptr - pass a copy from ParentRef (one reference each call),
        // or the screen's root loses a reference per widget and is freed while in use (heap corruption, found 2026-09-28).
        inline SharedNode* ScreenRoot(void* Screen) { return reinterpret_cast<SharedNode*>(static_cast<char*>(Screen) + 72); }
        inline SharedNode ParentRef(const SharedNode* Parent)
        {
            SharedNode copy = *Parent;
            if (copy.Control)
                _InterlockedIncrement(reinterpret_cast<volatile long*>(static_cast<char*>(copy.Control) + 8));   // the use count
            return copy;
        }
        inline void* ScreenOwner(void* Screen) { return static_cast<char*>(Screen) + 88; }
        inline void* ScreenData(void* Screen) { return static_cast<char*>(Screen) + 120; }

        // A scene node's position (RIX::Node::SetX / SetY / SetPosition: floats at node+88 / +92, relative to its parent).
        inline auto NodeSetX = reinterpret_cast<void(__fastcall*)(void* Node, float X)>(0x14075A2F0);
        inline auto NodeSetY = reinterpret_cast<void(__fastcall*)(void* Node, float Y)>(0x14075A310);
        inline auto NodeSetPosition = reinterpret_cast<void(__fastcall*)(void* Node, float X, float Y)>(0x14075A300);

        // Drops one reference of a shared_ptr the plugin owns (what std::shared_ptr's destructor does: use count at control+8, weak at +12).
        inline void ReleaseRef(SharedNode& Ref)
        {
            if (auto* control = static_cast<char*>(Ref.Control))
            {
                auto** vftable = *reinterpret_cast<void(__fastcall***)(void*)>(control);
                if (_InterlockedExchangeAdd(reinterpret_cast<volatile long*>(control + 8), -1) == 1)
                {
                    vftable[0](control);   // destroy the object
                    if (_InterlockedExchangeAdd(reinterpret_cast<volatile long*>(control + 12), -1) == 1)
                        vftable[1](control);   // free the control block
                }
            }
            Ref = {};
        }

        // ---- plain pictures: a DFX::TLayerAnimoo (one sprite of a sheet) inside a DFX::TBase node, the way the game builds its own
        // (RIX::LayerAnimoo_CreateFromDesc 0x140785190). The sheet is a resource name ("pdui/doShared" = pdui\doShared.png + .dfymoo; loaded on
        // first use by DFX::Resource_FindOrLoad) and the sprite a name in it. Every shared_ptr argument marked "consumed" is taken BY VALUE by
        // the game: pass a counted copy (ParentRef), never a pointer you still own.
        namespace Dfx
        {
            inline auto MakeLayerAnimoo = reinterpret_cast<SharedNode*(__fastcall*)(SharedNode* Out)>(0x14075B580);
            inline auto LayerSetResource = reinterpret_cast<void(__fastcall*)(void* Layer, const char* Resource)>(0x14075BAB0);
            inline auto LayerSelectByName = reinterpret_cast<void(__fastcall*)(void* Layer, const char* Sprite)>(0x14075BB00);
            inline auto LayerGetPlayer = reinterpret_cast<char*(__fastcall*)(void* Layer)>(0x14075B970);   // +16 sprite index, +48 alignment bits
            // (out, parent consumed, content consumed, z, x, y): a new TBase under parent holding content, at x, y. Out holds one reference.
            inline auto MakeChildWithContent = reinterpret_cast<SharedNode*(__fastcall*)(SharedNode* Out, SharedNode* Parent, SharedNode* Content, int Z, float X, float Y)>(0x140744A10);
            inline auto RemoveChild = reinterpret_cast<void(__fastcall*)(void* Parent, SharedNode* Child)>(0x140759DD0);   // child consumed
            inline auto SetScaleXY = reinterpret_cast<void(__fastcall*)(void* Node, float X, float Y)>(0x14075A350);
            inline auto SetAlpha = reinterpret_cast<void(__fastcall*)(void* Node, float Alpha)>(0x14075A1E0);
            inline auto SetFlag = reinterpret_cast<void(__fastcall*)(void* Node, unsigned Mask, char On)>(0x14075A2A0);
            constexpr unsigned FlagVisible = 0x8;

            // The sprite a layer shows: its full (untrimmed) size, or false when the sheet or the name was not found.
            inline bool LayerSpriteSize(void* Layer, int& Width, int& Height)
            {
                auto* resource = *reinterpret_cast<char**>(static_cast<char*>(Layer) + 152);
                if (!resource)
                    return false;
                const int index = *reinterpret_cast<int*>(LayerGetPlayer(Layer) + 16);
                const uint32_t count = *reinterpret_cast<uint32_t*>(resource);
                if (index < 0 || static_cast<uint32_t>(index) >= count)
                    return false;
                const char* entry = *reinterpret_cast<char**>(resource + 8) + 56 * index;
                Width = *reinterpret_cast<const int*>(entry + 24);
                Height = *reinterpret_cast<const int*>(entry + 28);
                return Width > 0 && Height > 0;
            }

            // Puts one sprite on a node (normally ScreenRoot(screen)) with its top-left at X, Y, stretched to Width x Height (0 = its own size).
            // A negative Width / Height flips it that way and keeps it in the same box (X..X+|Width|, Y..Y+|Height|): the node is scaled by
            // a negative factor, which mirrors it about the node's origin, so the node moves to the box's far edge (2026-10-11: an up arrow
            // from doShared's down-pointing arrow_1). Returns the node holding it (the caller owns one reference: take it off with RemoveImage), or an empty SharedNode when the
            // sheet or sprite does not exist.
            inline SharedNode AddImage(const SharedNode* Parent, const char* Resource, const char* Sprite, int Z, float X, float Y, float Width = 0, float Height = 0)
            {
                SharedNode layer{};
                MakeLayerAnimoo(&layer);
                if (!layer.Node)
                    return {};
                LayerSetResource(layer.Node, Resource);
                LayerSelectByName(layer.Node, Sprite);
                *reinterpret_cast<int*>(LayerGetPlayer(layer.Node) + 48) = 0;   // top-left anchored (no alignment bits)

                int w = 0, h = 0;
                if (!LayerSpriteSize(layer.Node, w, h))
                {
                    ReleaseRef(layer);
                    return {};
                }

                // a flipped axis mirrors about the node's origin: start the node at the box's far edge
                const float boxWidth = Width != 0 ? (Width < 0 ? -Width : Width) : static_cast<float>(w);
                const float boxHeight = Height != 0 ? (Height < 0 ? -Height : Height) : static_cast<float>(h);
                SharedNode parent = ParentRef(Parent);
                SharedNode node{};
                MakeChildWithContent(&node, &parent, &layer, Z, Width < 0 ? X + boxWidth : X, Height < 0 ? Y + boxHeight : Y);   // parent and layer consumed
                if (node.Node && (Width != 0 || Height != 0))
                    SetScaleXY(node.Node, Width != 0 ? Width / w : 1.0f, Height != 0 ? Height / h : 1.0f);
                return node;
            }

            // ---- text: a DFX::TLayerText in a TBase node. Its style (DFX::TTextSpec) is at layer+152: +8/+12 colour (ARGB), +24 flags
            // (0x100 no wrap), +28 wrap width, +32 font id, +40 align (1 left 2 centre 4 right: lines sit around x = 0; 8 top). Glyphs are
            // drawn at the font's own pixel size, so bigger text = a bigger font or the node scaled.
            inline auto MakeLayerText = reinterpret_cast<SharedNode*(__fastcall*)(SharedNode* Out)>(0x14075DC60);
            // Text: a string id (1..1213) or a wchar_t* that must stay valid while the node lives (the game re-reads it on every rebuild).
            inline auto LayerSetText = reinterpret_cast<void(__fastcall*)(void* Layer, int64_t IdOrText)>(0x14075E000);

            enum class TextAlign { Left, Centre, Right };

            // The game's UI face (FONT_ID_PD_*): font ids 0..7 at these pixel sizes.
            inline constexpr int PdFontSizes[] = { 88, 44, 32, 23, 20, 16, 14, 12 };

            // Puts text on a node: the box's top-left at X, Y (Width 0 = no box: X is the text's left / centre / right edge), PixelSize high,
            // aligned in the box, wrapped to the box's width when it has one. Text must outlive the node. Returns the node (caller owns one
            // reference, take it off with RemoveImage).
            inline SharedNode AddText(const SharedNode* Parent, const wchar_t* Text, int Z, float X, float Y, float Width, float PixelSize,
                                      TextAlign Align = TextAlign::Left, uint32_t Colour = 0xFFFFFFFF)
            {
                if (!Text || PixelSize <= 0)
                    return {};
                // the smallest PD font that is at least as big (scaling down stays sharp), else the biggest
                int font = 0;
                for (int i = 0; i < static_cast<int>(std::size(PdFontSizes)); ++i)
                    if (PdFontSizes[i] >= PixelSize)
                        font = i;
                const float scale = PixelSize / PdFontSizes[font];

                SharedNode layer{};
                MakeLayerText(&layer);
                if (!layer.Node)
                    return {};
                char* style = static_cast<char*>(layer.Node) + 152;
                *reinterpret_cast<uint32_t*>(style + 8) = Colour;
                *reinterpret_cast<uint32_t*>(style + 12) = Colour;
                *reinterpret_cast<uint32_t*>(style + 24) = Width > 0 ? 0 : 0x100;
                *reinterpret_cast<float*>(style + 28) = Width > 0 ? Width / scale : 0.0f;
                *reinterpret_cast<int*>(style + 32) = font;
                *reinterpret_cast<int*>(style + 40) = 8 | (Align == TextAlign::Left ? 1 : Align == TextAlign::Centre ? 2 : 4);
                LayerSetText(layer.Node, reinterpret_cast<int64_t>(Text));

                const float x = Align == TextAlign::Left ? X : Align == TextAlign::Centre ? X + Width / 2 : X + Width;
                SharedNode parent = ParentRef(Parent);
                SharedNode node{};
                MakeChildWithContent(&node, &parent, &layer, Z, x, Y);   // parent and layer consumed
                if (node.Node && scale != 1.0f)
                    SetScaleXY(node.Node, scale, scale);
                return node;
            }

            // Takes a node made by AddImage or AddText off its parent and drops the caller's reference.
            inline void RemoveImage(const SharedNode* Parent, SharedNode& Node)
            {
                if (!Node.Node)
                    return;
                if (Parent && Parent->Node)
                {
                    SharedNode child = ParentRef(&Node);
                    RemoveChild(Parent->Node, &child);   // consumed
                }
                ReleaseRef(Node);
            }
        }

        inline void WidgetSetFocused(void* Widget, bool On) { (*reinterpret_cast<void(__fastcall***)(void*, char)>(Widget))[1](Widget, On); }
        inline void WidgetSetVisible(void* Widget, bool On) { (*reinterpret_cast<void(__fastcall***)(void*, char)>(Widget))[3](Widget, On); }

        // The card trunk (RIX::widget_TrunkZone): the deck editor's grid of cards with its filter bar. Only the deck editor has one (at +9984).
        namespace Trunk
        {
            constexpr size_t Size = 1632;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Trunk)>(0x1408BE760);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Trunk, SharedNode* Parent, int Z, void* Owner, float X, float Y)>(0x1408BF050);
            inline auto SetDeckInfo = reinterpret_cast<void(__fastcall*)(void* Trunk, void* DeckInfo)>(0x1408C01B0);   // where the grid reads in-deck counts
            inline auto ProcessInput = reinterpret_cast<char(__fastcall*)(void* Trunk, int Pressed, int Held, int* Action, int* Kind, char* Flag)>(0x1408BEB30);
            inline auto Update = reinterpret_cast<void(__fastcall*)(void* Trunk)>(0x1408C0860);           // every frame
            inline auto GetSelectedCardId = reinterpret_cast<uint16_t(__fastcall*)(void* Trunk)>(0x1408BF000);

            // The list is built the way TrunkView_BuildCardList (0x1408BFEF0) does it: entries {u16 card id, u32 count, u32 0} in the vector at +1576,
            // the vector<int> at +1552 maps (card id - 3900) to an entry; then ApplyFilter, SortAndFillGrid, Refresh.
            constexpr size_t Entries = 1576;
            constexpr size_t IdToEntry = 1552;
            constexpr size_t Grid = 56;
            constexpr int FirstCardId = 3900;
            struct Entry
            {
                uint16_t CardId;
                uint16_t Pad;
                uint32_t Count;
                uint32_t Unused;
            };
            static_assert(sizeof(Entry) == 12);
            inline auto GridReset = reinterpret_cast<void(__fastcall*)(void* Grid, int)>(0x14088FA40);    // (trunk+56, 0), then GridSetMode(trunk+56, 10)
            inline auto GridSetMode = reinterpret_cast<void(__fastcall*)(void* Grid, int)>(0x14088F610);
            inline auto IntVectorResizeFill = reinterpret_cast<void(__fastcall*)(Vector* Vec, size_t Size, const int* Value)>(0x14088CE10);
            inline auto EntryVectorEmplace = reinterpret_cast<void(__fastcall*)(Vector* Vec, void* Where, const Entry* Value)>(0x1408BE270);
            inline auto ApplyFilter = reinterpret_cast<void(__fastcall*)(void* Trunk)>(0x1408BF9E0);
            inline auto SortAndFillGrid = reinterpret_cast<void(__fastcall*)(void* Trunk)>(0x1408BFC30);
            inline auto Refresh = reinterpret_cast<void(__fastcall*)(void* Trunk)>(0x1408C08D0);

            // Immediates in TrunkView_BuildCardList: the internal id loop bound (cmp ebx, imm32 at 0x1408C0061) and the size of the id -> entry
            // vector (mov ebx, imm32 at 0x1408BFF49). Yu-Gi-Oh-Cards raises both for its extra cards, so they are read, not assumed.
            inline uint32_t InternalIdLimit() { return *reinterpret_cast<uint32_t*>(0x1408C0063); }
            inline uint32_t IdToEntrySize() { return *reinterpret_cast<uint32_t*>(0x1408BFF4A); }

            // The grid (widget_CardGrid at trunk+720) keeps its own copy of the counts: a vector of {u16 card id, int owned, int in deck} at
            // grid+528, filled only by SetCards (RIX::widget_CardGrid::UpdateCounts, 0x14088C690, through the trunk's count function, which
            // returns 0 for ids above 14968). Its cells (272 bytes each, vector at grid+232; card id at +256) print owned - in deck.
            constexpr size_t GridCounts = 720 + 528;
            constexpr size_t GridCells = 720 + 232;
            constexpr size_t CellSize = 272;
            constexpr size_t CellCardId = 256;
            struct GridCount
            {
                uint16_t CardId;
                uint16_t Pad;
                int32_t Owned;
                int32_t InDeck;
            };
            static_assert(sizeof(GridCount) == 12);
            inline auto CellSetOwned = reinterpret_cast<void(__fastcall*)(void* Cell, int Owned)>(0x140882D60);   // redraws the number when it changed
        }

        // RIX::DeckStateHelper: a deck's contents. An empty one is enough for the trunk grid when there is no deck.
        namespace DeckState
        {
            constexpr size_t Size = 52 + 0x567A;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Helper)>(0x140755DE0);
        }

        // RIX::widget_CardInfo: the deck editor's card picture + ATK / DEF / level panel (editor +41488).
        namespace CardInfo
        {
            constexpr size_t Size = 928;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Info)>(0x140883380);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Info, SharedNode* Parent, int Z, void* Owner, float X, float Y)>(0x140883BA0);
            inline auto SetWidth = reinterpret_cast<void(__fastcall*)(void* Info, float Width)>(0x140884AA0);
            inline auto SetCard = reinterpret_cast<void(__fastcall*)(void* Info, uint16_t CardId)>(0x140884300);   // 0xFFFF = none
            inline auto Update = reinterpret_cast<void(__fastcall*)(void* Info, float Seconds, int Held, int)>(0x140884ED0);
        }

        // RIX::widget_BarV: the game's scroll bar (vertical; the tutorial list, character grid, deck list ...). ScreenSelectTutorial::SetupWidgets
        // builds it with CreateFromLayout(bar, root, 5, screen+88, 1743, 182.5, 750) and SetScaleX(node, 3.0); SetRange after each scroll.
        namespace ScrollBar
        {
            constexpr size_t Size = 160;
            constexpr size_t Node = 16;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Bar)>(0x14087AEF0);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Bar, SharedNode* Parent, int Z, void* Owner, float X, float Y, float Height)>(0x14087B980);
            inline auto SetRange = reinterpret_cast<void(__fastcall*)(void* Bar, float First, float Visible, float Total)>(0x14087BD60);   // Total 0 hides it
            inline auto SetScaleX = reinterpret_cast<void(__fastcall*)(void* Node, float Scale)>(0x14075A340);
        }

        // RIX::widget_CardDisplay (the same class as widget_CardGrid): a framed box of cols x rows card cells with a cursor, which scrolls
        // when there are more cards than cells. The Random Pack Store shows the cards a pack gave in one (screen+2256,
        // ScreenRandomPackStore::SetupWidgets 0x14085CB30); the trunk's grid is one too (trunk+720).
        namespace CardBox
        {
            constexpr size_t Size = 568;
            struct Desc   // RIX_CardDisplayDesc, copied to +336
            {
                const char* Name = "CardDisplay";
                float X = 0, Y = 0;
                const wchar_t* Title = nullptr;
                int Cols = 4, Rows = 2;
                float CellW = 255.17f, CellH = 370.0f;
                float FrameX = 0, FrameY = 0;          // the TextFrame's offset
                float CountX = 200.0f, CountY = 0;     // the "n cards" text
                bool ShowCount = false;
                int CountFontSize = 36;
                bool ShowFrame = true;
                float PadX = 20, PadY = 20, GapX = 10, GapY = 10;
                int CardStyle = 2;
                void* FrameArg = nullptr;
            };
            static_assert(sizeof(Desc) == 96);
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Box)>(0x14088B5A0);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Box, SharedNode* Parent, int Z, void* Owner, Desc* Layout)>(0x14088BC10);
            inline auto SetCursorScale = reinterpret_cast<void(__fastcall*)(void* Box, float Scale)>(0x14088C480);   // the store: 1.03
            // Expected -1 = no "n / m"; counters null = no numbers on the cells (what the store passes).
            inline auto SetCards = reinterpret_cast<void(__fastcall*)(void* Box, unsigned Count, const uint16_t* Ids, int Expected, void* Owned, void* InDeck)>(0x14088C490);
            // Per frame. Action gets 3 on confirm / a click (Clicked = 1 for the mouse); true when the cursor moved.
            inline auto ProcessInput = reinterpret_cast<char(__fastcall*)(void* Box, int Pressed, int Held, int* Action, char* Clicked)>(0x14088B7C0);
            inline auto GetSelectedCardId = reinterpret_cast<uint16_t(__fastcall*)(void* Box)>(0x14088BBB0);       // 0xFFFF none
        }

        // RIX::widget_DeckCardsZone: the deck editor's deck panel (the deck's name, Main / Extra / Side card rows, the 10-row stats bar and
        // a sort bar). ScreenBattlePackEdit::SetupWidgets (0x140827790) builds it at (646, 120), then SetCellStyle(2), SetMode(2) and
        // SetFocused(1); OnEnter / RefreshDeck (0x140827D90) show a deck with RebuildCounts(zone + Counts, deck), SetDeck, RefreshSections,
        // RefreshCounts. The stats bar reads the zone's own DeckStateHelper (+3432).
        namespace DeckView
        {
            constexpr size_t Size = 3432 + 52 + 0x567A;   // the helper is the last member
            constexpr size_t Counts = 3432;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Zone)>(0x140892670);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Zone, SharedNode* Parent, int Z, void* Owner, float X, float Y)>(0x1408931C0);
            inline auto SetCellStyle = reinterpret_cast<void(__fastcall*)(void* Zone, int Style)>(0x1408937E0);
            inline auto SetMode = reinterpret_cast<void(__fastcall*)(void* Zone, int Mode)>(0x140893830);
            inline auto SetMainLimit = reinterpret_cast<void(__fastcall*)(void* Zone, int Limit)>(0x1408937D0);     // 40 (30 in the short format)
            inline auto SelectSection = reinterpret_cast<void(__fastcall*)(void* Zone, unsigned Section, char Quiet)>(0x140893660);   // 0 main, 1 extra, 2 side
            // The deck record (YGO::SAVE::DeckListItem) from a deck id (0-31 a save deck, 32 + n deckdata deck n, 732-736 a battle pack slot).
            inline auto DeckFromId = reinterpret_cast<void*(__fastcall*)(unsigned Profile, unsigned DeckId)>(0x14081AB40);
            inline auto RebuildCounts = reinterpret_cast<void(__fastcall*)(void* Helper, void* Deck)>(0x140755E30);  // Deck_RebuildCardCountTables
            inline auto SetDeck = reinterpret_cast<void(__fastcall*)(void* Zone, void* Deck)>(0x140893740);          // null = empty
            inline auto RefreshSections = reinterpret_cast<void(__fastcall*)(void* Zone)>(0x140893A30);
            inline auto RefreshCounts = reinterpret_cast<void(__fastcall*)(void* Zone)>(0x1408939B0);
            // Per frame while focused: up / down at a row's edge move between Main / Extra / Side, 0x4000 sorts. Section gets the section + 2
            // when a card was confirmed (clicked / confirm). True when something changed.
            inline auto ProcessInput = reinterpret_cast<char(__fastcall*)(void* Zone, int Pressed, int Held, int* Section)>(0x1408929C0);
            inline auto GetSelectedCardId = reinterpret_cast<uint16_t(__fastcall*)(void* Zone)>(0x140893060);       // 0xFFFF none
            constexpr size_t Section = 3088;              // ListState: its first int = the current section (0 main, 1 extra, 2 side)
            constexpr size_t Rows = 936;                  // the three card rows (widget_CardDisplayFlex), 648 bytes each
            constexpr size_t RowSize = 648;
            // A row: +80 its title (widget_TextFrame: "Main Deck" = layout +16, string 0x1A3), +216 the count text, +420..+431 the count's
            // colouring bounds (from the layout; 0 = no colouring), +600 vector of 12-byte cells (+0 the card id).
            constexpr size_t RowTitle = 80, RowCountColours = 420;
            // The row's pool of card widgets (+232, 272 bytes each) is made once, at creation: +368 squeezed cards per row x +372 rows, from
            // the row layouts (g_DeckZoneLayout_Main / Extra / Side, copied to row+336: +32 squeezed cols, +36 rows: 15 x 4, 15 x 1, 15 x 1).
            // The row places cells by card index, so more cards than the pool write past it (a crash). Up to cols x rows (+360 x +364)
            // show full size, then they squeeze (fan) up to the pool; rows never scroll.
            constexpr size_t RowSqueezedCols = 368, RowVisibleRows = 372;
            inline const uintptr_t RowLayouts[3] = { 0x140A7E550, 0x140A7E5B0, 0x140A7E610 };
            constexpr size_t LayoutSqueezedCols = 32;
            constexpr int SectionSqueezedCols[3] = { 50, 80, 80 };   // RIX deckView "sections": pools of 200 / 80 / 80
            // Fills a row with Count cards (at most its pool, see above), as SortAndFill does (Highlight -1, the zone's +3424 and +3432 as
            // the last two).
            inline auto FillRow = reinterpret_cast<void(__fastcall*)(void* Row, unsigned Count, const int16_t* Cards, int Highlight, void* Combo, void* Counts)>(0x14088E1C0);
            constexpr size_t Combo = 3424;                // passed to FillRow
            constexpr size_t Header = 96;                 // the deck name's text layer (SetDeck writes the deck's Name there)
            inline auto SetHeaderText = reinterpret_cast<void(__fastcall*)(void* Layer, const wchar_t* Text)>(0x14075E260);   // copied
            constexpr size_t Stats = 208;                 // widget_DeckStats (the monster / spell / trap bar), vftable +24 = SetVisible
            inline void ShowStats(void* Zone, bool Show)
            {
                char* stats = static_cast<char*>(Zone) + Stats;
                (*reinterpret_cast<void(__fastcall***)(void*, bool)>(stats))[3](stats, Show);
            }
            // A widget's own box under the mouse (what ProcessInput tests each row with).
            inline auto WidgetHitTest = reinterpret_cast<bool(__fastcall*)(void* Widget, int64_t Mouse)>(0x140782DA0);
            inline bool HitTest(void* Zone, int64_t Mouse)
            {
                for (int i = 0; i < 3; ++i)
                    if (WidgetHitTest(static_cast<char*>(Zone) + Rows + 648 * i, Mouse))
                        return true;
                return false;
            }
        }

        // RIX::widget_BarH: the loading screen's progress bar (load_bar_outer / load_bar_inner). The card loading screen (0x1407B73C0)
        // builds it with CreateFromLayout(bar, root, 3, 0, &{616, 935, 1190, 34}); SetRange(bar, 0, done, total) fills it.
        namespace ProgressBar
        {
            constexpr size_t Size = 184;
            struct Desc { float X, Y, Width, Height; };
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Bar)>(0x14087A970);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Bar, SharedNode* Parent, int Z, void* Owner, const Desc* Layout)>(0x14087AB80);
            inline auto SetRange = reinterpret_cast<void(__fastcall*)(void* Bar, float First, float Visible, float Total)>(0x14087AE00);   // Total 0: unchanged
        }

        // RIX::widget_SettingsLine: one row of the Live setting screen (a label, a value and, when Arrows, the left / right arrows shown while
        // focused). ScreenLiveSetting::SetupWidgets (0x140854E60) builds ten with CreateFromLayout(row, node, 2 + i, screen+88, 0, 133 + 130 i,
        // 1, 300, bits of 480.0f) under a node at (1437, 120); LiveSetting_RefreshRows (0x140856500) writes the values. Label / value take a
        // string id or a wchar_t* (as Screen::SetHeaderText); the game copies the text.
        namespace OptionRow
        {
            constexpr size_t Size = 240;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Vector, void* Rows, int64_t Count)>(0x1408568E0);   // Vector unused
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Row, SharedNode* Parent, int Z, void* Owner, float X, float Y, uint8_t Arrows, float ValueWidth, int LabelWidthBits)>(0x1408B3350);
            inline auto SetLabel = reinterpret_cast<void(__fastcall*)(void* Row, int64_t Text)>(0x1408B3850);
            inline auto SetValue = reinterpret_cast<void(__fastcall*)(void* Row, int64_t Text)>(0x1408B3880);
            // The whole row (label, value and arrows) under the mouse (ScreenLiveSetting's mouse focus, 0x140854560).
            inline auto HitTest = reinterpret_cast<bool(__fastcall*)(void* Row, int64_t Mouse)>(0x1408B38E0);
            // Row-local x of the arrows' centres: -(ValueWidth / 2 + 54) and +(ValueWidth / 2 + 54) (CreateFromLayout). The value
            // text only redraws when SetFocused changes its colour state (+344 cleared), so a new value needs a focus flip.
            // SetFocused (0x1408B37C0) gives the label (+96) and the value (+128) TextColour(focused) and shows the arrows (+144, +176).
            constexpr size_t LabelText = 96, ValueText = 128;
            constexpr size_t TextColourAt = 160, TextDrawn = 344;   // in a text: its colour, and the "drawn" flag (0 = draw again)
            inline auto TextColour = reinterpret_cast<int(__fastcall*)(bool Focused)>(0x140869E80);
        }

        // RIX::widget_PackWrapper: one booster pack of the Random Pack Store (its picture, title, price and owned counts). The store
        // builds six at (506 + 235 i, 575) (ScreenRandomPackStore::SetupWidgets) and fills them with SetPack (BoosterShop_RefreshRows).
        // About 211 x 300 around its position.
        namespace PackWrapper
        {
            constexpr size_t Size = 216;
            constexpr size_t PackId = 208;      // unsigned, what SetPack was given
            constexpr size_t Complete = 212;    // char: every card of the pack owned three times
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Wrapper)>(0x1408A2F50);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Wrapper, SharedNode* Parent, int Z, void* Owner, float X, float Y)>(0x1408A30D0);
            // A locked pack (save unlock bit off; checked for ids below 128) shows "wrap_locked" with no title.
            inline auto SetPack = reinterpret_cast<void(__fastcall*)(void* Wrapper, unsigned PackId)>(0x1408A34A0);
            // SetPack's own test: a pack record exists and (id >= 128 or its unlock bit at save +2944 is on).
            inline bool Unlocked(void* Wrapper)
            {
                const unsigned id = *reinterpret_cast<unsigned*>(static_cast<char*>(Wrapper) + PackId);
                if (!reinterpret_cast<void*(__fastcall*)(unsigned)>(0x14080E340)(id))   // YGO::GAME::Get_PackRecord
                    return false;
                if (id >= 128)
                    return true;
                const char* save = static_cast<const char*>(reinterpret_cast<void*(__fastcall*)(unsigned)>(0x1407F80A0)(0xFFFFFFFD));   // Get_PlayerSection
                return (*reinterpret_cast<const unsigned*>(save + 2944 + 4 * (id >> 5)) >> (id & 31)) & 1;
            }
        }

        // RIX::widget_TextFrame: the framed title tag of the card boxes (widget_CardDisplay / CardDisplayFlex): a text in a box that
        // grows with the text, up to MaxWidth (0 = no limit).
        namespace TextFrame
        {
            constexpr size_t Size = 120;
            inline auto Construct = reinterpret_cast<void*(__fastcall*)(void* Frame)>(0x1408B4F90);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Frame, SharedNode* Parent, int Z, void* Owner, float X, float Y, float MaxWidth)>(0x1408B5080);
            // A string id or wchar_t* (the game copies it); Fit sizes the box to the text; 0 hides the frame.
            inline auto SetText = reinterpret_cast<void(__fastcall*)(void* Frame, int64_t Text, char Fit)>(0x1408B51F0);
        }

        // widget_LoadSpinner: the loading rings (load_ring_inner / outer turning both ways), the main menu's (+0x4A0) and the card loading
        // screen's (574, 952). The rings turn by two endless animations CreateFromLayout adds to the screen's animation list (ScreenBase +0x78).
        namespace LoadSpinner
        {
            constexpr size_t Size = 128;
            constexpr size_t ScreenAnims = 0x78;
            inline void Construct(void* Spinner)   // inline in the game (ScreenMainMenu::Constructor)
            {
                reinterpret_cast<void(__fastcall*)(void*)>(0x14087BF90)(Spinner);   // widget_Base::Constructor
                *reinterpret_cast<uintptr_t*>(Spinner) = 0x140A57B98;               // widget_LoadSpinner vftable
                std::memset(static_cast<char*>(Spinner) + 48, 0, Size - 48);
            }
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Spinner, SharedNode* Parent, int Z, void* Owner, float X, float Y, void* Anims)>(0x1408A0C00);
        }

        // One digit wheel of the player match code entry (widget_EntryDigit: a number in a box, an arrow above and below).
        namespace EntryDigit
        {
            constexpr size_t Size = 184;
            constexpr size_t Value = 176;       // int, 0..9
            inline auto Construct = reinterpret_cast<void(__fastcall*)(void* Digit)>(0x140853240);
            inline auto CreateFromLayout = reinterpret_cast<void(__fastcall*)(void* Digit, SharedNode* Parent, int Z, void* Owner, float X, float Y, void* ScreenData)>(0x14089DE70);
            inline auto SetSelected = reinterpret_cast<void(__fastcall*)(void* Digit, char Selected)>(0x14089E150);
            inline auto SetValue = reinterpret_cast<void(__fastcall*)(void* Digit, int Value)>(0x14089E220);     // stores Value % 10
            inline auto HitTest = reinterpret_cast<int(__fastcall*)(void* Digit, int64_t Mouse)>(0x14089DAC0);  // 0 upper arrow, 1 lower arrow, 2 box, -1 none
        }

        // ---- the deck picker (DeckSelectList): the grid of decks with category tabs used by Free Duel (screen+1456) and the campaign's
        // "choose your deck" (screen+664), and the deck info panel next to it (DeckInfoPanel: Free Duel +2864 yours / +3448 the opponent's,
        // campaign +2072). Deck ids: 0-31 a save deck, 32 + n deckdata deck n, 732-736 a battle pack slot (DeckId_Decode 0x1407F9150).
        namespace DeckSelectList
        {
            constexpr size_t TabCount = 8;                // +392 the tabs; tab 0 = every deck, 1-7 the categories (RebuildTabs fills them)
            constexpr size_t IdsOffset = 1208;            // Vector of uint32 deck ids per tab (24 bytes each)
            inline Vector* Ids(void* List, size_t Tab = 0) { return reinterpret_cast<Vector*>(static_cast<char*>(List) + IdsOffset + 24 * Tab); }

            inline auto FillPlayerDecks = reinterpret_cast<void(__fastcall*)(void* List)>(0x1408ADB70);              // the player's decks (save + owned)
            inline auto RebuildTabs = reinterpret_cast<void(__fastcall*)(void* List)>(0x1408AE5B0);                  // tabs 1-7 from tab 0, then lays out
            inline auto Refresh = reinterpret_cast<void(__fastcall*)(void* List, bool Refill)>(0x1408AE190);         // Refill = the owned list again
            inline auto SelectDeckId = reinterpret_cast<bool(__fastcall*)(void* List, int DeckId)>(0x1408AE2F0);     // false: not in the list
            inline auto GetSelectedDeckId = reinterpret_cast<int(__fastcall*)(void* List)>(0x1408ADBC0);             // -1 none
            inline auto GetSelectedDeckIdPtr = reinterpret_cast<unsigned int*(__fastcall*)(void* List)>(0x1408AE130);
            inline auto Tick = reinterpret_cast<void(__fastcall*)(void* List)>(0x1408AE480);                         // every frame, before HandleInput
            // Navigation and tabs. Mask: the frame's buttons plus TabButtons(); Clicked gets the index clicked with the mouse (-1 none).
            // Returns true when the selection moved.
            inline auto HandleInput = reinterpret_cast<bool(__fastcall*)(void* List, int Mask, int* Clicked)>(0x1408AD4E0);
            // The tab switch buttons (L/R) as a mask, what the game ORs into HandleInput's mask.
            inline int TabButtons(void* Input) { return reinterpret_cast<int(__fastcall*)(void*, int, unsigned int, unsigned int)>(0x140800940)(Input, 0, 0x20000000u, 0x40000000u); }
            // Appends to a game vector of uint32 (its own allocator: never free or grow these with ours).
            inline auto VectorPushU32 = reinterpret_cast<void(__fastcall*)(Vector* Vec, void* Where, const unsigned int* Value)>(0x14081A840);
            inline void Push(Vector* Vec, unsigned int Value)
            {
                if (Vec->End == Vec->Capacity)
                    VectorPushU32(Vec, Vec->End, &Value);
                else
                {
                    *static_cast<unsigned int*>(Vec->End) = Value;
                    Vec->End = static_cast<unsigned int*>(Vec->End) + 1;
                }
            }
        }
        namespace DeckInfoPanel
        {
            inline auto ShowDeck = reinterpret_cast<void(__fastcall*)(void* Panel, const unsigned int* DeckId)>(0x1408AB9C0);  // null = empty
            // Its parts (CreateFromLayout 0x1408AACC0 / ShowDeck): a part's widget (SetEnabled shows it) and the text layer 16 bytes on
            // (TextLayer_SetWideText, copied). Mode 2 / 3 (Free Duel's) builds all but the signature card (+288 / +416) and validity (+448).
            constexpr size_t Portrait = 128, PortraitImage = 144;   // the owner's portrait (an image layer)
            constexpr size_t TypeBox = 192, Type = 224;             // the deck type line (Starter / Story deck / ...); TypeBox shown with it
            constexpr size_t Source = 256, SourceLine = 384;        // "Source" heading, then its line (the pack / story duel)
            constexpr size_t SignatureHeading = 288, Signature = 416, Validity = 448;
            constexpr size_t Owner = 320, Name = 352;               // the owner's name, the deck's name
            constexpr size_t TextOf = 16;                           // part + 16 = its text layer
            inline auto CharacterPortrait = reinterpret_cast<void*(__fastcall*)(int Character)>(0x1407FE920);   // cached portrait texture
            inline auto ImageSetTexture = reinterpret_cast<void(__fastcall*)(void* Image, void* Texture, char Flag)>(0x14075EB80);
            inline auto ImageRefresh = reinterpret_cast<void(__fastcall*)(void* Image)>(0x14075EDD0);
            inline auto SetText = reinterpret_cast<void(__fastcall*)(void* Layer, const wchar_t* Text)>(0x14075E260);   // copied
        }

        // ---- Free Duel (decoded 2026-10-07): screen 21, RIX::ScreenHardChallenge (vtable 0x140A740F0). The main menu opens it as
        // Duelist Challenge (item 2: challenge mode on, unlock bit 2964&1); with every mode flag off it is a plain free duel (any
        // character, their normal deck) - Yu-Gi-Oh-TagDuel opens it that way. NOT screen 41 (the campaign). A state machine at +4064:
        //   0 series tabs (+656), 1 opponent list (+904), 2 your deck (DeckSelectList +1456, panels +2864 yours / +3448 the opponent's),
        //   3 start: Set_DuelSideCharacter/Deck(0) from your deck (character = the deck's RecordSlot), (1) from the opponent list
        //   (deck = character's deck + 32), arena from the character, then Screen_GotoDuel. 5 leaving.
        // Back on the list = SetState(0); SetState(1) is the game's own way back to the list (rebuilds it). The duel's seats are then
        // made by AssignSeatDecksAndDuelists from g_DuelSideCharacters/g_DuelSideDecks (int[4] at 0x140C8D1F8/0x140C8D208) - all 4
        // seats when tag is on (Yu-Gi-Oh-TagDuel uses this for partners).
        namespace FreeDuel
        {
            constexpr size_t State = 4064;
            enum : int { StateSeries = 0, StateOpponents = 1, StateYourDeck = 2, StateStart = 3, StateLeaving = 5 };
            constexpr size_t SeriesTabs = 656, OpponentList = 904, DeckList = 1456, YourDeckPanel = 2864, OpponentDeckPanel = 3448;
            inline auto HandleInput = reinterpret_cast<void(__fastcall*)(void* Screen, void* UI)>(0x140840B10);
            inline auto SetState = reinterpret_cast<void(__fastcall*)(void* Screen, int State)>(0x140841700);
            inline auto BuildOpponentList = reinterpret_cast<void(__fastcall*)(void* Screen)>(0x140841900);
            inline auto UpdateHelpBar = reinterpret_cast<void(__fastcall*)(void* Screen)>(0x140841A40);
            // The opponent list (+904): the highlighted character (g_CharacterRecords index; 0 = none / locked).
            inline auto GetSelectedCharacter = reinterpret_cast<unsigned int(__fastcall*)(void* List)>(0x1408A52A0);
            inline int* const OpponentDeckId = reinterpret_cast<int*>(0x140C8E818);   // g_FreeDuelOpponentDeckId, shown in +3448
        }
    }
}
#endif
