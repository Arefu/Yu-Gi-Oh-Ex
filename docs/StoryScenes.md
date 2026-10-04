# Story scenes (scriptdata_#.bin): the campaign's dialog

`main/scriptdata_<L>.bin` (E F G I J S) holds the scenes played before and after campaign duels. The parser is
`File Type Libraries/ScriptData` (`StoryScriptTable`, `StoryScriptJson`).

Scenes are edited in the **Story editor** tab (`Tools/Shared/Editors/StoryDuelEditor.cs` + `SceneEditor.cs`, in WolfX and WolfEx), next to
the duel they belong to: pick a duel, then its **Intro / Win / Lose scene** tabs. **Yu-Gi-Oh-Campaign** (`StoryScripts.cpp`) applies
WolfEx's `storyscripts.json` to the game.

## Scenes and duels

A scene is named after its duel's key (dueldata): `<key>_INTRO` (before the duel), `<key>_OUTRO` (after a win), `<key>_OUTRO_LOSE` (after
a loss). The game finds it with `StoryDuel_FindDialogScript` (0x14082D660), **ignoring case**, first match. A duel without a scene just
skips it.
* The game has 307 scenes: 154 intros and 153 outros (two of them spelled `AWorldofChaos`, which still matches `AWorldOfChaos`).
* There are no `_OUTRO_LOSE` scenes in the shipped data.
* 29 duels, mostly VRAINS, have none at all.

## File layout

| Part | Layout |
|---|---|
| Header | u64 linesOffset, u32 scriptCount (307), u32 lineCount (7340) |
| Scripts | scriptCount x {u32 firstLine, u32 lastLine (inclusive), u64 nameOffset}; they cover the lines in order |
| Names | the script names |
| Lines | lineCount x {u64 speaker, position, expression, text} |
| Strings | the line strings, in order |

* All strings are UTF-8, zero terminated, never shared, with no padding.
* Every language has the same scripts and lines; only the texts differ.
* All six files round-trip byte for byte.

## A line

| Speaker | Position | Expression | Text |
|---|---|---|---|
| `command` | `BG` | background `pdui/dialog_bg/<name>` (.jpg, 1920x1080) | – |
| `command` | `PROP_ON` / `PROP_OFF` | prop `pdui/dialog_props/<name>` (Millennium items, cards...) | – |
| `infn8` | (CENTER / NONE) | – | narrator box (name = character 139's); empty text hides it |
| character key | `LEFT`, `CENTER`, `RIGHT`, `NONE` (leave), with `FADEIN` / `FADEOUT`; empty = stay | `smile`, `anger`, `dark_smile` (costume_expression)...; empty = keep | what they say |

**Position** is upper-cased and substring-matched, so `LEFT_BACK`, `BACK_LEFT` and `Left` are all LEFT. The BACK/FRONT parts do nothing.

**Character steps** (`StoryScene_PlayLine`, 0x14082F890):
* With an empty position, no text and an expression, the step only changes the expression.
* With an empty position otherwise, the character stays where they are; if they aren't on stage, they take LEFT, or RIGHT if LEFT is taken.
* `{GAMERTAG}` in the text becomes the player's name.

**Stage slots** (`g_StorySceneSlots`, 0x140A72370) on the 1920x1080 stage. Each slot gives the sprite's x, its depth, where an occupant
is pushed aside to, and where it exits:

| Slot | 0 | 1 | 2 LEFT | 3 CENTER | 4 RIGHT | 5 | 6 |
|---|---|---|---|---|---|---|---|
| x | −300 | 120 | 500 | 960 | 1460 | 1780 | 2220 |

Entering an occupied LEFT pushes its occupant to slot 1, and RIGHT pushes to slot 5. Slots 0 and 6 are off stage.

**Sprites** (`StoryScene_GetCharacterTextures`, 0x14074AED0): the base is `pdui/dialog_chars/<key>[_<costume>]_neutral.png`, with the
expression's picture `<key>[_<costume>]_<expression>.png` drawn over it (same size).
* `dark_smile` means costume `dark` with expression `smile`.
* The editor's expression list comes from those file names.

## In the exe (IDB)

* `LoadStoryScriptData` (0x14074A290) runs first in `LoadLanguageContent`'s campaign part.
  * It loads the file into one malloc'd block, `g_pStoryScriptFile` (0x140D4DEF0, `StoryScriptFile`), and makes the offsets absolute.
  * It frees the old block with `free()` on the next load.
* `StoryScene_PreloadTextures` (0x14082ECF0) loads every picture a scene uses before it plays.
* `StoryScene_PlayLine` (0x14082F890) plays one line.
* Types: `StoryScriptFile`, `StoryScriptEntry`, `StoryScriptLine`, `StorySceneSlot`.

## The scene editor

* **Left:** the steps (who / position / expression / text), with Add line, Narrator, Background, Prop, Duplicate, Up / Down, Remove,
  and ◀ Prev / Next ▶ (Page Up / Page Down) to play the scene through.
* **Under the steps:** the selected step: who (character keys, `command`, `infn8`), position, expression or picture (the pictures that
  exist for that character, background or prop), and the text in every language.
* **Right:** the stage as of the selected step.
  * The editor works it out the way the game does: background, prop, characters in their slots with their expressions, the speaker lit
    and the others dimmed, and the dialogue box with the name plate.
  * **Drag a character** to LEFT / CENTER / RIGHT, or off the stage's edge (NONE). If the selected step is theirs, that step changes;
    otherwise a new step is added after it.
* **Create <key>_INTRO** (on an empty scene tab) starts a scene with:
  * the background of the duel's other scenes, or one from its series;
  * the duel's player LEFT and opponent RIGHT, in their costumes.
* **Remove scene** drops a scene.
* Changing a duel's key renames its scenes, and **Duplicate** copies a duel's scenes with it.

**What's approximate:** sprites are drawn at their own size, standing on the stage's bottom edge and centred on the slot's x. The
dialogue box is drawn by the editor, not taken from the game's `campaign_dialog` sheet. Fades aren't animated.

## storyscripts.json (WolfEx → Yu-Gi-Oh-Campaign)

```json
{ "scripts": [ { "name": "MyDuel_INTRO", "lines": [
    { "who": "command", "position": "BG", "expression": "classic_school" },
    { "who": "yugimuto", "position": "LEFT", "expression": "smile", "text": { "E": "Let's duel!" } } ] } ] }
```

It lists only scenes that differ from the game's or are new, each one whole. A scene listed with no lines removes the game's.

Yu-Gi-Oh-Campaign hooks `LoadStoryScriptData`, which runs at start and on a language change. After the game's load, the plugin:
1. builds one new block in the game's layout: the game's scenes, with the JSON's replacing those of the same name (ignoring case), plus
   the new ones;
2. picks the texts in the game's language (falling back to English);
3. allocates the block from ucrtbase's heap, so the game's next `free()` is valid;
4. frees the game's block and stores its own in `g_pStoryScriptFile`.

Built, but not yet run in the game. Yu-Gi-Oh-Campaign must be switched on in Config.ini: `[Yu-Gi-Oh-Core]` `Yu-Gi-Oh-Campaign=1`.
