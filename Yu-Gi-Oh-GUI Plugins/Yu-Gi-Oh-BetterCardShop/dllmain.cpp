#include <Windows.h>
#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <imgui.h>

#include <iostream>

int BetterShop_Cost = 0;
int Card = 0;

extern "C" __declspec(dllexport) void SetContext(ImGuiContext* Context)
{
	ImGui::SetCurrentContext(Context);
}

extern "C" __declspec(dllexport) void ProcessDetours()
{

}

extern "C" __declspec(dllexport) void ProcessWindow()
{
	ImGui::Begin("Yu-Gi-Oh! Better Shop");
	ImGui::Text("Enter Card ID: ");


	ImGui::InputInt("##CardID", &Card, 0);
	if (Card <= 0 || Card >= 65535)
		Card = 0;

	ImGui::Button("Buy Card");
	ImGui::End();
}

extern "C" _declspec(dllexport) void ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{

}

extern "C" _declspec(dllexport) void ProcessConfig()
{
	BetterShop_Cost = GetPrivateProfileIntA("Yu-Gi-Oh-BetterCardShop", "BetterShop-Cost", 500, ".\\Config.ini");
	std::cout << "[Yu-Gi-Oh-BetterCardShop] BetterShop-Cost: " << BetterShop_Cost << std::endl;
}


BOOL APIENTRY DllMain( HMODULE hModule,DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:

    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

