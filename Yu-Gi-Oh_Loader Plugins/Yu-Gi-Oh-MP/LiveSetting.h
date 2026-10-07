#pragma once

// A "Ban list" row on the game's match settings screen (RIX::ScreenLiveSetting, screen 34: single/match, LP, time limit, open to...)
// when hosting (not Ranked: fixed rules): Game (the game's list), Off (every card at 3), or a custom list (BanList.h). Changing it applies
// at once, so the deck list and the deck check on that screen already follow it.
//
// The screen is data driven (names in the IDB): 10 setting slots at +3848 (48 bytes: label, description, options vector {text, 0} x n),
// the chosen option per slot at +4328, the rows shown at +3824 (slot numbers, the last row is the action: 6 = create, 7 = join...).
// The game uses slots 0-4 and 6-9; slot 5 is free and becomes "Ban list". LiveSetting_BuildOptions fills the options,
// LiveSetting_BuildRows picks the rows (the row goes in before Create), LiveSetting_ChangeOption steps a slot's option (any slot < 10),
// LiveSetting_RowsInput only sends left/right to slots < 5, so the hook sends them for slot 5.
// Lobby: the host writes lobby data "exbanmode" + "exbanlist" (QNet__PublishHostLobbyData), a joiner with Yu-Gi-Oh-MP reads it on entering
// (QNet__OnLobbyEnter); players without the plugin keep their own list for their own deck (the duel itself never checks it).
// A joiner is asked first (Yes = play the host's list, No = leave the lobby); until then their own game list applies.
// Back on the screen or leaving the lobby turns the ban list back on.
namespace LiveSetting
{
    // Attaches the hooks; call inside a Detours transaction.
    void Attach();
}
