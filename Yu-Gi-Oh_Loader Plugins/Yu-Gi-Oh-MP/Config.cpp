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

std::wstring Config::Get_ContentFolder()
{
    HMODULE self = nullptr;
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        reinterpret_cast<LPCWSTR>(&Config::Get_ContentFolder), &self);
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(self, path, MAX_PATH);
    std::wstring folder(path);
    folder.resize(folder.find_last_of(L"\\/") + 1);
    return folder + L"Yu-Gi-Oh-MP";
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
