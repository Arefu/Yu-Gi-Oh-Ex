# BOB for pages (page scripts)

New menus and pages without C++. A page is written as a short BOB script in WolfX (**Art & menus > Page scripts**). WolfX compiles it into
the version 2 page file Yu-Gi-Oh-RIX already runs (docs/PageEngine.md), the same way EffectScript is compiled for effects: the plugin
never sees the script.

Status (2026-10-19): compiler, editor and the RIX game actions built. Every demo page (docs/examples/rix-demo) converts to a script and back
to the same JSON. **Not yet tested in game** (the page engine itself isn't either).

## Files

| File | Made by | Read by |
| --- | --- | --- |
| `Yu-Gi-Oh-Ex\pages\<name>.bob` | you (WolfX) | WolfX only |
| `Yu-Gi-Oh-Ex\pages\<name>.json` | WolfX, on save | Yu-Gi-Oh-RIX |
| `Yu-Gi-Oh-Ex\menus\bob-<name>.json` | WolfX, when the script has `button on ...` lines | Yu-Gi-Oh-RIX (at start-up) |

A version 2 page file with no script opens in the editor as a script made from it; saving writes the script beside it.
Headless: `WolfX.exe --compile-page <script> <out>` and `WolfX.exe --decompile-page <page.json> <script>`.

## Example

```
header "Card Shop";
button on main "Card Shop" "Buy single cards." -> page("shop");

menu buy at 160, 200 size 660, 500 {
    button "Blue-Eyes White Dragon" "1,000 DP" if game:dp >= 1000
        -> take_dp(1000) -> { give_card(4007); sound(52); } else message("Not enough DP.");
    choice "Difficulty" ["Easy", "Normal", "Hard"] bind var:difficulty default "Normal";
    toggle "Show prices" bind var:prices;
    button "Back" -> back;
}

text "You have {game:dp} DP and {game:owned.4007} Blue-Eyes." at 1000, 220 width 760 textSize 26;
```

`//` starts a comment. Statements end with `;`; a menu ends with its `}`.

## Statements

- `header "Title";` - the page's header (default: the page name).
- `edit <game button> ["Label" ["help"]] [hidden] [-> action];` - changes one of the game's main menu buttons (`cardShop`, `deckEditor`,
  `quitGame` ... as in docs/MenuFiles.md): its text, hides it, or gives it a new action. Written to the same menu file.
- `button on <menu> "Label" ["help"] [page <menu page>] [key "k"] -> action;` - a button on one of the game's menus (names as in
  docs/MenuFiles.md, e.g. `main`). It shows after the game restarts.
- a widget: `<widget> [value] <fields> ;` (a `menu` takes `{ items }` instead of `;`).

## Widgets

