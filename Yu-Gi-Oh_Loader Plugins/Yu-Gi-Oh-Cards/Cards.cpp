#include <cstring>
#include <cstdlib>
#include <fstream>
#include <iostream>
#include <vector>
#include <Windows.h>

#include "Cards.h"
#include "Logger.h"

#include <cstdint>

// Requires nlohmann::json (single header - add nlohmann/json.hpp to the
// project's include path, or `vcpkg install nlohmann-json`).
#include <json.hpp>
using json = nlohmann::json;

// NOTE: stb_image is no longer needed here. The game's own decoder
// (traced to sub_140876E80 -> sub_1408C70C0 -> sub_140872F70) already
// dispatches on file magic bytes - 0x89 for PNG, 0xFF for JPEG - so we
// can hand it raw, untouched file bytes and get a genuinely lossless
// PNG round-trip for free. Decoding to RGBA ourselves was solving a
// problem the engine already solves downstream.

unsigned long long Cards::orig_getInternalCardID = 0x14076E000;
unsigned long long Cards::orig_getKonamiCardID = 0x14076D7F0;
unsigned long long Cards::orig_getCardProps = 0x1407CAB30;

// FIXED: this used to point at 0x140753BA0 (GetCardImage_ in the reference
// module), which returns a CardImageData* - an 8-byte bookkeeping struct
// (two Dwords) used by SetupImageTable_, NOT pixel/image bytes. Handing a
// decoded RGBA pointer through that call site corrupts whatever consumes
// that struct downstream.
//
// The actual illustration-loading entry point is the id-resolution +
// dispatch function traced via IDA (sub_14086D050 in this binary, matches
// IllustrationFromZibHook in the reference mod at 0x14086D050). Its
// signature is bool(__int64 obj, unsigned __int16 id, void** buffer,
// size_t* size) - it hands back raw encoded file bytes (JPEG or PNG),
// which then flow into the game's own format-sniffing decoder. This is
// the correct hook target for supplying custom card art.
unsigned long long Cards::orig_getCardIllustration = 0x14086D050;

unsigned long long Cards::orig_getUnlockedTrunkCards = 0x1407F9120; // matches UnlockedTrunkCardsHook in the reference module

// --- Image table setup (0x1407521E0) ---
unsigned long long Cards::orig_setupImageTable = 0x1407521E0;
uint64_t Cards::WidenedImageTable[TRUNK_SCAN_LIMIT] = {};

// --- Card image slot redirect (0x140753BA0) ---
//
// FIX (previously missing): these two static members were declared in
// Cards.h but never defined here. Left undefined, ImageSlotTable defaults
// to a zero-length vector, which means the bounds check in
// Get_CardImageSlot (id >= ImageSlotTable.size()) is unconditionally true -
// the hook would return null for every card, vanilla and custom alike.
unsigned long long Cards::orig_getCardImageSlot = 0x140753BA0;
std::vector<uint64_t> Cards::ImageSlotTable;
unsigned long long Cards::orig_getCardIllustrationCacheState = 0x1407529E0;

std::vector<unsigned __int16> Cards::CardIDs(VANILLA_CARD_ID_COUNT);
std::vector<unsigned __int16> Cards::InternalIDs(VANILLA_INTERNAL_ID_COUNT);
std::vector<Cards::IN_MEMORY_CARD_PROP> Cards::CardProps(VANILLA_CARD_PROP_COUNT);

std::unordered_map<unsigned __int16, Cards::ExtraCard> Cards::ExtraCardsById;
std::unordered_map<unsigned __int16, __int64> Cards::ExtraLoadIDs;

// Caches raw (still-encoded) file bytes per card id, so repeat lookups
// don't re-hit disk. NOTE: this cache is NOT what we hand back to the
// game directly - see LoadCustomCardIllustration below for why.
std::unordered_map<unsigned __int16, std::vector<unsigned char>> Cards::RawImageCache;

unsigned char Cards::TrunkScanBuffer[TRUNK_SCAN_LIMIT] = {};

typedef void(__fastcall* SetupImageTable_t)();

