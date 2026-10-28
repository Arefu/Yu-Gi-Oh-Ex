#include "UserDecks.h"

#include <Windows.h>
#include <algorithm>
#include <cstdint>
#include <cstring>
#include <deque>
#include <filesystem>
#include <format>
#include <fstream>
#include <iterator>
#include <mutex>
#include <vector>

#include "Detours.h"
#include "Logger.h"
#include "Save.h"

// Ported from Toon Jinzo's Yu-Gi-Oh-DeckSlots (thank you): the same hook points, ids and file layout, as Detours on the function entries,
// with the decks kept in Core's save extension instead of a side file. See UserDecks.h.
namespace
{
    using namespace UserDecks;

    constexpr size_t kDeckSize = 0x130;               // YGO::SAVE::DeckListItem
    constexpr size_t kUsedOffset = 300;               // DeckListItem.IsValid: the slot holds a deck
    constexpr unsigned kValidationCount = 732;        // the profile's DeckValidation table: deck ids 0-731
    constexpr size_t kManagerBlob = 10208;            // ProfileSaveManager: the profile's save blob
    constexpr size_t kManagerValidation = 16;         // ProfileSaveManager: DeckValidation* begin (+24 end)
    constexpr unsigned kCurrentProfile = 0xFFFFFFFD;
    constexpr uint32_t kFlagEmpty = 8;                // DeckValidation.Flags: the slot holds no deck
    constexpr size_t kDeckListIds = 1208;             // DeckSelectList: tab 0's ids (vector<u32>)
    constexpr const char* kExtensionTag = "UserDecks";
    constexpr char kFileMagic[8] = { 'Y', 'G', 'O', 'D', 'E', 'C', 'K', 'X' };   // Yu-Gi-Oh-DeckSlots' file header, also the extension's

    struct DeckValidation
    {
        uint32_t Flags, OwnedCount, TotalCount;
    };

    struct IntVector   // the game's std::vector<int>
    {
        int* First;
        int* Last;
        int* End;
    };

#pragma pack(push, 1)
    struct FileHeader
    {
        char Magic[8];
        uint32_t Version, DeckSize, Count, Reserved;
    };
#pragma pack(pop)

    uintptr_t orig_DecodeDeckId = 0x1407F9150;
    uintptr_t orig_UserDeckAt = 0x14076E8E0;
    uintptr_t orig_GetValidation = 0x14081AA20;        // YGO::SAVE::Deck_GetValidation
    uintptr_t orig_CopyValidation = 0x140871430;
    uintptr_t orig_Revalidate = 0x1407BDFD0;           // YGO::SAVE::Deck_Revalidate
    uintptr_t orig_RevalidateAll = 0x1407BDF20;        // YGO::SAVE::Deck_RevalidateAll
    uintptr_t orig_SetSelectedDeck = 0x1407F93C0;
    uintptr_t orig_ListAll = 0x14081AC80;              // Refill's (deck editor, deck pickers)
    uintptr_t orig_ListForSeatTable = 0x14081ADC0;     // DeckSelectList_FillSeatTableDecks'
    uintptr_t orig_ListUserSlots = 0x14081AEA0;        // the deck editor's slot list (empty slots too)
    uintptr_t orig_FillDecks = 0x1408AD890;            // YGO::RIX::DeckSelectList_FillDecks
    uintptr_t orig_GetOrInitProfileBuffer = 0x140871500;
    uintptr_t orig_GetOrCreateBlob = 0x140871490;      // YGO::SAVE::Get_OrCreateBlob

