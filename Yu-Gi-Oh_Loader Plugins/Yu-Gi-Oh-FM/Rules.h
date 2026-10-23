#pragma once

// Forbidden Memories' summoning rules.
//
// NoTributes ([Yu-Gi-Oh-FM] NoTributes, default 1): any monster is Normal Summoned / Set without Tributes, as in FM. The game's summon
// checks read a hand card's Level through sub_140011C30 (side, card instance: the Level with Star Blast / Level Conversion Lab ...) and
// compare it with 5 and 7 (sub_14012D210, the Summon / Set flags for the card menu, and the summon itself). The hook answers at most 4 for
// a monster in the hand, so no Tribute is asked for; on the field the card has its real Level again. One Normal Summon per turn is the
// game's own rule, the same as FM's.
namespace Rules
{
    // Reads the settings and attaches the hooks; call inside the plugin's Detours transaction.
    void Attach();
}
