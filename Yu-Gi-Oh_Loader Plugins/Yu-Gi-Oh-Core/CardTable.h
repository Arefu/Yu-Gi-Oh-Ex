#pragma once

// The saved card table (which cards a profile owns), made big enough for every Konami id.
//
// The game keeps it in the save blob: blob+0x5DC8, one byte per Konami id (bits 0-2 copies, bit 3 seen), 20000 bytes, which ends where the
// 44008-byte blob ends. Every reader and writer gets it from YGO::SAVE::Get_CardUnlockTable (0x1407F8130, and its thunk 0x1408705F0), so
// that getter hands out a 65536-byte table Core owns instead:
//   - bytes 0..19999 are the blob's table: copied in when a save is merged into the live blob (ValidateAndMerge / its legacy twin) or a
//     new profile is made (InitNewBlob), and written back into every blob the game saves (a save write filter);
//   - bytes 20000..65535 have no room in the blob: they are kept in the save extension (tag "cards.high", u16 id + u8 byte per owned card).
// The game's own `id < 20000` checks around the table are a card plugin's business (Yu-Gi-Oh-MoreCards widens them); Core only makes the
// table big enough. docs/SaveSlots.md has the save layout.
namespace CardTable
{
    constexpr size_t Size = 0x10000;          // one byte per Konami id 0..65535
    constexpr size_t SavedSize = 20000;       // the part the blob holds

    bool Install();
}
