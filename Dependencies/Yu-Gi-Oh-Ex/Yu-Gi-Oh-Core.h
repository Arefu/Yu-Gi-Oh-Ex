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

/* Adds a section to the game's Credits screen, before the game's own credits (see Credits.h in Yu-Gi-Oh-Core for the whole order). Heading may be
   NULL or empty (no heading); Lines are separated by '
'. Both UTF-8. Kept until the game closes and shown every time Credits is opened, so call it
   once (at start-up). Added after version 1 shipped without changing CORE_API_VERSION: Core::Functions().AddCredits is NULL with an older Core. */
CORE_API void __cdecl Core_AddCredits(const char* Heading, const char* Lines);

/* The file name (no folder, UTF-8) of the save the game reads and writes now: savegame-ex.dat, or savegame-ex-N.dat for save slot N. For plugins
   that keep something per save outside it. Returns its length, or 0 when Out is NULL or Size is too small. Added after version 1 like
   AddCredits: Core::Functions().GetSaveFile is NULL with an older Core. */
CORE_API int __cdecl Core_GetSaveFile(char* Out, int Size);

/* Save write filters: every time the game writes its save, Core hands each filter a COPY of the 44008-byte blob (after the game's own checksum
   step) and writes that copy, re-checksummed, instead of the game's blob. A filter changes the copy only (the game's blob in memory stays as
   it is) and returns nonzero when it changed something. For plugins that put their own data into the save while the game runs (Campaign's
   new series borrow a series slot) and must never let it reach the file. Called on the game's save thread. Added after version 1 like
   AddCredits: Core::Functions().AddSaveWriteFilter is NULL with an older Core. */
typedef int(__cdecl* CoreSaveWriteFilter)(unsigned char* Blob, int Size, void* User);
CORE_API void __cdecl Core_AddSaveWriteFilter(CoreSaveWriteFilter Filter, void* User);

/* Save extension: plugin data kept IN the save file (savegame-ex.dat, or the slot's file), after the game's 44008 bytes, by tag. The file
   only grows when some plugin has data there (a save with none stays exactly the game's 44008 bytes); the game is only ever handed its own
   44008 bytes. Layout after the blob: "YGEX", u32 version (1), u32 count, then count x { char tag[32] (NUL padded), u32 size, size bytes }.
   Get: copies the data of Tag for the save read last into Out (up to Size bytes) and returns its full size, 0 when there is none (Out may
   be NULL to ask the size). Set: replaces it (Size 0 removes it); written with the game's next save write. Thread safe. Added after
   version 1 like AddCredits: NULL with an older Core. */
CORE_API int __cdecl Core_GetSaveExtension(const char* Tag, void* Out, int Size);
CORE_API void __cdecl Core_SetSaveExtension(const char* Tag, const void* Data, int Size);

/* Deck size limits while the game runs (docs/DeckLimits.md): the fewest/most Main Deck cards a usable deck has and the deck editor's Extra and
   Side caps, clamped to 1-60 / MinMain-60 / 0-15 / 0-15 (a deck holds 60/15/15). A negative MinMain puts back Config.ini's limits
   ([Yu-Gi-Oh-Core] MinMainDeck...). Applies at once (the deck lists check decks again when they are shown). For Yu-Gi-Oh-MP's lobby rules.
   Added after version 1 like AddCredits: NULL with an older Core. */
CORE_API void __cdecl Core_SetDeckLimits(int MinMain, int MaxMain, int MaxExtra, int MaxSide);

/* The duel format for ONLINE duels (docs/DuelFormat.md): 0 Master, 1 Speed, 2 Rush. Offline duels use [Yu-Gi-Oh-Core] DuelFormat; online duels
   use this, which every player in the duel must set the same (Yu-Gi-Oh-MP sends the host's choice through the lobby). The online starting LP
   stays the lobby's LP setting. Taken at the start of each duel. Added after version 1 like AddCredits: NULL with an older Core. */
CORE_API void __cdecl Core_SetOnlineDuelFormat(int Format);

/* The user deck slots past the save's 32 (docs/UserDecks.md, from Toon Jinzo's Yu-Gi-Oh-DeckSlots): returns how many (224) and puts the
   first one's deck id in FirstId (1024; they run FirstId..FirstId+count-1). Deck_FromId (0x14081AB40) answers them like ids 0-31: an empty
   slot is a deck with no cards. Added after version 1 like AddCredits: NULL with an older Core (32 slots only). */
CORE_API int __cdecl Core_GetExtraUserDecks(int* FirstId);

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
        void(__cdecl* AddCredits)(const char*, const char*) = nullptr;   // NULL with a Core that predates it
        int(__cdecl* GetSaveFile)(char*, int) = nullptr;                 // NULL with a Core that predates it
        void(__cdecl* AddSaveWriteFilter)(CoreSaveWriteFilter, void*) = nullptr;   // NULL with a Core that predates it
        int(__cdecl* GetSaveExtension)(const char*, void*, int) = nullptr;           // NULL with a Core that predates it
        void(__cdecl* SetSaveExtension)(const char*, const void*, int) = nullptr;    // NULL with a Core that predates it
        void(__cdecl* SetDeckLimits)(int, int, int, int) = nullptr;                  // NULL with a Core that predates it
        void(__cdecl* SetOnlineDuelFormat)(int) = nullptr;                           // NULL with a Core that predates it
        int(__cdecl* GetExtraUserDecks)(int*) = nullptr;                             // NULL with a Core that predates it
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
        CORE_BIND(AddCredits);
        CORE_BIND(GetSaveFile);
        CORE_BIND(AddSaveWriteFilter);
        CORE_BIND(GetSaveExtension);
        CORE_BIND(SetSaveExtension);
        CORE_BIND(SetDeckLimits);
        CORE_BIND(SetOnlineDuelFormat);
        CORE_BIND(GetExtraUserDecks);
#undef CORE_BIND
        return true;
    }
}
#endif
#endif
