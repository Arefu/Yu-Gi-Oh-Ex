#pragma once
#include <cstdint>

// Shiny cards: real-card rarities (Rarity.h: every printed rarity, Rare to Quarter Century Secret, Starlight, Gold, Ghost, Parallel,
// Millennium, Starfoil ...) rolled when booster packs are opened, kept in the save, and drawn as animated foil over each card face.
// From the Yu-Gi-Oh-Foil plugin (its texture <-> card matching), made generic. docs/Shiny.md.
//
// [Yu-Gi-Oh-Core] in Config.ini (declared in this plugin's manifest; the section stayed when the code moved here from Core, 2026-11-02):
//   Shiny=0            1 = packs roll rarities and the cards are drawn with them (restart after changing; the saved rarities stay either way)
//   ShinyOddsScale=100 percent of every rarity's odds (50 = twice as many shinies, 200 = half)
//   ShinyStrength=1    how strong the foil is drawn (0 .. 2)
//   ShinySpeed=1       how fast it moves
//   ShinyMouseTilt=1   on a card shown big (card details) the light follows the mouse, as if the card were tilted; elsewhere (and 0) a slow sweep
//   ShinyPreview=      a rarity key ("starlight") or name to draw EVERY card with, "cycle" = every rarity in turn (3 s each), empty = off
//
// How it works:
//   * Packs: GAME::Store::OpenPackAndGrantCards (0x14085BFC0) leaves the pack's 8 cards in the shop screen's PulledCards (+4448), the rare
//     slot last. The hook rolls each card: the rare slot against every rarity with "slot": "rare" or "any", rarest first (Rare when none
//     hits), the 7 common slots against "any" only. Installed before BulkPacks, so the bulk job's per-pack calls come through it too.
//   * Save: extension tag "shiny" (version 2: u16 card, u8 rarity, u16 owned, u16 pulled, u8 seen per entry; version 1 is read too),
//     read when the card table takes over a save (Core_AddCardTableListener), written by a save write filter. A card is shown with its
//     highest-tier rarity. Every pull is logged "<rarity> ripped: <card>".
//   * Card pictures: a rarity with its own art (Rarity::ArtStyle) draws cardart\<card>.<style>.png from the content folders while the
//     face is built (Get_IllustrationData 0x14086D050, and FaceArt for Card.cpp's own hook on it, which serves custom cards' art).
//   * Faces: CardFace_Build (0x14074E7D0) + AddQuad (0x1408795A0) record where the art, attribute and stars are drawn, after Anime Cards'
//     changes: these detours must stay inside its, so its manifest has "loadAfter" this plugin (a later detour runs first and calls the earlier
//     one). CardFace_RenderToTarget (0x140754420) queues the card id; the recorded clear of
//     its 400 x 580 target (ClearRenderTargetView, grey 0xFF808080) runs later on the render thread and ties that texture to the card.
//   * Drawing: every draw with a rarity card's face in pixel shader slot 0 is drawn again with ShinyShader.h blended over it, keeping
//     the game's vertex shader. D3D11CreateDeviceAndSwapChain (thunk 0x14090D2B0) gives the device; only the first call with a swap chain.
namespace Shiny
{
    void Install();

    int ShownRarity(uint16_t card);                    // 0 = none
    int Count(uint16_t card, uint8_t rarity);
    int Add(uint16_t card, uint8_t rarity, int count);  // returns the new count
    // While the face of `card` is being built with a picture of its own: a copy in the game's heap (it frees it), true. Else false.
    bool FaceArt(uint16_t card, void** bytes, size_t* size);
}
