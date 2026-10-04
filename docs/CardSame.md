# CARD_Same.bin (cards treated as another card's name)

`bin/CARD_Same.bin` lists the cards whose name counts as another card's name, such as Harpie Lady 1 -> Harpie Lady and
Cyber Dragon Zwei -> Cyber Dragon. The parser is `File Type Libraries/CARD_Same.bin` (`Types.CardSameTable`). You edit it on the
Card Manager's **Properties** tab, in the **Same name as** row. Custom cards get the same thing from `"sameName"` in cards.json, set
on the New cards page and applied by Yu-Gi-Oh-MoreCards.

## Layout

No header. The file is a list of 6-byte rows, **sorted by card**:

| Offset | Type | Meaning |
|---|---|---|
| +0 | u16 | the card (Konami id) |
| +2 | u16 | the target: the card whose name it counts as |
| +4 | u16 | mode: `0` always, `0x100` while an effect says so |

The game's file has 72 rows: 14 with mode 0 and 58 with mode 0x100. Round trip through the library is byte-exact.

| Mode | Meaning | The game's examples |
|---|---|---|
| 0, **always** | "This card's name is always treated as ..." | Harpie Lady 1/2/3 and Cyber Harpie Lady -> Harpie Lady, A Legendary Ocean / Lemuria / Pacifis -> Umi, Fusion Substitute -> Polymerization, Neo-Spacian Marine Dolphin -> Aqua Dolphin, the Sheep / Ojama Token variants |
| 0x100, **while an effect says so** | "This card's name becomes ... while on the field / in the GY" | Cyber Dragon Zwei/Drei/Core/... -> Cyber Dragon, Harpie Channeler -> Harpie Lady, Blue-Eyes Alternative -> Blue-Eyes White Dragon, The Lady in Wight -> Skull Servant |

## What the exe does with it

`YGO::CARDS::Setup_CardPropTable` (0x14076BFC6) loads the file before CARD_Prop.bin. Then, for each card id below 14969, it looks the
card up with `List_BinarySearchRowByKonamiId` (0x14076C340). That is a **binary search**, so the rows must stay sorted by card;
`CardSameTable.Set` keeps them sorted. Each card's props (`CARDS::CardProps`, 0x30 bytes) get two ids in the dword at +0x2C:

| Word | Name | No row | Mode 0 (always) | Mode 0x100 |
|---|---|---|---|---|
| +0x2C | identity id | the card | **target** | the card |
| +0x2E | same-name id | the card | target | target |

* **`Card_IsSameName` (0x14076D6E0)** compares two cards' identity ids. The duel engine calls it more than 300 times through
  `j_Card_IsSameName` (0x1407EBA00): fusion materials (`Fusion_CardMatchesMaterialCode`), negation (`Cond_NegateTargetMatches`),
  Xyz checks, effect filters. A mode-0 card therefore *is* its target in every duel name test.
* `Setup_FullCardProps` copies the two ids to `FULL_CARD_PROPS` +0x6C (`Get_CardIdentityIdFromKonamiId`, 0x14076D5F0) and +0x70
  (`Get_CardSameFromKonamiId`, 0x14076D610).
* **Open question:** no reader of the same-name id (+0x2E / FULL_CARD_PROPS +0x70) has been found yet. The "while on the field" part
  of mode 0x100 probably comes from the card's own effect rows. So changing a mode-0x100 row may do nothing until that reader is
  found. Mode 0 works through `Card_IsSameName`.
* **The chain is not followed.** A target that has its own row is still compared by its own id.
* Ids 14969 and up (custom cards) are never looked up. A row for a custom card is ignored, which is why custom cards use cards.json.

IDB: named `Card_IsSameName`, `j_Card_IsSameName` and `Get_CardIdentityIdFromKonamiId`, with comments at the lookup and the accessors.

## WolfX

* **Card Manager > Properties > Same name as:** shows the target (or "(its own)"). **Pick card...** sets it, the mode list switches
  between *always* and *while an effect says so*, and **Own name** removes the row. Saved into `bin\CARD_Same.bin` (the patch
  archive), sorted.
* **New cards > Properties > Same name as:** the same controls for a custom card. Saved in its cards.json entry:

```json
{ "id": 15300, "name": "Harpie Lady 4", "sameName": { "card": 4068, "always": true } }
```

`"sameName": 4068` (just a number) means always. Yu-Gi-Oh-MoreCards (`Card.cpp`, ParseCard and WriteGameTableEntry) writes the two
ids into the custom card's props and its FULL_CARD_PROPS entry, the way the exe does for the game's cards. A custom card that borrows
a vanilla id in a duel keeps them, because the props hook serves the custom card's props for the borrowed id.

Not tested in game yet. Test it with a custom "always Harpie Lady" card used as a Harpie Lady fusion material, or with Harpie Lady
Sisters' "3 Harpie Lady" type checks.