    const auto GetBlob = reinterpret_cast<uint8_t*(__fastcall*)(unsigned profile)>(0x1407F8100);        // YGO::SAVE::Get_Blob
    const auto FindProfile = reinterpret_cast<void*(__fastcall*)(void* input, unsigned profile)>(0x1408004D0);
    const auto GetProfileSaveManager = reinterpret_cast<uint8_t*(__fastcall*)(void* profile)>(0x140874930);
    const auto ValidateDeck = reinterpret_cast<DeckValidation*(__fastcall*)(DeckValidation* out, unsigned profile, const uint8_t* deck)>(0x1407F9B60);
    const auto VectorInsert = reinterpret_cast<void*(__fastcall*)(IntVector* vector, int* where, const int* value)>(0x14081A840);
    const auto RebuildDeckTabs = reinterpret_cast<void(__fastcall*)(int64_t list)>(0x1408AE5B0);
    void* const kInputManager = reinterpret_cast<void*>(0x142924010);   // g_InputManager

    struct DeckBytes
    {
        uint8_t Bytes[kDeckSize];
    };

    // The game keeps pointers to a deck (Deck_UserDeckAt) and its validation (Deck_GetValidation), so these are deques: growing them never
    // moves an element. They only grow; g_Count is how many slots the profile has.
    std::deque<DeckBytes> g_Decks;
    std::deque<DeckValidation> g_Validation;
    std::vector<bool> g_ValidationKnown;
    unsigned g_Count = 0;
    uint8_t g_EmptyDeck[kDeckSize]{};
    DeckValidation g_OutOfRange{};
    uint8_t* g_Owner = nullptr;   // the profile's save blob the extras belong to
    std::mutex g_Lock;
    std::filesystem::path g_Imported;   // a Yu-Gi-Oh-DeckSlots file read into this save: deleted once the save holding its decks is written
    bool g_WroteDecks = false;          // the last save write put the decks into the extension

    uint8_t* Deck(unsigned k) { return g_Decks[k].Bytes; }
    bool Used(unsigned k) { return Deck(k)[kUsedOffset] != 0; }
    bool Owns(const uint8_t* blob) { return g_Owner && blob == g_Owner; }
    void Forget() { std::fill(g_ValidationKnown.begin(), g_ValidationKnown.end(), false); }

    // Empty slots for a profile whose decks run up to slot used - 1: those plus kHeadroom free ones.
    void Reset(unsigned used)
    {
        g_Count = (std::min)(used + kHeadroom, static_cast<unsigned>(kMaxExtraDecks));
        if (g_Decks.size() < g_Count)
        {
            g_Decks.resize(g_Count);
            g_Validation.resize(g_Count);
            g_ValidationKnown.resize(g_Count);
        }
        for (DeckBytes& deck : g_Decks)
            std::memset(deck.Bytes, 0, kDeckSize);
        Forget();
    }

    // An extra deck id (1024 up) -> its slot, or -1.
    int ExtraSlot(unsigned id) { return id >= kFirstExtraId && id - kFirstExtraId < g_Count ? static_cast<int>(id - kFirstExtraId) : -1; }

    DeckValidation* Validation(unsigned k)
    {
        if (!g_ValidationKnown[k])
        {
            DeckValidation result{};
            ValidateDeck(&result, kCurrentProfile, Deck(k));
            if (!Used(k))
                result.Flags |= kFlagEmpty;
            g_Validation[k] = result;
            g_ValidationKnown[k] = true;
        }
        return &g_Validation[k];
    }

    void Push(IntVector* vector, int value)
    {
        if (vector->Last != vector->End)
            *vector->Last++ = value;
        else
            VectorInsert(vector, vector->Last, &value);
    }

    // The used extras go right after the save's user decks (ids 0-31) in a list that goes on with built-in decks.
    void InsertExtras(IntVector* vector, uint32_t mask, bool checkMask)
    {
        int* first = vector->First;
        const size_t count = vector->Last - first;
        size_t at = 0;
        while (at < count && static_cast<unsigned>(first[at]) < static_cast<unsigned>(kSaveDecks))
            ++at;
        const std::vector<int> tail(first + at, first + count);
        vector->Last = first + at;
        for (unsigned k = 0; k < g_Count; ++k)
            if (Used(k) && (!checkMask || (Validation(k)->Flags & mask) == 0))
                Push(vector, static_cast<int>(kFirstExtraId + k));
        for (int id : tail)
            Push(vector, id);
    }

