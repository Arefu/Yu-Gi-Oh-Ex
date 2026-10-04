# Widget tracker

One row per widget class in `YuGiOh.exe` (RTTI `widget_*` vftables), with what is known about building it. Every address is named in `YuGiOh.exe.i64`.
For the fields and background of each class, see [Widgets.md](Widgets.md); for what pages can use, see [PageDesigner.md](PageDesigner.md).

**How this was found (2026-09-29):**

* **Constructor** = a function that writes the class's vftable *and* calls `widget_Base`'s constructor (`0x14087BF90`).
  * *inline* = no constructor of its own: a screen's constructor builds it in place (named in the last column).
* **Builder** = a caller of `RIX::widget_Base::CreateNode` (`0x14087C030`), which every `CreateFromLayout` calls to make the widget's node.
  * Builders were matched to classes by address: from `0x14087A970` on, each widget is its own source file, linked in class-name order, so a builder follows its class's constructor.
  * The first builder is named `::CreateFromLayout`, any others `::CreatePartN`.
  * The ones RIX already calls (TrunkZone, CardDetails, EntryDigit) were confirmed by use. Treat the rest as leads until each is read.
* **vftable layout** (5 slots, from `widget_Base` `0x1409FA680`):
  * 0: destructor (`0x140749170`)
  * 1: SetFocused (`0x14087C230`)
  * 2: an empty hook
  * 3: SetEnabled (`0x14087C240`)
  * 4: release nodes (`0x14087BFC0`)
  * Classes that override slot 1 have their own highlighted look; for example `YGO_FRONT::widget_MenuItem`, `widget_UserSlot` and `ScreenHelpControls::widget_Category`.
* **RIX** column: **page file** = a page designed in WolfEx builds it in the game; **plugin code** = `YuGiOh-RIX.h` can build it from a plugin; empty = not yet.

To add a class to RIX, the recipe used for the Trunk, CardDetails and EntryDigit:

1. Read its constructor for the size (the last field it zeroes, or the `new(size)` at a caller).
2. Read its `CreateFromLayout` for the arguments: usually `(widget, parent shared_ptr BY VALUE, z, owner = screen+88, x, y[, screenData = screen+120])`.
3. Find its per-frame `Update` / input function (called from the screen that owns it).
4. Find what feeds it data (a card id, a list, a string id).
5. Wrap all of that in `YuGiOh-RIX.h`, and teach `MenuFile.cpp` the page element that uses it.

