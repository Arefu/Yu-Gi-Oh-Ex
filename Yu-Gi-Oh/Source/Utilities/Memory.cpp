#include <Windows.h>
#include <tlhelp32.h>
#include <iostream>
#include <iostream>
#include <Psapi.h>
#include <vector>

#include "Utilities/Memory.h"

int Memory::GetSceneValue()
{
	DWORD Address = Memory::GetThreadStackAddr(0);
	DWORD Base = 0;

	Address = Address - 2752;
	Base = *(long long*)Address;
	Base += 0x10;
	Base = *(long long*)Base;
	Base += 0x358;
	Base = *(long long*)Base;

	Base = *(long long*)Base;
	return Base;
}

void Memory::SetSceneValue(DWORD Value)
{
	DWORD Address = Memory::GetThreadStackAddr(0);
	DWORD Base = 0;

	Address = Address - 2752;
	Base = *(long long*)Address;
	Base += 0x10;
	Base = *(long long*)Base;
	Base += 0x358;
	Base = *(long long*)Base;

	*(long long*)Base = Value;
}

DWORD Memory::GetThreadStackAddr(int AddrToGet)
{
	DWORD ThreadId = 0;
	HANDLE Snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
	std::vector<DWORD> Threads = std::vector<DWORD>();

	if (Snapshot == INVALID_HANDLE_VALUE) {
		return FALSE;
	}

	THREADENTRY32 ThreadEntry{};
	ThreadEntry.dwSize = sizeof(ThreadEntry);

	if (Thread32First(Snapshot, &ThreadEntry))
	{
		do
		{
			if (ThreadEntry.th32OwnerProcessID == GetCurrentProcessId())
			{
				Threads.push_back(ThreadEntry.th32ThreadID);
			}
		} while (Thread32Next(Snapshot, &ThreadEntry));
	}
	CloseHandle(Snapshot);

	int Stack = 0;
	for (auto x = Threads.begin(); x != Threads.end(); ++x) {
		HANDLE threadHandle = OpenThread(THREAD_GET_CONTEXT | THREAD_QUERY_INFORMATION, FALSE, *x);
		DWORD threadStartAddress = GetThreadStartAddress(GetCurrentProcess(), threadHandle);

		if (Stack == AddrToGet)
			return threadStartAddress;
		Stack++;
	}

	return 0;
}

DWORD Memory::GetThreadStartAddress(HANDLE processHandle, HANDLE hThread) {
	DWORD used = 0, ret = 0;
	DWORD stacktop = 0, result = 0;

	MODULEINFO mi;

	GetModuleInformation(processHandle, GetModuleHandle("kernel32.dll"), &mi, sizeof(mi));
	stacktop = (DWORD)GetThreadStackTopAddress_x86(processHandle, hThread);

	CloseHandle(hThread);

	if (stacktop) {
		auto* buf32 = new DWORD[4096];

		if (ReadProcessMemory(processHandle, (LPCVOID)(stacktop - 4096), buf32, 4096, NULL)) {
			for (int i = 4096 / 4 - 1; i >= 0; --i) {
				if (buf32[i] >= (DWORD)mi.lpBaseOfDll && buf32[i] <= (DWORD)mi.lpBaseOfDll + mi.SizeOfImage) {
					result = stacktop - 4096 + i * 4;
					break;
				}
			}
		}

		delete buf32;
	}

	return result;
}

void* Memory::GetThreadStackTopAddress_x86(HANDLE hProcess, HANDLE hThread)
{
	bool loadedManually = false;
	HMODULE module = GetModuleHandle("ntdll.dll");

	if (!module)
	{
		module = LoadLibrary("ntdll.dll");
		loadedManually = true;
	}

	NTSTATUS(__stdcall * NtQueryInformationThread)(HANDLE ThreadHandle, THREADINFOCLASS ThreadInformationClass, PVOID ThreadInformation, ULONG ThreadInformationLength, PULONG ReturnLength);
	NtQueryInformationThread = reinterpret_cast<decltype(NtQueryInformationThread)>(GetProcAddress(module, "NtQueryInformationThread"));

	if (NtQueryInformationThread)
	{
		NT_TIB tib = { 0 };
		THREAD_BASIC_INFORMATION tbi = { 0 };

		NTSTATUS status = NtQueryInformationThread(hThread, ThreadBasicInformation, &tbi, sizeof(tbi), nullptr);
		if (status >= 0)
		{
			ReadProcessMemory(hProcess, tbi.TebBaseAddress, &tib, sizeof(tbi), nullptr);

			if (loadedManually)
			{
				FreeLibrary(module);
			}
			return tib.StackBase;
		}
	}

	if (loadedManually)
	{
		FreeLibrary(module);
	}

	return nullptr;
}