#pragma once

#include <Windows.h>
#include <iostream>
#include <vector>

#include "Types/Cards.h"

typedef unsigned __int64 QWORD;

using std::hex;
using std::cout;
using std::endl;
using std::vector;
using Cards::Card;
using Cards::Position;
using Cards::Simple_Card;

//This is incomplete, there are more vars in the state in memory that I haven't mapped.... :(
class Player
{
public:
	Player(QWORD BaseAddr, BOOL Debu = FALSE);

	BOOL Is_FieldSpellActive();

	INT Get_NumberOfCardsInHand();
	INT Get_NumberOfCardsInDeck();
	INT Get_NumberOfCardsInGraveyard();
	INT Get_NumberOfCardsInExtraDeck();
	INT Get_NumberOfCardsInDiscardPile();

	Card Get_FieldSpell();

	vector<Simple_Card> Get_CardsInHand();
	vector<Simple_Card> Get_CardsInDeck();
	vector<Simple_Card> Get_CardsInExtraDeck();
	vector<Simple_Card> Get_CardsInGraveyard();
	vector<Simple_Card> Get_CardsInDiscardPile();

	vector<Card> Get_CardsInMonsterZone();
	vector<Card> Get_CardsInMagicZone();

private:
	QWORD _BaseAddr;
	BOOL _Debug = FALSE;
};