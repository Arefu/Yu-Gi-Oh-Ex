# Game UI widgets (RIX / YGO_FRONT)

Catalogue of every widget class in `YuGiOh.exe`, taken from the RTTI names and the `??_7widget_*` vftables in the IDB.
There are 112 widget vftables. **Verified** means the code was read; everything else is inferred from the class name and where it is used, so treat it as a lead.

**[WidgetTracker.md](WidgetTracker.md)** has one row per class with its constructor, builder (`CreateFromLayout`), which screens build it, and whether RIX can build it. All of those are named in the IDB (2026-09-29).

Correction from the tracker:
* The main menu's 424-byte buttons are `YGO_FRONT::widget_MenuItem` (vftable `0x140A759E8`, constructor `0x1408A10C0`).
* `RIX::MenuKit::widget_Item` is a different class: 480 bytes, vftable `0x140A6DAC0`, constructor `0x140808FC0`, used by the Battle Pack, Help and Pause menus.
* Older text below that calls the main menu buttons `MenuKit::widget_Item` means `widget_MenuItem`.

## Summary for plugin authors

* There is **no checkbox or on/off toggle** widget. The only stepped control is `RIX::ScreenHelpSetting::tickLine_t` (a row of 10 ticks, `Level1`..`Level10`, used for the volume bars).
* The widget that would carry a *choice* is a menu button (`MenuKit::widget_Item`) whose label you change with `SetLabelById`. That is how the Plugins list should show On/Off.
* Text entry exists only as `widget_EntryCode`/`widget_EntryDigit` (digit-by-digit code, used by the Live setting screen) and `widget_KeyEntry` (key rebinding). There is no free-text field.
* All widgets derive from `RIX::widget_Base` (vftable `0x1409FA680`, 6 slots, base fields `+8 .. +0x2F`; constructor `sub_14087BF90`). Derived widgets are copied with move constructors that take the source widget as the second argument.

## What Yu-Gi-Oh-RIX covers (2026-09-29)

RIX covers **7 of the 112** widget classes. Everything else is catalogued here and in the WolfEx page designer ([PageDesigner.md](PageDesigner.md)), which draws all of them as a preview, but nothing builds them yet.

| Widget | How RIX covers it | Where |
| --- | --- | --- |
| `MenuKit::widget_Item` (menu button) | Built from files with no code: main menu and options buttons (`menus/*.json`), and page buttons (`pages/*.json`, up to 4, 100 px apart) | `Yu-Gi-Oh-RIX.h` (`RIX_AddMainMenuButton`, `RIX_OpenPage`), MenuFile.cpp |
| `widget_Title` / `MenuKit::widget_Header` (screen title) | The page header text (files or `RIX_PageDesc::Header`); the game keeps its position | Pages.cpp (`SetHeaderText` hook) |
| `MenuKit::widget_Description` | Each button's description | same |
| `widget_Dialog` (message, Yes/No) | Plugin code: `ShowMessageText`, `ShowYesNo`, `DialogClear` / `SetMode` / `SetText` / `AddItem` | `YuGiOh/YuGiOh-RIX.h` |
| `widget_Help` (prompt bar) | Plugin code: `HelpClear` / `HelpAdd` / `HelpLayout`, `Input::HelpBarPressed` | `YuGiOh/YuGiOh-RIX.h` |
| `widget_TrunkZone` (card grid) | Plugin code: `Trunk::Construct` / `CreateFromLayout` / list building / `Update` | `YuGiOh/YuGiOh-RIX.h`; used by BetterCardShop |
| `widget_CardInfo` (card picture and text) | Plugin code: `CardInfo::Construct` / `CreateFromLayout` / `SetWidth` / `SetCard` / `Update` | `YuGiOh/YuGiOh-RIX.h`; used by BetterCardShop |
| `widget_EntryDigit` (0-9 wheel) | Plugin code: `EntryDigit::Construct` / `CreateFromLayout` / `SetValue` / `SetSelected` / `HitTest` | `YuGiOh/YuGiOh-RIX.h`; BetterCardShop's password page |
| Pictures (`DFX::TLayerAnimoo`) and text (`DFX::TLayerText`) | Built from page files (`image`, `text` elements); plugin code: `Dfx::AddImage`, `Dfx::AddText`, `Dfx::RemoveImage` | `YuGiOh/YuGiOh-RIX.h`, MenuFile.cpp |
| `widget_Base` (any widget) | `SetEnabled`, `WidgetSetFocused`, `WidgetSetVisible`, `NodeSetX` / `NodeSetY` / `NodeSetPosition`, `ParentRef` | `YuGiOh/YuGiOh-RIX.h` |

