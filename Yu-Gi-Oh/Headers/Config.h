#pragma once

#include <Windows.h>
#include <string>

using std::string;

class Config
{
public:
	static BOOL Load(string Path);

	static SHORT Get_LifePoints();
	static SHORT Get_HandLimit();

private:
	static BOOL Engine_Pause_When_Not_In_Focus;

	static SHORT Duel_LifePoints;
	static SHORT Duel_HandLimit;

	static INT32 Engine_Rules;
};