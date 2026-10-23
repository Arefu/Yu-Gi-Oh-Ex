# Deck size limits

Yu-Gi-Oh-Core (`DeckLimits.cpp`, 2026-10-22). `[Yu-Gi-Oh-Core]` in Config.ini, also on WolfX's Config page:

| Key | Default | Range | What it changes |
|---|---|---|---|
| `MinMainDeck` | 40 | 1 to 60 | The fewest Main Deck cards a usable deck has |
| `MaxMainDeck` | 60 | MinMainDeck to 60 | The most Main Deck cards (legality check and deck editor) |
| `MaxExtraDeck` | 15 | 0 to 15 | The deck editor's Extra Deck cap |
| `MaxSideDeck` | 15 | 0 to 15 | The deck editor's Side Deck cap |

Speed Duel decks: `MinMainDeck=20`, `MaxMainDeck=30`, `MaxExtraDeck=5`. The limits can only go down: a saved deck
(`YGO::SAVE::DeckListItem`, 304 bytes) holds 60 main, 15 extra and 15 side cards.

Plugins change them while the game runs with `Core_SetDeckLimits(min, max, extra, side)` (Yu-Gi-Oh-Core.h; a negative min = Config.ini's
again). Yu-Gi-Oh-MP does for an online lobby's Duel format ([DuelFormat.md](DuelFormat.md)).

## How the game checks a deck (traced 2026-10-22, all named in the IDB)

* `YGO::GAME::Deck_ValidateAndCountByInternalId` (0x1407F9B60) is the **only** legality check. It returns a
  `DeckValidation { Flags, OwnedCount, TotalCount }`. Flags (`DeckValidationFlag`): 1 more copies than owned, 2 more copies than
  the card's limit, **4 Main Deck size outside 40 to 60**, 8 deck not valid (`DeckListItem.IsValid` = 0), 0x10 no cards.
  Extra and Side have no minimum and no maximum here.
  * minimum: `mov eax, 28h` at 0x1407F9CDE (imm32 at 0x1407F9CDF)
  * maximum: `cmp cx, 3Ch` at 0x1407F9CF8 (imm8 at 0x1407F9CFB, sign-extended, so 60 or less)
* The results are cached per deck (12 bytes each at ProfileSaveManager+16) by `Deck_RevalidateAll`, `Deck_Revalidate` and
  `RebuildLiveUnlockCounts`; `Deck_GetValidation` (0x14081AA20) reads the cache. `DeckSelectList_FillDecks` hides decks whose
  flags match its mask, the Live lobby (`LiveSetting_ValidateSelectedDeck`) needs flags 0.
* `g_DeckSectionMaxSizes` (0x140A521D8, int[3] main/extra/side) is the deck editor's "this part is full" cap
  (`Deck_SectionHasRoom`, `DeckEdit_ShowSectionFullMessage`, `Deck_SetSectionCards`).
* `Deck_CopyCardsClamped` (0x140756280) clamps to the storage sizes 60/15/15 with its own immediates; those are buffer bounds and stay.
* Not a rule: Battle Pack's `cmp word [r13+42h], 28h` (0x14081E749) only picks the opponent's deck family.

## The text

The numbers are written in the game's strings (Strings_STEAM_<lang>.BND, id = BND index + 1):
637 "The Side Deck may not have more than 15 cards.", 638 Main (60), 639 Extra (15), 764 " Less than 40 cards".
Core hooks `YGO::LOCAL::Load_StringFiles` (0x140870E50, startup and language change) and points those entries at copies with
the new number, in every language that writes the number with ASCII digits.
