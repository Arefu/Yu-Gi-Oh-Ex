# Yu-Gi-Oh-Music

The plugin plays music files itself (miniaudio) in place of the game's Wwise music. How the game's audio works, and why the plugin
hooks where it does, is in [AudioSystem.md](AudioSystem.md).

Roadmap: **1. slot overrides (built)** -> **2. per-arena / per-duel music (built)** -> 3. duel events (summons etc.) -> **4. voice
lines (built for the events known so far)** -> **5. WolfX pages (built: Sound > Music, Sound > Voice Over)**. Nothing is tested in game
yet. Voice lines on summons (by card), cards in hand and the win reason are built (2026-10-04); music on summons and card
activations are still to do (step 3).

## Step 1: slot overrides

`Yu-Gi-Oh-Ex\music.json` maps the game's music slots to files. A slot without an entry plays the game's own music, and so does a slot
whose file is missing or won't open (the log says why). Files go in `Yu-Gi-Oh-Ex\music\` unless `folder` or an absolute path says
otherwise. Formats: **mp3, wav, flac** (no ogg).

```json
{
  "folder": "music",
  "volume": 1.0,
  "fadeIn": 0,
  "fadeOut": 0,
  "slots": {
    "mus_title": "title.mp3",
    "mus_duel_01": ["duel_a.mp3", "duel_b.flac"],
    "duel_1_r": { "file": "pinch.wav", "volume": 0.8, "loopStart": 4.25, "fadeIn": 500, "fadeOut": 800 },
    "mus_duel_03": { "files": ["duel_c.mp3", "duel_d.mp3"], "fadeOut": 1000 },
    "13": { "file": "results.mp3", "loop": false }
  }
}
```

- **Slot key:** the slot's name or its number. Music slots: `duel_1_r` 0, `duel_1_y` 1, `duel_2_r` 2, `duel_2_y` 3 (the LP pinch
  pairs), `duel_normal_2_t` 4, `mus_arc_v` 5, `mus_duel_01` 6, `mus_duel_03` 7 (duel base music), `mus_title` 8 (title and menus),
  `mus_tutorial` 9, `mus_vrains` 10, `mus_vs01` 11 (VS intro), `system_result` 13 (duel end / results).
- **Value:** a file, a list of files (one is picked at random each time the slot starts), an object, or an object with `files`
  (its settings apply to each file).
- **Object keys:** `file`, `volume` (on top of the game's music volume), `loop` (default true), `loopStart` / `loopEnd` (seconds; 0 =
  end of file), `fadeIn` / `fadeOut` (ms; `fadeOut` is used when the track is replaced). Missing keys come from the top-level defaults.

The game's music volume setting applies to everything the plugin plays, and the music pauses while the game window is hidden (the
same as Wwise).

## Step 2: per arena, per opponent, per story duel

Besides `slots` (used everywhere), music.json can hold three more scopes. Each one is `{ "<id>": { <slot key>: <value> } }` with the
same slot keys and values as `slots`:

```json
{
  "slots": { "mus_title": "title.mp3" },
  "arenas": {
    "3": { "duel": "arena3_theme.mp3", "pinch": { "file": "arena3_pinch.mp3", "fadeIn": 300 } }
  },
  "opponents": {
    "12": { "duel": ["kaiba_a.mp3", "kaiba_b.mp3"], "mus_vs01": { "slot": "mus_vrains" } }
  },
  "storyDuels": {
    "41": { "mus_duel_01": "finale.flac", "system_result": { "file": "finale_end.mp3", "loop": false } }
  }
}
```

- **Ids:** `arenas` = the arena id (`g_CurrentArenaId`, the same ids as the WolfX arena names), `opponents` = the opponent's
  chardata character id (`g_DuelSideCharacters[1]`), `storyDuels` = the dueldata id of a campaign duel.
- **Which wins:** story duel -> opponent -> arena -> `slots`, per slot. A scope that has nothing for the slot being played falls
  through to the next one, so an arena can set only its pinch music and keep the global duel music.
