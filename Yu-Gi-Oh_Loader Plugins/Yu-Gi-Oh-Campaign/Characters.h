#pragma once

// The duelists, from Yu-Gi-Oh-Ex/characters.json (written by WolfEx's Characters tab, docs/Characters.md):
//
//   { "characters": [ { "id": 192, "key": "yugimuto", "series": 0, "deck": 5, "selectable": 1, "sku": 1, "arena": 2, "unlocked": true,
//                       "name": { "E": "My Duelist" }, "bio": { "E": "..." } } ] }
//
// The game: YGO::GAME::LoadCharacterData (0x1407FED60, called by LoadLanguageContent at start and on a language change) fills
// g_CharacterRecords[240] (0x68 each) from main/chardata_#.bin; 191 are used. A listed id the game has is changed (the fields the entry
// gives), a free one becomes a new character. key = portrait ("<key>_neutral" in pdui/chars: an existing character's key borrows their
// picture). Free Duel lists a character only when it is selectable, in the tab's series, its sku is owned and it owns its deck.
// "unlocked" (default true for new characters) sets the character's bit in the save (player section +0x18, 240 bits) whenever the Free Duel
// list or the collection count is built.
namespace Characters
{
    // Reads characters.json and attaches the hooks; call inside the plugin's Detours transaction.
    void Attach();

    // Applies to what the game has already loaded (if it has): call after the transaction.
    void ApplyIfLoaded();
}
