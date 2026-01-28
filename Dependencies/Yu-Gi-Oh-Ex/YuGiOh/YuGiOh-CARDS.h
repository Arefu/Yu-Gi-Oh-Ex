#include <Windows.h>

namespace YGO
{
    namespace CARDS
    {
        struct CARD_PROPS
        {
            __int16 KonamiID;
            __int16 field_2;
            int ATK;
            int DEF;
            int Kind;
            int Type;
            int Attribute;
            int Level;
            int Icon;
            int Field_10;
            int Field_12;
            int LimitedStatus;
            int Field_13;
        };

        inline auto Get_CardNameFromKonamiId = reinterpret_cast<LPCTSTR(__fastcall*)(short KonamiID)>(0x14076D0F0);
        inline auto Get_CardDescriptionFromKonamiId = reinterpret_cast<LPCTSTR(__fastcall*)(short KonamiID)>(0x14076D070);
        inline auto Get_EffectiveAttackFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA6F0);
        inline auto Get_RawAttackFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA6B0);
        inline auto Get_CardAttributeFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA730);
        inline auto Get_EffectiveDefenseFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA7C0);
        inline auto Get_RawDefenseFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA760);
        inline auto Get_SpellTrapCardProperty = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA860);
        inline auto Get_CardLimitedStatusFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA910);
        inline auto Get_CardLevelFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CAA60);
        inline auto Get_CardTypeFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA890);
        inline auto Get_CardMonsterTypeFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CAA90);
        inline auto Get_CardSameFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14076D610);
        inline auto Get_CurrentCardId = reinterpret_cast<short(__fastcall*)(short KonamiID)>(0x14076D630);
        inline auto Get_InternalIdFromKonamiId = reinterpret_cast<short(__fastcall*)(short KonamiID)>(0x14076E000);
        inline auto Get_CardPendulumScaleFromKonamiId = reinterpret_cast<short(__fastcall*)(short KonamiID)>(0x1407CAA30);
        inline auto Get_CardPropsFromInternalId = reinterpret_cast<CARD_PROPS * (__fastcall*)(short InternalID)>(0x1407CAB30);
        inline auto Get_CardPropsFromKonamiId = reinterpret_cast<CARD_PROPS * (__fastcall*)(short KonamiID)>(0x1407CAB00);
        inline auto Get_CardProp = reinterpret_cast<short(__fastcall*)(short KonamiID)>(0x14076D5F0);

        inline auto Get_CardTypeFromFullCardPropsByKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14081A650);
        inline auto Get_SpellTrapCardPropertyFromFullCardProps = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14081A630);
        inline auto Get_RawDefFromFullCardProps = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14081A5D0);
        inline auto Get_EffectiveDefFromFullCardProps = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14081A5B0);
        inline auto Get_CardLimitedStatusFromKonamiIdProps = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14076DFD0);
        inline auto Get_CardAttributeFromFullCardPropsByKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14081A570);

        inline auto Get_Something = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14081A800);

        inline auto Is_CardKonamiIdLinkedToInternalId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14076D5F0);
        inline auto Is_ValidCardId = reinterpret_cast<bool(__fastcall*)(short KonamiID)>(0x1407CA820);
        inline auto Is_TokenCard = reinterpret_cast<bool(__fastcall*)(short KonamiID)>(0x140743010);
        inline auto Is_SpellCard = reinterpret_cast<bool(__fastcall*)(short KonamiID)>(0x140742E30);
        inline auto Is_FieldSpell = reinterpret_cast<bool(__fastcall*)(short KonamiID)>(0x140742E70);
        inline auto Is_TrapCard = reinterpret_cast<bool(__fastcall*)(short KonamiID)>(0x140742E50);

        inline auto Reset_CardNames = reinterpret_cast<void(__fastcall*)()>(0x14081A7D0);
    }
}
