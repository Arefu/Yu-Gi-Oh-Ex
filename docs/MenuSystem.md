# The menu system (RIX) and Yu-Gi-Oh-RIX

What the game's UI framework looks like, how extra buttons get into it, and how another plugin can use that. Everything here was traced
in `YuGiOh.exe.i64`; the functions, types and tables are named and commented there too (search for `RIX::`).

## The framework

The game calls its UI layer **RIX**. A screen manager (`RIX::App::GetUI()`) holds a stack of screens, each a class derived from
`RIX::ScreenBase`. `RIX::UI::NavigateToScreen(ui, screenId, fadeSeconds, flags, force)` changes screen; the ids are in
`YGO::UI::RIX::ScreenID` (`YuGiOh-UI.h`) and the `RIX_ScreenId` enum in the IDB.

Menus are made from **MenuKit**:

| Piece | What it is |
| --- | --- |
| `RIX_MenuItemDef` | 24 bytes: `id`, `label`, `description`. `label` and `description` are a string id (1..1213) **or a raw `const wchar_t*`** (any value above 2214). |
| A definition table | An array of `RIX_MenuItemDef` ending with `id == -1`. |
| `RIX::widget_Text::SetTextById(widget, idOrPointer)` | Sets any text. This is why a custom label is just a wide-string pointer. |
| A *skin* id | Every button widget is created with an artwork id (main menu: item id + 3; MenuKit menus: index + 2). It only picks the picture, so an extra button borrows an existing one. |

### The main menu (`RIX::ScreenMainMenu`, screen 8; 6 is `ScreenSignIn`)

13 buttons (`RIX_MainMenuItem` in the IDB), three pages, all data driven:

| Page | Item ids, in on-screen order |
| --- | --- |
| 0 main | 0 single player menu, 3 multiplayer menu, 7 battle pack, 8 deck editor, 9 card shop, 10 help and options, 12 quit |
| 1 single player | 1 solo duel, 2 duelist challenge, 11 tutorials |
| 2 multiplayer | 4, 5 player match, 6 leaderboard |

* `g_MainMenuItemTable` (`0x140A76680`) holds the 13 `RIX_MenuItemDef`s. `LoadScreenTable` copies them into the screen.
* `ResetItems` (`0x1408574A0`, end of the constructor) sizes the definition vector and the widget vector to **13**, then calls
  `SetupArrays` (`0x140857FA0`), which fills the three page vectors (`vector<int>` of item ids at `+0x2F8`, `+0x310`, `+0x328`).
* `SetupWidgets` (`0x1408579B0`) creates one widget per item in a loop of 13 (`cmp r15d, 0Dh`, immediate at `0x140857D17`).
* `LayoutPage` (`0x1408583D0`) shows the widgets of the current page: label from the definition, `Y = slot * 112 + (540 - (n - 1) * 56)`.
  Items 2, 7 and 9 are greyed out until bits 0, 1, 2 of `PlayerSection + 2964` are set.
* `ActivateItem` (`0x140856C40`) is a `switch` on the item id: 1 solo duel (screen 41), 2 duelist challenge (21), 4/5 player match (33),
  6 leaderboard (38), 7 battle pack (30), 8 deck editor (25), 9 card shop (29), 10 options (12), 11 tutorials (24), 12 quit; 0 and 3 open a page.

**Adding a main menu button** needs five things, all done by hooks in `Yu-Gi-Oh-RIX`:

1. grow the definition vector and the widget vector (`ReallocateDefs`, `ReallocateItems`) and fill the new definition;
2. put the new id in a page vector (order = position on screen);
3. raise the loop bound at `0x140857D17` so a widget is created;
4. make `CreateFromLayout` use an artwork id that exists (the loop would ask for 16 and up);
5. handle the new id in `ActivateItem`, and finish like the game's cases do (`PendingItem = -1`, unlock the widgets) or the menu freezes.

### The options menu (`RIX::ScreenHelp`, screen 12) and every other `ScreenBaseMenu`

