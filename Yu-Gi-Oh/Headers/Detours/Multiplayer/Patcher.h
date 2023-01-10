#pragma once

#include "ImGui/Windows/Console/Console.h"
#include "ImGui/Windows/Connect To Server/Connect.h"

#include "Utilities/Memory.h"
#include "Types/Scenes.h"

#include<winsock.h>

#pragma comment(lib,"ws2_32.lib") //Winsock Library

class MPatcher
{
public:
	static VOID Patch_MultiplayerHost(__int64 a1);
	static __int64 Patch_MultiplayerFind(__int64 a1, __int64* a2);
private:
};
