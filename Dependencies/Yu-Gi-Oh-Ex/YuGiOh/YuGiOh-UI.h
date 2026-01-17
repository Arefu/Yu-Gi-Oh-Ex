#include <string>

namespace YGO
{
    namespace UI
    {
        enum Animations {
            DUEL = 0x1,
            YOU_WIN = 0x2,
            YOU_LOOSE = 0x2,
            YOU_DRAW = 0x2,
        };

        inline auto Draw_DuelAnimationFromId = reinterpret_cast<void(__fastcall*)(Animations Animation, int OptionOne, int a3, int a4)>(0x1407EB7E0);

        inline int& g_iActiveDuelAnimation = *reinterpret_cast<int*>(0x1427D0C08);
        inline int& g_iCurrentDuelAnimation = *reinterpret_cast<int*>(0x1427D0C18);
        inline int& g_iPreviousDuelAnimation = *reinterpret_cast<int*>(0x1427D0C1C);

        const char* g_sPendulumEffectE = reinterpret_cast<const char*>(0x140A52068);
    }
}