    // ---- what the decks are read from and written to

    bool ValidDeck(const uint8_t* deck)
    {
        uint32_t sleeve;
        uint16_t main, extra, side;
        std::memcpy(&sleeve, deck + 0x120, 4);
        std::memcpy(&main, deck + 0x42, 2);
        std::memcpy(&extra, deck + 0x44, 2);
        std::memcpy(&side, deck + 0x46, 2);
        return (sleeve == 0 || sleeve < 0xF0) && main <= 60 && extra <= 15 && side <= 15;   // the game's own save check, as DeckSlots did
    }

    // {header, decks}: the extension and a Yu-Gi-Oh-DeckSlots file share the layout. Every deck keeps its slot (and so its id): the slot
    // count is sized from the file (Reset).
    int ReadDecks(const std::vector<uint8_t>& bytes, const char* from)
    {
        Reset(0);
        FileHeader header{};
        if (bytes.size() < sizeof(header))
            return 0;
        std::memcpy(&header, bytes.data(), sizeof(header));
        if (std::memcmp(header.Magic, kFileMagic, sizeof(kFileMagic)) != 0 || header.DeckSize != kDeckSize)
        {
            Logger::WriteLog(std::format("User decks: {} is not a deck file, ignored", from), MODULE_NAME, 2);
            return 0;
        }
        const size_t stored = (std::min)(static_cast<size_t>(header.Count), (bytes.size() - sizeof(header)) / kDeckSize);
        const auto at = [&](size_t k) { return bytes.data() + sizeof(header) + k * kDeckSize; };
        size_t extent = 0;   // up to the last deck
        for (size_t k = 0; k < stored; ++k)
            if (at(k)[kUsedOffset] && ValidDeck(at(k)))
                extent = k + 1;
        size_t lost = 0;
        if (extent > static_cast<size_t>(kMaxExtraDecks))
        {
            lost = extent - kMaxExtraDecks;   // past what the game can address; not expected from any real save
            extent = kMaxExtraDecks;
        }
        Reset(static_cast<unsigned>(extent));
        int used = 0, invalid = 0;
        for (size_t k = 0; k < extent; ++k)
        {
            if (!at(k)[kUsedOffset])
                continue;
            if (!ValidDeck(at(k)))
            {
                ++invalid;
                continue;
            }
            std::memcpy(Deck(static_cast<unsigned>(k)), at(k), kDeckSize);
            ++used;
        }
        Logger::WriteLog(std::format("User decks: {} extra deck(s) from {}, {} slots (ids {}-{}){}{}", used, from, g_Count, kFirstExtraId,
            kFirstExtraId + g_Count - 1, invalid ? std::format(", {} invalid cleared", invalid) : "",
            lost ? std::format(", {} slot(s) past the game's limit of {} DROPPED", lost, kMaxExtraDecks) : ""), MODULE_NAME, lost ? 2 : 0);
        return used;
    }

    // The Yu-Gi-Oh-DeckSlots file next to the save: <save name without .dat>-extradecks.dat.
    std::filesystem::path DeckSlotsFile()
    {
        const std::filesystem::path save = Save::CurrentSavePath();
        std::filesystem::path name = save.filename();
        if (name.extension() == ".dat")
            name.replace_extension();
        return save.parent_path() / (name.u8string() + u8"-extradecks.dat");
    }

    void LoadDecks()
    {
        g_Imported.clear();
        const int size = Save::GetExtension(kExtensionTag, nullptr, 0);
        if (size > 0)
        {
            std::vector<uint8_t> bytes(size);
            Save::GetExtension(kExtensionTag, bytes.data(), size);
            ReadDecks(bytes, "the save");
            return;
        }
        Reset(0);
        const std::filesystem::path old = DeckSlotsFile();
        std::ifstream file(old, std::ios::binary);
        if (!file)
            return;
        std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        file.close();
        if (ReadDecks(bytes, old.filename().string().c_str()) > 0)
        {
            g_Imported = old;
            Logger::WriteLog(std::format("User decks: imported from Yu-Gi-Oh-DeckSlots' {}; they go into the save at its next write, then the old "
                "file is deleted", old.filename().string()), MODULE_NAME, 0);
        }
    }

