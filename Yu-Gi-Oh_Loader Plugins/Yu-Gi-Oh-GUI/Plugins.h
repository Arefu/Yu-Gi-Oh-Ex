#pragma once
#include <vector>
#include <string>

static class PluginManager
{
public:
    static void DelayLoad();
    static void Load();
    static std::vector<std::string> ScanForPlugins();

    static void ProcessDetours();
    static void ProcessConfigForPlugin();
    static void ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
    static void ProcessGui();

    static bool _IsLoaded;
};
