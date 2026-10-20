#pragma once

// Progress a profile has from the start, from Yu-Gi-Oh-Ex/progress.json:
//
//   { "unlockAllCharacters": true, "modes": { "duelistChallenges": true, "battlePacks": true, "cardShop": true } }
//
// unlockAllCharacters sets every defined character's bit in the save's 240-bit set (player section +0x18), which the collection
// reads, and their own deck's state (+0x38 + 4 * deck), which Free Duel and Duelist Challenges read to list them as unlocked. modes set the main menu's mode bits (player section +0xB94: bit 0 Duelist Challenges,
// item 2; bit 1 Battle Packs, item 7; bit 2 Card Shop, item 9), which RIX::ScreenMainMenu::LayoutPage and ActivateItem test.
// Both are written in the save when the main menu lays out a page (LayoutPage, 0x1408583D0, hooked), so they stay once saved.
namespace Progress
{
    // Reads progress.json and attaches the hook; call inside the plugin's Detours transaction.
    void Attach();
}
