#pragma once
#include <cstddef>

// Synchro and Xyz Summon requirements (docs/EffectSystem.md section 41).
//
// The game keeps them in two sorted, read-only tables that every reader binary-searches by card id:
//   XyzSummonRequirements      0x140ACF2E0, 219 rows of {i16 Xyz Monster, i16 material code, i16 number of materials}
//   SynchroSummonRequirements  0x140AD0BA0, 194 rows of {i16 Synchro Monster, i16 Tuner code, i16 non-Tuner code, i16 materials}
// A card with no row uses the generic rule: Xyz = 2 materials of the card's Rank as Level, Synchro = 1 Tuner + 1 or more non-Tuners. A code is
// the Fusion material code space (0 = any, 1..24 race, 26..31 attribute, 72 Gemini, 73 Normal, 75 Synchro (Synchro only), 83 Pendulum,
// 94 DARK Pendulum, 98..516 archetype, >= 3000 a card (Synchro only), 97 = a per-card rule in the code). The Synchro materials count is the
// Tuner plus the non-Tuners: positive = that many or more, negative = exactly that many.
//
// Both tables are copied into memory this DLL owns with room for every row from cards.json / summoning.json; the instructions that address
// them get the new address and the search bounds the new size (operand writes, checked first). Unused rows hold id 0x7FFF, which sorts last
// and never matches. The rows are rebuilt with the ids the cards play under in the current duel when a reader is entered and those ids changed
// (a custom card that borrows a vanilla id takes that id's row; one without requirements removes it, so it gets the generic rule).
//   "xyz":     {"material": <code or word>, "materials": 3}
//   "synchro": {"tuner": <code, word or card id>, "nonTuner": <...>, "materials": 2, "exactly": false}
namespace SynchroXyz
{
    // Reads the requirements, moves the tables and attaches the entry stubs. Call between DetourTransactionBegin and DetourTransactionCommit.
    void Setup();
}
