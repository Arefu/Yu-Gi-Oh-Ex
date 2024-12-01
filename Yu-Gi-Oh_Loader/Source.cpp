#include <Windows.h>
#include "Shlwapi.h"

#include <iostream>
#include "Game.h"

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance,
	PSTR lpCmdLine, int nCmdShow)
{
	Game::Locate();
	Game::CreateConfig("Config.ini");
	return Game::Start();
}