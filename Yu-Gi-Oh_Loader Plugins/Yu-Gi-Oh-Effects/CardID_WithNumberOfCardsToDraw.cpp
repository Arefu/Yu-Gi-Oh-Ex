#include "CardID_WithNumberOfCardsToDraw.h"

#include <Windows.h>
#include <iostream>
#include <string>
#include <fstream>
#include <map>
#include <json.hpp>


#include "FS_Utility.h"
#include "Manifest.h"

void CardID_WithNumberOfCardsToDraw::Setup()
{
	if (CardID_WithNumberOfCardsToDraw::Table.size() > 0)
	{
		std::cout << "[Yu-Gi-Oh-Effects]: Card ID With Number of Cards to Draw Already Setup, What's Going On?!" << std::endl;
		return;
	}

	std::cout << "[Yu-Gi-Oh-Effects]: Setting Up Base Card ID With Number of Cards to Draw" << std::endl;

	for (uintptr_t Address = CardID_WithNumberOfCardsToDraw::Base; Address < CardID_WithNumberOfCardsToDraw::Base + CardID_WithNumberOfCardsToDraw::Length; Address += 0x6)
	{
		Requirements Req;
		Req._Card = (*reinterpret_cast<short*>(Address));
		Req._Count = (*reinterpret_cast<short*>(Address + 0x2));
	}

	std::cout << "[Yu-Gi-Oh-Effects]: Base Card ID With Number of Cards to Draw Setup Complete" << std::endl;
}

bool CardID_WithNumberOfCardsToDraw::ProcessChanges(std::string Path)
{
    std::cout << "[Yu-Gi-Oh-Effects]: Processing Card ID With Number of Cards to Draw Changes" << std::endl;

    nlohmann::json Manifest = Utility::Find_ManifestFilesInDirectory(Path);
    std::cout << "[Yu-Gi-Oh-Effects]: Loading: " << Manifest["Title"] << std::endl;

	auto Changes = _Manifest::ProcessChanges(Manifest, "CardID_WithNumberOfCardsToDraw");
	for (auto Change : Changes)
	{
		
	}
	//Deletions
	//Additions




    std::cout << "[Yu-Gi-Oh-Effects]: Card ID With Number of Cards to Draw Processed" << std::endl;
    return true;
}

short CardID_WithNumberOfCardsToDraw::ChangeCardAtIndex(short Index, short NewCard)
{
	if (Index > Length)
		return -1;

	return 0;
}