#include "imgui.h"
#include "Logger.h"
#include "Plugins.h"
#include "Yu-Gi-Oh-Ex.h"
#include <chrono>
#include <iostream>
#include <string>
#include <thread>
#include <Windows.h>

CHAR PluginManager::PluginPath[MAX_PATH];

void PluginManager::Load()
{
    if (_IsLoaded)
        return;

    for (auto& Plugin : PluginManager::m_PluginEnabled)
    {
        if (Plugin.second == false) continue;

        Logger::WriteLog("Loading Plugin: " + Plugin.first, MODULE_NAME, 0);

        auto hModule = LoadLibraryA((std::string(PluginManager::PluginPath) + "\\YGO-Ex\\" + Plugin.first).c_str());
        auto SetImGuiContextForPlugin = GetProcAddress(hModule, "SetContext");
        if (SetImGuiContextForPlugin)
            reinterpret_cast<void(__stdcall*)(ImGuiContext*)>(SetImGuiContextForPlugin)(ImGui::GetCurrentContext());
    }

    _IsLoaded = true;
}

void PluginManager::ProcessGui()
{
    if (PluginManager::_IsLoaded == false)
        return;

    for (auto& Plugin : PluginManager::m_PluginEnabled)
    {
        if (Plugin.second == false)
            continue;

        auto hModule = LoadLibraryA((std::string(PluginManager::PluginPath) + "\\YGO-Ex\\" + Plugin.first).c_str());
        auto DrawImGui = GetProcAddress(hModule, "ProcessWindow");
        if (DrawImGui)
            reinterpret_cast<void(__stdcall*)()>(DrawImGui)();
    }
}

void PluginManager::ProcessDetours()
{
    if (PluginManager::_IsLoaded == false)
        return;

    for (auto& Plugin : PluginManager::m_PluginEnabled)
    {
        if (Plugin.second == false)
            continue;

        auto hModule = LoadLibraryA((std::string(PluginManager::PluginPath) + "\\YGO-Ex\\" + Plugin.first).c_str());
        auto ProcessDetours = GetProcAddress(hModule, "ProcessDetours");
        if (ProcessDetours)
            reinterpret_cast<void(__stdcall*)()>(ProcessDetours)();

        std::cout << "Processing Detours for: " << Plugin.first << std::endl;
    }
}

void PluginManager::ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
    if (PluginManager::_IsLoaded == false)
        return;

    for (auto& Plugin : PluginManager::m_PluginEnabled)
    {
        if (Plugin.second == false)
            continue;

        auto hModule = LoadLibraryA((std::string(PluginManager::PluginPath) + "\\YGO-Ex\\" + Plugin.first).c_str());
        auto ProcessInput = GetProcAddress(hModule, "ProcessInput");
        if (ProcessInput)
            reinterpret_cast<void(__stdcall*)(HWND, UINT, WPARAM, LPARAM)>(ProcessInput)(hWnd, msg, wParam, lParam);
    }
}

void PluginManager::ProcessConfigForPlugin()
{
    if (PluginManager::_IsLoaded == false)
        return;
    for (auto& Plugin : PluginManager::m_PluginEnabled)
    {
        if (Plugin.second == false)
            continue;

        auto hModule = LoadLibraryA((std::string(PluginManager::PluginPath) + "\\YGO-Ex\\" + Plugin.first).c_str());
        auto ProcessConfig = GetProcAddress(hModule, "ProcessConfig");
        if (ProcessConfig)
            reinterpret_cast<void(__stdcall*)()>(ProcessConfig)();
    }
}

std::vector<std::string> PluginManager::ScanForPlugins()
{
    GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "PluginsPath", "", PluginManager::PluginPath, MAX_PATH, ".\\Config.ini");
    std::vector<std::string> DLLs;

    WIN32_FIND_DATAA FindFileData;
    HANDLE hFind = FindFirstFileA((std::string(PluginManager::PluginPath) + "\\YGO-Ex\\*.dll").c_str(), &FindFileData);
    if (hFind == INVALID_HANDLE_VALUE)
    {
        MessageBoxA(NULL, "PluginsPath is empty! Check your Config.ini", "Plugin Path", MB_OK);
        exit(ERROR_FILE_NOT_FOUND);
    }

    do
    {
        DLLs.push_back(FindFileData.cFileName);
    } while (FindNextFileA(hFind, &FindFileData) != 0);
    FindClose(hFind);

    return DLLs;
}
