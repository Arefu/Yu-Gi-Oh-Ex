#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <vector>
#include <string>

#include "Memory.h"
#include "Table_140B15B30.h"
#include "CardsThatMakeYouDraw.h"

#include "Effects.h"
#include "Logger.h"
#include "Config.h"
#include "File.h"

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    Logger::SetupLogger();

    std::string Path = "C:\\";

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        Path = Config::Get_WorkingDirectory();
        if (Path == "")
        {
            char Buffer[256];
            MessageBoxA(NULL, "Failed To Load/Parse Config.ini, Please Make Sure It Exists/ Is Setup And In The Same Directory As The Game!", "Yu-Gi-Oh-Effects", MB_ICONERROR);
        }
        Logger::WriteLog(std::format("Running from: {}", Path), MODULE_NAME, 0);

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());

        Effects_FunctionTable::Setup();

        Table_140B15B30::Setup();
        Table_140B15B30::AlterTable(FileIO::Read_FromEffectFile(std::format("{}{}", Path, "\\140B15B30\\140B15B30.json")));
        Table_140B15B30::Patch();
        Effects_FunctionTable::AdjustTable();
        //CardsThatMakeYouDraw::Setup(std::format("{}{}", Path, "\\CardsThatMakeYouDraw\\CardsThatMakeYouDraw.json"));

        Effects_FunctionTable::InsertRedirects();
        DetourTransactionCommit();

        break;;
    }
    return TRUE;
}
