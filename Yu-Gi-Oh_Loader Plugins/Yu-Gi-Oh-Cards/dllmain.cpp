#include <Windows.h>
#include "HookDiagnostics.h"
#include <cstring>

#include "BulkPacks.h"
#include "Card.h"
#include "Detours.h"
#include "Genres.h"
#include "Limit.h"
#include "Logger.h"
#include "Related.h"
#include "Save.h"
#include "Shiny.h"
#include "Text.h"
#include "Yu-Gi-Oh-Core.h"

// Card rarities (docs/Shiny.md), for prizes, a shop or a trade page: rarity ids are Rarity.h's (1 = Rare .. 57 built in, 128-255 from rarities.json), per
// Konami id. GetCardRarity = the rarity the card is shown with (the highest tier owned, 0 = none); GetCardRarityCount = copies of one rarity;
// AddCardRarity adds (or with a negative Count removes) copies and returns the new count. They work whether or not Shiny is on. Find them with
// GetProcAddress on this DLL.
extern "C"
{
    __declspec(dllexport) int __cdecl Cards_GetCardRarity(int CardId) { return CardId >= 0 && CardId <= 0xFFFF ? Shiny::ShownRarity(static_cast<uint16_t>(CardId)) : 0; }

    __declspec(dllexport) int __cdecl Cards_GetCardRarityCount(int CardId, int RarityId)
    {
        return CardId >= 0 && CardId <= 0xFFFF && RarityId > 0 && RarityId <= 0xFF ? Shiny::Count(static_cast<uint16_t>(CardId), static_cast<uint8_t>(RarityId)) : 0;
    }

    __declspec(dllexport) int __cdecl Cards_AddCardRarity(int CardId, int RarityId, int Count)
    {
        return CardId >= 0 && CardId <= 0xFFFF && RarityId > 0 && RarityId <= 0xFF ? Shiny::Add(static_cast<uint16_t>(CardId), static_cast<uint8_t>(RarityId), Count) : 0;
    }
}

typedef char(__fastcall* Setup_CardPropTable_t)(const int* a1, int language);
static uintptr_t orig_Setup_CardPropTable = 0x14076BFC0;

// CardSearch_BuildIndex (0x14076DD60): the deck editor's name/text search index. For every Konami id 0..0xFFFF it copies
// FULL_CARD_PROPS Name + Description, case folded, into static buffers; the trunk filter (CardSearch_MatchesText, 0x1407BBD70)
// wcsstr's those copies, never the live names. Setup_CardPropTable calls it before returning - before Card::Install - so custom
// cards (and renamed game cards) were indexed empty. Safe to re-run: it only rewrites its own buffers.
typedef void(__fastcall* CardSearch_BuildIndex_t)();
static const auto CardSearch_BuildIndex = reinterpret_cast<CardSearch_BuildIndex_t>(0x14076DD60);

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

    Genres::Load();          // before Install: WriteGameTableEntry gives custom cards their genres
    Card::Install();
    Genres::ApplyToGameCards();
    Text::Load();
    Related::Load();

    CardSearch_BuildIndex(); // after Install: the game built it from the vanilla names

    // Master Duel's duel engine, when Core runs it (docs/MasterDuelEngine.md): its card data again, now with this plugin's cards in the
    // game's lookups (Core's own refresh ran inside the game's setup, before Install)
    if (Core::Load() && Core::Functions().EngineRefreshCards && Core::Functions().EngineReady && Core::Functions().EngineReady())
        Core::Functions().EngineRefreshCards();

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

        // Shiny cards (Shiny.h; Shiny=1), then buying many packs (BulkPacks.h). This order matters: both detour OpenPackAndGrantCards and Shiny's must be the
        // inner one, so the bulk job's per-pack calls get their rarities rolled too. Core was loaded first, so Save, the card table and mod settings are up.
        Shiny::Install();
        BulkPacks::Install();

        if (!HookDiagnostics::Begin("Card tables, text and related cards", MODULE_NAME, Logger::WriteLog))
            break;
        DetourAttach(&(PVOID&)orig_Setup_CardPropTable, Hook_Setup_CardPropTable);
        Text::Attach();
        Related::Attach();
        HookDiagnostics::Commit("Card tables, text and related cards", MODULE_NAME, Logger::WriteLog);
        break;
    }
    }
    return TRUE;
}