// Hook for sub_1407521E0. See the long comment on the declaration in
// Cards.h for why this is a wrapping hook rather than a byte patch, and
// what crashed (0x1407522A0) when it was tried as a bound-widening patch
// on the function's internals instead.
void __fastcall Cards::Setup_ImageTable()
{
    *reinterpret_cast<void**>(0x142847E84) = Cards::WidenedImageTable;

    reinterpret_cast<SetupImageTable_t>(Cards::orig_setupImageTable)();
}

// Full replacement for the original 0x140753BA0 lookup - see the long
// comment on the declaration in Cards.h for the crash this fixes and the
// open question about `a1`. Reuses Get_InternalID so custom cards route
// through ExtraLoadIDs exactly like every other lookup path.
__int64 __fastcall Cards::Get_CardImageSlot(void* a1, __int16 KonamiID)
{
    __int64 id = Cards::Get_InternalID(KonamiID);
    if (id < 0 || static_cast<size_t>(id) >= Cards::ImageSlotTable.size())
        return 0;
    return reinterpret_cast<__int64>(&Cards::ImageSlotTable[id]);
}

typedef __int64(__fastcall* GetCardIllustrationCacheState_t)(void*, __int16, unsigned char*);

// See the long comment on the declaration in Cards.h for the crash this
// fixes and why we don't attempt a full reimplementation. Custom cards get
// the function's own already-safe "no resource" fallback (same as its
// a2 == -1 case: return 0, *outFlag = 0); vanilla ids run the real original
// function, completely untouched - mutex/cache/async-load logic and all.
typedef void(__fastcall* Sub_140754380_t)(__int64*, unsigned __int16*);
typedef __int64(__fastcall* Sub_1407519E0_t)(__int64*, __int64, unsigned __int16*);

__int64 __fastcall Cards::Get_CardIllustrationCacheState(void* a1, __int16 a2, unsigned char* a3)
{
    if (static_cast<unsigned __int16>(a2) >= EXTRA_CARD_ID_BASE)
    {
        if (a3 != nullptr)
            *a3 = 0;

        unsigned __int16 idParam = static_cast<unsigned __int16>(a2);
        __int64* base = reinterpret_cast<__int64*>(a1);

        // Force-register this id the same way the "cold cache" branch of
        // sub_140753FF0 does, bypassing its LRU-list-membership check
        // entirely - that check only correctly handles ids that were
        // already known, not first-time-seen custom ids after the list
        // has warmed up with vanilla lookups.
        reinterpret_cast<Sub_140754380_t>(0x140754380)(base + 12, &idParam);

        __int64 stackBuf[3] = {}; // stand-in for `a2` out-param in sub_1407519E0's signature (__int128 v13/v14 + v15) - size/layout not fully confirmed
        reinterpret_cast<Sub_1407519E0_t>(0x1407519E0)(base + 10, reinterpret_cast<__int64>(stackBuf), &idParam);

        return 0;
    }

    return reinterpret_cast<GetCardIllustrationCacheState_t>(Cards::orig_getCardIllustrationCacheState)(a1, a2, a3);
}

__int64 __fastcall Cards::Get_InternalID(__int16 a1)
{
    // Ground truth from Get_InternalIdFromKonamiId decompile: ANY id that
    // lands outside the vanilla range (sentinel -1, custom-range ids, etc)
    // returns 0 in the real original - not -1. 0 is a valid, meaningful
    // internal id (slot 0), and downstream code expects it as the "out of
    // range" answer, not a distinct invalid marker.
    if (a1 == -1)
        return 0;

    if (static_cast<unsigned __int16>(a1) >= EXTRA_CARD_ID_BASE)
    {
        auto it = Cards::ExtraLoadIDs.find(static_cast<unsigned __int16>(a1));
        if (it != Cards::ExtraLoadIDs.end())
            return it->second;

        Logger::WriteLog(std::format("Get_InternalID: unknown extra card id {}", a1), MODULE_NAME, 1);
        return 0;
    }

    __int64 index = static_cast<__int64>(a1) - VANILLA_INTERNAL_ID_OFFSET;
    if (index < 0 || static_cast<size_t>(index) >= Cards::InternalIDs.size())
        return 0;
    return Cards::InternalIDs[index];
}

__int64 __fastcall Cards::Get_KonamiID(__int16 a1)
{
    return 0;
}

// --- illustration loading ---

typedef bool(__fastcall* GetCardIllustration_t)(__int64, unsigned __int16, void**, size_t*);

