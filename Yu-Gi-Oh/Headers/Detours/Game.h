#pragma once

#include <Windows.h>
#include <detours.h>

#include "Types/Scenes.h"
#include "Types/CardID.h"
#include "Yu-Gi-Oh.h"
#include "Config.h"

class Game
{
public:
	static QWORD Patch_SetupDuel();
	static QWORD Patch_DefaultMaxNumberOfCards(unsigned int UNK);
	static QWORD Patch_IsGameOutOfFocus();

private:
};
