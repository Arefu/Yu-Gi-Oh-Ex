#include <Windows.h>
#include "imgui.h"

#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <thread>
#include <string>
#include <detours.h>
#include <iostream>

#include "YuGiOh/YuGiOh-DUEL.h"
#include "YuGiOh/YuGiOh-UTIL.h"
#include "YuGiOh/YuGiOh-CARDS.h"
#include "YuGiOh//YuGiOh-GAME.h"
#include "YuGiOh//YuGiOh-UI.h"
#include "DebugTools.h"

extern "C" __declspec(dllexport) void SetContext(ImGuiContext* Context)
{
    ImGui::SetCurrentContext(Context);
}

extern "C" __declspec(dllexport) void ProcessDetours()
{
    DebugTools::Init();
}

extern "C" __declspec(dllexport) void ProcessWindow()
{
    DebugTools::Draw();
}

extern "C" _declspec(dllexport) void ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    switch (msg)
    {
        
    }
}

extern "C" _declspec(dllexport) void ProcessConfig()
{
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