// Reads ExtraCardsById[id].ImagePath off disk as raw bytes (no decoding -
// PNG or JPEG both work, the engine's own decoder dispatches on magic
// bytes). Caches the encoded bytes so repeat lookups skip the disk.
//
// IMPORTANT: whatever we write into *buffer must be freeable via the
// engine's own free() - it frees the pointer unconditionally right after
// consuming it (confirmed in the caller: `free(Block: v285)`). We
// malloc() a fresh copy per call rather than handing out a pointer into
// RawImageCache's own storage, so the cache stays valid across repeat
// calls and we never risk freeing memory we still own.
bool Cards::LoadCustomCardIllustration(unsigned __int16 id, void** outBuffer, size_t* outSize)
{
    auto cacheIt = Cards::RawImageCache.find(id);
    if (cacheIt == Cards::RawImageCache.end())
    {
        auto entry = Cards::ExtraCardsById.find(id);
        if (entry == Cards::ExtraCardsById.end() || entry->second.ImagePath.empty())
        {
            Logger::WriteLog(std::format("LoadCustomCardIllustration: no ImagePath for card {}", id), MODULE_NAME, 1);
            return false;
        }

        std::ifstream file(entry->second.ImagePath, std::ios::binary);
        if (!file)
        {
            Logger::WriteLog(std::format("LoadCustomCardIllustration: failed to open {}", entry->second.ImagePath), MODULE_NAME, 1);
            return false;
        }

        std::vector<unsigned char> fileBytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        if (fileBytes.empty())
        {
            Logger::WriteLog(std::format("LoadCustomCardIllustration: {} is empty or unreadable", entry->second.ImagePath), MODULE_NAME, 1);
            return false;
        }

        Logger::WriteLog(std::format("LoadCustomCardIllustration: cached {} ({} bytes) for card {}",
            entry->second.ImagePath, fileBytes.size(), id), MODULE_NAME, 0);

        cacheIt = Cards::RawImageCache.emplace(id, std::move(fileBytes)).first;
    }

    const std::vector<unsigned char>& cached = cacheIt->second;

    void* copy = std::malloc(cached.size());
    if (!copy)
    {
        Logger::WriteLog(std::format("LoadCustomCardIllustration: malloc failed for card {}", id), MODULE_NAME, 2);
        return false;
    }

    std::memcpy(copy, cached.data(), cached.size());

    *outBuffer = copy;
    *outSize = cached.size();
    return true;
}

// Hook target for the illustration loader (0x14086D050 equivalent).
// Mirrors IllustrationFromZib_ in the reference module: check for a
// custom-range id with art on disk first, otherwise fall through to the
// original function untouched.
bool __fastcall Cards::Get_CardIllustration(__int64 a1, unsigned __int16 a2, void** buffer, size_t* size)
{
    if (a2 >= EXTRA_CARD_ID_BASE)
    {
        if (Cards::LoadCustomCardIllustration(a2, buffer, size))
            return true;

        // Fall back to a known-good vanilla illustration rather than
        // returning false and leaving the card with no art at all.
        Logger::WriteLog(std::format("Get_CardIllustration: falling back to placeholder art for extra card {}", a2), MODULE_NAME, 1);
        return reinterpret_cast<GetCardIllustration_t>(Cards::orig_getCardIllustration)(a1, EXTRA_CARD_PLACEHOLDER_IMAGE_ID, buffer, size);
    }

    return reinterpret_cast<GetCardIllustration_t>(Cards::orig_getCardIllustration)(a1, a2, buffer, size);
}

// --- trunk / unlocked cards ---

typedef unsigned char* (__fastcall* GetUnlockedTrunkCards_t)();

unsigned char* __fastcall Cards::Get_UnlockedTrunkCards()
{
    // Preserve whatever the vanilla function actually computes/returns for
    // the original card range - we only ever want to ADD entries for
    // custom cards on top of it, never silently wipe vanilla trunk state.
    unsigned char* original = reinterpret_cast<GetUnlockedTrunkCards_t>(Cards::orig_getUnlockedTrunkCards)();

    static bool copiedVanillaState = false;
    if (!copiedVanillaState)
    {
        std::memcpy(Cards::TrunkScanBuffer, original, VANILLA_CARD_ID_COUNT);
        copiedVanillaState = true;
    }

    for (auto const& [id, loadId] : Cards::ExtraLoadIDs)
    {
        if (loadId >= 0 && loadId < TRUNK_SCAN_LIMIT)
            Cards::TrunkScanBuffer[loadId] = 3;
    }

    return Cards::TrunkScanBuffer;
}

