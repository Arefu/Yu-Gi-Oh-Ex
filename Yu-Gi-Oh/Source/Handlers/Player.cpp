#include "Handlers/Player.h"
#include "Types/Cards.h"

Player::Player(QWORD BaseAddr, BOOL Debug)
{
	this->_BaseAddr = BaseAddr;
	this->_Debug = Debug;
}

INT Player::Get_NumberOfCardsInHand()
{
	if (this->_Debug)
		cout << "Get_NumberOfCardsInHand() << " << *(INT*)(this->_BaseAddr + 0xC) << endl;

	return *(INT*)(this->_BaseAddr + 0xC);
}

INT Player::Get_NumberOfCardsInDeck()
{
	if (this->_Debug)
		cout << "Get_NumberOfCardsInDeck() << " << *(INT*)(this->_BaseAddr + 0x10) << endl;

	return *(INT*)(this->_BaseAddr + 0x10);
}

INT Player::Get_NumberOfCardsInGraveyard()
{
	if (this->_Debug)
		cout << "Get_NumberOfCardsInGraveyard() << " << *(INT*)(this->_BaseAddr + 0x14) << endl;

	return *(INT*)(this->_BaseAddr + 0x14);
}

INT Player::Get_NumberOfCardsInExtraDeck()
{
	if (this->_Debug)
		cout << "Get_NumberOfCardsInExtraDeck() << " << *(INT*)(this->_BaseAddr + 0x18) << endl;

	return *(INT*)(this->_BaseAddr + 0x18);
}

INT Player::Get_NumberOfCardsInDiscardPile()
{
	if (this->_Debug)
		cout << "Get_NumberOfCardsInDiscardPile() << " << *(INT*)(this->_BaseAddr + 0x1C) << endl;

	return *(INT*)(this->_BaseAddr + 0x1C);
}

BOOL Player::Is_FieldSpellActive()
{
	if (this->_Debug)
		cout << "Is_FieldSpellActive() << " << *(BYTE*)(this->_BaseAddr + 0x16C) << endl;

	if (*(BYTE*)(this->_BaseAddr + 0x16C) == 0x0)
		return FALSE;
	else
		return TRUE;
}

Card Player::Get_FieldSpell()
{
	Card Card{};
	auto Offset = this->_BaseAddr + 0x16C;

	Card.Id = *(SHORT*)Offset;
	Card.Picture = *(SHORT*)(Offset + 0x2);
	Card.Unk = *(SHORT*)(Offset + 0x4);
	Card.Position = static_cast<Position>(*(SHORT*)(Offset + 0x6));

	return Card;
}

vector<Simple_Card> Player::Get_CardsInHand()
{
	auto i_NumberOfCardsInHand = this->Get_NumberOfCardsInHand();
	auto HandStart = this->_BaseAddr + 0x19C;
	auto HandEnd = HandStart + (i_NumberOfCardsInHand * 4);
	vector<Simple_Card> Cards;

	if (this->_Debug)
	{
		cout << "i_NumberOfCardsInHand << " << hex << i_NumberOfCardsInHand << endl;
		cout << "HandStart << 0x" << hex << HandStart << endl;
		cout << "HandEnd << 0x" << hex << HandEnd << endl;
	}

	for (auto Offset = HandStart; Offset < HandEnd; Offset += 0x4)
	{
		Simple_Card Card{};

		INT Value = *(INT*)Offset;

		Card.Id = LOWORD(Value);
		Card.Picture = HIWORD(Value);

		if (this->_Debug)
		{
			cout << "Card.Id << 0x" << hex << Card.Id << " Card.Picture << 0x" << hex << Card.Picture << endl;
		}
	}

	return Cards;
}

vector<Simple_Card> Player::Get_CardsInDeck()
{
	auto i_CardsInDeck = this->Get_NumberOfCardsInDeck();
	auto DeckStart = this->_BaseAddr + 0x37C;
	auto DeckEnd = DeckStart + (i_CardsInDeck * 4);
	vector<Simple_Card> Cards;

	if (this->_Debug)
	{
		cout << "i_CardsInDeck << 0x" << hex << i_CardsInDeck << endl;
		cout << "DeckStart << 0x" << hex << DeckStart << endl;
		cout << "DeckEnd << 0x" << hex << DeckEnd << endl;
	}

	for (auto Offset = DeckStart; Offset < DeckEnd; Offset += 0x4)
	{
		Simple_Card Card{};
		INT Value = *(INT*)Offset;

		Card.Id = LOWORD(Value);
		Card.Picture = HIWORD(Value);

		if (this->_Debug)
		{
			cout << "Card.Id << 0x" << hex << Card.Id << " Card.Picture << 0x" << hex << Card.Picture << endl;
		}

		Cards.push_back(Card);
	}

	return Cards;
}

