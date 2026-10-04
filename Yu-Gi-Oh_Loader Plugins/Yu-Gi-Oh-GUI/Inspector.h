#pragma once

// The main "Yu-Gi-Oh!" ImGui window: a read-only look at the game's memory while it runs (players, duel state, the game's
// globals and tables, a memory viewer). Changing the game at runtime is Yu-Gi-Oh-Funky's job, not this window's.
namespace Inspector
{
    void Draw(bool* open);
}
