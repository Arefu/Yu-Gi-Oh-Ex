#pragma once
#include <cstdint>
#include <unordered_set>

// The shop's booster packs, from Yu-Gi-Oh-Ex/packs.json (written by WolfX's New packs page). A Core limit breaker since 2026-10-19 (it was
// in Yu-Gi-Oh-Cards, then Yu-Gi-Oh-BetterCardShop, which is retired): the game's 128 pack slots and 6 shop rows, open to any content, owned by no store plugin.
//
//   { "replaceDefaults": false,
//     "packs":    [ { "pack": "1_1", "common": [15300], "rare": [], "replace": false } ],          cards added to (or replacing) a game pack
//     "newPacks": [ { "id": 36, "name": "custom_1", "series": 0, "cost": 200, "art": "1_1",          packs the game doesn't have
//                     "title": { "E": "My Pack" }, "text": { "E": "..." },
//                     "common": [4007, 4041], "rare": [15300], "unlockWith": "1_3" } ] }
//
// How the game does packs (docs/Packs.md): LoadPackDefinitions (0x14080E3C0) fills g_PackRecords[128] (0x68 each) from
// main/packdefdata_#.bin; the game uses 36 ids. A shop tab lists every record with a kind whose series is the tab
// (BoosterShop_BuildPackList), so a new record in a free slot shows up by itself. Buying needs the pack's bit in the save's 128-bit
// unlock field (player section +0xB80). The picture is the "wrap_<name>" layer (Pack_GetArtName).
//
// New packs: "id" 0-127 and not a game pack's (WolfEx picks the next free one; keep it, the save's unlock bit is by id), "series" = the
// shop row (tab; 6+ = new rows, no "series" = first new row with room; a row holds as many packs as the game's fullest row,
// "rows": [{"row": 6, "name": {"E": "TEST"}, "art": 2}] = row 6's name, drawn over game row 2's banner ("art": "<key>" = a new
// "wrap_series_<key>" banner added to pdui\SeriesLogo; a pack's "art" likewise names any "wrap_<x>" of pdui\PackWrappers); "rowLabel": {"x", "y",
// "size", "colour"} = where the name goes, screen pixels 1920x1080), "art" = an existing pack name whose picture to use, "common"/"rare" must both have cards. Unlock: no "unlockWith" = open
// from the start; "unlockWith": "<pack name>" = opens when that pack does; "unlockWith": "never" = stays locked. The bits are set when a
// shop tab is built and are kept in the save (a game without this plugin ignores them: the slot is empty there).
namespace Packs
{
    // The cards in a rare slot of any booster pack loaded now (the game's packs with packs.json applied). Every card the game has is in
    // exactly one rarity: 2,094 rares and 7,933 commons, none in both.
    std::unordered_set<uint16_t> RareCards();

    // Reads packs.json, hooks the pack loader, art and shop list, and applies everything to the packs the game has already loaded.
    // Core calls it once when it starts; needs nothing else (RIX only for the row labels, when it is there).
    void Install();
}
