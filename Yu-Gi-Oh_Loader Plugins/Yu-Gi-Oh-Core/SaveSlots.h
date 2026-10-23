#pragma once
#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>

// Save slots: separate profiles, each its own save file. Yu-Gi-Oh-Ex\saves.json holds them and what the save-select screen shows:
//
//   { "lastSlot": 1, "slots": [ { "name": "Duelist 1", "avatar": "yugimuto", "file": "savegame-ex.dat" }, ... ] }
//
// The number of entries is the number of slots (1 to MaxSlots, 3 when there is no file). "file" is the slot's save file next to the
// GameSaveName one; an entry without it gets the old name for its position (slot 1 the GameSaveName file, slot N "-N" before the
// extension), written back so deleting a slot never renames another slot's file. A new slot takes the lowest free "-N" number (Add).
// "avatar" is a portrait in pdui/chars (the sprite name without "_neutral") or a character id (its portrait).
// [Yu-Gi-Oh-Core] SaveSlot=N in Config.ini always loads slot N and skips the screen (unless slot N can't be played, see CanPlay).
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

    // True when the run is "modded": any active pack in <game>\Mods, or any loaded plugin that needs its own save (a
    // "newSave" tag). The save-select screen uses it to warn before copying the vanilla Steam save into a modded run.
    bool IsModded();

    std::filesystem::path PathOf(int slot);
    std::wstring NameOf(int slot);
    std::string AvatarSpriteOf(int slot); // "<key>_neutral"
    Summary Read(int slot);

    // Makes slot the one the game reads and writes, and remembers it as lastSlot in saves.json. newGame = a new profile starts in it: the
    // slot takes the save tags of the plugins loaded now.
    void Select(int slot, bool newGame);

    // Plugins whose manifest has "newSave" need a save made with them. A slot entry keeps the tags it was made with
    // ("tags": [ { "tag": "forbidden-memories", "title": "Forbidden Memories" } ]); a slot with a save file can only be played when its
    // tags are exactly the loaded plugins' tags (no tags = a plain save, fine while no such plugin loads). An empty slot always can.
    bool CanPlay(int slot);
    // Why it can't, one short line per reason ("Needs a new save: Forbidden Memories", "Made with Forbidden Memories (off)").
    std::vector<std::wstring> WhyNot(int slot);

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
