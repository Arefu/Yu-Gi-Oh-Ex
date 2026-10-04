#pragma once

// Voice lines (VOX): Yu-Gi-Oh-Ex\voices.json gives characters lines to say when something happens in a duel (the duel starts,
// a turn starts, an attack, damage, low LP, win/lose). The game has no voice audio of its own; the plugin plays the files.
// Format and events: docs/MusicPlugin.md "Voice lines".
namespace Voice
{
    // Reads voices.json and hooks the duel engine (Engine_Init, MsgQueue PumpAndMirror).
    void Setup();
    void Reload();
    // From the watcher thread: starts a line that a duel event picked, and ducks the music while one plays.
    void Tick();
    // The voices.json volume (on top of the game's sound effect volume).
    float Volume();
}
