#pragma once

// New campaign series past the game's 6, from Yu-Gi-Oh-Ex/campaigns.json (Series.cpp, docs/CampaignSeries.md):
//
//   { "series": [ { "id": 6, "name": { "E": "TEST" }, "logo": 1, "unlock": "start" } ] }
//
//   "id"     6-63: the series number; its duels are storyduels.json duels with this "series" (orders 1-48)
//   "logo"   1-6: a game series' logo, or a name: the "logo_<name>" sprite of pdui\SeriesLogo.dfymoo (a new logo added to that sheet)
//   "unlock" "start" (default: open from the start), "never", or { "after": n } = once the LAST duel of series n (0-5 a game series, 6+
//            another new one) is won
//
// The game: the series select screen lists ids 0-5 in 6 fixed panels (CampaignSeriesSelect_*), and the save keeps 6 blocks of 1208 bytes
// (unlock + 24 bytes per duel) at Section_0x1B70+8. Here the list gets every series and scrolls (the 6 panels are a window on it, with
// the campaign duel list's up / down arrows to click), and a new series BORROWS game slot 5 while the cursor is on it: slot 5's save block is set aside and the new series' block
// (kept in the save file through Yu-Gi-Oh-Core's save extension, tag "campaign.series") takes its place, and the slot's duel list, name and logo are the new series'. Leaving puts
// slot 5 back. Every save write goes through Core's write filter, which puts slot 5's own block back into what is written, so the save file
// only ever holds the game's own data.
namespace Series
{
    // Reads campaigns.json and attaches the hooks; call inside the plugin's Detours transaction.
    void Attach();
}
