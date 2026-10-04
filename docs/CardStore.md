# Card Store prices (Yu-Gi-Oh-BetterCardShop)

Written 2026-10-02. Code: `Yu-Gi-Oh-GUI Plugins/Yu-Gi-Oh-BetterCardShop/Prices.cpp` (prices), `StorePages.cpp` (the pages). **Status:** built,
not yet tried in game.

The main menu's Card Shop button opens the Card Store (RIX pages): Booster Packs (the game's shop), Card Shop (every card, buy one copy at
a time) and Enter Password (eight digits, three copies of the card).

## What a card costs

The order is:

1. **`Yu-Gi-Oh-Ex\prices.json`** (optional): a fixed price for single cards, by Konami id.
2. **The settings** (Config.ini `[Yu-Gi-Oh-BetterCardShop]`, or WolfX's Config Editor).

```json
{ "cards": [
    { "id": 4007, "price": 5000, "password": 2500 },
    { "id": 4041, "price": 150 }
] }
```

`price` is the Card Shop price and `password` is the Enter Password price. Either one can be left out. These prices are used exactly as
written (0 = free, and the 100 DP floor does not apply). The file is read again each time a Card Store page opens.

| Key | Default | Meaning |
|---|---|---|
| `BetterShop-PasswordCost` | 1000 | Enter Password, any card (three copies). |
| `BetterShop-MonsterPrice` | attack | One of `attack`, `classic`, `custom` or `flat`; see below. |
| `BetterShop-MonsterFormula` | `ATK * 1.8 / 5` | Used by `custom`. |
| `BetterShop-SpellTrapPrice` | rarity | One of `rarity`, `custom` or `flat`. |
| `BetterShop-SpellTrapFormula` | `500 + 500 * RARE` | Used by `custom`. |
| `BetterShop-Cost` | 500 | The flat price. |

**Monster rules:**

* **attack:** ATK × 1.8 ÷ 5. Blue-Eyes costs 1,080 DP, Dark Magician 900 DP, a typical common (ATK 1400) 500 DP and a typical rare (ATK 2200)
  790 DP.
* **classic:** the Forbidden Memories curve (below), 11.56 × e^(0.001179 × (ATK + DEF)), capped at 10,000. A typical common costs about
  200 DP, a typical rare about 1,060 DP and Blue-Eyes about 7,540 DP.
* **custom:** the Monster formula setting.
* **flat:** `BetterShop-Cost`.
* A Link monster has no DEF, so its DEF counts as 0.8 × ATK. That is the average of the game's other monsters (0.797).
* A monster whose ATK is `?` (68 cards, such as the Egyptian Gods) gets its value from its effect, so it is priced like a spell or trap by
  rarity.

**Spell/Trap rules:**

* **rarity:** the median price of the loaded monsters of the same rarity under the current monster rule. With `attack` that is 790 DP for a
  rare and 500 DP for a common.
* **custom:** the Spell/Trap formula setting.
* **flat:** `BetterShop-Cost`.

**Formulas:**

* Operators: `+ - * / ^` and brackets.
* Names (case doesn't matter):
  * `ATK`, `DEF`;
  * `LEVEL` (level, rank or link rating);
  * `RARE` (1 for a card in a pack's rare slot, else 0);
  * `LINK` (1 or 0).
* Functions: `min max exp log sqrt round floor ceil`.
* A formula that can't be read is logged once, and the built-in rule is used instead.

Formula prices are rounded to 10 DP and kept between 100 and 999,999.

## Rarity

Every card the game ships is in exactly one rarity. Its packs (`g_PackRecords`, the contents are a u16 common count, a u16 rare count,
then the common ids, then the rare ids) list **2,094 rare** and **7,933 common** cards, with none in both and none missing.
`Packs::RareCards()` reads the packs as loaded, so packs.json additions count. A custom card that is in no pack is common.

| | rare share |
|---|---|
| ATK 0-1999 | 13-15% |
| ATK 2000-2499 | 32% |
| ATK 2500-2999 | 54% |
| ATK 3000+ | 68% |
| Spells / traps | 527 of 3,371 (16%) |
| Extra Deck (Fusion effect, Xyz, Synchro, Link) | 52-72% |

The median ATK is 2200 for rare monsters and 1400 for common ones (median ATK+DEF 3800 and 2400). So rarity is Konami's own rating of a
card, and it is the only per-card value judgement in the data that covers spells and traps.

## Research: how older games priced cards

| Game | Passwords | Card prices |
|---|---|---|
| Forbidden Memories (PS1) | Pay the card's star chip price. 98 cards cost 999,999 (not really for sale). | Data: per-card cost in WA_MRG.MRG, next to the password. |
| Sacred Cards / Reshef (GBA) | 1,000 Domino to enter a password, which only puts the card in the shop. You then pay its price as well. | Per-card shop prices. Sacred Cards' are 10x Reshef's, and selling gives 5% back. |
| Nightmare Troubadour (DS) | 1,000 per password, and only for cards you already have. | |
| World Championship 2006 (GBA) | Each password has a DP price per card, by rarity (alternate Dark Magician 4,680 DP). | |
| World Championship 2008 (DS) | Per card, about 1,000-2,000 DP (players also report 250-4,000 DP by rarity and ban status). | |
| GX Tag Force 1-3 (PSP) | Free, but the duel DP you earn is cut while you have rented cards. | |
| Duelists of the Roses (PS2) | | Deck cost = (ATK + DEF) / 100, +5 with an effect, +10 for Immortals. |
| Dark Duel Stories (GBC) | | Deck cost = (ATK + DEF) / 100 − 10 per tribute, +50 for monsters with abilities; field spells 20, rituals 0. |

**Forbidden Memories, measured.** The source is MarceloSilvarolla's YFM.db (YFM-Database-and-Fusion-Guide, release 1),
using the 533 monsters that are for sale:

* A straight line on ATK + DEF fits badly (R² 0.59; ATK alone 0.52, DEF alone 0.44).
* ln(cost) = 1.754 + 0.001179 × (ATK + DEF) fits with **R² 0.968**. The price doubles for every ~590 points of ATK + DEF.
* Median price by ATK + DEF:

  | ATK + DEF | 500 | 1000 | 1500 | 2000 | 2500 | 3000 | 3500 | 4000 | 4500 |
  |---|---|---|---|---|---|---|---|---|---|
  | Star chips | 15 | 30 | 40 | 70 | 140 | 260 | 500 | 800 | 1,800 |

* Magic, trap and ritual cards were priced by hand:
  * rituals 10, 50 or 100;
  * fields 55;
  * equips 800 (Megamorph 50,000);
  * Dark Hole 20,000;
  * Ookazi 35,000;
  * every trap and most strong magic 999,999.

  There was no formula for them. That is why this plugin uses rarity for spells and traps rather than inventing a stat.

**The game's own economy.** A booster pack costs 400 DP (the first one 200). Each pack draws from about 248 commons and 66 rares. If a pack
gives one rare, a specific rare takes about 66 packs on average (~26,000 DP; the slot count per pack is not checked). So any of the rules
above makes a single copy much cheaper than chasing it in packs.

**Sources:**

* [Yugipedia: Star Chip (Forbidden Memories)](https://yugipedia.com/wiki/Star_Chip_(Forbidden_Memories))
* [YFM-Database-and-Fusion-Guide](https://github.com/MarceloSilvarolla/YFM-Database-and-Fusion-Guide)
* [FM Recompiled passwords mod](https://github.com/Unchiga/Yu-Gi-Oh-Forbidden-Memories-Recompiled/pull/136)
* [Yugipedia: Deck Cost](https://yugipedia.com/wiki/Deck_Cost)
* [Yugipedia: Password](https://yugipedia.com/wiki/Password)
* [Reshef of Destruction (TV Tropes)](https://tvtropes.org/pmwiki/pmwiki.php/VideoGame/YuGiOhReshefOfDestruction)
* [Nightmare Troubadour password machine](https://www.supercheats.com/nintendods/yu-gi-oh-nightmare-troubadour/18287/password-machine/)
* [Dark Magician, WC2006 alternate password](https://yugipedia.com/wiki/Dark_Magician_(World_Championship_2006_alternate_password))
* The pack and rarity numbers came from the game's own files (`main\packdefdata_E.bin`, `packs.zib`, `bin\CARD_Prop.bin`), read with the
  WolfX libraries.

## Buying in the Card Shop

* Picking a card (Enter or a click) opens a Yes / No box: "Buy X for N DP? You have M DP and K copies."
* Yes takes the DP, adds one copy (at most 3) and marks the card as seen. The list's count for that card is changed in place, so the
  list keeps its scroll position.
* With 3 copies, or too little DP, a message says so instead.
