#pragma once
#include <string>

// What the card code needs to know about the game's save. The save file itself (savegame-ex.dat, never the Steam save) is handled by
// Yu-Gi-Oh-Core, which is always loaded.
namespace Save
{
    // The folder YuGiOh.exe is in, with a trailing backslash. Paths are built from this instead of the
    // working directory, so they are right however the game was started.
    const std::string& GameFolder();

    // The saved card ownership table of a profile: one byte per Konami id (0x4E20 entries),
    // bits 0-2 = owned copies, bit 3 = "new" flag. nullptr if the profile has no save.
    unsigned char* GetCardUnlockTable(unsigned int profile);
}
