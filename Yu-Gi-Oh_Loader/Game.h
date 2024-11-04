#pragma once

static class Game
{
public:
	static BOOL Locate();
	static BOOL Start();

	static BOOL Check(LPSTR Path);
	static BOOL Start(LPSTR CustomPath);
	static void LookForPlugins();

	static void Set_GamePath(CHAR Path[MAX_PATH]);

private:
	static TCHAR gGamePath[MAX_PATH];
	static TCHAR gGameLocation[MAX_PATH];
	static LPCSTR gDlls[MAX_PATH];
};
