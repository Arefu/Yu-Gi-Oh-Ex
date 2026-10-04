#pragma once
#include <filesystem>
#include <string>

namespace Save
{
    // The folder YuGiOh.exe is in, with a trailing backslash. Paths are built from this instead of the
    // working directory, so they are right however the game was started.
    const std::string& GameFolder();

    // Reads [Yu-Gi-Oh-Core] from Config.ini (GameSaveName, SeedFromGameSave).
    // The game's save file I/O is always
    // redirected to savegame-ex.dat in the game folder (GameSaveName can rename it), in the exact
    // same format the game writes to Steam Cloud. If it doesn't exist the first run copies the
    // Steam save into it, or lets the game start a new profile when there is no Steam save.
    bool Install();
    // False when the redirect could not be put in: the game must not run then, it would write the Steam save.

    // The GameSaveName file (save slot 1; the only one ever seeded from the Steam save).
    const std::filesystem::path& PrimarySavePath();

    // The file the game reads and writes now, and switching it (save slots). Thread safe; the game's next read or write uses
    // the new file, so only switch it while no save is being read or written (before ScreenSignIn loads the save).
    std::filesystem::path CurrentSavePath();
    void SetSavePath(const std::filesystem::path& path);

    // The music and sound volume are shared by every slot (the rest of the save is not): every save read gets the volume of the last
    // save read or written. ApplySharedVolume puts it into a blob directly (a new profile made in memory).
    void ApplySharedVolume(unsigned char* blob);
}
