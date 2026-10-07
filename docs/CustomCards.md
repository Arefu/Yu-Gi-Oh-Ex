# Custom cards: everything in cards.json

`<game>\Yu-Gi-Oh-Ex\cards.json` holds every custom card (ids 15300-19999) and **everything about it**: stats, text in every
language, art, archetypes, same name, summoning requirements, effect, and the parts the game keeps in other tables for its own
cards (genres, related cards, links, password, name order, index letters). WolfX's **New cards** page writes it; Yu-Gi-Oh-MoreCards
(`Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Cards`) reads it.

Game cards are unchanged: their edits go into the game's files (WolfX patch archive) or the page's own Yu-Gi-Oh-Ex JSON.

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

Genre keys / tag conditions / link targets: docs/CardGenre.md, docs/RelatedCards.md, docs/CardLink.md.

## WolfX

* `Tools/Shared/Editors/CustomCardStore.cs`: `ICustomCardStore` / `CustomCards.Store` = the New cards page (`CardsPanel.Store.cs`).
* Card genres, Related cards and Card links pages: a card cards.json has keeps its part in its entry. An edit goes straight into the
  entry (cards.json becomes unsaved, not the page). Their own JSON (genres.json, relatedcards.json, cardlinks.json) keeps only ids
  cards.json doesn't have. An older entry there for a custom card is still shown, and moves into cards.json when that page is saved.
* New cards page tabs: Properties (+ password, "?" ATK/DEF), Text (language picker, index letters, sort as), Art, Required cards,
  Genres, Related cards, Links (the same editor instances as their pages and the Card Manager's tabs).
* Not covered: the "other 4 bits" after the Pendulum Scale (meaning unknown), an uncensored second picture (custom art is one file).

Untested in game (2026-10-04): built only.