- **`mus_title` (8)** is only ever taken from `slots`: the menus never pick up the last duel's music.
- **Group keys:** `"duel"` = the base duel tracks the game picks from (4, 5, 6, 7, 10), `"pinch"` = the LP pinch tracks (0-3). A
  slot's own key in the same scope wins over its group.
- **Game track:** a choice `{ "slot": "<name or number>" }` plays one of the game's own Wwise tracks instead of a file, e.g. VRAINS
  music in any arena. It can be mixed with files in a list.

### Knowing which duel is starting

The game sets the arena through three functions just before a duel; the plugin hooks them to know the context. The game's own story
duel id (`g_CurrentStoryDuelId` 0x140C8D1E0) is never reset, so it can't be used.

| Setter | Called for | Plugin context |
|---|---|---|
| `Set_CurrentArenaId` 0x140769540 | free duel, tutorials | not a story duel; opponent known |
| `Set_ArenaIdFromDuel` 0x1407695B0 | campaign duels (gets the story duel id) | story duel = id; opponent known |
| `Set_ArenaIdFromMatchType` 0x140769560 | battle packs, online and other match types | not a story duel; opponent unknown (only arena + `slots` apply) |

### Game-track re-routing

For a `{ "slot": ... }` choice the plugin stops the current music, sets that Wwise slot's volume to `g_MusicVolume`, plays it, and
remembers it (`g_RemapSlot`). `g_CurrentMusicSlot` still holds the slot the game asked for. `SetVolumeLevel` is hooked so a volume
change on the settings page also reaches the re-routed slot, and the next music change (or `ShutdownSoundSlots`) stops it.

## WolfX: Sound > Music

`Tools/Shared/Editors/MusicEditor.cs` edits `Yu-Gi-Oh-Ex\music.json`:

- **Left:** Everywhere, then the arena / opponent / story duel scopes by name (arena names, characters + characters.json, story
  duels + storyduels.json). Add a scope by picking its kind and id.
- **Right:** one row per choice: slot (or group), file or game track, volume, loop, loop start/end, fade in/out. Several rows for
  one slot = picked at random.
- **Toolbar:** Save, Add files... (copies them into the music folder), Add game track, Remove, Play/Stop preview (mp3/wav), Open
  music folder. The top row holds the folder name and the default volume and fades.
- Save checks for missing files and loop ends before loop starts.

## Voice lines (VOX)

