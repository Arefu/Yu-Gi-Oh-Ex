#include <Windows.h>
#include <iostream>
#include <cstdio>
#include <fstream>
#include <detours.h>
#include <format>
#include <algorithm>
#include <io.h>
#include <vector>

#include <DbgHelp.h>
#include <fstream>
#include "Logger.h"

uintptr_t LoadArchive = 0x14080D3D0;

bool AllowMultiInstance = false;

auto _Archive = new CHAR[MAX_PATH];
__int64 __fastcall _hLoadArchive(__int64* Struct, const char* Archive)
{
    Logger::WriteLog("Loading Archive: " + std::string(Archive), MODULE_NAME, 0);

    return ((int(__fastcall*)(__int64*, const char*))LoadArchive)(Struct, _Archive);
}

//The game doesn't save the HANDLE, we're just going to return ourselves.
static FARPROC pCreateMutex = GetProcAddress(GetModuleHandle(L"kernel32.dll"), "CreateMutexW");
HANDLE WINAPI HookCreateMutex(LPSECURITY_ATTRIBUTES lpMutexAttributes, BOOL bInitialOwner, LPCWSTR lpName)
{
    return GetModuleHandle(NULL);
}

typedef FILE* (__cdecl* fopen_t)(const char* filename, const char* mode);
typedef errno_t(__cdecl* fopen_s_t)(FILE** file, const char* filename, const char* mode);
typedef size_t(__cdecl* fread_t)(void* ptr, size_t size, size_t count, FILE* stream);

fopen_t original_fopen = nullptr;
fopen_s_t original_fopen_s = nullptr;
fread_t original_fread = nullptr;

FILE* __cdecl hooked_fopen(const char* filename, const char* mode)
{
    Logger::WriteLog("Requested file (fopen): " + std::string(filename), MODULE_NAME, 0);

    return original_fopen(filename, mode);
}
errno_t __cdecl hooked_fopen_s(FILE** file, const char* filename, const char* mode)
{
    Logger::WriteLog("Requested file (fopen_s): " + std::string(filename), MODULE_NAME, 0);
    return original_fopen_s(file, filename, mode);
}
size_t __cdecl hooked_fread(void* ptr, size_t size, size_t count, FILE* stream)
{
    // Just log and call original  int fd = _fileno(file);
    Logger::WriteLog(std::format("Reading From {}", count), MODULE_NAME, 0);
    return original_fread(ptr, size, count, stream);
}
__int64 orig_14080DDE0 = 0x0;

bool FileExists(const wchar_t* path)
{
    DWORD attrs = GetFileAttributesW(path);
    return (attrs != INVALID_FILE_ATTRIBUTES) && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
}

typedef void* (__fastcall* impl_new_t)(size_t);
impl_new_t game_alloc = (impl_new_t)0x14090D2B8;

__int64* __fastcall sub_14080DDE0(BYTE* a1, LPCTSTR a2, size_t size)
{
    std::string filename(reinterpret_cast<const char*>(a2));

    std::wstring wFilename(filename.begin(), filename.end());
    std::wstring fullPath = L"YGO_2020\\" + wFilename;

    if (FileExists(fullPath.c_str()) == false)
    {
        Logger::WriteLog(std::format("Failed To Load {} From Disk", filename), MODULE_NAME, 2);
        return reinterpret_cast<__int64* (__fastcall*)(BYTE*, LPCTSTR, size_t)>(orig_14080DDE0)(a1, a2, size);
    }
    Logger::WriteLog(std::format("Loading {} from YGO_2020", filename), MODULE_NAME, 0);
    FILE* f = _wfopen(fullPath.c_str(), L"rb");
    fseek(f, 0, SEEK_END);
    size_t fileSize = ftell(f);
    fseek(f, 0, SEEK_SET);
    size_t alignedSize = (fileSize + 3) & ~3;
    size_t totalSize = alignedSize + size;
    void* buffer = game_alloc(totalSize);

    fread(buffer, 1, fileSize, f);
    fclose(f);

    if (alignedSize > fileSize)
        memset((char*)buffer + fileSize, 0, alignedSize - fileSize);

    if (size > 0)
        memset((char*)buffer + alignedSize, 0, size);
    return reinterpret_cast<__int64*>(buffer);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    Logger::SetupLogger();

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        HMODULE stdio = GetModuleHandleA("api-ms-win-crt-stdio-l1-1-0.dll.dll");
        original_fopen = (fopen_t)GetProcAddress(stdio, "fopen");
        original_fopen_s = (fopen_s_t)GetProcAddress(stdio, "fopen_s");
        original_fread = (fread_t)GetProcAddress(stdio, "fread");

        GetPrivateProfileStringA("Yu-Gi-Oh-BetterLoad", "Archive", "YGO_2020", _Archive, 255, ".\\Config.ini");

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());

        DetourAttach(&(PVOID&)LoadArchive, _hLoadArchive);
        DetourAttach(&(PVOID&)original_fopen, hooked_fopen);
        DetourAttach(&(PVOID&)original_fopen_s, hooked_fopen_s);

        __int64 func_14080DDE0 = 0x14080DDE0;

        if (GetPrivateProfileIntA("Yu-Gi-Oh-BetterLoad", "AllowMultiInstance", 0, ".\\Config.ini") == 1)
            DetourAttach(&(PVOID&)pCreateMutex, HookCreateMutex);
        if (GetPrivateProfileIntA("Yu-Gi-Oh-BetterLoad", "LooseLoading", 0, ".\\Config.ini") == 1)
        {
            TCHAR currentDir[MAX_PATH];

            DWORD length = GetCurrentDirectory(MAX_PATH, currentDir);
            DWORD attrs = GetFileAttributes(L"YGO_2020");
            if (length == 0 || length > MAX_PATH) {
                Logger::WriteLog("Failed to get current directory.", MODULE_NAME, 1);
            }
            if (attrs == INVALID_FILE_ATTRIBUTES || !(attrs & FILE_ATTRIBUTE_DIRECTORY)) {
                Logger::WriteLog("You do not have an unnpacked copy of the game in its directory", MODULE_NAME, 2);
            }
            else {
                Logger::WriteLog("Using Yu-Gi-Oh-BetterLoad to load content", MODULE_NAME, 0);
                DetourAttach(&(PVOID&)func_14080DDE0, sub_14080DDE0);
            }
        }

        DetourTransactionCommit();
        orig_14080DDE0 = func_14080DDE0;
        break;
    }
    return TRUE;
}
