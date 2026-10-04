#include <d3d11.h>
#include <detours.h>
#include <dxgi.h>
#include <fstream>
#include <imgui.h>
#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <iostream>
#include <string>
#include <thread>
#include <unordered_map>
#include <windows.h>

#include "Inspector.h"
#include "Logger.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Log.h"
#include "Plugins.h"
#include "Yu-Gi-Oh-Ex.h"

typedef __int64 Address;

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
static HWND g_hWnd = nullptr;
static Address oCreateDeviceAndSwapChain = 0x14090D2B0;
static Address nCreateDeviceAndSwapChain = 0x0;

static Address oPresent = 0x0;
static Address nPresent = 0x0;

static ID3D11RenderTargetView* pMainRenderTargetView = nullptr;
static ID3D11DeviceContext* pContext = nullptr;
static IDXGISwapChain* pSwapChain = nullptr;
static ID3D11Device* pDevice = nullptr;

static WNDPROC oWndProc = nullptr;

static bool bShowMenu = true;
static bool bShowDemo = false;

static bool b_IsImGuiInitialized = false;
static ImGuiContext* _ImGuiContext = nullptr;

// The game renders into a fixed-size back buffer (e.g. 1920x1080) and DXGI stretches it onto
// the window. ImGui must lay out and draw in back-buffer pixels, so window (client) mouse
// coordinates have to be scaled into that space or the hit-test drifts from what is drawn.
static bool GetBackBufferScale(HWND hWnd, float& sx, float& sy, float& bw, float& bh)
{
    if (!pSwapChain)
        return false;
    DXGI_SWAP_CHAIN_DESC sd;
    RECT rect;
    if (FAILED(pSwapChain->GetDesc(&sd)) || !GetClientRect(hWnd, &rect))
        return false;
    const LONG cw = rect.right - rect.left, ch = rect.bottom - rect.top;
    if (cw <= 0 || ch <= 0 || sd.BufferDesc.Width == 0 || sd.BufferDesc.Height == 0)
        return false;
    bw = (float)sd.BufferDesc.Width;
    bh = (float)sd.BufferDesc.Height;
    sx = bw / (float)cw;
    sy = bh / (float)ch;
    return true;
}

static LPARAM ScaleMouseLParam(HWND hWnd, UINT msg, LPARAM lParam)
{
    switch (msg)
    {
    case WM_MOUSEMOVE:
    case WM_LBUTTONDOWN: case WM_LBUTTONUP: case WM_LBUTTONDBLCLK:
    case WM_RBUTTONDOWN: case WM_RBUTTONUP: case WM_RBUTTONDBLCLK:
    case WM_MBUTTONDOWN: case WM_MBUTTONUP: case WM_MBUTTONDBLCLK:
    case WM_XBUTTONDOWN: case WM_XBUTTONUP: case WM_XBUTTONDBLCLK:
    {
        float sx, sy, bw, bh;
        if (!GetBackBufferScale(hWnd, sx, sy, bw, bh))
            return lParam;
        const int x = (int)((short)LOWORD(lParam) * sx);
        const int y = (int)((short)HIWORD(lParam) * sy);
        return MAKELPARAM((WORD)(short)x, (WORD)(short)y);
    }
    default:
        return lParam;
    }
}

LRESULT CALLBACK WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    // Only ImGui sees the scaled coordinates; the game keeps its own client-space lParam.
    if (ImGui_ImplWin32_WndProcHandler(hWnd, msg, wParam, ScaleMouseLParam(hWnd, msg, lParam)))
        return true;

    switch (msg)
    {
    case WM_KEYDOWN:
        switch (wParam)
        {
        case VK_F1:
            bShowMenu = !bShowMenu;
            if (bShowMenu)
                YGO::Log("Menu opened", "Yu-Gi-Oh-GUI", 69);
            else
                YGO::Log("Menu closed", "Yu-Gi-Oh-GUI", 69);
            break;
        case VK_F8:
            bShowDemo = !bShowDemo;
            break;
        }
        break;

    case WM_CLOSE:
        YuGiOhEx::g_bIsQuitReady = true;
        break;

    case WM_MOUSEMOVE: case WM_LBUTTONDOWN: case WM_LBUTTONUP:
    case WM_RBUTTONDOWN: case WM_RBUTTONUP:
    case WM_MBUTTONDOWN: case WM_MBUTTONUP:
    case WM_XBUTTONDOWN: case WM_XBUTTONUP:
    case WM_MOUSEWHEEL: case WM_MOUSEHWHEEL:
        if (ImGui::GetIO().WantCaptureMouse)
            return true;
        break;

    case WM_SIZE:
    {
        if (wParam != SIZE_MINIMIZED && pMainRenderTargetView)
        {
            pMainRenderTargetView->Release();
            pMainRenderTargetView = nullptr;
        }

        LRESULT result = CallWindowProcA(oWndProc, hWnd, msg, wParam, lParam);

        if (wParam != SIZE_MINIMIZED && pDevice && pSwapChain)
        {
            ID3D11Texture2D* pBackBuffer = nullptr;
            pSwapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (LPVOID*)&pBackBuffer);
            if (pBackBuffer)
            {
                pDevice->CreateRenderTargetView(pBackBuffer, NULL, &pMainRenderTargetView);
                pBackBuffer->Release();
            }
        }

        return result;
    }
    default:
        break;
    }

    PluginManager::ProcessInput(hWnd, msg, wParam, lParam);
    return CallWindowProcA(oWndProc, hWnd, msg, wParam, lParam);
}

