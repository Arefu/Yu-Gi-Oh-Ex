#include "Detours/Multiplayer/Client/Client.h"

#include <iostream>

#include "ImGui/Windows/Console/Console.h"

void Client::ConectToHost(std::string host)
{
	Console::WriteMessage("Connecting To Host: %s", host.c_str());
}