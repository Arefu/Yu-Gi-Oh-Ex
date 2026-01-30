#pragma once
#include <vector>
#include <string>
#include <windows.h>
#include <unordered_map>

class PluginManager
{
public:
    static void Load();
    static std::vector<std::string> ScanForPlugins();

    static void ProcessDetours();
    static void ProcessConfigForPlugin();
    static void ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);
    static void ProcessGui();

    static bool _IsLoaded;
    static std::unordered_map<std::string, bool> m_PluginEnabled;
    static CHAR PluginPath[MAX_PATH];
};
