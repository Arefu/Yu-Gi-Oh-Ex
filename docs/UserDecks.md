# Extra user decks (Core)

Credit: **Toon Jinzo**, whose Yu-Gi-Oh-DeckSlots found every hook point. Core's `UserDecks.cpp` is a port of it (Detours on the same
functions, the decks kept in the save), and Core's manifest `replaces` Yu-Gi-Oh-DeckSlots, so an old copy is no longer loaded.

## What changes

- No fixed cap: the save's 32 user decks plus, when a save is loaded, as many extra slots as it uses (up to its last deck) **+ 256 free**.
  The count follows the save, so it grows as decks are added (at the next load) and shrinks when the last ones are deleted. Always on,
  no setting. Until 2026-10-28 it was a fixed 224 extras, which dropped decks a DeckSlots save kept past them (issue #40).
- The extra decks have deck ids **1024 up** (user deck index 32 up), the ids DeckSlots used, so a deck picked before keeps its id. The
  only hard limit is the game's: `Deck_FromId` reads the index as an int16, so up to 32735 extras.
- They are kept in the save file's extension (tag `UserDecks`, after the 44008-byte blob; see `Core_GetSaveExtension`), so each save slot
  has its own and the vanilla game, which only reads the blob, never sees them.
- A save without the extension imports DeckSlots' side file once: `<save name without .dat>-extradecks.dat` next to the save
  (`savegame-ex-extradecks.dat`). It (and DeckSlots' `.bak` of it) is deleted after the next save write that put the decks into the save
  (`Save::AddWrittenListener`), never before, so a crash before that save loses nothing. Every deck keeps its slot and id,
  however many DeckSlots kept (its `TotalSlots` went to 1000).

## How the game does it (IDB, 2026-10-23)

| Function | Address | What Core does |
|---|---|---|
| `Deck_DecodeId` | 0x1407F9150 | id -> (type, index): 1024+k is type 1, index 32+k |
| `Deck_UserDeckAt` | 0x14076E8E0 | index 32 up -> the extra deck (another profile's blob gets an empty one) |
| `YGO::SAVE::Deck_GetValidation` | 0x14081AA20 | the profile's DeckValidation table holds ids 0-731; extras have their own, worked out when asked |
| `ProfileSave_CopyValidation` | 0x140871430 | the same, for a copy |
| `YGO::SAVE::Deck_Revalidate` | 0x1407BDFD0 | **no bounds check** in the game's: an extra only drops its cached validation |
| `YGO::SAVE::Deck_RevalidateAll` | 0x1407BDF20 | drops every extra's cached validation too |
| `PlayerSection_SetSelectedDeck` | 0x1407F93C0 | +4 selected id, +8 last user deck **index** (`ScreenDeckEditor::OnEnter` reads it through `Deck_UserDeckAt`) |
| `Deck_ListAll` | 0x14081AC80 | Refill's list (deck editor, deck pickers): used extras after the save's decks |
| `Deck_ListForSeatTable` | 0x14081ADC0 | `DeckSelectList_FillSeatTableDecks`' list: the same |
| `Deck_ListUserSlots` | 0x14081AEA0 | the deck editor's slot list (empty slots too: where a new deck goes) and its `%d/%d` counter |
| `YGO::RIX::DeckSelectList_FillDecks` | 0x1408AD890 | after the game's user deck loop (validation mask), the extras, then `RebuildTabs` 0x1408AE5B0 again |
| `GetOrInitProfileBuffer` | 0x140871500 | the profile's save was loaded (read the extension) or made new (empty) |
| `YGO::SAVE::Get_OrCreateBlob` | 0x140871490 | a blob made on first use, the owner only when no profile is loaded yet |

The first three lists each have one caller, so hooking the function is the same as DeckSlots' call-site patch. The save write
(`Save::AddWriteFilter`) puts the used extras into the extension, replacing DeckSlots' hook on `FinalizeChecksum`.

## Deck tags

Any user deck can carry tags, and the deck editor's list can be filtered by them, all on the game's own card filter panel
(`widget_TrunkFilterCategories`). Core's `DeckTags.cpp` keeps them in the save extension `DeckTags`, so each save slot has its own: u32 version 3,
u32 count, then count x { u32 deck id, u32 `RowsOn`, u64 `Picks[15]`, u64 `CardBits[15]`, i32 `Values[15][5]`, u32 chars, UTF-16 text }. Versions 1 (text only) and 2 (no values) still read.

- **Setting them:** on the deck list, Deck Options > **Edit Tags** opens the panel with the deck's tags, on its own at the left. Confirm adds an option
  to the row and confirming it again takes it off, so a row can hold several. Picks apply as they are made: the deck's info panel sits at the far right with its Tags row updating live. **Done** (Back, from the
  row list; from an option list Back first returns to the rows) leaves. There is no cancel, as with the game's own card filter. A row in the list shows at most two
  of its icons (the game's row has two icon slots); the Tags row shows them all.
- **Filtering:** Deck Options > **Filter Decks** opens the same panel for the list, with the deck list itself at the far right, narrowing as you pick. The row reads
  `Filter Decks (on)` while one is set. The panel's reset clears it. Options are there on an empty slot too, and the matching decks plus the first empty slot stay listed, so a filter that hides
  everything can still be changed and a new deck can still be made. The filter lasts until the editor is left, and only this list is filtered.
- **Matching:** every row in use must match; within a row, any pick is enough. The Name row's text matches when one of the filter's comma separated
  words appears in the deck's text.
- **Showing them:** the panel beside the list gets a Tags row: the picks as inline icons, up to five to a line and fewer when they are wide (a line breaks before the game would wrap it), at most `DeckTagMaxLines` lines (default 4) before the last one ends in `+More...`. A deck with only free-text tags shows no row, unless `DeckTagShowText=1` adds them, one to a line; the filter finds them either way.
  In `[Yu-Gi-Oh-Core]`: `DeckTagIconSpacing` (default 2, 0 to 6) is the spaces between icons and `DeckTagLineSpacing` (default 140, 100 to 300) the percent of the normal line
  height between lines. That sets `TTextSpec.LineScale` on the row's text, which `YGO::TEXT::Layout_Build` multiplies the font's line height by.
- **Rows** (table 0x140A7A260, read from each row's matching function): 1 card frame (`Get_Frame`), 2 Attribute, 3 Spell/Trap property, 4 Monster Type,
  8 Link Arrows, 12 limit status, 13 card genre (`Get_CardGenre`, [CardGenre.md](CardGenre.md)), 14 copies owned. There is **no archetype row** and no era row:
  use the free text for those. Rows 5, 6, 7, 9, 10, 11 are number rows (Level, Rank, Link Rating, Pendulum Scale, ATK, DEF). A deck's picks there are values of its own, shown as the
  row's icon and the value (`>=1000`). Filtering tests each of the deck's values with the filter's comparisons, the way the game tests a card, so a deck tagged Level 4 passes Level >=3.
- **Free text:** the Name row opens the game's Win32 text dialog (see [Widgets.md](Widgets.md)). There is no in-game text box.
- **Deleted decks:** a deleted deck reads as no tags at once; its entry is dropped the next time a deck is named. A new or copied deck starts with none.

How it hooks in (IDB 2026-11-02):

| Function | Address | What Core does |
|---|---|---|
| `ScreenDeckEditor_BuildDeckOptions` | 0x1408394A0 | After the game's rows, adds Edit Tags and Filter Decks and calls `CommandMenu_SetItems` 0x140891080 again. Buttons are made on demand and the box sizes itself, so a longer list fits |
| `ScreenDeckEditor_DoMenuAction` | 0x140836C30 | Actions 100 and 101 borrow the panel. The game's switch stops at 12 |
| `RIX::ScreenDeckEditor::Update` | 0x1408379F0 | After the game's update, Core shows the panel's picks on the preview. Closing the panel sets state 3 (the card grid): Core sees that, takes the picks and pops back |
| `RIX::ScreenDeckEditor::OnEnter` | 0x140838FB0 | Clears the list's filter |
| `RIX::ScreenDeckEditor::SetupWidgets` | 0x140838200 | The list's info panel is mode 1, which has no source row; Core builds `SourceHeading` and `SourceLine` from the same layout data modes 0, 2 and 4 use |
| `ScreenDeckEditor_ShowSelectedDeck` | 0x140839B30 | Fills the Tags row (U+E000 + icon id is an inline icon) |
| `TrunkFilterState_SetRowPick` | 0x140870030 | While borrowed, adds the option to the row instead of replacing the row's pick |
| `RIX__ScreenDeckEditor__OnSaveDialogResult` | 0x140835F20 | A new or copied deck starts with no tags |
| `Deck_ListUserSlots` | 0x14081AEA0 | (UserDecks' hook; its one caller is `DeckSlotList_Refresh`) drops what the filter hides |

The types are named in the IDB and in `Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-DECKEDIT.h`: `ScreenDeckEditor`, `widget_RecipeInfo`, `widget_TrunkFilterCategories`, `TrunkFilterState`, `TrunkFilterRow`, `TLayerText`, `TTextSpec`. `g_TrunkFilterState` 0x140C91C60 is global: `RowsOn`, `Picks` and `CardBits` per row, `ChangedFlags`, five `Values` per row, `NameText`. Core copies it
aside while the panel is borrowed and puts it back, so the player's card filter is untouched. `ScreenDeckEditor_SlideTo` 0x140839D80 moves the columns so the
panel sits at the left (x 33); the card grid is off screen and the panel's hit tests go through `ToLocal`, so the mouse follows.

## For plugins

`Core_GetDeckTags(deckId, out, size)` and `Core_SetDeckTags(deckId, "GX, Combo")` read and replace a user deck's free text (UTF-8, game thread only).
`Core_GetDeckTagMask(deckId, row)` gives its picks on one panel row, a bit per option.

`Core_GetExtraUserDecks(&firstId)` returns the loaded save's extra slot count and sets `firstId` to 1024; ask again rather than keep
the count, it changes when another save is loaded. `Deck_FromId` (0x14081AB40) answers those ids like 0-31.
Yu-Gi-Oh-Campaign's opponent deck picker lists them, and its decks.json `hideGameDecks` only hides built-in decks (32-731).