Traced in IDA but not wrapped yet:

* `ScreenHelpSetting::tickLine_t::Setup` 0x14084B9B0 (the 10-tick volume bar).
* The scene graph under every screen (traced 2026-09-29, all named in the IDB):
  * `DFX::TBase` node, 0xC8 bytes: `MakeShared` 0x140759190, `AddChild` 0x140758EA0, `RemoveChild` 0x140759DD0, `SetZ` 0x14075A4B0, `SetScaleXY` 0x14075A350, `SetFlag` 0x14075A2A0 (0x8 = visible), `SetAlpha` 0x14075A1E0.
  * Drawables sit inside a TBase via `MakeChildWithContent` 0x140744A10:
    * `TLayerAnimoo`, one sprite: `MakeShared` 0x14075B580, `SetResource` 0x14075BAB0 (sheets load on first use), `SelectByName` 0x14075BB00. Its player+48 holds the alignment bits.
    * `TLayerText`: `MakeShared` 0x14075DC60, style `DFX::TTextSpec` at +152, text set by `SetTextById` 0x14075E000.
* `widget_EntryCode` (a row of digit wheels; RIX builds the row itself from `EntryDigit`).

## Layouts decoded so far

**`RIX::widget_Base`** (vftable `0x1409FA680`, constructor `sub_14087BF90`, 0x30 bytes; every widget starts with it). **Verified.**

| Offset | Meaning |
|---|---|
| `+0x00` | vftable. Slots: 0 destructor (`0x140749170`), 1 set flag byte at `+8` (`0x14087C230`), 2 empty (`nullsub`), 3 set enabled on the element (`0x14087C240`), 4 release the element (`0x14087BFC0`), 5 not decoded. |
| `+0x08` | byte flag (set by slot 1). |
| `+0x10` / `+0x18` | `std::shared_ptr` to the widget's **element**, a `DFX::TBase` scene node (pointer, control block). |
| `+0x20`, `+0x28` | zero at construction, not decoded. |

The element's flags dword is at `+0x88` (136). `widget_Base::SetEnabled` (0x14075A490) sets or clears bit `0x8` there, so "enabled" is a bit on the element, not on the widget.

**`MenuKit::widget_Item`** (menu button, 424 bytes, vftable `YGO_FRONT::widget_MenuItem` `0x140A759E8`). **Verified from `Constructor` and `CreateFromLayout`.**

| Offset | Meaning |
|---|---|
| `+0x00..0x2F` | `widget_Base`. |
| `+0x30` | child element (shared_ptr, 16 bytes) created with layout type 1 (built by `sub_14077A110`). |
| `+0x50` | child element created with layout type 0 (`unk_140A7FD90`), takes the width/height arguments. |
| `+0x70` | child element created with layout type 2 (`unk_140A7FD70`); **disabled at creation**. |
| `+0x90` | animation object (`DFX::AnimXY<shared_ptr<TBase>,float>`, later `AnimSpr`); `+0xB8` holds its callback (`nullsub` by default). |
| `+0xA0` byte, `+0xA4`, `+0xAC` (=1.0f), `+0x180`, `+0x184` (=1.0f), `+0x1A0` byte | state flags and scale values, set by the constructor. Meaning not decoded. |

`CreateFromLayout(item, layoutNode, index, parent, width, height, screenData)` is the shared builder (see [MenuSystem.md](MenuSystem.md)).

