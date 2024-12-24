#include <Windows.h>

#include "Memory.h"

bool Memory::ChangeMemoryProtection(void* address, size_t size, DWORD& oldProtect) {
    return VirtualProtect(address, size, PAGE_READWRITE, &oldProtect) != 0;
}
bool Memory::RestoreMemoryProtection(void* address, size_t size, DWORD oldProtect) {
    return VirtualProtect(address, size, oldProtect, &oldProtect) != 0;
}

template <typename T>
bool Memory::UpdateMemoryValue(uintptr_t address, const T& newValue) {
    T* target = reinterpret_cast<T*>(address);
    DWORD oldProtect;

    if (!ChangeMemoryProtection(target, sizeof(T), oldProtect)) {
        return false;
    }
    *target = newValue;
    return RestoreMemoryProtection(target, sizeof(T), oldProtect);
}