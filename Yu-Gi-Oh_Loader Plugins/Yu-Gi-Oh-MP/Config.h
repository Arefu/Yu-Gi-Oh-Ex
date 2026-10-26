#pragma once

#include <string>

// [Yu-Gi-Oh-MP] in Config.ini, next to the exe:
//   ServerUrl=https://mp.example.com     the tournament/leaderboard service this plugin talks to (docs/MultiplayerService.md)
//   ClaimCode=XXXX-XXXX-XXXX-XXXX        sign in with a code the server's host minted (Epic/GOG copies, or no Steam); empty = Steam
//   DisplayName=Yugi                     the name a claim code's account gets the first time it is used
static class Config
{
public:
    static std::string Get_WorkingDirectory();   // this plugin's own folder under PluginsPath (cached token, etc.)
    static std::wstring Get_ContentFolder();     // what the plugin ships: <folder of this DLL>\Yu-Gi-Oh-MP (pages\, banlists\; source Content\)
    static std::string Get_ServerUrl();          // empty if Config.ini or the key is missing - callers must handle that (no MP without it)
    static std::string Get_ClaimCode();
    static std::string Get_DisplayName();
};
