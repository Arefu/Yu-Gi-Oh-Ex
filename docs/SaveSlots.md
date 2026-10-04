# Save slots and the save-select screen (Yu-Gi-Oh-Core)

Up to five separate profiles, picked on a new screen between the title screen and the main menu. Everything lives in
Yu-Gi-Oh-Core (`SaveSlots.cpp`, `SaveScreen.cpp`, `Save.cpp`) because Core is always loaded and already owns the save redirect.

## Files

| File | What |
| --- | --- |
| `savegame-ex.dat` (the `GameSaveName` file) | Slot 1. The only slot that is ever seeded from the Steam save (`SeedFromGameSave`). |
| `savegame-ex-2.dat` ... `savegame-ex-5.dat` | Slots 2 to 5: the same name with `-N` before the extension. A new slot starts as a new profile. |
| `Yu-Gi-Oh-Ex\saves.json` | What the screen shows, and the slot played last. |

```json
{
  "lastSlot": 1,
  "slots": [
    { "name": "Duelist 1", "avatar": "yugimuto" },
    { "name": "Duelist 2", "avatar": "jadenyuki" },
    { "name": "Duelist 3", "avatar": 12 }
  ]
}
```

* The number of entries is the number of slots (1 to 5). Without the file there are 3, and the file is written the first time a slot is picked.
* `avatar` is a portrait from the `pdui/chars` sheet, given as the sprite name without `_neutral` (273 of them: `yugimuto`, `setokaiba`,
  `Playmaker`...), or a character id (that character's portrait, read from the game's character table).
* `[Yu-Gi-Oh-Core] SaveSlot=N` in Config.ini (1 to 5) always loads slot N and skips the screen. `0` (or no line) shows the screen. With one slot
  there is no screen either.

## The flow

1. When Core starts, the game is pointed at the slot to start on: `SaveSlot`, else `lastSlot` (slot 1 if that file is gone).
2. The title screen's first `OnEnter` reads that slot early, as the game always does (`SaveMgr_BeginLoad`; this sets the volume).
3. **Play** on the title asks for `ScreenSignIn` (6) by writing the pending screen of the main record (`RIX::UI::SetPendingScreen`). Core's hook on
   `ScreenTitle::Update` changes the pending id to **100**, the save-select screen.
4. Picking a slot (`SaveSlots::Select`) points the save redirect at its file, writes `lastSlot`, and goes to `ScreenSignIn` with `GotoScreen`.
   SignIn reads the save (the vanilla "second read" after Play), applies its settings and opens the main menu (8). Nothing is reloaded by hand.
5. An **empty** slot asks "Start a new game in this slot?" first. With no file, the game writes out the profile that is in memory
   (`YGO::SAVE::Profile_WriteLiveBlob`), which would be the slot the title screen read, so Core resets it with the game's own
   `GetOrInitProfileBuffer(manager, nullptr)` (`InitNewBlob`) before SignIn runs. A file that is not a save shows "This save file can't be read."
6. Back (cancel) returns to the title screen; Play brings the save-select screen back.

**Volume is shared, nothing else is.** The save's settings block at 0x14 is music volume, then sound effects / ambient volume (0..10 each,
read by `ApplySavedAudioSettings`), then two unknown values. Core remembers those two volumes from the first save read (the title's early read)
and from every save written (moving a slider in Settings saves). Every later read gets them patched in, with the checksum recomputed (`Save.cpp`,
`ShareVolume`), and a new slot gets them right after its profile is reset. The unknown values stay per slot.

Each slot shows its portrait in `characterframe_large`, the name, and from its file: DP (wallet), distinct cards owned, wins / duels
(campaign + challenge + multiplayer + battle pack, `SaveStat` 13/16/17/22 and 0/3/4/9) and the file date.

## A new RIX screen

This is the first screen the mods add rather than borrow. How the game's screens work (all named in `YuGiOh.exe.i64`):

| Piece | Address | Notes |
| --- | --- | --- |
| `RIX::ScreenBase` | 656 bytes | `ScreenBase::Constructor` 0x140821B20 sets vftable `RIX::ScreenBase2` (0x140A716B8, 22 slots). |
| `SetScreenId` | 0x140822E00 | Stores the id at +56 and puts the screen in `g_ScreenMap` (`std::map<int, ScreenBase*>`), which is how `NavigateToScreen` / `GotoScreen` find it. |
| `SetScreenType` | 0x1408228C0 | +40. `ScreenCommonBg` (screen 7) draws the backdrop of the current screen from it: 1/2 flat, 3..8 background styles 1..6. Card shop = 3, main menu / SignIn = 4, title = 1. |
| `Load` (slot 2) | 0x140822470 | Root node (+72), help bar (+264), dialog (+432), then `SetupWidgets` (slot 8). |
| `Activate` (slot 4) | 0x140822390 | Sets +52; `Tick` does nothing until it is set. |
| `Tick` (slot 1) | 0x140821EC0 | Every frame: `OnEnter` (12) when the screen becomes current, `Update` (14) while it is (the dialog takes the input while it is open), `OnLeave` (13) when it stops, draws (17). |
| Main scheduler | instance + 0x200 | `RIX::App::Frame` runs `RIX::Scheduler::RunJobs(instance+0x200, ui)` = every main screen's Tick. Instance = `qword_1429275D8`, UI = instance + 0x1F0. |
| Screen records | ui + 856, index ui + 852 | 48 bytes: +0 current id, +4 previous, +8 pending. Record 0 = the main screens; record 1 is a second channel `App::UpdateScreens` runs (scheduler instance + 0x210). |

The game builds its screens in `RIX::App::Initialize` and adds two scheduler jobs for each (a one-shot `Load`, a repeating `Tick`). Core does the same
by hand once the title screen is up: game `operator new`, `ScreenBase::Constructor`, a copy of the base vftable with slots 0 (destructor: never
frees), 8, 12, 13 and 14 replaced, `SetScreenId(100)`, `SetScreenType(3)`, `Load`, `Activate`, and its `Tick` is called from a detour of
`RunJobs` right after the main scheduler's jobs. Widgets are the plain DFX images / text of `YuGiOh-RIX.h` (`Dfx::AddImage`, `Dfx::AddText`).

Screen ids: **6 is `ScreenSignIn`, 8 is `ScreenMainMenu`** (`YuGiOh-UI.h` had 6 as the main menu; fixed).

## Not done yet

* Not tested in game. The layout (header, frame spacing, text sizes) is a first guess.
* Choosing the avatar / name in game (they are only in `saves.json` for now); a "Change save" button on the main menu (SignIn from 8 reloads the
  current profile, so main menu -> save-select -> SignIn should work), deleting a slot.