// FIX: was Cards::CardProps.at(a1), which throws a C++ exception
// (0xe06d7363 / EH_EXCEPTION - not a null-pointer access violation) instead
// of returning null when a1 is out of range. The reference mod avoids this
// entirely by statically sizing every backing array to new_card_limit, so
// its equivalent lookup can never go out of range in the first place. We
// size CardProps to actual card count instead, so anything that calls this
// with an id from a widened scan loop (e.g. up to TRUNK_SCAN_LIMIT) beyond
// the real card count needs an explicit bounds check rather than relying on
// .at() to do it via exception.
// NOTE: mirrors the original game's own out-of-range behavior, confirmed
// via decompile of YGO::CARDS::Get_CardPropsFromInternalId:
//
//   if (InternalID >= 10166) return INTERNAL_ID_CARD_PROPS; // base pointer, i.e. &array[0]
//   else return &INTERNAL_ID_CARD_PROPS[InternalID];
//
// The original clamps to slot 0 rather than returning a zeroed dummy -
// whatever real card data lives at index 0 is presumably what's expected
// downstream, so we match that instead of inventing our own placeholder.
Cards::IN_MEMORY_CARD_PROP* __fastcall Cards::Get_CardPropsByInternalID(__int16 a1)
{
    // NOTE: this fires for every id the widened trunk/visibility scan loop
    // walks (up to TRUNK_SCAN_LIMIT = 0xFFFF), of which only ~10168 are
    // real cards - so "out of range" here is the normal, expected outcome
    // for the vast majority of calls, not an error condition. Do NOT log
    // per-call (confirmed: was producing 50,000+ log lines per scan pass).
    if (a1 < 0 || static_cast<size_t>(a1) >= Cards::CardProps.size())
        return &Cards::CardProps[0];
    return &Cards::CardProps[a1];
}

// --- helpers ---

std::wstring Cards::Utf8ToWide(const std::string& s)
{
    if (s.empty()) return std::wstring();
    int size = MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), nullptr, 0);
    std::wstring result(size, 0);
    MultiByteToWideChar(CP_UTF8, 0, s.c_str(), (int)s.size(), result.data(), size);
    return result;
}

Cards::Attribute Cards::AttributeFromString(const std::string& s)
{
    static const std::unordered_map<std::string, Attribute> map = {
        {"Special", SPECIAL}, {"Light", LIGHT}, {"Dark", DARK}, {"Water", WATER},
        {"Fire", FIRE}, {"Earth", EARTH}, {"Wind", WIND}, {"Divine", DIVINE},
        {"Spell", SPELL}, {"Trap", TRAP},
    };
    auto it = map.find(s);
    return it != map.end() ? it->second : SPECIAL;
}

Cards::Type Cards::TypeFromString(const std::string& s)
{
    static const std::unordered_map<std::string, Type> map = {
        {"Unknown", Unkown}, {"Dragon", Dragon}, {"Zombie", Zombie}, {"Fiend", Fiend},
        {"Pyro", Pyro}, {"SeaSerpent", SeaSerpent}, {"Rock", Rock}, {"Machine", Machine},
        {"Fish", Fish}, {"Dinosaur", Dinosaur}, {"Insect", Insect}, {"Beast", Beast},
        {"BeastWarrior", BeastWarrior}, {"Plant", Plant}, {"Aqua", Aqua}, {"Warrior", Warrior},
        {"WingedBeast", WingedBeast}, {"Fairy", Fairy}, {"Spellcaster", Spellcaster},
        {"Thunder", Thunder}, {"Reptile", Reptile}, {"Psychic", Psychic}, {"Wyrm", Wyrm},
        {"Cyberse", Cyberse}, {"DivineBeast", DivineBeast}, {"CreatorGod", CreatorGod},
    };
    auto it = map.find(s);
    return it != map.end() ? it->second : Unkown;
}

