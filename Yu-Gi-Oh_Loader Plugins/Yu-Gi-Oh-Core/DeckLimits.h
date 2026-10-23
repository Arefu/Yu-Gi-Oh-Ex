#pragma once

// Deck size limits, a Core limit breaker (2026-10-22), [Yu-Gi-Oh-Core] in Config.ini:
//   MinMainDeck=40   fewest Main Deck cards a deck may have (1 to 60)
//   MaxMainDeck=60   most Main Deck cards (MinMainDeck to 60)
//   MaxExtraDeck=15  most Extra Deck cards (0 to 15)
//   MaxSideDeck=15   most Side Deck cards (0 to 15)
// Speed Duel decks are 20-30 main / 0-5 extra.
//
// How the game does it (traced in the IDB 2026-10-22, docs/DeckLimits.md):
//   * YGO::GAME::Deck_ValidateAndCountByInternalId (0x1407F9B60) is the only legality check. It sets DeckValidation flag 4 when the main
//     count is < 40 (imm32 at 0x1407F9CDF) or > 60 (imm8 at 0x1407F9CFB); extra and side have no minimum. Every caller (deck select list,
//     Live lobby, save revalidation) reads its flags, so patching the two immediates changes the rule everywhere.
//   * g_DeckSectionMaxSizes (0x140A521D8, int[3] main/extra/side) is the deck editor's "section full" cap.
//   * A DeckListItem stores at most 60/15/15 cards, so the limits can only go down.
//   * The numbers in the game's text ("may not have more than 60 cards", " Less than 40 cards", string ids 637-639 and 764) are rewritten
//     in the loaded strings, in every language that writes them with ASCII digits.
// While the game runs, Core_SetDeckLimits (Yu-Gi-Oh-Core.h) sets other limits (Yu-Gi-Oh-MP's lobby rules) or puts Config.ini's back.
namespace DeckLimits
{
    void Install();

    // Core_SetDeckLimits: a negative minMain = Config.ini's limits again.
    void Set(int minMain, int maxMain, int maxExtra, int maxSide);
}
