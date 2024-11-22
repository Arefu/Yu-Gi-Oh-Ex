#include "Yu-Gi-Oh-Ex.h"

#include <string>

Player::Player(__int64   Player)
{
	_Player = Player;
}

__int64 Player::Get_Player()
{
	return _Player;
}

INT Player::Get_NumberOfCardsInHand()
{
	return *(INT*)(_Player + 0xC);
}

INT Player::Get_NumberOfCardsInDeck()
{
	return *(INT*)(_Player + 0x10);
}

INT Player::Get_NumberOfCardsInGraveYard()
{
	return *(INT*)(_Player + 0x14);
}

INT Player::Get_NumberOfDiscardPile()
{
	return *(INT*)(_Player + 0x18);
}

SHORT Player::Get_MonsterInSlot(int Slot)
{
	if ((Slot < 0) || (Slot > 5))
	{
		SetLastError(ERROR_INDEX_OUT_OF_BOUNDS);
		return 0;
	}
	//Monsters Start at +0x4C

	return *(SHORT*)(_Player + 0x4C + (Slot * 0x4));
}
