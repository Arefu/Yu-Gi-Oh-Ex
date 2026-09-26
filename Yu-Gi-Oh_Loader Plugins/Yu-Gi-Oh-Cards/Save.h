#pragma once

namespace Save
{
    // Reads [Yu-Gi-Oh-MoreCards] from Config.ini. The game's save file I/O is always
    // redirected to savegame-ex.dat in the game folder (GameSaveName can rename it), in the exact
    // same format the game writes to Steam Cloud. If it doesn't exist the first run copies the
    // Steam save into it, or lets the game start a new profile when there is no Steam save.
    void Install();

    // The saved card ownership table of a profile: one byte per Konami id (0x4E20 entries),
    // bits 0-2 = owned copies, bit 3 = "new" flag. nullptr if the profile has no save.
    unsigned char* GetCardUnlockTable(unsigned int profile);
}
