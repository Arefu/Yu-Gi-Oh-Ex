#include <cstring>
#include <iostream>
#include <vector>
#include <Windows.h>

#include "Cards.h"
#include "Targets.h"
#pragma once

#include <cstdint>

unsigned long long Cards::orig_getInternalCardID = 0x14076E000;
unsigned long long Cards::orig_getKonamiCardID = 0x14076D7F0;

std::vector<unsigned __int16> Cards::CardIDs(10168);
std::vector<unsigned __int16> Cards::InternalIDs(11072);

bool Cards::isHooked = false;
bool Cards::hasRan = false;

__int64 __fastcall Cards::Get_InternalID(__int16 a1)
{
	//run orignal function
	if (isHooked == false)
	{
		typedef __int64(__fastcall* GetCardIDByInternalCardIDFunc)(unsigned int);

		// Cast the original function address to the function pointer type
		GetCardIDByInternalCardIDFunc originalFunction = reinterpret_cast<GetCardIDByInternalCardIDFunc>(orig_getInternalCardID);

		// Call the original function
		__int64 originalResult = originalFunction(static_cast<unsigned int>(a1));
		return originalResult;
	}

	auto result = Cards::InternalIDs.at(a1- 3900);
	return result;
}

__int64 __fastcall Cards::Get_KonamiID(__int16 a1)
{

	if (isHooked == false)
	{
		typedef __int64(__fastcall* GetInternalIDByKonamiIDFunc)(unsigned int);
		GetInternalIDByKonamiIDFunc orginalFunction = reinterpret_cast<GetInternalIDByKonamiIDFunc>(orig_getKonamiCardID);

		__int64 origianlResult = orginalFunction(static_cast<unsigned int>(a1));
		return origianlResult;
	}

	auto result = Cards::CardIDs.at(a1);
	return result;
}
