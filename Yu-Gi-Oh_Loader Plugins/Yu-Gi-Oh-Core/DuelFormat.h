#pragma once

// Speed Duel / Rush Duel, a Core rule switch (2026-10-22), [Yu-Gi-Oh-Core] in Config.ini:
//   DuelFormat=master (default) | speed | rush
//
// The duel engine has Konami's own Speed and Rush rules built in, keyed on one value: Duel::PlayerState +0x37A4 (0x14349B3E4, IDB type
// DuelFormat: 0 Master, 1 Speed, 2 Rush). DuelSetup_ClearState zeroes it at the start of every duel and nothing in the exe ever writes it,
// so on PC the rules are dormant. Core writes it right after ClearState, before DuelSetup_InitEngine reads it (docs/DuelFormat.md has
// everything the ~97 readers do). What the engine then does by itself:
//   Speed: 4000 LP, 4-card opening hand, 3 monster + 3 spell/trap zones (and the field zone), no draw on the first turn,
//          no Main Phase 2, a draw at turn 50 (90 in tag duels).
//   Rush:  8000 LP, 4-card opening hand, draw until you hold 5 (also on the first turn), unlimited Normal Summons, 3 monster +
//          3 spell/trap zones with no Extra Monster Zones, no Main Phase 2, chains of one link, a draw at turn 50.
// Not done by the engine: the duel field still draws 5 zones (only the middle 3 can be used), Speed Duel Skills, Rush Maximum monsters.
//
// The starting LP: the duel UI writes g_iStartLifePoints (0x140C8D370, 8000 from Setup_RuleAndLP) over the engine's LP when the duel
// starts, so Speed also sets that to 4000 when it is still the default 8000 (a different value set by another plugin is kept).
// Online duels use their own value, Core_SetOnlineDuelFormat (Yu-Gi-Oh-MP sends the host's lobby choice to every player): both players'
// engines run in lockstep and would desync if their formats differed. Master until a plugin sets it; the starting LP online stays the
// lobby's LP setting.
namespace DuelFormat
{
    void Install();

    // Core_SetOnlineDuelFormat: 0 Master, 1 Speed, 2 Rush.
    void SetOnline(int format);
}
