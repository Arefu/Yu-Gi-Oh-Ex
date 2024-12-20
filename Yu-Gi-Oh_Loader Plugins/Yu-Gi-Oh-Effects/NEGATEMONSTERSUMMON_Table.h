#pragma once

#include <vector>

static class NEGATEMONSTERSUMMON_List
{
private:
	static const short Length = 0x36; //See 0x140131D5C.

public:
	static const long long Base = 0x140B0FDE0;
	static const long long End = Base + Length;

	static std::vector<short> List;
};