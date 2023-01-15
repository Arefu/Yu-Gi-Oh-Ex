#pragma once

#include <Yu-Gi-Oh.h>

class Threading
{
public:
	static bool __fastcall  Patch_StartThreadEx(void* ArgList, __int64 a1, __int64 NameAddr);
	static void Set_StartThreadExAddress(Address Address);
private:
	static Address _Address;
};

