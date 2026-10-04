# How the game draws a card

Everything the game shows as a card (hand, field, deck editor, trunk, card details) is one texture per card: a **400 x 580 "card face"**
the game composes itself from the artwork, a frame image, text and icons. Traced in `YuGiOh.exe.i64`; the names, structs
(`CardFace`, `CardTextSlot`, `CardFaceBatch`, `CardFaceVertex`, `FullCardProps`, `CardKindInfo`, `CardFaceFonts`, `CardImageManager`)
and comments are all in the IDB.

## The pipeline

```
Image_Request(manager, cardId)                  any thread that wants a card picture (widgets, duel field)
  -> Image_GetLoadedOrQueue                     cached texture, or queue the id and return FrameTextures[frame] (the blank frame) meanwhile
       -> Image_EnqueueLoad                     id goes on the loader queue, the loader thread is woken

CardFace_LoaderThreadLoop (loader thread)
  -> CardFace_CreateJob(cardId, reusedTarget)   new CardFace (0x750 bytes)
       -> CardFace_Build(face, cardId)          decode art, lay out text, emit every quad (see below)
       -> push on the render queue

GFX::RenderThread_ProcessQueues (render thread, every frame)
  -> CardFace_RenderNextQueued                  ONE face per frame
       -> CardFace_RenderToTarget(face)         draws the batches into the face's 400x580 target, which is the card's texture from now on
       -> CardFace_Destruct
```

* `g_CardImageTable` (`0x140D4E0C8`) points at the `CardImageManager`: a slot per internal id (`RefCount`, `CacheIndex`), a cache of at most
  **120** card textures (released ones are evicted oldest first and their render target reused), and `FrameTextures[19]`, the
  plain frame of each frame type. Those double as the placeholder while a card is still loading.
* Only internal ids below 10166 are cached (checks at `0x140752A31` and `0x140754192`, both patched by Yu-Gi-Oh-MoreCards).
* Because the face is baked into a texture, **changing how a card looks means changing `CardFace_Build`'s output** (its quads, text,
  matrices). Nothing later knows about names or ATK.

## Card data it reads: `FULL_CARD_PROPS` (`0x142927600`)

`FullCardProps[65536]`, 160 bytes, indexed by **Konami id**, filled by `Setup_FullCardProps`. Getters are `0x14081A3xx..0x14081A7xx`.

| Offset | Field | Getter | Used on the face for |
| --- | --- | --- | --- |
| +0x08 | Name | `Get_NameFromFullCardProps` | name |
| +0x1C | IsMonster | `Get_IsMonster` | ATK/DEF line, type line, line above ATK |
| +0x1E | IsTrap | `Get_IsTrap` | |
| +0x2D | IsTuner | `Get_IsTuner` | |
| +0x2F | IsPendulum | `Get_IsPendulum` | pendulum text and scales |
| +0x31 | IsLink | `Get_IsLink` | `LINK-n` instead of `DEF/` |
| +0x48 | ATK (the one drawn) | `Get_EffectiveAtkFromFullCardProps` (`0x14081A5B0`) | `ATK/` |
| +0x4C | Attribute (1 Light .. 7 Divine) | `Get_AttributeFromFullCardProps` (`0x14081A5D0`) | attribute icon |
| +0x54 | DEF (the one drawn) | `Get_BaseDefFromFullCardProps_0x54` (`0x14081A610`) | `DEF/` |
| +0x58 | Spell/Trap property (0 none, 1..6) | `Get_SpellTrapCardPropertyFromFullCardProps` | property icon |
| +0x5C | Kind (index into `KIND_TABLE`) | `Get_CardTypeFromFullCardPropsByKonamiId` | everything via `KIND_TABLE` |
| +0x60 | StarCount (level stars; 0 for Xyz/Link) | `Get_StarCount` | level stars |
| +0x74 | RankStars | `Get_RankStars` | rank stars when StarCount is 0 |
| +0x78 / +0x7C | Pendulum scales | `Get_PendulumScaleL/R` | scale numbers |
| +0x88 | Link rating | `Get_LinkRating` | `LINK-n` |
| +0x8C | Link arrows (bit mask, 8) | `Get_LinkArrows` | arrow icons |
| +0x94 | Frame (from `KIND_TABLE`) | `Get_Frame` | frame texture, name colour, art size |

