#pragma once
#include <vector>

struct Trace
{
    std::string _Name; //E.G. YGO::DuelSetup::Get_IsRoundBasedDuel
    void* _Address; //E.G. 0x1407F1E80
    void* _Detour; //E.G. _D[_Name]
};

static class Tracer
{
public:
	static std::vector<Trace> Traces;

	static int Get_TraceIndex(std::string _Name);
	static void* Get_TraceFunction(std::string _Name);
	static void* Get_TraceDetour(std::string _Name);
};