vector<Simple_Card> Player::Get_CardsInExtraDeck()
{
	auto i_NumberOfCardsInExtraDeck = this->Get_NumberOfCardsInExtraDeck();
	auto ExtraDeckStart = this->_BaseAddr + 0x55C;
	auto ExtraDeckEnd = ExtraDeckStart + (i_NumberOfCardsInExtraDeck * 4);
	vector<Simple_Card> Cards;

	if (this->_Debug)
	{
		cout << "i_NumberOfCardsInExtraDeck << 0x" << hex << i_NumberOfCardsInExtraDeck << endl;
		cout << "GraveyardStart << 0x" << hex << ExtraDeckStart << endl;
		cout << "GraveyardEnd << 0x" << hex << ExtraDeckEnd << endl;
	}

	for (auto Offset = ExtraDeckStart; Offset < ExtraDeckEnd; Offset += 0x4)
	{
		Simple_Card Card{};
		INT Value = *(INT*)Offset;

		Card.Id = LOWORD(Value);
		Card.Picture = HIWORD(Value);

		if (this->_Debug)
		{
			cout << "Card.Id << 0x" << hex << Card.Id << " Card.Picture << 0x" << hex << Card.Picture << endl;
		}

		Cards.push_back(Card);
	}

	return Cards;
}

vector<Simple_Card> Player::Get_CardsInGraveyard()
{
	auto i_NumberOfCardsInGraveyard = this->Get_NumberOfCardsInGraveyard();
	auto GraveyardStart = this->_BaseAddr + 0x7B4;
	auto GraveyardEnd = GraveyardStart + (i_NumberOfCardsInGraveyard * 4);
	vector<Simple_Card> Cards;

	if (this->_Debug)
	{
		cout << "i_NumberOfCardsInGraveyard << 0x" << hex << i_NumberOfCardsInGraveyard << endl;
		cout << "GraveyardStart << 0x" << hex << GraveyardStart << endl;
		cout << "GraveyardEnd << 0x" << hex << GraveyardEnd << endl;
	}

	for (auto Offset = GraveyardStart; Offset < GraveyardEnd; Offset += 0x4)
	{
		Simple_Card Card{};
		INT Value = *(INT*)Offset;

		Card.Id = LOWORD(Value);
		Card.Picture = HIWORD(Value);

		if (this->_Debug)
		{
			cout << "Card.Id << 0x" << hex << Card.Id << " Card.Picture << 0x" << hex << Card.Picture << endl;
		}

		Cards.push_back(Card);
	}

	return Cards;
}

vector<Simple_Card> Player::Get_CardsInDiscardPile()
{
	//
	auto i_NumberOfCardsInDiscardPile = this->Get_NumberOfCardsInDiscardPile();
	auto DiscardStart = this->_BaseAddr + 0xA0C;
	auto GraveyardEnd = DiscardStart + (i_NumberOfCardsInDiscardPile * 4);
	vector<Simple_Card> Cards;

	if (this->_Debug)
	{
		cout << "i_NumberOfCardsInDiscardPile << 0x" << hex << i_NumberOfCardsInDiscardPile << endl;
		cout << "DiscardStart << 0x" << hex << DiscardStart << endl;
		cout << "GraveyardEnd << 0x" << hex << DiscardStart << endl;
	}

	for (auto Offset = DiscardStart; Offset < GraveyardEnd; Offset += 0x4)
	{
		Simple_Card Card{};
		INT Value = *(INT*)Offset;

		Card.Id = LOWORD(Value);
		Card.Picture = HIWORD(Value);

		if (this->_Debug)
		{
			cout << "Card.Id << 0x" << hex << Card.Id << " Card.Picture << 0x" << hex << Card.Picture << endl;
		}

		Cards.push_back(Card);
	}

	return Cards;
}

vector<Card> Player::Get_CardsInMonsterZone()
{
	vector<Card> Cards;

	for (auto Offset = this->_BaseAddr + 0x4C; Offset <= this->_BaseAddr + 0xAC; Offset += 0x18)
	{
		Card Card{};

		Card.Id = *(SHORT*)Offset;
		Card.Picture = *(SHORT*)(Offset + 0x2);
		Card.Unk = *(SHORT*)(Offset + 0x4);

		Card.Position = static_cast<Cards::Position>(*(SHORT*)(Offset + 0x6));

		Cards.push_back(Card);
	}

	return Cards;
}

vector<Card> Player::Get_CardsInMagicZone()
{
	vector<Card> Cards;

	for (auto Offset = this->_BaseAddr + 0xF4; Offset <= this->_BaseAddr + 0x154; Offset += 0x18)
	{
		Card Card{};

		Card.Id = *(SHORT*)Offset;
		Card.Picture = *(SHORT*)(Offset + 0x2);
		Card.Unk = *(SHORT*)(Offset + 0x4);

		Card.Position = static_cast<Cards::Position>(*(SHORT*)(Offset + 0x6));

		Cards.push_back(Card);
	}
	return Cards;
}