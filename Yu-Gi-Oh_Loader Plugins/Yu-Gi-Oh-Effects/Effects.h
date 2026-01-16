#pragma once
#include <map>
#include <Windows.h>
#include <fstream>
#include <iostream>
#include <vector>

#include <json.hpp>

#include "Config.h"
#include "Effects.h"

class Effects_FunctionTable
{
public:
    struct Table_01 {
        short KonamiID;
        short UNK01;
        short UNK02;
        short UNK03;

        void* FunctionOne;
        void* FunctionTwo;
        void* FunctionThree;
        void* FunctionFour;
        void* FunctionFive;

        Table_01(uintptr_t id = 0, void* f1 = nullptr, void* f2 = nullptr, void* f3 = nullptr, void* f4 = nullptr, void* f5 = nullptr)
            : KonamiID(id), FunctionOne(f1), FunctionTwo(f2), FunctionThree(f3), FunctionFour(f4), FunctionFive(f5) {
        }
    };
    struct Table_02 {
        uintptr_t KonamiID;

        void* FunctionOne;
        void* FunctionTwo;
        void* FunctionThree;
        void* FunctionFour;
        void* FunctionFive;
    };

    struct Table_05 {
        uintptr_t KonamiID;
        void* FunctionOne;
        void* FunctionTwo;
        void* FunctionThree;
        void* FunctionFour;
        void* FunctionFive;

        Table_05(uintptr_t id = 0, void* f1 = nullptr, void* f2 = nullptr, void* f3 = nullptr, void* f4 = nullptr, void* f5 = nullptr)
            : KonamiID(id), FunctionOne(f1), FunctionTwo(f2), FunctionThree(f3), FunctionFour(f4), FunctionFive(f5) {
        }
    };

    //0x140B38290
    static std::vector<Table_01> EffectTable_1;

    //0x0140B5CC20
    static std::vector<Table_02> EffectCards_FunctionTable;

    static std::vector<Table_05> EffectTable_5;
    /// <summary>
    /// Entry Point - Redirects Vectors
    ///
    static void Setup();

    static void DeleteItemFromList(short KonamiID);

    static void InsertRedirects();
    static void AdjustTable();
    static void CopyTable(uintptr_t Address);
};
