#include <Windows.h>
#include <iostream>
#include <fstream>
#include <string>
#include <json.hpp>

#include "FS_Utility.h"

//Err past me, this doesn't support multple manifest files in directories. It returns on the first one it finds.
//I'll fix this later..... I'm not sure how I didn't notice this before.
nlohmann::json Utility::Find_ManifestFilesInDirectory(std::string Path)
{
	WIN32_FIND_DATAA findFileData;
	HANDLE hFind = FindFirstFileA((Path + ("\\*")).c_str(), &findFileData);
	if (hFind == INVALID_HANDLE_VALUE)
		return false;


	do
	{
		if (findFileData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)
		{
			if (findFileData.cFileName[0] == '.' || findFileData.cFileName == "..")
				continue;


			std::string ManifestPath = Path + findFileData.cFileName + "\\Manifest.json";
			DWORD fileAttr = GetFileAttributesA(ManifestPath.c_str());
			if (fileAttr == INVALID_FILE_ATTRIBUTES || (fileAttr & FILE_ATTRIBUTE_DIRECTORY))
			{
				std::cout << "[Yu-Gi-Oh-Effects]: Manifest.json not found in " << findFileData.cFileName << std::endl;
				continue;
			}

			std::ifstream ManifestFile(ManifestPath);
			std::cout << "[Yu-Gi-Oh-Effects]: Processing: " << findFileData.cFileName << "\\Manifest.json" << std::endl;
			return nlohmann::json::parse(ManifestFile);
		}
	} while (FindNextFileA(hFind, &findFileData));

	FindClose(hFind);
}