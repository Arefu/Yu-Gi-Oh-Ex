#pragma once
#include <map>
#include <string>


static class CardID_WithNumberOfCardsToDraw {
public:
	struct Requirements {
		short _Card;
		short _Count;
	};

	static const uintptr_t Base = 0x140B15110;
	static const long Length = 470;

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

	/// <summary>
	/// Checks "Path" for Folders with Manifest.json Files, and Processes requested change relevant to the CardID_WithNumberOfCardsToDraw Table.
	/// </summary>
	/// <param name="Path">The Root Folder for "Effects", Typically should be in .\Plugins\Effects\.</param>
	/// <returns></returns>
	static bool ProcessChanges(std::string Path);
};