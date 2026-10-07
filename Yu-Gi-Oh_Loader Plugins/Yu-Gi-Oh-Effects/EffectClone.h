#pragma once
#include <cstddef>

// Custom card effects by REUSE: a card in cards.json can name a vanilla card whose effect handlers it borrows, plus the
// few numbers those handlers read from id-keyed data tables:
//
//   "effectClone": { "from": 4844, "draw": 2 }     // behaves like Pot of Greed (id 4844), drawing 2
//
// How the game finds a card's effect (IDA, ygo-effects-moonshot-plan memory): every effect lookup goes through ONE function,
// YGO::Effects::Get_EffectTableEntryForCard (0x1400DFBC0), which binary-searches one of four per-card tables by Konami id
// (spell/trap 0x140BA8730, monster 0x140B5CC20, 0x140B85A50, trigger/continuous 0x140B38290) and returns five function
// pointers. The tables are sorted and their sizes are baked into the code, so instead of growing them this hooks the
// lookup: for a custom card it presents the source card's id to the original function and hands back the source's entry.
// Parameter tables keyed by id are hooked the same way, one at a time (draw count: Get_NumberOfCardsToDraw).
//
// The game's own cards can have their effect changed the same way: Yu-Gi-Oh-Ex\effects.json (WolfX Effects page, "Game cards"),
//   {"cards": [{"id": 4041, "name": "...", "overridden": true, "effectClone": { "from": 4844, "draw": 3 }}]}
// "overridden": true marks the entry as live; without "effectClone" the card has no effect. docs/EffectSystem.md section 43.
namespace EffectClone
{
    void Setup();
    size_t Count();
}
