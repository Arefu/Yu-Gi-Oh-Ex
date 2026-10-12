# Custom cards: everything in cards.json

`<game>\Yu-Gi-Oh-Ex\cards.json` holds every custom card (ids 15300-19999) and **everything about it**: stats, text in every
language, art, archetypes, same name, summoning requirements, effect, and the parts the game keeps in other tables for its own
cards (genres, related cards, links, password, name order, index letters). WolfX's **New cards** page writes it; Yu-Gi-Oh-MoreCards
(`Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Cards`) reads it.

Game cards are unchanged: their edits go into the game's files (WolfX patch archive) or the page's own Yu-Gi-Oh-Ex JSON.

## Ownership when content is unavailable

MoreCards resets saved ownership for unavailable custom cards by default, retaining the original behavior when switching mods.
To keep those copies instead, enable **PreserveUnavailableCards** in More Cards' plugin settings or add this to `Config.ini`:

```ini
[Yu-Gi-Oh-MoreCards]
PreserveUnavailableCards=1
```

Restart the game after changing the option. `0` (or an omitted key) resets unavailable custom ids; `1` preserves them when a card
is removed or its JSON cannot be loaded. In both modes, only loaded cards appear in the trunk. With preservation enabled, restoring
the same card id makes its saved copies available again. The `copies` value remains a minimum grant, so use `copies: 0` when checking
persistence. This cannot recover counts already erased without a backup or a new grant. The option applies while MoreCards is loaded.

To verify a MoreCards build, use Core with a separate toolkit save and back up both that profile and the Steam save:

1. Enable `PreserveUnavailableCards=1`. Give card 15300 two copies in the test save, keep it out of saved decks, and load a valid `cards.json` entry with `copies: 0`.
2. Open Deck Edit and check that the trunk shows two copies. Save and exit normally.
3. Temporarily remove `cards.json`, relaunch, and open the trunk. The unavailable card should be hidden. Save and exit.
4. Repeat with malformed JSON, then restore the valid entry and relaunch. The trunk should show two copies again without a grant.
5. Check the saved ownership bytes after every save, including another absent custom id and id 19999. Verify valid checksums,
   an increased save counter, and an unchanged Steam save. Restore the original content and configuration after testing.
6. Repeat from a fresh copy of the test save with the option omitted, then explicitly set to `0`. Opening the trunk should reset
   unavailable custom ids, while preserving ownership for loaded cards. Restore the removed entry and check that it receives no
   copies without a grant. Use a separate save for each mode so the intentional reset does not destroy the preservation fixture.

## Keys (besides id, name, description, image, kind, type, attribute, icon, level, atk, def, scale, linkmarkers, limitation, copies, archetypes, sameName, effectClone, effectScript, summoning keys)

| Key | Example | Game table it stands for | Read by |
|---|---|---|---|
| `"atk"` / `"def"` | `"?"` | CARD_Prop's 511 = unknown | MoreCards: props 511, FULL_CARD_PROPS effective 0 / raw 0xFFFF (as `Get_EffectiveAttackFromKonamiId` / `Get_RawAttackFromKonamiId` do) |
| `"genres"` | `["DRAW", "SPSUMMON"]` | CARD_Genre.bin | MoreCards `WriteGameTableEntry` / `BorrowInto` (over genres.json) |
| `"related"` | `[{"card": 4007, "tag": 12, "name": "..."}]` | tagdata.bin | MoreCards `Related.cpp` (the card's whole list, over relatedcards.json) |
| `"links"` | `[{"target": 4007, "kind": "card"}]` | CARD_Link.bin | nobody: the game never reads CARD_Link (docs/CardLink.md) |
| `"password"` | `12345678` | CARD_Pass.bin | `Card_FindByPassword` export, used by `YuGiOh-PASS.h` `FindCardByPassword` (Card Shop Enter Password) |
| `"sortAs"` | `"Dark Magician"` | CARD_Sort / Sort2 | MoreCards `NameSortRankFor` (default: the name) |
| `"indexLetters"` | `"DAR"` | CARD_Kana1/2/3 | MoreCards `IndexInitial` (first letter; default: from the name) |
| `"text"` | `{"F": {"name": "...", "description": "...", "indexLetters": "...", "sortAs": "..."}}` | CARD_Name/Desc/Kana/Sort per language | MoreCards picks the game's current language each card setup; empty = English |
| `"imageUncensored"` | `"15300_uncensored.jpg"` | the uncensored art zib (2020.full.illust_j) | MoreCards `Hook_Get_IllustrationData`: served while `g_bIsJpVersion` is set (the game in Japanese), else `"image"` |

Genre keys / tag conditions / link targets: docs/CardGenre.md, docs/RelatedCards.md, docs/CardLink.md.

## WolfX

* `Tools/Shared/Editors/CustomCardStore.cs`: `ICustomCardStore` / `CustomCards.Store` = the New cards page (`CardsPanel.Store.cs`).
* Card genres, Related cards and Card links pages: a card cards.json has keeps its part in its entry. An edit goes straight into the
  entry (cards.json becomes unsaved, not the page). Their own JSON (genres.json, relatedcards.json, cardlinks.json) keeps only ids
  cards.json doesn't have. An older entry there for a custom card is still shown, and moves into cards.json when that page is saved.
* New cards page tabs: Properties (+ password, "?" ATK/DEF), Text (language picker, index letters, sort as), Art, Required cards,
  Genres, Related cards, Links (the same editor instances as their pages and the Card Manager's tabs).
* Nothing left uncovered (2026-10-11). The "other 4 bits" after the Pendulum Scale are bits 27-30 of CARD_Prop's second word: bits 27-28
  are the star type (1 Level, 2 Rank, 3 Link; the Rank / Link getters return 0 without it), which MoreCards sets from the kind;
  the exe drops bits 29-30 when it loads. The Card Manager no longer shows them and sets the star type from the kind too.
  The uncensored picture is `"imageUncensored"` (New cards > Art).

Untested in game (2026-10-04): built only.
