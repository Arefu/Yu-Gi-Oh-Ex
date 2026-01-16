#pragma once

#include <Windows.h>
#include <cstdint>
#include <vector>

#define KONAMI_CARD_ID_LOCATION 0x140D50510
#define INTERNAL_CARD_ID_LOCATION 0x140D55480
#define IN_MEMORY_CARD_PROP_LOCATION 0x1427D0C30

class Cards
{
public:
    enum Attribute
    {
        SPECIAL = 0x0,
        DIVINE = 0x7,
        SPELL = 0x8,
        TRAP = 0x9,
    };
    enum Type
    {
        Unkown = 0x0,
        SeaSerpent = 0x5,
        Fish = 0x8,
        BeastWarrior = 0xC,
        WingedBeast = 0x10,
        Thunder = 0x13,
        Reptile = 0x14,
        Psychic = 0x15,
        Wyrm = 0x16,
        Cyberse = 0x17,
        DivineBeast = 0x18,
        CreatorGod = 0x19,
    };
    enum Status
    {
        Unlimited = 0x0,
        Limited = 0x1,
        SemiLimited = 0x2,
    };

    struct IN_MEMORY_CARD_PROP
    {
        int CARD_ID;
        int ATK;
        int DEF;
        int EffectTypes;
        Attribute Attribute;
        int Level;
        int Property;
        Type Type;
        int Field_10;
        int Field_12;
        Status LimitedStatus;
        int Field_13;
    };

    static unsigned long long orig_getInternalCardID;
    static unsigned long long orig_getKonamiCardID;
    static unsigned long long orig_getCardProps;

    static std::vector<unsigned __int16> CardIDs;
    static std::vector<unsigned __int16> InternalIDs;
    static std::vector<IN_MEMORY_CARD_PROP> CardProps;

    static Cards::IN_MEMORY_CARD_PROP* __fastcall Get_CardPropsByInternalID(__int16 a1);
    static __int64 __fastcall Get_InternalID(__int16 a1);
    static __int64 __fastcall Get_KonamiID(__int16 a1);
    __int64 __fastcall Get_ImageForCard(void* a1, __int16 a2);

private:
};
