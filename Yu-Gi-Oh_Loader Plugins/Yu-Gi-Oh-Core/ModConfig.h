#pragma once

// Mod settings (docs/Mods.md "Settings"): a mod can carry its own Config.ini (<game>\Mods\<id>\Config.ini, the same [Section] Key=Value
// layout as the game's), so a mod that needs plugin settings (Anime Cards' frame layout, the FM rules...) works without the player editing
// their Config.ini. Core hooks the four GetPrivateProfile* functions every plugin reads Config.ini through (kernel32 Int/String, A and W)
// and answers a [Section] Key that an active mod sets from the mod, later mods winning; everything else reads the player's Config.ini.
// Writes always go to the player's file. Not settable by a mod (left to the player's Config.ini):
//   [Yu-Gi-Oh-Core] the plugin switches (Yu-Gi-Oh-*, YGO-Ex/*), PluginsPath, ModConfig, GameSaveName, SeedFromGameSave, SaveSlot,
//                   ReplacedNotice (what loads and which save is used)
//   [Yu-Gi-Oh-MP]   everything (the server address and sign-in code must never be pointed somewhere else by a mod)
// [Yu-Gi-Oh-Core] ModConfig=0 in the player's Config.ini turns mod settings off. Installed first, so Core's own settings follow mods too.
namespace ModConfig
{
    void Install();
}
