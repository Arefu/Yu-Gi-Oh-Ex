#include <windows.h>
#include <string>
#include <iostream>

#include "File.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Log.h"

std::string FileIO::Read_FromEffectFile(const std::string& path)
{
    HANDLE hFile = CreateFileA(
        path.c_str(),
        GENERIC_READ,
        FILE_SHARE_READ,
        nullptr,
        OPEN_EXISTING,
        FILE_ATTRIBUTE_NORMAL,
        nullptr
    );

    if (hFile == INVALID_HANDLE_VALUE) {
        YGO::Log("Failed to open file: " + path + " (Error " + std::to_string(GetLastError()) + ")", "Yu-Gi-Oh-Effects", 2);
        return {};
    }

    DWORD fileSize = GetFileSize(hFile, nullptr);
    if (fileSize == INVALID_FILE_SIZE) {
        YGO::Log("Failed to get file size: " + path + " (Error " + std::to_string(GetLastError()) + ")", "Yu-Gi-Oh-Effects", 2);
        CloseHandle(hFile);
        return {};
    }

    std::string buffer(fileSize, '\0');
    DWORD bytesRead = 0;
    if (!ReadFile(hFile, buffer.data(), fileSize, &bytesRead, nullptr) || bytesRead != fileSize) {
        YGO::Log("Failed to read file: " + path + " (Error " + std::to_string(GetLastError()) + ")", "Yu-Gi-Oh-Effects", 2);
        CloseHandle(hFile);
        return {};
    }

    CloseHandle(hFile);
    return buffer;
}
