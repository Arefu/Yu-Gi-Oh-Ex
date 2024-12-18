#include <iostream>

#include "Tracer.h"
#include "YGO_DuelSetup.h"


bool YGO::DuelSetup::_D_Get_IsRoundBasedDuel(__int64 a1)
{
	std::cout << "[Yu-Gi-Oh-Tracer]: YGO::DuelSetup::Get_IsRoundBasedDuel Called!" << std::endl;
	auto Function = Tracer::Get_TraceFunction("YGO::DuelSetup::Get_IsRoundBasedDuel");
	return ((bool(*)(__int64))Function)(a1);
}