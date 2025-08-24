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

#pragma region Insert Instructions
	/*
		These functions insert OpCodes at specific addresses, they will replicate the original's size.
	*/

	/// <summary>
	/// Insert MOV <Register> <Value> into targetAddress
	/// </summary>
	/// <param name="targetAddress">The Desitnation Address of where you want the MOV being Inserted.</param>
	/// <param name="value">The Desired Value to Write</param>
	/// <param name="reg">The TargetRegister</param>
	/// <param name="Protected">Set RXW Code Page Permissions on Target Address</param>
	/// <param name="originalLength">Expected length of Instruction</param>
	/// <returns></returns>
	static uint8_t* InsertMOV(void* targetAddress, uint32_t value, X64Register reg, bool Protected, size_t originalLength);
#pragma endregion

	static uint8_t* EmplaceMOVSX(void* writeLocation, Memory::X64Register targetReg, Memory::X64Register offsetReg, uint32_t displacement, bool Protected, int writeLength);

	static uint8_t* PatchBytes(void* address, const uint8_t* values, size_t size, bool Protected);
	static uint8_t* PatchZeros(void* address, size_t size, bool Protected);

	static uint8_t* EmplaceJMP(void* targetAddress, uintptr_t jumpAddress, bool Protected);
	static uint8_t* EmplaceCALL(void* targetAddress, uintptr_t callAddress, bool Protected);

	//TODO: This EmplaceMOV ALWAYS writes 10 bytes, that isn't always needed. I also shouldn't need to always JMP to Code Caves

	static uint8_t* EmplaceMOV(void* targetAddress, uintptr_t value, X64Register Register, bool Protected);
	static uint8_t* EmplaceCMP(void* targetAddress, uintptr_t value, X64Register Register, bool Protected);
	static uint8_t* EmplaceLEARelativeRsi(void* targetAddress, void* baseAddress, bool Protected);
	static uint8_t* EmplaceAdd(void* address, Memory::X64Register dstRegister, Memory::X64Register srcRegister, bool Protected);
	static uint8_t* EmplaceRET(void* targetAddress, bool Protected);
	static uint8_t* EmplaceNOP(void* targetAddress, bool Protected, int fillLength);
private:
};
