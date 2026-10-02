#pragma once
#include <cstdint>
#include <optional>
#include <string>
#include <unordered_map>
#include <vector>

#include "Limit.h"

// Custom cards live between kFirstExtraCardId and the end of the table the game saves card ownership in (0x4E20 entries, indexed by Konami id).
// The last vanilla Konami id is 14968, but the game ships effect rows and id lists for 14969-15234 (cards it has no data for): a custom card
// numbered there inherits a ghost card's effect, hand-effect flag and trigger entries. Numbering starts at 15300, past all of them.
constexpr int kFirstExtraCardId = 0x3BC4;   // 15300
constexpr int kLastExtraCardId = 0x4E1F;

constexpr uintptr_t kInternalCardPropsAddress = 0x1427D0C30;
constexpr size_t kVanillaCardPropCount = 10166;

constexpr uintptr_t kFullCardPropsAddress = 0x142927600;
constexpr size_t kFullCardPropsStride = 0xA0;

constexpr uintptr_t kFrameTableAddress = 0x140BF7820;
constexpr uintptr_t kSubKindTableAddress = 0x140BF7824;

// Shown instead of the card art when a custom card's image can't be read.
constexpr uint16_t kPlaceholderImageId = 4007;

// The folder next to YuGiOh.exe that holds cards.json, unlocks.json, packs.json and the card art (ends with a slash).
const std::string& ExtraCardsDirectory();

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
        Cyberse = 0x17, DivineBeast = 0x18, CreatorGod = 0x19, Illusion = 0x1A,
        Spell = 0x1E, Trap = 0x1F,
    };

    enum Status { Forbidden = 0x0, Limited = 0x1, SemiLimited = 0x2, Unlimited = 0x3 };

    enum Icon { I_Normal = 0x0, I_Counter = 0x1, I_Field = 0x2, I_Equip = 0x3, I_Continuous = 0x4, I_QuickPlay = 0x5, I_Ritual = 0x6 };

    enum Kind { K_Normal = 0x0, K_Effect = 0x1, K_Fusion = 0x2, K_FusionEffect = 0x3, K_Ritual = 0x4, K_Spell = 0xD, K_Trap = 0xE };

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
        // Named-archetype codes this card belongs to ("archetypes": [217, ...] in cards.json). The codes are
        // the game's own: the index into bin/CARD_Named.bin, e.g. 217 Nekroz, 357 Salamangreat (see
        // Is_CardInNamedArchetype in IDA and the ygo-effects-moonshot-plan memory). Codes >= 419 are new
        // archetypes that exist only for custom cards.
        std::vector<int> Archetypes;
        // The vanilla card whose effect this card borrows ("effectClone": { "from": N } in cards.json, applied by Yu-Gi-Oh-Effects).
        // In a duel the card plays under that id when it is free (see BorrowSourceId), so every id-keyed rule of the engine treats it as the source.
        uint16_t CloneFrom = 0;
        IN_MEMORY_CARD_PROP Props{};
    };

    // A cards.json entry whose id is one the game already has: it changes that card instead of adding one.
    // Only the fields the entry lists are changed.
    struct CardOverride
    {
        int ID = 0;
        bool HasName = false, HasDescription = false;
        std::wstring Name, Description;   // the game table keeps pointers into these, so they must not change after loading
        std::optional<int> Attack, Defense, Level, Attribute, Type, Kind, Icon;
    };
    extern std::vector<CardOverride> Overrides;

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

    // ---------------------------------------------------------------------
    // Duel-session id remapping (implemented 2026-09-27; see Card.cpp for the borrow pool
    // itself, hooked onto Duel_LoadDeck/DuelSetup_ClearState/FinishAndUpdateSave).
    //
    // The duel engine keeps only 14 bits of a card id in the packed dword it uses everywhere
    // a card is referenced in a zone. Hand it an id above 16383 directly and it silently
    // aliases to whatever real card sits at (id & 0x3FFF) - not "unknown card", a WRONG card,
    // for the whole duel (see Duel_LoadDeck in the IDA notes). Patching the engine itself to
    // widen that field was investigated and rejected: a single search for the literal mask
    // ("3FFFh") hit ~200 times across ~60 different functions in just the first 27KB of the
    // duel engine's code, each with its own uniquely-compiled surrounding bit arithmetic
    // (owner bit, slot number, shift amounts). There is no single place to patch; it would be
    // hundreds to thousands of hand-verified binary edits with no source to check against.
    //
    // The chosen fix instead: a custom card above 16383 borrows a low id (<=16383) from a
    // reserved scratch pool for the lifetime of one duel, and that low id's FULL_CARD_PROPS
    // entry is overwritten with the real card's data (name/art/stats/frame) via the same
    // WriteGameTableEntry() already used for every other custom card. The engine is then
    // handed a completely ordinary, valid low id, so all ~200+ existing 14-bit sites work
    // unmodified - the deception happens once, at the boundary, not to the engine's logic.
    //
    // HARD RULE, applies to every plugin, not just this one: nothing that touches a card's
    // Konami id while a duel is being set up or is in progress may treat that id as the
    // card's true identity. Every such place must resolve through ResolveDuelSessionId()
    // first. As of this writing that means DuelTest.cpp (Yu-Gi-Oh-Funky) and Fusion.cpp
    // (Yu-Gi-Oh-Effects) both need updating once this is implemented - see the comments left
    // at their current id-range checks. Re-check for new callers whenever a plugin starts
    // touching duel card ids.
    //
    // See the memory note "ygo-duel-id-remap-plan" for the full design and status.

    // Given a Konami id as it would appear in a deck, a fusion material list, or anywhere
    // else a card is referenced going into or during a duel, returns the id the duel engine
    // (or any duel-scoped lookup) should actually be handed: unchanged for an id the engine
    // can already hold (<=16383), or a borrowed scratch-pool id made to look exactly like
    // this card, for anything above that. Valid only for the lifetime of one duel; the
    // mapping is rebuilt fresh each duel and is never persisted. Call this for every id
    // still to be resolved even when it is already <=16383 - it also records the id as "in
    // use this duel" so a later borrow for a different card never collides with it.
    uint16_t ResolveDuelSessionId(uint16_t id);

    // Pure lookup, no side effects: if `id` currently has an active borrow this duel, returns the borrowed
    // id; otherwise returns `id` unchanged (including when there is no duel in progress at all - safe to call
    // anytime). Unlike ResolveDuelSessionId this never creates a new borrow, so it is safe for another plugin
    // to poll (e.g. to find where a card it added to a deck actually ended up in the engine's own arrays,
    // which hold whatever ResolveDuelSessionId returned, not the card's real id). Exported for that reason -
    // see Card_GetActiveDuelSessionId in Card.cpp and Yu-Gi-Oh-Funky's DuelTest.cpp for the caller.
    uint16_t GetActiveDuelSessionId(uint16_t id);

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


    // Registers the extra cards and, the first time it runs, applies the patches and hooks.
    // Runs after every card table setup, since the game rebuilds its tables on a language change.
    void Install();
}