    // After a save write: the imported DeckSlots file (and its .bak) goes once the save on disk holds its decks.
    void __cdecl DecksWritten()
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        if (g_Imported.empty() || !g_WroteDecks)
            return;
        std::error_code error;
        std::filesystem::path backup = g_Imported;
        backup += ".bak";
        std::filesystem::remove(backup, error);
        if (std::filesystem::remove(g_Imported, error) || !std::filesystem::exists(g_Imported))
            Logger::WriteLog(std::format("User decks: the save now holds the imported decks, {} deleted", g_Imported.filename().string()), MODULE_NAME, 0);
        else
            Logger::WriteLog(std::format("User decks: could not delete {} ({}); it is not read again while the save holds the decks",
                g_Imported.filename().string(), error.message()), MODULE_NAME, 1);
        g_Imported.clear();
    }

    // Save.cpp runs this on every save write, before the extension is written.
    int __cdecl WriteDecks(unsigned char*, int, void*)
    {
        std::lock_guard<std::mutex> guard(g_Lock);
        g_WroteDecks = false;
        if (!g_Owner)
            return 0;
        unsigned count = 0;
        for (unsigned k = 0; k < g_Count; ++k)
            if (Used(k))
                count = k + 1;
        if (count == 0)
        {
            Save::SetExtension(kExtensionTag, nullptr, 0);
            g_WroteDecks = true;   // nothing left to keep (the player deleted the imported decks)
            return 0;
        }
        g_WroteDecks = true;
        std::vector<uint8_t> bytes(sizeof(FileHeader) + count * kDeckSize);
        FileHeader header{};
        std::memcpy(header.Magic, kFileMagic, sizeof(kFileMagic));
        header.Version = 1;
        header.DeckSize = kDeckSize;
        header.Count = count;
        std::memcpy(bytes.data(), &header, sizeof(header));
        for (unsigned k = 0; k < count; ++k)
            std::memcpy(bytes.data() + sizeof(header) + static_cast<size_t>(k) * kDeckSize, Deck(k), kDeckSize);
        Save::SetExtension(kExtensionTag, bytes.data(), static_cast<int>(bytes.size()));
        return 0;   // the blob itself is not changed
    }

    // ---- hooks

    int64_t __fastcall Hook_DecodeDeckId(unsigned id, int* type, unsigned* index)
    {
        const int k = ExtraSlot(id);
        if (k < 0)
            return reinterpret_cast<int64_t(__fastcall*)(unsigned, int*, unsigned*)>(orig_DecodeDeckId)(id, type, index);
        if (type)
            *type = 1;
        if (index)
            *index = kSaveDecks + k;
        return 1;
    }

    uint8_t* __fastcall Hook_UserDeckAt(unsigned profile, int index)
    {
        const unsigned k = static_cast<unsigned>(index - kSaveDecks);
        if (index < kSaveDecks || k >= g_Count)
            return reinterpret_cast<uint8_t*(__fastcall*)(unsigned, int)>(orig_UserDeckAt)(profile, index);
        if (Owns(GetBlob(profile)))
            return Deck(k);
        std::memset(g_EmptyDeck, 0, sizeof(g_EmptyDeck));   // another profile: an empty slot
        return g_EmptyDeck;
    }

    DeckValidation* __fastcall Hook_GetValidation(unsigned profile, unsigned deckId)
    {
        const int k = ExtraSlot(deckId);
        if (k >= 0 && Owns(GetBlob(profile)))
            return Validation(k);
        if (deckId >= kValidationCount)
        {
            g_OutOfRange = { kFlagEmpty, 0, 0 };
            return &g_OutOfRange;
        }
        return reinterpret_cast<DeckValidation*(__fastcall*)(unsigned, unsigned)>(orig_GetValidation)(profile, deckId);
    }

    DeckValidation* __fastcall Hook_CopyValidation(uint8_t* manager, DeckValidation* out, int deckId)
    {
        const int k = ExtraSlot(static_cast<unsigned>(deckId));
        if (k >= 0 && Owns(*reinterpret_cast<uint8_t**>(manager + kManagerBlob)))
        {
            *out = *Validation(k);
            return out;
        }
        return reinterpret_cast<DeckValidation*(__fastcall*)(uint8_t*, DeckValidation*, int)>(orig_CopyValidation)(manager, out, deckId);
    }

    int64_t __fastcall Hook_Revalidate(unsigned profile, int deckId)
    {
        const int k = ExtraSlot(static_cast<unsigned>(deckId));
        if (k >= 0)
        {
            g_ValidationKnown[k] = false;   // the game's would write past its 732-entry table
            return 0;
        }
        if (static_cast<unsigned>(deckId) >= kValidationCount)
            return 0;
        return reinterpret_cast<int64_t(__fastcall*)(unsigned, int)>(orig_Revalidate)(profile, deckId);
    }

    int64_t __fastcall Hook_RevalidateAll(unsigned profile)
    {
        const int64_t result = reinterpret_cast<int64_t(__fastcall*)(unsigned)>(orig_RevalidateAll)(profile);
        Forget();
        return result;
    }

    int64_t __fastcall Hook_SetSelectedDeck(uint8_t* section, unsigned id)
    {
        const int k = ExtraSlot(id);
        if (k < 0)
            return reinterpret_cast<int64_t(__fastcall*)(uint8_t*, unsigned)>(orig_SetSelectedDeck)(section, id);
        *reinterpret_cast<unsigned*>(section + 4) = id;
        *reinterpret_cast<unsigned*>(section + 8) = kSaveDecks + k;   // an index: the deck editor reads it through Deck_UserDeckAt
        return id;
    }

    char __fastcall Hook_ListAll(unsigned profile, IntVector* vector, char flag)
    {
        const char result = reinterpret_cast<char(__fastcall*)(unsigned, IntVector*, char)>(orig_ListAll)(profile, vector, flag);
        if (Owns(GetBlob(profile)))
            InsertExtras(vector, 0, false);
        return result;
    }

    void* __fastcall Hook_ListForSeatTable(unsigned profile, IntVector* vector)
    {
        void* result = reinterpret_cast<void*(__fastcall*)(unsigned, IntVector*)>(orig_ListForSeatTable)(profile, vector);
        if (Owns(GetBlob(profile)))
            InsertExtras(vector, 0, false);
        return result;
    }

    int64_t __fastcall Hook_ListUserSlots(unsigned profile, IntVector* vector, char usedOnly)
    {
        const int64_t result = reinterpret_cast<int64_t(__fastcall*)(unsigned, IntVector*, char)>(orig_ListUserSlots)(profile, vector, usedOnly);
        if (Owns(GetBlob(profile)))
            for (unsigned k = 0; k < g_Count; ++k)
                if (Used(k) || !usedOnly)
                    Push(vector, static_cast<int>(kFirstExtraId + k));
        return result;
    }

    // The game's loop over the 32 user decks skips any whose DeckValidation flags meet the mask; the extras follow it, then the tabs again.
    void __fastcall Hook_FillDecks(int64_t list, int mask, char flag)
    {
        reinterpret_cast<void(__fastcall*)(int64_t, int, char)>(orig_FillDecks)(list, mask, flag);
        if (!Owns(GetBlob(kCurrentProfile)))
            return;
        IntVector* ids = reinterpret_cast<IntVector*>(list + kDeckListIds);
        const size_t before = ids->Last - ids->First;
        InsertExtras(ids, static_cast<uint32_t>(mask), true);
        if (static_cast<size_t>(ids->Last - ids->First) != before)
            RebuildDeckTabs(list);
    }

    // The profile's save is loaded (loaded = the file's blob) or made new (null).
    void* __fastcall Hook_GetOrInitProfileBuffer(uint8_t* manager, const void* loaded)
    {
        void* result = reinterpret_cast<void*(__fastcall*)(uint8_t*, const void*)>(orig_GetOrInitProfileBuffer)(manager, loaded);
        std::lock_guard<std::mutex> guard(g_Lock);
        g_Owner = *reinterpret_cast<uint8_t**>(manager + kManagerBlob);
        if (loaded)
            LoadDecks();
        else
        {
            g_Imported.clear();
            Reset(0);
            Logger::WriteLog(std::format("User decks: a new profile, {} empty extra slots", g_Count), MODULE_NAME, 0);
        }
        Forget();
        return result;
    }

    // A blob made on first use. Only taken as the extras' owner when no profile has been loaded: another manager's blob must not take
    // the loaded profile's decks.
    uint8_t* __fastcall Hook_GetOrCreateBlob(uint8_t* manager)
    {
        const bool creating = *reinterpret_cast<uint8_t**>(manager + kManagerBlob) == nullptr;
        uint8_t* blob = reinterpret_cast<uint8_t*(__fastcall*)(uint8_t*)>(orig_GetOrCreateBlob)(manager);
        if (creating)
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            if (!g_Owner)
            {
                g_Owner = blob;
                Reset(0);
            }
        }
        return blob;
    }

    struct Site
    {
        uintptr_t* Target;
        void* Hook;
        const char* Name;
    };
}

