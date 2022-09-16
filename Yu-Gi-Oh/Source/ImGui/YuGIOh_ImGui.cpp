#include "ImGui/YuGiOh_ImGui.h"

#include "imgui.h"
#include "imgui_impl_win32.h"
#include "imgui_impl_dx11.h"
#include "ImGui/Windows/Connect To Server/Connect.h"

Address YuGiOh_ImGui::_ImGuiD3D11SetupPatchAddres;
BOOL YuGiOh_ImGui::g_bInitialised;
BOOL YuGiOh_ImGui::g_ShowMenu = FALSE;
bool YuGiOh_ImGui::g_ShowConsole = false;
ID3D11RenderTargetView* DirectX::pmainRenderTargetView;
WNDPROC YuGiOh_ImGui::_WndProcHandler;

#define IMGUI_DISABLE_OBSOLETE_KEYIO

void YuGiOh_ImGui::Start_DearImGui(IDXGISwapChain* pChain, UINT SyncInterval, UINT Flags)
{
	auto DirectX_PresentFunc = (PresentFunc)YuGiOh_ImGui::im_gui_present_address;

	if (!g_bInitialised)
	{
		if (FAILED(GetDeviceAndCtxFromSwapchain(pChain, &DirectX::pD3D11Device, &DirectX::pD3D11DeviceContext)))
			return DirectX_PresentFunc(pChain, SyncInterval, Flags);

		DirectX::pD3D11SwapChain = pChain;
		DXGI_SWAP_CHAIN_DESC sd;
		pChain->GetDesc(&sd);
		ImGui::CreateContext();
		ImGuiIO& io = ImGui::GetIO(); (void)io;
		io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;

		YuGiOh_ImGui::_WndProcHandler = (WNDPROC)SetWindowLongPtr(sd.OutputWindow, GWLP_WNDPROC, (LONG_PTR)YuGiOh_ImGui::Hooked_YuGiOhImGuiWndProc);

		ImGui_ImplWin32_Init(sd.OutputWindow);
		ImGui_ImplDX11_Init(DirectX::pD3D11Device, DirectX::pD3D11DeviceContext);
		ImGui::GetIO().ImeWindowHandle = sd.OutputWindow;

		ID3D11Texture2D* pBackBuffer = nullptr;

		pChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (LPVOID*)&pBackBuffer);
		DirectX::pD3D11Device->CreateRenderTargetView(pBackBuffer, NULL, &DirectX::pmainRenderTargetView);

		pBackBuffer->Release();

		g_bInitialised = true;
	}

	ImGui_ImplWin32_NewFrame();
	ImGui_ImplDX11_NewFrame();

	ImGui::NewFrame();

	if (YuGiOh_ImGui::g_ShowMenu == TRUE)
	{
		ImGui::ShowDemoWindow();
	}
	if (YuGiOh_ImGui::g_ShowConsole == TRUE)
	{
		Console::ShowConsole(&YuGiOh_ImGui::g_ShowConsole);
	}
	if (YuGiOh_ImGui::g_ShowConnectWindow == TRUE)
	{
		Connect::Show_ConnectWindow(&YuGiOh_ImGui::g_ShowConnectWindow);
	}

	ImGui::EndFrame();

	ImGui::Render();

	DirectX::pD3D11DeviceContext->OMSetRenderTargets(1, &DirectX::pmainRenderTargetView, NULL);
	ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());

	return DirectX_PresentFunc(pChain, SyncInterval, Flags);
}

void YuGiOh_ImGui::Set_ImGui_PresentPatchAddress(Address Address)
{
	YuGiOh_ImGui::_ImGuiD3D11SetupPatchAddres = Address;
}

HRESULT YuGiOh_ImGui::GetDeviceAndCtxFromSwapchain(IDXGISwapChain* pSwapChain, ID3D11Device** ppDevice, ID3D11DeviceContext** ppContext) {
	HRESULT ret = pSwapChain->GetDevice(__uuidof(ID3D11Device), (PVOID*)ppDevice);

	if (SUCCEEDED(ret))
		(*ppDevice)->GetImmediateContext(ppContext);

	return ret;
}

LRESULT YuGiOh_ImGui::Hooked_YuGiOhImGuiWndProc(HWND hWnd, UINT uMsg, WPARAM wParam, LPARAM lParam)
{
	ImGuiIO& io = ImGui::GetIO();
	POINT mPos;
	GetCursorPos(&mPos);
	ScreenToClient(GetForegroundWindow(), &mPos);
	ImGui::GetIO().MousePos.x = mPos.x;
	ImGui::GetIO().MousePos.y = mPos.y;

	if (uMsg == WM_KEYUP)
	{
		if (wParam == VK_F8)
		{
			*(long long*)0x14332A391 = 1;
		}
		if (wParam == VK_F1)
		{
			YuGiOh_ImGui::g_ShowMenu = !YuGiOh_ImGui::g_ShowMenu;
		}
		if (wParam == VK_OEM_3)
		{
			YuGiOh_ImGui::g_ShowConsole = !YuGiOh_ImGui::g_ShowConsole;
		}
	}

	if (YuGiOh_ImGui::g_ShowMenu || YuGiOh_ImGui::g_ShowConsole || YuGiOh_ImGui::g_ShowConnectWindow)
	{
		ImGui_ImplWin32_WndProcHandler(hWnd, uMsg, wParam, lParam);
		return true;
	}
	else
		return CallWindowProc(YuGiOh_ImGui::_WndProcHandler, hWnd, uMsg, wParam, lParam);
}