#include <Windows.h>

namespace YGO
{
    namespace CARDS
    {
        inline auto Get_CardNameFromKonamiId = reinterpret_cast<LPCTSTR(__fastcall*)(short KonamiID)>(0x14076D0F0);
        inline auto Get_CardDescriptionFromKonamiId = reinterpret_cast<LPCTSTR(__fastcall*)(short KonamiID)>(0x14076D070);
        inline auto Get_EffectiveAttackFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA6F0);
        inline auto Get_RawAttackFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA6B0);
        inline auto Get_EffectiveDefenseFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA7C0);
        inline auto Get_CardLimitedStatusFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CA910);
        inline auto Get_CardLevelFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CAA60);
        inline auto Get_CardTypeFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x1407CAA90);
        inline auto Get_CardSameFromKonamiId = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x14076D610);
        inline auto Get_CurrentCardId = reinterpret_cast<short(__fastcall*)()>(0x14076D630);

        inline auto Get_InternalIdFromKonamiId = reinterpret_cast<short(__fastcall*)(short KonamiID)>(0x14076E000);

        inline auto Is_ValidCardId = reinterpret_cast<bool(__fastcall*)(short KonamiID)>(0x1407CA820);

        inline auto Get_XX = reinterpret_cast<int(__fastcall*)(short KonamiID)>(0x140753BA0);
    }
}
