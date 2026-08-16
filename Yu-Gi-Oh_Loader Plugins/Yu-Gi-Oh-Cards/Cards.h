#pragma once

#include <Windows.h>
#include <cstdint>
#include <string>
#include <unordered_map>
#include <vector>

#define KONAMI_CARD_ID_LOCATION 0x140D50510
#define INTERNAL_CARD_ID_LOCATION 0x140D55480
#define IN_MEMORY_CARD_PROP_LOCATION 0x1427D0C30

// Address of the live game card table (Name/Description live here as part of
// a larger struct). Ported from the reversed `card` module - offsets are
// Name @ +0x8, Description @ +0x10, stride 0xA0. NOT YET CONFIRMED against
// this specific build's symbols - verify before shipping.
#define GAME_CARD_TABLE 0x142927600ULL
#define GAME_CARD_STRIDE 0xA0
#define GAME_CARD_NAME_OFFSET 0x8
#define GAME_CARD_DESC_OFFSET 0x10

// Custom cards live at 0x5000+ so they never collide with vanilla Konami IDs
// (vanilla max is 0x3A79).
#define EXTRA_CARD_ID_BASE 0x5000

// A vanilla card ID guaranteed to be loaded and to have a valid illustration
// slot. Used as a fallback art source if a custom card's ImagePath is
// missing or fails to read. Pick any always-present starter/staple card ID
// from your own data - 4 is a placeholder, confirm it resolves to something
// sane in your build.
#define EXTRA_CARD_PLACEHOLDER_IMAGE_ID 4

// Vanilla array sizes - kept as named constants instead of scattering the
// magic numbers 10168 / 11072 / 10166 across the codebase.
#define VANILLA_CARD_ID_COUNT 10168
#define VANILLA_INTERNAL_ID_COUNT 11072
#define VANILLA_CARD_PROP_COUNT 10166

// Konami-ID -> InternalID lookups in this exe are done via `id - 3900`.
#define VANILLA_INTERNAL_ID_OFFSET 3900

// Loop-bound patches (ApplyExtraCardVisibilityPatches in dllmain.cpp) raise
// the trunk-card scan up to this value - the backing buffer it writes into
// (below) MUST be sized to match, or the scan overruns whatever smaller
// buffer used to back it.
#define TRUNK_SCAN_LIMIT 0xFFFF

class Cards
{
public:
    enum Attribute
    {
        SPECIAL = 0x0,
        LIGHT = 0x1,
        DARK = 0x2,
        WATER = 0x3,
        FIRE = 0x4,
        EARTH = 0x5,
        WIND = 0x6,
        DIVINE = 0x7,
        SPELL = 0x8,
        TRAP = 0x9,
    };
    enum Type
    {
        Unkown = 0x0,
        Dragon = 0x1,
        Zombie = 0x2,
        Fiend = 0x3,
        Pyro = 0x4,
        SeaSerpent = 0x5,
        Rock = 0x6,
        Machine = 0x7,
        Fish = 0x8,
        Dinosaur = 0x9,
        Insect = 0xA,
        Beast = 0xB,
        BeastWarrior = 0xC,
        Plant = 0xD,
        Aqua = 0xE,
        Warrior = 0xF,
        WingedBeast = 0x10,
        Fairy = 0x11,
        Spellcaster = 0x12,
        Thunder = 0x13,
        Reptile = 0x14,
        Psychic = 0x15,
        Wyrm = 0x16,
        Cyberse = 0x17,
        DivineBeast = 0x18,
        CreatorGod = 0x19,
    };
    enum Status
    {
        Unlimited = 0x0,
        Limited = 0x1,
        SemiLimited = 0x2,
    };

    struct IN_MEMORY_CARD_PROP
    {
        int CARD_ID;
        int ATK;
        int DEF;
        int EffectTypes;   // Kind, in `card` module terms
        Attribute Attribute;
        int Level;
        int Property;      // Icon, in `card` module terms
        Type Type;
        int Field_10;
        int Field_12;
        Status LimitedStatus;
        int Field_13;
    };

