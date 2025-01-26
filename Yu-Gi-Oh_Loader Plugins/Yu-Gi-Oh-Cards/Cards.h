#pragma once

#include <Windows.h>
#include <cstdint>


class Cards
{
public:
	size_t BASE_LIMIT = 11072;
	static uint16_t INTERNAL_IDs[11072];
	static uint16_t CARD_IDs[11072];

	static __int64 __fastcall Get_InternalID(__int16 a1);
	static __int64 __fastcall Get_CardID(__int16 a1);
};