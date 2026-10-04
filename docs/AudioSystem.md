# The audio system (Wwise) and custom music

How the game plays music and sound effects, and where a plugin hooks in to play its own mp3/wav files instead. Everything here was traced
in `YuGiOh.exe.i64` (2026-10-02); the functions, globals, the `SoundSlot` enum and `SoundVolumeSetting` are named, typed and commented there
(search `YGO::SOUND::`).

## The engine

The game uses Audiokinetic **Wwise**. `YGO::SOUND::SoundManager_Init` (0x14087A850) starts it on `GeneratedSoundBanks\Windows\`, and
`YGO::SOUND::CreateSoundSlots` (0x14086B2A0) loads `bank0.bnk` and builds **73 sound objects** into `g_SoundSlots[73]` (0x143329500).
Each one is made by `Sound_CreateByName(name)`, which hashes the four events `Play_<name>`, `Stop_<name>`, `Pause_<name>` and
`Resume_<name>` (they must exist in bank0). The music `.wem` files are streamed and the SFX are inside the bank. We don't parse the bank:
custom audio doesn't go through Wwise at all (see below).

| Slot | Name | Played by |
|---|---|---|
| 0 / 1 | `duel_1_r` / `duel_1_y` | LP pinch pair A (`Duel_UpdateMusicForLP`) |
| 2 / 3 | `duel_2_r` / `duel_2_y` | LP pinch pair B |
| 4 | `duel_normal_2_t` | duel base music (random pick) |
| 5 | `mus_arc_v` | duel base music in arena 5 (ARC-V) |
| 6 / 7 | `mus_duel_01` / `mus_duel_03` | duel base music (random pick) |
| 8 | `mus_title` | title and menus (`PlayMenuMusic`, `RIX__Pause__QuitDuel` and 4 more) |
| 9 | `mus_tutorial` | tutorial duels |
| 10 | `mus_vrains` | duel base music in arena 17 (VRAINS) |
| 11 | `mus_vs01` | the VS intro before a duel (`sub_1407B57A0`) |
| 12 | `system_deck` | SFX |
| 13 | `system_result` | **played as music**: duel end (`YGO__DUEL__OnDuelEnd`, `Draw_DuelAnimationFromId`), results (`sub_14083F7F0`), after a special movie |
| 14-72 | `arcana_1` ... `UNWRAP` | SFX (`PlayUISound`) |

bank0 also has events no slot uses: `mus_duel_04`, `mus_duel_04_48k`, `mus_results`, and `Pause_`/`Resume_`/`Stop_mus_title`.

## Music: one entry point

**All music starts in `YGO::SOUND::PlayMusicSlot(slot)` (0x14086C300).** Its callers are:
- `Duel_StartMusic`: tutorial 9; otherwise random 6/7/4, overridden by arena 5 -> 5 and arena 17 -> 10; LP pair random (1,0)/(3,2) into
  `g_DuelLpMusicSlots`
- `Duel_UpdateMusicForLP`
- the menu, VS and result callers above
- `ProcessMusicQueue` (0x14086CA00): queued requests, type 2 = play a slot

What `PlayMusicSlot` does:
1. Returns 0 when `g_bSoundSlotsReady` (0x143329758) is off or `slot == g_CurrentMusicSlot` (0x14332975C).
2. `Sound_Stop`s the current slot.
3. Sets the slot's bus volume to `g_MusicVolume`.
4. `Sound_Play`s the new slot and stores it in `g_CurrentMusicSlot`.

Nothing else posts music events (the only other `Sound_Play` callers are an animation's sound and `PlayUISound`).

## Sound effects

Every SFX goes through `YGO::UI::PlayUISound(slot)` (0x14086C280, hundreds of callers). It stops the slot, sets the bus volume to
`g_SfxVolume`, plays the slot, and remembers it in `g_LastUISoundSlot`.

## Volume

`SoundVolumeSetting[2]` sits right after the slots: music `{g_MusicVolume 0x143329748, g_MusicVolumeLevel}` and sfx
`{g_SfxVolume 0x143329750, g_SfxVolumeLevel}`. Each volume is a float 0..1 equal to level / 10.

`YGO::SOUND::SetVolumeLevel(channel 0 music / 1 sfx, level 0..10)` (0x14086BE60) changes one setting and re-applies it to the playing
music. It is called by:
- the Settings sound page (`YGO::UI::SoundSettings_HandleInput`, save blob +20 music / +24 sfx)
- `ApplySavedAudioSettings` at sign-in

The defaults are 5 / 0.5 (`ApplyDefaultAudioSettings`, `ResetAudioSettings`).

## Window focus

`YGO::App::OnWindowVisibilityChanged` (0x1408CB610) suspends the whole Wwise engine when the window is hidden
(`SoundManager_Suspend`, 0x14087A3E0) and wakes it up when the window is shown (`SoundManager_Resume`, 0x14087A270). Shutdown is
`ShutdownSoundSlots` (0x14086C0C0): it stops everything, sets `g_CurrentMusicSlot = -1` and frees the slots.

## Custom music plan (Yu-Gi-Oh-Music)

Wwise can't play a loose mp3, so the plugin plays files itself (e.g. miniaudio: one header, mp3/wav/flac/ogg, its own output) and keeps
Wwise silent for the slots it overrides. All hooks go through MS Detours:

| Hook | What the plugin does |
|---|---|
| `PlayMusicSlot` | Pick an override for the slot in the current context (arena / tutorial / story duel / screen). **Override:** keep the early-out (same slot = no-op), `Sound_Stop` the current Wwise slot, set `g_CurrentMusicSlot = slot` (so the game's own bookkeeping, LP checks and same-slot test keep working), start the file looped at `g_MusicVolume`, return 1. **No override:** stop our file, then call the original. |
| `Duel_StartMusic` | Records the context (arena id, tutorial, story duel). An override can also set `g_DuelBaseMusicSlot` / `g_DuelLpMusicSlots` to re-route which vanilla slot plays. |
| `SetVolumeLevel` (or poll `g_MusicVolume` each frame) | Follows the music volume. |
| `SoundManager_Suspend` / `SoundManager_Resume` | Pause and resume our player with the window. |
| `ShutdownSoundSlots` | Stop and free. |
| `PlayUISound` (optional, SFX) | A one-shot file at `g_SfxVolume` instead of the slot. |

Config: JSON in `Yu-Gi-Oh-Ex` (e.g. `music.json` + files in `Yu-Gi-Oh-Ex\music\`). Keys are slot names, plus per-arena and per-duel
entries, and loop points later. Any Config.ini keys go in the WolfX ConfigCatalog, and a WolfX page edits the JSON.
