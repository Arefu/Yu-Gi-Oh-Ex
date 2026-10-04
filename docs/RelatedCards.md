# Related cards (tagdata.bin + taginfo_&lt;lang&gt;.bin)

In the deck editor, the **Related cards** panel lists the cards related to the selected card, each with a line saying why
("Related card affects: [ATK icon] <= 1000", "Related to: Nekroz"). The data for it is in two files:

* `bin/tagdata.bin`: which cards are related to each card, and the tag that explains why.
* `bin/taginfo_<L>.bin` (E F G I J S, no R): what each tag means.

The parser is `File Type Libraries/RelatedCards` (`TagDataTable`, `TagInfoTable`, `TagInfo`, `RelatedCardsJson`). The editor is
`Tools/Shared/Editors/RelatedCardsEditor.cs`, on the **Related cards** tab in both WolfX and WolfEx.

## tagdata.bin

| Offset | Type | Meaning |
|---|---|---|
| 0 | 10166 x {u32 Start, u32 Count} | one entry per **internal** card id (CARD_INTID.bin maps a Konami id to it) |
| 81328 | u32 pool | `RelatedCard` units: u16 related Konami id, u16 tag id |

Each internal id's units are written in turn, sorted by Konami id, followed by **one 0 unit**. `Start` is the index of the first
unit. An empty list writes only its 0 unit, and its `Start` points at it. The game's file has 1,076,179 links for 10,027 cards.

What a list means: card X lists the cards whose effect tag matches X. For example, Nekroz of Brionac (a Warrior) lists Legendary
Sword, because the Sword's tag is `{AD}TYPE:SOLDIER`.

## taginfo_&lt;L&gt;.bin

u32 count (1802), then count x 0x34-byte records, then a UTF-16 pool. The pool holds each record's Key and Text, each ending in a
zero, in record order.

| Offset | Type | Meaning |
|---|---|---|
| +0x00 | u8 Group | 0 Name ("Related to: ..."), 1 Affects (`{AD}`), 2 Matches (`{FIND}`) |
| +0x01 | u8 Flag1 | set on some Xyz/Pendulum tags |
| +0x02 | u8 HasNumber | 1 when a condition compares a number (ATK/DEF/LEVEL/RANK/LINK) |
| +0x03 | u8 | 0 |
| +0x04 | 8 x {u8 Type, u8 Op, u16 Value} | conditions; an unused slot is `FF FF 0000` |
| +0x24 | u64 | Key: a file offset, turned into a pointer on load |
| +0x2C | u64 | Text: a file offset, turned into a pointer on load |

The first four bytes, read as one u32, are the panel's sort key. After that the panel sorts by tag id, then by name.

**Condition types:**

| Type | Condition | Values |
|---|---|---|
| 0 | ATK | a number |
| 1 | ATTR | 1 LIGHT, 2 DARK, 3 WATER, 4 FIRE, 5 EARTH, 6 WIND |
| 2 | DEF | a number |
| 3 | KIND | 0 NORMAL, 1 EFFECT, 13 MAGIC, 14 TRAP, 46-55 NORMAL\*/SYNC\*/XYZ\*/TUNER\*/FUSION\*/RITUAL\*/PEND\*/FLIP\*/LINK\*/UNION\*, and others |
| 4 | LEVEL | a number |
| 5 | ICON | the Spell/Trap icon |
| 6 | TYPE | the monster type: 1 DRAGON ... 15 SOLDIER ... 23 CYVERSE |
| 7 | DECK | 0 MAIN, 1 EXTRA |
| 8 | RANK | a number |
| 9 | SPECIALSUMMON | YES |
| 10 | TRIBUTE | YES (shown as LEVEL > 4) |
| 11 | TRIBUTE2 | YES (shown as LEVEL >= 7) |
| 12 | LINK | a number |

**Ops:** 0 `<=`, 1 `<`, 2 `=`, 3 `>=`, 4 `>`, 5 `!=`. The full value tables are in `TagInfo.ValueNames`.

Every language has the same records, conditions and keys; only Text differs. **The game never reads Key.** It shows Text only for
Name tags. For Affects and Matches tags it builds the line from the conditions: UI string 649 "Related card affects:" or 648
"Related card matches:", followed by icons and values (`TagInfo_BuildText`). The game's keys have two quirks: 65535 is written as
`-1` (the game shows it as "?"), and two RANK keys leave out the `=`.

## In the exe (IDB)

* `Setup_CardPropTable` loads both files. They are **required**: if either fails to load, the card data doesn't load.
  After loading, it turns Key and Text into pointers (0x14076CB48).
* `Get_RelatedCardCount` (0x14076D640) and `Get_RelatedCardList` (0x14076D690) take a Konami id and go through the internal id.
  An id outside 3900-14968 uses entry 0, so **custom cards get an empty list**.
* `Get_TagInfoRecord` (0x14076DF60). `TagInfo_BuildText` (0x14076B310). `RelatedCards_SortCompare` (0x14074DFA0).
* UI: `DeckEdit_ApplyState` (0x14083A6C0) states 4 (a card in the deck list) and 5 (a card in the trunk) call
  `RelatedCardsPanel_SetCard` (0x1408AF3B0). `RelatedCardsPanel_GetTagId` and `RelatedCardsPanel_ShowTagText` show why a
  card is related.
* IDB types: `TagInfoRecord`, `TagCondition`, `TagInfoFile`, `TagDataEntry`, `RelatedCardUnit`, and the enums `TagGroup`,
  `TagCondType`, `TagCompareOp`.

## WolfX / WolfEx

* **WolfX:** open the bin folder. It needs tagdata.bin, taginfo_*.bin and CARD_IntID.bin. Edit, then save back into the files;
  a `.bak` of each original is kept. tagdata.bin is indexed by internal id, so a card that isn't in CARD_IntID.bin, such as a
  custom card, can't be saved there.
* **WolfEx:** opens the game's data with `Yu-Gi-Oh-Ex\relatedcards.json` on top, and saves only the differences:

```json
{ "tags":  [ { "id": 1802, "group": "name", "key": "MyArchetype", "text": { "E": "Related to: My Archetype" } } ],
  "cards": [ { "card": 15300, "add": [ { "card": 4007, "tag": 1802 } ] },
             { "card": 4041, "remove": [ { "card": 4007, "tag": 12 } ] } ] }
```

"tags" holds new tags (ids after 1801) and changed game tags, whole. "cards" is keyed by Konami id, so custom cards can have
related cards.

## In the game: Yu-Gi-Oh-Cards (`Related.cpp`)

Yu-Gi-Oh-Cards reads relatedcards.json after every card setup: at startup, and again when the language changes. It hooks three
functions with MS Detours:
* `Get_RelatedCardCount` and `Get_RelatedCardList` answer by **Konami id**, so custom cards (15300+) get lists too. Each list
  in the JSON starts from the game's list for that card (none for a custom card), applies the removes and then the adds, and
  is sorted by related card the way the game sorts it. Cards the JSON doesn't list go to the game's own data.
* `Get_TagInfoRecord` serves new tags (ids past 1801) and changed tags. A changed tag starts from the game's record, so it keeps
  any fields the JSON leaves out. Text uses the game's current language, falling back to English and then to any language.
  The panel's sort order and "why" line go through this hook too, so they work for new tags.

The log line `relatedcards.json: N new and M changed tag(s), related cards for K card(s)` shows what was loaded.

Tested: tagdata.bin and all six taginfo files round-trip byte for byte, in both the YGO_2020 and Remaining Files copies.
Rebuilding tagdata.bin from the per-Konami-id view gives the identical file. A JSON diff applied again gives the same tables.
