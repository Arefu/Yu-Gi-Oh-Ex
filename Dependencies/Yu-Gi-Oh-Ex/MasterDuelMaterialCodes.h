#pragma once

namespace YGO::Effects
{
    // Fusion / Link material codes (docs/EffectSystem.md section 36: a recipe slot is a card id or a code) in Master Duel's duel engine
    // (docs/MasterDuelEngine.md). Master Duel added 7 codes after the races and 3 more before the archetypes, so its codes are the game's
    // renumbered: 1-25 (races) the same, 26-97 (attributes, Levels, kind letters) + 7, 98 and up (archetype = code - 98) + 10, card ids
    // (3000+) the same. Checked 2026-10-31 over the 316 Fusions both engines' recipe tables share (157 rows identical, the other 159 differ
    // only by this) and the Link rule table (0x49 -> 0x50).
    //
    // Only for the game's own codes. Yu-Gi-Oh-Effects' custom codes (517-2999: custom archetypes, Ritual, Illusion, any monster, combined
    // materials; answered by its matcher hook) are NOT translated: under Master Duel's engine 527 and up are real archetype codes, so the
    // custom range must move before Fusion.cpp runs there (not done yet).
    constexpr int kGameCustomCodeFirst = 517;   // the first code the game's matcher does not know
    constexpr int kCardIdFirst = 3000;

    constexpr int ToMasterDuelMaterialCode(int gameCode)
    {
        if (gameCode >= kCardIdFirst || gameCode <= 25)
            return gameCode;            // card ids, races, 0 (no material) and negatives
        if (gameCode <= 97)
            return gameCode + 7;        // attributes 26-31, Levels 32-67, kind letters 68-97
        if (gameCode < kGameCustomCodeFirst)
            return gameCode + 10;       // archetypes (code - 98)
        return gameCode;                // the plugin's own codes: see above
    }

    static_assert(ToMasterDuelMaterialCode(26) == 33, "LIGHT");
    static_assert(ToMasterDuelMaterialCode(97) == 104, "the last kind letter");
    static_assert(ToMasterDuelMaterialCode(107) == 117, "an archetype");
    static_assert(ToMasterDuelMaterialCode(1) == 1, "a race");
    static_assert(ToMasterDuelMaterialCode(4007) == 4007, "a card id");
}
