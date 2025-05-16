#include <cstring>
#include <iostream>
#include <vector>
#include <Windows.h>

#include "Cards.h"
#include "Logger.h"

#include <cstdint>

unsigned long long Cards::orig_getInternalCardID = 0x14076E000;
unsigned long long Cards::orig_getKonamiCardID = 0x14076D7F0;

std::vector<unsigned __int16> Cards::CardIDs(10168);
std::vector<unsigned __int16> Cards::InternalIDs(11072);
std::vector<Cards::IN_MEMORY_CARD_PROP> Cards::MEMCardProps(10166);

__int64 __fastcall Cards::Get_InternalID(__int16 a1)
{
	auto result = Cards::InternalIDs.at(a1- 3900);
	return result;
}

__int64 __fastcall Cards::Get_KonamiID(__int16 a1)
{

	return  0;
}

__int64 __fastcall Cards::Get_ImageForCard(void* a1, __int16 a2)
{
	return 0;
}

Cards::IN_MEMORY_CARD_PROP* __fastcall Cards::Get_CardPropsByCardID(__int16 a1)
{
	auto result = &Cards::MEMCardProps.at(a1);

	return result;
}