    // One JSON-authored card: numeric props + text + art path.
    struct ExtraCard
    {
        unsigned __int16 ID;
        std::wstring Name;
        std::wstring Description;
        std::string ImagePath;   // PNG or JPEG - the game's own decoder
        // dispatches on file magic bytes, so no
        // conversion is needed either way.
        IN_MEMORY_CARD_PROP Props;
    };

    static unsigned long long orig_getInternalCardID;
    static unsigned long long orig_getKonamiCardID;
    static unsigned long long orig_getCardProps;
    static unsigned long long orig_getUnlockedTrunkCards;

    static unsigned char* __fastcall Get_UnlockedTrunkCards();
    static std::vector<unsigned __int16> CardIDs;
    static std::vector<unsigned __int16> InternalIDs;
    static std::vector<IN_MEMORY_CARD_PROP> CardProps;

    // New cards, keyed by their 0x5XXX+ ID.
    static std::unordered_map<unsigned __int16, ExtraCard> ExtraCardsById;
    // Konami ID -> synthetic internal/load ID, for cards outside the vanilla
    // 3900..0x3A79 range that `InternalIDs.at(id - 3900)` can't address.
    static std::unordered_map<unsigned __int16, __int64> ExtraLoadIDs;

    // Backing storage for the widened trunk-card scan. Sized to
    // TRUNK_SCAN_LIMIT to match the loop-bound patches, so the scan (now
    // reaching 0x5XXX+ ids) never writes past the end of this buffer.
    static unsigned char TrunkScanBuffer[TRUNK_SCAN_LIMIT];

    static Cards::IN_MEMORY_CARD_PROP* __fastcall Get_CardPropsByInternalID(__int16 a1);
    static __int64 __fastcall Get_InternalID(__int16 a1);
    static __int64 __fastcall Get_KonamiID(__int16 a1);

    // --- Image table setup (0x1407521E0) ---
    //
    // sub_1407521E0 allocates its OWN fixed-size (0x13F60-byte, ~10220-entry)
    // CardImageData buffer internally and fills it with a hardcoded
    // 10166-bound loop - patching that loop's bound to 0xFFFF (tried
    // earlier, crashed at 0x1407522A0) overruns the fixed allocation. The
    // reference module does NOT touch this function's internals at all: it
    // wraps it with a hook that, before calling the untouched original,
    // repoints a SEPARATE global pointer at 0x142847E84 to its own
    // statically-sized array. Whatever code actually reads card image data
    // through 0x142847E84 downstream sees our widened array instead of the
    // original's small one; the original's own internal buffer becomes
    // dead/unused, which is harmless.
    static unsigned long long orig_setupImageTable; // 0x1407521E0
    static void __fastcall Setup_ImageTable();

    // Backing storage for the widened image table, sized to TRUNK_SCAN_LIMIT
    // to match every other widened-range buffer in this file. The global
    // pointer at 0x142847E84 gets repointed here in Setup_ImageTable(),
    // before the original function runs.
    static uint64_t WidenedImageTable[TRUNK_SCAN_LIMIT];

    // --- Card image slot redirect (0x140753BA0) ---
    //
    // This function does NOT return pixel/illustration bytes - it returns a
    // pointer into a fixed-size array of 8-byte CardImageData bookkeeping
    // entries, indexed by internal id and hard-bound-checked against the
    // vanilla count (10166) in the original code:
    //
    //   ID = Get_InternalIdFromKonamiId(KonamiID);
    //   if (ID >= 10166) return 0;
    //   return a1 + 8 * ID + 192;
    //
    // Custom cards get internal ids >= 10166, so the original always
    // returns null for them - whatever calls this (confirmed: Deck Edit,
    // which crashes on entry) doesn't null-check and crashes immediately.
    //
    // Fix: fully replace the lookup with our own dynamically-sized table,
    // indexed the same way (via Get_InternalID, which already routes
    // custom ids through ExtraLoadIDs) but with no fixed 10166 cap.
    //
    // OPEN QUESTION: whether `a1` is a stable/global object each call or a
    // transient per-Deck-Edit-session buffer. Confirmed-in-code assumption
    // right now is that it's safe to ignore `a1` and always return a
    // pointer into our own static ImageSlotTable - verify via breakpoint
    // (open Deck Edit twice, compare `a1`) before relying on this long-term.
    static unsigned long long orig_getCardImageSlot; // 0x140753BA0
    static __int64 __fastcall Get_CardImageSlot(void* a1, __int16 KonamiID);

