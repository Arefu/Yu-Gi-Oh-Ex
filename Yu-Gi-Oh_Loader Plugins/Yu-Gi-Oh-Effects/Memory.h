#pragma once
#include <Windows.h>




static class Memory
{
public:
	 template <typename T>
	static bool UpdateMemoryValue(uintptr_t address, const T& newValue);

private:
	static bool ChangeMemoryProtection(void* address, size_t size, DWORD& oldProtect);
	static bool RestoreMemoryProtection(void* address, size_t size, DWORD oldProtect);
};