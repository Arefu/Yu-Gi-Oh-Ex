#pragma once
#include <cstdint>

// What the Card Store charges, in DP. The settings are read from Config.ini [Yu-Gi-Oh-BetterCardShop] on every call, so a change on the
// plugin settings screen applies straight away. docs/CardStore.md has the research behind the formulas.
//
//   Yu-Gi-Oh-Ex\prices.json      fixed prices for single cards, by Konami id; they beat everything below:
//                                  { "cards": [ { "id": 4007, "price": 5000, "password": 2500 } ] }   (either price may be left out)
//   BetterShop-PasswordCost      Enter Password: one price for any card (default 1000).
//   BetterShop-MonsterPrice      Card Shop, monsters:
//                                  attack  - ATK x 1.8 / 5 (Blue-Eyes 1,080 DP)
//                                  classic - Forbidden Memories' curve: doubles every ~590 ATK+DEF (fitted to its star chip prices)
//                                  custom  - BetterShop-MonsterFormula
//                                  flat    - BetterShop-Cost
//   BetterShop-MonsterFormula    e.g. "ATK * 1.8 / 5"; names ATK, DEF, LEVEL, RARE, LINK; + - * / ^ ( ); min max exp log sqrt round floor ceil
//   BetterShop-SpellTrapPrice    Card Shop, spells and traps:
//                                  rarity  - what a typical monster of the same pack rarity costs (median, by the monster rule)
//                                  custom  - BetterShop-SpellTrapFormula
//                                  flat    - BetterShop-Cost
//   BetterShop-SpellTrapFormula  e.g. "500 + 500 * RARE"
//   BetterShop-Cost              the flat price (default 500).
// Monsters whose ATK is "?" are priced like spells and traps by rarity (their worth is in their effect). Formula prices are rounded to 10 DP
// and kept between 100 and 999,999 (the classic curve stops at 10,000).
namespace Prices
{
    uint64_t PasswordCost(uint16_t CardId);
    uint64_t CardPrice(uint16_t CardId);

    // Forgets the rarities, medians and prices.json, so they are read again next time (the Card Store pages call it when they open).
    void Reset();
}
