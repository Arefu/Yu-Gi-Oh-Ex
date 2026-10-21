#pragma once

// The ban list picker: a BOB page (pages\banlists.json, from the Ban Lists mod) that filters the saved lists (BanList::Saved) by era and
// year and applies one. The host's "Ban list" row opens it ("Choose a list..."). Everything shown is the page's; this file only gives
// Yu-Gi-Oh-RIX the pieces it can't know:
//
//   rows "mp.banlists"          one row per list that matches var:banlist.era / var:banlist.year (empty or "all" = any), page
//                               var:banlist.page (0-based) of kPageSize rows; the row's value is its index in BanList::Saved()
//   call("mp.banlist.use")      uses the list whose index is in var:banlist (the rows menu's id)
//   call("mp.banlist.game") / call("mp.banlist.off")
//   {game:mp.banlist}           the list in use ("Game", "Off" or its name)
//   {game:mp.banlist.count}     how many lists match the filters
//   {game:mp.banlist.pages}     how many pages of rows they make (at least 1)
//   {game:mp.banlist.pagetext}  "Page 2 of 7"
//
// A filter change runs `set(var:banlist.page, "0"); reload;` so the rows are made again.
namespace BanListPicker
{
    // Registers the list, actions and values with Yu-Gi-Oh-RIX (once; does nothing while RIX isn't loaded). Call on the game's thread.
    void Register();

    // Opens the picker page; false when RIX or the page file is missing.
    bool Open();
}