| Class | vftable | Constructor | Builder(s) | Built by / notes | RIX |
| --- | --- | --- | --- | --- | --- |
| `YGO_FRONT::widget_MenuItem` | 0x140A759E8 | 0x1408A10C0 | 0x1408A1260, part 0x1408A1AF0 | main menu buttons (424 bytes) | page file |
| `RIX::MenuKit::widget_Item` | 0x140A6DAC0 | 0x140808FC0 | 0x140809CC0 (menu_item) | MenuKit menus: Battle Pack, Help, Pause (480 bytes) | page file |
| `widget_Title` | 0x140A71650 | inline | - | every screen, screen+152 (ScreenBase::Constructor) | page file (text only) |
| `RIX::widget_Dialog` | 0x140A7EE68 | 0x1408978C0 | 0x1408984A0, part 0x140898620 | every screen, screen+432 | plugin code |
| `RIX::widget_Dialog::dialogItem_t` | 0x140A7EE38 | 0x140898BC0 | - | one per dialog button (200 bytes) | plugin code |
| `RIX::widget_Help` | 0x140A7FAD8 | 0x14089E770 | 0x14089F220, part 0x14089F7D0 | every screen, screen+264 (prompt bar) | plugin code |
| `RIX::widget_ButtonHelp` | 0x140A7D2E0 | 0x14087CD30 | 0x14087CF80 |  |  |
| `RIX::widget_DialogOptions` | 0x140A7EFF0 | 0x1408995B0 | - | deck editor |  |
| `YGO_FRONT::widget_DialogWindow` | 0x140A7EF70 | 0x140898CF0 | 0x140898FB0, part 0x140899160 | duel dialogs |  |
| `YGO_FRONT::widget_DialogButton` | 0x140A52B08 | 0x140775F10 | - | duel dialogs (built inside 0x140779160, 0x14077ACA0, 0x14077C6B0) |  |
| `RIX::widget_SwipeDialog` | 0x140A812C0 | 0x1408B3A50 | - | deck editor |  |
| `RIX::widget_CommandMenu` | 0x140A7E4E8 | 0x1408900A0 | - | deck editor |  |
| `RIX::widget_CommandMenu::widget_helpItem` | 0x140A7E4B8 | 0x140890170 | 0x140890890, part 0x140890CF0 |  |  |
| `RIX::widget_CommandMenuItem` | 0x140A7E488 | 0x1408918F0 | 0x140891A70 |  |  |
| `RIX::widget_TextFrame` | 0x140A81348 | 0x1408B4F90 | 0x1408B5080 |  |  |
| `widget_BlockText` | 0x140A7D240 | 0x14087C250 | 0x14087C4D0 |  |  |
| `YGO_FRONT::widget_TextCrawl` | 0x140A73DD8 | 0x1408B4430 | 0x1408B4620 |  |  |
| `YGO_FRONT::widget_TextScroll` | 0x140A813A0 | 0x1408B54D0 | 0x1408B5730, parts 0x1408B5C80 0x1408B5E50 |  |  |
| `YGO_FRONT::widget_TouchText` | 0x140A52870 | 0x1408B3B40 | 0x1408B3F10 | inline in Battle Pack Draft/Store, Campaign Dialog, Card Shop, Side Deck Swap |  |
| `widget_EntryDigit` | 0x140A76118 | 0x140853240 | 0x14089DE70 | Live setting (session code) | plugin code |
| `widget_EntryCode` | 0x140A76148 | inline | 0x14089DB80 | ScreenLiveSetting::Constructor |  |
| `RIX::ScreenHelpSetting::tickLine_t` | (none) | 0x14084B770 | 0x14084B9B0 (Setup) | settings volume bars |  |
| `RIX::widget_SettingsLine` | 0x140A760E8 | 0x1408568E0 | - |  |  |
| `RIX::ScreenLiveSetting::widget_Item` | 0x140A76178 | inline | - | ScreenLiveSetting::Constructor |  |
| `RIX::widget_KeyEntry` | 0x140A7FCA8 | 0x14089FE90 | 0x1408A0930, part 0x1408A0C00 | controller settings |  |
| `RIX::ScreenHelpControls::widget_Category` | 0x140A74AD0 | 0x1408421A0 | - |  |  |
| `RIX::ScreenHelpControls::widget_KeyMapping` | 0x140A74B00 | 0x1408421F0 | 0x140842400, part 0x140842600 |  |  |
| `RIX::widget_BarH` | 0x140A7CF80 | 0x14087A970 | 0x14087AB80 |  |  |
| `RIX::widget_BarV` | 0x140A7D198 | 0x14087AEF0 | 0x14087B980 |  |  |
| `widget_User` | 0x140A82108 | 0x1408C0A60 | 0x1408C0C90 |  |  |
| `widget_UserSlot` | 0x140A73E78 | 0x1408404B0 (with PlayerName) | - |  |  |
| `RIX::widget_UserSlotManager` | 0x140A75C48 | inline | - | ScreenLiveLoading::Constructor |  |
| `RIX::widget_Card` | 0x140A7D938 | 0x140882640 | 0x1408829C0 |  |  |
| `widget_CardDetails (RIX 'CardInfo')` | 0x140A7DAE0 | 0x140883380 | 0x140883BA0 | deck editor card panel | plugin code |
| `widget_CardDetailsDuel` | 0x140A7DD40 | 0x140885210 | 0x140885D60 |  |  |
| `widget_CardDetailsFull` | 0x140A7DED8 | 0x140888080 | 0x140888660 |  |  |
| `RIX::widget_CardDisplay` | 0x140A7E098 | 0x14088B5A0 | 0x14088BC10 |  |  |
| `RIX::widget_CardDisplayFlex` | 0x140A7E200 | 0x14088D100 | 0x14088D820 |  |  |
| `RIX::widget_CardSortBar` | 0x140A7E2E0 | 0x14088F450 | 0x14088F700 |  |  |
| `RIX::widget_DeckCardsZone` | 0x140A7E800 | 0x140892670 | 0x1408931C0 | deck editor |  |
| `RIX::widget_DeckCardsZoneSide` | 0x140A7EA28 | 0x140893EF0 | 0x140894390, part 0x140894D00 | deck editor |  |
| `RIX::widget_DeckItem` | 0x140A7EB70 | 0x140896B70 | - |  |  |
| `RIX::widget_DeckList` | 0x140A7EBA0 | 0x140895B10 | 0x140896150 |  |  |
| `RIX::widget_DeckStats` | 0x140A7ECC0 | 0x140896C80 | 0x140896E10 |  |  |
| `RIX::widget_UserDeckSortBar` | 0x140A821D8 | 0x1408C1180 | 0x1408C1400 |  |  |
| `RIX::widget_UserDeckZone` | 0x140A822F0 | 0x1408C1AA0 | 0x1408C2150, part 0x1408C2800 |  |  |
| `RIX::widget_RecipeFilterBar` | 0x140A80888 | 0x1408A9E80 | 0x1408AA150 |  |  |
| `RIX::widget_RecipeInfo` | 0x140A80998 | 0x1408AA5C0 | 0x1408AACC0 |  |  |
| `RIX::widget_RecipeZone` | 0x140A80AC8 | 0x1408AD1C0 | 0x1408ADC70 |  |  |
| `RIX::widget_RecommendedList` | 0x140A80C30 | 0x1408AE6F0 | 0x1408AEEA0 |  |  |
| `RIX::widget_TrunkZone` | 0x140A81F08 | 0x1408BE760 | 0x1408BF050 | deck editor card grid | plugin code |
| `RIX::widget_TrunkBookmark` | 0x140A81528 | 0x1408B6250 | 0x1408B62A0 |  |  |
| `RIX::widget_TrunkFilterBar` | 0x140A81608 | 0x1408B67A0 | 0x1408B69F0 |  |  |
| `RIX::widget_TrunkFilterCategories` | 0x140A817B0 | 0x1408B7160 | 0x1408B8B00 |  |  |
| `RIX::widget_TrunkFilterCategory` | 0x140A81780 | 0x1408B9B30 | 0x1408B9D90 |  |  |
| `RIX::widget_TrunkFilterMaskOptions` | 0x140A81A00 | 0x1408BB120 | 0x1408BB950 |  |  |
| `RIX::widget_TrunkFilterMaskOption` | 0x140A81908 | 0x1408BA590 | 0x1408BA720 |  |  |
| `RIX::widget_TrunkFilterNumOptions` | 0x140A81CC8 | 0x1408BCD80 | 0x1408BD760 |  |  |
| `RIX::widget_TrunkFilterNumOption` | 0x140A81AD8 | 0x1408BC5F0 | 0x1408BC840 |  |  |
| `widget_PackZone` | 0x140A71AE0 | inline | - | ScreenBattlePackDraft::Constructor |  |
| `widget_PackPicker` | 0x140A71FD0 | 0x140828230 | - |  |  |
| `RIX::widget_PackWrapper` | 0x140A771B0 | 0x1408A2F50 | 0x1408A30D0, part 0x1408A3A50 |  |  |
| `RIX::ScreenBattlePackStore::widget_Reports` | 0x140A72000 | inline | - | ScreenBattlePackStore::Constructor |  |
| `RIX::widget_ReportWindow` | 0x140A80E70 | 0x1408B0660 | 0x1408B1160, part 0x1408B1420 |  |  |
| `RIX::widget_ReportWindow::widget_ReportItem` | 0x140A80E40 | 0x1408B1C10 | - |  |  |
| `RIX::ScreenCampaignDialog::widget_Panel` | 0x140A72610 | inline | - | ScreenCampaignDialog::Constructor |  |
| `RIX::widget_CampaignDuel` | 0x140A7D428 | 0x14087D4A0 | 0x14087D8B0 |  |  |
| `RIX::widget_CampaignRewards` | 0x140A7D820 | 0x14087E960 | 0x14087FAE0 |  |  |
| `RIX::widget_RewardWindow` | 0x140A7D7F0 | 0x1408B1D60 | 0x1408B1FE0, part 0x1408B2260 |  |  |
| `RIX::ScreenGameResult::widget_Standby` | 0x140A73ED8 | inline | - | ScreenGameResult::Constructor |  |
| `RIX::widget_PlayerName` | 0x140A73E48 | 0x1408404B0 (with UserSlot) | - |  |  |
| `widget_Hurry` | 0x140A73EA8 | inline | - | Game Result, Match Result, Side Deck Swap constructors |  |
| `RIX::widget_DuelBonusPoints` | 0x140A7F7A0 | 0x140899F20 | - |  |  |
| `RIX::widget_DuelStatsPItem (+ widget_FaceBox)` | 0x140A7F720 / 0x140A7F6F0 | 0x14089A470 | 0x14089B2E0, parts 0x14089B790 0x14089BC60 0x14089BFE0 |  |  |
| `RIX::widget_DuelStatsEItem` | 0x140A7F750 | 0x14089D950 | - |  |  |
| `RIX::ScreenLiveLeaderboard::widget_Category` | 0x140A84578 | 0x1408CC5A0 | 0x1408CCED0, part 0x1408CD0D0 |  |  |
| `RIX::ScreenLiveLeaderboard::widget_Row` | 0x140A845A8 | 0x1408CEE00 | - |  |  |
| `RIX::widget_Session` | 0x140A84B60 | 0x1408D4230 | - |  |  |
| `RIX::widget_SessionInfo` | 0x140A810C8 | 0x1408B2500 | 0x1408B2A20, part 0x1408B3350 |  |  |
| `RIX::ScreenSelectRung::widget_Body` | 0x140A77D88 | 0x14085F410 | 0x14085FEC0 |  |  |
| `RIX::ScreenSelectTutorial::widget_Item` | 0x140A781F0 | 0x1408647F0 | - |  |  |
| `RIX::widget_pickCharacter` | 0x140A80200 | 0x1408A4C50 | 0x1408A5540, part 0x1408A5910 |  |  |
| `RIX::widget_pickCharacter::widget_Item` | 0x140A801D0 | 0x1408A6B60 | - |  |  |
| `RIX::widget_pickSeries` | 0x140A80418 | 0x1408A6DD0 | 0x1408A7220, part 0x1408A7870 |  |  |
| `widget_LoadSpinner` | 0x140A57B98 | 0x1407B71F0 | - | also inline in 8 screens (Main Menu, Live screens, ...) |  |
| `YGO_FRONT::widget_PlayerHUD` | 0x140A52810 | 0x1407708D0 | - | with widget_Life inline |  |
| `YGO_FRONT::widget_PlayerHUD::widget_Face` | 0x140A52760 | 0x140770830 | - |  |  |
| `YGO_FRONT::widget_PlayerHUD::widget_Life` | 0x140A52790 | inline | - | widget_PlayerHUD::Constructor |  |
| `YGO_FRONT::widget_PlayerHUD::widget_Name` | 0x140A527C0 | 0x140770880 | - |  |  |
| `YGO_FRONT::widget_TurnHUD` | 0x140A52840 | 0x140770500 | - |  |  |
| `YGO_FRONT::widget_Cell` | 0x140A53710 | 0x14077CD10 | - | field zones |  |
| `YGO_FRONT::widget_Cell::widget_attributeLine` | 0x140A536E0 | 0x14077CFB0 | - |  |  |
| `widget_PendulumZoneMark` | 0x140A650B0 | 0x1407ED0F0 | 0x1407ED220, parts 0x1407ED740 .. 0x1407EDC80 |  |  |
| `widget_PhaseBar` | 0x140A5D948 | 0x140001830 | - |  |  |
| `YGO_FRONT::widget_PlayerStats` | 0x140A805B0 | 0x1408A7E40 | 0x1408A7FE0, part 0x1408A81B0 |  |  |
| `YGO_FRONT::widget_PlayerStats::widget_Item` | 0x140A80580 | 0x1408A8810 | - |  |  |
| `YGO_FRONT::widget_RawDuelStats` | 0x140A80770 | 0x1408A8BC0 | 0x1408A9130, part 0x1408A9330 |  |  |
| `YGO_FRONT::widget_RawDuelStats::widget_Item` | 0x140A80740 | 0x1408A9DC0 | - |  |  |
| `YGO_FRONT::DUEL_DIALOG_COIN::widget_Item` | 0x140A52CC8 | 0x140776960 | 0x140777170 |  |  |
| `YGO_FRONT::DUEL_DIALOG_DICE::widget_Item` | 0x140A53170 | 0x140778C60 | 0x140779600 |  |  |
| `YGO_FRONT::DUEL_DIALOG_SELECT_EFFECT::widget_Tab` | 0x140A53470 | 0x14077C250 | - |  |  |
| `YGO_FRONT::DUEL_DIALOG_SELECT_NAME::widget_Line` | 0x140A53EA0 | 0x140784C60 | - |  |  |
| `YGO_FRONT::DUEL_DIALOG_SELECT_GENERIC::widget_Line` | 0x140A5EDA0 | 0x1407CE8B0 | - |  |  |
| `YGO_FRONT::WINDOW_SELECT_GENERIC::widget_Line` | 0x140A65688 | 0x1407F1CE0 | - |  |  |
| `YGO_FRONT::DUEL_DIALOG_SELECT_PHASE::widget_PhaseIcon` | 0x140A543F0 | 0x140785F10 | 0x1407867F0, part 0x140786EC0 |  |  |
| `YGO_FRONT::DUEL_DIALOG_SELECT_STAND::widget_CardPos` | 0x140A548F8 | 0x140788C70 | 0x1407890F0 |  |  |
| `YGO_FRONT::Janken::widget_selectFrame` | 0x140A57830 | 0x1407AF280 | - |  |  |
| `YGO_FRONT::Janken::widget_PlayerID` | 0x140A57860 | 0x1407AF190 | - |  |  |
| `DFX::TLayerAnimoo (picture)` | 0x140A4E4B0 | 0x14075B580 | DFX::TBase::MakeChildWithContent 0x140744A10 | any sheet sprite | page file |
| `DFX::TLayerText (text)` | 0x140A4E798 | 0x14075DC60 | DFX::TBase::MakeChildWithContent 0x140744A10 | style DFX::TTextSpec at +152 | page file |
| `DFX::TLayerTexture (plain png)` | 0x140A4E890 | ? | ? | next: panels such as pdui/menuframe |  |

111 rows. Classes whose constructor RIX already wraps: TrunkZone, CardDetails, EntryDigit, Dialog, Help, MenuItem (main menu), MenuKit item (pages).
