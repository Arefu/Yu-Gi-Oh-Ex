#pragma once

// A "More Options" button beside Create on the game's match settings screen (RIX::ScreenLiveSetting, screen 34: single/match, LP, time
// limit, open to...) when hosting a casual match (not Ranked: fixed rules, which is what keeps ranked duels comparable). It opens the More
// Options page (MatchSettingsPage.h, shipped with this plugin): the duel format and the ban list (Game, Off or a custom list, BanList.h).
// If the page can't open, the screen's message box says so. The page is its own screen, so coming back rebuilds this one (deck list included).
//
// The screen is data driven (names in the IDB): 10 setting slots at +3848 (48 bytes: label, description, options vector {text, 0} x n),
// the chosen option per slot at +4328, the rows shown at +3824 (slot numbers, the last row is the action: 6 = create, 7 = join...).
// The game uses slots 0-4 and 6-9; slot 5 is free: it is the button's place in the key order (a row before Create whose row widget is
// hidden; LiveSetting.cpp has the button's widget layout). LiveSetting_RowsInput only sends left/right to slots < 5 (patched to < 6).
// Until 2026-10-25 the ban list and duel format were two rows (the format one borrowing slot 7, the Join button's): the second ran off the
// panel, and the borrowing also relabelled the Join button "Duel format" when joining.
//
// Lobby: the host writes lobby data "exbanmode" + "exbanlist" and "exformat" (QNet__PublishHostLobbyData), a joiner with Yu-Gi-Oh-MP reads
// them on entering (QNet__OnLobbyEnter); players without the plugin keep their own list for their own deck (the duel itself never checks it).
// A joiner is asked before playing the host's ban list (Yes = play it, No = leave the lobby); until then their own game list applies.
// The format is applied straight away (Core_SetOnlineDuelFormat: the online duel is lockstep, a player without Yu-Gi-Oh-MP plays Master and
// the duel desyncs, so the host should kick players on the "No Yu-Gi-Oh-MP" line). Deck limits follow (Core_SetDeckLimits): Speed 20-30 /
// Extra 5, Rush 40-60 / 15, Master = the player's own Config.ini limits.
// Back on the screen or leaving the lobby puts the game's ban list and Master back.
namespace LiveSetting
{
    // Attaches the hooks; call inside a Detours transaction.
    void Attach();

    // The lobby's duel format as a lobby data key: master / speed / rush.
    const char* FormatKey();

    // Plays a duel format (master / speed / rush; anything else = master): Core's online format and the deck limits that go with it.
    void SetFormatKey(const char* key);
}
