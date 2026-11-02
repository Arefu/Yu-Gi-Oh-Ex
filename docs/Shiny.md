# Shiny cards (card rarities)

Yu-Gi-Oh-MoreCards gives cards **real-card rarities** (it was in Core until 2026-11-02). Cards pulled from booster packs can come as Super, Ultra, Secret, Starlight,
Quarter Century Secret and so on. The save keeps how many of each you own, and every card face is drawn with its rarity as animated
foil over the game's own card. This started as Toon Jinzo's Yu-Gi-Oh-Foil plugin (three finishes) and grew into the full rarity list.
The game records card faces on a deferred Direct3D context, so the face clear is hooked on both contexts (docs/CardRendering.md).
Code: `Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Cards/Shiny.cpp`, `Rarity.cpp`, `ShinyShader.h`.

With More Cards switched off there are no rolls and no foil. Core still keeps the save's `shiny` data as it is, so nothing is lost.

## Settings (`[Yu-Gi-Oh-Core]`, declared in Yu-Gi-Oh-MoreCards.json; the section stayed when the code moved, so existing settings keep working)

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
`frameFoil`, `regions`, `coating`, `coatStrength` (0..2), `desaturate` (0..1), `watermark`, `art`, `note`.

## Card pictures (full art, broken frames, alternative art)

A rarity's `art` says it has its own picture of the card. Over Rush is `broken`, Full Over Rush is `full`, and Grand Master is
`extended`. The picture is `cardart\<card id>.<style>.png` (or `.jpg`) in any content folder, so a mod's `Yu-Gi-Oh-Ex\cardart\` works.

| `art` | The picture | How it is drawn |
|---|---|---|
| `alt` | another artwork | in place of the art, in the normal frame |
| `broken` | the whole card, frame painted in, the art breaking out | covers the card; the game's frame is not drawn; name, text, icons on top |
| `full` | the whole card, no frame | covers the card; the game's frame goes over it see-through (`ShinyFullArtFrame`, default 0.35) |
| `extended` | as `full` (a `full` picture is used if there is no `extended` one) | as `full` |
| `printed` | a scan of the printed card, text and all | covers the card; the game draws nothing over it (`CardFace+0x748` BatchCount set to 1) |

