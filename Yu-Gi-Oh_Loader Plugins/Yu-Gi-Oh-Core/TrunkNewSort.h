#pragma once

// A "New" step on the deck editor trunk's sort button: cards the save marks NEW (saved card table bit 3 clear, Card_IsNewlyOwned) come first,
// each half in name order.
//
// Which cards are new is read once, when the deck editor opens (TrunkView_BuildCardList, from ScreenDeckEditor::OnEnter), and kept until the
// editor is opened again. Hovering a card clears its NEW mark in the save (Card_MarkSeen), so reading it live would move cards about on the next
// filter change; the snapshot keeps the order still until the player leaves.
//
// The sort button (widget_CardSortBar at trunk+56) steps through the 11 modes of g_CardSortModes (0x140C8C670, 40 bytes each:
// { mode, comparator factory, group key factory, header arg, string id }) with `% 11` and an enabled mask. "New" is a 12th step after the last
// one, for the deck editor's trunk only: the bar's mode stays 0 (Name) so the group jump (sub_1408BF770) and the row headers (sub_1408C0270)
// keep working, and our flag adds the partition after the game's sort.
//
// Traced in the IDB 2026-11-27; docs/UserDecks.md "Trunk: sort by New".
namespace TrunkNewSort
{
    void Install();
}