The other flag bytes (+0x1D..+0x30) are listed in the IDB struct. Several older getter names are misleading. The one named `Get_EffectiveAtk...`
returns the *printed* ATK (+0x48), and `Get_CardTypeFromFullCardPropsByKonamiId` returns the kind, not the monster type.

### `KIND_TABLE` (`0x140BF7820`, 46 kinds, 6 words each)

`Frame, BaseFrame, Category, DeckSection, Tuner, Pendulum`. The frames match the `duel/frame/card_*` images:

| Frame | Card | Frame | Card |
| --- | --- | --- | --- |
| 0 | Normal | 10 | Synchro |
| 1 | Effect | 11 | Dark Synchro |
| 2 | Ritual | 12 | Xyz |
| 3 | Fusion | 13 | Pendulum Normal |
| 4, 5, 6 | (unused / special) | 14 | Pendulum Effect |
| 7 | Spell | 15 | Xyz Pendulum |
| 8 | Trap | 16 | Synchro Pendulum |
| 9 | Token | 17 | Fusion Pendulum |
|  |  | 18 | Link |

Kinds **13 = Spell, 14 = Trap, 42/43 = Link** monsters. Category 0 is Spell/Trap, 2 is vanilla (flavour) text, 3 is effect text.

## `CardFace_Build` (`0x14074E7D0`): what goes where

Coordinates are card pixels, 400 wide and 580 tall, y down. The base matrix maps that to clip space:
`translate(-200, -290) * scale(1/200, -1/290)`.

### Text (laid out into 8 `CardTextSlot`s, then emitted as their own batches)

Fonts come from `g_CardFaceFonts` (`0x140A4CB00`, JP `0x140A4CB30`) through `Get_FontById` (39 fonts per language set).

| Slot | Text | Font (non-JP) | Width | Drawn at | Fit |
| --- | --- | --- | --- | --- | --- |
| Name | card name | 27 | 301 | 31, 46 (JP 52) | x squeezed to 301 |
| TypeLine (monster) | `[Dragon/Effect]`, upper-cased | 29 | 336 | 32, 436 (JP 440) | |
| TypeLine + SpellTrapSecondLine | `[SPELL CARD` ... `]` around the icon | 28 | none | 330, 83 and 360, 83 (JP 370, 86) | |
| Description | card text (before `[Pendulum Effect]`) | 30..34, largest that fits | 336 | 32, below the type line (Spell/Trap: 32, 438) | y squeezed to 94 px (Spell/Trap 109) |
| PendulumText | pendulum effect | 30..34 | 278 | 62, 365 (JP 369) | y squeezed to 66 px |
| PendulumScaleL / R | scale numbers | 36 | none | 42, 411 and 357, 411 | |
| AtkDef | `ATK/2500 DEF/2100` or `ATK/2300 LINK-3` (spaces -> U+2007) | 35 | none | 367, 544, right-aligned | |

The name colour comes from `g_CardNameColorByFrame[frame]`: white on Spell, Trap, Xyz, Xyz Pendulum and Link, black on the rest.

### Images (all through `AddQuad`, 6 vertices of 24 bytes: x y z u v colour)

| Batch | Quad | Rect |
| --- | --- | --- |
| 0 | artwork (`ArtTexture`) | 48,106 304x304; pendulum frames (13..17): 26,104 347x444 |
| 1 | frame (`FrameTextures[frame]`) | 0,0 400x580 |
| 2 (monsters) | line above ATK/DEF, colour 0xD0231815 on a white texture | 32,530 - 368,531 |
| 3 | icons from the UI atlas (`Icon_GetScaled`): | |
|  | attribute (icon id attribute + 129), size 37 | centred 353.5, 45.5 |
|  | Spell/Trap property (`g_SpellTrapPropertyIconIds`, 141..146), size 20 | 343, 83 (JP 86) |
|  | link arrows (icon 87 + bit), native size | from the atlas |
|  | stars, size 28: level (icon 139) right to left from x 346, 28 apart (27 above 11); rank (icon 140) left to right from x 54, 28 apart. Loop at `0x14074FF80`, constants in `0x140A4CC28..CCE8` | y 83 |

**Batches 0, 1 and 2 have a fixed vertex count of 6** (constants written at `0x14074F930`, `0x14074FA19`, `0x14074FAFE`), not a count taken from the vertex cursor. Never skip their `AddQuad`: the batch would draw the next 6 vertices instead. Skipping the line quad made batch 2 draw the attribute icon's quad with the white texture (a solid white square). To hide one, emit a zero-area quad.

