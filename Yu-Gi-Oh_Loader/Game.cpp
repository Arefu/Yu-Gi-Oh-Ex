#include <fstream>
#include <iostream>
#include <Shlwapi.h>
#include <detours.h>
#include <string>
#include <vector>
#include <Windows.h>

#include "Game.h"
TCHAR Game::gGamePath[MAX_PATH];
TCHAR Game::gGameLocation[MAX_PATH];

std::vector<std::string> Game::gDlls;
std::vector<LPCSTR> Game::gPlugins;

BOOL Game::Locate()
{
	HKEY hKey;
	char lGamePath[MAX_PATH];
	DWORD dwSize = sizeof(lGamePath);
	if (RegOpenKeyExA(HKEY_LOCAL_MACHINE, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1150640", 0, KEY_READ, &hKey) == ERROR_SUCCESS)
	{
		if (RegQueryValueExA(hKey, "InstallLocation", NULL, NULL, (LPBYTE)lGamePath, &dwSize) == ERROR_SUCCESS)
		{
			strncpy(Game::gGamePath, lGamePath, MAX_PATH);
			strncat(Game::gGameLocation, lGamePath, strlen(lGamePath));
			strncat(Game::gGameLocation, "\\YuGiOh.exe", sizeof("\\YuGiOh.exe"));
		}
		else
			return FALSE;

		RegCloseKey(hKey);
		return TRUE;
	}
	else
		return FALSE;
}

BOOL Game::Start()
{
	if (Game::Check(gGamePath) == FALSE)
		return FALSE;

	STARTUPINFO info = { sizeof(info) };
	PROCESS_INFORMATION processInfo;

	Game::LookForPlugins();

	return DetourCreateProcessWithDllsA(gGameLocation, NULL, NULL, NULL, FALSE, 0, NULL, gGamePath, &info, &processInfo, gDlls.size(), gPlugins.data(), NULL);
}

BOOL Game::Start(LPWSTR CustomPath)
{
	
	STARTUPINFO info = { sizeof(info) };
	PROCESS_INFORMATION processInfo;

	Set_GamePath(CustomPath);

	Game::LookForPlugins();

	return DetourCreateProcessWithDllsA(gGameLocation, NULL, NULL, NULL, TRUE, NULL, NULL, NULL, &info, &processInfo, gDlls.size(), gPlugins.data(), NULL);
}

void Game::LookForPlugins()
{
	WIN32_FIND_DATAA FindFileData;
	CHAR CurrentDir[MAX_PATH];
	GetCurrentDirectoryA(MAX_PATH, CurrentDir);

	strncat(CurrentDir, "\\Plugins\\*.dll", sizeof("\\Plugins\\*.dll"));

	HANDLE hFind = FindFirstFileA(CurrentDir, &FindFileData);


	CHAR FullPathOfDll[MAX_PATH];
	GetCurrentDirectoryA(MAX_PATH, FullPathOfDll);
	strncat(FullPathOfDll, "\\Plugins\\", sizeof("\\Plugins\\"));


	int i = 0;
	do
	{
		auto bleh = std::string(FullPathOfDll) + std::string(FindFileData.cFileName);
		gDlls.push_back(bleh);
		gPlugins.push_back(gDlls.back().c_str());

		OutputDebugStringA(gDlls[i].c_str());
		i++;
	} while (FindNextFileA(hFind, &FindFileData) != 0);



	FindClose(hFind);
}

void Game::Set_GamePath(LPWSTR Path)
{
    char charPath[MAX_PATH];
    wcstombs(charPath, Path, MAX_PATH);

    strncpy(Game::gGamePath, charPath, MAX_PATH);
    strncat(Game::gGameLocation, charPath, strlen(charPath));
    strncat(Game::gGameLocation, "\\YuGiOh.exe", sizeof("\\YuGiOh.exe"));

    HKEY hKey;
    if (RegOpenKeyExA(HKEY_LOCAL_MACHINE, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1150640", 0, KEY_WRITE, &hKey) == ERROR_SUCCESS)
    {
        RegSetValueExA(hKey, "InstallLocation", 0, REG_SZ, (LPBYTE)charPath, strlen(charPath));
        RegCloseKey(hKey);
    }
}

void Game::CreateConfig(LPCSTR ConfigName)
{
	CHAR ConfigPath[MAX_PATH];
	strncpy(ConfigPath, gGamePath, MAX_PATH);
	strncat(ConfigPath, "\\", sizeof("\\"));
	strncat(ConfigPath, ConfigName, strlen(ConfigName));

	if (PathFileExistsA(ConfigPath) == FALSE)
	{
		std::ofstream ConfigFile(ConfigPath);

		char CurrentDir[MAX_PATH];
		GetCurrentDirectoryA(MAX_PATH, CurrentDir);
		ConfigFile << "[Yu-Gi-Oh-GUI]" << std::endl;
		ConfigFile << "PluginsPath=" << CurrentDir << "\\Plugins\\" << std::endl;
		ConfigFile.close();
	}
}

BOOL Game::Check(LPSTR Path)
{
	return PathFileExistsA(Path);
}