| Script | Page file `kind` | The value written straight after it |
| --- | --- | --- |
| `menu` | menu | its id (the variable set to the chosen item) |
| `text` | text | the text |
| `image` | image | the resource |
| `slider` | slider | the label |
| `digits` | digits | how many wheels |
| `card` | cardInfo | the card |
| `cards` | cardGrid | `owned` / `all` |
| `decks` | decks | |
| `deck` | deckInfo | the deck |
| `series` | seriesTabs | |
| `characters` | characters | the series |
| `user` | user | (the player's name, avatar and DP, top right) |
| `related` | relatedCards | |
| `box` | cardBox | the card ids (`box "4007, 4041" cols 4 rows 2`) |

Fields: `at x, y`, `size w, h`, `if <condition>` (visible), `enabled <condition>`, `-> action`, `onchange action`, `rows "plugin.list"`.
**Any other field is written as `name value`** and passes straight into the page file (`bind var:x`, `default "5"`, `textSize 26`,
`align left`, `colour "#FFFFFFFF"`, `spacing 100`, `max 10`, `side 1` ...), so a widget field RIX learns later works without a compiler change.

Menu items: `button "Label" ["help"] ...;`, `choice "Label" ["help"] ["A", "B"] ...;` (or `["Easy" = "1", "Hard" = "2"]`),
`toggle "Label" ["help"] ...;`, and `rows "plugin.list" [-> action];` (a plugin fills the menu).

## Actions

`{ a; b; c; }` runs several in order.

| Script | Does |
| --- | --- |
| `page("name")` / `back` / `quit` | open a page / close this one / quit the game |
| `goto(deckEditor)` | a game screen (MenuFiles.md names, or an id) |
| `video("intro")` | plays movies\intro.mp4 full screen (Yu-Gi-Oh-Media) |
| `call("mymod.thing")` | an action a plugin registered |
| `set(var:x, "text")`, `toggle(var:x)`, `add(var:x, 1)` | change a variable or a Config.ini line |
| `message("..")`, `confirm("..") -> action` | the game's OK / Yes-No boxes |
| `if <condition> -> action else action` | branches |
| `popup "Title" [at x, y] { button "A" -> ...; }` | the game's popup list |
| `show_card(4007)`, `reset_filter`, `sound(39)` | full card view, reset the card grid filter, a UI sound |
| `give_dp(500)` | adds DP to the profile (up to 999,999,999) |
| `take_dp(500) -> action else action` | takes DP if the profile has enough, then runs the first action; otherwise the `else` (or the error sound) |
| `give_card(4007[, copies])` | gives copies (up to the save's 3), marked as seen |
| `take_card(4007[, copies]) -> action else action` | takes copies if the profile has them |
| `unlock(card_shop)` / `lock(...)` | the main menu buttons a new profile has greyed out: `card_shop`, `battle_packs`, `duelist_challenge` |
| `unlock_character(12)` | a chardata id (0-239) shows in Free Duel |

Amounts and card ids may be a variable: `give_dp(var:reward)`. An action name the compiler doesn't know is passed on as
`{"name": value}` (RIX logs "not understood" if it doesn't know it either).

## Conditions and values

Conditions: `var:x` (set and not 0), `not var:x`, `var:x = "v"`, `var:x != "v"`, `game:dp >= 1000`, `game:dp < 1000`, joined with `and`.

Values (in conditions and in any text as `{...}`):

| | |
| --- | --- |
| `var:name` | a page variable (shared by all pages and with plugins) |
| `config:Section/Key` | a Config.ini line (new keys must go into WolfX's ConfigCatalog) |
| `game:dp` | the profile's DP |
| `game:owned.<id>` | copies of a card the profile owns (0-3) |
| `game:cardname.<id>` | a card's name |
| `game:unlocked.<menu>` | 1 when `card_shop` / `battle_packs` / `duelist_challenge` is unlocked |
| `game:character.<id>` | 1 when a character is unlocked |
| `game:language` | the game's language id |

| `game:password.<8 digits>` | the card with that password (0 = none; custom cards' passwords too, with Yu-Gi-Oh-MoreCards) |
| `game:atk.<id>` / `game:def.<id>` | a card's ATK / DEF (empty when it is "?"; a Link monster's DEF is empty) |
| `game:level.<id>`, `game:kind.<id>`, `game:attribute.<id>`, `game:type.<id>` | its level / rank / link rating and the game's numbers for its kind, attribute and type |
| `game:monster.<id>`, `game:spell.<id>`, `game:trap.<id>`, `game:link.<id>`, `game:known.<id>` | 1 or 0 (`known` = its ATK isn't "?") |
| `game:rare.<id>` / `game:rarity.<id>` | 1 / `rare` when the card is in a booster pack's rare slot, else 0 / `common` (the game marks every card one or the other) |
| `game:median.<value>.<rare\|common>` | the median of a page value over every monster with a known ATK of that rarity (var:card set to each in turn) |
| `value:<name>` | a page value (below) |

**Values inside names:** `game:owned.{var:card}`, `{game:cardname.{var:card}}` - the inner value is read first. The right side of `>=` / `<`
can be a value too: `if game:dp < value:price`. In a widget field that takes a card or deck, write the value as text:
`card "{var:card}"` (a bare `var:card` there is the name of the variable, which is what `bind` wants).

`game:` values are read only; the actions above change them. RIX reads them from the save in memory (Get_PlayerSection /
Get_CardUnlockTable, as the Card Store does); with no profile loaded they are empty and the actions do nothing (logged).

## Values and maths

A page can name texts and reuse them - the way to write rules (prices, rewards, unlock costs) without a plugin:

```
use "card-store-prices";                                  // another page file's values, here too (a values-only script is fine)
value ATK = "{game:atk.{var:card}|0}";
value price = "{value:fixed.{var:card}|{= clamp(round(ATK * 1.8 / 5 / 10) * 10, 100, 999999)}}";
```

- `value <name> = "<text>";` (or a number). Read as `{value:name}` in text, `value:name` in conditions and amounts
  (`take_dp(value:price)`), or by its bare name inside `{= ...}`. It is worked out each time it is read, so a value that names `{var:card}`
  follows the card. Names can have dots (`fixed.4007`), so `{value:fixed.{var:card}}` is a per-card table.
- `{name|fallback}`: the fallback when the value is empty - `{value:fixed.{var:card}|500}`.
- `{= ...}`: maths. `+ - * / % ^ ( )`, comparisons `< <= > >= == !=` (1 or 0), `and`, `or`, `not`, numbers, values by name, and
  `min`, `max`, `clamp(x, lo, hi)`, `round`, `floor`, `ceil`, `abs`, `exp`, `log`, `sqrt`, `if(test, then, else)`. `{...}` inside is
  read first. An expression that can't be read gives an empty text (logged once).
- Values are shared by every page once a page that defines them (or uses their file) has opened.

## Values from plugins

A plugin can supply its own `game:` values (RIX API, `Yu-Gi-Oh-RIX.h`):

```cpp
int __cdecl Value(const char* argument, char* buffer, int size, void* user);   // writes UTF-8, returns the length (0 = no value)
if (RIX::Functions().RegisterPageValue)                                         // null with an older RIX
    RIX::Functions().RegisterPageValue("mymod", &Value, nullptr);               // pages: {game:mymod.4007}
```

## Worked example: the Card Store

`docs/examples/card-store` is the whole Card Store - it was the Yu-Gi-Oh-BetterCardShop plugin (about 1,100 lines of C++, retired on
2026-10-19) - as four page scripts and the menu file one of them writes:

| Script | Does |
| --- | --- |
| `card-store.bob` | the store menu (Booster Packs = the game's shop screen, Card Shop, Enter Password); `edit cardShop -> page("card-store");` points the main menu's Card Shop button at it (`menus\bob-card-store.json`) |
| `card-store-shop.bob` | every card in the deck editor's grid, bound to `var:card`; confirm: "already 3" / "not enough DP" / Yes-No, then `take_dp(value:price) -> give_card(var:card)` |
| `card-store-password.bob` | eight digit wheels; Unlock looks the password up (`game:password.{var:pw.code}`), charges `value:password_price`, gives 3 copies and shows the card |
| `card-store-prices.bob` | the prices, values only: settings (`monster_price`, `spell_trap_price`, `flat_price`, `password_cost`), the card (`ATK`, `DEF`, `LEVEL`, `RARE`, `LINK`), the rules (`attack_price` ATK x 1.8 / 5, `classic_price` Forbidden Memories' curve, `rarity_price` = the median monster of the same rarity, your own formulas) and per-card fixed prices (`value fixed.4007 = "5000";`) |

Copy `pages\*` into `Yu-Gi-Oh-Ex\pages` and `menus\bob-card-store.json` into `Yu-Gi-Oh-Ex\menus`, and open the scripts in WolfX
(Art & menus > Page scripts) to change them. The old plugin's Config.ini `BetterShop-*` settings and `prices.json` are gone: the settings
and fixed prices are lines of `card-store-prices.bob`. Booster packs (`packs.json`) are Core's (docs/Packs.md).

## Adding to the language

- A new widget field: nothing to do (`name value`).
- A new widget: add its word to `Kinds` (and `Positional` if it takes a leading value) in `Tools\WolfX\Content\PageScript.cs`.
- A new game action: one `else if (step.contains(...))` in `RunStep` (`Yu-Gi-Oh-RIX\PageScreen.cpp`), one `case` in `Step()` and one
  line in `StepText()` in PageScript.cs, and a row here.

## Next (candidates)

From the shared headers (`Dependencies\Yu-Gi-Oh-Ex`), what could be exposed next:
- **Start a duel** against a character with a deck (Campaign's AiDeck / Free Duel setup), with starting LP and time limit
  (`YGO::DUEL::Set_StartingLifePoints`, `Set_DuelTimeLimit`) and tag duels (TagDuel exports). Needs the Campaign plugin's duel setup,
  so it is a bigger step than the save actions.
- **Packs**: open a pack / give a pack (Core Packs.cpp): a `give_pack` action and `game:pack...` values in RIX.
- **Music**: play a track (Yu-Gi-Oh-Media).
- **Story**: unlock / jump to a story duel.
- **Duel values** in conditions (in-duel pages don't exist yet): LP, hand counts (YuGiOh-DUELSTATE.h).
- BAB blocks for pages, after the script is proven.