**`ScreenHelpSetting::tickLine_t`** (408 bytes, `Constructor 0x14084B770`): a `widget_Base` (`+0`), then four zeroed qwords at `+0x30..0x4F`, then 10 tick sub-objects of 32 bytes at `+0x50` (`__vec_ctor(+80, 32, 10)`). `Setup 0x14084B9B0` builds the ticks from art `pdui/settings` (element type 34, positions `x = 949 + 49 * i`, `y = -19`), names each one `Level<i>`, then sets the active count at `+0x190` to 10. **Verified.**

**`RIX::widget_Dialog`** - the game's message / Yes-No box. **Verified.** Every screen owns one at **`screen + 432` (0x1B0)** (built in `ScreenBase::Constructor`), and the global `PopcornCore` app object owns another.

| Item | Detail |
|---|---|
| Size | 0xB0 bytes: `widget_Base` (0x30) + fields to `+0xAC`. Vftable `0x140A7EE68`. |
| `+0x90 / +0x98 / +0xA0` | `std::vector<dialogItem_t>` (begin, end, capacity); one **200 byte** (0xC8) item per button. |
| Item | `+0x00` vftable (`dialogItem_t`, a `widget_Base`), `+0x10` button element, `+0x40` and `+0x50` label / hover parts, `+0x60` hit box (floats at `+0x1C..+0x28` = left, top, right, bottom), `+0x70` label string id, `+0x78` input mask (`0x1000` first button = confirm, `0x2000` second = cancel), `+0x80` `std::function<void()>` callback (MSVC layout, impl pointer at `+0xB8`), `+0xC0` sound id (-1 = default). |
| Layout | one button: x = 189. Two buttons: x = -270 and +270, y = 189 (`LayoutItems 0x140897EB0`). |
| Input | `ProcessInput 0x140898040` hit-tests the mouse against each item, then fires confirm (`0x1000`, sound 0x27) or cancel (`0x2000`, sound 0x12). |

Ways to open one on a screen (all take the **screen** pointer first; text is a string-table id from 1 to 1213, or any pointer to a wide string):

| Function | Buttons | Arguments |
|---|---|---|
| `RIX::Screen::ShowMessage` (0x140822C80) | OK (label 901) | `(screen, sfx or -1, textIdOrPtr, std::function<void()>* onOk)` |
| `RIX::Screen::ShowMessageText` (0x140822BA0) | OK | same, text is always a `wchar_t*` |
| `RIX::Screen::ShowYesNo` (0x1408229D0) | Yes (918) and No (900) | `(screen, yesSfx, textIdOrPtr, std::function<void()>* onYes)`; **only Yes has a callback**, No just closes |
| `RIX::Screen::ShowYesNoText` (0x1408228D0) | Yes / No | same, text is always a `wchar_t*` |
| `RIX::Screen::ShowErrorMessage` (0x140822250) | none | `(screen)`, fixed text id 1173 |

Under the hood they call `Clear 0x1408987F0`, `SetMode 0x1408988A0` (0 message, 1 Yes/No), `SetText 0x1408988E0`, `AddItem 0x140897BC0(dialog, labelId, std::function*, sfx)`, then virtual slot 3 (show) and set `screen+48 = 1`. Building a Yes/No picker for a plugin therefore needs: the **active screen pointer**, a wide string, and a small callback stored inline in a `std::function` (a captureless function pointer keeps it inline, so the game can destroy it safely). The active screen pointer is the open question (`CurrentScreenId` is unreliable, see [MenuSystem.md](MenuSystem.md)); the RIX menu hook already has the main menu screen as `this`.

**Other classes, layouts from their constructors** (bytes zeroed after `widget_Base`; **sizes are minimums**, the last written offset):

