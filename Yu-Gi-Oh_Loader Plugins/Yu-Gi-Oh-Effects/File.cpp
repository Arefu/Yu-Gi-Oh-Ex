#include <windows.h>
#include <string>
#include <iostream>

#include "File.h"

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
        std::cerr << "Failed to open file: " << path << " (Error " << GetLastError() << ")\n";
        return {};
    }

    DWORD fileSize = GetFileSize(hFile, nullptr);
    if (fileSize == INVALID_FILE_SIZE) {
        std::cerr << "Failed to get file size: " << path << " (Error " << GetLastError() << ")\n";
        CloseHandle(hFile);
        return {};
    }

    std::string buffer(fileSize, '\0');
    DWORD bytesRead = 0;
    if (!ReadFile(hFile, buffer.data(), fileSize, &bytesRead, nullptr) || bytesRead != fileSize) {
        std::cerr << "Failed to read file: " << path << " (Error " << GetLastError() << ")\n";
        CloseHandle(hFile);
        return {};
    }

    CloseHandle(hFile);
    return buffer;
}
