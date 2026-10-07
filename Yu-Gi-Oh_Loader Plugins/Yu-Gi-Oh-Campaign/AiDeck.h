#pragma once

// Opponent decks: the opponent keeps their portrait and name but plays a deck you choose, in the campaign and in Free Duel.
//
// Campaign: the deck menu's items read "Story Deck" (the story's decks for both sides) and "Custom Decks". Custom Decks opens "Your Deck",
// and confirming it opens "Opponent's Deck" before the duel starts (Screen_GotoDuel is held until then). Free Duel: after "Your Deck" the
// same "Opponent's Deck" step comes. That step lists every deck the game has, the opponent's own first; Duel starts, Back returns to your
// deck. The last pick per opponent is where the list starts next time (until the game closes).
//
// The deck is swapped where the duel copies it in: Duel_SetupSeat (0x14076A7B0) for seat 1, only for the duel it was picked for, offline,
// not tutorial / battle pack / challenge. See docs/Widgets.md (DeckSelectList) and YuGiOh-RIX.h.
namespace AiDeck
{
    // Attaches the hooks (and relabels the campaign menu); call inside the plugin's Detours transaction.
    void Attach();
}
