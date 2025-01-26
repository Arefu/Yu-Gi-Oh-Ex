#include "Memory.h"
#include <Windows.h>
#include <cstdint>
#include <iostream>
#include <string>
#include <iomanip>

bool Memory::PatchBytes(void* address, const uint8_t* values, size_t size, bool Protected) {
	DWORD oldProtect;
    if (Protected) {
        if (VirtualProtect(address, size, PAGE_EXECUTE_READWRITE, &oldProtect)) {
            memcpy(address, values, size);
            return true;
        }
    }
    else {
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

bool Memory::EmplaceMOV(void* targetAddress, uintptr_t value, bool Protected) {
    uint8_t movOpCode[10] = { 0x48, 0xB9 };
    *reinterpret_cast<uint64_t*>(&movOpCode[2]) = value;

    return PatchBytes(targetAddress, movOpCode, sizeof(movOpCode), Protected);
}

bool Memory::EmplaceRET(void* targetAddress, bool Protected) {
    uint8_t retOpCode = 0xC3; 
    return PatchBytes(targetAddress, &retOpCode, sizeof(retOpCode),  Protected);
}