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

#include "Logger.h"
#include "Plugins.h"
#include "YuGiOh/YuGiOh-CARDS.h"
#include "YuGiOh/YuGiOh-GAME.h"
#include "Yu-Gi-Oh-Ex.h"

typedef __int64 Address;

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

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

Player g_Player1 = Player(PLAYER_ONE);
Player g_Player2 = Player(PLAYER_TWO);

bool PluginManager::_IsLoaded;
std::unordered_map<std::string, bool> PluginManager::m_PluginEnabled;

static bool DoIStart = false;

LRESULT CALLBACK WndProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (ImGui_ImplWin32_WndProcHandler(hWnd, msg, wParam, lParam))
        return true;

    if (ImGui::GetIO().WantCaptureMouse)
        return true;

    switch (msg)
    {
    case WM_KEYDOWN:
        switch (wParam)
        {
        case VK_F1:
            bShowMenu = !bShowMenu;
            if (bShowMenu)
                std::cout << "[Yu-Gi-Oh-GUI] Menu Opened" << std::endl;
            else
                std::cout << "[Yu-Gi-Oh-GUI] Menu Closed" << std::endl;
            break;
        case VK_F8:
            bShowDemo = !bShowDemo;
            break;
        }
        break;

    case WM_CLOSE:
        YuGiOhEx::g_bIsQuitReady = true;
        break;
    }

    PluginManager::ProcessInput(hWnd, msg, wParam, lParam);
    return CallWindowProcA(oWndProc, hWnd, msg, wParam, lParam);
}

