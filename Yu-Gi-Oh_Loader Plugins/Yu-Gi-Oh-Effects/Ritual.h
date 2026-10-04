#pragma once
#include <cstddef>

// Custom Ritual Monster <-> Ritual Spell pairs.
//
// The game keeps every pair in one read-only table, RitualMonsterSpellTable (0x140AD07C0): 96 rows of {u16 Ritual Monster id, u16 the Ritual
// Spell that summons it (0 = only the generic spells: Advanced Ritual Art, Chaos Form, ...)}. A Ritual Monster that is not in it cannot be
// Ritual Summoned at all, not even by Advanced Ritual Art (Ritual_CanSummonMonsterWithSpell 0x1400692E0 gives up when the monster has no row).
// The table is copied into memory this DLL owns with room for 31 more rows (the readers' bound is an 8 bit compare, 127 at most), the six
// instructions that address it get the new address and the four bounds go from 96 to the new row count (plain operand writes, not hooks).
// Custom rows come from Yu-Gi-Oh-Ex\cards.json:
//   "ritualSpell": id         on a Ritual Monster: the Ritual Spell that summons it
//   "ritualMonsters": [ids]   on a Ritual Spell: the Ritual Monsters it summons
// and every custom Ritual Monster without either gets a row with spell 0, so the generic spells can summon it. The rows hold the ids the cards
// play under in the current duel (a custom card can borrow a vanilla id, docs/EffectSystem.md section 32): they are written again when
// the readers are entered (an entry stub that keeps every register, installed with Detours).
namespace Ritual
{
    // Reads the pairs, moves the table and attaches the entry stubs. Call between DetourTransactionBegin and DetourTransactionCommit.
    void Setup();

    // Number of custom rows in the table.
    size_t Count();
}
