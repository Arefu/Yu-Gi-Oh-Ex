# Shiny cards (card rarities)

Yu-Gi-Oh-Core gives cards **real-card rarities**. Cards pulled from booster packs can come as Super, Ultra, Secret, Starlight,
Quarter Century Secret and so on. The save keeps how many of each you own, and every card face is drawn with its rarity as animated
foil over the game's own card. This started as Toon Jinzo's Yu-Gi-Oh-Foil plugin (three finishes) and grew into the full rarity list.
The game records card faces on a deferred Direct3D context, so the face clear is hooked on both contexts (docs/CardRendering.md).
Code: `Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Core/Shiny.cpp`, `Rarity.cpp`, `ShinyShader.h`.

## Settings (`[Yu-Gi-Oh-Core]`, declared in Yu-Gi-Oh-Core.json)

| Key | Default | |
|---|---|---|
| `Shiny` | 0 | 1 = packs roll rarities and cards are drawn with them. Off keeps the saved rarities. |
| `ShinyOddsScale` | 100 | Percent of every rarity's odds (50 = twice as many shinies). |
| `ShinyStrength` | 1 | How strongly the foil is drawn, 0 to 2. |
| `ShinySpeed` | 1 | How fast it moves. |
| `ShinyMouseTilt` | 1 | The light follows the mouse, like tilting the card. |
| `ShinyPreview` | (empty) | Draw every card as one rarity (`starlight`, `gold`, `Ultra Rare`...), or `cycle` = all of them, 3 s each. For trying the looks out; the save isn't touched. |

All of them need a restart.

## The rarities

Every rarity printed on real cards is in the catalogue. The list comes from Yugipedia's Category:Rarities (2026-11-01), minus
7 Trials to Glory's pack-screen colours, BAM Legend, and the KaibaCorp Super/Secret Rares that were never printed. Each rarity is a
stack of layers the shader draws:

