#include <Windows.h>
#include "Shlwapi.h"

#include <iostream>
#include "Game.h"

int APIENTRY WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nShowCmd)
{
	if (lpCmdLine)
		Game::Start(lpCmdLine);

	Game::Locate();

	Game::Start();

		system("pause");
}