#include <Windows.h>
#include <iostream>

#include <detours.h>

#include "ImGui/D3D11/D3D11.h"

#include "Detours/Game.h"
#include "ImGui/YuGiOh_ImGui.h"

#include "Yu-Gi-Oh.h"
#include "Memory.h"

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

	g_Config.Load("Config.ini");

	Address ImGui_D3D11Setup = reinterpret_cast<Address>(0x1408CA830);
	DetourAttach((PVOID*)(&ImGui_D3D11Setup), DirectX::Patch_ImGUi_D3D11Setup);

	Address SetupDuel = reinterpret_cast<Address>(0x1407696E0);
	DetourAttach((PVOID*)(&SetupDuel), Game::Patch_SetupDuel);

	Address DefaultMaxNumberOfCards = reinterpret_cast<Address>(0x14005D120);
	DetourAttach((PVOID*)(&DefaultMaxNumberOfCards), Game::Patch_DefaultMaxNumberOfCards);

	Address IsGameOutOfFocus = reinterpret_cast<Address>(0x14083C9F0);
	DetourAttach((PVOID*)(&IsGameOutOfFocus), Game::Patch_IsGameOutOfFocus);

	DetourTransactionCommit();

	DirectX::Set_ImGuiD3D11SetupPatchAddress(ImGui_D3D11Setup);
}