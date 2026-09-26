#pragma once
#include <cstdint>

// Removes the vanilla card count limits: redirects the Konami id and internal id lookup
// tables into bigger arrays owned by this DLL, and raises the hardcoded bound checks in
// the trunk scan and the deck checks so ids above the vanilla range are visited.

constexpr uintptr_t kKonamiCardIdLocation = 0x140D50510;   // g_KonamiIds
constexpr uintptr_t kInternalCardIdLocation = 0x140D55480; // g_iInternalIDs

constexpr size_t kVanillaCardIdCount = 10168;
constexpr size_t kVanillaInternalIdCount = 11072;

// How far the widened bound checks reach. Every backing array they read must be at least this big.
constexpr size_t kWidenedIdLimit = 0xFFFF;

namespace Limits
{
    // Backing storage the two id tables are redirected into. Raw arrays, not vectors: the
    // game is handed pointers into them.
    inline uint16_t KonamiIDTable[kWidenedIdLimit]{};
    inline uint16_t InternalIDTable[kWidenedIdLimit]{};

    // Applies every patch below in order. Call once, after the game has filled its own id
    // tables and after they were copied into KonamiIDTable / InternalIDTable.
    void ApplyAll();

    void RedirectInternalIDTable();
    void RedirectKonamiIDTable();
    void RemoveVanillaCountLimit();
    void WidenTrunkScanBounds();
    void WidenIllustrationLoadBounds();

    // Detour replacement for the internal id -> Konami id lookup (0x14076D7F0). Its table
    // address is a rip-relative LEA that can't reach a global in this DLL, so the whole
    // function is hooked instead of patching the operand.
    int64_t __fastcall Get_KonamiIndexLookup(unsigned int a1);
}
