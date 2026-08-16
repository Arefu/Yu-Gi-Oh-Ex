#include <Windows.h>
#include <iostream>
#include <string>
#include <vector>
#include <format>

#include "detours.h"
#include "Cards.h"
#include "Logger.h"
#include "Memory.h"
#include "Targets.h"

void SetupInternalIDJumpCalls()
{
    //Internal IDs.
    Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D11E), INTERNAL_CARD_ID_LOCATION, true);
    Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D09E), INTERNAL_CARD_ID_LOCATION, true);
    Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D44B), INTERNAL_CARD_ID_LOCATION, true);
    Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D4B5), INTERNAL_CARD_ID_LOCATION, true);
    Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D5D8), INTERNAL_CARD_ID_LOCATION, true);
    Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D668), INTERNAL_CARD_ID_LOCATION, true);
    Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D6B8), INTERNAL_CARD_ID_LOCATION, true);
}
void SetupRedirectionForInternalIDs()
{
    Memory::EmplaceMOV(reinterpret_cast<void*>(INTERNAL_CARD_ID_LOCATION), reinterpret_cast<uintptr_t>(Cards::InternalIDs.data()), Memory::X64Register::RCX, true);
    Memory::EmplaceRET(reinterpret_cast<void*>(INTERNAL_CARD_ID_LOCATION + 10), true);
}

void SetupCardIDJumpCalls()
{
    Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076C0A9), KONAMI_CARD_ID_LOCATION, true);
    Memory::EmplaceCALL(reinterpret_cast<void*>(0x14076D7FA), KONAMI_CARD_ID_LOCATION, true);
}
void SetupRedirectionForCardIDs()
{
    Memory::EmplaceMOV(reinterpret_cast<void*>(KONAMI_CARD_ID_LOCATION), reinterpret_cast<uintptr_t>(Cards::CardIDs.data()), Memory::X64Register::RDI, true);
    Memory::EmplaceRET(reinterpret_cast<void*>(KONAMI_CARD_ID_LOCATION + 10), true);
}

//TODO: Make my own function hook.
void SetupLimitRemover()
{
    Memory::EmplaceNOP(reinterpret_cast<void*>(0x140753BB1), true, 5);
    Memory::EmplaceNOP(reinterpret_cast<void*>(0x140753BB6), true, 2);
}

void SetupDetours()
{
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());

    DetourAttach(&(PVOID&)Cards::orig_getInternalCardID, Cards::Get_InternalID);
    DetourAttach(&(PVOID&)Cards::orig_getKonamiCardID, Cards::Get_KonamiID);
    DetourAttach(&(PVOID&)Cards::orig_getCardProps, Cards::Get_CardPropsByInternalID);
    DetourAttach(&(PVOID&)Cards::orig_getCardIllustration, Cards::Get_CardIllustration);
    DetourAttach(&(PVOID&)Cards::orig_getUnlockedTrunkCards, Cards::Get_UnlockedTrunkCards);
    DetourAttach(&(PVOID&)Cards::orig_getCardImageSlot, Cards::Get_CardImageSlot);
    DetourAttach(&(PVOID&)Cards::orig_getCardIllustrationCacheState, Cards::Get_CardIllustrationCacheState);
    DetourAttach(&(PVOID&)Cards::orig_setupImageTable, Cards::Setup_ImageTable); // NEW - replaces the crashing SetupImageTablePatch byte patch
    DetourTransactionCommit();
}

