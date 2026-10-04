# CARD_Genre.bin (card genres)

A card's **genres** are its effect categories: "Recover LP", "Special Summon", "Destroy Monster", "Toon Monster" and so on. The
card details page shows them as a list of icons and names. The duel code also checks them: `Has_CardGenre` has 50+ call sites,
for example the AI's card evaluation.

The parser is `File Type Libraries/CardGenre` (`CardGenreTable`, `CardGenreJson`). The editor is
`Tools/Shared/Editors/CardGenreEditor.cs`, on the **Card genres** tab in both WolfX and WolfEx.

## Layout

No header. The file is 10166 u64 bitmasks (81328 bytes), one per **internal** card id (CARD_INTID.bin maps a Konami id to it).
Bit n means the card has genre n. The copy in Remaining Files is identical to the game's.

| Bit | Key (ICON_ID_GENRE_*) | Game name | Bit | Key | Game name |
|---|---|---|---|---|---|
| 0 | LPUP | Recover LP | 27 | LEVELUP | LV Monster |
| 1 | LPDOWN | Damage LP | 28 | ORIGINAL | Original *(unused)* |
| 2 | DRAW | Help Draw | 29 | FUSION | Fusion Material Monster |
| 3 | SPSUMMON | Special Summon | 30 | RITUAL | Ritual |
| 4 | DISABLE | Negate effect | 31 | TOKEN | Token |
| 5 | DECKSEARCH | Search Deck | 32 | COUNTER | Counter |
| 6 | USEGRAVE | Recover from Graveyard | 33 | GAMBLE | Gamble |
| 7 | POWER | Increase/Decrease ATK/DEF | 34 | ATTR | Attribute-related |
| 8 | POSITION | Change battle position | 35 | TYPE | Type-related |
| 9 | CONTROL | Set controls | 36 | TUNER | Tuner |
| 10 | BREAKMONST | Destroy Monster | 37 | SYNC | Synchro Monster |
| 11 | BREAKMAGIC | Destroy Spell Card | 38 | DROPGRAVE | Send to Graveyard |
| 12 | HANDDES | Destroy Hand | 39 | NORMAL | Normal Monster *(hidden)* |
| 13 | DECKDES | Destroy Deck | 40-45 | ATTR_LIGHT/DARK/EARTH/WATER/FIRE/WIND | ... Attribute *(hidden)* |
| 14 | REMOVECARD | Remove Card | 46 | XYZ | Xyz Monster |
| 15 | CARDBACK | Return Card | 47 | LVUPDOWN | Level Modifier |
| 16 | SPEAR | Piercing | 48 | PENDULUM | Pendulum |
| 17 | DIRECTATK | Direct Attack | 49 | LINK | Link Monster |
| 18 | MANYATK | Attack multiple times | 50 | ATTR_DIVINE | Divine Attribute *(unused)* |
| 19 | UNBREAK | Cannot be destroyed | 51 | NEWCARD | New Card *(unused)* |
| 20 | LIMITATK | Limit Attack | 52 | GAMEORIGINAL | Game Original *(unused)* |
| 21 | CANTSUMMON | Cannot Normal Summon | 53 | VARIATION | Card Variation *(unused)* |
| 22 | REVERSE | Flip Effect Monster | | | |
| 23-26 | TOON / SPIRIT / UNION / DUAL | Toon / Spirit / Union / Gemini Monster | | | |

The display names are UI strings (`g_GenreNameStringIds` at 0x140A51E10; 806 "Recover LP" to 865 "Link Monster"). A genre's icon
id is bit + 175.

* **Hidden:** `Get_GenreFromKonamiId` clears bits 39-45 (mask `0x3F8000000000`), so they never reach the game even though the
  file sets them.
* **Unused:** the game has names for bits 28 and 50-53, but no card in the file uses them.

## In the exe (IDB)

* `Setup_CardPropTable` loads the file into `g_CardDataFiles.CardGenre` (+0x90).
* `Get_GenreFromKonamiId` (0x14076D420) goes from Konami id to internal id to the mask. Its only caller is
  `Setup_FullCardProps`, which stores the result in `FULL_CARD_PROPS[id].field_38`. **Custom cards (past 14968) get entry 0,
  meaning no genres.**
* `Get_CardGenre` (0x140819FD0) returns the mask. `Has_CardGenre` (0x14081A050) and `Has_CardGenre_Thunk` (0x1407EB9F0)
  test one bit and are used all over the duel code. For example, `Duel_GenreCategory_1402B86C0` ranks a card by DECKDES,
  BREAKMONST, CARDBACK and BREAKMAGIC.
* `CardDetails_ShowEffectOrGenres` (0x14088AA60) is the card details text box. Mode 1 lists each set genre (bits 0-53) with
  `Genre_IconId` and `Get_GenreName`.
* The IDB has the enum `CardGenreBit`.

## WolfX / WolfEx

* **WolfX:** open a CARD_Genre.bin. CARD_IntID.bin must be in the same folder. Tick and untick genres, then save; a `.bak` of the
  original is kept. A card that isn't in CARD_IntID.bin, such as a custom card, has no slot in the file.
* **WolfEx:** opens the game's genres with `Yu-Gi-Oh-Ex\genres.json` on top. **New card...** gives genres to any card, custom
  cards included. Save writes each changed card's full genre list:

```json
{ "cards": [ { "card": 15300, "name": "My Card", "genres": [ "DRAW", "SPSUMMON", "LINK" ] } ] }
```

A genre can be written as its key, its game name ("Help Draw") or its bit number.

## In the game: Yu-Gi-Oh-Cards (`Genres.cpp`)

Yu-Gi-Oh-Cards reads genres.json on every card setup (startup and language change). Each listed card gets exactly the genres the
JSON gives it, written to `FULL_CARD_PROPS[id]` +0x38. Bits 39-45 are cleared, as the game does.
* **Custom cards:** set in `WriteGameTableEntry`, and a custom card the JSON doesn't list gets no genres. When a custom card
  borrows a vanilla id for a duel, `BorrowScratchId` gives the borrowed entry the custom card's genres. Before this fix it kept
  the vanilla card's genres, so the duel code judged the custom card by some other card's categories.
* **Game cards:** patched after `Card::Install` (`Genres::ApplyToGameCards`).

The log line `genres.json: genres for N card(s)` shows what was loaded, and it counts any genre names it didn't recognise.

Tested: both copies round-trip byte for byte. Rebuilding the file from the per-Konami-id view gives the identical file. A JSON
diff applied again gives the same masks. Pot of Greed comes out as exactly "Help Draw".
