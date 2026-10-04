# Making a new screen

How to add a screen of our own to the game, the way Yu-Gi-Oh-Core's save-select screen is made.
Written 2026-10-02. **Status:** the save-select screen (Core `SaveScreen.cpp`, id 100) is the one in the code. RIX's plugin settings screen
(`SettingsScreen.cpp`, id 101) was made the same way and removed on 2026-10-04 (the user didn't want plugin settings in game), so id 101
is free again. Update this page once a screen is confirmed in game, with what had to change.

## Don't borrow a game screen for a new page

The first try at plugin settings took over the game's Video Settings screen (`ScreenHelpVideo`, 14): it hid its rows and drew ours. It was
rejected because the player sees Video Settings, not a plugin page. Borrowing only fits when the screen's purpose stays the same. Pages.cpp
borrows the Battle Pack screen as a generic menu, and VideoScreen.cpp adds one real video-ish row. A new purpose gets a new screen.

## The recipe (copy SaveScreen.cpp)

All addresses are in `YuGiOh.exe.i64`.

1. **Pick an id** above the game's (its ids end at 43): 100 = save select (Core); 101 was RIX's plugin settings (removed). Check with
   `R::FindScreenObject(id)` first, and log and give up if it's taken.
2. **Make the object:** `GameNew(0x290 + 0x70)` (the game's allocator 0x14090D2B8; ScreenBase is 656 bytes, plus slack), zero it, then
   run `ScreenBase_Constructor` (0x140821B20).
3. **Give it a vftable:** copy the 22 slots of `RIX::ScreenBase2`'s vftable (0x140A716B8), keeping `base[-1]` (the RTTI locator) in
   front. Override what you need:
   - slot 0 destructor (return self: the screen lives forever);
   - slot 8 SetupWidgets (can be empty);
   - slot 12 OnEnter (build);
   - slot 13 OnLeave (take your nodes off);
   - slot 14 Update (input, every frame while current).

   Point the object at `&table[1]`.
4. **Register it:**
   - `SetScreenId` (0x140822E00) puts it in `g_ScreenMap`, so `GotoScreenFrom` / `NavigateToScreen` find it.
   - `SetScreenType` (0x1408228C0) picks the backdrop `ScreenCommonBg` draws (3 = Card Shop, 4 = the option screens).
   - `Load(screen, ui)` (0x140822470) makes the root node (`screen + 72`), the help bar (`screen + 264`) and the dialog (`screen + 432`).
   - `Activate` (0x140822390) sets the flag Tick needs.
5. **Tick it yourself:** the game only ticks the screens it made (in `RIX::App::Initialize`). Detour `RIX::Scheduler::RunJobs`
   (0x140821170). After the original runs, when `scheduler` is the main scheduler (`*(YGOInstance 0x1429275D8) + 0x200`), call
   `ScreenBase_Tick(screen, ui)` (0x140821EC0). Tick calls OnEnter / Update / OnLeave and draws. Create the screen from the same hook
   once the game's screens exist: Core waits for the title screen (5), RIX for the main menu (8). The current screen record is
   `*(ui + 856) + 48 * *(ui + 852)`: +0 current id, +8 pending id. More than one plugin can detour RunJobs; Detours chains them.
6. **Go to it** from another screen object with `R::GotoScreenFrom(fromScreen, id)` (0x1408227A0). That records where it came from
   (`screen + 60`), which is what the game's own Back logic uses. From a main-menu button callback, `fromScreen` is
   `Menu::CallbackSource()`. Leave with `GotoScreenFrom(yourScreen, targetId)`.

## Drawing on it

Use the Dfx helpers in `Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-RIX.h` on `R::ScreenRoot(screen)`:

- `AddImage(root, sheet, sprite, z, x, y)` places any sprite from a `.dfymoo` sheet, top-left anchored.
  - `SetScaleXY(node, -1, 1)` flips one: x then becomes its right edge.
  - `SetAlpha` dims it.
- `AddText(root, wchar_t*, z, x, y, width, pixelSize, align, colour)` places text. The string must outlive the node, so keep it in a member
  `std::wstring`.
- `RemoveImage(root, node)` takes either one off. Rebuild everything when something changes; that's cheap for a page of rows.
- The header is the sprite `pdui/doShared` / `menuheader` at (0, 40), with 44 px text at (80, 72), the same as the save screen.
- Selector row (from `ScreenHelpVideo::OnEnter`, 0x14084FF40):
  - the box is `pdui/doshared` / `optionbox` (Display Mode's value box);
  - the arrows are `arrow_left1`, and the right one is the same sprite flipped. The game flips it with `sub_140759180(node, 2)`; we
    use scale -1.
  - Get sprite sizes with `LayerSpriteSize` on a throwaway `MakeLayerAnimoo` layer (the removed SettingsScreen.cpp logged them when it started).
- The help bar at the bottom:

  ```cpp
  HelpClear(screen + 264);
  HelpAdd(screen + 264, entry);   // the game's entries: kHelpSelect 0x140A7A1A0 and kHelpBack 0x140A7A190
  HelpLayout(screen + 264);
  ```

## Input (in Update)

- `R::Input::GetPressed | GetRepeat` (bits: up 1, down 2, left 4, right 8, confirm 0x1000, cancel 0x2000), plus
  `HelpBarPressed(screen + 264)` for clicks on the help bar, plus `InputCancelPressed(InputState, 0x2000)` for Esc / Backspace.
- Mouse: `MouseActive()`, `MousePosition()` (two ints in the 1920 x 1080 layout space), `TakeMouseClick(InputState, 0x1000)`.
- Sounds: `PlayUISound(16)` for a selector step (what the game's rows use), 39 to confirm, 71 for an error.
- Boxes:
  - `R::ShowYesNo(screen, -1, text, &std::function)` for Yes / No; the function must fit the small buffer, so use a plain function pointer.
  - `R::ShowMessageText` for an OK message.

## Gotchas

- `widget_Text::SetTextById` (0x14075E000) skips a call with the pointer it already shows (`+232` last value, `+240` has-text flag). It
  matters when you reuse a game text widget: clear `+240` first. Dfx `AddText` nodes are rebuilt, so this doesn't come up.
- Coming back to the main menu: Yu-Gi-Oh-RIX's Plugins list keeps the main menu exclusive while the settings screen is up. The main menu's
  frame callback (`PluginMenu` Tick) sees `SettingsShown()` was called and redraws the list. Any screen that returns to a RIX main-menu page
  needs the same handshake.
- The screen's header can't be changed through `RIX::Screen::SetHeaderText` (0x140822F00), because that writes the game's `widget_Header`
  at `screen + 152`, which a ScreenBase made like this doesn't build. Draw your own header as above.

## Files

- `Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Core/SaveScreen.cpp`: the save-select screen (portraits, Yes / No, a reroute of the title's Play).
- `docs/SaveSlots.md`: how the save screen fits the title → sign-in flow.
