#pragma once

// The duel played on Master Duel's engine (docs/MasterDuelEngine.md, "Redirect design"): duel start, the tick, the state the duel screen reads
// and the engine calls it makes go to duel.dll. Installed by MasterDuel::Install once duel.dll has its work memory.
namespace MasterDuelBridge
{
    bool Install(void* workMemory);

    // The current duel runs on duel.dll.
    bool Active();
}
