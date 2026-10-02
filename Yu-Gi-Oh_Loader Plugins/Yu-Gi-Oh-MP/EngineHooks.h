#pragma once

// Passthrough logging stubs on every multiplayer / duel-flow / stats function mapped so far (docs/MultiplayerSystem.md,
// docs/StatsAndMatchResults.md, see EngineHooks.cpp for the list and Stubs.h for the log format). Nothing here changes
// behaviour except Duel__NetEvent__Send, which also mirrors each event to our server as a P2P::EventReport.
namespace EngineHooks
{
    void Setup();

    // Directly calls the game's own YGO::DUEL::Set_IsDuelMultiplayer(on). The real Multiplayer menu sets it too, so this
    // is only needed to force the MP code paths in an offline duel. Debug-only.
    void DEBUG_SetIsDuelMultiplayer(bool on);
}
