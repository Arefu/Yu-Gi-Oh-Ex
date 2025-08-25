#pragma once
#include <algorithm>
#include <vector>
#include <format>

#include "Memory.h"
#include "Logger.h"

/// <summary>
/// 0x140B15B30  - https://yugipedia.com/wiki/Effect_Damage
/// This Is the *TABLE*, not the Konami ID, Function Lookup.
/// Add to this to define what LP is expected to change by during normal triggering of the effect.
/// </summary>
class Table_140B15B30
{
private:
	static struct Table
	{
		short KonamiID;
		short Amount;
	};

	static bool Patched;
public:
	static std::vector<Table_140B15B30::Table> Items;
	static void Setup();
	static void Patch();
	static void List();

	static bool ChangeItemInList(short KonamiID, short Amount);
	static bool AddToList(short KonamiID, short Amount);
	static bool RemoveFromList(short KonamiID);

	static short GetItemFromList(short KonamiID);
};
