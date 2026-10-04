#pragma once

// New tutorials (numbers 27-99) from Yu-Gi-Oh-Ex/tutorials/steam_tutorial_NN_L.json, written by WolfX's Tutorials page (docs/Tutorials.md):
//
//   { "tutorial": 27, "language": "E", "title": "My tutorial", "arena": 1, "menu": false,
//     "steps": [ { "op": "SetupScenario", "p2": 1 }, { "op": "Message", "text": "Hello!" }, ..., { "op": "End" } ] }
//
// The game only knows tutorials 1-26 through fixed tables in the exe, so this:
//   - serves the steps (LoadScriptFile 0x1408709D0) in the game's own layout when the game asks for a number that has a JSON;
//   - lists the numbers after the game's own in Help > Tutorial (ScreenSelectTutorial's number vector, filled on every OnEnter), unless
//     "menu" is false, with "title" as the name (Get_TitleStringId 0x1408709B0: SetTextById takes a wide-string pointer as well as an id);
//   - plays it in "arena" (the screen's Set_CurrentArenaId call reads g_TutorialArenaIds, which has 27 entries);
//   - keeps it out of the save: End (op 0x7F) skips its done-bit test for numbers >= 27 but still SETS the bit, which for 32+ lands past the
//     tutorial bits (PlayerSection +2960). A stub on that bit write sends numbers >= 27 past it, whether or not a JSON exists; the plugin
//     remembers them in Yu-Gi-Oh-Ex/tutorials/done.json per save file instead and draws the list's done mark for them.
namespace Tutorials
{
    // Reads the JSON and attaches the hooks; call inside the plugin's Detours transaction.
    void Attach();
}
