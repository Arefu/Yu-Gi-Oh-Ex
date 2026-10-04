# Decks (deckdata_#.bin + decks.zib)

`main/deckdata_<L>.bin` (E F G I J S) lists the game's decks. Each deck's cards are in `decks.zib/<file>.ydc`. The parser is
`File Type Libraries/DeckData`:
* `DeckDataFile` reads and writes one language's file;
* `DeckDataTable` holds all languages together, plus the cards;
* `DeckJson` handles WolfEx's `decks.json`.

The editor is `Tools/Shared/Editors/DeckEditor.cs`, the **Decks** tab in WolfX and WolfEx. WolfX's older single-file "Deck Data" page is
still there. **Yu-Gi-Oh-Campaign** (`Decks.cpp`) applies `decks.json` to the game.

## File layout

u32 count (529), u32 0, then count x 56-byte records, then the strings (ASCII file name, then three UTF-16 texts, each zero terminated,
in record order).

| Offset | Field | Meaning |
|---|---|---|
| +0 / +4 | u32 Id, Slot | 1-699 (the game has 700 slots; Slot = Id). Duels use id + 32. The 5 starter decks are 380-384. |
| +8 | i32 Series | 0 DM ... 5 VRAINS; -1 for the starters |
| +12 | u16 SignatureCard | Konami id shown on the deck's info panel; 0xFFFF = none |
| +14 | u16 | never read |
| +16 | u32 CharacterId | the owner (chardata id). A character is in Free Duel only with a deck it owns; the list sorts by it; "<owner>'s <title>" |
| +20 | i32 Sku | content pack; -1 = 1, the base game |
| +24 | u64 | file name: `decks.zib/<name>.ydc` (the game finds it ignoring case) |
| +32 / +40 / +48 | u64 | Title, Text2, Text3 (UTF-16). Text2 is set on 6 English decks; Text3 is never set |

* Every language has the same records; only the texts differ.
* All six files round-trip byte for byte, and so do all 529 `.ydc` files.

**.ydc:** 8 header bytes (they vary and the game skips them), then for main, extra and side: a u16 count followed by that many u16 Konami
ids. The limits are **60 / 15 / 15** (`g_DeckSectionMaxSizes`); a bigger count makes the game reject the deck. decks.zib also holds some
`.ydc` files no deck uses (`*_unlockdeck_*`, `burn`).

## In the exe (IDB)

* **Records:** `LoadDeckDataFile` (0x1407FE500, in `LoadLanguageContent`) fills **`g_DeckRecords[700]`** (`DeckRecord` 0x88) through
  `DeckRecord_InitFromFileRecord`. The runtime record holds:
  * Slot, Series, CharacterId, SignatureCard, FileName;
  * Title / Text2 / Text3 as `std::wstring`s;
  * **StoryDuelId** (+0x78) and **Role** (+0x7C), then Sku.
* **Story decks:** `StoryDuel_FromFileRecord` (dueldata) marks a story duel's decks: StoryDuelId = the duel, Role = 1.
* **Roles:** `AssignDeckRoles` sets 0 for the starters, 2 for a selectable character's own deck, and 3 for a deck of another SKU.
  `RebuildLiveUnlockCounts` grants a role-3 deck's cards once its SKU is owned.
* **Cards:** `LoadDeckTemplatesFromDecksZib` (0x1407BD2E0) runs once at content load (`LoadGameContentOnce`) and parses every deck's
  `.ydc` into **`DeckTemplateList[700]`** (`YGO::SAVE::DeckListItem` 0x130).
  * A template holds Name (the title, 33 chars), counts, Main[60], Extra[15] and Side[15] as Konami ids, the record's Slot/Series, and IsValid.
  * The IDB struct had Extra and Side wrong at 20 each; that is fixed now.
* **Save:**
  * Player section +0x38 holds a u32 per deck id; 1 = unlocked, set by `MarkDeckStateKnown` when you beat the deck. An unlocked deck's
    recipe is available.
  * +0xB28 is a 700-bit set.

## decks.json (WolfEx → Yu-Gi-Oh-Campaign)

```json
{ "decks": [ { "id": 546, "file": "my_deck", "series": 0, "character": 192, "signatureCard": 4007, "sku": 1, "unlocked": true,
               "title": { "E": "My Deck" }, "cards": { "main": [4007, ...], "extra": [], "side": [] } } ] }
```

It lists only decks that differ from the game's or are new, each one whole. `cards` is included only when the card list differs from the
game's `.ydc`, or when the deck is new.
* **A game id** changes that deck; only the fields the entry gives are applied.
* **A free id** makes a new deck. It needs a `file` name; that name never has to exist in decks.zib when `cards` is given.
* **unlocked** (new decks, default false) marks the deck unlocked in the save.

Yu-Gi-Oh-Campaign:
* **`LoadDeckDataFile` (hooked):** applies the records and writes the titles with the game's `std::wstring::assign`. This runs before the
  story duels and `AssignDeckRoles`, so new decks can be used by characters and story duels.
* **`LoadDeckTemplatesFromDecksZib` (hooked):** fills the templates of entries that have `cards`.
* **`RebuildLiveUnlockCounts` (hooked):** sets the save state of unlocked new decks.

## Using a new deck

* **Free Duel opponent:**
  1. Make a deck owned by the character (Decks tab: Owner).
  2. Point the character at it (Characters tab: Deck).
  3. Keep the character selectable.
* **Story duel:** set the duel's player or opponent deck to it (Story editor: Duel tab).
* **"Used by"** in the Decks tab lists every character and story duel that uses the selected deck.

Built, but not yet run in the game. Yu-Gi-Oh-Campaign must be switched on in Config.ini: `[Yu-Gi-Oh-Core]` `Yu-Gi-Oh-Campaign=1`.