| Class | Base / notes | Reaches to |
|---|---|---|
| `widget_CommandMenu` | `+0x30..0x68` fields, `+0x70` array of 2 x 0x70 byte `widget_helpItem`, `+0x150..` vectors, `+0x168` a `ScreenBase` sub-object, `+0x198` name string `"widget_CommandMenu"`. **Used only by the deck editor.** | 0x1B8 |
| `widget_DialogOptions` | derives from `widget_CommandMenu`, adds one qword at `+0x1D0`. Deck editor only. | 0x1D8 |
| `widget_SwipeDialog` | `+0x30..0x6F` fields, `+0x70` a text block, `+0xA0` array of 4 x 0x80 bytes, embedded `widget_TouchText` at `+0x2A0`. Deck editor only. | 0x320 |
| `widget_Help` | 14 qwords `+0x30..0x9F`, float `1.0` at `+0xA0`. One per screen at `screen+264`. | 0xA4 |
| `widget_ButtonHelp` | `+0x30` a 40 byte text-line helper, then fields to `+0xA7`. | 0xA8 |
| `widget_KeyEntry` | 16 qwords `+0x30..0xAF`, byte at `+0x4B0`. | 0x4B1 |
| `widget_TextFrame` | 8 qwords `+0x30..0x6F`, dword `+0x74`. | 0x78 |
| `widget_BarH` | 16 qwords `+0x30..0xAF`, two dwords `+0xB0`, `+0xB4`. | 0xB8 |
| `widget_Title` | embedded in every screen at `screen+152`; eight qwords `+0x30..0x6F`. | 0x70 |

`widget_DialogWindow` (0x140898CF0) is the duel-side dialog (YGO_FRONT): word `0x101` at `+0x30`, dword `+0x34`, qwords to `+0x80`, float `1050.0f` (`0x44834000`) at `+0x80`.

**Not decoded yet:** everything else in this document. The list below names the classes and their purpose only. Widgets are copied with move constructors (the ones that take the source widget as the second argument), so a class's true size is easiest to read from where it is allocated or embedded (for example the 424-byte `widget_Item` vector stride in `ScreenHelpVideo`).

## Menu widgets (RIX::MenuKit) - used by RIX plugin

| Class | Notes |
|---|---|
| `MenuKit::widget_Item` | The menu button (424 bytes). `Constructor 0x1408A10C0`, `CreateFromLayout 0x1408A1260`, `SetLocked 0x1408A1880`, `SetLabelById 0x1408A1900`. **Verified.** |
| `MenuKit::widget_Description` | Description text under a menu. `SetTextById 0x14089F6E0`. |
| `MenuKit::widget_Header` | Menu title text. `SetTextById 0x1408B5E20`. |
| `widget_Text` | Plain text. `SetTextById 0x14075E000`. |
| `widget_Base::SetEnabled` (0x14075A490) | Enables or greys any widget. |

## Settings and input

| Class | Notes |
|---|---|
| `ScreenHelpSetting::tickLine_t` | 10-tick stepped bar, 408 bytes, built by `Setup 0x14084B9B0`. Volume bars. **Verified.** |
| `widget_SettingsLine` | Row in the Live setting screen (also `widget_Item@ScreenLiveSetting`). Moves only seen; layout not decoded. |
| `widget_EntryCode`, `widget_EntryDigit` | Numeric code entry (Live session code). Created in `ScreenLiveSetting::Constructor`. |
| `widget_KeyEntry` | Key rebinding cell. |
| `ScreenHelpControls::widget_Category`, `widget_KeyMapping` | Controls screen rows. |
| `widget_UserSlot`, `widget_UserSlotManager`, `widget_User` | Profile slot pickers (Live loading, sign in). |

## Dialogs and prompts

| Class | Notes |
|---|---|
| `widget_Dialog` (+ `dialogItem_t`) | Generic message box with buttons. State starts at `+0x30`. |
| `widget_DialogOptions` | Option list inside a dialog. |
| `widget_SwipeDialog` | Dialog that slides in. |
| `widget_DialogWindow`, `widget_DialogButton` (YGO_FRONT) | Duel dialog frame and button. |
| `widget_Help`, `widget_ButtonHelp` | Help text and the button-prompt bar at the screen bottom. |
| `widget_CommandMenu`, `widget_CommandMenuItem`, `widget_CommandMenu::widget_helpItem` | Context command menu (deck editor and similar). |
| `widget_TextFrame`, `widget_BlockText`, `widget_TextCrawl`, `widget_TextScroll` | Framed, block, crawling and scrolling text. |

## Bars, lists and layout

