#pragma once

// Sets up a duel with chosen cards in the opening hand (and extra deck) so a card can be tested without waiting for the draw.
//
// The game builds each player's duel deck in sub_140082580 (YGO::DUEL::DuelDeck_Load): it copies the deck struct's card ids into the
// engine, then shuffles the order. The detour puts the chosen cards into the deck first when the deck lacks them, lets the game shuffle,
// then moves the chosen cards to the front of the shuffled order, which is what the opening hand is drawn from.
namespace DuelTest
{
    // Reads the saved list from [Yu-Gi-Oh-Funky] in Config.ini and attaches the detour.
    void Install();

    // Attaches the test deck hook OUTSIDE Yu-Gi-Oh-MoreCards' Duel_LoadDeck hook (called on the first frame, after every plugin loaded),
    // so a .ydc test deck is in the deck struct before MoreCards resolves custom ids and reserves vanilla ids for source-id lending.
    void LateInstall();

    // The "Test hand" part of the Debug Tools window.
    void Draw();
}
