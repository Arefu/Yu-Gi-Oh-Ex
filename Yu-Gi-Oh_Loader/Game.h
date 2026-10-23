#pragma once

#include <string>
#include <vector>

static class Game
{
public:
    /// <summary>
    /// Looks for the game's path: first Steam's registry key, then a path the player
    /// chose by hand last time (kept in Yu-Gi-Oh_Loader.ini next to the loader).
    /// </summary>
    /// <returns>TRUE if a folder holding YuGiOh.exe was found, FALSE if not.</returns>
    static BOOL Locate();

    /// <summary>
    /// Fallback for when Locate() fails (a non-Steam copy, or Steam never wrote the key):
    /// asks the player to point at YuGiOh.exe with a standard open-file dialog, then saves
    /// the folder next to the loader so it is remembered next time. No admin rights needed.
    /// </summary>
    /// <returns>TRUE if the player chose a valid folder, FALSE if they cancelled.</returns>
    static BOOL PromptForGame();

    /// <summary>
    /// Checks if the game's path is valid by looking for the game's executable.
    /// </summary>
    /// <param name="Path">The location to check</param>
    /// <returns>TRUE if the EXE is there.</returns>
    static BOOL Check(LPSTR Path);

    /// <summary>
    /// Starts the game
    /// </summary>
    /// <param name="Plugins">Enable Plugin Loading</param>
    /// <returns>TRUE if Process Created</returns>
    static BOOL Start();

    /// <summary>
    /// Looks for plugins in the Plugins folder from the Loader's directory
    /// </summary>
    static void LookForPlugins();

    /// <summary>
    /// Creates Base Config File For "Yu-Gi-Oh-GUI". Other plugins may use this for their own configuration.
    /// </summary>
    /// <param name="ConfigName">File Name</param>
    static void CreateConfig(LPCSTR ConfigName);

    /// <summary>
    /// Used to Ensure DLLs are injected in a specific order, E.G. Loading Yu-Gi-Oh-Console before others to ensure the Library is ready for logging.
    /// </summary>
    static void CheckForLoadOrder();

    /// <summary>
    /// The plugins to load, formatted for Detours.
    /// </summary>
    static std::vector<LPCSTR> gPlugins;

    static std::vector<std::string> gModsToLoad;

private:
    /// <summary>
    /// If <paramref name="folder"/> holds YuGiOh.exe, sets gGamePath (the folder) and
    /// gGameLocation (the full exe path) and returns true; otherwise leaves them and returns false.
    /// </summary>
    static bool TrySetFolder(const char* folder);

    static TCHAR gGamePath[MAX_PATH];
    static TCHAR gGameLocation[MAX_PATH];
    static std::vector<std::string> gDlls;
};