`ScreenHelp`, `ScreenPause` (20) and `ScreenLiveMenu` derive from `RIX::ScreenBaseMenu`, which owns a MenuKit **menu** object at `+664`:

* `menu::SetDefinition(menu, table)` (`0x14080A210`) builds the items from a definition table (options: `g_HelpMenuItemTable`, `0x140A74200`).
* `menu::ShowItem(menu, id)` (`0x140809580`) appends an id to the visible list; the screen's `OnEnter` calls it once per button, in order.
  `ScreenHelp::OnEnter` shows 0, (8), 1, 2, 3, 6, 7; 7 is Back and always last.
* `menu::ProcessInput` reads the pad/mouse and calls the screen's listener (`screen + 656`): `IsItemEnabled(id)` then
  `OnItemActivated(ui, id, profile)`; Back is id -2.
* `ScreenHelp::OnItemActivated` (`0x140841CD0`): 0 how to play (17), 1 controller (16), 2 settings (13), 3 video (14), 4 statistics (18),
  5 score review (43), 6 credits (15), 8 tutorials (24), 7 / -2 close.

Adding an options button: hook `SetDefinition` (pass a table with the new item), hook `ShowItem` (show the new ids just before Back while
`OnEnter` runs), hook `OnItemActivated`, and borrow artwork in `menu_item::CreateFromLayout` (`0x140809CC0`).
The pause menu and live menu work the same way; they only need their own table / `OnEnter` / listener addresses (not traced yet).

## Yu-Gi-Oh-RIX (the plugin)

`Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-RIX`, loaded by the loader like any plugin. It exports plain C functions declared in
`Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-RIX.h`, so any plugin (or language) can add, change and remove buttons:

```cpp
#include "Yu-Gi-Oh-RIX.h"

void __cdecl OnPress(int id, void* user) { RIX_GotoScreen(RIX_SCREEN_GAME_CREDITS); }

if (RIX::Load())   // finds the module the loader already loaded
{
    RIX_ButtonDesc button = RIX::Describe(L"My Mod", L"What it does", RIX_PAGE_MAIN, &OnPress);
    int id = RIX::AddMainMenuButton(&button);                    // main menu
    RIX_ButtonDesc option = RIX::Describe(L"My Options", L"", 0, &OnPress, nullptr, RIX_OPTION_SETTINGS, RIX_MENU_OPTIONS);
    RIX::AddMainMenuButton(&option);                             // options menu
}
```

| Function | |
| --- | --- |
| `RIX_GetVersion` | interface version (`RIX_API_VERSION`) |
| `RIX_AddMainMenuButton(desc)` | adds to the menu in `desc->Menu`; returns the id (main 13-92, options 100+) or -1 |
| `RIX_RemoveMainMenuButton(id)` | hides it (the id is not reused) |
| `RIX_UpdateMainMenuButton(id, desc)` | changes label, description, page, callback (live on the main menu) |
| `RIX_GetMainMenuButton(id, out)` / `...Count` / `...IdAt` | read back |
| `RIX_PressMainMenuItem(item)` | presses one of the game's 13 buttons (main menu open) |
| `RIX_IsMainMenuOpen`, `RIX_GetCurrentScreenId`, `RIX_GotoScreen(id)` | screens |

Rules: buttons added at start-up appear the first time the menu is built; buttons added later appear the next time the game builds the
menu (a restart always works); label / description / page changes and removals apply on the next main menu frame. Callbacks run on the
game's thread. All calls are thread safe.

## Yu-Gi-Oh-Funky

The ImGui debug plugin. `Debug Tools` window (opened by the `Debug Tools` demo button or `DemoMenuButtons=1`): **Save** (points,
menu unlock flags, characters, card copies), **Screens** (jump to any screen, shows the current one), **Main menu** (press the game's
buttons, edit / add / remove buttons through Yu-Gi-Oh-RIX), **Duel** (starting life points).

Config (`Config.ini`, game folder):

