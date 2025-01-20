#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#include <vector>

#include "Cards.h"

using std::vector;

#define PLAYER_ONE 0x143497C40
#define PLAYER_TWO 0x1434989D4


namespace YuGiOhEx
{
	static bool& g_bIsQuitReady = *(bool*)0x14332A391;
	static bool& g_bOnPageFirst = *(bool*)0x140C91C48;
	static bool& g_bUseJpLogo = *(bool*)0x14332A348;
	static HWND& g_hWnd = *(HWND*)0x140D31E60;
	static unsigned _int64 DeductMoneyFromStoreTransaction = 0x1407F93E0;
	static __int64 UnkFuncForLoading = 0x14076D840; //Rename this at some point
	static __int64 AutoPauseOnLostFocus = 0x14083C9F0;
	static __int64 UseJPLogo = 0x1408734C0;

	static __int64 JankenAndPlayerSelection = 0x1407AFD80;
}


class Player
{
private:
	__int64  _Player;

public:

	Player(__int64  Player);
	
	INT Get_NumberOfCardsInHand();
	SHORT Get_CardInHand(INT Slot);
	vector<SHORT> Get_CardsInHand();

	INT Get_NumberOfCardsInDeck();
	SHORT Get_CardInDeck(INT Slot);
	vector<SHORT> Get_CardsInDeck();

	INT Get_NumberOfCardsInGraveYard();
	SHORT Get_CardInGraveYard(INT Slot);
	vector<SHORT> Get_CardsInGraveYard();

	INT Get_NumberOfCardsInExtraPile();
	SHORT Get_CardInExtraPile(INT Slot);
	vector<SHORT> Get_CardsInExtraPile();

	INT Get_NumberOfDiscardPile();
	SHORT Get_CardInDiscardPile(INT Slot);
	vector<SHORT> Get_CardsInDiscardPile();


	SHORT Get_MonsterInSlot(INT Slot);
};

class YuGiOh
{
private:

public:
	static BYTE Get_IsDuelTutorial();


	static INT Get_SelectedSlotOnDuelMat();
};