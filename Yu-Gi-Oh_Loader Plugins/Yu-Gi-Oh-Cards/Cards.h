#pragma once

#include <Windows.h>
#include <cstdint>
#include <vector>

#define KONAMI_CARD_ID_LOCATION 0x140D50510
#define INTERNAL_CARD_ID_LOCATION 0x140D55480

#include <vector> // Ensure this include is present for std::vector

class Cards
{
public:
    struct MEMORY_CARD_PROP
    {
        int CARD_ID;
        int ATK;
        int DEF;
        int EffectType;
        int Type;
        int Attribute;
        int Level;
        int Property;
        int LimitedStatus;
        int Field_10;
        int Field_12;
        int Field_13;
    };

    static unsigned long long orig_getInternalCardID;
    static unsigned long long orig_getKonamiCardID;

    static std::vector<unsigned __int16> CardIDs;
    static std::vector<unsigned __int16> InternalIDs;

    static __int64 __fastcall Get_InternalID(__int16 a1);
    static __int64 __fastcall Get_KonamiID(__int16 a1);

    static bool isHooked;
    static bool hasRan;

private:
};