| Class | Notes |
|---|---|
| `widget_BarH`, `widget_BarV` | Horizontal and vertical bars (scroll/gauge). |
| `IScrollable` | Scroll interface used by lists. |
| `widget_Title`, `widget_LoadSpinner`, `widget_Hurry`, `widget_PhaseBar` | Screen title, loading spinner, timer, duel phase bar. |
| `ScreenBattlePackStore::widget_Reports`, `widget_ReportWindow`, `widget_ReportItem` | Pack opening reports. |
| `ScreenCampaignDialog::widget_Panel`, `widget_CampaignDuel`, `widget_CampaignRewards`, `widget_RewardWindow` | Campaign panels. |
| `ScreenGameResult::widget_Standby`, `widget_PlayerName`, `widget_DuelBonusPoints`, `widget_FaceBox` | Result screen. |
| `ScreenMatchResult::icon_t`, `widget_DuelStatsPItem`, `widget_DuelStatsEItem` | Match result stats. |
| `ScreenLiveLeaderboard::widget_Category`, `widget_Row`; `widget_Session`, `widget_SessionInfo` | Leaderboard and lobby rows. |
| `ScreenSelectRung::widget_Body`, `ScreenSelectTutorial::widget_Item`, `widget_pickCharacter` (+`widget_Item`), `widget_pickSeries` | Selection screens. |
| `widget_PackZone`, `widget_PackPicker`, `widget_PackWrapper` | Card pack shop widgets. |

## Card and deck widgets

| Class | Notes |
|---|---|
| `widget_Card`, `widget_CardDisplay`, `widget_CardDisplayFlex` | A card, and grids of cards. |
| `widget_CardDetails`, `widget_CardDetailsDuel`, `widget_CardDetailsFull` | Card detail panel (three variants). |
| `widget_CardSortBar`, `widget_UserDeckSortBar` | Sort selectors. |
| `widget_DeckCardsZone`, `widget_DeckCardsZoneSide`, `widget_DeckItem`, `widget_DeckList`, `widget_DeckStats`, `widget_UserDeckZone` | Deck editor. |
| `widget_RecipeFilterBar`, `widget_RecipeInfo`, `widget_RecipeZone`, `widget_RecommendedList` | Recipe browser. |
| `widget_TrunkZone`, `widget_TrunkBookmark`, `widget_TrunkFilterBar`, `widget_TrunkFilterCategory/Categories` | Card collection ("trunk") and its filter bar. |
| `widget_TrunkFilterMaskOption(s)` | Bitmask filter options (attribute, type, ...). Probably the closest thing to a multi-select tick list, but not decoded, so treat that as a guess. |
| `widget_TrunkFilterNumOption(s)` | Numeric range filter options. |

## Duel widgets (YGO_FRONT)

`widget_PlayerHUD` (+ `widget_Face`, `widget_Life`, `widget_Name`), `widget_TurnHUD`, `widget_TouchText`, `widget_Cell` (+ `widget_attributeLine`), `widget_PendulumZoneMark`, `widget_MenuItem`, `widget_PlayerStats`, `widget_RawDuelStats`, and the per-dialog items `DUEL_DIALOG_COIN/DICE::widget_Item`, `DUEL_DIALOG_SELECT_EFFECT::widget_Tab`, `DUEL_DIALOG_SELECT_NAME/GENERIC::widget_Line`, `DUEL_DIALOG_SELECT_PHASE::widget_PhaseIcon`, `DUEL_DIALOG_SELECT_STAND::widget_CardPos`, `WINDOW_SELECT_GENERIC::widget_Line`, `Janken::widget_selectFrame`, `Janken::widget_PlayerID`.

## Not widgets, but useful

`RIX::MenuKit::menu` (`SetDefinition 0x14080A210`, `CreateItems 0x140809E90`, `ProcessInput 0x1408096C0`, `ShowItem 0x140809580`), `MenuKitCallbackI` (the callback interface a `ScreenBaseMenu` screen implements), `ScreenBaseMenu` (base of Help, Pause and LiveMenu), `ScreenBase`/`ScreenBase2`, `ScreenSwitcher`.
