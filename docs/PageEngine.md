# Page engine (RIX pages, version 2)

New pages, from empty to full, without code. A page file with `"version": 2` opens on a screen of RIX's own (screen id 102,
`Yu-Gi-Oh-RIX/PageScreen.cpp`) instead of borrowing the Battle Pack screen.

**Everything on a version 2 page is one of the game's own widgets.** Each one is lifted from the screen that owns it, with its
constructor, builder, per-frame update, input and data feed, and built on our screen. Plain pictures and text (DFX layers) are allowed as
decoration. The **Demo Page** button on the main menu opens `rix-demo`, which walks through every widget the engine can build so far. It
is how each new widget gets worked out before real pages use it.

Status (2026-10-14): built, **not yet tested in game**.

Pages can also be written as BOB scripts in WolfX (Art & menus > Page scripts), compiled into these files: see docs/PageScript.md. That file also lists the game actions (`giveDp`, `takeDp`, `giveCard`, `takeCard`, `unlock`, `lock`, `unlockCharacter`, `sound`) and the read-only `game:` values (`{game:dp}`, conditions with `"game"` and `"atLeast"`) added on 2026-10-19. Names may nest
(`{game:cardname.{var:card}}`), `atLeast` may be a `"{...}"` value, and plugins add values with `RIX_RegisterPageValue`.

## Files

