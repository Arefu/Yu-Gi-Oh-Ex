#include <windows.h>
#include <string>
#include <iostream>

#include "File.h"

std::string EffectFileHandler::Read_FromEffectFile(const std::string& path)
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

	DWORD fileSize = GetFileSize(hFile, nullptr);
	if (fileSize == INVALID_FILE_SIZE)
		CloseHandle(hFile);

	std::string buffer(fileSize, '\0');
	DWORD bytesRead = 0;
	if (!ReadFile(hFile, buffer.data(), fileSize, &bytesRead, nullptr) || bytesRead != fileSize)
		CloseHandle(hFile);

	CloseHandle(hFile);
	return buffer;
}