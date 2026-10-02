#pragma once

// The campaign's story duels, from Yu-Gi-Oh-Ex/storyduels.json (written by WolfEx's Story duels tab, docs/StoryDuels.md):
//
//   { "duels": [ { "id": 186, "series": 0, "order": 33, "key": "MyDuel",
//                  "player": { "character": 104, "deck": 5, "costume": "" }, "opponent": { "character": 94, "deck": 6, "costume": "" },
//                  "arena": 1, "rewardPack": -1, "sku": 1, "title": { "E": "..." }, "description": { "E": "..." }, "tip": { "E": "..." } } ] }
//
// The game: LoadStoryDuelData (0x1407FF6D0, called by LoadLanguageContent at start and on a language change) fills g_StoryDuelRecords[226]
// (0xB0 each) from main/dueldata_#.bin through StoryDuel_FromFileRecord (0x1407FF810); Campaign_BuildSeriesDuelLists (0x14074A3B0) runs
// after it and makes each series' list (index = order -> duel id). A listed id the game has is changed (the fields the entry gives), a free
// one (Id 0 in the table) becomes a new duel. Every entry goes through the game's own StoryDuel_FromFileRecord, so the texts are
// game-allocated wstrings and the decks get marked as this duel's story decks exactly as the game does it.
namespace StoryDuels
{
    // Reads storyduels.json and attaches the hook; call inside the plugin's Detours transaction.
    void Attach();

    // Applies to what the game has already loaded (if it has) and rebuilds the series lists: call after the transaction.
    void ApplyIfLoaded();
}