* **Full art needs no picture.** A `full` / `extended` rarity on a card with no picture of its own uses the card's own art, cropped
  across to the card's shape and stretched over the whole card, with the see-through frame on top. Every card can have it except
  Pendulums (their art isn't square).
* Any size works: the game decodes PNG or JPEG of any size and stretches it over the art's rectangle. A whole-card picture should
  have the card's layout, with the art window at 49-351 x 107-407 of 400 x 580.
* A whole-card rarity uses any whole-card picture of the card, real ones first: `broken`, then `printed`, then `extended` / `full`. The
  picture's own style decides how it is drawn.
* Packs only roll a rarity with `art` for cards it can be drawn on. Over Rush only lands on cards with a broken-frame or printed picture.
  Full Over Rush lands on any card that isn't a Pendulum.
* **Alternate artworks** are `cardart\<card id>.alt1` .. `alt9`. A card pulled from a pack comes in one of its artworks, each as likely,
  and the save keeps which (a print = rarity + art). The card shows its highest-tier print, then the highest art.
* `ShinyPreview` set to one rarity (not `cycle`) also shows the pictures, and `alt1` .. `alt9` shows every card in that artwork. Faces
  are cached, so a card pulled with a new picture or art shows it once its face is rebuilt (or after a restart).
* How: the face build is hooked. While it runs, `Get_IllustrationData` (0x14086D050) returns the picture; `Card.cpp`'s own hook on it, which
  serves custom cards' art, asks `Shiny::FaceArt` first. The first `AddQuad` (the art, batch 0) is stretched to the whole
  card. The second (the frame, batch 1) becomes zero-area (`broken`, `printed`) or has its alpha scaled (`full`).

**The Rarities mod** (`Mods\Rarities`, shipped in releases) holds every picture. It requires `Yu-Gi-Oh-MoreCards/Shiny`, so switching it
on switches More Cards and Shiny on. Its `rarities.json` makes Over Rush roll 1 in 24 and Full Over Rush 1 in 60 rare slots. Three scripts fill it
(they write into the repo's `Mods\Rarities` by default, `--mod` for another folder):

| Script | What |
|---|---|
| `Tools/Scripts/make_fullframe_mod.py` | Master Duel's Over Frame cards (`md/of_card_asset.bytes`, 20 cards) from a Master Duel export, text-free broken frames, matched to LOTD and Cards-mod ids by English name. Also writes `mod.json` and `rarities.json`. |
| `Tools/Scripts/fetch_extended_art.py` | Every card on Yugipedia's "Extended art" list (the TCG's extended art / the OCG's Over-Frame prints): Master Duel's text-free render (`-VG-EA`) as `broken`, else a scan of the printed card as `printed`. 72 cards on 2026-11-01 (8 text-free, 64 scans). |
| `Tools/Scripts/fetch_alt_arts.py` | Every alternate artwork on YGOPRODeck, matched by passcode (LOTD's `bin\CARD_Pass.bin`, the Cards mod's `password`): 115 cards, 151 artworks on 2026-11-01. |

There is no source of broken-frame art for every card: only cards Konami printed that way have it. Master Duel has no frameless
full art (its other 512 x 1024 pictures are Pendulum art), which is why full art is drawn from the card's own art.

## Save

Save extension tag `shiny`: a version byte (2), then 9 bytes per card and print: `u16 card, u8 rarity (its type: the rarity id, 0 =
Common), u8 art (0 = the card's own artwork, n = alternate art n), u16 owned (copies now), u16 pulled (copies ever pulled from packs),
u8 seen (1 once its face has been drawn with that print; a new pull or gift is 0)`. An entry stays while it is owned or was ever pulled;
a Common in its own artwork isn't kept. Version 1 saves (`u16 card, u8 rarity, u16 count`) are read as art 0, owned = pulled = count, seen. It is read when the card table takes over a save (`Core_AddCardTableListener`) and written by a save
write filter. The data is kept whether Shiny is on or off. A save made by a newer build with a later version is left as it is.

Every pull with a rarity or an alternate art is logged as `<rarity>[ (alternate art n)] ripped: <card> (<id>)`, e.g.
`Ultra Rare ripped: Blue-Eyes White Dragon (4007)`.

Exports of `Yu-Gi-Oh-MoreCards.dll` (`GetProcAddress`), for prizes, shops and trade pages: `Cards_GetCardRarity(card)`,
`Cards_GetCardRarityCount(card, rarity)`, `Cards_AddCardRarity(card, rarity, count)`.

## Detour order

Three orders matter, and all three are set on purpose. A later detour runs first and calls the earlier one.

* **Shiny before BulkPacks** (`dllmain.cpp`): both detour `OpenPackAndGrantCards`. Shiny's must be inside, or bulk purchases roll no rarities.
* **More Cards before Anime Cards** (`"loadAfter"` in Anime Cards' manifest): both detour `CardFace_Build` and `AddQuad`. Shiny's must be inside, so that it records
  the positions Anime Cards moved things to. Plugins otherwise load by name, which would put Anime Cards first.
* **Shiny before `Card.cpp`'s art hook**: `Card::Install` attaches later, at card setup, so custom art asks Shiny first.

## How it draws

1. `CardFace_Build` (0x14074E7D0) and `AddQuad` (0x1408795A0) record where each face's art, attribute and stars are drawn.
   * The art is the biggest quad that isn't the frame.
   * The stars are the last run of evenly spaced, same-size squares, as many as the card has.
   * The attribute is the biggest remaining square.
   * These detours sit inside Anime Cards' (see Detour order), so its moved stars and attribute are what gets recorded.
   * The name and text box are fixed rectangles on the game's own frame. With Anime Cards they are left out.
2. `CardFace_RenderToTarget` (0x140754420) only records drawing, so the card id is queued.
3. The recorded clear of a 400 x 580 target to grey (`ClearRenderTargetView`) ties that texture to the card.
4. Any draw with such a texture in pixel shader slot 0 is drawn a second time with `ShinyShader.h` blended over it, using the game's
   own vertex shader (only shaders that output SV_POSITION, COLOR0, TEXCOORD0).
5. The device comes from `D3D11CreateDeviceAndSwapChain` (thunk 0x14090D2B0): the first call that has a swap chain.

The rarity is per card id. Every copy of a card you own as an Ultra shows as an Ultra, the opponent's copies included.

## Not done yet

* The 20th / 25th / 10000 logos, which need their picture.
* Choosing which owned rarity a card shows, per card or per deck slot. Today a card always shows its highest tier.
* Showing the rarity on the pack result grid (a label or sound for big pulls). Today big pulls are logged.
