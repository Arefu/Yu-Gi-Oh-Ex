#pragma once
#include <algorithm>
#include <string>
#include <vector>
#include <format>

#include "Effects.h"
#include "Memory.h"
#include "Logger.h"
#include "json.hpp"

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
    struct Operation {
        std::string OperationType;
        std::vector<Table> Cards;
    };
    struct OperationsData {
        std::vector<Operation> Operations;
    };

    static bool Patched;
public:
    static std::vector<Table_140B15B30::Table> Items;
    static void Setup();
    static void Patch();
    static void List();

    static void AlterTable(std::string JSON);

    static bool UpdateItemInList(short KonamiID, short Amount);
    static bool CreateItemInList(short KonamiID, short Amount);
    static bool DeleteItemFromList(short KonamiID);
    static short ReadItemFromList(short KonamiID);
};
