#include <Windows.h>

#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Log.h"
#include "Packs.h"
#include "StorePages.h"

// Yu-Gi-Oh-BetterCardShop: the Card Store (StorePages.cpp), made of Yu-Gi-Oh-RIX pages in the game's own UI, and the booster packs from
// Yu-Gi-Oh-Ex/packs.json (Packs.cpp: cards added to game packs, and new packs). Yu-Gi-Oh-Core starts it and calls ProcessDetours once;
// there is no ImGui window any more, so Yu-Gi-Oh-GUI is not needed.
namespace
{
    constexpr const char* MODULE_NAME = "Yu-Gi-Oh-BetterCardShop";
}

extern "C" __declspec(dllexport) void ProcessDetours()
{
    Packs::Install();   // works without RIX
    if (!StorePages::Install())
        YGO::Log("Yu-Gi-Oh-RIX is not loaded, so the Card Shop button stays the game's", MODULE_NAME, 1);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    return TRUE;
}