void ApplyIllustrationVisibilityPatches()
{
    // Ported directly from the reference module's card.ixx (GetIllustrationPatch1/2,
    // LoadIllustrationsPatch1-14, LoadImagePatch1-5). These widen the hardcoded
    // vanilla-range bound checks INSIDE the illustration-loading machinery that
    // sits upstream of 0x14086D050 (our Get_CardIllustration hook).
    //
    // Root cause this fixes: Get_CardIllustration is correctly hooked and its logic
    // is correct, but nothing ever reaches it for custom-range ids, because the
    // original code around 0x14086Cxxx/0x1407523xx/0x140752xxx still validates ids
    // against the vanilla ~0x3A79/10166 range before ever calling into the hook.
    // These are byte patches to the ORIGINAL machine code (same pattern as
    // ApplyExtraCardVisibilityPatches above), not hooks - some of them raise a
    // literal 0xFFFF bound, others NOP out now-dead vanilla-only branches.
    //
    // NOTE: SetupImageTablePatch (0x14075228A) was REMOVED from this function -
    // applying it as a raw byte patch crashed at 0x1407522A0 because it widened
    // a loop bound against a fixed-size allocation (sub_1407521E0 allocates only
    // ~10220 entries' worth of memory, not 0xFFFF). That's now handled correctly
    // via the Setup_ImageTable() hook in Cards.cpp/SetupDetours() instead - see
    // the comment on Cards::orig_setupImageTable in Cards.h.

    // --- GetIllustrationPatch1/2 (0x14086D057, 0x14086D073) ---
    static const uint8_t gi1[] = { 0x41, 0x8D, 0x02, 0x90, 0x90, 0x90, 0x90, 0x3D, 0xFF, 0xFF, 0x00, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086D057), gi1, sizeof(gi1), true);

    static const uint8_t gi2[] = { 0x46, 0x0F, 0xB6, 0x8C, 0x50, 0x20, 0x00, 0x08, 0x00, 0x4B, 0x8D, 0x04, 0x91, 0x0F, 0xBF, 0x54, 0x41, 0x28, 0x90, 0x90, 0x90 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086D073), gi2, sizeof(gi2), true);

    // --- LoadIllustrationsPatch1-14 ---
    static const uint8_t li1[] = { 0xB9, 0x24, 0x00, 0x0A, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x1407523A9), li1, sizeof(li1), true);

    static const uint8_t li2[] = { 0x48, 0x8D, 0x97, 0x21, 0x00, 0x08, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CDA5), li2, sizeof(li2), true);

    static const uint8_t li3[] = { 0x81, 0xF9, 0xFF, 0xFF, 0x00, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CDD3), li3, sizeof(li3), true);

    static const uint8_t li4[] = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x3D, 0xFF, 0xFF, 0x00, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CEE7), li4, sizeof(li4), true);

    static const uint8_t li5[] = { 0x0F, 0xB6, 0x84, 0x4F, 0x20, 0x00, 0x08, 0x00, 0x0F, 0xAB, 0xF0, 0x88, 0x84, 0x4F, 0x20, 0x00, 0x08, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CEFF), li5, sizeof(li5), true);

    static const uint8_t li6[] = { 0x48, 0x63, 0xF3, 0x0F, 0xB6, 0xAC, 0x77, 0x20, 0x00, 0x08, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CF78), li6, sizeof(li6), true);

    static const uint8_t li7[] = { 0xC6, 0x84, 0x77, 0x20, 0x00, 0x08, 0x00, 0x03 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CFA4), li7, sizeof(li7), true);

    static const uint8_t li8[] = { 0xC6, 0x84, 0x77, 0x20, 0x00, 0x08, 0x00, 0x02 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CFC5), li8, sizeof(li8), true);

    static const uint8_t li9[] = { 0xC6, 0x84, 0x77, 0x20, 0x00, 0x08, 0x00, 0x01 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CFD4), li9, sizeof(li9), true);

    static const uint8_t li10[] = { 0xC6, 0x84, 0x77, 0x20, 0x00, 0x08, 0x00, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CFE3), li10, sizeof(li10), true);

    static const uint8_t li11[] = { 0xC6, 0x84, 0x77, 0x20, 0x00, 0x08, 0x00, 0x01 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CFF0), li11, sizeof(li11), true);

    static const uint8_t li12[] = { 0xC6, 0x84, 0x77, 0x20, 0x00, 0x08, 0x00, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CFFF), li12, sizeof(li12), true);

    static const uint8_t li13[] = { 0x41, 0x81, 0xFE, 0xFF, 0xFF, 0x00, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086D00A), li13, sizeof(li13), true);

    static const uint8_t li14[] = { 0x33, 0xDB, 0x90, 0x90, 0x90 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x14086CF70), li14, sizeof(li14), true);

    // --- LoadImagePatch1-5 ---
    static const uint8_t lim1[] = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x4C, 0x8B, 0x3D, 0x72, 0x51, 0x0F, 0x02, 0x4D, 0x8D, 0x3C, 0xC7 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x140752D05), lim1, sizeof(lim1), true);

    static const uint8_t lim2[] = { 0x8B, 0xC0, 0x48, 0x8B, 0x0D, 0x4A, 0x54, 0x0F, 0x02, 0x48, 0x83, 0xC1, 0x04, 0x90, 0x90, 0x90 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x140752A31), lim2, sizeof(lim2), true);

    static const uint8_t lim3[] = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0xB8, 0x78, 0x3A, 0x00, 0x00, 0x66, 0x3B, 0xE8, 0x90, 0x90, 0x90, 0x90, 0x90, 0x90 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x140752CE4), lim3, sizeof(lim3), true);

    static const uint8_t lim4[] = { 0x90, 0x90, 0xE8, 0xD2, 0xBB, 0x01, 0x00, 0x3D, 0xB6, 0x27, 0x00, 0x00, 0x48, 0x8B, 0x0D, 0x4A, 0x5A, 0x0F, 0x02, 0x90, 0x48, 0x8D, 0x0C, 0xC1 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x140752427), lim4, sizeof(lim4), true);

    static const uint8_t lim5[] = { 0x90, 0x90, 0x90, 0x90, 0x90, 0x90, 0x4C, 0x8B, 0x35, 0xE0, 0x3C, 0x0F, 0x02, 0x4D, 0x8D, 0x34, 0xC6 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x140754197), lim5, sizeof(lim5), true);
}

