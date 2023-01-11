#include "Detours/Engine/Threading.h"

Address Threading::_Address;

bool __fastcall Threading::Patch_StartThreadEx(__int64 a1, __int64 a2, __int64 a3, __int64 a4)
{
	std::cout << "----------" << std::endl;
	//std::cout << "ArgList->NameAddr: " << *(&ArgList + 0xE) << std::endl;
	//std::cout << "ArgList->ThrdAddr: " << *(&ArgList + 0x2) << std::endl;
	std::cout << "a1: " << a1 << std::endl;
	std::cout << "a2: " << a2 << std::endl;
	std::cout << "a3: " << a3 << std::endl;
	std::cout << "a4: " << a4 << std::endl;
	//std::cout << "NameAddr: " << NameAddr << std::endl;
	std::cout << "----------" << std::endl;

	typedef bool StartThreadExPrototype(__int64 a1, __int64 a2, __int64 a3, __int64 a4);
	StartThreadExPrototype* StartThread = (StartThreadExPrototype*)_Address;

	return true;//StartThread( a1,  a2,  a3,  a4);
}

void Threading::Set_StartThreadExAddress(Address Address)
{
	_Address = Address;
}

