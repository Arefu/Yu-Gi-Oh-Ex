#pragma once
#include <cstdint>
#include <filesystem>
#include <string>

// Save slots: up to five separate profiles, each its own save file. Slot 1 is the GameSaveName file (savegame-ex.dat), slot N the
// same name with "-N" before the extension (savegame-ex-2.dat). Yu-Gi-Oh-Ex\saves.json holds what the save-select screen shows:
//
//   { "lastSlot": 1, "slots": [ { "name": "Duelist 1", "avatar": "yugimuto" }, ... ] }
//
// The number of entries is the number of slots (1 to 5, 3 when there is no file). "avatar" is a portrait in pdui/chars (the sprite name
// without "_neutral") or a character id (its portrait). [Yu-Gi-Oh-Core] SaveSlot=N in Config.ini always loads slot N and skips the screen.
namespace SaveSlots
{
    constexpr int MaxSlots = 5;

    // What the screen shows for one slot, read from its file.
    struct Summary
    {
        bool Exists = false;      // the file is there
        bool Valid = false;       // ... and is a 44008 byte save with the right magic
        uint64_t Wallet = 0;      // DP
        uint64_t Duels = 0;       // campaign + challenge + multiplayer + battle pack
        uint64_t Wins = 0;
        int CardsOwned = 0;       // distinct cards with at least one copy
        std::wstring LastPlayed;  // file date, "" when there is no file
    };

    // Reads saves.json and Config.ini and points the game at the slot to start with (lastSlot, or SaveSlot). Call after Save::Install.
    void Install();

    int Count();
    int LastSlot();                       // 1-based
    bool ShouldAsk();                     // show the save-select screen (more than one slot and no SaveSlot in Config.ini)

    std::filesystem::path PathOf(int slot);
    std::wstring NameOf(int slot);
    std::string AvatarSpriteOf(int slot); // "<key>_neutral"
    Summary Read(int slot);

    // Makes slot the one the game reads and writes, and remembers it as lastSlot in saves.json.
    void Select(int slot);
}
