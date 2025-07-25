#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <vector>
#include <string>

#include "Memory.h"
#include "PolymerizationTwoOrMore.h"
#include "Logger.h"
#include "Config.h"

#define TWO_OR_MORE_POLYMERIZATION 0x140ACCAD0
std::vector<PolymerizationTwoOrMore> _PolymerizationTwoOrMore(319);
void Redirect_TwoOrMorePolymerization()
{
	memcpy(_PolymerizationTwoOrMore.data(), reinterpret_cast<void*>(TWO_OR_MORE_POLYMERIZATION), sizeof(PolymerizationTwoOrMore) * 319);
	Logger::WriteLog(std::format("Setup 0x{:X}, At: {}", TWO_OR_MORE_POLYMERIZATION, reinterpret_cast<void*>(_PolymerizationTwoOrMore.data())), MODULE_NAME, 0);

	Memory::PatchZeros(reinterpret_cast<void*>(0x140ACCAD0), sizeof(PolymerizationTwoOrMore) * 319, true);

	PolymerizationTwoOrMore P{};
	P.Summons = 4386;
	P.FirstRequirement = 4007;
	P.SecondRequirement = 0;
	P.ThirdRequirement = 0;
	_PolymerizationTwoOrMore.push_back(P);

	//Update Size Check
	Memory::EmplaceMOV(reinterpret_cast<void*>(0x140006402), _PolymerizationTwoOrMore.size(), Memory::X64Register::R11, true);

	Memory::EmplaceCALL(reinterpret_cast<void*>(0x140006449), TWO_OR_MORE_POLYMERIZATION, false);

	//uint8_t* Location = Memory::EmplaceLEARelativeRsi(reinterpret_cast<void*>(TWO_OR_MORE_POLYMERIZATION), reinterpret_cast<uintptr_t>(_PolymerizationTwoOrMore.data()), Memory::X64Register::RDX, true);
   // Memory::EmplaceRET(reinterpret_cast<void*>(Location), true); Logger::WriteLog("Location = 0x" +std::format("{:016X}", reinterpret_cast<uintptr_t>(Location)),MODULE_NAME, 0);
	//Memory::EmplaceCALL(reinterpret_cast<void*>(0x140006449), TwoOrMorePolymerization, false);
	//Memory::EmplaceMOV(reinterpret_cast<void*>(0x140006622), reinterpret_cast<uintptr_t>(_PolymerizationTwoOrMore.data()), Memory::X64Register::R11, true);

	//Memory::EmplaceCALL(reinterpret_cast<void*>(0x140006449), TwoOrMorePolymerization, false);
	//Memory::EmplaceMOV(reinterpret_cast<void*>(0x1400062AA), reinterpret_cast<uintptr_t>(_PolymerizationTwoOrMore.data()), Memory::X64Register::RDX, true);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
	Logger::SetupLogger();

	std::string Path = "C:\\";

	switch (ul_reason_for_call)
	{
	case DLL_PROCESS_ATTACH:
		Path = Config::Get_WorkingDirectory(); //We Don't Want Content To Be Loaded From The Wrong Directory, This Will Be Where *ALL* Stuff Goes.
		if (Path == "")
		{
			char Buffer[256];
			MessageBoxA(NULL, "Failed To Load/Parse Config.ini, Please Make Sure It Exists/ Is Setup And In The Same Directory As The Game!", "Yu-Gi-Oh-Effects", MB_ICONERROR);
		}
		Logger::WriteLog(std::format("Running from: {}", Path), MODULE_NAME, 0);
		DetourTransactionBegin();
		DetourUpdateThread(GetCurrentThread());

		Redirect_TwoOrMorePolymerization();

		DetourTransactionCommit();

		break;;
	case DLL_THREAD_ATTACH:
	case DLL_THREAD_DETACH:
	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}