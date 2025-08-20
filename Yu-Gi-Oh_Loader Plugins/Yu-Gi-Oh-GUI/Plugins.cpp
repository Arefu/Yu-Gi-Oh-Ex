#include <Windows.h>
#include <iostream>
#include <string>
#include "Plugins.h"
#include "imgui.h"
#include <thread>
#include <chrono>
#include "Yu-Gi-Oh-Ex.h"

void PluginManager::Load()
{
	if (_IsLoaded)
		return;

	std::vector<std::string> Plugins = ScanForPlugins();
	for (auto& Plugin : Plugins)
	{
		std::cout << "Loading Plugin: " << Plugin << std::endl;

		auto PluginPath = new CHAR[MAX_PATH];
		GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "PluginsPath", "", PluginPath, MAX_PATH, ".\\Config.ini");
		auto hModule = LoadLibraryA((std::string(PluginPath) + "\\YGO-Ex\\" + Plugin).c_str());
		auto SetImGuiContextForPlugin = GetProcAddress(hModule, "SetContext");
		if (SetImGuiContextForPlugin)
			reinterpret_cast<void(__stdcall*)(ImGuiContext*)>(SetImGuiContextForPlugin)(ImGui::GetCurrentContext());
	}

	_IsLoaded = true;
}

void PluginManager::DelayLoad()
{
	std::this_thread::sleep_for(std::chrono::milliseconds(100)); //Wait just a smidge for Windows to do Windows things.
	while (YuGiOhEx::g_bOnPageFirst == true)
	{
		std::this_thread::sleep_for(std::chrono::milliseconds(250));
	}

	if (!PluginManager::_IsLoaded)
	{
		Load();
		ProcessConfigForPlugin();
		ProcessDetours();
	}
}

void PluginManager::ProcessGui()
{
	if (PluginManager::_IsLoaded == false)
		return;

	std::vector<std::string> Plugins = ScanForPlugins();

	for (auto& Plugin : Plugins)
	{
		auto PluginPath = new CHAR[MAX_PATH];
		GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "PluginsPath", "", PluginPath, MAX_PATH, ".\\Config.ini");

		auto hModule = LoadLibraryA((std::string(PluginPath) + "\\YGO-Ex\\" + Plugin).c_str());
		auto DrawImGui = GetProcAddress(hModule, "ProcessWindow");
		if (DrawImGui)
			reinterpret_cast<void(__stdcall*)()>(DrawImGui)();
	}
}

void PluginManager::ProcessDetours()
{
	if (PluginManager::_IsLoaded == false)
		return;

	std::vector<std::string> Plugins = ScanForPlugins();
	for (auto& Plugin : Plugins)
	{
		auto PluginPath = new CHAR[MAX_PATH];
		GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "PluginsPath", "", PluginPath, MAX_PATH, ".\\Config.ini");

		auto hModule = LoadLibraryA((std::string(PluginPath) + "\\YGO-Ex\\" + Plugin).c_str());
		auto ProcessDetours = GetProcAddress(hModule, "ProcessDetours");
		if (ProcessDetours)
			reinterpret_cast<void(__stdcall*)()>(ProcessDetours)();

		std::cout << "Processing Detours for: " << Plugin << std::endl;
	}
}

void PluginManager::ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
	if (PluginManager::_IsLoaded == false)
		return;
	std::vector<std::string> Plugins = PluginManager::ScanForPlugins();
	for (auto& Plugin : Plugins)
	{
		auto PluginPath = new CHAR[MAX_PATH];
		GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "PluginsPath", "", PluginPath, MAX_PATH, ".\\Config.ini");
		auto hModule = LoadLibraryA((std::string(PluginPath) + "\\YGO-Ex\\" + Plugin).c_str());
		auto ProcessInput = GetProcAddress(hModule, "ProcessInput");
		if (ProcessInput)
			reinterpret_cast<void(__stdcall*)(HWND, UINT, WPARAM, LPARAM)>(ProcessInput)(hWnd, msg, wParam, lParam);
	}
}

void PluginManager::ProcessConfigForPlugin()
{
	if (PluginManager::_IsLoaded == false)
		return;
	std::vector<std::string> Plugins = ScanForPlugins();
	for (auto& Plugin : Plugins)
	{
		auto PluginPath = new CHAR[MAX_PATH];
		GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "PluginsPath", "", PluginPath, MAX_PATH, ".\\Config.ini");
		auto hModule = LoadLibraryA((std::string(PluginPath) + "\\YGO-Ex\\" + Plugin).c_str());
		auto ProcessConfig = GetProcAddress(hModule, "ProcessConfig");
		if (ProcessConfig)
			reinterpret_cast<void(__stdcall*)()>(ProcessConfig)();
	}
}

std::vector<std::string> PluginManager::ScanForPlugins()
{
	std::vector<std::string> DLLs;
	//Win32 Open Conmfig.InI

	auto PluginPath = new CHAR[MAX_PATH];
	GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "PluginsPath", "", PluginPath, MAX_PATH, ".\\Config.ini");

	//Scan for all DLLs in the YGO-Ex subfolder of gGamePath
	WIN32_FIND_DATAA FindFileData;
	HANDLE hFind = FindFirstFileA((std::string(PluginPath) + "\\YGO-Ex\\*.dll").c_str(), &FindFileData);
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