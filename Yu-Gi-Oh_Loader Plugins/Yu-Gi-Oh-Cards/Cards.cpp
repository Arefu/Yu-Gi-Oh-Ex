#include <Windows.h>
#include <iostream>

#include "Cards.h"

__int64 __fastcall Cards::Get_InternalID(__int16 a1)
{
	a1 = a1 - 3900;
	if(a1 == 0xFFAF)
		std::cout << "special case" << std::endl;
	return Cards::INTERNAL_IDs[a1];
}

__int64 __fastcall Cards::Get_CardID(__int16 a1)
{
	a1 = a1 - 3900;
	if (a1 == 0xFFAF)
		std::cout << "special case" << std::endl;
	return Cards::CARD_IDs[a1];
}


bool PropCopied = false;
Cards::MEMORY_CARD_PROP* __fastcall Cards::Get_CardProps(unsigned int a1)
{
	if (PropCopied == false)
	{
		std::cout << "Copying Props" << std::endl;
		memcpy(&Cards::CARD_PROPS, reinterpret_cast<void*>(0x1427D0C30), 487920);
		PropCopied = true;
		std::cout << &Cards::CARD_PROPS << std::endl;
	}


	return &Cards::CARD_PROPS[a1];
}