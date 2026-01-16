#include <string>
#include "Tracer.h"

int Tracer::Get_TraceIndex(std::string _Name)
{
    for (int i = 0; i < Traces.size(); i++)
    {
        if (Traces[i]._Name == _Name)
        {
            return i;
        }
    }
}

void* Tracer::Get_TraceFunction(std::string _Name)
{
    for (auto& _Trace : Traces)
    {
        if (_Trace._Name == _Name)
        {
            return _Trace._Address;
        }
    }
}

void* Tracer::Get_TraceDetour(std::string _Name)
{
    for (auto& _Trace : Traces)
    {
        if (_Trace._Name == _Name)
        {
            return _Trace._Detour;
        }
    }
}
