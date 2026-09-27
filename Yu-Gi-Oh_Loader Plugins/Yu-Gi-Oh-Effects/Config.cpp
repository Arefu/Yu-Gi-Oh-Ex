#include <Windows.h>
#include <iostream>
#include <shlwapi.h>
#include <string>

#include "Config.h"

std::string Config::Get_WorkingDirectory()
{
    if (PathFileExistsA(".\\Config.ini") == FALSE)
        return "";

    auto Path = new CHAR[MAX_PATH];
    GetPrivateProfileStringA("Yu-Gi-Oh-Core", "PluginsPath", "", Path, MAX_PATH, ".\\Config.ini");
    if (!Path[0])   // configs written before Yu-Gi-Oh-Core owned this
        GetPrivateProfileStringA("Yu-Gi-Oh-GUI", "PluginsPath", "", Path, MAX_PATH, ".\\Config.ini");

    return std::string(Path).append("\\Effects");
}
