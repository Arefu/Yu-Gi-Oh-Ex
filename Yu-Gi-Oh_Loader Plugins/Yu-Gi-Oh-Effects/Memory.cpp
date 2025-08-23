#include "Memory.h"
#include <cstdint>
#include <vector>
#include <iomanip>
#include <iostream>
#include <string>
#include <Windows.h>

#include "Logger.h"

uint8_t* Memory::EmplaceMOVSX(void* writeLocation, Memory::X64Register targetReg, Memory::X64Register offsetReg, uint32_t displacement, bool Protected, int writeLength)
{
	std::vector<uint8_t> code;

	uint8_t rex = 0x40;
	if ((static_cast<int>(targetReg) & 8)) rex |= 0x04;
	if ((static_cast<int>(offsetReg) & 8)) rex |= 0x01;
	code.push_back(rex);

	code.push_back(0x0F);
	code.push_back(0xBF);

	uint8_t modrm = 0x80;
	modrm |= ((static_cast<int>(targetReg) & 7) << 3);
	modrm |= (static_cast<int>(offsetReg) & 7);
	code.push_back(modrm);

	for (int i = 0; i < 4; i++)
		code.push_back((displacement >> (i * 8)) & 0xFF);

	if (code.size() < writeLength) {
		code.resize(writeLength, 0x90);
	}

	return PatchBytes(writeLocation, code.data(), code.size(), Protected);
}

uint8_t* Memory::PatchBytes(void* address, const uint8_t* values, size_t size, bool Protected) {
	DWORD oldProtect;
	if (Protected) {
		if (VirtualProtect(address, size, PAGE_EXECUTE_READWRITE, &oldProtect)) {
			memcpy(address, values, size);
			return static_cast<uint8_t*>(address) + size;
		}
	}
	else {
		memcpy(address, values, size);
		return static_cast<uint8_t*>(address) + size;
	}
	return nullptr;
}

uint8_t* Memory::EmplaceJMP(void* targetAddress, uintptr_t jumpAddress, bool Protected) {
	uint8_t jmpOpCode[7] = { 0xE9 };
	int32_t relativeOffset = static_cast<int32_t>(jumpAddress - reinterpret_cast<uintptr_t>(targetAddress) - 5);
	*reinterpret_cast<int32_t*>(&jmpOpCode[1]) = relativeOffset;
	jmpOpCode[5] = 0x90;
	jmpOpCode[6] = 0x90;
	return PatchBytes(targetAddress, jmpOpCode, sizeof(jmpOpCode), Protected);
}

uint8_t* Memory::EmplaceCALL(void* targetAddress, uintptr_t callAddress, bool Protected) {
	uint8_t callOpCode[8] = { 0xE8 };
	int32_t relativeOffset = static_cast<int32_t>(callAddress - reinterpret_cast<uintptr_t>(targetAddress) - 5);
	*reinterpret_cast<int32_t*>(&callOpCode[1]) = relativeOffset;
	callOpCode[5] = 0x90;
	callOpCode[6] = 0x90;
	callOpCode[7] = 0x90;
	return PatchBytes(targetAddress, callOpCode, sizeof(callOpCode), Protected);
}

uint8_t* Memory::EmplaceMOV(void* targetAddress, uintptr_t value, X64Register reg, bool Protected, size_t  originalLength) {
	std::vector<uint8_t> code;

	// REX prefix
	uint8_t rex = 0x40;
	if (static_cast<int>(reg) >= static_cast<int>(X64Register::R8)) rex |= 0x01; // B
	rex |= 0x08; // W = 64-bit
	code.push_back(rex);

	// Opcode
	switch (reg) {
	case X64Register::RAX: code.push_back(0xB8); break;
	case X64Register::RCX: code.push_back(0xB9); break;
	case X64Register::RDX: code.push_back(0xBA); break;
	case X64Register::RBX: code.push_back(0xBB); break;
	case X64Register::RSP: code.push_back(0xBC); break;
	case X64Register::RBP: code.push_back(0xBD); break;
	case X64Register::RSI: code.push_back(0xBE); break;
	case X64Register::RDI: code.push_back(0xBF); break;
	case X64Register::R8:  code.push_back(0xB8); break;
	case X64Register::R9:  code.push_back(0xB9); break;
	case X64Register::R10: code.push_back(0xBA); break;
	case X64Register::R11: code.push_back(0xBB); break;
	case X64Register::R12: code.push_back(0xBC); break;
	case X64Register::R13: code.push_back(0xBD); break;
	case X64Register::R14: code.push_back(0xBE); break;
	case X64Register::R15: code.push_back(0xBF); break;
	default: return nullptr;
	}

	// Immediate
	if (originalLength <= 6) {
		// Use 32-bit immediate if overwriting small instruction
		for (int i = 0; i < 4; i++)
			code.push_back((value >> (i * 8)) & 0xFF);
	}
	else {
		// Use full 64-bit immediate
		for (int i = 0; i < 8; i++)
			code.push_back((value >> (i * 8)) & 0xFF);
	}

	// Pad with NOPs if needed
	while (code.size() < originalLength)
		code.push_back(0x90);

	return PatchBytes(targetAddress, code.data(), code.size(), Protected);
}

