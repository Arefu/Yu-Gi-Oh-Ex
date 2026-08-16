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
#include "Logger.h"

uintptr_t LoadArchive = 0x14080D3D0;
bool AllowMultiInstance = false;
auto _Archive = new CHAR[MAX_PATH];

__int64 __fastcall _hLoadArchive(__int64* Struct, const char* Archive)
{
    Logger::WriteLog("Loading Archive: " + std::string(Archive), MODULE_NAME, 0);
    return ((int(__fastcall*)(__int64*, const char*))LoadArchive)(Struct, _Archive);
}

static FARPROC pCreateMutex = GetProcAddress(GetModuleHandle(L"kernel32.dll"), "CreateMutexW");
HANDLE WINAPI HookCreateMutex(LPSECURITY_ATTRIBUTES lpMutexAttributes, BOOL bInitialOwner, LPCWSTR lpName)
{
    return GetModuleHandle(NULL);
}

typedef FILE* (__cdecl* fopen_t)  (const char*, const char*);
typedef errno_t(__cdecl* fopen_s_t)(FILE**, const char*, const char*);
typedef size_t(__cdecl* fread_t)  (void*, size_t, size_t, FILE*);

fopen_t   original_fopen = nullptr;
fopen_s_t original_fopen_s = nullptr;
fread_t   original_fread = nullptr;

WCHAR g_FolderName[MAX_PATH] = L"YGO_2020";

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
    Logger::WriteLog(std::format("Reading From {}", count), MODULE_NAME, 0);
    return original_fread(ptr, size, count, stream);
}

uintptr_t orig_14080DDE0 = 0x0;

bool FileExists(const wchar_t* path)
{
    DWORD attrs = GetFileAttributesW(path);
    return (attrs != INVALID_FILE_ATTRIBUTES) && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
}

typedef void* (__fastcall* impl_new_t)(size_t);
impl_new_t game_alloc = (impl_new_t)0x14090D2B8;

typedef __int64* (__fastcall* orig_fn_t)(BYTE*, LPCTSTR, size_t);

__int64* __fastcall sub_14080DDE0(BYTE* a1, LPCTSTR a2, size_t a3)
{
    const char* FILE_NAME = reinterpret_cast<const char*>(a2);
    std::string filename(FILE_NAME);
    std::wstring wFilename(filename.begin(), filename.end());
    std::wstring fullPath = std::wstring(g_FolderName) + L"\\" + wFilename;

    if (!FileExists(fullPath.c_str()))
    {
        Logger::WriteLog(std::format("{} not found on disk", filename), MODULE_NAME, 1);
        return ((orig_fn_t)orig_14080DDE0)(a1, a2, a3);
    }


    FILE* f = _wfopen(fullPath.c_str(), L"rb");
    if (!f)
    {
        Logger::WriteLog(std::format("Failed to open file handle for {}", filename), MODULE_NAME, 1);
        return ((orig_fn_t)orig_14080DDE0)(a1, a2, a3);
    }

    fseek(f, 0, SEEK_END);
    size_t fileSize = ftell(f);
    fseek(f, 0, SEEK_SET);

    size_t alignedSize = (fileSize + 3) & ~3ULL;
    size_t totalSize = (alignedSize >= (fileSize + a3)) ? alignedSize : (fileSize + a3);

    void* buffer = game_alloc(totalSize);
    if (!buffer)
    {
        fclose(f);
        Logger::WriteLog(std::format("Buffer allocation failed for {} ({} bytes)", filename, totalSize), MODULE_NAME, 1);
        return ((orig_fn_t)orig_14080DDE0)(a1, a2, a3);
    }

    memset(buffer, 0, totalSize);

    size_t bytesRead = fread(buffer, 1, fileSize, f);
    fclose(f);

    if (bytesRead != fileSize)
        return ((orig_fn_t)orig_14080DDE0)(a1, a2, a3);
    

    Logger::WriteLog(std::format("Loaded {} from disk ({} bytes)", filename, fileSize), MODULE_NAME, 0);
    return reinterpret_cast<__int64*>(buffer);
}

typedef __int64(__fastcall* sub_140752AF0_t)(__int64 a1);
uintptr_t orig_140752AF0 = 0x140752AF0;

__int64 __fastcall Hook_140752AF0(__int64 a1)
{
    Logger::WriteLog(std::format("Card Texture Setup a1: {:016x}", a1), MODULE_NAME, 0);
    return ((sub_140752AF0_t)orig_140752AF0)(a1);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    Logger::SetupLogger();

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    {
        HMODULE stdio = GetModuleHandleA("api-ms-win-crt-stdio-l1-1-0.dll");
        original_fopen = (fopen_t)GetProcAddress(stdio, "fopen");
        original_fopen_s = (fopen_s_t)GetProcAddress(stdio, "fopen_s");
        original_fread = (fread_t)GetProcAddress(stdio, "fread");

        GetPrivateProfileStringA("Yu-Gi-Oh-BetterLoad", "Archive", "YGO_2020", _Archive, 255, ".\\Config.ini");

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());

        DetourAttach(&(PVOID&)LoadArchive, _hLoadArchive);
        DetourAttach(&(PVOID&)original_fopen, hooked_fopen);
        DetourAttach(&(PVOID&)original_fopen_s, hooked_fopen_s);
        DetourAttach(&(PVOID&)orig_140752AF0, Hook_140752AF0);
        uintptr_t func_14080DDE0 = 0x14080DDE0;

        if (GetPrivateProfileIntA("Yu-Gi-Oh-BetterLoad", "AllowMultiInstance", 0, ".\\Config.ini") == 1)
            DetourAttach(&(PVOID&)pCreateMutex, HookCreateMutex);

        if (GetPrivateProfileIntA("Yu-Gi-Oh-BetterLoad", "LooseLoading", 0, ".\\Config.ini") == 1)
        {
            TCHAR currentDir[MAX_PATH];
            DWORD length = GetCurrentDirectory(MAX_PATH, currentDir);

            CHAR folderNameA[MAX_PATH];
            GetPrivateProfileStringA("Yu-Gi-Oh-BetterLoad", "FolderName", "YGO_2020", folderNameA, MAX_PATH, ".\\Config.ini");
            MultiByteToWideChar(CP_ACP, 0, folderNameA, -1, g_FolderName, MAX_PATH);

            DWORD attrs = GetFileAttributes(g_FolderName);

            if (length == 0 || length > MAX_PATH)
                Logger::WriteLog("Failed to get current directory.", MODULE_NAME, 2);
            else if (attrs == INVALID_FILE_ATTRIBUTES || !(attrs & FILE_ATTRIBUTE_DIRECTORY))
                Logger::WriteLog(std::format("Folder '{}' not found -- you need an unpacked copy of the game", folderNameA), MODULE_NAME, 2);
            else
            {
                Logger::WriteLog(std::format("serving files from '{}'", folderNameA), MODULE_NAME, 0);
                DetourAttach(&(PVOID&)func_14080DDE0, sub_14080DDE0);
            }
        }

        DetourTransactionCommit();
        orig_14080DDE0 = func_14080DDE0;
        break;
    }
    }
    return TRUE;
}
