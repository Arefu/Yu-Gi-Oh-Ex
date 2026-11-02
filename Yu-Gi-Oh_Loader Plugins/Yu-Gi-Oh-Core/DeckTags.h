#pragma once
#include <cstdint>
#include <string>

// Tags on user decks, and filtering the deck editor's deck list by them, all on the game's own card filter panel (docs/UserDecks.md).
//
// A deck's tags are what the panel can pick: card frame, Attribute, Monster Type, genre and its other icon rows (bits per row), plus the panel's Name
// row as free text, comma separated (there is no archetype or era row). They live in the save extension "DeckTags", so they follow save slots and the vanilla game never sees them.
//
// Deck Options gets "Edit Tags" and "Filter Decks". Each opens widget_TrunkFilterCategories (the deck editor's state 6) with g_TrunkFilterState swapped
// for the deck's tags or the list's filter. Picks apply as they are made and show live at the far right (the deck's info panel, or the deck list itself);
// the help bar's Done (Back) leaves, and the player's card filter goes back. Number rows (Level, ATK...) are tags too: a deck's values are tested with the filter's.
//
// A deck passes the filter when, for every row in use, it has at least one of the row's picks, and has a text tag containing one of the
// filter's words. The filter lasts until the editor is left. Matching decks and the first empty slot stay listed, so a new deck can still be made and
// Deck Options can still be opened when nothing matches.
//
// Traced in the IDB 2026-11-02; every hook is a Detour on a function entry, table in docs/UserDecks.md.
namespace DeckTags
{
    // The deck's free-text tags as "a, b, c", or empty: none, not a user deck, or the slot holds no deck.
    std::wstring Get(unsigned deckId);
    // Replaces the free-text tags, keeping its picks. False when deckId is not a user deck or its slot holds no deck.
    bool Set(unsigned deckId, const std::wstring& tags);
    // The options picked on a row of the filter panel, one bit per option (row 0 is the text). 0 when none.
    uint64_t GetMask(unsigned deckId, int row);

    // The deck editor's slot list: drops what the filter hides. Returns the new count. From UserDecks' hook on Deck_ListUserSlots.
    size_t FilterSlots(int* ids, size_t count);

    // A save was loaded (read the extension) or a new profile made. From UserDecks' hook on GetOrInitProfileBuffer.
    void OnProfile(bool loaded);

    void Install();
}
