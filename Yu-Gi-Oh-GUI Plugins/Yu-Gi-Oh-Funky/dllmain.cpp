#include <Windows.h>
#include "imgui.h"

#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <thread>
#include <string>
#include <detours.h>
#include <iostream>

#include "YuGiOh/YuGiOh-DUEL.h"

extern "C" __declspec(dllexport) void SetContext(ImGuiContext* Context)
{
    ImGui::SetCurrentContext(Context);
}

extern "C" __declspec(dllexport) void ProcessDetours()
{
}

extern "C" __declspec(dllexport) void ProcessWindow()
{
    ImGui::Begin("Yu-Gi-Oh! Funky");

    ImGui::Text("YGO::DUEL::Get_NumberOfPlayers() %d", YGO::DUEL::Get_NumberOfPlayers());
    ImGui::Text("YGO::DUEL::Get_IsTutorialDuel() %d", YGO::DUEL::Get_IsTutorialDuel());

    ImGui::Text("YGO::DUEL::bIsTagDuel %d", YGO::DUEL::bIsTagDuel);
    ImGui::Text("YGO::DUEL::bIsTutorialDuel %d", YGO::DUEL::bIsTutorialDuel);
    ImGui::Text("YGO::DUEL::iTutorialDuelIndex %d", YGO::DUEL::iTutorialDuelIndex);

    ImGui::Text("YGO::DUEL::Set_IsTutorialDuel()");
    if (ImGui::Button("TRUE"))
        YGO::DUEL::Set_IsTutorialDuel(true);
    ImGui::SameLine();
    if (ImGui::Button("FALSE"))
        YGO::DUEL::Set_IsTutorialDuel(false);

    ImGui::Text("YGO::DUEL::SetTutorialDuelIndex()");
    if (ImGui::Button("1"))
        YGO::DUEL::Set_TutorialDuelIndex(1);
    ImGui::SameLine();
    if (ImGui::Button("5"))
        YGO::DUEL::Set_TutorialDuelIndex(5);

    ImGui::End();
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