HRESULT __stdcall YGOGUIPresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags)
{
    ImGui_ImplWin32_NewFrame();

    // ImGui_ImplWin32_NewFrame sets DisplaySize to the client rect; override it with the
    // back-buffer size so the DX11 viewport covers the whole buffer DXGI stretches.
    float sx, sy, bw, bh;
    if (GetBackBufferScale(g_hWnd, sx, sy, bw, bh))
    {
        ImGuiIO& io = ImGui::GetIO();
        io.DisplaySize = ImVec2(bw, bh);

        // The Win32 backend's GetCursorPos fallback (mouse captured/outside the window) queues
        // unscaled client coordinates; re-queue the scaled position so the last event wins.
        POINT pos;
        if (GetForegroundWindow() == g_hWnd && GetCursorPos(&pos) && ScreenToClient(g_hWnd, &pos))
            io.AddMousePosEvent(pos.x * sx, pos.y * sy);
    }

    ImGui_ImplDX11_NewFrame();
    ImGui::NewFrame();

    if (bShowMenu)
    {
        Inspector::Draw(&bShowMenu);
        PluginManager::ProcessGui();
    }

    if (bShowDemo)
        ImGui::ShowDemoWindow(&bShowDemo);

    ImGui::EndFrame();
    ImGui::Render();
    ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());

    return reinterpret_cast<HRESULT(__stdcall*)(IDXGISwapChain*, UINT, UINT)>(nPresent)(pSwapChain, SyncInterval, Flags);
}

HRESULT __stdcall CreateDeviceSwapChainAndSetupDearImGui(
    IDXGIAdapter* pAdapter, D3D_DRIVER_TYPE DriverType, HMODULE Software,
    UINT Flags, const D3D_FEATURE_LEVEL* pFeatureLevels, UINT FeatureLevels,
    UINT SDKVersion, const DXGI_SWAP_CHAIN_DESC* pSwapChainDesc,
    IDXGISwapChain** ppSwapChain, ID3D11Device** ppDevice,
    D3D_FEATURE_LEVEL* pFeatureLevel, ID3D11DeviceContext** ppImmediateContext)
{
    auto result = reinterpret_cast<HRESULT(__stdcall*)(
        IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT,
        const D3D_FEATURE_LEVEL*, UINT, UINT, const DXGI_SWAP_CHAIN_DESC*,
        IDXGISwapChain**, ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**)>
        (nCreateDeviceAndSwapChain)(
            pAdapter, DriverType, Software, Flags, pFeatureLevels,
            FeatureLevels, SDKVersion, pSwapChainDesc, ppSwapChain,
            ppDevice, pFeatureLevel, ppImmediateContext);

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

    ImGui::CreateContext();
    _ImGuiContext = ImGui::GetCurrentContext();
    ImGuiIO& io = ImGui::GetIO();
    (void)io;

    DXGI_SWAP_CHAIN_DESC sd;
    pSwapChain->GetDesc(&sd);

    RECT rect;
    GetClientRect(sd.OutputWindow, &rect);
    io.DisplaySize = ImVec2((float)(rect.right - rect.left), (float)(rect.bottom - rect.top));

    ImGui_ImplWin32_Init(sd.OutputWindow);
    ImGui_ImplDX11_Init(pDevice, pContext);
    g_hWnd = sd.OutputWindow;
    ID3D11Texture2D* pBackBuffer = nullptr;
    pSwapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), (LPVOID*)&pBackBuffer);
    pDevice->CreateRenderTargetView(pBackBuffer, NULL, &pMainRenderTargetView);
    pBackBuffer->Release();

    oWndProc = reinterpret_cast<WNDPROC>(
        SetWindowLongPtrA(sd.OutputWindow, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(WndProc)));

    return result;
}

extern "C" __declspec(dllexport) ImGuiContext* __stdcall Get_ImGuiContext()
{
    if (!ImGui::GetCurrentContext()) return nullptr;
    return ImGui::GetCurrentContext();
}

// -------------------------------------------------------
// DllMain
// -------------------------------------------------------
BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    Logger::SetupLogger();

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        SetProcessDPIAware();
        DetourRestoreAfterWith();
        DetourTransactionBegin();
        DetourAttach(reinterpret_cast<PVOID*>(&oCreateDeviceAndSwapChain), CreateDeviceSwapChainAndSetupDearImGui);
        DetourTransactionCommit();
        nCreateDeviceAndSwapChain = oCreateDeviceAndSwapChain;
        break;
    }
    return TRUE;
}
