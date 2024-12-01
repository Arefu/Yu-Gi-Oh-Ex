#pragma once
#include <vector>
#include <string>

static class PluginManager
{
public:
	static void Load();
	static std::vector<std::string> ScanForPlugins();

	static void ProcessDetours();
	static void ProcessGui();

	static bool _IsLoaded;
};