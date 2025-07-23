#pragma once

#include <Windows.h>
#include <vector>

static class CardsThatMakeYouDraw
{
	public:

	struct CardsWithDrawCount
	{
		short Target_CardID;
		short NumberOfCardsDrawn;
	};

	static std::vector<CardsWithDrawCount> _CardsThatMakeYouDraw;
};