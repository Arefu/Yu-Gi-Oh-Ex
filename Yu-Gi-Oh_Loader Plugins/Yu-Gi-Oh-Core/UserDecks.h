#pragma once

// Extra user deck slots, a Core limit breaker (2026-10-23). Ported from Toon Jinzo's Yu-Gi-Oh-DeckSlots, which it replaces.
//
// The save holds 32 user decks (DeckListItem, 0x130 bytes, at blob +0x37C8). Deck ids are packed: 0-31 user decks (type 1), 32-731 built-in
// decks (type 2), 732-736 special decks (type 3). The extra decks have ids 1024 up (type 1, user deck index 32 up; the same ids
// Yu-Gi-Oh-DeckSlots used) and live in the save extension "UserDecks" (Save.h), so they follow save slots and the vanilla game never sees
// them. A Yu-Gi-Oh-DeckSlots side file (<save>-extradecks.dat) is imported the first time a save without the extension is loaded.
//
// No fixed cap (issue #40: a cap of 224 dropped decks a DeckSlots save kept past it): when a save is loaded the slot count is the slots it
// uses (up to its last deck) + kHeadroom free ones, so every deck keeps its slot and id, and the count follows the save as it grows or shrinks.
// The only hard limit is the game's: Deck_FromId reads the user deck index as an int16 (kMaxExtraDecks).
//
// Traced in the IDB 2026-10-23; every hook is a Detour on a function entry (docs/UserDecks.md):
//   Deck_DecodeId 0x1407F9150          id -> (type, index)
//   Deck_UserDeckAt 0x14076E8E0        user deck by index (the game's returns NULL past 31)
//   Deck_GetValidation 0x14081AA20 / ProfileSave_CopyValidation 0x140871430 / Deck_Revalidate 0x1407BDFD0 / Deck_RevalidateAll 0x1407BDF20
//                                      the per-deck DeckValidation table holds ids 0-731; extras get their own (Revalidate has no bound check)
//   PlayerSection_SetSelectedDeck 0x1407F93C0   +4 selected id, +8 last user deck INDEX (the deck editor reads it through Deck_UserDeckAt)
//   Deck_ListAll 0x14081AC80 / Deck_ListForSeatTable 0x14081ADC0 / Deck_ListUserSlots 0x14081AEA0 / DeckSelectList_FillDecks 0x1408AD890
//                                      the deck lists (each of the first three has one caller)
//   GetOrInitProfileBuffer 0x140871500 / Get_OrCreateBlob 0x140871490   a save loaded / a new profile
// The save write (Save.cpp's write filter) puts the extras into the extension.
namespace UserDecks
{
    constexpr int kSaveDecks = 32;
    constexpr int kHeadroom = 256;                        // free slots past the save's last deck
    constexpr int kMaxExtraDecks = 0x7FFF - kSaveDecks;   // user deck index 32 + k fits Deck_FromId's int16
    constexpr unsigned kFirstExtraId = 1024;

    // The extra slots of the loaded profile (ids kFirstExtraId..kFirstExtraId + count - 1); set when a save is loaded or made.
    int ExtraCount();

    void Install();
}
