#include <map>
#include <Windows.h>
#include <fstream>
#include <iostream>
#include <vector>

#include <json.hpp>

#include "Config.h"
#include "Memory.h"
#include "Effects.h"
#include "Logger.h"

std::vector<Effects_FunctionTable::Table_01> Effects_FunctionTable::EffectTable_1(3123);
std::vector <Effects_FunctionTable::Table_02> Effects_FunctionTable::EffectCards_FunctionTable(3489);
std::vector<Effects_FunctionTable::Table_05> Effects_FunctionTable::EffectTable_5(2831);

void Effects_FunctionTable::Setup()
{
    Logger::WriteLog("Setting Up Internal Effect Table #1", MODULE_NAME, 0);
    memcpy(EffectTable_1.data(), reinterpret_cast<void*>(0x140B38290), sizeof(Effects_FunctionTable::Table_01) * 3123);
    Memory::PatchZeros(reinterpret_cast<void*>(0x140B38290), sizeof(Effects_FunctionTable::Table_01) * 3123, true);

    Logger::WriteLog("Setting Up Internal Effect Table #2 (Effect Cards)", MODULE_NAME, 0);
    memcpy(EffectCards_FunctionTable.data(), reinterpret_cast<void*>(0x140B5CC20), sizeof(Effects_FunctionTable::Table_02) * 3489);
    Memory::PatchZeros(reinterpret_cast<void*>(0x140B5CC20), sizeof(Effects_FunctionTable::Table_02) * 3489, true);

    Logger::WriteLog("Setting Up Internal Effect Table #5", MODULE_NAME, 0);
    memcpy(EffectTable_5.data(), reinterpret_cast<void*>(0x140BA8730), sizeof(Effects_FunctionTable::Table_05) * 2831);
    Memory::PatchZeros(reinterpret_cast<void*>(0x140BA8730), sizeof(Effects_FunctionTable::Table_05) * 2831, true);
}

void Effects_FunctionTable::InsertRedirects()
{
    Logger::WriteLog("Setting Up Internal Effect Table #1 for Code Cave", MODULE_NAME, 0);
    Memory::InsertCALL(reinterpret_cast<void*>(0x1400DFC32), 0x140B38290, 7, true);
    auto Position = Memory::EmplaceMOV(reinterpret_cast<void*>(0x140B38290), reinterpret_cast<uintptr_t>(EffectTable_1.data()), Memory::X64Register::RSI, true);
    Memory::EmplaceRET(reinterpret_cast<void*>(Position), true);
    Memory::InsertMOV(reinterpret_cast<void*>(0x1400DFC81), EffectTable_5.size(), Memory::X64Register::R8, true, 6);

    Logger::WriteLog("Setting Up Internal Effect Table #2 (Effect Cards) for Code Cave", MODULE_NAME, 0);
    Memory::InsertCALL(reinterpret_cast<void*>(0x1400DFC66), 0x140B5CC20, 7, true);
    Position = Memory::EmplaceMOV(reinterpret_cast<void*>(0x140B5CC20), reinterpret_cast<uintptr_t>(EffectCards_FunctionTable.data()), Memory::X64Register::RSI, true);
    Memory::EmplaceRET(reinterpret_cast<void*>(Position), true);
    Memory::InsertMOV(reinterpret_cast<void*>(0x1400DFC6D), EffectCards_FunctionTable.size(), Memory::X64Register::R8, true, 6);

    Logger::WriteLog("Setting Up Internal Effect Table for Code Cave", MODULE_NAME, 0);
    Memory::InsertCALL(reinterpret_cast<void*>(0x1400DFC75), 0x140BA8730, 7, true);
    Position = Memory::EmplaceMOV(reinterpret_cast<void*>(0x140BA8730), reinterpret_cast<uintptr_t>(EffectTable_5.data()), Memory::X64Register::RSI, true);
    Memory::EmplaceRET(reinterpret_cast<void*>(Position), true);

    Memory::InsertMOV(reinterpret_cast<void*>(0x1400DFC48), EffectTable_5.size(), Memory::X64Register::R8, true, 6);
}

void Effects_FunctionTable::AdjustTable()
{
    //sub_14016B430, offset sub_1401B0FF0, 0, 0, offset sub_14008CD30

    Effects_FunctionTable::Table_02 Item;
    Item.KonamiID = 4008;
    Item.FunctionOne = reinterpret_cast<void*>(0x140156D60);

    EffectCards_FunctionTable.push_back(Item);
    std::sort(EffectCards_FunctionTable.begin(), EffectCards_FunctionTable.end(), [](const Effects_FunctionTable::Table_02& a, const Effects_FunctionTable::Table_02& b) {return a.KonamiID < b.KonamiID; });
}

void Effects_FunctionTable::DeleteItemFromList(short KonamiID)
{
    auto it = std::remove_if(EffectTable_5.begin(), EffectTable_5.end(), [KonamiID](const Table_05& item) {
        return item.KonamiID == KonamiID;
        });

    if (it != EffectTable_5.end())
    {
        EffectTable_5.erase(it, EffectTable_5.end());
        return;
    }
}