| Layer | Choices |
|---|---|
| name ink | silver, gold, rainbow, speckled (Prismatic), champagne (25th), red (20th), bronze, platinum, colour, sparkleSilver |
| art foil | holo, parallel (the Secret dot screen), prismatic (bars), ghost (pale), emboss (Ultimate), gold, platinum, secretGold, secretPlatinum, reliefGold, rainbow (Collector's) |
| frame foil + regions | the same foils, on: attribute, stars, artBorder, edge (`border`), textBorder, wholeFrame, nameBox |
| coating | the whole card, text box included: parallel, duelTerminal, millennium, kaibaCorp, starfoil, mosaic, shatterfoil, crisscross, sparkle |
| desaturate | 0..1: pale art (Ghost, Holographic, Mirror) |
| watermark | 20th, 25th, 10000: a faint medallion in the text box (the logos themselves are not drawn) |

Built in, by id (the save keeps the id, so ids never change). "Packs" shows the default odds per card in the slot (R = the rare slot,
any = any slot). A dash means packs don't roll it: it's for prizes, mods and `ShinyPreview`.

| Id | Rarity | Packs |
|---|---|---|
| 1 | Rare (what a rare-slot card is when nothing rarer is rolled) | R |
| 2, 3, 4, 5 | Super, Ultra, Secret, Prismatic Secret | R 1/5, 1/12, 1/31, 1/60 |
| 6, 7, 8 | Ultimate, Ghost, Holographic | R 1/96, 1/288, - |
| 9, 10, 11, 12 | Starlight, Quarter Century Secret, 20th Secret, 10000 Secret | R 1/576, 1/576, -, - |
| 13, 14, 15 | Extra Secret, Platinum Secret, Platinum | -, R 1/500, R 1/400 |
| 16, 17, 18, 19 | Gold, Gold Secret, Ghost/Gold, Premium Gold | R 1/150, 1/300, -, 1/400 |
| 20, 21, 22 | Collector's, Prismatic Collector's, Grand Master | R 1/200, -, - |
| 23, 24, 25 | Ultra (Pharaoh's), Ultra Secret, Secret Ultra | - |
| 26-32 | Normal / Rare / Super / Ultra / Secret / Extra Secret / Holographic Parallel | Normal any 1/200, Super R 1/150, Ultra R 1/250, rest - |
| 33-38 | Duel Terminal Normal, Normal Rare, Rare, Super, Ultra, Secret Parallel | - |
| 39-43 | Millennium, Millennium Super / Ultra / Secret / Gold | - |
| 44, 45, 46 | KaibaCorp Common, Rare, Ultra | - |
| 47, 48, 49 | Starfoil, Mosaic, Shatterfoil | any 1/120 each |
| 50, 51, 52 | Normal Rare (looks Common), Holofoil, Colorful (blue name) | - |
| 53-56 | Rush, Over Rush, Full Over Rush, Gold Rush | - |
| 57 | Mirror (unreleased, seen on a 2025 test sheet) | - |

Rolling: the rare slot tries every rare-slot or any-slot rarity, rarest first; the first hit wins, otherwise the card is a Rare. The
7 common slots only try any-slot rarities (real packs don't put Supers in common slots). A card shows its highest-tier rarity.

## rarities.json

`Yu-Gi-Oh-Ex\rarities.json`, with every mod's copy merged, changes built-in rarities by `key` and adds new ones (ids 128-255):

```json
{ "rarities": [
  { "key": "ultra", "odds": 20 },
  { "key": "starfoil", "odds": 0 },
  { "id": 128, "key": "neon", "name": "Neon Rare", "tier": 33, "odds": 400, "slot": "rare",
    "nameInk": "colour", "nameColour": "#40FFE0", "artFoil": "holo", "frameFoil": "rainbow",
    "regions": ["attribute", "stars", "border"], "coating": "sparkle" }
] }
```

Fields: `name`, `tier`, `odds` (1 in N; 0 = never), `slot` (rare / any / none), `nameInk`, `nameColour` (#RRGGBB), `artFoil`,
`frameFoil`, `regions`, `coating`, `coatStrength` (0..2), `desaturate` (0..1), `watermark`, `note`.

## Save

Save extension tag `shiny`: a version byte (1), then 5 bytes per entry: `u16 card, u8 rarity, u16 count`. It is read when the card
table takes over a save (`CardTable::AddTakeListener`) and written by a save write filter. The data is kept whether Shiny is on or off.
A save made by a newer Core with a later version is left as it is.

Exports (Yu-Gi-Oh-Core.h), for prizes, shops and trade pages: `Core_GetCardRarity(card)`, `Core_GetCardRarityCount(card, rarity)`,
`Core_AddCardRarity(card, rarity, count)`.

## How it draws

1. `CardFace_Build` (0x14074E7D0) and `AddQuad` (0x1408795A0) record where each face's art, attribute and stars are drawn.
   * The art is the biggest quad that isn't the frame.
   * The stars are the last run of evenly spaced, same-size squares, as many as the card has.
   * The attribute is the biggest remaining square.
   * Core's detours are the innermost, so Anime Cards' moved stars and attribute are what gets recorded.
   * The name and text box are fixed rectangles on the game's own frame. With Anime Cards they are left out.
2. `CardFace_RenderToTarget` (0x140754420) only records drawing, so the card id is queued.
3. The recorded clear of a 400 x 580 target to grey (`ClearRenderTargetView`) ties that texture to the card.
4. Any draw with such a texture in pixel shader slot 0 is drawn a second time with `ShinyShader.h` blended over it, using the game's
   own vertex shader (only shaders that output SV_POSITION, COLOR0, TEXCOORD0).
5. The device comes from `D3D11CreateDeviceAndSwapChain` (thunk 0x14090D2B0): the first call that has a swap chain.

The rarity is per card id. Every copy of a card you own as an Ultra shows as an Ultra, the opponent's copies included.

## Not done yet

* **Alternative / full art** (Over Rush, Full Over Rush, Grand Master, extended art). These need a different picture per rarity. The
  plan is a per-card, per-rarity art override using the same streamed art path as Card Manager's Art tab. The pictures could come from
  the player's own Master Duel or Duel Links install, extracted locally: Konami's assets are not downloaded or shipped with Yu-Gi-Oh-Ex.
* The 20th / 25th / 10000 logos, which need their picture.
* Choosing which owned rarity a card shows, per card or per deck slot. Today a card always shows its highest tier.
* Showing the rarity on the pack result grid (a label or sound for big pulls). Today big pulls are logged.