- Pages: `<game>\Yu-Gi-Oh-Ex\pages\<name>.json`. Version 1 pages (the WolfX page designer's) still open the old way, on the Battle Pack screen.
- The demo: `pages\rix-demo*.json` and `menus\rix-demo.json` (the main menu button). A copy is in `docs/examples/rix-demo`.
- Open a page from any action with `{ "page": "<name>" }`, from a menu file button, or from a plugin with `RIX_OpenPageFile`.

## File format

```json
{
  "version": 2,
  "header": "My Page",
  "elements": [
    { "kind": "menu", "id": "pick", "x": 160, "y": 200, "width": 660, "height": 500,
      "items": [ { "label": "Start", "description": "Shown on the help bar.", "action": { "page": "next" } },
                 { "label": "Difficulty", "values": [ "Easy", "Hard" ], "bind": "var:difficulty" },
                 { "label": "Back", "action": { "back": true } } ] },
    { "kind": "text", "x": 1000, "y": 220, "width": 760, "text": "Difficulty: {var:difficulty}", "textSize": 26 }
  ]
}
```

Positions are in the game's 1920 x 1080 space, with `x` / `y` as the top left. `width` / `height` are also the box the mouse uses to
focus a control. Every element can have `"visible": <condition>`.

| `kind` | Game widget (where it comes from) | Fields |
| --- | --- | --- |
| `menu` | `RIX::MenuKit::menu`: the button list of the Battle Pack, Help & Options and Pause screens | `items`, `source` (a plugin's list), `spacing` (100), `action` (for list rows), `id` (variable set to the chosen item) |
| `slider` | `ScreenHelpSetting::tickLine_t`: the 10-tick volume bar of Help & Options > Settings | `bind`, `min` (0), `max` (10), `default`, `onChange`, `description`, `label` (placed like the Settings rows' labels); a click on the bar sets the level under the mouse |
| `digits` | `widget_EntryDigit`: the code wheels of the Player Match setting screen | `count` (4), `spacing` (140), `bind` (the digits as a number string), `action` (on confirm), `onChange` |
| `cardInfo` | `widget_CardDetails`: the deck editor's card picture and text | `card` (an id or `"{var:name}"`), `width` (486.5) |
| `seriesTabs` | `widget_pickSeries`: the Free Duel series row (series 0..5, as Free Duel's OnEnter lists them) | `bind` (the series id), `onChange`, `action` (on confirm); left / right pick |
| `characters` | `widget_pickCharacter`: the Free Duel opponent grid, filled like `FreeDuel_BuildOpponentList` | `series` (a number or `"{var:...}"`; the list is rebuilt when it changes), `bind` (the character index), `onChange`, `action`; L / R leave the grid |
| `decks` | `widget_RecipeZone`: Free Duel's deck grid with its category tabs (the player's decks) | `bind` (the deck id), `onChange`, `action`; L / R change the tab, so the mouse moves focus away |
| `deckInfo` | `widget_RecipeInfo`: the deck panel beside it | `deck` (an id or `"{var:...}"`), `side` (0 yours, 1 the opponent's look) |
| `cardGrid` | `widget_TrunkZone`: the deck editor's card grid with its filter and sort bar | `cards` (`owned`, default, or `all`; may be `"{var:...}"`, the grid refills when it changes), `bind` (the highlighted card id), `onChange`, `action` (on a confirmed card), `filterX` / `filterY` (the filter panel's centre, 1568.5 / 120). As in the deck editor: the wheel scrolls while the mouse is on the grid's own area, Filter opens the editor's filter panel, a right-click opens the full card view |
| `cardBox` | `widget_CardDisplay`: the Random Pack Store's box of the cards a pack gave (the trunk's grid is one too) | `cards` (ids split by anything not a digit, `"{var:...}"`, or a JSON array; refilled when it changes), `cols` (4) / `rows` (2), `cellWidth` (255.17) / `cellHeight` (370), `gap` (10), `frame` (true), `bind` (the highlighted card id), `onChange`, `action` (on a confirmed card). More cards than cells scroll; a right-click opens the full card view; L / R leave the box |
| `image` | a sprite of a `.dfymoo` sheet (decoration) | `resource`, `sprite`, `alpha` |
| `text` | text in the UI font (decoration) | `text` (with `{var:...}` / `{config:...}`), `textSize`, `align`, `colour` |

The screen's own widgets are always there: the **header** (`widget_TextScroll` at screen+152, built as the Battle Pack screen builds
it) and the **help bar** (Select / Back, plus the focused item's `description`, set the way the Battle Pack screen sets it).

**Menu items:**
- `label`
- `description`
- `action`
- `visible`
- `enabled` (a condition; a disabled item plays the game's "unable" sound)
- a **choice item** has `values` (strings, or `{ "label", "value" }`) or `"toggle": true` (Off / On = 0 / 1), plus `bind`, `default` and
  `onChange`. It shows as "Label: Value"; left / right and confirm change it.

**Focus:** up / down move inside a menu, and past its first / last item they move to the previous / next control. Left / right on a
normal button moves to the next control, and on a slider, digit wheels or a choice item it changes the value. The mouse over a control's
box focuses it. Back (Esc / Backspace / the pad's cancel) closes the page; the last page returns to the screen it was opened from.

## Values, conditions, actions

- **Bindings** (`bind`, `set`, `toggle`, `add`): `"var:name"` is a page variable (kept while the game runs, shared by all pages and with
  plugins: `RIX_SetPageVar` / `RIX_GetPageVar`); `"config:Section/Key"` is a line of `Config.ini` (written at once; plugins usually read
  their settings at start-up, so most take effect on the next start). Any new key must also go into WolfX's ConfigCatalog.
- **Texts** expand `{var:name}` and `{config:Section/Key}`.
- **Conditions**: `{ "var": "name" }` or `{ "config": "Section/Key" }`, true when set and not `0`; add `"equals": "value"` and / or
  `"not": true`. An array means all of them.
- **Actions**: one object, or an array run in order.

| Action | Does |
| --- | --- |
| `{ "page": "name" }` | Opens a page (on top of this one) |
| `{ "back": true }` | Closes this page |
| `{ "goto": "deckEditor" }` | Goes to a game screen (names as in MenuFiles.md, or an id); Back returns here |
| `{ "call": "mymod.doThing" }` | Runs an action a plugin registered (`RIX_RegisterAction`) |
| `{ "quit": true }` | Quits the game |
| `{ "set": "var:x", "value": "text {var:y}" }` | Sets a variable / config line |
| `{ "toggle": "var:x" }` | 0 <-> 1 |
| `{ "add": "var:x", "value": 1 }` | Adds to a number |
| `{ "showCard": "{var:card}" }` | The deck editor's full card view (`widget_CardDetailsFull`) over the page; confirm / Back closes it |
| `{ "resetFilter": true }` | Resets the card grid filter (the filter panel's own reset) and filters every grid on the page again |
| `{ "message": "text" }` | The game's OK box |
| `{ "confirm": "text", "yes": <action> }` | The game's Yes / No box |
| `{ "if": <condition>, "then": <action>, "else": <action> }` | Branches |

After an action, the page re-reads its conditions and texts.

**List sources** (plugins): `RIX_RegisterPageList("mymod.decks", callback, user)`. The callback calls
`RIX_PageListAdd(list, label, description, value)` once per row, each time a page with `"source": "mymod.decks"` is built. RIX itself
registers `rix.demoRows` for the demo.

## How each widget is built (the recipes)

Addresses are named in `YuGiOh.exe.i64`. Widgets are built the first time their page shows and are never freed, because the game keeps
pointers into them; a covered or closed page hides them.

- **Screen**: as `docs/CustomScreens.md` (ScreenBase + copied vftable + `RunJobs` tick), created when the main menu is first up, with
  screen type 4 (the option screens' backdrop).
- **Header**: `widget_TextScroll::CreatePart2(screen+152, root, 0, screen+88, 960, 0, 475)` once, then `Screen::SetHeaderText(screen, text)`.
- **Menu** (`ScreenBattlePackStore::SetupWidgets`):
  1. `MenuKit::menu::Constructor` (152 bytes).
  2. `SetDefinition(menu, {id, label, description}... {-1})`, then `ShowAll`.
  3. A `DFX::TBase` (`MakeShared`, `AddChild` to the root, `SetZ`, `SetPosition(centre x, 0)`).
  4. `CreateItems(menu, node, {startY, spacing, 0, zBase}, screen+88, screen+120)`.
  5. Every frame, `ProcessInput(menu, pressed, listener, -3)`, where the listener is `{ OnItemActivated(listener, ui, id, profile), IsItemEnabled(listener, id) }` (id -2 = Back).

  Labels and descriptions are the item's +56 / +96 (a string id or a `wchar_t*`). Item ids stay under 100: Help & Options' extra
  buttons use 100+ (`MainMenu.cpp`'s CreateFromLayout hook).
- **Volume bar** (`ScreenHelpSetting`):
  1. `tickLine_t::Constructor` (408 bytes).
  2. `Setup(tick, root, z, screen+88, x, y)`. The ticks sit at x + 949 + 49 * i.
  3. Set the level with `widget_Base::SetEnabled` on tick node i (tick+80+32*i) for i < level, and keep the level at tick+400 (`RefreshVisuals`).
- **Digit wheels / card details**: as BetterCardShop's password page (`YuGiOh-RIX.h` `EntryDigit` / `CardInfo`).
- **Free Duel's four** (`ScreenHardChallenge::SetupWidgets` 0x140841110, `OnEnter` 0x1408415F0, `FreeDuel_HandleInput` 0x140840B10):
  - **Series row**: `widget_pickSeries::Constructor`, then `CreateFromLayout(w, root, z, screen+88, 960, 553, screen+120)`.
    Then `SetList(w, vector<int>)`. Every frame, `ProcessInput(w, pressed)`, `HitTest(w, mouse)` and `GetCurrent`.
  - **Opponent grid**: `widget_pickCharacter::Constructor`, then `CreatePart2(w, root, z, screen+88, 960, 553, 0)`.
    Then `SetList(w, vector<{int character, int unlocked}>)`. Every frame, `ProcessInput(w, pressed, held, &confirmed)` and `GetSelected`.
  - **Deck grid**: `widget_RecipeZone::Constructor`, then `CreateFromLayout(w, root, z, screen+88, 118.5, 171, 1, 0)`.
    Then `Refill(true)`, `FillPlayerDecks`, `Refill(false)`. Every frame, `Tick` then `HandleInput(w, pressed | L/R, &clicked)`. Focus look: `SetFocused`.
  - **Deck panel**: `widget_RecipeInfo::Constructor`, then `CreateFromLayout(w, root, z, screen+88, x, y, 2 + side, side, 0)`, then `ShowDeck(w, &id)`.
- **Card grid**: as BetterCardShop's Card Shop (`YuGiOh-RIX.h` `Trunk`). The counts in the grid are written from the save every frame (the game's count function answers 0 above id 14968).
- **Card box** (`ScreenRandomPackStore::SetupWidgets` 0x14085CB30, `Update` 0x14085C7C0 state 4): `widget_CardDisplay::Constructor` (568 bytes), then
  `CreateFromLayout(w, root, z, screen+88, &desc)` with a 96-byte `RIX_CardDisplayDesc` (`YuGiOh-RIX.h` `CardBox::Desc`), then `SetCursorScale(1.03)`.
  `SetCards(w, n, ids, -1, null, null)` (null counters = no numbers). Every frame `ProcessInput(w, pressed, held, &action, &clicked)` (action 3 = confirm / click)
  and `GetSelectedCardId`.
- **Message boxes**: `Screen::ShowMessageText` / `ShowYesNo` on the page screen.

## Next widgets to lift

Candidates from `docs/WidgetTracker.md`:
- the deck editor's own deck list (`widget_DeckList`)
- `widget_TextScroll` as a body text
- the settings row (`widget_SettingsLine`)
- `widget_UserSlot`

Each gets a demo page, then a `kind`.
