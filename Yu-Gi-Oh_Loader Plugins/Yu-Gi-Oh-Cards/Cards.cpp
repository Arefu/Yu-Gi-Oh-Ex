#include <Windows.h>
#include <iostream>
#include <vector>
#include <cstring>

#include "Targets.h"
#include "Cards.h"

std::vector<Cards::MEMORY_CARD_PROP> Cards::_CARD_PROPS;
bool PropCopied = false;

__int64 __fastcall Cards::Get_InternalID(__int16 a1)
{
	a1 = a1 - 3900;
	return Cards::_INTERNAL_IDs.at(a1);
}

__int64 __fastcall Cards::Get_CardID(__int16 a1)
{
	a1 = a1 - 3900;
	return Cards::_CARD_IDs.at(a1);
}

Cards::MEMORY_CARD_PROP* __fastcall Cards::Get_CardProps(unsigned int a1)
{
	if (PropCopied == false)
	{
		// Copy 487920 bytes from 0x1427D0C30 to _CARD_PROPS.
		void* source = reinterpret_cast<void*>(0x1427D0C30);
		_CARD_PROPS.resize(487920 / sizeof(MEMORY_CARD_PROP));
		std::memcpy(_CARD_PROPS.data(), source, 487920);

		PropCopied = true;
	}

	return &Cards::_CARD_PROPS.at(a1);
}

__int64 Cards::Setup_CardPropTable()
{
	// Call Original Function
	__int64 result = reinterpret_cast<__int64(__fastcall*)()>(_Setup_CardPropTable)();
	return result;
}