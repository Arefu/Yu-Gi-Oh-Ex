#include <Windows.h>
#include <array>
#include <cstring>
#include <format>
#include <mutex>
#include <vector>

#include "CardTable.h"
#include "Detours.h"
#include "Logger.h"
#include "Save.h"

namespace
{
    constexpr size_t kBlobTable = 0x5DC8;          // 24008: the card table's place in the blob
    constexpr const char* kExtensionTag = "cards.high";

    uintptr_t orig_GetCardUnlockTable = 0x1407F8130;  // YGO::SAVE::Get_CardUnlockTable(profile) -> blob + 0x5DC8
    uintptr_t orig_ValidateAndMerge = 0x1407F82E0;    // (file blob, live blob): merges a save read from disk into the live blob
    uintptr_t orig_ValidateAndMergeLegacy = 0x1407F8530;
    uintptr_t orig_InitNewBlob = 0x1407F8160;         // (blob): a new profile

    std::mutex g_Lock;
    std::array<unsigned char, CardTable::Size> g_Table{};
    unsigned char* g_Blob = nullptr;                  // the live blob g_Table belongs to (its table is g_Blob + kBlobTable)

    // The ids past the blob's table, as the save extension keeps them.
    void LoadHigh()
    {
        std::memset(g_Table.data() + CardTable::SavedSize, 0, CardTable::Size - CardTable::SavedSize);
        const int size = Save::GetExtension(kExtensionTag, nullptr, 0);
        if (size <= 0)
            return;
        std::vector<unsigned char> data(static_cast<size_t>(size));
        Save::GetExtension(kExtensionTag, data.data(), size);
        int cards = 0;
        for (size_t i = 0; i + 3 <= data.size(); i += 3)
        {
            const uint16_t id = static_cast<uint16_t>(data[i] | data[i + 1] << 8);
            if (id >= CardTable::SavedSize)
            {
                g_Table[id] = data[i + 2];
                ++cards;
            }
        }
        Logger::WriteLog(std::format("Card table: {} card(s) with ids past {} from the save extension", cards, CardTable::SavedSize - 1), MODULE_NAME, 0);
    }

    void StoreHigh()
    {
        std::vector<unsigned char> data;
        for (size_t id = CardTable::SavedSize; id < CardTable::Size; ++id)
            if (g_Table[id])
            {
                data.push_back(static_cast<unsigned char>(id & 0xFF));
                data.push_back(static_cast<unsigned char>(id >> 8));
                data.push_back(g_Table[id]);
            }
        Save::SetExtension(kExtensionTag, data.data(), static_cast<int>(data.size()));   // empty = no entry (the save stays the game's size)
    }

    // A blob the game keeps on the stack is a temporary (a save being read or checked). g_Table must never take it over: once that function
    // returns, FlushToBlob would write 20000 bytes into a dead stack frame (crash 2026-10-20: zeroed stack, return to address 0).
    bool OnThisStack(const unsigned char* blob)
    {
        ULONG_PTR low = 0, high = 0;
        GetCurrentThreadStackLimits(&low, &high);
        return reinterpret_cast<ULONG_PTR>(blob) >= low && reinterpret_cast<ULONG_PTR>(blob) < high;
    }

    // g_Table takes over a live blob: its low part from the blob, its high part from the save that was read (or none: a new profile).
    void TakeBlob(unsigned char* blob, bool fromSave)
    {
        g_Blob = blob;
        std::memcpy(g_Table.data(), blob + kBlobTable, CardTable::SavedSize);
        if (fromSave)
            LoadHigh();
        else
            std::memset(g_Table.data() + CardTable::SavedSize, 0, CardTable::Size - CardTable::SavedSize);
    }

    // Changes made through g_Table go back into its blob (before it is replaced, and before it is saved).
    void FlushToBlob()
    {
        if (g_Blob)
            std::memcpy(g_Blob + kBlobTable, g_Table.data(), CardTable::SavedSize);
    }

    unsigned char* __fastcall Hook_GetCardUnlockTable(unsigned int profile)
    {
        auto* table = reinterpret_cast<unsigned char*(__fastcall*)(unsigned int)>(orig_GetCardUnlockTable)(profile);
        if (!table)
            return table;
        unsigned char* blob = table - kBlobTable;
        if (OnThisStack(blob))
            return table;   // a temporary blob: the game reads its own table
        std::lock_guard<std::mutex> guard(g_Lock);
        if (blob != g_Blob)
        {
            FlushToBlob();
            TakeBlob(blob, true);
        }
        return g_Table.data();
    }

    char __fastcall Hook_ValidateAndMerge(unsigned char* file, unsigned char* live)
    {
        const char ok = reinterpret_cast<char(__fastcall*)(unsigned char*, unsigned char*)>(orig_ValidateAndMerge)(file, live);
        if (ok && live && live != file && !OnThisStack(live))
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            TakeBlob(live, true);   // the merge rewrote the blob's table from the file; the extension of that file came with it
        }
        return ok;
    }

    char __fastcall Hook_ValidateAndMergeLegacy(unsigned char* file, unsigned char* live)
    {
        const char ok = reinterpret_cast<char(__fastcall*)(unsigned char*, unsigned char*)>(orig_ValidateAndMergeLegacy)(file, live);
        if (ok && live && live != file && !OnThisStack(live))
        {
            std::lock_guard<std::mutex> guard(g_Lock);
            TakeBlob(live, true);
        }
        return ok;
    }

    void __fastcall Hook_InitNewBlob(unsigned char* blob)
    {
        reinterpret_cast<void(__fastcall*)(unsigned char*)>(orig_InitNewBlob)(blob);
        if (OnThisStack(blob))
            return;
        std::lock_guard<std::mutex> guard(g_Lock);
        if (blob == g_Blob || !g_Blob)
            TakeBlob(blob, false);   // a new profile owns nothing past the game's ids either
    }

    // Every blob the game writes gets the table as it is now: the low part in the blob, the high part in the extension.
    int __cdecl WriteFilter(unsigned char* blob, int size, void*)
    {
        if (size < static_cast<int>(kBlobTable + CardTable::SavedSize))
            return 0;
        std::lock_guard<std::mutex> guard(g_Lock);
        if (!g_Blob)
            return 0;
        FlushToBlob();
        StoreHigh();
        if (std::memcmp(blob + kBlobTable, g_Table.data(), CardTable::SavedSize) == 0)
            return 0;
        std::memcpy(blob + kBlobTable, g_Table.data(), CardTable::SavedSize);
        return 1;
    }
}

bool CardTable::Install()
{
    Save::AddWriteFilter(&WriteFilter, nullptr);

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_GetCardUnlockTable, Hook_GetCardUnlockTable);
    DetourAttach(&(PVOID&)orig_ValidateAndMerge, Hook_ValidateAndMerge);
    DetourAttach(&(PVOID&)orig_ValidateAndMergeLegacy, Hook_ValidateAndMergeLegacy);
    DetourAttach(&(PVOID&)orig_InitNewBlob, Hook_InitNewBlob);
    const LONG err = DetourTransactionCommit();
    Logger::WriteLog(std::format("Card table for every Konami id (0-{}, ids past {} in the save extension): {}", Size - 1, SavedSize - 1, err), MODULE_NAME, err == 0 ? 0 : 2);
    return err == 0;
}
