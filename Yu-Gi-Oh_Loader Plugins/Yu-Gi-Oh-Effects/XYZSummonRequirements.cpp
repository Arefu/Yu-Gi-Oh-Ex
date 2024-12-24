#include "XYZSummonRequirements.h"

#include <Windows.h>
#include <iostream>
#include <vector>

#include "json.hpp"

void XYZSummonRequirements::Setup()
{
	if (XYZSummonRequirements::Table.size() > 0)
	{
		std::cout << "[Yu-Gi-Oh-Effects]: XYZ Summon Requirements Already Setup, What's Going On?!" << std::endl;
		return;
	}

	std::cout << "[Yu-Gi-Oh-Effects]: Setting Up Base XYZ Summon Requirements" << std::endl;

	for (uintptr_t Address = XYZSummonRequirements::Base; Address < XYZSummonRequirements::Base + XYZSummonRequirements::Length; Address += 0x6)
	{
		Requirements Req;
		Req._Card = (*reinterpret_cast<short*>(Address));
		Req._Requirement = (*reinterpret_cast<short*>(Address + 0x2));
		Req._Count = (*reinterpret_cast<short*>(Address + 0x4));
	}

	std::cout << "[Yu-Gi-Oh-Effects]: Base XYZ Summon Requirements Setup Complete" << std::endl;
}

bool XYZSummonRequirements::ProcessChanges()
{
	std::cout << "[Yu-Gi-Oh-Effects]: Processing XYZ Summon Requirement Changes" << std::endl;

	// JSON TOML So many choices...

	std::cout << "[Yu-Gi-Oh-Effects]: XYZ Summon Requirement Changes Processed" << std::endl;
	return true;
}

short XYZSummonRequirements::ChangeCardAtIndex(short Index, short NewCard)
{
	if(Index > Length)
		return -1;



	return 0;
}