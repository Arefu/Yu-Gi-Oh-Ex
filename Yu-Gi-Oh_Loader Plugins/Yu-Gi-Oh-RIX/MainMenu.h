#pragma once
#include <functional>
#include <string>

#include "Yu-Gi-Oh-RIX.h"

// The engine behind the exported functions: extra buttons for the main menu (ScreenMainMenu).
//
// How the game builds the menu (all of it traced in YuGiOh.exe.i64, see the comments on the RIX::ScreenMainMenu functions):
//  - ScreenMainMenu::ResetItems sizes two vectors to exactly 13: the item definitions (24 bytes) and the button widgets (424 bytes).
//  - ScreenMainMenu::SetupArrays fills three vectors of item ids, one per page. The order of the ids is the order of the buttons.
//  - ScreenMainMenu::SetupWidgets creates one widget per item in a loop of 13; the widget's artwork is chosen by (item id + 3).
//  - ScreenMainMenu::LayoutPage shows the widgets of the current page: label from the definition, Y from the slot number.
//  - ScreenMainMenu::ActivateItem is a switch on the item id that does whatever the button does.
// An extra button therefore needs a bigger definition and widget vector, its id in a page vector, the loop bound raised, an artwork
// id that exists, and a case in ActivateItem. The hooks for all of that live in MainMenu.cpp.
namespace Menu
{
    int Add(const RIX_ButtonDesc& Desc);
    bool Remove(int Id);
    bool Update(int Id, const RIX_ButtonDesc& Desc);
    bool Get(int Id, RIX_ButtonDesc& Out);
    int ActiveCount();
    int ActiveIdAt(int Index);

    // Renames and/or hides one of the game's 13 main menu buttons (null = leave that text alone). Hidden buttons are taken off their
    // page when the menu is built; there is no way to bring one back without restarting.
    void EditVanilla(int Item, const std::wstring* Label, const std::wstring* Description, bool Hidden);

    // A pinned button stays visible while the menu is exclusive. Exclusive mode lists ONLY the pinned buttons (the game's own and everyone
    // else's are taken off the pages, and put back when it ends): a whole menu of your own on the main menu screen.
    void Pin(int Id);

    // The ScreenMainMenu object last seen updating (null before the first frame).
    void* MainScreen();

    // Called on every frame of the main menu (the game's thread). One callback; nothing runs while the main menu is not showing.
    void SetFrameCallback(void (*Callback)());

    // Called once, on the game's thread, just before the main menu is first built. Buttons added from it are part of that build.
    void SetBuildCallback(void (*Callback)());
    // WhenShownAgain (with On = false): stay exclusive until the main menu is next shown, so it does not flash up while another screen fades in.
    // The reentry callback runs at that moment, before the pages are put back.
    void SetExclusive(bool On, bool WhenShownAgain = false);
    void SetReentryCallback(void (*Callback)());

    // The named actions menu files can call. Run one (false when there is none by that name).
    bool RegisterAction(const std::string& Name, RIX_ButtonCallback Callback, void* User);
    bool RunAction(const std::string& Name);

    // Reads Yu-Gi-Oh-Ex/menus/*.json next to the game (see docs/MenuFiles.md) and adds what they describe.
    void LoadMenuFiles();

    // While one of our buttons' callbacks runs: the screen object the button was pressed on (the ScreenMainMenu, or the ScreenHelp for an
    // options button), null otherwise. The game changes screen FROM a screen object (RIX::Screen::GotoScreen), which is how the new
    // screen learns where its Back button leads, so screen changes made by a callback must use it.
    void* CallbackSource();

    bool IsOpen();
    bool Press(int Item);

    // Puts the hooks in. Nothing changes in the game until a button is added.
    void Install();
}
