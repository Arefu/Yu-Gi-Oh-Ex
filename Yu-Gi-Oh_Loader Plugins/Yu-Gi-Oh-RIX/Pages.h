#pragma once
#include "Yu-Gi-Oh-RIX.h"

// Pages (RIX_OpenPage): screens of a plugin's own, shown on the Battle Pack screen (RIX::ScreenBattlePackStore, screen 30). Its top level is
// a header and a MenuKit menu, so instead of registering a new screen class the real screen is borrowed while pages are open, and pages
// are a stack switched in place on it:
//  - OnEnter is hooked to make the screen think it came from the main menu (so it shows its top level, not the pack details), then the top
//    page is applied: header, the menu items relabelled and shown (items 1, 2, 10, 8 of g_BattlePackMenuItemTable), or the menu hidden for a
//    page of the plugin's own widgets;
//  - Screen::SetHeaderText is hooked so the game's own "Battle Pack" (text 475) never replaces the page's header;
//  - HandleItem (the screen's button switch) runs the page's callbacks, and Back closes the top page;
//  - Update is replaced for pages without buttons: RIX reads the input, closes the page on cancel and hands the rest to OnFrame.
// Entering the screen from the main menu's own Battle Pack button (no page open) puts the game's labels back.
namespace Pages
{
    bool Open(const RIX_PageDesc& Desc);
    bool Close();
    void Install();
}
