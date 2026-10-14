# Arenas

## The game (IDB)

* `Arena_LoadBackground` (0x14076F200) is the only reader of `g_ArenaFolderTable` (0x140A52290, .rdata): `{int id, char* folder}` rows ended by a null folder. The game's 14: 1 classic, 2 5ds, 3 zexal, 4 gx, 5 arc5, 6 blimp, 7 urban, 8 barian, 13 fusion_dimension, 14 synchro_dimension, 15 xyz_dimension, 16 you_show_duel_school, 17 vrains_dimension, 18 vrains_2019. An unknown id falls back to classic.
* For the duel's arena it first loads **`arenas/<folder>/animlist.txt`** (`Animlist_Load`: an animated arena, its pictures named relative to that folder, e.g. `ground, 0, 0, 0` = `Ground.png`), and when there is none the still picture **`arenas/<folder>.jpg`** (`Texture_LoadFromDataFile`). Animated in the game: blimp, vrains_dimension, vrains_2019.
* The table is referenced twice in that function: `lea rbx, [rip+...]` at 0x14076F246 and `cmp [rip+...], r8` (row 0's folder) at 0x14076F25C.
* `main/arenadata_#.bin` holds names but the game never reads it.

## New arenas (Yu-Gi-Oh-Campaign `Arenas.cpp`, `arenas.json`)

```json
{ "arenas": [ { "id": 100, "folder": "my_arena", "name": { "E": "My Arena" } } ] }
```

The plugin copies the table next to the exe with the new rows and points both operands at the copy. A duelist's or story duel's arena can then be the new id. The arena's files (`arenas\<folder>.jpg`, or `arenas\<folder>nimlist.txt` + pictures) are game content: they go in the Ex patch.

## WolfX: Campaign > Arenas

* The game's 14 are listed first (their look can be changed: **Set picture...** for a still one, the Animlists / Sprite sheets pages for an animated one).
* **New arena...** copies the selected arena: a still one copies its `.jpg`; an animated one copies its `animlist.txt` and pictures. Then restyle it.
* **Set picture...** writes `arenas\<folder>.jpg` from any image (saved as JPEG).
* New arenas appear in every arena picker (Characters, Story editor, Music, Tutorials).

## What an animlist can do (verified in IDA 2026-10-11)

* `Animlist_Load` (0x140745D30): lines `name, x, y, slide`, at most **10** layers; an optional `side, N` line (title screen); `#` / `;` start a comment.
* `Animlist_BuildLayers` (0x1407458A0): one picture per layer (`<folder>/<name>`, any case), placed at x, y, in list order (later = in front).
* `Animlist_UpdateSlide` (0x140746510) is the **only** animation, called by `DuelBackground_Tick` (arenas) and `ScreenTitle::Update`: one shared timer += frame time (wraps at 2 pi, about 6.3 s a cycle), and each layer with `slide` != 0 gets `y = y + slide * cos(timer)`. No x movement, rotation, scale, fading or frames; all layers bob in step.
