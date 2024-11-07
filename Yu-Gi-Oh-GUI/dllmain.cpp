#include <chrono>
#include <d3d11.h>
#include <detours.h>
#include <dxgi.h>
#include <imgui.h>
#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <iostream>
#include <thread>
#include <windows.h>


typedef void(__stdcall* PresentFunc)(IDXGISwapChain* pChain, UINT SyncInterval, UINT Flags);

IDXGISwapChain* g_pSwapChain = nullptr;
ID3D11Device* g_pD3D11Device = nullptr;
ID3D11DeviceContext* g_pD3D11DeviceContext = nullptr;;
 ID3D11RenderTargetView* g_pmainRenderTargetView = nullptr;

uintptr_t pSwapChainAddress = 0x14332D380;
uintptr_t pDeviceAddress = 0x14332BBB8;
uintptr_t pPresentAddress = 0x0;

BOOL g_bInitialised = FALSE;

void Start_DearImGui(IDXGISwapChain* pChain, UINT SyncInterval, UINT Flags)
{
	std::cout << "Dear ImGui started!" << std::endl;
	auto DirectX_PresentFunc = (PresentFunc)pPresentAddress;

	if (!g_bInitialised)
	{
		if (FAILED(pChain->GetDevice(__uuidof(g_pD3D11Device), (void**)&g_pD3D11Device)))
		{
			std::cout << "Failed to get D3D11 device!" << std::endl;
			return;
		}

		g_pD3D11Device->GetImmediateContext(&g_pD3D11DeviceContext);

		DXGI_SWAP_CHAIN_DESC sd;
		pChain->GetDesc(&sd);

		ID3D11Texture2D* pBackBuffer = nullptr;
		pChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (LPVOID*)&pBackBuffer);
		g_pD3D11Device->CreateRenderTargetView(pBackBuffer, nullptr, &g_pmainRenderTargetView);
		pBackBuffer->Release();

		ImGui::CreateContext();
		ImGuiIO& io = ImGui::GetIO(); (void)io;
		ImGui::StyleColorsDark();

		HWND hwnd = FindWindowA("UnityWndClass", nullptr);
		ImGui_ImplWin32_Init(GetCurrentWindow());
		ImGui_ImplDX11_Init(g_pD3D11Device, g_pD3D11DeviceContext);


		g_bInitialised = true;
	}
}

void WaitForSwapChainInitialization()
{
	//We should be able to use a loop to poll and see if we have a reasonable value as well, but for now, I'm lazy to get a PoC working.
	std::this_thread::sleep_for(std::chrono::seconds(1));

	g_pSwapChain = reinterpret_cast<IDXGISwapChain*>(pSwapChainAddress);
	
	std::cout << "IDXGISwapChain initialized at address: " << g_pSwapChain << std::endl;

	pPresentAddress = *(uintptr_t*)(g_pSwapChain)+0x8 * sizeof(uintptr_t);
	std::cout << "Present function address: " << pPresentAddress << std::endl;


	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());

	DetourAttach((PVOID*)pPresentAddress, Start_DearImGui);

	DetourTransactionCommit();
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	DetourRestoreAfterWith();
	AllocConsole();
	FILE* f;
	freopen_s(&f, "CONOUT$", "w", stdout);
	freopen_s(&f, "CONOUT$", "w", stderr);


	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		std::thread(WaitForSwapChainInitialization).detach();

		break;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}