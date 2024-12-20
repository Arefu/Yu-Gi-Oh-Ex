#pragma once

#include <map>

static class DRAW_Table
{
private:
	static const short Length = 0x1D5; //See 0x14015EAFA.

public:
	static const long long Base = 0x140B15110;
	static const long long End = Base + Length;

	static std::map<short, short> Table;
};