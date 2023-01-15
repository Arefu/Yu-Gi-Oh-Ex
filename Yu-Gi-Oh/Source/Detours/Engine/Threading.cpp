#include "Detours/Engine/Threading.h"

Address Threading::_Address;

bool __fastcall Threading::Patch_StartThreadEx(void *ArgList, __int64 a1, __int64 NameAddr)
{
	std::cout << "----------" << std::endl;
	std::cout << "ArgList->NameAddr: " << *(&ArgList + 0xE) << std::endl;
	std::cout << "ArgList->ThrdAddr: " << *(&ArgList + 0x2) << std::endl;
	std::cout << "a1: " << a1 << std::endl;
	std::cout << "NameAddr: " << NameAddr << std::endl;
	std::cout << "----------" << std::endl;

	typedef bool StartThreadExPrototype(void* ArgList, __int64 a1, __int64 NameAddr);
	StartThreadExPrototype* StartThread = (StartThreadExPrototype*)_Address;

	return StartThread(ArgList, a1, NameAddr);
}

void Threading::Set_StartThreadExAddress(Address Address)
{
	_Address = Address;
}

