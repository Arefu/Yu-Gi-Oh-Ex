#pragma once

// Extra and replacement entries for the game's two text tables, from Yu-Gi-Oh-Ex/text.json (written by WolfEx's Text tables tab):
//
//   { "word": { "246": { "E": "/Custom/Effect", "F": "..." } },     WORD: the words on card frames (card kind 246 = 200 + kind 46, ...)
//     "dlg":  { "688": { "E": "Select @21@0 @3card@0." } } }         DLG: the duel's prompts
//
// A number past the game's own entries adds one; a number it has replaces it. The text for the game's current language is used,
// else English, else any. YGO::CARDS::Get_WordText (0x14076D7B0) and Get_DlgText (0x14076D770) are hooked (MS Detours).
namespace Text
{
    // Reads text.json (again): call after each card setup, since that's when the game reloads its tables for a language.
    void Load();

    // The letter of the game's current language (E, F, G, I, J, R, S); 'E' if unknown.
    char CurrentLanguage();

    // Attaches the two getter hooks; call inside the plugin's Detours transaction.
    void Attach();
}
