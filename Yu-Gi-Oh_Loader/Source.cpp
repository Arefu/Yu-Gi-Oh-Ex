#include <Windows.h>

#include "Game.h"

int APIENTRY WinMain(HINSTANCE hInst, HINSTANCE hInstPrev, PSTR cmdline, int cmdshow)
{
	if (Game::Locate() == FALSE)
		exit(ERROR_FILE_NOT_FOUND);
	if (Game::Start() == FALSE)
		exit(ERROR_PROCESS_ABORTED);

	return 0;
}