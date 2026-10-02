#pragma once
#include <cstdint>

// Card genres from Yu-Gi-Oh-Ex/genres.json (written by WolfEx's Card genres tab, docs/CardGenre.md):
//
//   { "cards": [ { "card": 15300, "genres": [ "DRAW", "SPSUMMON", "LINK" ] } ] }
//
// Each listed card gets exactly those genres (keys, the game's names like "Help Draw", or bit numbers). The game keeps a card's genres in
// FULL_CARD_PROPS +0x38 (from bin/CARD_Genre.bin via Get_GenreFromKonamiId, which custom cards fall outside of, so they had none); the
// card details page lists them and the duel code (AI) checks them with Has_CardGenre. Custom cards get theirs in WriteGameTableEntry
// (so a borrowed duel id carries them too), game cards are patched after the table is built. Bits 39-45 are cleared as the game does.
namespace Genres
{
    // Reads genres.json (again). Call before Card::Install on every card setup.
    void Load();

    // The genres genres.json gives this Konami id, or `fallback` if it doesn't list it.
    uint64_t MaskFor(int konamiId, uint64_t fallback);

    // Writes the listed game cards' (ids below the custom range) genres into FULL_CARD_PROPS. Call after Card::Install.
    void ApplyToGameCards();
}
