#pragma once
#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>

// Save slots: separate profiles, each its own save file. Yu-Gi-Oh-Ex\saves.json holds them and what the save-select screen shows:
//
//   { "lastSlot": 1, "nextFile": 4, "slots": [ { "name": "Duelist 1", "avatar": "yugimuto", "file": "savegame-ex.dat" }, ... ] }
//
// The number of entries is the number of slots (1 to MaxSlots, 3 when there is no file). "file" is the slot's save file next to the
// GameSaveName one; an entry without it gets the old name for its position (slot 1 the GameSaveName file, slot N "-N" before the
// extension), written back so deleting a slot never renames another slot's file. New slots take "-<nextFile>", a number never reused.
// "avatar" is a portrait in pdui/chars (the sprite name without "_neutral") or a character id (its portrait).
// [Yu-Gi-Oh-Core] SaveSlot=N in Config.ini always loads slot N and skips the screen.
namespace SaveSlots
{
    constexpr int MaxSlots = 99;

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
    bool ShouldAsk();                     // show the save-select screen (no SaveSlot in Config.ini)

    std::filesystem::path PathOf(int slot);
    std::wstring NameOf(int slot);
    std::string AvatarSpriteOf(int slot); // "<key>_neutral"
    Summary Read(int slot);

    // Makes slot the one the game reads and writes, and remembers it as lastSlot in saves.json.
    void Select(int slot);

    // A new empty slot at the end; returns its number (0 when there are MaxSlots already).
    int Add();

    // Erases the slot's save file (and an old .series.bin) and removes the slot. The last slot left is only emptied. False when the file
    // could not be erased (nothing is changed then).
    bool Delete(int slot);

    // The slot's portrait: a pdui/chars sprite key (without "_neutral"), written to saves.json.
    void SetAvatar(int slot, const std::string& key);
    std::string AvatarKeyOf(int slot);
    // Every character's portrait key from the game's character table, in table order without repeats (for the picture picker).
    std::vector<std::string> AvatarKeys();
}
