#pragma once
#include <windows.h>

// Draws and feeds input to the plugins in Plugins\YGO-Ex. Loading and starting them is Yu-Gi-Oh-Core's job (it works without this plugin);
// this only hands them the ImGui context, calls their ProcessWindow every frame and forwards input.
class PluginManager
{
public:
    static void ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
    static void ProcessGui();
};
