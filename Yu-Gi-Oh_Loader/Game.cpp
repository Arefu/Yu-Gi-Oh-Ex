#include <Windows.h>
#include <detours.h>
#include <string>

#include "Game.h"

TCHAR Game::gGamePath[MAX_PATH];
TCHAR Game::gGameLocation[MAX_PATH];
TCHAR Game::gDllLocation[MAX_PATH];

BOOL Game::Locate()
{
	HKEY hKey;
	char lGamePath[MAX_PATH];
	DWORD dwSize = sizeof(lGamePath);
	if (RegOpenKeyExA(HKEY_LOCAL_MACHINE, "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\Steam App 1150640", 0, KEY_READ, &hKey) == ERROR_SUCCESS)
	{
		if (RegQueryValueExA(hKey, "InstallLocation", NULL, NULL, (LPBYTE)lGamePath, &dwSize) == ERROR_SUCCESS)
		{
			Set_GamePath(lGamePath);
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
	STARTUPINFO info = { sizeof(info) };
	PROCESS_INFORMATION processInfo;
	BOOL Status = DetourCreateProcessWithDllExA(gGameLocation, NULL, NULL, NULL, FALSE, 0, NULL, gGamePath, &info, &processInfo, gDllLocation, NULL);
	return Status;
}

void Game::Set_GamePath(CHAR Path[MAX_PATH])
{
	strncpy(Game::gGamePath, Path, MAX_PATH);
	strncat(Game::gGameLocation, Path, strlen(Path));
	strncat(Game::gGameLocation, "\\YuGiOh.exe", sizeof("\\YuGiOh.exe"));

	TCHAR NPath[MAX_PATH];
	GetCurrentDirectory(MAX_PATH, NPath);
	strncpy(Game::gDllLocation, NPath, sizeof(NPath));
	strncat(Game::gDllLocation, "\\Yu-Gi-Oh-x64.dll", strlen("\\Yu-Gi-Oh-x64.dll"));
}