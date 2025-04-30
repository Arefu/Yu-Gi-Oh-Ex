#include "Memory.h"
#include <cstdint>
#include <iomanip>
#include <iostream>
#include <string>
#include <Windows.h>

#include "Cards.h"
#include "Targets.h"
#include "Logger.h"

bool Memory::PatchBytes(void* address, const uint8_t* values, size_t size, bool Protected) {
	DWORD oldProtect;
	if (Protected) {
		if (VirtualProtect(address, size, PAGE_EXECUTE_READWRITE, &oldProtect)) {
			memcpy(address, values, size);
			return true;
		}
	}
	else {
		std::cout <<"FAIL";
		memcpy(address, values, size);

		return true;
	}
	std::cout << "Failed to patch bytes" << std::endl;
	return false;
}

bool Memory::EmplaceJMP(void* targetAddress, uintptr_t jumpAddress, bool Protected) {
	uint8_t jmpOpCode[7] = { 0xE9 };
	int32_t relativeOffset = static_cast<int32_t>(jumpAddress - reinterpret_cast<uintptr_t>(targetAddress) - 5);
	*reinterpret_cast<int32_t*>(&jmpOpCode[1]) = relativeOffset;
	jmpOpCode[5] = 0x90;
	jmpOpCode[6] = 0x90;
	return PatchBytes(targetAddress, jmpOpCode, sizeof(jmpOpCode), Protected);
}

bool Memory::EmplaceCALL(void* targetAddress, uintptr_t callAddress, bool Protected) {
	uint8_t callOpCode[7] = { 0xE8 };
	int32_t relativeOffset = static_cast<int32_t>(callAddress - reinterpret_cast<uintptr_t>(targetAddress) - 5);
	*reinterpret_cast<int32_t*>(&callOpCode[1]) = relativeOffset;
	callOpCode[5] = 0x90;
	callOpCode[6] = 0x90;
	return PatchBytes(targetAddress, callOpCode, sizeof(callOpCode), Protected);
}

bool Memory::EmplaceMOV(void* targetAddress, uintptr_t value, X64Register reg, bool Protected) {
	uint8_t movOpCode[10] = { 0 };

	if (static_cast<int>(reg) >= static_cast<int>(X64Register::R8)) {
		movOpCode[0] = 0x49;
	}
	else {
		movOpCode[0] = 0x48;
	}

	switch (reg) {
	case X64Register::RAX:
		movOpCode[1] = 0xB8;
		break;
	case X64Register::RCX:
		movOpCode[1] = 0xB9;
		break;
	case X64Register::RDX:
		movOpCode[1] = 0xBA;
		break;
	case X64Register::RBX:
		movOpCode[1] = 0xBB;
		break;
	case X64Register::RSP:
		movOpCode[1] = 0xBC;
		break;
	case X64Register::RBP:
		movOpCode[1] = 0xBD;
		break;
	case X64Register::RSI:
		movOpCode[1] = 0xBE;
		break;
	case X64Register::RDI:
		movOpCode[1] = 0xBF;
		break;
	case X64Register::R8:
		movOpCode[1] = 0xB8;
		break;
	case X64Register::R9:
		movOpCode[1] = 0xB9;
		break;
	case X64Register::R10:
		movOpCode[1] = 0xBA;
		break;
	case X64Register::R11:
		movOpCode[1] = 0xBB;
		break;
	case X64Register::R12:
		movOpCode[1] = 0xBC;
		break;
	case X64Register::R13:
		movOpCode[1] = 0xBD;
		break;
	case X64Register::R14:
		movOpCode[1] = 0xBE;
		break;
	case X64Register::R15:
		movOpCode[1] = 0xBF;
		break;
	default:
		return false;
	}

	*reinterpret_cast<uint64_t*>(&movOpCode[2]) = value;

	return PatchBytes(targetAddress, movOpCode, sizeof(movOpCode), Protected);
}

bool Memory::EmplaceRET(void* targetAddress, bool Protected) {
	uint8_t retOpCode = 0xC3;
	return PatchBytes(targetAddress, &retOpCode, sizeof(retOpCode), Protected);
}

bool Memory::EmplaceNOP(void* targetAddress, bool Protected, int fillLength = 0) {
	std::vector<uint8_t> nopBytes(fillLength, 0x90);
	return PatchBytes(targetAddress, nopBytes.data(), fillLength, Protected);
}

bool Memory::EmplaceCMP(void* targetAddress, uintptr_t value, X64Register reg, bool Protected) {
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
		return false;
	}

	*reinterpret_cast<uint32_t*>(&cmpOpCode[2]) = static_cast<uint32_t>(value);

	return PatchBytes(targetAddress, cmpOpCode, sizeof(cmpOpCode), Protected);
}

