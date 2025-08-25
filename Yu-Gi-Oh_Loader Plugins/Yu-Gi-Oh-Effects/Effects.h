#pragma once
#include <map>
#include <Windows.h>
#include <fstream>
#include <iostream>
#include <vector>

#include <json.hpp>

#include "Config.h"
#include "Effects.h"

class EffectTable_SpellAndTrap
{
public:
	struct Table {
		uintptr_t KonamiID;
		void* FunctionOne;
		void* FunctionTwo;
		void* FunctionThree;
		void* FunctionFour;
		void* FunctionFive;

		Table(uintptr_t id = 0, void* f1 = nullptr, void* f2 = nullptr, void* f3 = nullptr, void* f4 = nullptr, void* f5 = nullptr)
			: KonamiID(id),
			FunctionOne(f1),
			FunctionTwo(f2),
			FunctionThree(f3),
			FunctionFour(f4),
			FunctionFive(f5)
		{
		}
	};
	static std::vector<Table> EffectTable;

	static void SetupInternalEffectTable(uintptr_t Address);
	static void InsertRedirects(uintptr_t Address);
	static void AdjustTable();
	static void CopyTable(uintptr_t Address);
};
