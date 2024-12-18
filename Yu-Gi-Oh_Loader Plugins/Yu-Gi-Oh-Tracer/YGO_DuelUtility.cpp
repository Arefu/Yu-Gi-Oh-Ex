
#include <iostream>

#include "YGO_DuelUtility.h"
#include "Tracer.h"

__int64 YGO::DuelUtility::_D_Get_TutorialDuelIndex()
{
	std::cout << "[Yu-Gi-Oh-Tracer]: YGO::DuelUtility::Get_TutorialDuelIndex Called!" << std::endl;
	auto Function = Tracer::Get_TraceFunction("YGO::DuelUtility::Get_TutorialDuelIndex");
	
	//Call the original function
	return ((__int64(*)(__int64))Function)(0);

}