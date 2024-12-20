#pragma once

#include <map>

static class REMOVESPELLCOUNTER_Table
{
private:
	static const short Length = 0x24; //See 0x1401F72A9.

public:
	static const long long Base = 0x140B24D60;
	static const long long End = Base + Length;

	static std::map<short, short> Table;
};