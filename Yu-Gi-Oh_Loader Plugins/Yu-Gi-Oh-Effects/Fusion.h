#pragma once
#include <cstddef>

// Custom Fusion recipes.
//
// The game keeps every fusion recipe in two read-only tables (2/3 materials at 0x140ACCAD0 and 4/5 materials at 0x140ACD4D0, both keyed by the
// fusion monster's Konami id) and reads them through three functions, nothing else touches the tables:
//   Get_FusionMaterialCount    (0x140006250)  0 = not a fusion in the tables, 2..5 = number of materials, 7 = Rainbow Overdragon
//   Get_FusionMaterial         (0x1400063E0)  the n-th material of a fusion monster, 0 when there is none
//   Get_FusionsUsingMaterial   (0x140006610)  every fusion monster that lists the material, written to the caller's buffer
// The recipes of custom cards come from the "fusion" array of an entry in Yu-Gi-Oh-Ex\cards.json (2 to 5 materials: card ids or generic
// material codes - race, attribute, Level, kind, archetype, combinations - see docs/EffectSystem.md section 36). The tables are left alone:
// the three functions are detoured, and only fall through to the game's own when a card has no custom recipe. Codes the game's material
// check (0x140004B10) does not know are answered by a fourth detour on it.
namespace Fusion
{
    // Reads the recipes and attaches the detours. Call between DetourTransactionBegin and DetourTransactionCommit.
    void Setup();

    // Number of custom recipes loaded.
    size_t Count();
}
