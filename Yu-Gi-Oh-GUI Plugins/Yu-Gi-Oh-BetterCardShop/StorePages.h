#pragma once

// The Card Store, made of Yu-Gi-Oh-RIX pages on the Battle Pack screen (the main menu's Card Shop button opens it):
//   Card Store      - Booster Packs (the game's own card shop), Card Shop, Enter Password
//   Card Shop       - every card in the game in the deck editor's card trunk, with the card details panel; picking one offers a copy at
//                     its price (Prices.h: prices.json, else by ATK, the classic curve, a custom formula, rarity or flat)
//   Enter Password  - eight digit wheels and an Unlock button: a real card's password (the game's bin\CARD_Pass.bin) gives three copies
//                     of it for the password price (1,000 DP by default; prices.json can set one per card)
namespace StorePages
{
    // Makes the main menu's Card Shop button open the Card Store. False when Yu-Gi-Oh-RIX is not loaded.
    bool Install();
}
