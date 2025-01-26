#pragma once
#include <Windows.h>
#include <cstdint>

static class Memory
{
public:
	static bool PatchByte(void* address, uint8_t value);
	static bool PatchBytes(void* address, const uint8_t* values, size_t size, bool Protected);

	static bool EmplaceJMP(void* targetAddress, uintptr_t jumpAddress, bool Protected);
	static bool EmplaceCALL(void* targetAddress, uintptr_t callAddress, bool Protected);
    static bool EmplaceMOV(void* targetAddress, uintptr_t value, bool Protected);
    static bool EmplaceRET(void* targetAddress, bool Protected);


};

