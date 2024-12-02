#include <Windows.h>
#include "imgui.h"

#include <imgui_impl_dx11.h>
#include <imgui_impl_win32.h>
#include <thread>
#include <string>
#include <detours.h>
#include <iostream>

typedef DWORD(WINAPI* _tGetTickCount)(void);
typedef ULONGLONG(WINAPI* _tGetTickCount64)(void);
typedef BOOL(WINAPI* _tQueryPerformanceCounter)(LARGE_INTEGER*);

_tGetTickCount _GTC = nullptr;
_tGetTickCount64 _GTC64 = nullptr;
_tQueryPerformanceCounter _QPC = nullptr;


DWORD _GTC_BaseTime = 0;
DWORD _GTC_OffsetTime = 0;
ULONGLONG _GTC64_BaseTime = 0;
ULONGLONG _GTC64_OffsetTime = 0;
LARGE_INTEGER _QPC_BaseTime;
LARGE_INTEGER _QPC_OffsetTime;

int speed = 2;
bool speed_Enabled = true;
bool detours_Enabled = false;

DWORD WINAPI _hGetTickCount()
{
	return _GTC_OffsetTime + ((_GTC() - _GTC_BaseTime) * speed);
}

ULONGLONG WINAPI _hGetTickCount64()
{
	return _GTC64_OffsetTime + ((_GTC64() - _GTC64_BaseTime) * speed);
}

BOOL WINAPI _hQueryPerformanceCounter(LARGE_INTEGER* lpPerformanceCount)
{
	LARGE_INTEGER x;
	_QPC(&x);
	lpPerformanceCount->QuadPart = _QPC_OffsetTime.QuadPart + ((x.QuadPart - _QPC_BaseTime.QuadPart) * speed);
	return TRUE;
}

void UndoDetour()
{
	if (detours_Enabled == false)
		return;

	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());
	DetourDetach(&(PVOID&)_GTC, _hGetTickCount);
	DetourDetach(&(PVOID&)_GTC64, _hGetTickCount64);
	DetourDetach(&(PVOID&)_QPC, _hQueryPerformanceCounter);
	DetourTransactionCommit();

	detours_Enabled = false;
}

void SetupDetour()
{
	if (detours_Enabled == true)
		return;

	DetourTransactionBegin();
	DetourUpdateThread(GetCurrentThread());
	DetourAttach(&(PVOID&)_GTC, _hGetTickCount);
	DetourAttach(&(PVOID&)_GTC64, _hGetTickCount64);
	DetourAttach(&(PVOID&)_QPC, _hQueryPerformanceCounter);
	DetourTransactionCommit();

	detours_Enabled = true;
}

extern "C" __declspec(dllexport) void SetContext(ImGuiContext* Context)
{
	ImGui::SetCurrentContext(Context);
}

extern "C" __declspec(dllexport) void ProcessDetours()
{
	_GTC = GetTickCount;
	_GTC64 = GetTickCount64;
	_QPC = QueryPerformanceCounter;

	LARGE_INTEGER _QPC_BaseTime = LARGE_INTEGER();

	_GTC_BaseTime = _GTC();
	_GTC_OffsetTime = _GTC_BaseTime;
	_GTC64_BaseTime = _GTC64();
	_GTC64_OffsetTime = _GTC64_BaseTime;

	_QPC(&_QPC_BaseTime);

	_QPC_OffsetTime.QuadPart = _QPC_BaseTime.QuadPart;

	SetupDetour();
}

extern "C" __declspec(dllexport) void ProcessWindow()
{
	ImGui::Begin("Yu-Gi-Oh! Speed Hacks");
	ImGui::Checkbox("Enabled", &speed_Enabled);
	if (speed_Enabled)
		SetupDetour();
	else
		
		UndoDetour();
	ImGui::InputInt("Speed Hack Speed", &speed, 1, 1, ImGuiInputTextFlags_AlwaysInsertMode);

	_GTC_OffsetTime = _hGetTickCount();
	_GTC64_OffsetTime = _hGetTickCount64();

	_hQueryPerformanceCounter(&_QPC_OffsetTime);

	_GTC_BaseTime = _GTC();
	_GTC64_BaseTime = _GTC64();
	_QPC(&_QPC_BaseTime);

	ImGui::End();
}

extern "C" _declspec(dllexport) void ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
	switch (msg)
	{
	case WM_KEYDOWN:
		switch (wParam)
		{
		case VK_SUBTRACT:
			if (speed > 1)
			{
				speed--;
			
			}
			break;
		case VK_ADD:
			if (speed < 10)
			{
				speed++;
			
			}
			break;
		}
	}
}

extern "C" _declspec(dllexport) void ProcessConfig()
{
	speed = GetPrivateProfileIntA("Yu-Gi-Oh-SpeedHacks", "Speed", 2, ".\\Config.ini");
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		break;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}
