#include <Windows.h>
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

    return std::string(Path).append("\\MP");
}

namespace
{
    std::string Read(const char* key)
    {
        char value[512]{};
        GetPrivateProfileStringA("Yu-Gi-Oh-MP", key, "", value, sizeof(value), ".\\Config.ini");
        return value;
    }
}

std::string Config::Get_ServerUrl()
{
    std::string url = Read("ServerUrl");
    while (!url.empty() && (url.back() == '/' || url.back() == ' '))   // routes are appended with their own leading '/'
        url.pop_back();
    return url;
}

std::string Config::Get_ClaimCode()
{
    return Read("ClaimCode");
}

std::string Config::Get_DisplayName()
{
    return Read("DisplayName");
}
