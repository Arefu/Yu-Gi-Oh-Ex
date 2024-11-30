#include <Windows.h>
#include "imgui.h"

#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <thread>
#include <string>

extern "C" __declspec(dllexport) void Set_ImGuiContextForPlugin(ImGuiContext* Context)
{
	ImGui::SetCurrentContext(Context);
}

extern "C" __declspec(dllexport) void Draw_ImGui()
{
		ImGui::ShowDemoWindow();
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
