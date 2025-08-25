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

std::vector<EffectTable_SpellAndTrap::Table> EffectTable_SpellAndTrap::EffectTable(2831);

void EffectTable_SpellAndTrap::SetupInternalEffectTable(uintptr_t Address)
{
	Logger::WriteLog("Setting Up Internal Effect Table for Spell & Trap", MODULE_NAME, 0);
	memcpy(EffectTable.data(), reinterpret_cast<void*>(Address), sizeof(EffectTable_SpellAndTrap::Table) * 2831);
	Memory::PatchZeros(reinterpret_cast<void*>(Address), sizeof(EffectTable_SpellAndTrap::Table) * 2831, true);
}

void EffectTable_SpellAndTrap::InsertRedirects(uintptr_t Address)
{
	Logger::WriteLog("Setting Up Internal Effect Table for Code Cave", MODULE_NAME, 0);
	Memory::InsertCALL(reinterpret_cast<void*>(0x1400DFC75), Address, 7, true);
	auto Position = Memory::EmplaceMOV(reinterpret_cast<void*>(Address), reinterpret_cast<uintptr_t>(EffectTable.data()), Memory::X64Register::RSI, true);
	Memory::EmplaceRET(reinterpret_cast<void*>(Position), true);

	Memory::InsertMOV(reinterpret_cast<void*>(0x1400DFC81), EffectTable.size(), Memory::X64Register::R8, true, 6);
}

void EffectTable_SpellAndTrap::AdjustTable()
{
	//sub_14016B430, offset sub_1401B0FF0, 0, 0, offset sub_14008CD30
	EffectTable.emplace_back(EffectTable_SpellAndTrap::Table{ 4317,reinterpret_cast<void*>(0x14015E570), 0,0, 0, 0 });

	std::sort(EffectTable.begin(), EffectTable.end(), [](const EffectTable_SpellAndTrap::Table& a, const EffectTable_SpellAndTrap::Table& b) {return a.KonamiID < b.KonamiID; });
}