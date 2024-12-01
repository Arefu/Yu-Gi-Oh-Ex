#include <Windows.h>
#include <string>
#include "Plugins.h"
#include "imgui.h"

void PluginManager::Load()
{
	if (_IsLoaded)
		return;

	std::vector<std::string> Plugins = ScanForPlugins();
	for (auto& Plugin : Plugins)
	{
		auto PluginPath = new CHAR[MAX_PATH];
		GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "PluginsPath", "", PluginPath, MAX_PATH, ".\\Config.ini");
		auto hModule = LoadLibraryA((std::string(PluginPath) + "\\YGO-Ex\\" + Plugin).c_str());
		auto SetImGuiContextForPlugin = GetProcAddress(hModule, "SetContext");

		reinterpret_cast<void(__stdcall*)(ImGuiContext*)>(SetImGuiContextForPlugin)(ImGui::GetCurrentContext());
	}

	_IsLoaded = true;
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

		reinterpret_cast<void(__stdcall*)()>(ProcessDetours)();
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
		MessageBoxA(NULL, PluginPath, "Plugin Path", MB_OK);
		return DLLs;
	}

	do
	{
		DLLs.push_back(FindFileData.cFileName);

	} while (FindNextFileA(hFind, &FindFileData) != 0);
	FindClose(hFind);

	return DLLs;

}
