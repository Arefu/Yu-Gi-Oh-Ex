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

/* The user deck slots past the save's 32 (docs/UserDecks.md, from Toon Jinzo's Yu-Gi-Oh-DeckSlots): returns how many and puts the
   first one's deck id in FirstId (1024; they run FirstId..FirstId+count-1). The count is the loaded save's (the slots it uses + 256 free),
   so ask again rather than keep it. Deck_FromId (0x14081AB40) answers them like ids 0-31: an empty
   slot is a deck with no cards. Added after version 1 like AddCredits: NULL with an older Core (32 slots only). */
CORE_API int __cdecl Core_GetExtraUserDecks(int* FirstId);

/* Master Duel's duel engine (docs/MasterDuelEngine.md): Core loads Plugins\Yu-Gi-Oh-Core\duel.dll when [Yu-Gi-Oh-Core] MasterDuelEngine=1.
   EngineReady: 1 once it is loaded and has card data. EngineProc: one of its exports by name ("DLL_DuelSysAct", "DLL_SetCardNamed" ...), NULL
   when it is not loaded; the signatures are in docs/MasterDuelEngine.md. EngineRefreshCards: gives it the game's card data again (every card
   the game's lookups know, so a plugin that adds cards calls it after adding them); returns the number of cards. Added after version 1 like
   AddCredits: NULL with an older Core. */
CORE_API int __cdecl Core_EngineReady(void);
CORE_API void* __cdecl Core_EngineProc(const char* Name);
CORE_API int __cdecl Core_EngineRefreshCards(void);

/* Card ids in the engine. duel.dll knows a card's effect by its Konami id (YGOPRODeck konami_id), so a plugin that adds cards gives each one
   the id the engine should see it as: EngineMapCard(game id, engine id) - its Konami id, or an id from 40000 up for a card Konami does not
   have (so it never picks up a real card's effect). Call before EngineRefreshCards. The game's duel state only holds 14-bit ids, so for a
   duel a card with a game id past 16383 plays under a borrowed id: EngineSetDisplayId(engine id, borrowed id) tells Core what to show for it
   (call it when the duel's decks are loaded). Ids not mapped are the same in both. Added after version 1 like AddCredits. */
CORE_API void __cdecl Core_EngineMapCard(int GameId, int EngineId);
CORE_API void __cdecl Core_EngineSetDisplayId(int EngineId, int DisplayId);

/* For this duel only, the card the screen shows as DisplayId plays as engine id EngineId (any card the engine knows): duel.dll's card record
   for EngineId gets the props of game card PropsGameId (its stats, kind, level ...), so the card keeps its own stats while every one of
   the engine's rules for EngineId (its effect rows and every id-keyed branch) applies to it. Call when the duel's decks are loaded, at most
   once per EngineId per duel, and only when no real copy of EngineId is in the duel. Returns 1 when lent, 0 when not (no engine, or the
   engine has no such card). The records are put back at the next duel. Added after version 1 like AddCredits: NULL with an older Core. */
CORE_API int __cdecl Core_EngineLend(int DisplayId, int EngineId, int PropsGameId);

/* The id the duel screen shows for an engine id (the game id, or this duel's borrowed id for a card past 16383; a lent card's display id):
   what a plugin keyed on game ids looks a record's engine id up as inside duel.dll. Ids not mapped come back unchanged. Added after
   version 1 like AddCredits: NULL with an older Core. */
CORE_API int __cdecl Core_EngineToDisplay(int EngineId);

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
        int(__cdecl* EngineReady)() = nullptr;                                       // NULL with a Core that predates it
        void*(__cdecl* EngineProc)(const char*) = nullptr;                           // NULL with a Core that predates it
        int(__cdecl* EngineRefreshCards)() = nullptr;                                // NULL with a Core that predates it
        void(__cdecl* EngineMapCard)(int, int) = nullptr;                            // NULL with a Core that predates it
        void(__cdecl* EngineSetDisplayId)(int, int) = nullptr;                       // NULL with a Core that predates it
        int(__cdecl* EngineLend)(int, int, int) = nullptr;                           // NULL with a Core that predates it
        int(__cdecl* EngineToDisplay)(int) = nullptr;                                // NULL with a Core that predates it
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
        CORE_BIND(EngineReady);
        CORE_BIND(EngineProc);
        CORE_BIND(EngineRefreshCards);
        CORE_BIND(EngineMapCard);
        CORE_BIND(EngineSetDisplayId);
        CORE_BIND(EngineLend);
        CORE_BIND(EngineToDisplay);
#undef CORE_BIND
        return true;
    }
}
#endif
#endif
