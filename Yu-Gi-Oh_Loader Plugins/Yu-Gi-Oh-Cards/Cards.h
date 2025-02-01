#pragma once

#include <Windows.h>
#include <cstdint>

class Cards
{
public:
	struct MEMORY_CARD_PROP
	{
		int CARD_ID;
		int ATK;
		int DEF;
		int EffectType;
		int Type;
		int Attribute;
		int Level;
		int Property;
		int LimitedStatus;
		int Field_10;
		int Field_12;
		int Field_13;
	};

	static uint16_t INTERNAL_IDs[0xFFFFF];
	static uint16_t CARD_IDs[0xFFFFF];
	static MEMORY_CARD_PROP CARD_PROPS[0xFFFFF];

	static __int64 __fastcall Get_InternalID(__int16 a1);
	static __int64 __fastcall Get_CardID(__int16 a1);

	static MEMORY_CARD_PROP* __fastcall Get_CardProps(unsigned int a1);

private:
};