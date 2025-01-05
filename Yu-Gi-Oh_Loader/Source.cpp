#include <Windows.h>
#include <Shlwapi.h>
#include <string>
#include "Game.h"

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow)
{
	Game::Locate();
	Game::CreateConfig("Config.ini");
	Game::Start();
}