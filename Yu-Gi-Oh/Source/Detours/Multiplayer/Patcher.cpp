#include "Detours/Multiplayer/Patcher.h"

#include <cstdio>

#include "ImGui/YuGiOh_ImGui.h"

bool YuGiOh_ImGui::g_ShowConnectWindow;

VOID MPatcher::Patch_MultiplayerHost(__int64 a1)
{
	Console::WriteMessage("[info] Multiplayer Host Session Controller Fired");
	Console::WriteMessage("[warn] Multiplayer is not fully implimented yet, there WILL be bugs");
	Console::WriteMessage("[info] Current Scene: %d", Memory::GetSceneValue());
	Console::WriteMessage("[info] Setting Scene: %d", Types::Scene::Scene_LoadScreen);
	Memory::SetSceneValue(Types::Scene::Scene_LoadScreen);

	return VOID();
}

__int64 MPatcher::Patch_MultiplayerFind(__int64 a1, __int64* a2)
{
	Console::WriteMessage("[info] Multiplayer Find Session Controller Fired");
	Console::WriteMessage("[warn] Multiplayer is not fully implimented yet, there WILL be bugs");
	
	YuGiOh_ImGui::g_ShowConnectWindow = true;

	return 5;
}