Cards::Status Cards::StatusFromString(const std::string& s)
{
    static const std::unordered_map<std::string, Status> map = {
        {"Unlimited", Unlimited}, {"Limited", Limited}, {"SemiLimited", SemiLimited},
    };
    auto it = map.find(s);
    return it != map.end() ? it->second : Unlimited;
}

// --- new cards pipeline ---

bool Cards::LoadExtraCardsFromJson(const std::string& path)
{
    std::ifstream file(path);
    if (!file)
    {
        Logger::WriteLog(std::format("extracards.json not found at {} - skipping.", path), MODULE_NAME, 1);
        return false;
    }

    json j;
    try
    {
        file >> j;
    }
    catch (const std::exception& e)
    {
        Logger::WriteLog(std::format("Failed to parse extracards.json: {}", e.what()), MODULE_NAME, 2);
        return false;
    }

    if (j.value("version", 0) != 1)
    {
        Logger::WriteLog("extracards.json version not supported - skipping.", MODULE_NAME, 2);
        return false;
    }

    int entryIndex = 0;
    for (auto const& entry : j["cards"])
    {
        // FIX: entry.at("id") / entry.at("name") throw nlohmann::json
        // exceptions (out_of_range if the key is missing, type_error if it
        // can't convert - both surface as the same 0xe06d7363 C++ exception
        // code you'd see from a std::vector::at() throw) if a future edit
        // of extracards.json ever has a malformed entry. Previously nothing
        // in this loop caught that, so one bad entry would abort loading
        // silently-uncaught during DLL init. Wrapping per-entry so one bad
        // card is skipped and logged instead of taking the whole load down.
        try
        {
            ExtraCard card{};
            card.ID = entry.at("id").get<unsigned __int16>();

            if (card.ID < EXTRA_CARD_ID_BASE)
            {
                Logger::WriteLog(std::format("Skipping card {}: below EXTRA_CARD_ID_BASE (0x{:X}).", card.ID, EXTRA_CARD_ID_BASE), MODULE_NAME, 1);
                entryIndex++;
                continue;
            }

            card.Name = Utf8ToWide(entry.at("name").get<std::string>());
            card.Description = Utf8ToWide(entry.value("description", ""));
            card.ImagePath = entry.value("image", "");

            IN_MEMORY_CARD_PROP& props = card.Props;
            props.CARD_ID = card.ID;
            props.ATK = entry.value("attack", 0);
            props.DEF = entry.value("defense", 0);
            props.EffectTypes = entry.value("effect_types", 0);
            props.Attribute = AttributeFromString(entry.value("attribute", "Special"));
            props.Level = entry.value("level", 0);
            props.Property = entry.value("property", 0);
            props.Type = TypeFromString(entry.value("type", "Unknown"));
            props.Field_10 = 0;
            props.Field_12 = 0;
            props.LimitedStatus = StatusFromString(entry.value("limitation", "Unlimited"));
            props.Field_13 = 0;

            Cards::ExtraCardsById[card.ID] = std::move(card);
        }
        catch (const std::exception& e)
        {
            Logger::WriteLog(std::format("LoadExtraCardsFromJson: card entry {} failed to parse - {}. Skipping entry.", entryIndex, e.what()), MODULE_NAME, 2);
        }

        entryIndex++;
    }

    Logger::WriteLog(std::format("Parsed {} extra cards from JSON.", Cards::ExtraCardsById.size()), MODULE_NAME, 0);
    return true;
}

void Cards::ApplyExtraCards()
{
    // CardProps grows by push_back; the load id for each extra card is
    // simply its resulting index, mirroring `load_ids[id] = ++load_id`
    // in the reference module.
    for (auto& [id, card] : Cards::ExtraCardsById)
    {
        __int64 loadId = static_cast<__int64>(Cards::CardProps.size());
        Cards::CardProps.push_back(card.Props);
        Cards::ExtraLoadIDs[id] = loadId;
    }

    // FIX (previously missing): nothing resized ImageSlotTable, so
    // Get_CardImageSlot's bounds check (id >= ImageSlotTable.size()) was
    // always true against an empty vector - every lookup returned null,
    // vanilla and custom cards alike. Size it here, after CardProps has
    // its final count for this load, so every internal id up to
    // CardProps.size() has a valid 8-byte slot to hand out.
    Cards::ImageSlotTable.resize(Cards::CardProps.size());

    Logger::WriteLog(std::format("Applied {} extra cards. CardProps now {} entries. ImageSlotTable resized to {}.",
        Cards::ExtraCardsById.size(), Cards::CardProps.size(), Cards::ImageSlotTable.size()), MODULE_NAME, 0);
}

