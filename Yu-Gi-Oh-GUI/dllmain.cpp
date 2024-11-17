#include <chrono>
#include <d3d11.h>
#include <detours.h>
#include <dxgi.h>
#include <imgui.h>
#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <iostream>
#include <thread>
#include <string>
#include <windows.h>

typedef __int64 Address;

//typedef HRESULT(__stdcall* Present)(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags);

static Address oCreateDeviceAndSwapChain = 0x14090D2B0;
static Address nCreateDeviceAndSwapChain = 0x0;

static Address oPresent = 0x0;
static Address nPresent = 0x0;


HRESULT __stdcall CreateDeviceSwapChainAndSetupDearImGui(IDXGIAdapter* pAdapter, D3D_DRIVER_TYPE DriverType, HMODULE Software, UINT Flags, const D3D_FEATURE_LEVEL* pFeatureLevels, UINT FeatureLevels, UINT SDKVersion, const DXGI_SWAP_CHAIN_DESC* pSwapChainDesc, IDXGISwapChain** ppSwapChain, ID3D11Device** ppDevice, D3D_FEATURE_LEVEL* pFeatureLevel, ID3D11DeviceContext** ppImmediateContext)
{
	auto result = reinterpret_cast<HRESULT(__stdcall*)(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT, const D3D_FEATURE_LEVEL*, UINT, UINT, const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**, ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**)>(nCreateDeviceAndSwapChain)(pAdapter, DriverType, Software, Flags, pFeatureLevels, FeatureLevels, SDKVersion, pSwapChainDesc, ppSwapChain, ppDevice, pFeatureLevel, ppImmediateContext);
	
	//Get VMT of SwapChain
	IDXGISwapChain* pSwapChain = *ppSwapChain;
	void** vmt = *(void***)(pSwapChain);

	//Get Present function address
	oPresent = reinterpret_cast<Address>(vmt[8]);

	//Show Present Address messagebox
	std::string message = "Present function address: " + std::to_string(oPresent);
	MessageBoxA(NULL, message.c_str(), "Present Address", MB_OK);

	return result;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		DetourRestoreAfterWith();

		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());
		
		DetourAttach(reinterpret_cast<PVOID*>(&oCreateDeviceAndSwapChain), CreateDeviceSwapChainAndSetupDearImGui);
		DetourTransactionCommit();

		//Update nCreateDeviceAndSwapChain with the address of where the original function is stored
		nCreateDeviceAndSwapChain = oCreateDeviceAndSwapChain;

		break;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}