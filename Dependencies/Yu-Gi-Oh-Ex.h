constexpr auto PLAYER_ONE = 0x143497C40;
constexpr auto PLAYER_TWO = 0x1434989D4;

namespace YuGiOhEx
{
	static bool& g_bIsQuitReady = *(bool*)0x14332A391;
}

namespace YGO
{
	namespace Duel
	{
		namespace Player
		{
			
			class Player
			{
			private:
				__int64 BaseAddr;
			public:
				Player(__int64 BaseAddr);

				INT Get_NumberOfCardsInHand();
			};
			
			Player::Player(__int64 BaseAddr)
			{
				this->BaseAddr = BaseAddr;
			}

			INT Player::Get_NumberOfCardsInHand()
			{
				return *(INT*)(BaseAddr + 0xC);

			}
		}
	}
}