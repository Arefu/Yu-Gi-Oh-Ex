#pragma once
#include <string>

namespace Save
{
    // The folder YuGiOh.exe is in, with a trailing backslash. Paths are built from this instead of the
    // working directory, so they are right however the game was started.
    const std::string& GameFolder();

    // Reads [Yu-Gi-Oh-Core] from Config.ini (GameSaveName, SeedFromGameSave; [Yu-Gi-Oh-MoreCards] is still read for them).
    // The game's save file I/O is always
    // redirected to savegame-ex.dat in the game folder (GameSaveName can rename it), in the exact
    // same format the game writes to Steam Cloud. If it doesn't exist the first run copies the
    // Steam save into it, or lets the game start a new profile when there is no Steam save.
    bool Install();
    // False when the redirect could not be put in: the game must not run then, it would write the Steam save.
}
