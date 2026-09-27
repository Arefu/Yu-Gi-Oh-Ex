#pragma once
/*
    Yu-Gi-Oh-Core - the public interface of the always-on base plugin (Yu-Gi-Oh-Core.dll).

    The loader always injects it (first) and it can not be turned off. It holds what every other plugin can share and nothing that belongs to one
    feature:
      - the game's save redirect (savegame-ex.dat), so no plugin can ever write the Steam save;
      - the plugin list: which plugins exist, are on, and can load (see Yu-Gi-Oh-Manifest.h), and switching them on and off;
      - the host for the plugins in Plugins\YGO-Ex (SetContext / ProcessConfig / ProcessDetours / ProcessWindow / ProcessInput), so they work
        with or without Yu-Gi-Oh-GUI (which only draws their windows);
      - the game folder and small helpers.

    From C++ include this header, call Core::Load() once (it finds the DLL that is already loaded) and use Core::Functions().
    Every function is a plain C export, safe to call from any thread.
*/
#include <stdint.h>
#include <windows.h>

#ifdef __cplusplus
extern "C" {
#endif

#define CORE_API_VERSION 1

#ifdef CORE_EXPORTS
#define CORE_API __declspec(dllexport)
#else
#define CORE_API __declspec(dllimport)
#endif

/* What the plugin list knows about one plugin. All text is fixed size so nothing has to be freed. */
typedef struct CorePluginInfo
{
    uint32_t Size;          /* sizeof(CorePluginInfo) */
    char Key[96];           /* the line in Config.ini: "Yu-Gi-Oh-Cards" or "YGO-Ex/Yu-Gi-Oh-Funky" */
    char Name[64];          /* the DLL's name without .dll */
    char Title[96];         /* the manifest's title, or the DLL's name */
    char Description[200];  /* the manifest's description, or empty */
    char Problem[160];      /* why a plugin that is on can not load ("needs Yu-Gi-Oh-GUI, which is off"), or empty */
    int32_t Gui;            /* 1 = lives in Plugins\YGO-Ex (started by Core), 0 = injected by the loader */
    int32_t Enabled;        /* switched on in Config.ini (enforced plugins always are) */
    int32_t Active;         /* Enabled and everything it requires is active: this is what loads at the next start */
    int32_t Enforced;       /* can not be switched off */
    int32_t Loaded;         /* is in memory right now */
    HMODULE Module;         /* its module when loaded, else NULL */
} CorePluginInfo;

CORE_API int __cdecl Core_GetVersion(void);

/* The folder YuGiOh.exe is in, with a trailing backslash. */
CORE_API const char* __cdecl Core_GameFolder(void);

/* Re-reads the plugin folders, manifests and Config.ini. Called by the other functions when they need to; call it to pick up a changed file. */
CORE_API void __cdecl Core_Refresh(void);

/* The plugins in a stable order: the loader's plugins first, then the YGO-Ex ones, each by name. */
CORE_API int __cdecl Core_GetPluginCount(void);
CORE_API int __cdecl Core_GetPluginInfo(int Index, CorePluginInfo* Out);

/* Switches a plugin on or off in Config.ini (Key as in CorePluginInfo). Switching one ON also switches on what it requires; switching one OFF also
   switches off what requires it. Enforced plugins stay on. Returns how many plugins changed (0 = nothing to do). The change applies at the next
   start, except for a YGO-Ex plugin being switched on, which Core_StartPlugins can start now. */
CORE_API int __cdecl Core_SetPluginEnabled(const char* Key, int Enabled);

/* Loads and starts the YGO-Ex plugins that are on and can load and are not loaded yet. Safe to call again. Returns how many it started. A plugin
   that faults while starting is logged and left off. Called by Yu-Gi-Oh-RIX once the main menu is up, and by the GUI's "Load Plugins". */
CORE_API int __cdecl Core_StartPlugins(void);

#ifdef __cplusplus
}

#ifndef CORE_NO_CLIENT
namespace Core
{
    struct Api
    {
        int(__cdecl* GetVersion)() = nullptr;
        const char*(__cdecl* GameFolder)() = nullptr;
        void(__cdecl* Refresh)() = nullptr;
        int(__cdecl* GetPluginCount)() = nullptr;
        int(__cdecl* GetPluginInfo)(int, CorePluginInfo*) = nullptr;
        int(__cdecl* SetPluginEnabled)(const char*, int) = nullptr;
        int(__cdecl* StartPlugins)() = nullptr;
    };

    inline Api& Functions()
    {
        static Api api;
        return api;
    }

    // True when Yu-Gi-Oh-Core.dll is loaded and speaks this version of the interface.
    inline bool Load()
    {
        Api& api = Functions();
        if (api.GetVersion)
            return true;

        HMODULE module = GetModuleHandleA("Yu-Gi-Oh-Core.dll");
        if (!module)
            return false;

#define CORE_BIND(name) api.name = reinterpret_cast<decltype(api.name)>(GetProcAddress(module, "Core_" #name))
        CORE_BIND(GetVersion);
        if (!api.GetVersion || api.GetVersion() != CORE_API_VERSION)
        {
            api.GetVersion = nullptr;
            return false;
        }
        CORE_BIND(GameFolder);
        CORE_BIND(Refresh);
        CORE_BIND(GetPluginCount);
        CORE_BIND(GetPluginInfo);
        CORE_BIND(SetPluginEnabled);
        CORE_BIND(StartPlugins);
#undef CORE_BIND
        return true;
    }
}
#endif
#endif