uint8_t* Memory::EmplaceRET(void* targetAddress, bool Protected) {
	uint8_t retOpCode = 0xC3;
	return PatchBytes(targetAddress, &retOpCode, sizeof(retOpCode), Protected);
}

uint8_t* Memory::EmplaceNOP(void* targetAddress, bool Protected, int fillLength = 0) {
	std::vector<uint8_t> nopBytes(fillLength, 0x90);
	return PatchBytes(targetAddress, nopBytes.data(), fillLength, Protected);
}

uint8_t* Memory::EmplaceCMP(void* targetAddress, uintptr_t value, X64Register reg, bool Protected) {
	uint8_t cmpOpCode[10] = { 0x48, 0x81 };

	switch (reg) {
	case X64Register::RAX:
		cmpOpCode[1] = 0x3D;
		break;
	case X64Register::RCX:
		cmpOpCode[1] = 0xF9;
		break;
	case X64Register::RDX:
		cmpOpCode[1] = 0xFA;
		break;
	case X64Register::RBX:
		cmpOpCode[1] = 0xFB;
		break;
	case X64Register::RSP:
		cmpOpCode[1] = 0xFC;
		break;
	case X64Register::RBP:
		cmpOpCode[1] = 0xFD;
		break;
	case X64Register::RSI:
		cmpOpCode[1] = 0xFE;
		break;
	case X64Register::RDI:
		cmpOpCode[1] = 0xFF;
		break;
	default:
		return nullptr;
	}

	*reinterpret_cast<uint32_t*>(&cmpOpCode[2]) = static_cast<uint32_t>(value);

	return PatchBytes(targetAddress, cmpOpCode, sizeof(cmpOpCode), Protected);
}

uint8_t* Memory::PatchZeros(void* address, size_t size, bool Protected)
{
	std::vector<uint8_t> zeros(size, 0);
	return PatchBytes(address, zeros.data(), size, Protected) ? static_cast<uint8_t*>(address) + size : nullptr;
}

uint8_t* Memory::EmplaceLEARelativeRsi(void* targetAddress, void* baseAddress, bool Protected)
{
	uint8_t leaInstruction[8]; // 7 bytes + 1 for alignment maybe
	leaInstruction[0] = 0x48;
	leaInstruction[1] = 0x8D;
	leaInstruction[2] = 0x14;
	leaInstruction[3] = 0xB6;

	uint64_t instrAddr = reinterpret_cast<uint64_t>(targetAddress);
	uint64_t nextInstrAddr = instrAddr + sizeof(leaInstruction);
	uint64_t baseAddr = reinterpret_cast<uint64_t>(baseAddress);

	int32_t disp = static_cast<int32_t>(baseAddr - nextInstrAddr);

	*reinterpret_cast<int32_t*>(&leaInstruction[4]) = disp;

	return PatchBytes(targetAddress, leaInstruction, sizeof(leaInstruction), Protected);
}

uint8_t* Memory::EmplaceAdd(void* address, Memory::X64Register dst, Memory::X64Register src, bool Protected)
{
	// REX prefix: 0x48 (set W=1 for 64-bit operand size)
	uint8_t rex = 0x48;

	// Extend if registers are R8–R15
	if (static_cast<uint8_t>(src) & 0x08) rex |= 0x04; // R bit
	if (static_cast<uint8_t>(dst) & 0x08) rex |= 0x01; // B bit

	// ModR/M byte: mod = 11b (register-direct)
	uint8_t modrm = 0xC0;
	modrm |= ((static_cast<uint8_t>(src) & 0x07) << 3); // reg field = src
	modrm |= (static_cast<uint8_t>(dst) & 0x07);        // rm field = dst

	uint8_t bytes[3] = { rex, 0x01, modrm };

	return Memory::PatchBytes(address, bytes, sizeof(bytes), Protected);
}