HRESULT __stdcall YGOGUIPresent(IDXGISwapChain* pSwapChain, UINT SyncInterval, UINT Flags)
{
    ImGui_ImplWin32_NewFrame();
    ImGui_ImplDX11_NewFrame();
    ImGui::NewFrame();

    if (bShowMenu)
    {
        ImGui::Begin("Yu-Gi-Oh!", &bShowMenu);
        ImGui::Text("Yu-Gi-Oh-Ex: WolfX");
        ImGui::Separator();

        if (ImGui::Button("Quit Game"))
            YuGiOhEx::g_bIsQuitReady = true;

        ImGui::BeginGroup();
        if (ImGui::CollapsingHeader("Player One"))
        {
            ImGui::Text("Number of Cards in Hand: %d", g_Player1.Get_NumberOfCardsInHand());
            if (ImGui::TreeNodeEx("Cards in Hand"))
            {
                for (int i = 0; i < g_Player1.Get_NumberOfCardsInHand(); i++)
                {
                    ImGui::Text("Card %d: %ls (%d)", i,
                        YuGiOhEx::Get_CardNameFromKonamiID(g_Player1.Get_CardInHand(i)),
                        g_Player1.Get_CardInHand(i));
                    if (ImGui::IsItemHovered())
                    {
                        ImGui::BeginTooltip();
                        ImGui::PushTextWrapPos(ImGui::GetFontSize() * 35.0f);
                        ImGui::TextWrapped("%ls", YuGiOhEx::Get_CardDescFromKonamiID(g_Player1.Get_CardInHand(i)));
                        ImGui::PopTextWrapPos();
                        ImGui::EndTooltip();
                    }
                }
                ImGui::TreePop();
            }

            ImGui::Text("Number of Cards in Deck: %d", g_Player1.Get_NumberOfCardsInDeck());
            if (ImGui::TreeNodeEx("Cards in Deck"))
            {
                for (int i = 0; i < g_Player1.Get_NumberOfCardsInDeck(); i++)
                {
                    auto Get_CardNameFromID = reinterpret_cast<LPCTSTR(__fastcall*)(short)>(0x14076D0F0);
                    LPCTSTR name = Get_CardNameFromID(g_Player1.Get_CardInDeck(i));
                    ImGui::Text("Card %d: %ls (%d)", i, name, g_Player1.Get_CardInDeck(i));
                }
                ImGui::TreePop();
            }

            ImGui::Text("Number Of Cards in Grave Yard: %d", g_Player1.Get_NumberOfCardsInGraveYard());
            if (ImGui::TreeNodeEx("Cards in Grave Yard"))
            {
                for (int i = 0; i < g_Player1.Get_NumberOfCardsInGraveYard(); i++)
                    ImGui::Text("Card %d: %d", i, g_Player1.Get_CardInGraveYard(i));
                ImGui::TreePop();
            }

            ImGui::Text("Number Of Cards in Discard Pile: %d", g_Player1.Get_NumberOfDiscardPile());
            if (ImGui::TreeNodeEx("Cards in Discard Pile"))
            {
                for (int i = 0; i < g_Player1.Get_NumberOfDiscardPile(); i++)
                    ImGui::Text("Card %d: %d", i, g_Player1.Get_CardInDiscardPile(i));
                ImGui::TreePop();
            }
        }

        if (ImGui::CollapsingHeader("Duel Manipulation"))
        {
            if (ImGui::TreeNode("Deck List"))
            {
                for (int i = 1; i < 700; i++)
                {
                    auto Deck = YGO::GAME::Get_DeckTemplateAtIndex(i);
                    if (!Deck) continue;

                    ImGui::Text("Name: %ls", Deck->Name);
                    ImGui::Text("Number of Cards in Main Deck: %d", Deck->NumberOfCardsInMainDeck);
                    ImGui::Text("Number of Cards in Extra Deck: %d", Deck->NumberOfCardsInExtraDeck);
                    ImGui::Text("Number of Cards in Side Deck: %d", Deck->NumberOfCardsInSideDeck);
                    if (ImGui::CollapsingHeader("Main Deck Cards"))
                    {
                        for (int j = 0; j < Deck->NumberOfCardsInMainDeck; j++)
                            ImGui::Text("Card %ws", YGO::CARDS::Get_CardNameFromKonamiId(Deck->CardsInMainDeck[j]));
                    }
                }
                ImGui::TreePop();
            }
        }
        ImGui::EndGroup();

        ImGui::Separator();

        ImGui::BeginGroup();
        if (ImGui::CollapsingHeader("Debug Mode", ImGuiTreeNodeFlags_DefaultOpen))
        {
            static bool bPluginsInitialized = false;
            if (!bPluginsInitialized)
            {
                bPluginsInitialized = true;
                CHAR lastLoaded[1024];
                GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "LoadedPlugins", "", lastLoaded, 1024, ".\\Config.ini");
                std::string lastLoadedStr(lastLoaded);
                for (auto& Plugin : PluginManager::ScanForPlugins())
                {
                    std::string search = " " + Plugin + " ";
                    std::string padded = " " + lastLoadedStr + " ";
                    PluginManager::m_PluginEnabled[Plugin] = (padded.find(search) != std::string::npos);
                }
            }

            for (auto& Plugin : PluginManager::ScanForPlugins())
                ImGui::Checkbox(Plugin.c_str(), &PluginManager::m_PluginEnabled[Plugin]);

            if (ImGui::Button("Load Plugins"))
            {
                if (!PluginManager::_IsLoaded)
                {
                    std::string enabledList;
                    for (auto& [name, enabled] : PluginManager::m_PluginEnabled)
                    {
                        if (enabled)
                        {
                            if (!enabledList.empty()) enabledList += " ";
                            enabledList += name;
                        }
                    }
                    WritePrivateProfileStringA("Yu-Gi-Oh-GUI", "LoadedPlugins", enabledList.c_str(), ".\\Config.ini");
                    PluginManager::Load();
                    PluginManager::ProcessConfigForPlugin();
                    PluginManager::ProcessDetours();
                }
            }
        }
        ImGui::EndGroup();

        ImGui::Separator();
        ImGui::BeginGroup();
        if (ImGui::CollapsingHeader("UI Witchcraft"))
        {
            uintptr_t base = (uintptr_t)GetModuleHandle(NULL);
            uintptr_t App = *(uintptr_t*)(base + 0x29275D8);
            uintptr_t MainContext = *(uintptr_t*)(App + 0x1F0);

            if (MainContext == 0)
            {
                ImGui::TextColored(ImVec4(1, 0, 0, 1), "MainContext not ready yet");
            }
            else
            {
                // String lookup helper
                auto GetString = [](int id) -> wchar_t*
                    {
                        uintptr_t langIndex = *(int*)0x14332A344;
                        uintptr_t bundleBase = *(uintptr_t*)(0x143329E80 + langIndex * 8);
                        uintptr_t offsetTable = *(uintptr_t*)(bundleBase + 8);
                        return *(wchar_t**)(offsetTable + 8 * id - 8);
                    };

                // Scan for menu button labels - try ranges around what we know
                if (ImGui::CollapsingHeader("String Scanner"))
                {
                    static int scanStart = 800;
                    static int scanEnd = 850;
                    ImGui::InputInt("Start", &scanStart);
                    ImGui::InputInt("End", &scanEnd);

                    for (int i = scanStart; i < scanEnd; i++)
                    {
                        {
                            wchar_t* str = GetString(i);
                            if (str && str[0] != L'\0')
                                ImGui::Text("%d: %ls", i, str);
                        }
                    }
                }

                uintptr_t ScreenMainMenu = *(uintptr_t*)(MainContext + 0x088);
                uintptr_t ScreenPause = *(uintptr_t*)(MainContext + 0x138);
                uintptr_t ScreenSwitcher = *(uintptr_t*)(MainContext + 0x18);

                ImGui::Text("MainContext:    0x%llX", MainContext);
                ImGui::Text("ScreenMainMenu: 0x%llX", ScreenMainMenu);
                ImGui::Text("ScreenPause:    0x%llX", ScreenPause);
                ImGui::Text("ScreenSwitcher: 0x%llX", ScreenSwitcher);

                if (ScreenSwitcher)
                {
                    ImGui::Text("Is Transitioning: %d", *(int*)(ScreenSwitcher + 88));
                    ImGui::Text("Transition State: %d", *(int*)(ScreenSwitcher + 120));
                    ImGui::Text("Duration:         %.3f", *(float*)(ScreenSwitcher + 72));
                }

                ImGui::Separator();

                auto NavigateToScreen = reinterpret_cast<char(__fastcall*)(__int64, int, double, int, char)>
                    (0x1408087A0);
                auto OnMenuItemSelected = reinterpret_cast<void(__fastcall*)(__int64, __int64, char)>
                    (0x140856C40);

                if (ScreenMainMenu)
                {
                    if (ScreenMainMenu)
                    {
                        uintptr_t arrayData = *(uintptr_t*)(ScreenMainMenu + 0x2E0);
                        uintptr_t arrayWrite = *(uintptr_t*)(ScreenMainMenu + 0x2E8);
                        uintptr_t arrayEnd = *(uintptr_t*)(ScreenMainMenu + 0x2F0);

                        int currentCount = (arrayWrite - arrayData) / 24;
                        int allocatedSlots = (arrayEnd - arrayData) / 24;

                        ImGui::Text("Button count:     %d", currentCount);
                        ImGui::Text("Allocated slots:  %d", allocatedSlots);
                    }
                    if (ScreenMainMenu)
                    {
                        int currentPage = *(int*)(ScreenMainMenu + 832);
                        ImGui::Text("Current page: %d", currentPage);

                        uintptr_t arr1Data = *(uintptr_t*)(ScreenMainMenu + 0x2F8);
                        uintptr_t arr1Write = *(uintptr_t*)(ScreenMainMenu + 0x300);
                        uintptr_t arr2Data = *(uintptr_t*)(ScreenMainMenu + 0x310);
                        uintptr_t arr2Write = *(uintptr_t*)(ScreenMainMenu + 0x318);
                        uintptr_t arr3Data = *(uintptr_t*)(ScreenMainMenu + 0x328);
                        uintptr_t arr3Write = *(uintptr_t*)(ScreenMainMenu + 0x330);

                        ImGui::Text("Array 1 count: %d", (arr1Write - arr1Data) / 4);
                        ImGui::Text("Array 2 count: %d", (arr2Write - arr2Data) / 4);
                        ImGui::Text("Array 3 count: %d", (arr3Write - arr3Data) / 4);
                    }
                    ImGui::Text("ScreenMainMenu Case Tester");
                    for (int i = 0; i <= 11; i++)
                    {
                        char label[32];
                        sprintf_s(label, "Case %d", i);
                        if (ImGui::Button(label))
                            OnMenuItemSelected(ScreenMainMenu, i, 1);
                        if (i % 2 == 0) ImGui::SameLine();
                    }

                    ImGui::Separator();
                    ImGui::Text("Custom Navigation");

                    if (ImGui::Button("Home Screen (18)"))
                        NavigateToScreen(MainContext, 18, 0.15, 273, 1);

                    if (ImGui::Button("Test Custom Button (13)"))
                        OnMenuItemSelected(ScreenMainMenu, 13, 1);
                }
            }
        }
        ImGui::EndGroup();

        ImGui::End();
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
    io.DisplaySize = ImVec2(1920, 1080);

    DXGI_SWAP_CHAIN_DESC sd;
    pSwapChain->GetDesc(&sd);

    ImGui_ImplWin32_Init(sd.OutputWindow);
    ImGui_ImplDX11_Init(pDevice, pContext);

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
