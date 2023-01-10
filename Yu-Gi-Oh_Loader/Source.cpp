#include <Windows.h>
#include "Shlwapi.h"

#include "Game.h"

int APIENTRY WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nShowCmd)
{
	if (lpCmdLine)
		Game::Start(lpCmdLine);

	if (Game::Locate() == FALSE)
		exit(ERROR_FILE_NOT_FOUND);
	if (Game::Start() == FALSE)
		exit(ERROR_PROCESS_ABORTED);

	return 0;
}