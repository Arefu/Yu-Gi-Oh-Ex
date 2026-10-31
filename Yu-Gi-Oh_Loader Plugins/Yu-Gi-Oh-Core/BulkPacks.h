#pragma once

// Buying many booster packs at once in the game's own shop, a Core limit breaker (2026-11-01), [Yu-Gi-Oh-Core] in Config.ini:
//   MaxPacksPerPurchase=999   most packs one purchase can buy (1 = the game's one pack at a time)
//
// How the game buys a pack (traced in the IDB 2026-11-01, docs/Packs.md "Buying many packs"):
//   * BoosterShop_HandleInput (0x14085BAC0) checks the unlock bit and the DP, then opens the screen's Yes/No box with
//     RIX::Screen::ShowYesNoText (0x1408228D0); its Yes callback is the lambda with vftable 0x140A77390, whose call
//     (BoosterShop_BuyYes_DoCall 0x14085E850) runs GAME::Store::OpenPackAndGrantCards (0x14085BFC0) and SetState(2).
//   * OpenPackAndGrantCards takes the cost, rolls 7 commons + 1 rare (weights g_PackPullWeightByOwned {10,10,10,1} by copies owned),
//     adds them to the saved card table, then RecountOwnedCards, widget_CardGrid::SetCards on the result grid (screen+2256),
//     RebuildLiveUnlockCounts (every deck template + all 732 decks revalidated) and RequestAutosave.
//   * State 2 = the big wrapper, 3 = the 8 grid cells flip in, 4 = the grid (it scrolls, any number of cards), 5 = one card's details.
//
// What this adds:
//   * The buy box gets a count row: the game's arrows (clickable) either side of "N packs"; Left / Right = 1 less / more (wrapping
//     between 1 and the most you can afford), Up / Down = 10 more / less, the mouse wheel = 1. The box's text is the game's own "buy"
//     line with the total cost.
//   * Yes starts a job that opens a few packs per frame while the big wrapper waits ("Opening 340 / 999" under it; unwrapping early
//     waits for the last pack). OpenPackAndGrantCards runs once per pack (so every card plugin's patches apply), with call gates on its
//     slow calls: the grid fill for every pack, the save work (recount, deck revalidation, autosave) for all but the last. The wrapper
//     and the flip play once.
//   * The grid then lists each card pulled once: new cards first, then rares, then the most pulled. Under it a page control (arrows and
//     a block per page) turns pages on a click; the mouse wheel scrolls it too.
namespace BulkPacks
{
    void Install();
}
