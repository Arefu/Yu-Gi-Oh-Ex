#include <Windows.h>
#include "Shlwapi.h"

#include <iostream>
#include "Game.h"

int APIENTRY WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nShowCmd)
{

	Game::Locate();

	Game::Start(false);

//	Game::Start(true);

}