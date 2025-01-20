#include <string>

#include "Yu-Gi-Oh-Ex.h"


Player::Player(__int64   Player)
{
	_Player = Player;

}

INT Player::Get_NumberOfCardsInHand()
{
	return *(INT*)(_Player + 0xC);
}
SHORT Player::Get_CardInHand(INT Slot)
{
	if ((Slot < 0) || (Slot > 24))
	{
		SetLastError(ERROR_INDEX_OUT_OF_BOUNDS);
		return 0;
	}

	return *(SHORT*)(_Player + 0x19C + (Slot * 0x4));
}
vector<SHORT> Player::Get_CardsInHand()
{
	vector<SHORT> Cards;
	for (int i = 0; i < Get_NumberOfCardsInHand(); i++)
	{
		Cards.push_back(Get_CardInHand(i));
	}
	return Cards;
}

INT Player::Get_NumberOfCardsInDeck()
{
	return *(INT*)(_Player + 0x10);
}

SHORT Player::Get_CardInDeck(INT Slot)
{
	if ((Slot < 0) || (Slot > 120))
	{
		SetLastError(ERROR_INDEX_OUT_OF_BOUNDS);
		return 0;
	}

	return *(SHORT*)(_Player + 0x37C + (Slot * 0x4));
}

vector<SHORT> Player::Get_CardsInDeck()
{
	vector<SHORT> Cards;
	for (int i = 0; i < Get_NumberOfCardsInDeck(); i++)
	{
		Cards.push_back(Get_CardInDeck(i));
	}
	return Cards;
}

INT Player::Get_NumberOfCardsInGraveYard()
{
	return *(INT*)(_Player + 0x14);
}

SHORT Player::Get_CardInGraveYard(INT Slot)
{
	if ((Slot < 0) || (Slot > 150))
	{
		SetLastError(ERROR_INDEX_OUT_OF_BOUNDS);
		return 0;
	}
	
	return *(SHORT*)(_Player + 0x7B4 + (Slot * 0x4));
}

vector<SHORT> Player::Get_CardsInGraveYard()
{
	vector<SHORT> Cards;
	for (int i = 0; i < Get_NumberOfCardsInGraveYard(); i++)
	{
		Cards.push_back(Get_CardInGraveYard(i));
	}
	return Cards;
}

INT Player::Get_NumberOfCardsInExtraPile()
{
	return 0;
}

SHORT Player::Get_CardInExtraPile(INT Slot)
{
	return SHORT();
}

vector<SHORT> Player::Get_CardsInExtraPile()
{
	return vector<SHORT>();
}

INT Player::Get_NumberOfDiscardPile()
{
	return *(INT*)(_Player + 0x18);
}

SHORT Player::Get_CardInDiscardPile(INT Slot)
{
	return SHORT();
}

vector<SHORT> Player::Get_CardsInDiscardPile()
{
	return vector<SHORT>();
}

SHORT Player::Get_MonsterInSlot(int Slot)
{
	if ((Slot < 0) || (Slot > 5))
	{
		SetLastError(ERROR_INDEX_OUT_OF_BOUNDS);
		return 0;
	}

	return *(SHORT*)(_Player + 0x4C + (Slot * 0x8));
}


BYTE YuGiOh::Get_IsDuelTutorial()
{
	return *(BYTE*)0x140C8D1EA;
}

INT YuGiOh::Get_SelectedSlotOnDuelMat()
{
	return *(SHORT*)0x14278EE70;
}
