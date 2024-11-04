#include <Windows.h>
#include <detours.h>
#include <Shlwapi.h>
#include <iostream>
#include <string>

#include "Game.h"
TCHAR Game::gGamePath[MAX_PATH];
TCHAR Game::gGameLocation[MAX_PATH];
LPCSTR Game::gDlls[MAX_PATH];


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
	if (Game::Check(gGamePath) == FALSE)
		return FALSE;

	STARTUPINFO info = { sizeof(info) };
	PROCESS_INFORMATION processInfo;

	Game::LookForPlugins();
	if (gDlls[0] == NULL)
		return FALSE;

	// Convert gDlls to an array of LPCSTR
	LPCSTR dllArray[1];
	int dllCount = 0;
	for (int i = 0; i < 1 && gDlls[i] != NULL; ++i) {
		dllArray[dllCount++] = "C:/Users/Johnathon/Downloads/Yu-Gi-Oh-Ex-main/Yu-Gi-Oh-Ex-main/x64/Debug/Plugins/Yu-Gi-Oh-GUI.dll";
	}
	CHAR CurrentDir[MAX_PATH];
	GetCurrentDirectoryA(MAX_PATH, CurrentDir);
	
	//DetourCreateProcess with only gDll[0]
	if (DetourCreateProcessWithDllsA(gGameLocation, NULL, NULL, NULL, FALSE, NULL, NULL, CurrentDir, &info, &processInfo, dllCount, dllArray, NULL) == FALSE)
	{
		//Format GetLastError to String
		auto Err = GetLastError();
		LPVOID lpMsgBuf;
		FormatMessage(
			FORMAT_MESSAGE_ALLOCATE_BUFFER |
			FORMAT_MESSAGE_FROM_SYSTEM |
			FORMAT_MESSAGE_IGNORE_INSERTS,
			NULL,
			Err,
			MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
			(LPTSTR)&lpMsgBuf,
			0, NULL);

		MessageBox(NULL, (LPCTSTR)lpMsgBuf, TEXT("Error"), MB_OK | MB_ICONINFORMATION);
		return FALSE;
	}


}

BOOL Game::Start(LPSTR CustomPath)
{
	if (Game::Check(CustomPath) == FALSE)
		return FALSE;

	STARTUPINFO info = { sizeof(info) };
	PROCESS_INFORMATION processInfo;

	Set_GamePath(CustomPath);

	Game::LookForPlugins();
	if (gDlls[0] == NULL)
		return FALSE;
	//return Status;
}

void Game::LookForPlugins()
{
	// Look in Current Directory for Plugins Folder and search for all DLLs in that folder
	// Find all DLLS in Plugins Folder
	WIN32_FIND_DATAA FindFileData;
	//Get current dir
	CHAR CurrentDir[MAX_PATH];
	GetCurrentDirectoryA(MAX_PATH, CurrentDir);
	//Append Current Dir with Plugins Folder
	strncat(CurrentDir, "\\Plugins\\*.dll", sizeof("\\Plugins\\*.dll"));
	//Search
	HANDLE hFind = FindFirstFileA(CurrentDir, &FindFileData);
	//output CurrentDir to debug
	OutputDebugStringA(CurrentDir);
	DWORD errorMessageID = GetLastError();
	LPSTR messageBuffer = nullptr;
	size_t size = FormatMessageA(
		FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
		NULL, errorMessageID, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (LPSTR)&messageBuffer, 0, NULL);

	OutputDebugStringA(messageBuffer);
	LocalFree(messageBuffer);

	if (hFind == INVALID_HANDLE_VALUE)
	{
		std::cout << "No Plugins Found" << std::endl;
		return;
	}

	CHAR FullPath[MAX_PATH];
	GetCurrentDirectoryA(MAX_PATH, FullPath);
	strncat(FullPath, "\\Plugins\\", sizeof("\\Plugins\\"));

	int i = 0;
	do
	{
		//FullPath of FIleFindData.cFileName
		strncat(FullPath, FindFileData.cFileName, sizeof(FindFileData.cFileName));
		//replace \\ with \ in fullpath
		for (int i = 0; i < sizeof(FullPath); i++)
		{
			if (FullPath[i] == '\\')
				FullPath[i] = '/';
		}
		gDlls[i] = FullPath;
		i++;
	} while (FindNextFileA(hFind, &FindFileData) != 0);

	FindClose(hFind);

	// Show them in console
	for (int i = 0; i < sizeof(gDlls) / sizeof(gDlls[0]); i++)
	{
		if (gDlls[i] == NULL)
			break;

		std::cout << gDlls[i] << std::endl;
	}
}

void Game::Set_GamePath(CHAR Path[MAX_PATH])
{
	strncpy(Game::gGamePath, Path, MAX_PATH);
	strncat(Game::gGameLocation, Path, strlen(Path));
	strncat(Game::gGameLocation, "\\YuGiOh.exe", sizeof("\\YuGiOh.exe"));
}

BOOL Game::Check(LPSTR Path)
{
	return PathFileExistsA(Path);
}