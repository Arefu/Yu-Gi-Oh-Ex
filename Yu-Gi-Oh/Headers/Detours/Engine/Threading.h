#pragma once

#include <Yu-Gi-Oh.h>

class Threading
{
public:
	static bool __fastcall  Patch_StartThreadEx(__int64 a1, __int64 a2, __int64 a3, __int64 a4);
	static void Set_StartThreadExAddress(Address Address);
private:
	static Address _Address;
};

