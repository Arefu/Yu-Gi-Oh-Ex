#include <Windows.h>
#include <iostream>

#include <detours.h>

#include "ImGui/D3D11/D3D11.h"

#include "Detours/Game.h"
#include "ImGui/YuGiOh_ImGui.h"

#include "Yu-Gi-Oh.h"
#include "Memory.h"
#include "Detours/Multiplayer/MPatcher.h"

void StartDetours();
void Stop();

BOOL WINAPI DllMain(HINSTANCE hinstDLL, DWORD fdwReason, LPVOID lpvReserved)
{
	switch (fdwReason)
	{
	case DLL_PROCESS_ATTACH:
		DetourRestoreAfterWith();
		StartDetours();
		break;

	case DLL_PROCESS_DETACH:
		break;
	}

	return TRUE;
}

void StartDetours()
{
	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());
	AllocConsole();

	static_cast<void>(freopen("CONOUT$", "w", stdout));

	Config::Load("Config.ini");

	auto ImGui_D3D11Setup = reinterpret_cast<Address>(0x1408CA830);
	DetourAttach(reinterpret_cast<PVOID*>(&ImGui_D3D11Setup), DirectX::Patch_ImGUi_D3D11Setup);

	auto SetupDuel = reinterpret_cast<Address>(0x1407696E0);
	DetourAttach(reinterpret_cast<PVOID*>(&SetupDuel), Game::Patch_SetupDuel);

	auto DefaultMaxNumberOfCards = reinterpret_cast<Address>(0x14005D120);
	DetourAttach(reinterpret_cast<PVOID*>(&DefaultMaxNumberOfCards), Game::Patch_DefaultMaxNumberOfCards);

	auto IsGameOutOfFocus = reinterpret_cast<Address>(0x14083C9F0);
	DetourAttach(reinterpret_cast<PVOID*>(&IsGameOutOfFocus), Game::Patch_IsGameOutOfFocus);

	auto MultiplayerHost = reinterpret_cast<Address>(0x1408D1110);
	//DetourAttach(reinterpret_cast<PVOID*>(&MultiplayerHost), MPatcher::Patch_MultiplayerHost);

	auto MultiplayerFind = reinterpret_cast<Address>(0x1408D82B0);
	DetourAttach(reinterpret_cast<PVOID*>(&MultiplayerFind), MPatcher::Patch_MultiplayerFind);

	DetourTransactionCommit();

	DirectX::Set_ImGuiD3D11SetupPatchAddress(ImGui_D3D11Setup);
}