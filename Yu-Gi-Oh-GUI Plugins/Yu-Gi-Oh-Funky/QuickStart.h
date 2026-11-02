#pragma once

// Starting the game on a chosen screen or page, to check something without clicking through. [Yu-Gi-Oh-Funky] in Config.ini (declared in Yu-Gi-Oh-Funky.json):
//   StartScreen=0     a screen id (RIX_SCREEN_*; 25 = the deck editor), 0 = off
//   StartPage=        a page in Yu-Gi-Oh-Ex\pages (RIX_OpenPageFile); wins over StartScreen
//   StartSlot=0       the save slot to load, 0 = the one last played
//
// With either set: the logos are skipped, the title's Play is pressed for you and the save-select screen is skipped (Core_QuickPlaySlot), then once the main
// menu is up it goes to the screen, so Back returns to the main menu as usual. A slot with no save, or one these plugins can't play, stops at the save-select
// screen as normal; picking a slot there still ends on the chosen screen. The title's Play is RIX::ScreenTitle::Update 0x1408676A0, traced 2026-11-02.
namespace QuickStart
{
    void Install();     // from ProcessDetours
    void Tick();        // every frame, from ProcessWindow
    void DrawTool(int selectedScreen, const char* selectedName);   // the part of the Screens tab that sets it
}
