namespace YGO
{
    namespace DUEL
    {
        /// <summary>
        /// Returns the number of Players for the current duel type.
        /// </summary>
        /// <returns>2 or 4 Players</returns>
        inline auto Get_NumberOfPlayers = reinterpret_cast<int(*)()>(0x140768DF0);

        /// <summary>
        /// Returns if the current duel is a Tutorial Duel.
        /// </summary>
        /// <returns></returns>
        inline auto Get_IsTutorialDuel = reinterpret_cast<short(*)()>(0x1407694F0);

        /// <summary>
        /// Sets if the current duel is a Tutorial Duel.
        /// </summary>
        /// <returns></returns>
        inline auto Set_IsTutorialDuel = reinterpret_cast<bool(*)(bool)>(0x140769B00);

        /// <summary>
        /// Sets the Tutorial Duel Index.
        /// </summary>
        /// <returns></returns>
        inline auto Set_TutorialDuelIndex = reinterpret_cast<int(*)(int)>(0x140769B20);

        /// <summary>
        /// Address for whether the current duel is a Tag Duel.
        /// </summary>
        inline bool& bIsTagDuel = *reinterpret_cast<bool*>(0x140C8D35D);

        /// <summary>
        /// Addres for whether the current duel is a Tutorial Duel.
        /// </summary>
        inline bool& bIsTutorialDuel = *reinterpret_cast<bool*>(0x140C8D1EA);

        /// <summary>
        /// Address for the Tutorial Duel Index.
        /// </summary>
        inline int& iTutorialDuelIndex = *reinterpret_cast<int*>(0x140C8D1EC);
    }
}
