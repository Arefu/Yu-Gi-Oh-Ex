#pragma once
#include <cstdint>

namespace YGO
{
    namespace SAVE
    {
        // Profile ids the save functions accept: 0 to 3 are the real profiles, this one means "the profile in use".
        constexpr unsigned int CURRENT_PROFILE = 0xFFFFFFFD;

        // The player section of the save (the same 2968 bytes as PlayerData in the savegame library), live in memory.
        inline auto Get_PlayerSection = reinterpret_cast<uint8_t * (__fastcall*)(unsigned int Profile)>(0x1407F80A0);

        // The saved card table: 20000 bytes indexed by Konami id, bits 0-2 = owned copies, bit 3 = already seen.
        inline auto Get_CardUnlockTable = reinterpret_cast<uint8_t * (__fastcall*)(unsigned int Profile)>(0x1407F8130);

        inline auto Get_CurrentProfileId = reinterpret_cast<unsigned int(__fastcall*)(void* SaveSystem)>(0x140800690);
        inline void* const g_SaveSystem = reinterpret_cast<void*>(0x142924010);

        // Offsets inside the player section.
        namespace PlayerSection
        {
            constexpr size_t Wallet = 0x10;             // uint64, the points (DP)
            constexpr size_t UnlockedCharacters = 0x18; // 240 bits
            constexpr size_t CharacterCount = 240;
            constexpr size_t MenuUnlockFlags = 2964;    // uint32, see MenuUnlock

            // ScreenMainMenu greys a button until its bit is set.
            enum MenuUnlock : uint32_t
            {
                DUELIST_CHALLENGE = 1 << 0,
                BATTLE_PACKS = 1 << 1,
                CARD_SHOP = 1 << 2,
            };
        }
    }
}
