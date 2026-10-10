#pragma once

// Continuous locks like Jinzo's ("Trap Cards, and their effects on the field, cannot be activated. Negate all Trap effects on the field.").
//
// The game has no table for them (docs/EffectSystem.md section 44): Duel_BuildContinuousLockState (0x14010EAA0) fills a lock-state struct
// from hard-coded card ids every time the engine needs it (6 callers: activation checks, resolution, the AI). Among its ints:
//   +0 / +4    player 0 / 1's Traps are negated and can't be activated: 1 = all of them, a larger value = only those activated after that
//              activation number (Royal Decree). Jinzo 4758 face-up in a Monster Zone sets both, or only the opponent's with Amplifier 4819.
//   +12 / +16  player 0 / 1's Spells, the same way (Spell Canceller 5594, Silent Swordsman LV7 6166).
// A card counts only while it is face-up, its effects aren't negated (sub_14010E0E0 on the finished struct) and it isn't marked disabled.
//
// This hooks the builder and adds every card with a "lock" (cards.json for new cards, effects.json "overridden" game cards):
//   +20/+24, +28/+32  player 0 / 1's monster effects negated (Skill Drain 5740; read by sub_1401103A0, which sets the zone's negated bit)
//   +88 / +92 / +96 / +100  Continuous Traps / Continuous Spells / Field Spells / Equip Spells can't be activated (both players; read by the
//              can't-activate check sub_140111690)
//   +112 / +116 player 0 / 1 may activate anyway (exemption); +52..+84 narrower locks (Extra Deck monsters, DARK, counters...) not exposed.
//   "lock": { "traps": true, "spells": false, "monsters": false, "continuousTraps": false, "continuousSpells": false, "fieldSpells": false,
//             "equipSpells": false, "side": "both" | "opponent" | "you", "zone": "monster" | "spelltrap" | "any" }
// side is relative to the card's controller (traps / spells / monsters); zone is where the card must be (Jinzo: monster).
namespace Lock
{
    // Reads the cards' locks and attaches the hook when there is one. Call between DetourTransactionBegin and DetourTransactionCommit.
    void Setup();
}
