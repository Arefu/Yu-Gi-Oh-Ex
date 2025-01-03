#pragma once
#include <map>



static class XYZSummonRequirements {
public:
	struct Requirements {
		short _Card;
		short _Requirement;
		short _Count;
	};


	static const uintptr_t Base = 0x140ACF2E0;
	static const long Length = 220;

	static std::map<Requirements, uintptr_t> Table;


	/// <summary>
	/// Sets Up The In-Memory Table Of Card Requirements.
	/// 
	static void Setup();

	/// <summary>
	/// Updates In-Memory Value Of Card Requirement, Does **NO** Checks on validity of the card.
	/// </summary>
	/// <param name="Index">Check Length.</param>
	/// <param name="NewCard">Yu-Gi-Oh Card Database ID #</param>
	/// <returns>The Previous Value</returns>
	static short ChangeCardAtIndex(short Index, short NewCard);

	static bool ProcessChanges();
};