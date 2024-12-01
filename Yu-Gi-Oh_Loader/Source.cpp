#include <Windows.h>
#include "Shlwapi.h"

#include <iostream>
#include "Game.h"

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance,
	LPSTR lpCmdLine, int nCmdShow)
{
	Game::Locate();
	Game::CreateConfig("Config.ini");
	if (Game::Start() == false)
	{
		auto Count = 0;
		LPWSTR CmdLine = GetCommandLineW();

		auto Argument = CommandLineToArgvW(CmdLine, &Count);
		if (Argument[1] != NULL)
		{
			Game::Start(Argument[2]);
		}
	}
}