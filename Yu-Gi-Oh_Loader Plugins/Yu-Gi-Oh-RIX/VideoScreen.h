#pragma once

// A "Plugins per page" row on the game's Video Settings screen (Help & Options > Video Settings, ScreenHelpVideo, screen 14): a selector with the
// values 3, 4 and 5, changed with left / right like the game's own selector rows. It is kept as PluginsPerPage in [Yu-Gi-Oh-RIX] of Config.ini,
// which the plugin list reads.
//
// The screen is hard-wired to four rows (Resolution, Display Mode, Apply, Back), so this hooks it (details in SettingsScreen notes in
// docs/MenuSystem.md):
//  - the constructor's row count is patched from 4 to 5; the new row is index 4 in the game's logic but is DRAWN between Display Mode and Apply,
//    with Apply and Back moved down, and up / down follow the drawn order (the selection function is hooked to remap it);
//  - OnEnter is followed by creating the new row's widgets from the same templates the game's rows use;
//  - the per-frame visuals are hooked for the new row (arrows, value, highlight, the moved buttons);
//  - the description setter is hooked because the game reads a four row table (row 5 would read unrelated data);
//  - Update is hooked to read left / right while the new row is selected.
namespace VideoScreen
{
    void Install();
}
