#include <Windows.h>
#include <string>
#include "Plugins.h"
#include "imgui.h"

void PluginManager::Load()
{
	std::vector<std::string> Plugins = ScanForPlugins();

	for (auto& Plugin : Plugins)
	{
		auto hModule = LoadLibraryA(("Plugins\\" + Plugin).c_str());
		auto SetImGuiContextForPlugin = GetProcAddress(hModule, "Set_ImGuiContextForPlugin");

		reinterpret_cast<void(__stdcall*)(ImGuiContext*)>(SetImGuiContextForPlugin)(ImGui::GetCurrentContext());
	}
}

void PluginManager::ProcessGui()
{
	if (PluginManager::_IsLoaded == false)
		return;

	std::vector<std::string> Plugins = ScanForPlugins();

	for (auto& Plugin : Plugins)
	{
		auto hModule = LoadLibraryA(("Plugins\\" + Plugin).c_str());
		auto DrawImGui = GetProcAddress(hModule, "Draw_ImGui");
		reinterpret_cast<void(__stdcall*)()>(DrawImGui)();
	}
}

std::vector<std::string> PluginManager::ScanForPlugins()
{
	std::vector<std::string> DLLs;

	if (GetFileAttributesA("Plugins") == INVALID_FILE_ATTRIBUTES)
		return std::vector<std::string>();


	WIN32_FIND_DATAA FindFileData;
	HANDLE hFind = FindFirstFileA("Plugins\\*.dll", &FindFileData);

	if (hFind == INVALID_HANDLE_VALUE)
		return std::vector<std::string>();

	do
	{
		DLLs.push_back(FindFileData.cFileName);
	} while (FindNextFileA(hFind, &FindFileData));

	FindClose(hFind);

	return DLLs;

}
