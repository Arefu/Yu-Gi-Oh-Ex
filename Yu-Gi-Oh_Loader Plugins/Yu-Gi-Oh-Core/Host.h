#pragma once
#include <Windows.h>

#include "Yu-Gi-Oh-Core.h"

// The plugin list and the host for the plugins in Plugins\YGO-Ex (see Yu-Gi-Oh-Core.h and Yu-Gi-Oh-Manifest.h).
namespace Host
{
    // The Core module, so the plugin folders are found next to it.
    void SetModule(HMODULE Core);

    void Refresh();
    int Count();
    bool Info(int Index, CorePluginInfo& Out);
    int SetEnabled(const char* Key, bool Enabled);
    int StartPlugins();
}
