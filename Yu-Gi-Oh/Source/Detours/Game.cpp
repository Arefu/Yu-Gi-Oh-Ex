#include "Detours/Game.h"

QWORD Game::Patch_SetupDuel()
{
	*(SHORT*)0x140C8D370 = g_Config.Get_LifePoints();
	*(long long*)0x140C8D360 = 0;
	*(long long*)0x140C8D368 = 0;
	*(long long*)0x140C8D35C = 0;
	return 0;
}

QWORD Game::Patch_DefaultMaxNumberOfCards(unsigned int UNK)
{
	sub_14081A800* func_14081A800 = (sub_14081A800*)0x14081A800;
	sub_140014450* func_140014450 = (sub_140014450*)0x140014450;
	sub_140052BA0* func_140052BA0 = (sub_140052BA0*)0x140052BA0;

	__int64 v2 = 0, v3 = 0, v4 = 0, v5 = 0, v6 = 0, v7 = 0, v8 = 0;

	// Infinite Cards
	if (func_14081A800(CardIds::Infinite_Cards))
		v3 = func_140014450(-1, 0x1401u, 0, 18, -1, 0, 0, 2);
	else
		v3 = 0;

	v5 = func_140052BA0(UNK, 13, 5535);
	v6 = 0xFFFF;

	if (v3 <= 0)
		v6 = g_Config.Get_HandLimit();

	if (v5 > 0)
		v6 = 7;

	if (v5 <= 0)
		v5 = v4;

	// Enervating Mist
	if (func_14081A800(CardIds::Enervating_Mist))
		v7 = func_140014450(1 - UNK, 0x1800u, 1 - UNK, 18, -1, 0, 0, 2);
	else
		v7 = 0;

	if (v7 > v5)
		v6 = 5;

	if (v7 <= v5)
		v7 = v5;

	// Silent Wobby
	if (func_14081A800(CardIds::Silent_Wobby))
		v8 = func_140014450(UNK, 0x2B01u, UNK, 18, -1, 0, 0, 2);
	else
		v8 = 0;

	if (v8 > v7)
		v6 = 3;

	if (v8 <= v7)
		v8 = v7;

	// Finite Cards
	if (func_14081A800(CardIds::Finite_Cards))
		v2 = func_140014450(-1, 0x3035u, 0, 18, -1, 0, 0, 2);

	if (v2 > v8)
		return 3;

	return v6;
}

QWORD Game::Patch_IsGameOutOfFocus()
{
	return *(long long*)0x143328028;
}