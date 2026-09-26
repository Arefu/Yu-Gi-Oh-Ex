#include <Windows.h>
#include <cstring>

#include "Card.h"
#include "Detours.h"
#include "Limit.h"
#include "Logger.h"
#include "Save.h"

typedef char(__fastcall* Setup_CardPropTable_t)(const int* a1, int language);
static uintptr_t orig_Setup_CardPropTable = 0x14076BFC0;

char __fastcall Hook_Setup_CardPropTable(const int* a1, int language)
{
    // The game skips the whole setup when the language hasn't changed; the tables are then
    // still the ones patched last time, so there is nothing to copy.
    const bool reloads = *a1 != language;

    char result = reinterpret_cast<Setup_CardPropTable_t>(orig_Setup_CardPropTable)(a1, language);
    if (!reloads)
        return result;

    std::memcpy(Limits::KonamiIDTable, reinterpret_cast<void*>(kKonamiCardIdLocation), kVanillaCardIdCount * sizeof(uint16_t));
    std::memcpy(Limits::InternalIDTable, reinterpret_cast<void*>(kInternalCardIdLocation), kVanillaInternalIdCount * sizeof(uint16_t));

    Limits::ApplyAll();

    Card::CardProps.assign(
        reinterpret_cast<Card::IN_MEMORY_CARD_PROP*>(kInternalCardPropsAddress),
        reinterpret_cast<Card::IN_MEMORY_CARD_PROP*>(kInternalCardPropsAddress) + kVanillaCardPropCount);

    Card::Install();

    return result;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    Logger::SetupLogger();

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    {
        DetourRestoreAfterWith();

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_Setup_CardPropTable, Hook_Setup_CardPropTable);
        DetourTransactionCommit();

        Save::Install();
        break;
    }
    }
    return TRUE;
}
