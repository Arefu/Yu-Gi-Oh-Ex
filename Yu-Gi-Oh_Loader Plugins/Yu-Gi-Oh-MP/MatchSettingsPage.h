#pragma once

// The "More Options" page: the host's "More Options" button beside Create on the match settings screen (LiveSetting.cpp) opens it.
// It holds the settings the game's screen has no room for (the duel format, the ban list). Ranked has no such button: its rules are fixed,
// which is what makes ranked duels comparable (casual = custom settings, ranked = the game's rules).
//
// It is a BOB page this plugin ships (source Content\pages\mp-match-settings.bob, built to Plugins\Yu-Gi-Oh-MP\pages, found through
// RIX_AddPageFolder), next to the ban list picker it opens (banlists.bob, BanListPicker.h). A copy in Yu-Gi-Oh-Ex\pages or a mod overrides
// it. What this plugin gives it:
//
//   var:mp.format                 master / speed / rush (set from the lobby's format before the page opens)
//   call("mp.format.apply")       plays the format in var:mp.format
//   {game:mp.banlist}             the list in use (BanListPicker.h, with the picker's actions)
namespace MatchSettingsPage
{
    // Registers the actions with Yu-Gi-Oh-RIX (once; does nothing while RIX isn't loaded). Call on the game's thread.
    void Register();

    // Opens the page; false when RIX or the page file (Plugins\Yu-Gi-Oh-MP\pages) is missing.
    bool Open();
}
