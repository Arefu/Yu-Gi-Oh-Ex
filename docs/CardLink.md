# CARD_Link.bin (cards a card mentions)

`bin/CARD_Link.bin` lists what each card's text mentions: other cards, archetypes and counters.
The parser is `File Type Libraries/CardLink` (`Types.CardLinkTable`, `Types.CardLinkJson`). The editor is
`Tools/Shared/Editors/CardLinkEditor.cs`, on the **Card links** tab in both WolfX and WolfEx.

## Layout

No header. The file is a list of 4-byte links, sorted by card:

| Offset | Type | Meaning |
|---|---|---|
| +0 | u16 | the card (Konami id) |
| +2 | u16 | the target |

A card's own targets stay in the order they were written. The game's file has 5722 links from 4494 cards.
It also has one duplicate link: 12168 -> 257.

The target number tells you what kind of thing it is:

| Target | Kind | Examples |
|---|---|---|
| 3900 and up | Konami card id | Exodia the Forbidden One (4027) -> its four limbs (4023-4026) |
| 1-999 | CARD_Named archetype code (the game's are 1-418) | Amazoness Archers -> 6 (Amazoness); Gravekeeper's cards -> 3 |
| 1000 and up | counter kind | Endymion cards -> 1000 (Spell Counter); Alien cards -> 1048 (A-Counter) |

The counter names come from the text of the cards that link to each code. They are in `CardLinkTable.CounterNames`:
1000 Spell, 1002 Clock, 1003 Hyper-Venom, 1006 Bushido, 1020 Black Feather, 1039 Kaiju, 1048 A-Counter, 1049 Ice, 1050 Venom,
1051 Fog, 1053 Wedge, 1056 Cubic, 1058 Predator, and more. These codes are never used: 1001, 1015, 1021, 1044-1047 and 1052.

## What the exe does with it: nothing yet

`YGO::CARDS::Setup_CardPropTable` (0x14076BFC6) loads the file at 0x14076C9E1. It goes into `g_CardDataFiles.CardLink`
(0x14275ABA0 = size, 0x14275ABA8 = data). `CardDataFiles_Unload` frees it when the language changes.

**No code reads it.** All 15 users of `g_pCardDataFiles` (0x140C8D3A0) were checked. None of them touch +0xA0 or +0xA8, and
`g_CardDataFiles` itself has no other references. So editing the file changes nothing in the game today. It's useful data for a
plugin to use later, for example a "cards this card mentions" view, or related cards for custom cards. The game's own
related-cards feature uses tagdata.bin and taginfo, not this file.

The IDB now has the `CardDataFiles` struct: 19 `GameDataFile { Size, Data }` slots from +0x10. It also has names for
`LoadDataFile`, `FreeDataFile`, `CardDataFiles_Unload`, `CardDataFiles_ReloadForLanguage` and the related functions.

| Slot | File | Slot | File |
|---|---|---|---|
| +0x10 | CARD_Name_# | +0xB0 | CARD_Same |
| +0x20 | CARD_Indx_# | +0xC0 | CARD_Named |
| +0x30 | CARD_Desc_# | +0xD0 | CARD_INTID |
| +0x40 | CARD_Sort_# | +0xE0 | (never loaded) |
| +0x50 | CARD_Sort2_# | +0xF0 | CARD_Kana1_# |
| +0x60 | DLG_Indx_# | +0x100 | WORD_Indx_# |
| +0x70 | DLG_Text_# | +0x110 | WORD_Text_# |
| +0x80 | CARD_Prop | +0x120 | tagdata |
| +0x90 | CARD_Genre | +0x130 | taginfo_# |
| +0xA0 | **CARD_Link** | | |

## The two copies differ

The copy in `Remaining Files\bin` differs from the game's own copy in `YGO_2020.dat` in one link. In the first link, Swamp
Battleguard (4018) points to Pot of Greed (4844) in Remaining Files, and to Lava Battleguard (4560) in the game's copy. The game's
copy is the real one: the two Battleguards mention each other. The Remaining Files copy looks like it was edited by hand.

## WolfX / WolfEx

* **WolfX:** open a CARD_Link.bin, edit it, then save. A `.bak` of the original is kept. It only warns about problems your changes
  add: a duplicate link, a card linking to itself, a target of 0, or links out of order.
* **WolfEx:** opens the game's table with `Yu-Gi-Oh-Ex\cardlinks.json` on top. **New card...** gives links to any card,
  custom cards (15300+) included. **Add card... / Archetypes... / Add counter...** add targets, and **Remove** (or Delete) removes
  them. Save writes only the differences to cardlinks.json:

```json
{ "cards": [ { "card": 15300, "name": "My Card",
               "add":    [ { "target": 4007, "kind": "card", "name": "Blue-Eyes White Dragon" },
                           { "target": 6, "kind": "archetype", "name": "Amazoness" } ],
               "remove": [ { "target": 1000 } ] } ] }
```

Only `card` and `target` are read. Instead of `target`, a person can write `{ "card": id }`, `{ "archetype": code }` or
`{ "counter": code }`. No plugin reads cardlinks.json yet, because the game doesn't read the file either.

Tested: both copies round-trip byte for byte. The diff of an unchanged table is empty. Adds and removes written to JSON and
applied again give the same table.
