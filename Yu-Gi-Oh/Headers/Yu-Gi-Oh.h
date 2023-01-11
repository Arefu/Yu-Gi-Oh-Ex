#pragma once

#include <Windows.h>
#include <d3d11.h>

#include "Config.h"
#include "Handlers/Player.h"
#include "ImGui/D3D11/D3D11.h"

//Global Variables
static Player g_Player1(0x143497C40, TRUE);
static Player g_Player2(0x1434989D4, TRUE);

//TODO: Player 3-4

static BOOL g_IsRunning = *(BOOL*)0x14332A391;

//Types
typedef INT64(__stdcall* Address)();
typedef unsigned __int64 QWORD;

//Callable Functions
typedef HRESULT __fastcall DXPresent(IDXGISwapChain*, UINT, UINT);

typedef __int64 sub_140014450(int, unsigned int, int, int, int, __int16, unsigned int, int);
typedef __int64 sub_14081A800(unsigned __int16);
typedef __int64 sub_140052BA0(char, int, int);
typedef __int64 sub_1408751D0();