`Yu-Gi-Oh-Ex\voices.json` gives characters lines to say during a duel. The game has no voice audio of its own (no voice bus or
events in the Wwise bank), so these are only the plugin's files: mp3, wav or flac, in `Yu-Gi-Oh-Ex\voices\` (the WolfX page copies
them into `voices\<character id>\`).

```json
{
  "folder": "voices",
  "volume": 1.0,
  "duck": 0.5,
  "gap": 3,
  "characters": {
    "12": [
      { "when": "duelStart", "files": ["12/intro1.mp3", "12/intro2.mp3"] },
      { "when": "attack", "who": "opponent", "against": 1, "files": ["12/not_again.mp3"], "chance": 50 },
      { "when": "summon", "cards": [4064], "how": "special", "files": ["12/blue_eyes.mp3"] },
      { "when": "inHand", "who": "opponent", "cards": [4023, 4024, 4025, 4026, 4027], "count": 4, "files": ["12/exodia_panic.mp3"] },
      { "when": "lowLP", "amount": 2000, "files": ["12/pinch.mp3"] },
      { "when": "lose", "reason": "exodia", "files": ["12/obliterate.mp3"] },
      { "when": "win", "files": ["12/win.mp3"], "once": true }
    ]
  }
}
```

- **Characters** are chardata ids (custom ones from characters.json too). The speaker is the character on that duel side:
  `g_DuelSideCharacters[0]` for the local player, `[1]` for the opponent.
- **`when`:** `duelStart`, `turnStart`, `battlePhase`, `summon`, `attack`, `inHand`, `damage`, `lowLP`, `win`, `lose`, `draw`.
- **`who`:** `me` (default) = the character did it / it happened to them; `opponent` = said when the other duelist does it (a
  reaction). Not used by duelStart / win / lose / draw.
- **`amount`:** `damage` = at least this much; `lowLP` = said when LP falls from above `amount` to `amount` or below (reaching 0 is
  the result, not low LP).
- **`cards`** (or `card`, a number or a list; card ids as in the game's tables, e.g. Exodia the Forbidden One = 4027):
  - `summon` / `attack`: only when the summoned / attacking monster is one of these (none = any).
  - `inHand`: the cards to hold. **`count`**: how many different ones of them (default all). Said when the hand starts holding
    them (checked after every engine message), not again while it still does; a line that comes up while another plays waits.
- **`how`** (summon): `any` (default), `normal` (Normal / Tribute Summon), `flip`, `special`.
- **`reason`** (win / lose): only for that win: a DuelWinReason name (`lp`, `deckOut`, `timeLimit`, `surrender`, `rules`, `exodia`,
  `destinyBoard`, `yataLock`, `lastTurn`, `finalCountdown`, `effect`, `vennominaga`, `exodius`, `effect14`, `leo`, `disasterLeo`,
  `jackpot7`, `effect18`, `relaySoul`, `ghostrickAngel`, `phantasmSpiral`, `faWinners`, `flyingElephant`, `exodiaDefender`) or its
  number (docs/StatsAndMatchResults.md). A `lose` line's reason is the winner's.
- **`against`:** only when the other duelist is this character.
- **`files`:** one is picked each time from a shuffled bag: none repeats until all of the line's files have played, and a new round
  never starts with the one that just played.
- **`once`** (default **true**): at most once per duel. **`chance`**: percent (default 100). **`volume`**: on top of the rest.
- **Top level:** `volume` (on top of the game's **sound effect** volume, which voice lines follow), `duck` (the plugin's music level
  while a line plays, 0-1; the game's own Wwise music is not ducked), `gap` (seconds of quiet after a line before the next).
- One line at a time: if a line is playing or the gap hasn't passed, other events are skipped (not queued; inHand waits). Duel
  start and the result (win / lose / draw) always get through and replace the line playing. When both duelists have a line for the
  same event, one of them is picked.
- **The result line is said before the finish animation** (the Exodia video, YOU WIN / YOU LOSE): the hook holds the message queue
  at `DuelResult` until the line ends (30 s at most). Offline duels only; online the line plays as the animation starts.

### Where the events come from

| Hook (Detours) | |
|---|---|
| `YGO::DUEL::Engine_Init` 0x1407BC4C0 | New duel: resets the `once` flags and the field / hand state. `duelStart` is said when the duel's first message runs (not during loading). |
| `Duel__MsgQueue__PumpAndMirror` 0x1400117E0 | The same queue compare as the MP DuelRecorder: when Count drops, the front message was popped and is executing. Before calling the game it peeks at the next message: a `DuelResult` is answered first, and the call returns 1 ("busy", as it does while an animation plays) until the line ends. Held only when `PlayerState+0x37B0` (online) and `+0x388A` (queue stage; 0 = this call pops) are 0 and `Duel__UI__IsAnimBusy(0)` is false. |

`Duel__MsgQueue__Push` is never hooked (LTCG register hazard, docs/DuelIt.md). Messages used (`side` = the side it is about):

| Code | Message | Event |
|---|---|---|
| 0x0A | `Phase_Draw` | `turnStart` (every turn starts with it; the engine sends no TurnStart) |
| 0x0D | `Phase_Battle` | `battlePhase` |
| 0x17 | `Anim0C_Summon` in the IDB, but **seen live at an attack** (Duels.log 2026-10-04: a1 = attacker's zone, a2 = target side \| zone << 8, a3 = 2) | `attack`; the attacker's card is read from its zone. 0x41 `AttackDeclare` never appeared in a duel with an attack. |
| 0x51 | hand -> field (a1 = card, a2: bit 0 = side, bits 1-5 = zone, 0x4000 = monster zone) | remembered: a monster that turns up there waits up to 4 messages for its 0x2B |
| 0x2B | Normal Summon (a1 = card, a2 = zone; bumps the side's summon counter) | marks the card's instance as Normal Summoned |
| 0x24 | `LP_Damage` (a1 = amount, LP not applied yet) | `damage`, `lowLP` |
| 0x05 | `DuelResult` (a1: 1 the local side won, 2 the other, 3 draw; a2 / a3 = the sides' win reasons, the loser's is 0) | `win` / `lose` / `draw` (reason = the larger; 0 -> `g_LastDuelWinReason`) |

**Summons are read from the field, not from messages** (special summons use a dozen undecoded messages): after every message the
7 monster zones of both sides (5 Main + 2 Extra Monster Zones, `Duel::InPlay_Card[7]` from side block +0x4C; the IDB types it as
[5] and starts the S/T zones at +0xF0, but `YGO::DUEL::Count_OccupiedMonsterZones` reads 7) are compared with the last check. A
card that is face-up now (`Pos` not 2/8, `UNK_03 & 0x20` "not counted" clear) and wasn't face-up anywhere before (by card instance,
so moving zones or changing control doesn't count) was summoned: `flip` if the same instance was there face-down, `normal` if 0x2B
named it, else `special`. Every summon is logged ("Voice: side 0 summoned card 15324 (normal)") so this can be checked in
console.log.

**Exodia in the game:** `sub_14003D9B0` (side has all of 0xFB7-0xFBB in hand) is called by the win check `sub_14003DA40` (returns
the side's DuelWinReason), which `sub_14003DF30` runs for both sides and stores in side block +0x48 (`field_BC` in the IDB) and the
winner at +0x3792. No message is sent for it: the next `DuelResult` carries it, and its handler starts the finish animation.

Still to decode: activations (0x2E `ShowCardActivation`, a1 = card id?) for an `activate` event.

The player: voice lines play on their own sound group (`Player::PlayVoice`, one at a time) while music uses another, so the duck
only lowers the music. The watcher thread (every 50 ms) starts a line an event picked, follows `g_SfxVolume` (0x143329750) and
ducks / restores the music.

## WolfX: Sound > Voice Over

`Tools/Shared/Editors/VoiceEditor.cs` edits voices.json:

- **Left:** every character (game + custom), with how many lines each has; find box and "Only characters with lines".
- **Right:** the selected character's lines: **When** ("I summon a monster", "my opponent holds cards in their hand", "I lose"...),
  Amount (damage / LP / how many cards in hand), **Cards** (double-click: the card picker), **Summoned** (any way / Normal / Flip /
  Special), **Win by** (any reason / Exodia / deck-out...), **Against** (anyone or one character), Files (double-click to pick),
  Chance %, Once, Volume. Columns an event doesn't use are greyed out.
- Under the grid the selected row is spelled out: "Kaiba says one of 3 files when my opponent holds 4 of Exodia the Forbidden One,
  Left Arm of the Forbidden One... in their hand (said as it happens, not again while it stays true), at most once per duel."
- **Toolbar:** Save, Add line... (pick files = new line), Add files to line..., Remove, Play (a random file of the line), Stop, Open
  voices folder. The top row holds the folder, volume, music-while-talking % and quiet time.

## How it works

| Hook (Detours) | What the plugin does |
|---|---|
| `PlayMusicSlot` 0x14086C300 | Same early-out as the game (sound not ready / slot already current). Pick a choice (scopes above). If it is a file and it opens: stop the current Wwise slot, play the file, set `g_CurrentMusicSlot = slot` (so the LP checks and same-slot test keep working), return 1. If it is a game track: re-route (above). Otherwise stop the plugin's track and call the original. |
| `ShutdownSoundSlots` 0x14086C0C0 | Close the player, then the original. |
| `SoundManager_Suspend` / `Resume` 0x14087A3E0 / 0x14087A270 | Pause / resume the player's audio device with the window. |
| `SetVolumeLevel` 0x14086BE60 | The original, then the same volume on a re-routed game slot. |
| `Set_CurrentArenaId` / `Set_ArenaIdFromDuel` / `Set_ArenaIdFromMatchType` | Track the duel context (step 2), then the original. |

A watcher thread follows `g_MusicVolume` (written by the settings page, sign-in, defaults and reset, so the plugin follows the value
instead of relying on `SetVolumeLevel`) and frees tracks that finished fading out. The audio device opens on the first override, not in
`DllMain`.

Export: `Music_Reload()` re-reads music.json (the playing track carries on until the game changes music).
