#pragma once
#include <json.hpp>
#include <string>
#include <map>

namespace Effects_Functions
{
	static __int64 o_GetNumberOfCardsToDraw = 0x14015EA90;
}

static class Effects
{
public:
	/// <summary>
	/// 	Will find the amount of cards to draw for the card ID.
	///		h = Hook function.
	/// </summary>
	/// <param name="Card">Card ID</param>
	/// <param name="Default">If true, will use the default amount of cards to draw for the card ID. If false, you *SHOULD* have a Manifest.json for FindDrawAmountForCard..</param>
	static void h_FindDrawAmountForCard(unsigned short*);

	/// <summary>
	/// Returns a std::map with a kvp of the Card ID and the number of cards to draw based on what is in the game.
	/// u = Utility function.
	/// </summary>
	/// <returns>The default in memory number of cards associated with each card ID for drawing.</returns>
	static std::map<short, short> u_Populate_DrawAmountsForCards();

	/// <summary>
	/// Returns a std::map with a kvp of the Card ID and the number of cards to draw.
	/// u = Utility function.
	/// </summary>
	/// <param name="Tag">The tag for the current thing in the Manifest.json</param>
	static std::map<std::string, short> u_Populate_FromManifest_ForKVP(std::string);

private:
	static nlohmann::json u_FindAndSelectManfestFile(std::string);

	static std::map<std::string, short> _DrawAmountsForCards;
};