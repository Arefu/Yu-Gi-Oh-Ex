# WolfX: everything a tutorial needs to cover

This is a planning document for writing WolfX tutorials (written guides or videos). It lists **what exists in the editor, what every option
does, what has to be explained, and the traps to warn people about**, page by page. It is not the tutorial itself: each section ends with
the points to teach and the demos to record.

How to use it:

* Part 1 is the concepts every tutorial depends on. Teach these first (or link back to them); most user mistakes come from not knowing them.
* Part 2 is the window itself (opening data, saving, menus, the shared controls).
* Part 3 is page by page, in the order of the list on the left of the window.
* Part 4 is a suggested series order, Part 5 the known gaps to check before recording, Part 6 a glossary.

Sources for the details: `docs/WolfXContent.md` (the save rules, the archive), `docs/EffectLanguage.md` (EffectScript), `docs/CardRendering.md`
(how the game draws a card), `docs/PluginManifests.md`, `TESTING.md`.

> Status markers used below: **[tested]** = checked working; **[untested in game]** = WolfX writes it, but nobody has yet confirmed the game
> shows it; **[limit]** = a known restriction worth saying out loud.

---

## Part 1 - Concepts to teach before anything else

### 1.1 What WolfX is

One editor for *Yu-Gi-Oh! Legacy of the Duelist: Link Evolution* (Steam). It edits:

* **the game's own content** (its cards, decks, characters, story, text, packs, art...), and
* **new content** the game can't hold on its own (custom cards, new packs, new characters, new menu pages...), which the
  **Yu-Gi-Oh-Ex launcher plugins** add to the game when it starts.

WolfX used to be two programs (WolfX for the game's content, WolfEx for new content). They were merged on 2026-10-01; old guides and
forum posts that say "WolfEx" mean the pages now under WolfX (New cards, Effects, New packs, Unlocks, Pages, Menus).

### 1.2 Standard content vs additional content (the one rule)

Every page saves by the same rule, and the page header (grey line under the page title) says which applies:

| | What it is | Where it is saved | Needs a plugin? |
|---|---|---|---|
| **Standard** | a change to something the game already has (Blue-Eyes' ATK, a deck's cards, a string) | back into the game's own file, in the game's format | no |
| **Additional** | something the game's files can't hold (a new card id, a 241st character, a new pack) | JSON in the **Yu-Gi-Oh-Ex** folder | yes - the header names it |

Teach with one example of each: change Blue-Eyes' ATK (standard, works with no mods) and add a custom card (additional, needs
Yu-Gi-Oh-MoreCards).

### 1.3 Two ways to open the game data

| | The game's **YGO_2020.dat** | An **extracted folder** |
|---|---|---|
| What | the game's 1.5 GB archive (with YGO_2020.toc) in the Steam folder | a copy of the files taken out of the archive, any folder name |
| How | File > Open YGO_2020.dat (Ctrl+O), or "Open the Steam install" on the start screen | File > Open extracted folder (Ctrl+Shift+O) |
| Saving | into a small **patch archive** next to it (`YGO_2020-Ex.toc / .dat`); the game's archive is never written | as loose files in that folder |
| Undo | File > Remove WolfX's patch (deletes the two patch files) | your own backup |
| Good for | most people | modders who keep files in git / compare versions |

Points to teach:

* **The patch archive** (like an IPS patch): WolfX saves only the files you changed into `YGO_2020-Ex.dat` with its own `.toc`. The
  game's `YGO_2020.dat` / `.toc` are never touched, so Steam never needs to "verify" them. **Yu-Gi-Oh-Core** loads the patched files over
  the game's archive when the game starts whenever the patch files exist (`[Yu-Gi-Oh-Core] PatchArchive`, default `YGO_2020-Ex`, empty = off). Launch without the mods
  and the game is vanilla.
* **Remove WolfX's patch** (File menu, or the status-bar link) deletes the two files: every standard change is undone at once (it does not
  touch the Yu-Gi-Oh-Ex JSON). The title bar says how many files the patch changes.
* **The game must be closed** to save (it keeps the patch open). WolfX refuses and says so.
* A loose file in `<game>\YGO_2020\...` (the loose-file folder, `[Yu-Gi-Oh-Core] LooseLoading=1`) wins over the patch and the archive, for reading and saving.
* An extracted folder that lacks files every page needs (card bins, the art and deck/pack `.zib`s) shows a **"N required files missing"**
  link in the status bar; click it for the list.
