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

static ID3D11RenderTargetView* pMainRenderTargetView = nullptr;
static ID3D11DeviceContext* pContext = nullptr;
static IDXGISwapChain* pSwapChain = nullptr;
static ID3D11Device* pDevice = nullptr;


static BOOL g_bInitialised = FALSE;

HRESULT __stdcall YGOGUIPresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags)
{
	if (!g_bInitialised)
	{
		//We're Ready to Setup Dear-ImGui
        ImGui::CreateContext();
        ImGuiIO& io = ImGui::GetIO(); (void)io;
        io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;

        DXGI_SWAP_CHAIN_DESC sd;
        pSwapChain->GetDesc(&sd);

        ImGui_ImplWin32_Init(sd.OutputWindow);
        ImGui_ImplDX11_Init(pDevice, pContext);

        ImGui::GetIO().ImeWindowHandle = sd.OutputWindow;

        ID3D11Texture2D* pBackBuffer = nullptr;

        pSwapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (LPVOID*)&pBackBuffer);
        pDevice->CreateRenderTargetView(pBackBuffer, NULL, &pMainRenderTargetView);

        pBackBuffer->Release();

        g_bInitialised = true;
		MessageBoxA(NULL, "Dear ImGui Initialised", "Yu-Gi-Oh! GUI", MB_OK);
	}

    ImGui_ImplWin32_NewFrame();
    ImGui_ImplDX11_NewFrame();

    ImGui::NewFrame();

  //Do I'm Gui Things Here.

    ImGui::EndFrame();

    ImGui::Render();

    pContext->OMSetRenderTargets(1, &pMainRenderTargetView, NULL);
    ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());

	//Call Origianl Present
	auto result = reinterpret_cast<HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT)>(nPresent)(pSwapChain, SyncInterval, Flags);

    return result;
}

HRESULT __stdcall CreateDeviceSwapChainAndSetupDearImGui(IDXGIAdapter* pAdapter, D3D_DRIVER_TYPE DriverType, HMODULE Software, UINT Flags, const D3D_FEATURE_LEVEL* pFeatureLevels, UINT FeatureLevels, UINT SDKVersion, const DXGI_SWAP_CHAIN_DESC* pSwapChainDesc, IDXGISwapChain** ppSwapChain, ID3D11Device** ppDevice, D3D_FEATURE_LEVEL* pFeatureLevel, ID3D11DeviceContext** ppImmediateContext)
{
	auto result = reinterpret_cast<HRESULT(__stdcall*)(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT, const D3D_FEATURE_LEVEL*, UINT, UINT, const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**, ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**)>(nCreateDeviceAndSwapChain)(pAdapter, DriverType, Software, Flags, pFeatureLevels, FeatureLevels, SDKVersion, pSwapChainDesc, ppSwapChain, ppDevice, pFeatureLevel, ppImmediateContext);
	

	pDevice = *ppDevice;
	pContext = *ppImmediateContext;
    pSwapChain = *ppSwapChain;
	void** vmt = *(void***)(pSwapChain);
	oPresent = reinterpret_cast<Address>(vmt[8]);

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());

    DetourAttach(reinterpret_cast<PVOID*>(&oPresent), YGOGUIPresent);
    
    DetourTransactionCommit();
	nPresent = oPresent;

	return result;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    LONG res = 0;
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        DetourRestoreAfterWith();

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        
        DetourAttach(reinterpret_cast<PVOID*>(&oCreateDeviceAndSwapChain), CreateDeviceSwapChainAndSetupDearImGui);
    
        DetourTransactionCommit();
        nCreateDeviceAndSwapChain = oCreateDeviceAndSwapChain;

      
        


        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}