void Cards::WriteExtraCardNames()
{
    // Mirrors card::Card (0xA0) from the reference module - only the
    // fields we actually populate for custom cards. Do not construct or
    // copy this type; only offset into raw live game memory with it.
    struct GameCard
    {
        bool ExistsOnDuel;      // 0x0
        char pad0[7];
        wchar_t* Name;           // 0x8
        wchar_t* Description;    // 0x10
        uint16_t TrunkID;        // 0x18
        char pad1[2];
        bool IsMonster;          // 0x1C
        bool IsSpell;            // 0x1D
        bool IsTrap;             // 0x1E
        bool IsFieldSpell;       // 0x1F
        bool IsNormalMonster;    // 0x20
        bool IsEffectMonster;    // 0x21
        char pad2[0x44 - 0x22];
        int Attack1;             // 0x44
        int Attack2;             // 0x48
        Attribute CardAttribute; // 0x4C
        int Defense1;            // 0x50
        int Defense2;            // 0x54
        int Icon;                // 0x58
        int Kind;                // 0x5C
        int Level;                // 0x60
        int Limitation;           // 0x64
        int ID1, ID2, ID3;         // 0x68/0x6C/0x70
        int Rank;                  // 0x74
        int LeftPendulumScale;     // 0x78
        int RightPendulumScale;    // 0x7C
        int LevelOrLinkRatingOrRank; // 0x80
        Type CardType;             // 0x84
        int LinkRating;            // 0x88
        uint32_t LinkArrows;       // 0x8C
        int Valid;                 // 0x90
        int Frame;                 // 0x94
    };

    for (auto& [id, card] : Cards::ExtraCardsById)
    {
        auto* gc = reinterpret_cast<GameCard*>(
            GAME_CARD_TABLE + static_cast<unsigned __int64>(id) * GAME_CARD_STRIDE);

        const IN_MEMORY_CARD_PROP& props = card.Props;

        gc->Name = card.Name.data();
        gc->Description = card.Description.data();

        bool isSpell = props.Attribute == SPELL;
        bool isTrap = props.Attribute == TRAP;
        bool isMonster = !isSpell && !isTrap;

        gc->IsMonster = isMonster;
        gc->IsSpell = isSpell;
        gc->IsTrap = isTrap;
        // IsNormalMonster / IsEffectMonster: your EffectTypes field doesn't
        // yet distinguish Normal vs Effect the way the reference's Kind
        // enum does (Normal=0 vs Effect=1, etc). Flag as effect monster
        // whenever EffectTypes is nonzero as a first pass - revisit if you
        // want true Normal-monster cards.
        gc->IsNormalMonster = isMonster && props.EffectTypes == 0;
        gc->IsEffectMonster = isMonster && props.EffectTypes != 0;

        gc->Attack1 = gc->Attack2 = props.ATK;
        gc->Defense1 = gc->Defense2 = props.DEF;
        gc->CardAttribute = props.Attribute;
        gc->Icon = props.Property;
        gc->Kind = props.EffectTypes;
        gc->Level = props.Level;
        gc->Limitation = props.LimitedStatus;
        gc->ID1 = gc->ID2 = gc->ID3 = id;
        gc->CardType = props.Type;
        gc->Valid = 1;
        // Frame: the reference derives this from Kind via a lookup table
        // at 0x140BF7820 (CardData::GetFrame). You don't have that table
        // wired up yet - hardcoding Frame::Normal (0) or Frame::Effect (1)
        // for now is a reasonable stopgap, but XYZ/Synchro/Link/Pendulum
        // custom cards will render with the wrong frame art until this is
        // properly derived.
        gc->Frame = (props.EffectTypes != 0) ? 1 : 0;
    }

    Logger::WriteLog(std::format("Wrote {} extra card game-table entries.", Cards::ExtraCardsById.size()), MODULE_NAME, 0);
}
