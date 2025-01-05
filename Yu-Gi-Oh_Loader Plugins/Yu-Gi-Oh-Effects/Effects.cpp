#include <map>
#include <Windows.h>
#include <fstream>
#include <iostream>
#include <vector>

#include <json.hpp>

#include "Config.h"
#include "Effects.h"

using namespace nlohmann;

std::map<std::string, short> Effects::_DrawAmountsForCards;


void Effects::h_FindDrawAmountForCard(unsigned short* Card)
{
	std::cout << "[Yu-Gi-Oh-Effects]: Attempting To Find Card Draw For " << *Card << std::endl;
	_DrawAmountsForCards = u_Populate_FromManifest_ForKVP("FindDrawAmountForCard");

	reinterpret_cast<__int64(__fastcall*)(unsigned __int16* a1)>(Effects_Functions::o_GetNumberOfCardsToDraw)(Card);

	/*if (_DrawAmountsForCards.find(*Card) != _DrawAmountsForCards.end())
	{
		std::cout << "[Yu-Gi-Oh-Effects]: Found Card ID: " << *Card << " With Draw Amount: " << _DrawAmountsForCards[*Card] << std::endl;
		reinterpret_cast<__int64(__fastcall*)(unsigned __int16* a1)>(Effects_Functions::o_GetNumberOfCardsToDraw)(Card);
		return _DrawAmountsForCards[*Card];
	}
	else
	{
		std::cout << "[Yu-Gi-Oh-Effects]: Failed To Find Card ID: " << Card << std::endl;
		return reinterpret_cast<__int64 (__fastcall*)(unsigned __int16* a1)>(Effects_Functions::o_GetNumberOfCardsToDraw)(Card);
	}*/

	
}

std::map<short, short> Effects::u_Populate_DrawAmountsForCards()
{
	std::map<short, short> Cards;
	long long Offset = 0x140B15110;
	for (uintptr_t Address = Offset; Address < Offset + 470; Address += 0x4)
	{
		short _Card = (*reinterpret_cast<short*>(Address));
		short _Count = (*reinterpret_cast<short*>(Address + 0x2));

		std::cout << "[Yu-Gi-Oh-Effects]: Found Card ID: " << _Card << " With Draw Amount: " << _Count << std::endl;
		//Cards.emplace(_Card, _Count);
	}
	
	return Cards;
}

std::map<std::string, short> Effects::u_Populate_FromManifest_ForKVP(std::string Tag)
{
	std::map<std::string, short> KVP;
	auto Manifest = u_FindAndSelectManfestFile(Tag);
	auto Changes = Manifest["Change_Requests"];

	if (Tag == "FindDrawAmountForCard")
	{
		if (!Changes.empty()) {
			for (auto& [key, value] : Changes[0].items()) {
				try
				{
					std::string K = key;
				//	short V = value;

				//	std::cout << "[Yu-Gi-Oh-Effects]: Adding Card ID: " << K << " With Draw Amount: " << V << std::endl;
				}
				catch (const std::exception& e)
				{
					std::cout << "[Yu-Gi-Oh-Effects]: Failed To Add Card ID: " << key << " With Draw Amount: " << value << std::endl;
				}
			}
		}
	}

	return KVP;
}

json Effects::u_FindAndSelectManfestFile(std::string Tag)
{
	WIN32_FIND_DATAA findFileData;

	std::string Path = Config::Get_WorkingDirectory() + "\\*";
	HANDLE hFind = FindFirstFileA(Path.c_str(), &findFileData);

	if (hFind == INVALID_HANDLE_VALUE) {
		std::cout << "No folders found." << std::endl;
		return NULL;
	}

	do {
		if (findFileData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) {
			std::string folderName = findFileData.cFileName;
			if (folderName != "." && folderName != "..") {
				std::string jsonPath = Config::Get_WorkingDirectory() + "\\" + folderName + "\\*.json";
				HANDLE hJsonFind = FindFirstFileA(jsonPath.c_str(), &findFileData);
				if (hJsonFind != INVALID_HANDLE_VALUE) {

					std::ifstream f(Config::Get_WorkingDirectory() + folderName + "\\" + findFileData.cFileName);
					json Manifest = json::parse(f);
					
					if (Manifest["Tag"] == Tag)
					{
						std::cout << "[Yu-Gi-Oh-Effects]: Found & Loading: " << findFileData.cFileName << std::endl;
						return Manifest;
					}
					FindClose(hJsonFind);
				}
			}
		}
	} while (FindNextFileA(hFind, &findFileData) != 0);

	std::cout << "[Yu-Gi-Oh-Effects]: Failed To Find Manifest.json For Tag: " << Tag << std::endl;
	FindClose(hFind);
}