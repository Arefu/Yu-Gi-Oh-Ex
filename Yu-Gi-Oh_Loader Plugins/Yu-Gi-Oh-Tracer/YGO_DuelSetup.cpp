#include <iostream>

#include "Tracer.h"
#include "YGO_DuelSetup.h"


bool YGO::DuelSetup::_D_MP_Get_IsRoundBasedDuel(__int64 a1)
{
	std::cout << "[Yu-Gi-Oh-Tracer]: YGO::DuelSetup::MP_Get_IsRoundBasedDuel Called!" << std::endl;
	auto Function = Tracer::Get_TraceFunction("YGO::DuelSetup::MP_Get_IsRoundBasedDuel");
	return ((bool(*)(__int64))Function)(a1);
}

__int64 YGO::DuelSetup::_D_DoJankenMiniGame(__int64 a1, float a2)
{
	std::cout << "[Yu-Gi-Oh-Tracer]: YGO::DuelSetup::DoJankenMiniGame Called!" << std::endl;
	std::cout << "[Yu-Gi-Oh-Tracer]: Value of a1 is " << a1 << std::endl;
	auto Function = Tracer::Get_TraceFunction("YGO::DuelSetup::Do_JankenMiniGame");
	return ((__int64(*)(__int64, float))Function)(a1, a2);
}