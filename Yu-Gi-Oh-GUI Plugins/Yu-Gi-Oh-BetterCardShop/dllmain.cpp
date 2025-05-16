#include <Windows.h>
#include <imgui_impl_dx11.h>
#include <string>
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

	ImGui::End(); typedef WORD* (__fastcall* SubFuncType)(const void** a1, BYTE* a2, WORD* a3);
    SubFuncType sub_14077C310 = (SubFuncType)0x14077C310;

    // Example ImGui button
    if (ImGui::Button("Call sub_14077C310"))
    {
        // a3: WORD initialized to 0x4007
        static WORD resultCode = 0x4007;

        // a1: assume we need some valid object or structure, simplified as a dummy pointer here
        static const void* dummyObject = nullptr; // Replace with valid object as required
        const void** a1 = &dummyObject;

        // a2: dummy _BYTE buffer (size depends on expected structure)
        static BYTE dummyBytes[16] = {}; // Adjust size as needed
        BYTE* a2 = dummyBytes;

        WORD* result = sub_14077C310(a1, a2, &resultCode);

        // Optionally display result
        char buffer[64];
        snprintf(buffer, sizeof(buffer), "Result: 0x%04X", result ? *result : 0xFFFF);
        ImGui::Text("%s", buffer);
    }
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

