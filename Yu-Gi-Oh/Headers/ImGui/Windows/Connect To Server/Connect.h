#pragma once

#include "Detours/Multiplayer/Client/Client.h"

#include <Windows.h>
#include <string>

#include "imgui.h"

using namespace std;

class Connect
{
public:

	static void Show_ConnectWindow(BOOL* g_show_connect_window);
};
