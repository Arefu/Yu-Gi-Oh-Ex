#pragma once

// Duel rules per duelist, from Yu-Gi-Oh-Ex/duelistrules.json (WolfX Characters page, "Duel rules against this duelist"):
//
//   { "duelists": [ { "id": 12, "lp": 4000 } ] }
//
//   "lp"  both players' starting life points in a duel against this duelist (0 / missing = the game's)
//
// The game: every reader of the starting LP goes through YGO::DUEL::Get_StartingLifePoints (0x140768DC0; the duel animation's LP step
// writes it into each side's LP). The opponent is the duel's side 1 character (dword_140C8D1F8[1], set by the campaign / Free Duel setup).
// Online duels keep the game's LP.
namespace DuelistRules
{
    // Reads duelistrules.json and attaches the hook; call inside the plugin's Detours transaction.
    void Attach();
}