```
[Yu-Gi-Oh-Funky]
DemoMenuButtons=1    ; adds "Debug Tools" and "Credits" to the main menu and "Funky Tools" to the options menu
```

## Not done yet

* Buttons added while the menu is already built (needs creating a widget on a live screen).
* The pause and live menus (same mechanism, addresses not traced).
* Submenu *pages* of your own (a button that opens a new page): the page vectors and `ShowPage` exist, `ActivateItem` cases 0 and 3 show how.
* Custom screens in this plugin's API. How to build one is now known and done once, in Yu-Gi-Oh-Core's save-select screen: see
  [SaveSlots.md](SaveSlots.md#a-new-rix-screen).

## Pages and new actions for the game's buttons (API version 4)

* `RIX_SetMainMenuItemAction(item, callback, user)` makes one of the game's 13 main menu buttons run your callback instead (`Hook_ActivateItem`).
  Duelist Challenge, Battle Pack and Card Shop still show the game's "locked" message until their unlock bit (PlayerSection + 2964) is set.
* `RIX_OpenPage(desc)` / `RIX_ClosePage()` (`Pages.cpp`): screens of your own on the **Battle Pack screen**. A page is a menu (header + up to 4
  buttons) or a page of your own widgets (`ButtonCount` 0, with `OnShow` / `OnHide` / `OnFrame(screen, pressed, held, seconds)`). Opening a page
  from a page stacks it in place; Back closes the top page, and closing the last one leaves the screen. Rather than register a new screen class,
  the real `RIX::ScreenBattlePackStore` (screen 30) is borrowed, because its top level already is a header (text 475) and a MenuKit menu:
  * `OnEnter` (`0x14082B060`) is hooked to make `RIX::UI::GetPreviousScreenId` read 8 (main menu) during the call, which makes it show the top
    level (otherwise coming back from a screen a page opened would reopen the pack details), then the top page is applied: header, menu items
    1, 2, 10, 8 relabelled (item + 56 / + 96) and shown with `ClearVisible` / `ShowItem` / `Layout`, or the menu hidden for a widget page;
  * `RIX::Screen::SetHeaderText` (`0x140822F00`) swaps text 475 for the page's header;
  * `HandleItem` (`0x140828710`, the screen's switch) runs the button callbacks (clearing the pending item at screen + 7860), and Back closes pages;
  * `Update` (`0x14082A890`) is replaced for widget pages: RIX reads the input, closes the page on cancel and passes the rest to `OnFrame`.
  A callback's `RIX_GotoScreen` changes screen from the Battle Pack screen, so that screen's Back returns to the page.
* Game widgets can be built on any screen: `widget_Base::CreateNode` (`0x14087C030`) makes a node under the screen's root (screen + 72), so a
  widget is constructed in your own memory and `CreateFromLayout(widget, root, z, screen + 88, x, y)`. The declarations (trunk, card info,
  entry digits, input, more MenuKit) are in `YuGiOh-RIX.h`.
* Yu-Gi-Oh-BetterCardShop (`StorePages.cpp`): the main menu's Card Shop opens **Card Store** (Booster Packs = the game's card shop, screen 29;
  **Card Shop** = every card in its own `widget_TrunkZone` + `widget_CardInfo`, list filled like `TrunkView_BuildCardList` with the bounds read
  from the game's code; **Enter Password** = eight `widget_EntryDigit`s and an **Unlock** button (a page with both widgets and a button,
  `ButtonsY` 820, `ButtonsX` 1215 - the menu's root node at screen + 7840 is moved with `RIX::Node::SetX`) and a `widget_CardInfo` on the
  left: the password is looked up in the game's own `bin\CARD_Pass.bin` (`YuGiOh-PASS.h`, header only, loaded with the game's
  `Load_FileContent`: one u32 per internal id), a real card costs a flat 1,000 DP, is topped up to 3 copies and is shown in the card info while the message is up (a real
  `std::function` gives the box an OK button that highlights; an empty one only an OK prompt); anything else shows "This password is not for a card." in the
  screen's message box (`Screen::ShowMessageText` with an empty std::function: any confirm / cancel closes it; `ScreenBase::Tick` pauses the
  screen's Update while it is open). The mouse acts on clicks only (`TakeMouseClick`), not hover. Picking a card in the Card Shop only logs it so far.

## The Plugins list (Help & Options)

`Yu-Gi-Oh-RIX` adds a **Plugins** button to Help & Options. It opens a list on the main menu (one button per plugin, `Name: On/Off`),
Back returns to Help & Options.

* The list is in `[Yu-Gi-Oh-Core]` in `Config.ini` (Core owns it; RIX only draws the menu through Core's toggles. Older `[Yu-Gi-Oh-RIX]` / `[Plugins]` lists are not read): `Yu-Gi-Oh-Cards=1` for a DLL in `Plugins\` (the loader injects it when 1), `YGO-Ex/Yu-Gi-Oh-Funky=1`
  for a DLL in `Plugins\YGO-Ex\` (Yu-Gi-Oh-Core starts it when 1, once the main menu is up). The loader adds every plugin it finds as 0; `Yu-Gi-Oh-RIX` is always loaded.
* `PluginsPerPage` (3 to 7, default 6) is in the same section; the first page has a button that changes it.
* A button shows the DLL's name and no description. `<DLL name>.json` next to the DLL changes that:
  `{ "title": "Speed Hacks", "description": "Faster duels and no movies." }` (`Plugins\Yu-Gi-Oh-Cards.json`, `Plugins\YGO-Ex\Yu-Gi-Oh-Funky.json`).
* If the on/off state differs from what the game started with, leaving the list asks "restart now?" (the game's own Yes/No box). Yes runs the
  loader again (`--wait <pid>`, path from the `YGOEX_LOADER` and `YGOEX_LOADER_DIR` environment variables the loader sets) and closes the game; No goes on to Help & Options.

## The extra row on the Video Settings screen

`VideoScreen.cpp` in Yu-Gi-Oh-RIX adds **Plugins per page** (3 / 4 / 5, left / right, stored as `PluginsPerPage` in `[Yu-Gi-Oh-RIX]`) to Help & Options >
Video Settings (`RIX::ScreenHelpVideo`, screen 14). It first went on the Settings screen (13), which has no room for a fourth row; that version was dropped.

The screen is hard-wired to four rows: 0 Resolution, 1 Display Mode, 2 Apply, 3 Back (selection object at `+0x4B0`, the two buttons are MenuKit items at `+0x4F0`).
The new row is logic index 4 but is drawn between Display Mode and Apply:

* the constructor's row count (`mov edx, 4` at `0x14084ECFC`) becomes 5;
* `OnEnter` (`0x14084FF40`) is followed by creating the label (layout node 13) and value text (node 8) from the same templates Display Mode uses;
* the per-frame visuals (`0x140850790`) are hooked to move Apply and Back down (`sub_14075A310` sets a widget's y), highlight the label, set the value and put the arrows on the row;
* the selection function (`0x140868460`, shared by every screen) is hooked for this screen so up / down follow the drawn order `0, 1, 4, 2, 3`;
* the MenuKit description setter (`0x14089F6E0`) is hooked because the game reads the row's text id from a four row table (row 4 would read unrelated data);
* `Update` (`0x14084FB60`) is hooked to read left / right (`0x1408001B0`, `0x140800290`, bits 4 and 8) while the new row is selected.

Mouse hover does not select the new row. Positions (label 650, value 750, Apply 870, Back 980) are a first guess.

## Plugin settings screen (removed 2026-10-04)

Plugins > Plugin Settings and its own screen (RIX `SettingsScreen.cpp`, screen id 101) were removed on 2026-10-04 at the user's request
(not wanted in game). The Plugins button in Help & Options opens the on/off plugin list again. Plugin settings are edited in WolfX's
Config Editor, which reads the manifests' `"settings"` lists. How to make a screen of our own is still in docs/CustomScreens.md.
