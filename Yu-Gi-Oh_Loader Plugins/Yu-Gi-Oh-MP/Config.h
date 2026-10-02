#pragma once

#include <string>

// [Yu-Gi-Oh-MP] in Config.ini, next to the exe:
//   ServerUrl=https://mp.example.com     the tournament/leaderboard service this plugin talks to (docs/MultiplayerService.md)
static class Config
{
public:
    static std::string Get_WorkingDirectory();   // this plugin's own folder under PluginsPath (cached token, etc.)
    static std::string Get_ServerUrl();          // empty if Config.ini or the key is missing - callers must handle that (no MP without it)
};
