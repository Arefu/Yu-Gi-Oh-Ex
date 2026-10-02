#pragma once

// Tag duels through the game's own tag mode: YGO::DUEL::Set_IsTagDuel(true) right before YGO::DUEL::Engine_Init, plus the
// partner decks the game's setup never hands out for a forced tag duel (see TagDuel.cpp and docs/MultiplayerSystem.md "Tag
// duel"). Seats pair by parity: 0 & 2 are one team, 1 & 3 the other; the engine keeps each team's active duelist in the normal
// PlayerState block and parks the partner's hand/deck, swapping them at every turn change.
//
// Config.ini [Yu-Gi-Oh-TagDuel]:
//   DeckMode=separate (default) - each partner has their own hand and deck; shared - partners play one hand and deck (no swap).
//   PartnerDeck=path to a .ydc for your partner (empty = a copy of your deck); OpponentPartnerDeck= the same for the other team.
//   Relative paths are from the game folder.
namespace TagDuel
{
    void Setup();

    // Persistent until changed - NOT a one-shot. Every duel launched (from any source: the Solo Duel button, an MP
    // lobby, anything else that reaches Engine_Init) is a tag duel while this is true.
    void SetEnabled(bool on);
    bool IsEnabled();
}
