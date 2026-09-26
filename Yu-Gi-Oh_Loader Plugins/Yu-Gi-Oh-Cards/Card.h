#pragma once
#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

#include "Limit.h"

// Custom cards live between the last vanilla Konami id (14968) and the end of the
// table the game saves card ownership in (0x4E20 entries, indexed by Konami id).
constexpr int kFirstExtraCardId = 0x3A79;
constexpr int kLastExtraCardId = 0x4E1F;

constexpr uintptr_t kInternalCardPropsAddress = 0x1427D0C30;
constexpr size_t kVanillaCardPropCount = 10166;

constexpr uintptr_t kFullCardPropsAddress = 0x142927600;
constexpr size_t kFullCardPropsStride = 0xA0;

constexpr uintptr_t kFrameTableAddress = 0x140BF7820;
constexpr uintptr_t kSubKindTableAddress = 0x140BF7824;

// Shown instead of the card art when a custom card's image can't be read.
constexpr uint16_t kPlaceholderImageId = 4007;

constexpr const char* kExtraCardsDirectory = "Yu-Gi-Oh-Ex/";

namespace Card
{
    enum Attribute
    {
        SPECIAL = 0x0, LIGHT = 0x1, DARK = 0x2, WATER = 0x3, FIRE = 0x4,
        EARTH = 0x5, WIND = 0x6, DIVINE = 0x7, SPELL = 0x8, TRAP = 0x9,
    };

    enum Type
    {
        Unknown = 0x0, Dragon = 0x1, Zombie = 0x2, Fiend = 0x3, Pyro = 0x4,
        SeaSerpent = 0x5, Rock = 0x6, Machine = 0x7, Fish = 0x8, Dinosaur = 0x9,
        Insect = 0xA, Beast = 0xB, BeastWarrior = 0xC, Plant = 0xD, Aqua = 0xE,
        Warrior = 0xF, WingedBeast = 0x10, Fairy = 0x11, Spellcaster = 0x12,
        Thunder = 0x13, Reptile = 0x14, Psychic = 0x15, Wyrm = 0x16,
        Cyberse = 0x17, DivineBeast = 0x18, CreatorGod = 0x19,
        Spell = 0x1E, Trap = 0x1F,
    };

    enum Status { Forbidden = 0x0, Limited = 0x1, SemiLimited = 0x2, Unlimited = 0x3 };

    enum Icon { I_Normal = 0x0, I_Counter = 0x1, I_Field = 0x2, I_Equip = 0x3, I_Continuous = 0x4, I_QuickPlay = 0x5, I_Ritual = 0x6 };

    enum Kind { K_Normal = 0x0, K_Effect = 0x1, K_Spell = 0xD, K_Trap = 0xE };

    enum StarType { ST_None = 0x0, ST_Level = 0x1, ST_Rank = 0x2, ST_LinkRating = 0x3 };

    enum SubKind { SK_None = 0x0, SK_Normal = 0x2, SK_Effect = 0x3 };

    // Mirrors the game's 0x30 byte card props (KONAMI_ID_CARD_PROPS / INTERNAL_ID_CARD_PROPS).
    struct IN_MEMORY_CARD_PROP
    {
        int ID1;
        int Attack10;
        int ArrowsOrDefense10;
        int KindValue;
        Type Type;
        Attribute Attribute;
        int LevelOrLinkRatingOrRank;
        int Icon;
        Status Limitation;
        int PendulumScale;
        int StarTypeValue;
        short ID2;
        short ID3;
    };
    static_assert(sizeof(IN_MEMORY_CARD_PROP) == 0x30, "IN_MEMORY_CARD_PROP drifted from the game's card props");

    struct ExtraCard
    {
        uint16_t ID;
        std::wstring Name;
        std::wstring Description;
        std::string ImagePath;
        std::vector<unsigned char> ImageBytes; // read on first request
        bool ImageTried = false;
        int Copies = 3;                        // copies granted to the profile (0..3)
        IN_MEMORY_CARD_PROP Props{};
    };

    // Filled by LoadCardsFromJson() before Install(). The game table keeps pointers into
    // each card's Name/Description, so it must not change afterwards.
    extern std::vector<ExtraCard> ExtraCards;

    // Vanilla card props followed by one entry per extra card (the entry index is the
    // card's internal id).
    extern std::vector<IN_MEMORY_CARD_PROP> CardProps;

    // Konami id -> internal id of every extra card.
    extern std::unordered_map<uint16_t, int64_t> ExtraLoadIDs;

    // One { u32 refcount; i32 cacheIndex } image slot per internal id. Fixed size: the
    // game is handed pointers into it.
    inline uint64_t ImageSlotTable[kWidenedIdLimit]{};

    // Reads cards.json; bad entries are logged and skipped. Returns the number loaded.
    size_t LoadCardsFromJson(const std::string& path);

    // A card the player owns at least `Copies` of, whatever the save says.
    struct Unlock
    {
        int Id;
        int Copies;
    };

    // Read from unlocks.json, next to cards.json. Applies to game cards and extra cards alike.
    extern std::vector<Unlock> Unlocks;

    // Reads unlocks.json ({"cards": [{"id": 4039, "copies": 3}]}); bad entries are logged and skipped.
    size_t LoadUnlocksFromJson(const std::string& path);

    // When unlocks.json is present the game's own starting cards are not granted (set "replaceDefaults" to
    // false in the file to keep them and only add to them).
    extern bool ReplaceDefaultUnlocks;

    // Cards added to one of the shop packs.
    struct PackAddition
    {
        std::string Pack;                // the pack's file name, e.g. "1_1"
        bool Replace = false;            // true = the lists below are the pack's whole contents, not additions
        std::vector<uint16_t> Common;
        std::vector<uint16_t> Rare;
    };

    // Read from packs.json, next to cards.json.
    extern std::vector<PackAddition> PackAdditions;

    // Reads packs.json ({"replaceDefaults": false, "packs": [{"pack": "1_1", "common": [15000], "rare": [], "replace": false}]}).
    // "replaceDefaults" (default false) makes every listed pack use its lists as the whole contents;
    // a pack's own "replace" overrides it. A list that would end up empty keeps the game's cards instead.
    size_t LoadPacksFromJson(const std::string& path);

    // Registers the extra cards and, the first time it runs, applies the patches and hooks.
    // Runs after every card table setup, since the game rebuilds its tables on a language change.
    void Install();
}
