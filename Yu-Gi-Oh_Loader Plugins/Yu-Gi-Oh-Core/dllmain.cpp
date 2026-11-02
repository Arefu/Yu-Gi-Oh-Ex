#include <Windows.h>
#include <format>
#include <string>

#include "CardTable.h"
#include "Crash.h"
#include "Credits.h"
#include "DeckLimits.h"
#include "DuelFormat.h"
#include "Packs.h"
#include "Detours.h"
#include "Host.h"
#include "Loading.h"
#include "Logger.h"
#include "MasterDuel.h"
#include "ModConfig.h"
#include "Patch.h"
#include "Save.h"
#include "Tweaks.h"
#include "SaveScreen.h"
#include "SaveSlots.h"
#include "DeckTags.h"
#include "UserDecks.h"
#include "Yu-Gi-Oh-Core.h"
#include "YuGiOh/YuGiOh-Addresses.h"

// The exports (see Yu-Gi-Oh-Core.h).
extern "C"
{
    CORE_API int __cdecl Core_GetVersion(void) { return CORE_API_VERSION; }

    CORE_API const char* __cdecl Core_GameFolder(void) { return Save::GameFolder().c_str(); }

    CORE_API void __cdecl Core_Refresh(void) { Host::Refresh(); }

    CORE_API int __cdecl Core_GetPluginCount(void) { return Host::Count(); }

    CORE_API int __cdecl Core_GetPluginInfo(int Index, CorePluginInfo* Out)
    {
        return Out && Out->Size >= sizeof(CorePluginInfo) && Host::Info(Index, *Out) ? 1 : 0;
    }

    CORE_API int __cdecl Core_SetPluginEnabled(const char* Key, int Enabled) { return Host::SetEnabled(Key, Enabled != 0); }

    CORE_API int __cdecl Core_StartPlugins(void) { return Host::StartPlugins(); }

    CORE_API void __cdecl Core_AddCredits(const char* Heading, const char* Lines) { Credits::Add(Heading ? Heading : "", Lines ? Lines : ""); }

    CORE_API void __cdecl Core_AddSaveWriteFilter(CoreSaveWriteFilter Filter, void* User) { Save::AddWriteFilter(Filter, User); }

    CORE_API int __cdecl Core_GetSaveExtension(const char* Tag, void* Out, int Size) { return Save::GetExtension(Tag, Out, Size); }

    CORE_API void __cdecl Core_SetSaveExtension(const char* Tag, const void* Data, int Size) { Save::SetExtension(Tag, Data, Size); }

    CORE_API void __cdecl Core_SetDeckLimits(int MinMain, int MaxMain, int MaxExtra, int MaxSide) { DeckLimits::Set(MinMain, MaxMain, MaxExtra, MaxSide); }

    CORE_API void __cdecl Core_SetOnlineDuelFormat(int Format) { DuelFormat::SetOnline(Format); }

    CORE_API int __cdecl Core_GetExtraUserDecks(int* FirstId)
    {
        if (FirstId)
            *FirstId = static_cast<int>(UserDecks::kFirstExtraId);
        return UserDecks::ExtraCount();
    }

    CORE_API int __cdecl Core_GetDeckTags(int DeckId, char* Out, int Size)
    {
        const std::wstring tags = DeckId >= 0 ? DeckTags::Get(static_cast<unsigned>(DeckId)) : std::wstring();
        if (tags.empty())
        {
            if (Out && Size > 0)
                Out[0] = '\0';
            return 0;
        }
        const int length = WideCharToMultiByte(CP_UTF8, 0, tags.data(), static_cast<int>(tags.size()), nullptr, 0, nullptr, nullptr);
        if (Out && Size > length)
        {
            WideCharToMultiByte(CP_UTF8, 0, tags.data(), static_cast<int>(tags.size()), Out, Size, nullptr, nullptr);
            Out[length] = '\0';
        }
        else if (Out && Size > 0)
            Out[0] = '\0';
        return length;
    }

    CORE_API int __cdecl Core_SetDeckTags(int DeckId, const char* Tags)
    {
        if (DeckId < 0)
            return 0;
        const int bytes = Tags ? static_cast<int>(strlen(Tags)) : 0;
        std::wstring wide(bytes ? MultiByteToWideChar(CP_UTF8, 0, Tags, bytes, nullptr, 0) : 0, L'\0');
        if (!wide.empty())
            MultiByteToWideChar(CP_UTF8, 0, Tags, bytes, wide.data(), static_cast<int>(wide.size()));
        return DeckTags::Set(static_cast<unsigned>(DeckId), wide) ? 1 : 0;
    }

    CORE_API uint64_t __cdecl Core_GetDeckTagMask(int DeckId, int Row) { return DeckId >= 0 ? DeckTags::GetMask(static_cast<unsigned>(DeckId), Row) : 0; }

    CORE_API int __cdecl Core_EngineReady(void) { return MasterDuel::Ready() ? 1 : 0; }

    CORE_API void* __cdecl Core_EngineProc(const char* Name) { return reinterpret_cast<void*>(MasterDuel::Proc(Name)); }

    CORE_API int __cdecl Core_EngineRefreshCards(void) { return MasterDuel::RefreshCards(); }

    CORE_API void __cdecl Core_EngineMapCard(int GameId, int EngineId) { MasterDuel::MapCard(GameId, EngineId); }

    CORE_API void __cdecl Core_EngineSetDisplayId(int EngineId, int DisplayId) { MasterDuel::SetDisplayId(EngineId, DisplayId); }

    CORE_API int __cdecl Core_EngineLend(int DisplayId, int EngineId, int PropsGameId) { return MasterDuel::Lend(DisplayId, EngineId, PropsGameId) ? 1 : 0; }

    CORE_API int __cdecl Core_EngineToDisplay(int EngineId) { return EngineId > 0 && EngineId <= 0xFFFF ? MasterDuel::ToDisplay(static_cast<uint16_t>(EngineId)) : EngineId; }

    CORE_API int __cdecl Core_QuickPlaySlot(int Slot) { return SaveScreen::QuickPlay(Slot); }

    CORE_API void __cdecl Core_AddCardTableListener(CoreCardTableListener Listener)
    {
        // A bool and an unsigned char argument pass the same way.
        CardTable::AddTakeListener(reinterpret_cast<void(__cdecl*)(bool)>(Listener));
    }

    CORE_API int __cdecl Core_GetSaveFile(char* Out, int Size)
    {
        const std::u8string name = Save::CurrentSavePath().filename().u8string();
        if (!Out || Size <= static_cast<int>(name.size()))
            return 0;
        memcpy(Out, name.data(), name.size());
        Out[name.size()] = '\0';
        return static_cast<int>(name.size());
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    Logger::SetupLogger();

    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    {
        DetourRestoreAfterWith();
        Host::SetModule(hModule);

        // Mods' own Config.ini settings (ModConfig.h): first, so every setting read after this (Core's own and every plugin's) follows them.
        ModConfig::Install();

        // crash_log.txt + crash.dmp for any plugin's crash (Crash.h), then the small game tweaks (Tweaks.h).
        Crash::Install();
        Tweaks::Install();

        // Where the game's data comes from (was Yu-Gi-Oh-BetterLoad): the archive's name and multi-instance (Loading.h), then WolfX's patch
        // archive whenever it exists and loose files when LooseLoading is on (Patch.h). Before the game mounts its archive, so first.
        Loading::Install();
        Patch::Install();

        // Master Duel's duel engine (MasterDuel.h), when [Yu-Gi-Oh-Core] MasterDuelEngine=1: loaded now, given the card data when the game sets
        // up its card tables.
        MasterDuel::Install();

        // Credits: the built-in thanks, Yu-Gi-Oh-Ex\credits.json, what plugins add and the plugin list, then the game's own credits (Credits.h).
        Credits::Install();

        // Limit breakers no store plugin owns: booster packs from Yu-Gi-Oh-Ex/packs.json (new packs in the game's free slots, new shop rows).
        Packs::Install();
        // Deck size limits (MinMainDeck/MaxMainDeck/MaxExtraDeck/MaxSideDeck) and the engine's dormant Speed/Rush rules (DuelFormat).
        DeckLimits::Install();
        DuelFormat::Install();

        // Core is what keeps the base game unaltered: if the save could not be redirected the game would write the Steam save, so the player is asked.
        if (!Save::Install())
        {
            Logger::WriteLog("The save redirect could not be installed: the game would write the Steam save", MODULE_NAME, 2);
            if (MessageBoxA(NULL,
                "Yu-Gi-Oh-Core could not redirect the game's save.\n\n"
                "If you continue, the game can write to your real Steam save and you could lose progress.\n\n"
                "Close the game?", "Yu-Gi-Oh-Core", MB_ICONWARNING | MB_YESNO) == IDYES)
                TerminateProcess(GetCurrentProcess(), 1);
        }
        else
        {
            // Save slots: which file the game starts on, and the save-select screen between the title and the main menu.
            SaveSlots::Install();
            SaveScreen::Install();
            // The saved card table for every Konami id (ids past the save's 20000 in the save extension); card plugins widen the game's checks.
            CardTable::Install();
            // 256 user deck slots (the save holds 32; the rest in the save extension), from Toon Jinzo's Yu-Gi-Oh-DeckSlots (UserDecks.h).
            UserDecks::Install();
            // Tags on user decks and filtering the deck list by them, on the game's card filter panel; kept in the save extension (DeckTags.h).
            DeckTags::Install();
        }
        break;
    }
    }
    return TRUE;
}
