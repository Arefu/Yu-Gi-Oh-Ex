#include "UiWitchcraft.h"

#include <Windows.h>
#include <cstdint>
#include <cstdio>
#include "imgui.h"

namespace
{
    // String lookup: the language's string bundle (YGO::LOCAL::g_iStringLocations), then its offset table.
    wchar_t* GetString(int id)
    {
        uintptr_t langIndex = *(int*)0x14332A344;
        uintptr_t bundleBase = *(uintptr_t*)(0x143329E80 + langIndex * 8);
        uintptr_t offsetTable = *(uintptr_t*)(bundleBase + 8);
        return *(wchar_t**)(offsetTable + 8 * id - 8);
    }
}

namespace UiWitchcraft
{
    void Draw()
    {
        uintptr_t base = (uintptr_t)GetModuleHandle(NULL);
        uintptr_t App = *(uintptr_t*)(base + 0x29275D8);
        uintptr_t MainContext = App ? *(uintptr_t*)(App + 0x1F0) : 0;

        if (MainContext == 0)
        {
            ImGui::TextColored(ImVec4(1, 0, 0, 1), "MainContext not ready yet");
            return;
        }

        // Scan for menu button labels - try ranges around what we know
        if (ImGui::CollapsingHeader("String Scanner"))
        {
            static int scanStart = 800;
            static int scanEnd = 850;
            ImGui::InputInt("Start", &scanStart);
            ImGui::InputInt("End", &scanEnd);

            for (int i = scanStart; i < scanEnd; i++)
            {
                wchar_t* str = GetString(i);
                if (str && str[0] != L'\0')
                    ImGui::Text("%d: %ls", i, str);
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

        auto NavigateToScreen = reinterpret_cast<char(__fastcall*)(__int64, int, double, int, char)>(0x1408087A0);
        auto OnMenuItemSelected = reinterpret_cast<void(__fastcall*)(__int64, __int64, char)>(0x140856C40);

        if (!ScreenMainMenu)
            return;

        uintptr_t arrayData = *(uintptr_t*)(ScreenMainMenu + 0x2E0);
        uintptr_t arrayWrite = *(uintptr_t*)(ScreenMainMenu + 0x2E8);
        uintptr_t arrayEnd = *(uintptr_t*)(ScreenMainMenu + 0x2F0);
        ImGui::Text("Button count:     %d", static_cast<int>((arrayWrite - arrayData) / 24));
        ImGui::Text("Allocated slots:  %d", static_cast<int>((arrayEnd - arrayData) / 24));

        ImGui::Text("Current page: %d", *(int*)(ScreenMainMenu + 832));

        uintptr_t arr1Data = *(uintptr_t*)(ScreenMainMenu + 0x2F8);
        uintptr_t arr1Write = *(uintptr_t*)(ScreenMainMenu + 0x300);
        uintptr_t arr2Data = *(uintptr_t*)(ScreenMainMenu + 0x310);
        uintptr_t arr2Write = *(uintptr_t*)(ScreenMainMenu + 0x318);
        uintptr_t arr3Data = *(uintptr_t*)(ScreenMainMenu + 0x328);
        uintptr_t arr3Write = *(uintptr_t*)(ScreenMainMenu + 0x330);
        ImGui::Text("Array 1 count: %d", static_cast<int>((arr1Write - arr1Data) / 4));
        ImGui::Text("Array 2 count: %d", static_cast<int>((arr2Write - arr2Data) / 4));
        ImGui::Text("Array 3 count: %d", static_cast<int>((arr3Write - arr3Data) / 4));

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
