#include <WTypesbase.h>

#define PLAYER_ONE 0x143497C40
#define PLAYER_TWO = 0x1434989D4

namespace YuGiOhEx
{
	static bool& g_bIsQuitReady = *(bool*)0x14332A391;
}


class Player
{
private:
	__int64  _Player;

public:
	Player(__int64  Player);
	__int64 Get_Player();

	INT Get_NumberOfCardsInHand();
	INT Get_NumberOfCardsInDeck();
	INT Get_NumberOfCardsInGraveYard();
	INT Get_NumberOfDiscardPile();

	SHORT Get_MonsterInSlot(INT Slot);
};

