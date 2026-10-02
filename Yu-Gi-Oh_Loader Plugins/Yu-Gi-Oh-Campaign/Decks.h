#pragma once

// The decks, from Yu-Gi-Oh-Ex/decks.json (written by WolfEx's Decks tab, docs/Decks.md):
//
//   { "decks": [ { "id": 546, "file": "my_deck", "series": 0, "character": 192, "signatureCard": 4007, "sku": 1, "unlocked": true,
//                  "title": { "E": "My Deck" }, "cards": { "main": [4007, ...], "extra": [], "side": [] } } ] }
//
// The game:
// - LoadDeckDataFile (0x1407FE500, LoadLanguageContent: start and language change) fills g_DeckRecords[700] (DeckRecord 0x88) from
//   main/deckdata_#.bin. Hooked: a listed id the game has is changed (the fields the entry gives), a free one becomes a new deck. It runs
//   before LoadStoryDuelData and AssignDeckRoles, so story duels and characters can use the new decks.
// - LoadDeckTemplatesFromDecksZib (0x1407BD2E0, once at content load) reads each deck's cards from decks.zib/<file>.ydc into
//   DeckTemplateList[700]. Hooked: an entry with "cards" fills its template (Konami ids; at most 60 / 15 / 15).
// - RebuildLiveUnlockCounts (0x1407BB160, after loading a profile, opening packs and duels). Hooked: "unlocked" new decks get their
//   state in the save set (player section +0x38 + 4 * id = 1, as winning them does).
namespace Decks
{
    // Reads decks.json and attaches the hooks; call inside the plugin's Detours transaction.
    void Attach();

    // Applies to what the game has already loaded (if it has): call after the transaction.
    void ApplyIfLoaded();
}
