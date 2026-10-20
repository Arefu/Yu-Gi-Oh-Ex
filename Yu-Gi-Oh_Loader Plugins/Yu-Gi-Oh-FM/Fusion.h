#pragma once

// Forbidden Memories fusion from the hand, step by step (no Polymerization).
//
// A hand monster's command menu gets a "Fuse" command whenever it also offers Summon. Fuse on a first card picks it as the start of the
// chain; Fuse on each next card fuses it in, the FM way: the pair's result if fm.json has one (in either order), otherwise the card so
// far is discarded and the new one carries on. Fusing the chosen card again cancels the chain.
//
// STAGE 4 (2026-10-20, untested, verbose log): FM-style. Menus: a summonable hand monster Summon / Fuse / Set / Show Details; while a chain
// is open every hand card is highlighted (Get_CardCommandFlags / Pick_GetCardFlags get the Fuse bit) with Fuse / Summon / Set / Show
// Details (Summon and Set add the card, then finish); a chain card Summon / Set / Cancel fusion. Finishing is applied in
// YGO::Duel::SubmitPick (0x140063840, the UI's answer to the engine) just before the Summon / Set goes in: fusion animation (DuelAnim 46/48)
// for the pairs that fused, the materials to the GY with Eval_CardMovedTriggers skipped (no effects, as in FM), the first card turned into
// the result, the command sent for it. (Stage 3 applied at the idle machine's state 2, which doesn't run while the UI waits: too late.)
//
// The game (IDB):
//   - YGO::DUELUI::CardCommandMenu_Open (0x140777F70) works out a command bit mask for the card and builds DUEL_DIALOG_SELECT_COMMAND with
//     sub_140778610(dialog, mask): one menu line per set bit, from a 19-entry table (0x140A52DF0, 24 bytes: u32 command, i64 label = a
//     string id, or a wchar_t* when above 2214, i64 alternative label). The loop's table (lea at 0x14077868D) and count (0x1407786A2)
//     are patched to a 20-entry copy with command 19 = "Fuse". The line's icon is command + 229 (sub_1407FD360, only used there):
//     19 is shown with Summon's icon.
//   - The card the menu is for: dialog +1488 (u16: bit 0 side, bits 1-5 zone, 13 = the hand; higher bits the index).
//   - The pick: sub_140778270(card, command, mask, frames). Commands above 13 never reach the engine; 19 is handled here and returns 0.
//   - sub_14076FC60(card) = the card's id (Konami id).
namespace Fusion
{
    // Reads fm.json (cards, fusions) and patches / hooks; call inside the plugin's Detours transaction.
    bool Attach();
}
