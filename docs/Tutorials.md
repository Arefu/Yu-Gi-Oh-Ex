# Steam tutorials (steam_tutorial_NN_&lt;L&gt;.bin)

The Help > Tutorial duels, and the first duel of each campaign series, are scripts in `duel/tutorial/steam_tutorial_NN_#.bin`: `NN` is
the tutorial number (01-26), `#` the language letter (E, F, G, I, J, S ship with the game). They're loaded with `LoadFileFromArchives`,
so a loose `<game>\YGO_2020\duel\tutorial\steam_tutorial_05_E.bin` replaces the archive's copy.

Parser: `File Type Libraries/Tutorial` (`Types.TutorialFile`, `TutorialPlayback`). Editor: `Tools/Shared/Editors/TutorialEditor` - the
WolfX "Tutorials" tab (files on disk) and the WolfEx "Tutorials" tab (the game's files, saved as loose overrides; "New tutorial...").
The library round-trips all 156 shipped files byte for byte, both binary and script form.

## Format (little-endian), YGO::TUTORIAL::LoadScriptFile 0x1408709D0

```text
u16 stepCount, u16 textCount, u32 0, u32 0       the game writes the step table offset (12) and text table offset over the zeros
stepCount x 12-byte step                          { u8 op, u8 p1, u16 p2, u16 p3, u16 p4, u16 id, i16 text }
textCount x { u32 length, u32 0 }                 length in characters; the game fills in each string's offset (0 if empty)
textCount x UTF-16LE string                       length characters + a null, in order
```

* `id` is never read by the game. The shipped files count up from 3 (1 and 2 are skipped) and End has 0xFFFF.
* `text` indexes the text table (-1 = none). Every step with text has its own entry, in step order (identical lines are stored twice).
* The last step is End (0x7F).

## Ops, YGO::TUTORIAL::ExecuteStep 0x1407F5D50

| op | name | parameters |
|---|---|---|
| 0x00 | SetupScenario | P2 = board setup, hardcoded in `YGO::TUTORIAL::SetupScenario` 0x1407F4020: 1, 31, 32, 41-46, 51, 52, 61, 62, 71-73, 91, 92, 101-105, 111, 112, 121, 122, 141-143, 161, 162, 171-173, 200, 210 (others deal the plain decks) |
| 0x01 | Message | text in the message box, waits for the player; P3 != 0 = the upper box. Hides the hint line |
| 0x02 | Hint | text on the hint line (no text hides it) |
| 0x03 | SetTurnPlayer | P2 = 0 you, 1 opponent; plays the turn animation |
| 0x04 | Phase | P2 = phase (0 Draw, 1 Standby, 2 Main 1, 3 Battle, 4 Main 2, 5 End); P3 != 0 only plays the phase banner |
| 0x05 | MoveCursor | P1 player, P2 zone, P3 index. P2 = 12 with a card id in P3 moves to that card |
| 0x06 | Wait | P2 frames (60 a second) |
| 0x07 | WaitForAction | P1 = what the player must do (sets the input mask, `YGO::TUTORIAL::WaitForAction` 0x1407F32F0), P2 detail; optional hint text |
| 0x09 | Flag09 | P1 = 6 sets a duel UI flag |
| 0x0A | Set0A | stores P2, P3 (state +0x538/+0x53C, meaning not traced) |
| 0x0B | ShowCard | P2 = card (Konami id) in the card info panel; 0 clears it, 2 = "the current card" (`CardInfo_ShowCard` 0x14076FB00) |
| 0x0C | PointerSimple | pointer at target P1 (player 0); 0 hides it |
| 0x0D | Pointer | pointer at player P1, target P2, for P3 frames |
| 0x0E | ActivateAt | activates player P1's card in zone P2 if it can be |
| 0x0F | Set0F | stores P2, P4 |
| 0x11 | Label | P1 = label number (a jump target) |
| 0x12 / 0x13 | RetryStart / RetryEnd | a block the game can go back over (its counter starts at 1, so it carries on) |
| 0x14 | AllowInput | P2 != 0 = the player may act |
| 0x15 | Set15 | P2 = 1 unlocks every input |
| 0x17 | StackDeck | card P4 goes to index P3 of player P2's deck (swapped up from below) |
| 0x18 | Flag18 | |
| 0x19 / 0x1B / 0x1D | Set19 / Set1B / Set1D | store P2 |
| 0x1C | AllowCards | P2, P3, P4 = the cards the player may use (0 = none) |
| 0x1E | Goto | jumps to Label P2 |
| 0x7F | End | P1: 0 back to the menu, 1 win the duel, 2 leave tutorial mode. Marks the tutorial done in the save (bit N of PlayerSection +2960, N < 27) |

Anything else (0x08, 0x10, 0x16, 0x1A appear in the files) does nothing.

**Pointer targets** (`YGO::TUTORIAL::Pointer_Set` 0x1407ED010): 16, 20-26, 30-35 and 40-53 are parts of the screen, not zones. 20-25 are the
phase buttons in phase order, and 40-53 are parts of the card info panel (tutorial 5 points at them after ShowCard). Other numbers are
(player, zone). Best guess at the zones, from the engine's numbering: 0-4 monsters, 5-9 spells/traps, 10 field, 12/13 hand, 14 Extra Deck,
15 Deck, 16 GY, 17 banished.

## Text (YGO::TUTORIAL::ExpandText 0x1407F2E90)

* `$` + digits: that card's name (Konami id). An unknown id shows as `@8NNNN@0`.
* `$A` `$B` `$X` `$Y`: button icons (0x1000 / 0x2000 / 0x4000 / 0x8000).
* `@n` colours (as in How to Play; `@2` is the green used for terms, `@0` back to normal); new lines as `\n`.

## Where tutorials are used (fixed tables in the exe)

* Help > Tutorial lists `g_TutorialMenuList` 0x140A780C0: 1-15, 17, 18, 19, 20 (16 is in no menu). Titles are string ids from
  `g_TutorialTitleIds` 0x140A7BD50 (27 entries).
* Campaign series 0-5 start with tutorials 21-26 (`g_CampaignSeriesTutorial` 0x140A77E68).
* Each tutorial's arena: `g_TutorialArenaIds` 0x140A57C20.

So changing a listed tutorial only needs its file. Board setups are fixed in the exe, so a new tutorial picks one of the existing ones.

**Game bug:** End (`ExecuteStep` 0x1407F6457) skips its done-bit *test* for a number >= 27 but still *sets* the bit, so 32 and up write
past the 27 tutorial bits at PlayerSection +2960. Never play a number >= 27 without Yu-Gi-Oh-Campaign on.

## New tutorials (27-99): Yu-Gi-Oh-Campaign `Tutorials.cpp`

WolfX saves a new number as `Yu-Gi-Oh-Ex\tutorials\steam_tutorial_NN_L.json` (`TutorialFile.ToJson`). Besides the steps it holds the
tutorial's place in Help > Tutorial, which the Tutorials page shows for such a number:

```json
{ "tutorial": 27, "language": "E", "title": "My tutorial", "arena": 1, "menu": false, "steps": [ ... ] }
```

`title` is per language (each language's file has its own; none = "Tutorial NN"), `arena` (default 1) and `menu` (default true; false =
not listed) come from the English file, else the first one read. The plugin:

* **Steps**: detours `LoadScriptFile` (0x1408709D0): for a number with a JSON it builds the game's layout, allocates it with the CRT's
  `malloc` (LoadTutorial frees it with `free`) and does the same offset fix-up. The game's language first, then English, then any.
* **List**: detours `ScreenSelectTutorial::OnEnter` (0x1408643F0). The screen is built once at start-up (`RIX::App::Initialize`), so on
  every entry it re-reads the folder, trims its number vector (+712) back to the game's 19 and appends the listed new numbers (game heap
  via `std_vector_int_EmplaceReallocate` 0x140862EA0), then writes the list count (+660) in place so the cursor kept after a duel stays.
