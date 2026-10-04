# Sprite sheets (.dfymoo) and animlists

The same two editors are in **WolfX** and **WolfEx**. The code is shared in `Tools/Shared/Editors`, and the file formats are parsed by the `Dfymoo` and `Animlist` libraries in `File Type Libraries`.

* **WolfX** ("Sprite Sheets (dfymoo)" and "Animlists" tabs) opens and saves files on disk, such as an unpacked YGO_2020.
* **WolfEx** ("Sprite sheets" and "Animlists" tabs) can also:
  * **Open from game**: reads `YGO_2020.dat`, or your loose override if there is one.
  * **Save as a loose override** in `<game>\YGO_2020\<same path>`. The game loads that file instead of its own, the same way the Anime Frames mod replaces the card frames.
  * **New for game** (animlists): replaces one of the game's title animations.

## Sprite sheets

A sheet is a `.png` plus a `.dfymoo` list of named rectangles. The game asks for a sheet by resource name (`pdui/doShared`) and for a sprite in it by name (`DFX::TLayerAnimoo::SelectByName`).

| Line | Meaning |
| --- | --- |
| `i tk2d 1`, `w 1890`, `h 848` | Header: the format and the sheet's size |
| `n arrow_1` | A sprite's name |
| `s 1812 444 73 60` | x, y, width and height on the sheet |
| `o 7 7 85 74` | Optional: the sheet stores the sprite with its empty border trimmed; this puts it back at 7, 7 inside an 85 x 74 picture |
| `~` | Separates sprites |

**Editor:**

* **Drawing and editing rectangles**
  * Drag on empty space to draw a new sprite. Hold Ctrl to draw over an existing one.
  * Drag a rectangle to move it, or drag a handle to resize it.
  * Arrows nudge 1 px (Shift: 8 px or one grid step). Ctrl + arrows resize.
* **Snapping**: everything lands on whole pixels, then snaps to the grid (px to 64) and to other sprites' edges. Hold Alt to turn snapping off.
* **Tools**
  * **Fit to pixels** shrinks the selected sprite to its visible pixels.
  * **Find sprites** adds a sprite for every island of visible pixels that no sprite covers yet. Start a **New from PNG** sheet with it.
* **Views**
  * The preview shows the selected sprite the way the game draws it, with any trimmed border put back.
  * Wheel zooms around the cursor; middle-drag or Space-drag pans; the status bar shows the pixel and its colour under the cursor.
* Undo/redo with Ctrl+Z / Ctrl+Y.

## Animlists

Title screen animations (`title\anims\<X>\animlist.combined.txt`, otherwise `animlist.cropped.txt`) and duel arenas (`arenas\<X>\animlist.txt`). They are read by `YGO::ANIM::Animlist_Load` (0x140745D30):

```text
side, 0                                  title menu on the left (x 568); any other number: on the right (x 1367)
Utopia39_BG, 0, 0, 0                     picture name (no extension; .png or .jpg in the same folder), x, y, slide
Utopia39_Character.cropped, 770, -10, 30
```

* A file holds up to 10 layers; the first is furthest back. Lines starting with `#` or `;` are comments.
* `slide` is a vertical bob in pixels, on the title screen and in arenas alike (`YGO::ANIM::Animlist_UpdateSlide`, 0x140746510, called by `RIX::ScreenTitle::Update` and `YGO::DUEL::DuelBackground_Tick`). Every frame one phase advances by the frame time and wraps at 2 pi; each layer with a non-zero slide is drawn at `y + slide * cos(phase)`. So it moves between y - slide and y + slide, one cycle about every 6.3 seconds, all layers together, starting at y + slide. The game's files use 30 on the characters and 0 (still) on the rest.
* `side` only matters on the title screen (where the logo and menu go). Arenas ignore it.
* Title screen or arena is told apart by where the file is, as the game names them: `arenas\<X>\animlist.txt` is an arena, `title\anims\<X>\animlist.combined.txt` / `.cropped.txt` a title screen. For a file outside the game the editor goes by the name (`animlist.txt` = arena) and has a **Screen** dropdown to change it.
* The game plays only the title animations and arenas it knows (fixed lists in the exe: 23 title folders; the arena table in `YGO::DUEL::Arena_LoadBackground`). So a new one **replaces** one of them: **New for game** lists both, and saves `animlist.combined.txt` (title) or `animlist.txt` (arena) in that folder's loose override, which the game reads before its own file.

**Editor:**

* The layers sit on the 1920 x 1080 screen.
* A click picks the front-most layer with a visible pixel under the cursor, so the full-screen background doesn't get in the way.
* Drag to move. Moves snap to the grid and to guides (the other layers' and the screen's edges and centres); Alt turns snapping off.
* The layer list has a visibility tick for each layer (designer only), and Forward / Back to reorder.
* **Add picture** adds a layer. A picture from outside the folder is copied in as a `.png` when you save.
* The dashed box shows where the title logo and menu go for the chosen side (title screens only; arenas have no menu).
* **Play slides** animates the slide as the game does; stop it to edit at the file's positions.
