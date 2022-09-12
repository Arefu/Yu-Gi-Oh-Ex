#pragma once

#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <d3d11.h>

#include "ImGui/YuGiOh_ImGui.h"

typedef HRESULT(D3D11Present_Function)(IDXGISwapChain* pThis, UINT SyncInterval, UINT Flags);
typedef INT64(__stdcall* Address)();

class DirectX
{
public:
	static void Patch_ImGUi_D3D11Setup(BYTE* a1);

	static void Set_ImGuiD3D11SetupPatchAddress(Address Address);

	static IDXGISwapChain* pD3D11SwapChain;
	static ID3D11Device* pD3D11Device;
	static ID3D11DeviceContext* pD3D11DeviceContext;
	static ID3D11RenderTargetView* pmainRenderTargetView;
private:
	static Address _ImGuiD3D11SetupPatchAddres;
};
