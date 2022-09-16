#pragma once

#include <Windows.h>
#include <iostream>
#include <d3d11.h>

#include "imgui_impl_win32.h"
#include "imgui_impl_dx11.h"
#include "imgui.h"

#include "ImGui/Windows/Console/Console.h"
#include "ImGui/D3D11/D3D11.h"

typedef INT64(__stdcall* Address)();
typedef void(__stdcall* PresentFunc)(IDXGISwapChain* pChain, UINT SyncInterval, UINT Flags);

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

class YuGiOh_ImGui
{
public:
	static void Start_DearImGui(IDXGISwapChain* pChain, UINT SyncInterval, UINT Flags);

	static void Set_ImGui_PresentPatchAddress(Address Address);
	static Address im_gui_present_address;
	static BOOL g_ShowConnectWindow;
	static LRESULT CALLBACK Hooked_YuGiOhImGuiWndProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam);
private:
	static HRESULT GetDeviceAndCtxFromSwapchain(IDXGISwapChain* pSwapChain, ID3D11Device** ppDevice, ID3D11DeviceContext** ppContext);
	static Address _ImGuiD3D11SetupPatchAddres;
	static BOOL g_bInitialised;
	static BOOL g_ShowMenu;
	static bool g_ShowConsole;
	static WNDPROC _WndProcHandler;
};