`CardFace_RenderToTarget` then draws batch by batch (texture, matrix, blend flag) into the target, cleared to 0xFF808080.

## What Yu-Gi-Oh-AnimeCards hooks, in these terms

| Hook | Is | What the plugin does |
| --- | --- | --- |
| `0x14074E7D0` | `CardFace_Build` | remembers the card being built |
| `0x1408795A0` | `AddQuad` | hides the line above ATK (zero-area quad, see batch note), scales and moves the art (both rects), moves the Spell/Trap icon |
| `0x140766540` | `TextSlot_Layout` | font 35 (AtkDef) -> the ATK number only; description fonts (30..34) -> the DEF number on monsters; **every other text blanked** (name, type lines, pendulum text and scales) |
| `0x140877EC0` | `Matrix4_Translation` | only while `CardFace_Build` runs (it is also called by `DFX::TLayerAnimoo::Render` and two duel functions): 367,544 (AtkDef) -> custom ATK position and scale; x == 32 -> custom DEF position and scale |
| `0x14081A5D0` | attribute | pass-through |
| `0x14081A630` | Spell/Trap property | monsters get property 1 so the icon is drawn for them too |

Changes made for the anime frames in `Mods/Anime Frames` (2026-09-29):

* Level / rank stars are drawn as one row centred on `StarsX`, `StarsY` (default 200, 465.5: the middle of the frame's coloured band
  between the art's border, y 434, and the ATK/DEF boxes, y 497; `StarsY` < 0 keeps the game's y 83). Rows wider than `StarsMaxWidth`
  (240, so they clear the attribute circle at x 324) are shrunk to fit. The `CardFace_Build` hook reads the star count with the game's getters (StarCount, else RankStars) and resets a counter; the
  `AddQuad` hook re-places each 28 px star quad by index, keeping the game's spacing (28, or 27 above 11 level stars). The Spell/Trap
  property hook only acts while `CardFace_Build` runs (a `thread_local` flag).
* The attribute icon goes in the frame's circle: centre 346, 469.5 on monster frames and 198.5, 469 on the Link frame. It is not drawn on
  Spell and Trap frames, which have their symbol in the art. Icons come from `pdui/STEAM_icons` (.png + .dfymoo sprite list); the card
  uses the 37 px `ICON_ID_ATTR_L_*` sprites (id attribute + 129). Stretched to fill the 45 px circle they went soft and the circle's pale
  fill showed round them as a light ring, so the plugin draws the 48 px `ICON_ID_ATTR_*` sprites (attribute + 119) 48 px across instead,
  covering the circle.
* ATK and DEF are laid out **centred** (alignment 0x12) and unwrapped, on the centres of the frame's two boxes (107.5, 526.5 and 291.5, 526.5).
  The game lays ATK/DEF out bottom-right (36) and the card text top-left (9), so the same y put the two numbers at different heights. On
  Link monsters the right-hand box (the red one) shows the Link rating. `IsAbilityWrapFont` also checked only the first card-text font.
* Config keys (`[Yu-Gi-Oh-AnimeCards]`): `CustomAtkX/Y`, `CustomDefX/Y` (centres), `AttributeX/Y`, `LinkAttributeX/Y`, `AttributeSize`.
* The **level badge** (a star or rank icon plus the number, drawn by the duel UI over each card in your hand, not part of the card face) is
  hidden: `DuelCardView_UpdateLevelBadge` (`0x1407C6980`) reads the view's card data (`view+0x30`): `+292` kind (1 level, 2 rank, else
  off), `+284` the number, `+164` the location (zone 13 = hand). The hook sets the kind to 0 for that one call. Config
  `HideLevelBadge`: 0 = show (vanilla), 1 = hide in the hand (default), 2 = hide everywhere. The kind and number are filled by
  `DuelCardView_SetCardAtLocation` / `DuelCardView_RefreshCardAtLocation` from `DuelCardDisplayInfo_Get` (`0x1407AD800`).
