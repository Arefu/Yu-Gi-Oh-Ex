#pragma once

// The shop's booster packs, from Yu-Gi-Oh-Ex/packs.json (written by WolfEx's Packs tab; moved here from Yu-Gi-Oh-Cards):
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
// New packs: "id" 0-127 and not a game pack's (WolfEx picks the next free one; keep it, the save's unlock bit is by id), "series" 0-5 =
// the shop tab, "art" = an existing pack name whose picture to use, "common"/"rare" must both have cards. Unlock: no "unlockWith" = open
// from the start; "unlockWith": "<pack name>" = opens when that pack does; "unlockWith": "never" = stays locked. The bits are set when a
// shop tab is built and are kept in the save (a game without this plugin ignores them: the slot is empty there).
namespace Packs
{
    // Reads packs.json, hooks the pack loader, art and shop list, and applies everything to the packs the game has already loaded.
    // Call once from ProcessDetours; needs nothing else (no RIX).
    void Install();
}
