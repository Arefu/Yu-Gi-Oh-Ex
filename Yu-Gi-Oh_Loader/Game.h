#pragma once
#include <vector>

static class Game
{
public:
	/// <summary>
	/// Looks for the Game's path in the registry
	/// </summary>
	/// <returns>TRUE if located, FALSE if not.</returns>
	static BOOL Locate();

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
	/// Starts the game with a custom path
	/// </summary>
	/// <param name="CustomPath">Custom Path to load from</param>
	/// <param name="Plugins">Enable Plugin Loading</param>
	/// <returns>TRUE if Process Created</returns>
	static BOOL Start(LPWSTR CustomPath);

	/// <summary>
	/// Looks for plugins in the Plugins folder from the Loader's directory
	/// </summary>
	static void LookForPlugins();

	/// <summary>
	/// Sets the game's path to the specified path
	/// </summary>
	/// <param name="Path">Force the game path</param>
	static void Set_GamePath(LPWSTR Path);

	/// <summary>
	/// Creates Base Config File For "Yu-Gi-Oh-GUI". Other plugins may use this for their own configuration.
	/// </summary>
	/// <param name="ConfigName">File Name</param>
	static void CreateConfig(LPCSTR ConfigName);


	/// <summary>
	/// The plugins to load, formatted for Detours.
	/// </summary>
	static std::vector<LPCSTR> gPlugins;

private:
	static TCHAR gGamePath[MAX_PATH];
	static TCHAR gGameLocation[MAX_PATH];
	static std::vector<std::string> gDlls;
	
};
