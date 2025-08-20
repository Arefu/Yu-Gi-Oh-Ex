#pragma once
#include <Windows.h>
#include <cstdint>

static class Memory
{
public:

	enum class X64Register : uint8_t {
		RAX = 0x00,
		RCX = 0x01,
		RDX = 0x02,
		RBX = 0x03,
		RSP = 0x04,
		RBP = 0x05,
		RSI = 0x06,
		RDI = 0x07,
		R8 = 0x08,
		R9 = 0x09,
		R10 = 0x0A,
		R11 = 0x0B,
		R12 = 0x0C,
		R13 = 0x0D,
		R14 = 0x0E,
		R15 = 0x0F
	};

	static uint8_t* PatchBytes(void* address, const uint8_t* values, size_t size, bool Protected);
	static uint8_t* PatchZeros(void* address, size_t size, bool Protected);

	static uint8_t* EmplaceJMP(void* targetAddress, uintptr_t jumpAddress, bool Protected);
	static uint8_t* EmplaceCALL(void* targetAddress, uintptr_t callAddress, bool Protected);
	static uint8_t* EmplaceMOV(void* targetAddress, uintptr_t value, X64Register Register, bool Protected);
	static uint8_t* EmplaceCMP(void* targetAddress, uintptr_t value, X64Register Register, bool Protected);
	static uint8_t* EmplaceLEARelativeRsi(void* targetAddress, void* baseAddress, bool Protected);
	static uint8_t* EmplaceRET(void* targetAddress, bool Protected);
	static uint8_t* EmplaceNOP(void* targetAddress, bool Protected, int fillLength);
private:
};