* **Title and done mark**: after `RefreshItems` (0x140864560) it sets each new number's row title (`SetTextById` takes a wide-string
  pointer) and the done mark (`ListRow_SetMark(row + 0x70, 334)`).
* **Arena**: detours `Set_CurrentArenaId`; only the call from the tutorial list (return address 0x140863D14) is changed, for numbers >= 27.
* **Done**: a stub detoured onto the bit write (0x1407F6470) asks the plugin first; for any number >= 27 (JSON or not) it skips the write
  and the save request after it (to 0x1407F649F), and the plugin records it in `Yu-Gi-Oh-Ex\tutorials\done.json`
  (`{ "saves": { "savegame-ex.dat": [27] } }`, keyed by `Core_GetSaveFile`, so each save slot has its own).

Not tested in game yet. First test: a copy of tutorial 1 saved as 27 with a title, then Help > Tutorial (it should be last), play it to
the end, check the mark and `done.json`, and that savegame-ex.dat's bytes at the tutorial bits didn't change.

## Editor

* **Visual**: the steps (op, a plain reading of the parameters with card names, text) with Add step (any op) / Duplicate / Remove / Up /
  Down / Renumber ids. The selected step's fields are named for its op, with card pickers where a parameter is a card, plus its text
  (Insert card name adds `$id`).
* **Playback**: the tutorial frame by frame at 60 fps (`TutorialPlayback`). Play/pause, one frame back or on, previous/next step, and a
  scrub bar. A message stays up for "Message frames" and a WaitForAction for "Action frames", standing in for the player. The preview
  (`TutorialPreview`) shows the field with the cursor and pointer, the phase bar, the hint line, the message box (lower or upper), the card
  info panel, allowed cards and deck stacking. The layout is schematic, not the game's exact screen.
* **Script**: `id: Op p1 p2 p3 p4 | text`, one step a line (`\n` new line, `#` comment, ops by name or `0x..`).
* **New tutorial...**: a number, a board setup, blank or a copy of the open one; it says where the game would use that number.
  **Copy to languages...** writes the tutorial for the other five languages too.
