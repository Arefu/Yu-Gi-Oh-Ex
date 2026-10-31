#pragma once
#include <Windows.h>
#include <cstdint>
#include <vector>

// Master Duel's duel engine (docs/MasterDuelEngine.md). duel.dll is the game's own duel engine, extended by Konami for Master Duel: the same
// effect rows and machines, the same message queue, the same card ids, with every card released since. Core loads it from
// Plugins\Yu-Gi-Oh-Core\duel.dll when [Yu-Gi-Oh-Core] MasterDuelEngine=1, gives it its work memory and the game's card data (the cards
// Yu-Gi-Oh-MoreCards adds included: it calls Core_EngineRefreshCards once its cards are in), and lets other plugins reach its exports.
namespace MasterDuel
{
    bool Install();

    // Loaded and its card data set.
    bool Ready();

    // The engine's export of that name ("DLL_DuelSysAct" ...), or nullptr.
    FARPROC Proc(const char* name);

    // Builds the engine's card data from the game's card tables again (the game's lookups, so every card MoreCards added is in it).
    // Returns how many cards it has.
    int RefreshCards();

    // Card ids between the game and the engine (Core_EngineMapCard / Core_EngineSetDisplayId): the engine id of a game id, and the id the
    // duel screen shows for an engine id (a borrowed duel id for a card past 16383). Unmapped ids are the same in both.
    void MapCard(int gameId, int engineId);
    void SetDisplayId(int engineId, int displayId);
    uint16_t ToEngine(uint16_t gameId);
    uint16_t ToDisplay(uint16_t engineId);
    // a card in a loaded duel deck (which may hold borrowed ids); ClearDisplayIds forgets the last duel's borrowed ids
    uint16_t DeckToEngine(uint16_t gameId);
    void ClearDisplayIds();   // also puts back the records Lend changed

    // Core_EngineLend: for this duel the card shown as displayId plays as engineId, with game card propsGameId's props in engineId's record.
    bool Lend(int displayId, int engineId, int propsGameId);

    // Every card id the engine has a record for, lowest first.
    std::vector<uint16_t> EngineIds();
}
