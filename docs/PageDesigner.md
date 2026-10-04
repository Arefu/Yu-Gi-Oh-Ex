# Page designer (WolfEx "Pages" tab)

A menu maker: lay out a page on the game's screen with the game's own art, save it, and open it in the game from a menu button.

* The designer is the **Pages** tab of WolfEx (`Tools/WolfEx/Designer`).
* Files go to `<game folder>\Yu-Gi-Oh-Ex\pages\<name>.json`.
* Yu-Gi-Oh-RIX opens a page with the action `{ "page": "<name>" }`. It reads the file again whenever the file has changed, so you can edit a page and reopen it in the game without restarting.

## Using it

| Area | What it does |
| --- | --- |
| Palette (left) | Every widget class the game has (the 112 `widget_*` vftables, see [Widgets.md](Widgets.md)), grouped. Drag one onto the page, or double-click to put it in the middle. The colour says whether it shows up in the game: **green** "shows in game" (header, menu buttons); **orange** "not from a page yet" (the game has the widget and a C++ plugin can place it through YuGiOh-RIX.h, but the page loader does not build it yet); **grey** "designer only" (nothing can build it yet). Saving a page that has orange or grey elements tells you which ones will not appear. |
| Page (middle) | The game's 1920 x 1080 screen. Click to select; Ctrl or Shift click adds to the selection; drag on empty space to box-select. Drag to move and pull a handle to resize. Ctrl + mouse wheel zooms. |
| Properties (right) | The selected element's position, size, text, sprite, buttons and so on. With nothing selected it shows the page: name, header, background, and how many elements the game will build. |
| Widget gallery | A page with every widget of one group laid out on it, to see what exists. It is not saved unless you save it. |

**Snapping.** The game has no layout grid (its screen layouts are coded positions, not data files), so the designer adds its own:

* **Grid**: a size from 4 to 100 game pixels, or off. Moves and resizes land on it.
* **Guides**: while you drag, the element snaps to the edges and centres of the other elements and of the screen, within 8 screen pixels. A pink line shows the guide it snapped to.
* Hold **Alt** while dragging to place freely.
* The dashed pink lines are the screen's centre. **Safe area** shows the middle 90% of the screen.

**Arrange menu:**

* **Align**: one element aligns to the screen; several align to the box around them.
* **Space evenly**, across or down.
* **Same width / height as first**.
* **Usual size** (the widget's own size, or its sprite's size).
* **Bring to front / send to back**: the draw order is `z`.

**Keys:**

| Key | Action |
| --- | --- |
| Arrows | Nudge 1 px |
| Shift + arrows | Nudge one grid step |
| Del | Delete |
| Ctrl+C / Ctrl+V / Ctrl+D | Copy / paste / duplicate |
| Ctrl+A | Select all |
| Ctrl+Z / Ctrl+Y | Undo / redo |
| `]` / `[` | Bring to front / send to back |
| Ctrl+S | Save |

**Art.** Everything is drawn from `YGO_2020.toc` / `YGO_2020.dat`; nothing is unpacked.

* A sprite sheet is a `.png` plus its `.dfymoo` list of named rectangles. The game asks for a sheet by resource name (`pdui/doShared`) and for a sprite by name (`menuheader`).
* An **Image** element can use any sprite of any sheet. Its `...` button opens a picker with thumbnails.
* **Backgrounds** are the archive's full-screen pictures (`pdui\bg_vrains.jpg`, `arenas\*.jpg`) and the series menu backgrounds (`pdui/noise_bgtec#bg_tec` and so on). They are for the designer only; in the game a page shows on the Battle Pack screen.
* Card widgets draw placeholder cards. No card art is loaded, so the designer stays quick.

**Headless pictures.** `WolfEx.exe --render-pages <game folder> <output folder> [zoom]` writes `gallery-1.png` .. `gallery-8.png` (one per widget group) and a picture of every saved page.

## What the game builds from a page file

| Element (`kind`) | In the game |
| --- | --- |
| `header` | Its text becomes the page's title (`RIX_PageDesc::Header`). The game draws the title in its own place, so the element's position is not used. Without a header element, the page's `header` field is used, and without that, the page's name. |
| `buttonList` | The page's buttons: the first `buttonList` on the page, up to 4 buttons. The game lays them out 100 px apart, so the designer keeps the list 100 px high per button. `ButtonsX` is the list's centre (x + width / 2) and `ButtonsY` is the first button's middle (y + 50). Each button has a label, a description (shown while it is highlighted) and an action. |
| `image` | One sprite of any sheet (`resource` + `sprite`), its top-left at `x`, `y`, stretched to `width` x `height`. Built when the page shows and removed when it hides or closes: a `DFX::TLayerAnimoo` in a `DFX::TBase` node on the screen's root, at game z = 20 + `z`. A missing sheet or sprite is skipped, with a line in the console. |
| `text` | `text` in the game's UI font (`FONT_ID_PD_*`, the smallest size at least `textSize` tall, scaled to fit), wrapped to `width`, `align` = `left` / `center` / `right` in the box, `colour` = `#RRGGBB` or `#AARRGGBB`. A `DFX::TLayerText` built the same way as images. |
| everything else | Preview only for now. Widgets marked orange can be built by a plugin with YuGiOh-RIX.h (Trunk, CardInfo, EntryDigit, dialogs, the help bar). Use the page to plan where they go. |

Back (Esc / Backspace / the pad's cancel) closes a page. A page opened from a page's button goes on top of it.

## Actions

Button actions use the menu files' syntax ([MenuFiles.md](MenuFiles.md)), plus `page`:

```json
{ "goto": "credits" }            { "press": "cardShop" }        { "call": "mymod.doThing" }
{ "quit": true }                 { "page": "otherPage" }
[ { "call": "mymod.prepare" }, { "page": "otherPage" } ]
```

To open a page from the main menu, add a button on the **Menus** tab (action "open a page"), or in `menus/*.json`:

```json
{ "buttons": [ { "label": "My Page", "action": { "page": "myPage" } } ] }
```

## File format

```json
{
  "version": 1,
  "name": "myPage",
  "header": "Card Store",
  "background": "pdui/noise_bgtec#bg_tec",
  "elements": [
    { "id": "header1", "kind": "header", "x": 0, "y": 40, "width": 962, "height": 131, "z": 0, "text": "Card Store" },
    { "id": "buttonList1", "kind": "buttonList", "x": 630, "y": 440, "width": 660, "height": 200, "z": 1,
      "buttons": [
        { "label": "Booster Packs", "description": "Buy packs.", "action": { "goto": "cardShop" } },
        { "label": "Passwords", "action": { "page": "passwords" } }
      ] },
    { "id": "image1", "kind": "image", "x": 100, "y": 800, "width": 237, "height": 99, "z": 2, "resource": "pdui/doShared", "sprite": "optionbox" }
  ]
}
```

* Positions are game pixels: 1920 x 1080, with the top left at 0, 0 and y going down. These are the numbers the game's own widgets use (`CreateFromLayout` x / y, `NodeSetX`).
* `x` and `y` are the element's top-left corner.
* Optional fields, as the kind uses them:
  * `text`, `textSize`
  * `resource` and `sprite` (images)
  * `highlighted`
  * `buttons` (button lists)
  * `action`
  * `locked` (designer only)

## Next steps (traced, not built yet)

* **Digits, trunk and card info from a page file.** The code exists in YuGiOh-RIX.h. What is missing is a way for a page file to say what they do (for example which plugin action fills the trunk).
* **Panels** (`pdui/menuframe` and the other plain `.png` pictures): these are not sprite sheets, so they need `DFX::TLayerTexture` traced.
