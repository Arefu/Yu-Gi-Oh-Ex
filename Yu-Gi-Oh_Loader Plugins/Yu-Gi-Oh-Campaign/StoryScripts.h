#pragma once

// The story scenes (dialog before and after campaign duels), from Yu-Gi-Oh-Ex/storyscripts.json (written by WolfEx's Story editor,
// docs/StoryScenes.md):
//
//   { "scripts": [ { "name": "MyDuel_INTRO", "lines": [
//       { "who": "command", "position": "BG", "expression": "classic_school" },
//       { "who": "yugimuto", "position": "LEFT", "expression": "smile", "text": { "E": "Let's duel!" } } ] } ] }
//
// The game: LoadStoryScriptData (0x14074A290, called by LoadLanguageContent at start and on a language change) loads
// main/scriptdata_#.bin into one malloc'd block, g_pStoryScriptFile (0x140D4DEF0), and frees it with free() on the next load.
// StoryDuel_FindDialogScript looks scenes up by name (<duel key>_INTRO / _OUTRO / _OUTRO_LOSE, ignoring case, first match).
// After the game's load this builds one new block in the same layout: the game's scenes, with the JSON's replacing those of the same name
// (a scene with no lines removes it), plus the new ones; the texts in the game's language (else English). It frees the game's block and
// puts its own in its place, from the same CRT heap, so the game's next free() is right.
namespace StoryScripts
{
    // Reads storyscripts.json and attaches the hook; call inside the plugin's Detours transaction.
    void Attach();

    // Applies to what the game has already loaded (if it has): call after the transaction.
    void ApplyIfLoaded();
}
