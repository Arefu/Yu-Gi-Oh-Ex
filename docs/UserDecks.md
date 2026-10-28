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

## For plugins

`Core_GetExtraUserDecks(&firstId)` returns the loaded save's extra slot count and sets `firstId` to 1024; ask again rather than keep
the count, it changes when another save is loaded. `Deck_FromId` (0x14081AB40) answers those ids like 0-31.
Yu-Gi-Oh-Campaign's opponent deck picker lists them, and its decks.json `hideGameDecks` only hides built-in decks (32-731).