void ApplyExtraCardVisibilityPatches()
{
    // Ported from the reference module - same binary/addresses as confirmed
    // by matching function addrs in our own hooks. Raises the hardcoded
    // "max known card ID" bound in the trunk's unlocked-card scan from the
    // vanilla ~0x3A79 up to 0xFFFF so IDs >= 0x5000 actually get visited.

    static const uint8_t p1[] = { 0x81, 0xFB, 0xFF, 0xFF, 0x00, 0x00 }; // cmp ebx, 0xFFFF
    Memory::PatchBytes(reinterpret_cast<void*>(0x1408C0061), p1, sizeof(p1), true);

    static const uint8_t p2[] = { 0x42, 0x89, 0x14, 0x80, 0x90, 0x90, 0x90, 0x90 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x1408C0025), p2, sizeof(p2), true);

    static const uint8_t p3[] = { 0xBB, 0xFF, 0xFF, 0x00, 0x00 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x1408BFF49), p3, sizeof(p3), true);

    static const uint8_t p4[] = { 0x90, 0x90 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x1408BEFCD), p4, sizeof(p4), true);

    static const uint8_t p5[] = { 0x4C, 0x63, 0x04, 0x90, 0x90, 0x90, 0x90, 0x90 };
    Memory::PatchBytes(reinterpret_cast<void*>(0x1408BEFD9), p5, sizeof(p5), true);
}

typedef char(__fastcall* sub_14076BFC0)(const void** a1, int Lang);
uintptr_t Setup_Information = 0x14076BFC0;
char __fastcall _Setup_Information(const void** a1, int Lang)
{
    //Call the original function
    ((sub_14076BFC0)Setup_Information)(a1, Lang);

    memcpy(Cards::CardIDs.data(), reinterpret_cast<void*>(KONAMI_CARD_ID_LOCATION), 10168 * sizeof(unsigned __int16));
    Logger::WriteLog(std::format("Setup {} Card IDs, At: {}", Cards::CardIDs.size(), reinterpret_cast<void*>(Cards::CardIDs.data())), MODULE_NAME, 0);
    memcpy(Cards::InternalIDs.data(), reinterpret_cast<void*>(INTERNAL_CARD_ID_LOCATION), 11072 * sizeof(unsigned __int16));
    Logger::WriteLog(std::format("Setup {} Internal IDs, At: {}", Cards::InternalIDs.size(), reinterpret_cast<void*>(Cards::InternalIDs.data())), MODULE_NAME, 0);
    memcpy(Cards::CardProps.data(), reinterpret_cast<void*>(IN_MEMORY_CARD_PROP_LOCATION), 10166 * sizeof(Cards::IN_MEMORY_CARD_PROP));
    Logger::WriteLog(std::format("Setup {} Card Props, At: {}", Cards::CardProps.size(), reinterpret_cast<void*>(Cards::CardProps.data())), MODULE_NAME, 0);

    Logger::WriteLog("Wiping Original Memory Locations for Internal and KonamiCard IDs.", MODULE_NAME, 0);
    memset(reinterpret_cast<void*>(KONAMI_CARD_ID_LOCATION), 0, 10168);
    memset(reinterpret_cast<void*>(INTERNAL_CARD_ID_LOCATION), 0, 11072);

    Logger::WriteLog("Clearing Memory CardPROPS", MODULE_NAME, 0);
    memset(reinterpret_cast<void*>(IN_MEMORY_CARD_PROP_LOCATION), 0, 10166);

    Cards::LoadExtraCardsFromJson("extracards.json");
    Cards::ApplyExtraCards();
    Cards::WriteExtraCardNames();

    Logger::WriteLog("Writing Jump Calls For Internal IDs Graveyard", MODULE_NAME, 0);
    SetupInternalIDJumpCalls();
    Logger::WriteLog("Seting Redirection For Internal IDs", MODULE_NAME, 0);
    SetupRedirectionForInternalIDs();

    Logger::WriteLog("Writing Jump Calls For Card IDs Graveyard", MODULE_NAME, 0);
    SetupCardIDJumpCalls();
    Logger::WriteLog("Setup Card IDs Jump Calls", MODULE_NAME, 0);
    SetupRedirectionForCardIDs();

    Logger::WriteLog("Adding New Cards", MODULE_NAME, 0);
    ApplyExtraCardVisibilityPatches();
    ApplyIllustrationVisibilityPatches();
    SetupLimitRemover();

    SetupDetours();

    return 0;
}

//__int64 __fastcall sub_140753BA0(void *a1, Cards KonamiID)
BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        DetourRestoreAfterWith();

        Logger::SetupLogger();
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());

        DetourAttach(&(PVOID&)Setup_Information, _Setup_Information);
        DetourTransactionCommit();

        Logger::WriteLog("It's Time To Du-Du-Du-Duel!.", MODULE_NAME, 0);

        break;
    }
    return TRUE;
}
