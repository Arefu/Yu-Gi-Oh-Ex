namespace YGO
{
    namespace DUEL
    {
        inline auto Get_DuelStuck = reinterpret_cast<bool(*)()>(0x1407AB080);
        inline auto Set_DuelStuck = reinterpret_cast<void(*)()>(0x1407AB170);

        inline auto Get_IsDuelMultiplayer = reinterpret_cast<short(*)()>(0x1407691D0);
        inline auto Set_IsDuelMultiplayer = reinterpret_cast<void(*)(bool IsMultiplayer)>(0x140769720);

        inline auto Get_IsRoundBasedDuel = reinterpret_cast<short(*)()>(0x1407691F0);
        inline auto Set_IsRoundBasedDuel = reinterpret_cast<void(*)(bool IsRoundBasedDuel)>(0x140769830);

        inline auto Get_IsTutorialDuel = reinterpret_cast<short(*)()>(0x1407694F0);
        inline auto Set_IsTutorialDuel = reinterpret_cast<bool(*)(bool IsTutorialDuel)>(0x140769B00);

        inline auto Get_StartingLifePoints = reinterpret_cast<int(*)()>(0x140768E30);
        inline auto Set_StartingLifePoints = reinterpret_cast<void(*)(int LifePoints)>(0x140769710);

        inline auto Get_StartingPlayer = reinterpret_cast<int(*)()>(0x140768DE0);
        inline auto Set_StartingPlayer = reinterpret_cast<void(*)(int StartingPlayer)>(0x140769740);

        inline auto Get_TutorialDuelIndex = reinterpret_cast<int(*)()>(0x140769180);
        inline auto Set_TutorialDuelIndex = reinterpret_cast<int(*)(int TutorialIndex)>(0x140769B20);

        inline auto Get_DuelTimeLimit = reinterpret_cast<int(*)()>(0x140768E10);
        inline auto Set_DuelTimeLimit = reinterpret_cast<void(*)(int DuelTimeLimitInMilliseconds)>(0x140769750);

        inline auto Get_IsTagDuel = reinterpret_cast<bool(*)()>(0x1407694D0);
        inline auto Set_IsTagDuel = reinterpret_cast<void(*)(bool IsTagDuel)>(0x140769AC0);

        inline auto Get_DefaultStartingLifePoints = reinterpret_cast<int(*)(int Index)>(0x1407F1E40);

        inline auto Get_NumberOfPlayers = reinterpret_cast<int(*)()>(0x140768DF0);

        inline auto Is_DuelTimerIncriment = reinterpret_cast<bool(*)(int Increment)>(0x1407694C0);

        inline bool& bIsTagDuel = *reinterpret_cast<bool*>(0x140C8D35D);
        inline bool& bIsTutorialDuel = *reinterpret_cast<bool*>(0x140C8D1EA);
        inline bool& bIsDuelMultiplayer = *reinterpret_cast<bool*>(0x140C8D1E6);
        inline bool& bIsRoundBasedDuel = *reinterpret_cast<bool*>(0x140C8D35C);
        inline bool& bSetDuelStuck = *reinterpret_cast<bool*>(0x140D4FCFB);

        inline int& iTutorialDuelIndex = *reinterpret_cast<int*>(0x140C8D1EC);
        inline int& iDuelTimeLimit = *reinterpret_cast<int*>(0x140C8D368);
        inline int& iDuelTimerIncrement = *reinterpret_cast<int*>(0x140C8D364);
        inline int& iSTartingLifePoints = *reinterpret_cast<int*>(0x140C8D370);
    }
}