int UserDecks::ExtraCount() { return static_cast<int>(g_Count); }

void UserDecks::Install()
{
    const Site sites[] = {
        { &orig_DecodeDeckId, Hook_DecodeDeckId, "Deck_DecodeId" },
        { &orig_UserDeckAt, Hook_UserDeckAt, "Deck_UserDeckAt" },
        { &orig_GetValidation, Hook_GetValidation, "Deck_GetValidation" },
        { &orig_CopyValidation, Hook_CopyValidation, "ProfileSave_CopyValidation" },
        { &orig_Revalidate, Hook_Revalidate, "Deck_Revalidate" },
        { &orig_RevalidateAll, Hook_RevalidateAll, "Deck_RevalidateAll" },
        { &orig_SetSelectedDeck, Hook_SetSelectedDeck, "PlayerSection_SetSelectedDeck" },
        { &orig_ListAll, Hook_ListAll, "Deck_ListAll" },
        { &orig_ListForSeatTable, Hook_ListForSeatTable, "Deck_ListForSeatTable" },
        { &orig_ListUserSlots, Hook_ListUserSlots, "Deck_ListUserSlots" },
        { &orig_FillDecks, Hook_FillDecks, "DeckSelectList_FillDecks" },
        { &orig_GetOrInitProfileBuffer, Hook_GetOrInitProfileBuffer, "GetOrInitProfileBuffer" },
        { &orig_GetOrCreateBlob, Hook_GetOrCreateBlob, "Get_OrCreateBlob" },
    };

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    for (const Site& site : sites)
        DetourAttach(&reinterpret_cast<PVOID&>(*site.Target), site.Hook);
    const LONG error = DetourTransactionCommit();
    if (error != NO_ERROR)
    {
        Logger::WriteLog(std::format("User decks: hooks failed ({}), the game keeps its 32 deck slots", error), MODULE_NAME, 2);
        return;
    }
    Save::AddWriteFilter(WriteDecks, nullptr);
    Save::AddWrittenListener(DecksWritten);
    Logger::WriteLog(std::format("User decks: the save's {} + extra slots from id {} (as many as the save uses + {} free)", kSaveDecks,
        kFirstExtraId, kHeadroom), MODULE_NAME, 0);
}