* [untested in game] Core serving the patch is new (2026-10-02): check a changed card, deck and pack in game before recording.

### 1.4 The Yu-Gi-Oh-Ex folder and content.json

* Additional content lives in `<game folder>\Yu-Gi-Oh-Ex\` (for an extracted folder: next to it). File names: `cards.json` (+ art),
  `genres.json`, `relatedcards.json`, `cardlinks.json`, `text.json`, `characters.json`, `decks.json`, `storyduels.json`,
  `storyscripts.json`, `packs.json`, `unlocks.json`, `pages\*.json`, `menus\menus.json`, `tutorials\*.json`, `sprites\*.json`.
* WolfX keeps **`Yu-Gi-Oh-Ex\content.json`** up to date by itself: it lists which plugins each file needs. When the game starts, the
  launcher switches those plugins on and shows a message naming any that aren't installed. So a Yu-Gi-Oh-Ex folder copied to another PC
  says what it needs. [untested in game]
* A JSON file is deleted when nothing is left in it (e.g. you undo your last custom genre).
* File > Show the Yu-Gi-Oh-Ex folder opens it in Explorer.

Which page needs which plugin (from `docs/WolfXContent.md`, worth a table in the intro tutorial):

| Page | Additional file | Plugin |
|---|---|---|
| New cards / Effects | cards.json | Yu-Gi-Oh-MoreCards (+ Yu-Gi-Oh-Effects for effects) |
| Card genres / Related cards / Unlocks | genres.json / relatedcards.json / unlocks.json | Yu-Gi-Oh-MoreCards |
| Text tables | text.json | Yu-Gi-Oh-MoreCards |
| Characters / Decks / Story editor | characters.json / decks.json / storyduels.json + storyscripts.json | Yu-Gi-Oh-Campaign |
| New packs | packs.json | Yu-Gi-Oh-BetterCardShop |
| Pages / Menus | pages\, menus\menus.json | Yu-Gi-Oh-RIX |
| Card links, Tutorials (new numbers), Sprite sheets (new sheets) | cardlinks.json, tutorials\, sprites\ | none yet - saved, but nothing reads them |

### 1.5 Card ids (people always mix these up)

| Id | Example (Blue-Eyes) | Where it matters |
|---|---|---|
| **Konami id** | 4007 | the id everything in WolfX and the plugins uses. Game cards are 3900-14968 |
| **internal id** | 101 | the row in the game's card files (CARD_Prop, names, text). Mapped by `CARD_IntID.bin` (Card ids page) |
| **password** | 89631139 | the 8 digits printed on the card; used by the in-game password machine |
| **custom card id** | 15300-19999 | new cards. 14969-15234 are "ghost" ids the game keeps effect data for - don't use them |

### 1.6 Languages

The game has 7 text languages: **E** English, **F** French, **G** German, **I** Italian, **J** Japanese, **R** (not Russian: Japanese
ruby / reading text in some tables), **S** Spanish. Pages that edit text have a language picker; most JSON files fall back to English
when a language is empty.

### 1.7 Saving, unsaved changes, closing

* Each page has its own **Save** (Ctrl+S while the page has focus). **File > Save all** (Ctrl+Shift+S) saves every page with changes.
* Opening other data, or closing WolfX, with unsaved pages asks Save / Don't save / Cancel and names the pages.
* After saving, pages that show the same files reload them (e.g. save names on the Card Manager -> Name sort sees them).
* **Restart the game** to see changes; nothing is live-reloaded.

---

## Part 2 - The window

### 2.1 Opening

* **Start screen** (when nothing is open): *Open the Steam install* (found automatically from Steam's library list), *Open YGO_2020.dat...*,
  *Open an extracted folder...*, and a **Recent** list. Drag and drop works too: drop the game folder, YGO_2020.dat/.toc or an extracted
  folder on the window.
* The last thing opened is reopened at start.
* **Title bar** shows what is open: `WolfX - YGO_2020.dat - <folder>`, `... (edited by WolfX)` once the archive has saves, or
  `WolfX - extracted folder - <folder>`.

### 2.2 Menus

* **File**: Open YGO_2020.dat (Ctrl+O), Open extracted folder (Ctrl+Shift+O), Open recent, Save all (Ctrl+Shift+S), Show the open folder,
  Show the Yu-Gi-Oh-Ex folder, Remove WolfX's patch, Exit.
* **Tools** (older tools, worth one short segment):
  * **Config Editor**: edits the launcher's `Config.ini` (every plugin's settings, with descriptions).
  * **Extract Game / Pack Game / Verify Extracted Files / Set Game Path**: the legacy extract-repack workflow through `Yami-Yugi.exe`.
    With in-place saving most people won't need it; explain when it's still useful (making an extracted folder to open).
  * **Language**: the language the older pages (Strings, Forbidden & Limited, Deck files) use.

### 2.3 Shared controls (teach once, used everywhere)

* **Left list** of pages, grouped: Cards, Campaign, Shop, Text, Art & menus, Files.
* **Page header**: the page name, and the "Standard: ... / Additional: ..." line saying where it saves and which plugin it needs.
* **Status bar**: what just happened; links for "required files missing" and "Remove WolfX's patch".
* **Show larger (⤢)**: every preview has a small button in its top-right corner (double-click works on most). It moves the live preview
  into its own big window, shaped like what it shows (16:9 for game screens, card-shaped for cards). It keeps updating while you edit and
  stays interactive. Esc or closing it puts it back. The story scene and tutorial windows have Play / Prev / Next buttons.
* **Find boxes** match a name or an id. **Show** filters ("All", "Changed", ...) are on most list pages: "Changed" is the quick way to
  review your edits before saving.
* **Small windows**: below about 760 x 440 a page scrolls instead of squashing.
* Pages open on their first row.

### 2.4 Command line (for power users / automation)

* `WolfX.exe --page "Card genres"` opens on a page; `WolfX.exe --card 4007` opens the Card Manager on a card.
* `WolfX.exe --compile <script> <result>` compiles an EffectScript without the window; `--script-tree` dumps parse trees.
* `--render-pages <game> <out> [zoom]` renders the page designer's galleries and saved pages to PNG.

---

## Part 3 - Page by page

Template used for each page: **Purpose · Saves to · Layout · Options · Workflows to demo · Teach / warn**.

### CARDS

#### Card Manager

* **Purpose**: every card in one place - the game's and your custom ones - with a live card preview.
* **Saves to**: game cards: `bin\CARD_Prop.bin` (stats), `bin\CARD_Indx/Name/Desc_<L>.bin` (names and text), `bin\CARD_Pass.bin`
  (passwords), `bin\CARD_Named.bin` (archetypes). Custom cards are edited on New cards (one click away). Genres / related cards / links
  follow their own pages' rules.
* **Layout**:
  * Left: Find (id or part of a name), Show (All / Game / Custom / Changed), list with Id / Name / Kind. Custom cards are blue,
    changed cards orange.
  * Top right: the **card preview drawn the way the game draws it** (the archive's own frames, fonts and icons: name, attribute, level /
    rank stars, Spell/Trap label and icon, type line, card text in the largest of the game's five text sizes that fits, Pendulum box and
    scale, ATK/DEF or LINK-n, Link arrows) and a summary beside it (name, type line, stats, text, Konami / internal id, password).
  * Bottom right: tabs **Properties, Text, Genres, Related cards, Links**.
* **Options (Properties)**:
  * Kind (46 kinds, shown as "13: Spell"), Attribute, Type (race).
  * Level / **Rank** / **Link rating** (the label follows the kind).
  * ATK and DEF, in steps of 10, each with a **"?"** box (the game's unknown value).
  * **Link arrows**: a 3 x 3 picker around the card. A Link monster has no DEF: its DEF bits *are* its arrows. A note says if the number
    of arrows doesn't match the Link rating.
  * Spell / Trap icon (Normal, Counter, Field, Equip, Continuous, Quick-Play, Ritual).
  * **Pendulum scale**: the game has one scale per card (both sides show it). Next to it, **"Other 4 bits"**: a value stored after the scale
    that is *not* a scale (0-3 on the game's cards, meaning unknown) - leave it alone.
  * Password (8 digits), Archetypes (picker, any number).
* **Options (Text)**: language, name, text. A Pendulum monster's text is "monster text, then a line `[Pendulum Effect]`, then the pendulum
  effect" - the preview splits them into the two boxes the same way the game does.
* **Genres / Related cards / Links tabs**: those pages themselves, showing only the picked card (edit here or on the page; it's the same).
* **Workflows to demo**:
  1. Rebalance a card: find Raigeki, change it, save, restart the game, show it.
  2. Rename and rewrite a card in two languages; switch the Text tab's language and watch the preview follow.
  3. Make a Link monster's arrows match its rating; show the arrows on the preview.
  4. Give a card a new archetype; show the deck editor's filter in game.
  5. "Changed" filter -> review -> Save.
  6. Show larger on the preview.
* **Teach / warn**:
  * Changing kind changes the frame and which fields apply (greyed fields don't apply to that kind).
  * Renaming cards affects the deck editor's alphabetical order: rebuild it on **Name sort**.
  * The game ships 10,165 cards: you edit them, you don't add rows here. New cards go through New cards.
  * **Same name as** (Properties): the card whose name this one counts as (bin\CARD_Same.bin, docs/CardSame.md). "always" = it is
    that card in every duel name check (Harpie Lady 1 -> Harpie Lady); "while an effect says so" = Cyber Dragon Zwei style.
    [limit] "while an effect says so" may change nothing in game yet (the code that reads it hasn't been found).
  * **Index letters** (Text tab): the card's first three letters in that language (bin\CARD_Kana1/2/3_<lang>.bin, docs/CardKana.md);
    Japanese = the reading in hiragana. **From name** fills them; a rename updates them. [limit] Only the Japanese build of the game reads them.
  * The preview line shows the card's **name order** (its place in the deck editor's A-Z order, in the Text tab's language) and warns when a
    rename has put it out of place: rebuild on **Name sort**.

#### New cards

* **Purpose**: create custom cards (cards.json + art). Needs **Yu-Gi-Oh-MoreCards**.
* **Saves to**: `Yu-Gi-Oh-Ex\cards.json` and the art copied next to it as `<id>.jpg/.png`.
* **Layout**: like the Card Manager - list (Find, Id / Name / Kind), the card preview (drawn the same way as a game card), tabs
  **Properties, Text, Art**. Toolbar: Save, New card, Duplicate, Delete..., **Genres, related cards, links...** (opens the card in the Card
  Manager, which has those tabs).
* **Options**:
  * Card id (15300-19999; New card picks the next free one).
  * Kind, Attribute, Type, Level / Rank / Link rating, ATK, DEF, Spell/Trap icon, **Link arrows** (saved as `linkmarkers`),
    **Pendulum scale** (`scale`), Archetypes (a new archetype gets the next free code from 419 and is added to Archetypes.json).
  * **Same name as** (`sameName`): the card whose name this one counts as, always or while an effect says so (MoreCards applies it).
  * Limitation (Forbidden / Limited / Semi-Limited / Unlimited) and **Owned from the start** (copies every profile has).
  * Text: name, description. Art: Choose art..., with warnings.
* **Workflows to demo**: make a monster from scratch; make a Spell; Duplicate a card to make a variant; add art; give it an effect (hand
  over to the Effects page); give it genres / related cards via the button; save; launch the game; find it in the deck editor / shop.
* **Teach / warn**:
  * **Art: 304 x 304, JPG, 24-bit** is what the game expects. Other sizes are stretched (the page warns in red and in the status bar).
  * ATK/DEF must be multiples of 10; ids must be unique and in range - Save lists every problem instead of saving.
  * The art file is copied **on save**, not when picked.
  * Deleting a card leaves its art file behind.
  * Fields the editor doesn't show (YGOPRODeck extras like card_sets) are kept as they are when saving.
  * Custom cards need a deck or a pack or an unlock to be obtainable: link to Unlocks / New packs / Decks.

#### Effects

* **Purpose**: give custom cards effects, written in **EffectScript** or built from **blocks**. Needs Yu-Gi-Oh-MoreCards **and**
  Yu-Gi-Oh-Effects.
* **Saves to**: the card's `effectScript` (the text) and `effectClone` (what the game runs) in cards.json, with the rest of the card.
* **Layout**: card list with tick boxes (Filter, Check all shown, Clear all checks); the editor with **Script** and **Blocks (drag and drop)**
  tabs; Compile, Apply script to all checked, Attach from card text...; the compiled JSON (read only).
* **Script tab**: Scintilla editor with colours, line numbers, Ctrl+Space completion (what can follow at the caret), red squiggle at the error.
* **Blocks tab**: Blockly. Categories:
  * **Effect** - one effect: name (optional), *when* (trigger), *only if* (condition), *cost*, *do* (actions, run in order), *only once
    per turn*. Stack Effect blocks for several effects. "Act exactly like the game card..." for `as(...)`.
  * **Only if** - you control no monsters / you control (cards).
  * **Cost** - discard N, pay N LP, Tribute N, detach N, banish / discard / Tribute this card.
  * **Actions** - draw, gain LP, burn, search, send to GY, revive, Special Summon (hand / Deck / GY / this card), destroy, banish, return to
    hand, add to hand from GY / banished, gain ATK, equip.
  * **Which cards** - kind + conditions: Type, Attribute, Level/ATK comparisons, archetype, card id, or a raw condition.
* The two tabs are always in sync. A script the blocks can't show (an error, or something with no block yet) locks the blocks with a
  note so they never overwrite it.
* **Workflows to demo**:
  1. Pot of Greed clone in blocks (Effect -> do -> draw 2) and the same in script; Compile; show the JSON.
  2. A monster trigger: "when this card is sent to the GY: search a Warrior".
  3. A cost + condition: "cost: discard 1 card, do: draw 2".
  4. `as("Axe of Despair") with atk 700` for an equip.
  5. Bulk: tick 10 cards, Apply script to all checked.
  6. Attach from card text: the translator reads card text and writes scripts; review what it did.
* **Teach / warn**:
  * How it works under the hood (one slide): every action borrows a game card that already does it and changes its numbers; with `as(...)`
    the card plays *as* that game card in a duel. That's why some combinations aren't possible ("there is no game card to borrow").
  * Compile before saving: an uncompiled script isn't what the game runs.
  * Triggers that compile but are not confirmed in a duel (flip, destroyed by battle, sent to GY...) - check docs/EffectLanguage.md before
    promising them. [untested in game for several triggers]
  * The language is growing; the blocks lag the script by one table entry. If a new keyword shows a lock note, use the Script tab.

#### Effect library

* **Purpose**: learn the language from cards you know, keep scripts you reuse, start a new effect from one.
* **Contents**: ~1171 game cards written in EffectScript (a leading **~** = the card does more than the script says; a note lists what's
  missing), plus your own entries (**★**) and game entries you changed (**✎**).
* **Saves to**: `%APPDATA%\WolfX\effect_library.json` (yours only; the shipped library is never changed).
* **Options**: filter, "Only cards that are exactly this effect", "Spells and Traps only", "Only mine and edited ones"; Script / Blocks tabs;
  toolbar: Save to my library (Ctrl+S), New entry, Revert (game entry) / Delete (your entry), Check (compile), Use on the Effects page,
  Copy script.
* **Demo**: find "Sangan", see it as blocks, change it, Check, Use on the Effects page for a custom card.

#### Card genres

* **Purpose**: the genre tags (Draw, Destroy Monster, Search Deck...) the game uses on the card details page and in the duel AI.
* **Saves to**: game cards -> `bin\CARD_Genre.bin`; custom cards -> `genres.json` (Yu-Gi-Oh-MoreCards).
* **Options**: Show (Cards with genres / Changed / All cards), Find, New card... (give genres to a card that has none), the genre tick list
  (64 bits; some marked "hidden" - the game clears them when it reads the file - or "unused").
* **Teach**: why genres matter (AI choices, "related" filters); hidden bits.

#### Related cards

* **Purpose**: the deck editor's **Related cards** panel ("Related to: Blue-Eyes", "Supports Dragon"...).
* **Saves to**: `bin\tagdata.bin` and `bin\taginfo_<L>.bin`; new tags and custom cards -> `relatedcards.json` (Yu-Gi-Oh-MoreCards).
* **Layout**: tabs **Cards** (a card's related cards, each with a tag and a "why") and **Tags** (the tag definitions: name text, conditions).
* **Options**: Text language; Cards: Add related cards..., Change tag..., Remove, Show tag; Tags: New tag, Duplicate, From conditions,
  Apply, Revert, Cards using it.
* **Teach**: a tag is a reusable rule; a card's related list is built from tags. Demo: make a custom support card show up as related to its
  archetype.

#### Card links

* **Purpose**: `CARD_Link.bin` - which cards / archetypes / counters a card mentions.
* **Saves to**: `bin\CARD_Link.bin`; links with custom cards or archetypes (419+) -> `cardlinks.json`.
* **Options**: Show (All cards with links / Changed / Problems), Find, New card..., Add card..., Archetypes..., Add counter..., Remove.
* **Warn**: [limit] **the game loads this file but never uses it**, and no plugin reads cardlinks.json yet. Document it for completeness;
  don't promise an in-game effect. The "Problems" filter shows entries that are wrong in the game's own file.

#### Card ids

* **Purpose**: `CARD_IntID.bin`, the Konami id -> internal id map.
* **Options**: Show (All ids / Cards / Reserved ids), Find, Copy next free id, set an internal id (0 = no card).
* **Warn**: advanced; custom cards never go in this table (Yu-Gi-Oh-MoreCards gives them slots). 904 Konami ids are reserved with no card -
  the game keeps data for them, don't reuse them.

#### Name sort

* **Purpose**: `CARD_Sort_<L>.bin` / `CARD_Sort2_<L>.bin`, the deck editor's alphabetical order.
* **Options**: Language, Rebuild from names (this language), Rebuild all, Save. The status says whether the two files agree and how many
  cards are out of name order.
* **Teach**: after renaming cards, rebuild. Custom cards are ranked by the plugin automatically.

#### Forbidden & Limited

* **Purpose**: the ban list, `bin\pd_limits.bin`.
* **Options**: Reload, Save, Load Pictures, Use Card IDs (ids instead of names), Add / Remove / Replace, tabs Forbidden / Semi-Limited /
  Limited, counts at the top.
* **Teach**: making a custom format; Unlimited = not on the list. (An older designer page: the look differs from the newer pages.)

### CAMPAIGN

#### Characters

* **Purpose**: the duelists (`main\chardata_<L>.bin`, 240 slots); new characters -> `characters.json` (Yu-Gi-Oh-Campaign).
* **Options**: Language, Show, Find, New character, Duplicate; per character: Key (portrait `<key>_neutral`), Series tab, Deck (deckdata
  id, -1 none), Selectable (Free Duel opponent), Content pack (sku), Arena, Unlocked (new characters); name and bio per language.
* **Teach**: a Free Duel opponent needs a deck it owns (Decks page, Owner); portraits are sprites in `pdui\dialog_chars`; the "key" ties
  character, portraits and story scenes together.

#### Decks

* **Purpose**: the game's decks (`main\deckdata_<L>.bin` + `decks.zib`, 700 slots); new decks / custom cards in decks -> `decks.json`.
* **Options**: Language, Show, Find, New deck, Duplicate; File name (decks.zib/<name>.ydc), Owner (character), Series, Signature card,
  Content pack, Used by; title texts per language; Main / Extra / Side lists with Add..., Remove, Sort A-Z, Clear.
* **Teach**: limits 60 main / 15 extra / 15 side; a deck with a custom card goes to decks.json automatically; giving an opponent a new deck.

#### Story editor

* **Purpose**: the story duels (`main\dueldata_<L>.bin`) and their scenes (`main\scriptdata_<L>.bin`); new ones -> storyduels.json /
  storyscripts.json (Yu-Gi-Oh-Campaign).
* **Layout**: duel list (Id, Series, #, Title; Language, Series filter, Find, New duel, Duplicate, Remove scene); tabs **Duel**, **Intro
  scene**, **Win scene**, **Lose scene**.
* **Duel tab**: key (dialog `<key>_INTRO / _OUTRO / _OUTRO_LOSE`), series, order in the series (1-48), player / opponent character and
  deck (+ costume), arena, reward pack (first win), content pack, "not needed to unlock the opponent's own deck"; title / description /
  tip (after a loss) per language.
* **Scene tabs**: the step list (who / position / expression / text), the stage preview (background, prop, characters in their slots,
  dialogue box), and the selected step's fields. Toolbar: Add line, Narrator, Background, Prop, Duplicate, Up, Down, Remove,
  **◀ Prev / ▶ Play / Next ▶** (Play steps through the scene, each line up as long as it takes to read; Space on the list).
  Drag a character on the stage to move them (off the edge = leave). "Create <scene>" when a duel has none.
* **Demo**: write a 6-line intro scene with a background and two characters; Play it; Show larger with Play there.
* **Teach**: positions LEFT / CENTER / RIGHT (+ FADEIN / FADEOUT), commands BG / PROP_ON / PROP_OFF, `{GAMERTAG}` = the player's name,
  expressions = the portrait files.

#### Tutorials

* **Purpose**: the Steam tutorials (`duel\tutorial\steam_tutorial_NN_<L>.bin`); new numbers -> `tutorials\*.json` [limit: no plugin reads
  new numbers yet].
* **Layout**: Open..., Open from game, Save, Save as..., Tutorial number, Language, New tutorial..., Copy to languages...; tabs **Visual**
  and **Script**; the step list (Op, What); the duel-field preview with playback (|<, <<, <, Play, >, >>, frame counter, message
  frames); step fields (Op, P1-P4, Id, Has text, Insert card name..., text).
* **Teach**: ops (Message, MoveCursor, Pointer...), `$1234` = a card name, `$A $B $X $Y` = button icons, @ colours; Renumber ids is
  cosmetic (the game ignores ids).

### SHOP

#### Packs

* **Purpose**: the game's pack definitions (`main\packdefdata_<L>.bin`) and their card lists (`packs.zib`).
* **Options**: the pack grid (Id, Series, Cost, Kind 82 = reward pack / 66 = battle pack, Name, Title, Text, IsReward, ContentsFile);
  Common / Rare card lists with Add..., Remove, Sort A-Z, Clear; Reload, Save. (Older designer page.)

#### New packs

* **Purpose**: `packs.json` - cards added to the game's packs, and new packs. Needs **Yu-Gi-Oh-BetterCardShop**.
* **Options**: pack list, New pack, Remove new pack; per pack: extra common / rare card ids (or "Add ... by name..."), "Replace the game's
  cards in this pack (instead of adding to them)"; for new packs: title and text per language.
* **Teach**: the way to make custom cards buyable.

#### Unlocks

* **Purpose**: `unlocks.json` - cards every profile owns (Yu-Gi-Oh-MoreCards).
* **Options**: Add by name..., Add by id, Remove selected, Add from game data (what a new profile starts with), Import JSON..., "Owned at
  least (0-3)" per card, "Replace the game's default unlocks".
* **Warn**: test with a spare save profile (see Save editor warnings).

### TEXT

#### Text tables

* **Purpose**: the WORD / DLG tables (card frame words, attributes, UI words...): `bin\WORD_/DLG_ Indx + Text`; added entries -> text.json.
* **Options**: Table, Find, Add entry (English copied to the others until translated), Remove last added; a markup preview (⤢) with
  `@0-@9 @A-@G` colours and `%s` = card name.

#### Strings

* **Purpose**: the UI strings `strings\Strings_STEAM_<L>.BND` (1213 strings) and the credits `main\ui\credits\credits.dat` ("Credits File").
* **Options**: Reload, Save, Credits File, Search (Case Sensitive), Edit String. (Older designer page; uses Tools > Language.)

#### How to Play

* **Purpose**: the in-game manual (`main\howto_db\howtoplay_<L>.bin`, pictures `main\howto_img\help_duelimg_NNN.png`).
* **Options**: Open..., Open from game, Save, Save as..., Language, New language..., Add chapter / topic / sub-topic, Remove, Up, Down;
  Visual and Script tabs; preview of the screen as the game shows it (⤢); per entry: title, sub-topic, picture number or "Use a picture
  file...", text with markup (@7 blue, @B red, @0 normal, @/ @| fonts).

### ART & MENUS

#### Sprite sheets

* **Purpose**: the game's `.dfymoo` sprite lists (names + rectangles on a PNG); new sheets -> `sprites\*.json` [limit: nothing reads them yet].
* **Options**: New from PNG..., Open..., Open from game..., Save, Save as..., Undo / Redo, Fit to pixels, Find sprites, Duplicate, Delete,
  Fit view, Grid; mouse wheel zoom, middle-drag pan; sprite filter and properties; selected sprite preview.

#### Animlists

* **Purpose**: title-screen and arena animations (the layer lists the game plays).
* **Options**: New..., Open..., Open from game..., **New for game...** (replace a title animation with your pictures), Save, Save as...,
  Undo / Redo, Add picture..., Remove, Forward, Back; Screen, Play slides, Title menu side, Grid, Snap to grid / guides, Show grid; layers.
* **Warn**: the game only plays animations it lists - you replace, you don't add.

#### Pages

* **Purpose**: the RIX page designer: your own menu screens (`pages\*.json`, Yu-Gi-Oh-RIX). A RIX button opens one with `{"page": "<name>"}`.
* **Options**: Page, New, Save, Save as..., Delete, Widget gallery, Undo / Redo, Zoom, Grid, Snap to grid / guides, Show grid, Safe area,
  Arrange, Edit; the widget tree (green = shows in game, orange = the game has it but a page can't place it yet); property grid.
* **Teach**: build a page, link it from Menus.

#### Menus

* **Purpose**: `menus\menus.json` - buttons added to the game's menus, and changes to the game's own buttons (Yu-Gi-Oh-RIX).
* **Options**: tabs Buttons / Game buttons; Key, Menu, Page (main menu), Label, Description, Look (borrowed picture), When pressed (goto a
  screen / press a game button / run a plugin action such as `funky.toggleTools`), Which one.

### FILES

#### Archives (.zib)

* Every .zib in the open game data (the .dat, the patch archive or an extracted folder): list its files (card names for card art),
  preview pictures / decks / pack lists, Extract / Extract all, Replace, Add files, Save (into the patch archive).
* [limit] The two card art archives (about 600 MB each) are view and extract only here; change card art on the Card Manager's Art tab.

#### Deck files (.ydc)

* The .ydc files in decks.zib of the open game data, with which game deck (Decks page) uses each; show All / Not used / Used / Changed.
  Edit Main / Extra / Side, Export a .ydc, Replace from one, Add .ydc files; saved into decks.zib (patch archive).

#### Save editor

* `savegame.dat` / `savegame-ex.dat`: tabs General (points), Stats, Cards, Characters, Decks; Open..., Save, Save as...
* **Warn loudly**: back up saves first. Editing unlock tables has wiped a save before. Test on a copy / spare slot.

---

## Part 4 - Suggested tutorial series

1. **Install and first look** - launcher + plugins, WolfX, the start screen, opening the Steam install, the window tour, standard vs
   additional, the patch archive and removing it. *(Part 1, 2)*
2. **Your first edit** - Card Manager: change a card, save, see it in game, undo with Restore.
3. **Reading the card preview** - what each part of the face is; Pendulum and Link specifics; Show larger.
4. **Your first custom card** - New cards end to end, art rules, Unlocks to own it.
5. **Effects with blocks** - 3-4 small effects, then the same in script.
6. **EffectScript for real** - triggers, costs, conditions, `as(...)`, bulk apply, Attach from card text, the Effect library.
7. **Making cards findable** - genres, related cards, archetypes, name sort.
8. **Packs and the shop** - Packs vs New packs, putting custom cards in packs.
9. **Characters and decks** - a new opponent with a deck.
10. **Story** - a story duel with intro / win / lose scenes, Play.
11. **Text and translations** - Text tables, Strings, multi-language card text.
12. **How to Play and Tutorials** - editing the manual and a tutorial.
13. **Menus and pages (RIX)** - a custom menu page and a button to open it.
14. **Art** - sprite sheets and animlists.
15. **Advanced** - extracted folders, ids, Forbidden & Limited formats, the save editor (with warnings), command line.

Each episode: goal shown up front -> steps -> save -> in-game result -> how to undo.

## Part 5 - Check before recording

* In-game confirmation still open: binary toc loading (TESTING.md 6), content.json plugin switching (TESTING.md 7), several effect triggers.
* Use the **Debug** build (`Binaries\Debug\Tools\WolfX.exe`) or rebuild Release first: the Release build can lag behind.
* The block editor needs the Edge WebView2 Runtime (Windows 10/11 have it; say what to do if the Blocks tab shows the "needs WebView2" note).
* Use a spare save profile for anything touching unlocks or the save editor.
* Say so if asked: the card art archives are view-only on the Archives page (art goes through the Card Manager); Card links /
  new tutorials / new sprite sheets are saved but nothing in game reads them yet; index letters only matter to the Japanese build.
* Older pages (Forbidden & Limited, Strings, Packs, Archives, Deck files, Save editor) look different from the newer ones - expected.

## Part 6 - Glossary

* **DAT / TOC** - the game's archive (YGO_2020.dat) and its table of contents (YGO_2020.toc).
* **Extracted folder** - files copied out of the archive.
* **Yu-Gi-Oh-Ex folder** - where additional content (JSON + art) lives.
* **Plugin** - a DLL the launcher loads into the game (MoreCards, Effects, Campaign, BetterCardShop, RIX...).
* **content.json** - WolfX's list of which plugins your content needs.
* **Konami id / internal id / password** - see 1.5.
* **Kind** - the card's kind number (Normal, Effect, Fusion, Link...), which decides its frame.
* **Frame** - the card border picture (duel\frame\card_*.png).
* **Genre** - a card's tag bits used by the details page and the AI.
* **Tag** - a related-cards rule.
* **EffectScript** - the effect language; **blocks** - the drag-and-drop form of it; **effectClone** - the compiled JSON the game runs.
* **Borrowing** - how effects work: a custom card reuses a game card's effect handlers with its own numbers.
* **RIX** - the menu plugin; **page** - a custom menu screen; **sku** - a content-pack id (1 = base game).
* **.zib / .ydc / .dfymoo / .bnd** - the game's archive-in-archive, deck file, sprite list and string table formats.
