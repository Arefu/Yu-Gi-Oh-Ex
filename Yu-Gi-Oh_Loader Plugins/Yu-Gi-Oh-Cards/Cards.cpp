#include <Windows.h>
#include "Cards.h"

__int64 __fastcall Cards::Get_InternalID(__int16 a1)
{
	a1 = a1 - 3900;

	return Cards::INTERNAL_IDs[a1];
}

__int64 __fastcall Cards::Get_CardID(__int16 a1)
{
	a1 = a1 - 3900;

	return Cards::CARD_IDs[a1];
}