* The **big number over each monster in your hand** is not that widget: `DuelHand_Draw` (`0x1407B8570`) formats `data+284` with
  `"%d"` for kind 1/2 and draws it with `DuelHand_DrawLevelNumber` (`0x14078B0B0`, only caller `0x1407B8E5C`). When `HideLevelBadge`
  is 1 or 2 the plugin detours that drawer to a no-op (it never calls the original, so its arguments don't matter).

Text alignment values (`TextSlot_Layout` align, `Layout_FinishLine` / `Layout_Build`): 1 left, 2 centre, 4 right; 8 top, 0x10 centre,
0x20 bottom.

Things the trace corrects in the plugin (behaviour unchanged, only noted):

* `IsTrapSpellCard` returns **false for Spell, Trap and Link** (kinds 13, 14, 42, 43) and true for other monsters. The name is back to front,
  so "!IsTrapSpellCard" means "Spell, Trap or Link", not "monster".
* The hook named `Get_RawDefFromFullCardProps` (`0x14081A5D0`) is the **attribute** getter.
* The address flagged `TODO` (`0x14081A630`) is correct: it is the real Spell/Trap property getter.
* "Large ATK/DEF font" 36 is the game's **pendulum scale** font.
* The `x == 32` translation check catches both the Description and the monster TypeLine batches (both are drawn at x 32). The TypeLine is
  blank anyway, so only the Description (the DEF number) shows.
* ATK is read with `0x14081A5B0` (+0x48) and DEF with `0x14081A610` (+0x54): the printed values, as the vanilla face uses.

## Fonts (`fontbin/`) and why scaled text is blurry

`Font_Load` (`0x140761A80`) loads font *id* from two archive files named by `Font_GetFileName` (e.g. `FONT_ID_CARD_ATKDEF` = 35,
`FONT_ID_CARD_ATKDEF_SCALE` = 36, the pendulum-scale numbers):

* `fontbin/<NAME>.png`: the atlas. White glyphs with an alpha channel, **plain antialiased bitmaps rendered at one pixel size** (not a
  signed distance field). ATK/DEF is 37 x 1017 px at 18 px; `_SCALE` is 81 x 1019 at 32 px.
* `fontbin/<NAME>.fbin` (little endian): `u32, u32`, the source font name (`font/MatrixBoldSmallCaps`, null-terminated), 10 values (pixel size
  18 / 32, then line height 19 / 33 and other metrics, 0.45), then two glyph tables: `u32 count` (353), and per glyph a `u16` character + a
  64-byte record `{u16 char, u16 pad, u16 w, u16 h, float w, h, top, left, advance, u0, v0, u1, v1, 4 floats of half-texel padding, 0}`.
  The UVs are **normalised (0..1)**.

A scale in the text's matrix (AnimeCards' `AtkTextScale` 3.1) only stretches those bitmap glyphs, and texture filtering smears them, so
**scaling up always goes soft**. Switching to the 32 px font (what `UseLargeAtkDefFont` does) helps, but it is still stretched about
3.1 x 18 / 32 = 1.7 times. To stay sharp, glyphs need about as many pixels as they cover on the card (18 x 3.1 = about 56 px), and the scale
should be 1 or less.

Two ways to get there:

1. **A bigger font:** render the typeface at the size you draw it (e.g. 56 px), write a new atlas and `.fbin` with the new metrics, and use
   a scale of 1.
2. **A higher-resolution atlas, same `.fbin`:** render every glyph k times larger into a k-times atlas at the same relative place.
   The normalised UVs and the 18 px layout metrics still hold, so the game lays the text out exactly as before, and a matrix scale of k then
   draws texels 1:1.

Either can be dropped in as loose files with Yu-Gi-Oh-Core's `LooseLoading` (`YGO_2020\fontbin\...`) without repacking the archive.

The card face itself is a 400 x 580 texture. Where the game shows a card larger than that on screen (the card details view at high
resolutions), the whole face, art included, is magnified too. Sharper fonts cannot fix that part.

## Where to change things for an anime frame

* **Hide or replace text:** `TextSlot_Layout`, keyed by font id (27 name, 28 Spell/Trap label, 29 type line, 30..34 text, 35 ATK/DEF,
  36 pendulum scales). Blanking the text (as the plugin does) keeps the slot valid; an empty layout left the slot without a texture and crashed.
* **Move or resize text:** its `Matrix4_Translation` (positions in the table above; they are unique except x 32).
* **Move, resize, hide or swap images:** `AddQuad`, by rect (or wrap `CardFace_Build`'s batches directly).
* **Different frame art:** `FrameTextures[frame]` in the `CardImageManager`, or the frame images themselves (`duel/frame/card_*`).
* **Stars / attribute:** their icon ids and sizes above. Returning 0 from `Get_StarCount` / `Get_RankStars` / the property getter skips
  them, and the property getter can also force an icon on (the plugin's trick).
