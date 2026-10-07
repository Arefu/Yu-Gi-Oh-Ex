#pragma once

#include <cstdint>
#include <string>
#include <vector>

// The ban list a lobby plays with (moved here from PatchMeOut's NoBan 2026-10-07, grown into custom lists the same day). Only ever
// changed for a lobby: the host's "Ban list" setting (LiveSetting.cpp); nothing is saved, the game's list is back at every start.
//   Game   - the game's own list (bin/pd_limits.bin, which a mod's patch DAT can replace)
//   Off    - every card at 3 copies
//   Custom - a list from Yu-Gi-Oh-Ex\banlists\*.json (any active mod's too; WolfX Forbidden & Limited > Save as MP list):
//            { "name": "...", "forbidden": [Konami ids], "limited": [...], "semiLimited": [...] }, every other card at 3
//
// The game keeps a card's limit (0 Forbidden, 1 Limited, 2 Semi-Limited, 3 Unlimited) in two tables, both filled when the card data loads:
//   - the Konami id props (KONAMI_ID_CARD_PROPS 0x142847E50, 0x30 each, 0x3A79 of them) +0x20, from bin/pd_limits.bin
//     (Setup_CardPropTable writes 3 for every card, then the list's entries);
//   - FULL_CARD_PROPS (0x142927600, 0xA0 each, 65536, by Konami id) +0x64, copied from the first by Setup_FullCardProps (0x14081A080).
// Every reader (deck editor, deck checks, duel UI, ~30 call sites, some inlined) reads one of the two, so a list is applied by writing
// both; the game's own values are kept to put back. Setup_FullCardProps (language change) is hooked so the choice survives a rebuild.
namespace BanList
{
    enum class Mode { Game, Off, Custom };

    struct List
    {
        std::string Name;
        std::vector<uint16_t> Forbidden, Limited, SemiLimited;
    };

    // Attaches the rebuild hook; call inside a Detours transaction.
    void Attach();

    void UseGame();
    void UseOff();
    void UseCustom(const List& list);

    Mode CurrentMode();
    const List& CurrentList();   // the custom list in use (empty unless Custom)

    // The lists in banlists\*.json (read on first use, then kept).
    const std::vector<List>& Saved();

    // A list as one lobby data string and back: the name, a newline, then base64 of the list in pd_limits.bin's own layout (three blocks of
    // u16 count + u16 Konami ids), about 2.7 characters a card. The game's own list (228 cards) is about 620 characters.
    std::string Encode(const List& list);
    bool Decode(const std::string& text, List& list);
}