    // 8-byte CardImageData-equivalent slots, indexed by internal id. Sized
    // to CardProps.size() at the end of ApplyExtraCards() - MUST be resized
    // before this hook can safely serve any lookups, custom or vanilla.
    static std::vector<uint64_t> ImageSlotTable;

    // --- Illustration loading (0x14086D050) ---
    //
    // This is the actual pixel-data entry point: id-resolution + dispatch
    // function that hands back raw encoded file bytes (JPEG or PNG) via
    // out-params, which the engine's own decoder then consumes. Decoder is
    // confirmed to dispatch on file magic bytes (0x89 -> PNG, 0xFF -> JPEG),
    // so no format conversion is needed - raw PNG bytes round-trip
    // losslessly. Do not confuse this with orig_getCardImageSlot above;
    // they are two separate functions serving two separate purposes.
    static unsigned long long orig_getCardIllustration;
    static bool __fastcall Get_CardIllustration(__int64 a1, unsigned __int16 a2, void** buffer, size_t* size);
    // --- Illustration cache-state check (0x1407529E0) ---
    //
    // Separate, un-hooked code path that reads the same underlying
    // CardImageData table as orig_getCardImageSlot, but at offset +196
    // instead of +192, with its own inline hardcoded 10166 (0x27B6) cap
    // baked directly into the machine code. When InternalIdFromKonamiId
    // >= 0x27B6, the original sets v8 = 4 (a raw literal, not a pointer)
    // and then dereferences it — access violation at address 0x4.
    //
    // We don't reimplement this function's real logic (mutex lock/unlock,
    // illustration cache lookup, async load trigger via sub_140753FF0) —
    // not enough visibility to do that safely. Instead: custom-card ids
    // reuse the function's own already-safe fallback (its a2 == -1 path:
    // *outFlag = 0, return 0). Vanilla ids run the real original,
    // completely untouched.
    static unsigned long long orig_getCardIllustrationCacheState; // 0x1407529E0
    static __int64 __fastcall Get_CardIllustrationCacheState(void* a1, __int16 a2, unsigned char* a3);
    // Raw (still-encoded, PNG/JPEG) file bytes for custom card art, keyed by
    // Konami ID. This is NOT what gets handed to the game directly - the
    // engine frees whatever buffer it's given after consuming it
    // (`free(Block: v285)` in the caller), so Get_CardIllustration always
    // mallocs a fresh copy per call and leaves this cache untouched. Storing
    // the cache lets repeat lookups skip the disk read.
    static std::unordered_map<unsigned __int16, std::vector<unsigned char>> RawImageCache;

    // Reads ExtraCardsById[id].ImagePath off disk as raw bytes (cached after
    // first read) and mallocs a fresh copy into *outBuffer/*outSize, ready
    // for the engine to consume and free(). Returns false on failure -
    // caller (Get_CardIllustration) is responsible for falling back to
    // placeholder art in that case.
    static bool LoadCustomCardIllustration(unsigned __int16 id, void** outBuffer, size_t* outSize);

    // --- New cards pipeline ---

    // Parses extracards.json into ExtraCardsById. Does not touch the vanilla
    // vectors - call before ApplyExtraCards().
    static bool LoadExtraCardsFromJson(const std::string& path);

    // Appends parsed cards onto CardProps (and grows InternalIDs bookkeeping
    // via ExtraLoadIDs). Also resizes ImageSlotTable to match. MUST be
    // called before SetupRedirectionForInternalIDs() /
    // SetupRedirectionForCardIDs() in dllmain, since those bake vector
    // data() pointers into patched machine code.
    static void ApplyExtraCards();

    // Writes Name/Description for every extra card into GAME_CARD_TABLE.
    // Hook this onto _Setup_CardPropTable (0x14081A080) AFTER calling the
    // original function, same as CreateCardProps_ in the `card` module.
    static void WriteExtraCardNames();

private:
    static Attribute AttributeFromString(const std::string& s);
    static Type TypeFromString(const std::string& s);
    static Status StatusFromString(const std::string& s);
    static std::wstring Utf8ToWide(const std::string& s);
};
