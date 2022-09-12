#include <shlwapi.h>
#include <iostream>
#include <string>

#include "Config.h"

BOOL Config::Engine_Pause_When_Not_In_Focus;
SHORT Config::Duel_LifePoints;
SHORT Config::Duel_HandLimit;
INT32 Config::Engine_Rules;

BOOL Config::Load(std::string Path)
{
	if (PathFileExistsA(Path.c_str()) != TRUE)
		return FALSE;

	char ConfigFilePath[MAX_PATH];
	GetCurrentDirectory(MAX_PATH, ConfigFilePath);

	lstrcatA(ConfigFilePath, "\\");
	lstrcatA(ConfigFilePath, Path.c_str());

	//Engine Read
	Config::Engine_Rules = GetPrivateProfileInt("Engine", "Rules", 1, ConfigFilePath);
	Config::Engine_Pause_When_Not_In_Focus = GetPrivateProfileInt("Engine", "Pause_When_Not_In_Focus", FALSE, ConfigFilePath);

	//Duel Read
	Config::Duel_LifePoints = GetPrivateProfileInt("Duel", "Life_Points", 0x1F40, ConfigFilePath);
	Config::Duel_HandLimit = GetPrivateProfileInt("Duel", "Hand_Limit", 6, ConfigFilePath);

	std::cout << "[P] Setting Engine_Rules: " << Config::Engine_Rules << std::endl;
	*(long long*)0x140C8D1C9 = Config::Engine_Rules;

	return TRUE;
}

SHORT Config::Get_LifePoints()
{
	return Config::Duel_LifePoints;
}

SHORT Config::Get_HandLimit()
{
	return Config::Duel_HandLimit;
}