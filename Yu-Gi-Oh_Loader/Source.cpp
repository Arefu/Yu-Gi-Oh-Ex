#include <Windows.h>
#include <Shlwapi.h>
#include <string>
#include <vector>
#include <string>
#include <unordered_map>
#include <algorithm>
#include "Game.h"

std::string GetBaseNameNoExt(LPCSTR path)
{
    if (!path) return "";
    std::string str(path);
    size_t lastSlash = str.find_last_of("\\/");
    size_t start = (lastSlash == std::string::npos) ? 0 : lastSlash + 1;
    size_t dot = str.find_last_of('.');
    size_t end = (dot == std::string::npos || dot < start) ? str.length() : dot;
    return str.substr(start, end - start);
}
void SortPluginsByMods()
{
    std::unordered_map<std::string, size_t> modOrderMap;
    for (size_t i = 0; i < Game::gModsToLoad.size(); ++i) {
        modOrderMap[Game::gModsToLoad[i]] = i;
    }

    std::stable_sort(Game::gPlugins.begin(), Game::gPlugins.end(), [&](LPCSTR a, LPCSTR b) {
        std::string nameA = GetBaseNameNoExt(a);
        std::string nameB = GetBaseNameNoExt(b);

        auto itA = modOrderMap.find(nameA);
        auto itB = modOrderMap.find(nameB);

        bool foundA = itA != modOrderMap.end();
        bool foundB = itB != modOrderMap.end();

        if (foundA && foundB)
            return itA->second < itB->second;
        if (foundA)
            return true;
        if (foundB)
            return false;
        return false;
        });
}


int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow)
{
	Game::Locate();
	Game::CreateConfig("Config.ini");
    Game::LookForPlugins();
	Game::CheckForLoadOrder();

    SortPluginsByMods();

	Game::Start();
}