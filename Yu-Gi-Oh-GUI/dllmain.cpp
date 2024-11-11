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


typedef HRESULT(__stdcall* PresentFn)(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags);

IDXGISwapChain* g_pSwapChain = reinterpret_cast<IDXGISwapChain*>(0x14332D380);
ID3D11Device* g_pD3D11Device = reinterpret_cast<ID3D11Device*>(0x14332BBB8);
ID3D11DeviceContext* g_pD3D11DeviceContext = nullptr;

PresentFn pPresentAddress = nullptr;


void Start_DearImGui(IDXGISwapChain* pChain, UINT SyncInterval, UINT Flags)
{
	static bool initialized = false;
	if (!initialized) {
		// Initialize ImGui for DirectX 11 and Win32
		g_pD3D11Device->GetImmediateContext(&g_pD3D11DeviceContext);

		ImGui::CreateContext();
		ImGui_ImplDX11_Init(g_pD3D11Device, g_pD3D11DeviceContext);
		ImGui_ImplWin32_Init(GetActiveWindow());
		initialized = true;
	}

	// Start ImGui frame
	ImGui_ImplDX11_NewFrame();
	ImGui_ImplWin32_NewFrame();
	ImGui::NewFrame();

	// Draw UI
	ImGui::Begin("Overlay");
	ImGui::Text("Hello, world!");
	ImGui::End();

	// Render ImGui
	ImGui::Render();
	ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());

	 pPresentAddress(pChain, SyncInterval, Flags);
}

void WaitForSwapChainInitialization()
{
	//We should be able to use a loop to poll and see if we have a reasonable value as well, but for now, I'm lazy to get a PoC working.
	std::this_thread::sleep_for(std::chrono::seconds(1));

	void** vTable = *reinterpret_cast<void***>(g_pSwapChain);
	pPresentAddress = reinterpret_cast<PresentFn>(vTable[8]);
	
	std::cout << "IDXGISwapChain initialized at address: " << g_pSwapChain << std::endl;
	std::cout << "IDXGISwapChain vTable address: " << vTable << std::endl;
	std::cout << "Present function address: " << pPresentAddress << std::endl;

	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());

	DetourAttach(&(PVOID&)pPresentAddress, Start_DearImGui);
	system("pause");
	DetourTransactionCommit();
	std::cout << "New present function address: " << pPresentAddress << std::endl;

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