#define _CRT_RAND_S   // rand_s (RandomBit)
#include <stdlib.h>
#include <Windows.h>
#include <detours.h>
#include <iostream>
#include <fstream>
#include <string>
#include <format>
#include "Yu-Gi-Oh-Ex.h"
#include "Logger.h"

BOOL NoJanken = 1;
INT JankenFirst = 0; // 0 = random, 1 = P1 (you), 2 = P2 (opponent)
INT SetLP = 8000;

// Duel__Lobby__Update (0x1407AFD80): the pre-duel lobby (coin toss / Janken / who goes first). It returns true once its state (+3704) is
// 15; Duel__NetSession__Update then runs Engine_Init. With NoJanken (offline only) none of the Janken screens run: when the lobby is about
// to leave state 2 (the duel screen has faded in - the same test state 2 makes) the hook does the setup the duel needs and closes the lobby:
//   - Set_StartingPlayer (team 0/1; JankenFirst = random / P1 / P2). Engine_Init copies it into the duel.
//   - Duel_SetDuelCounts(1) (+ the battle-pack duelist slot), what Duel__Lobby__SetState(6) does after a non-draw Janken. Without it the
//     result screen skips FinishAndUpdateSave: no DP, no unlocks, no card rewards, no save.
//   - Duel__Lobby__SetState(14): the lobby's own exit. It arms the closing fade on the duel screen fader with the callback that hides the
//     lobby and sets state 15. Leaving the fader busy (an empty hook) also stopped the result screen being built.
// Online duels play Janken as normal.
using JankenUpdate_t = bool(__fastcall*)(__int64 lobby, float deltaTime);
static bool(__fastcall* IsDuelMultiplayer)() = (bool(__fastcall*)())0x1407691D0;
static int(__fastcall* LocalPlayerSeat)() = (int(__fastcall*)())0x140768F80;
static void(__fastcall* SetStartingPlayer)(int player) = (void(__fastcall*)(int))0x140769740;
static void(__fastcall* SetDuelCounts)(char on) = (void(__fastcall*)(char))0x1407697E0;
static bool(__fastcall* IsBattlePackMode)() = (bool(__fastcall*)())0x140769190;
static int(__fastcall* BattlePackDuelistIndex)() = (int(__fastcall*)())0x1407688A0;
static __int64(__fastcall* GetDuelistSlot)(unsigned int profile, int index) = (__int64(__fastcall*)(unsigned int, int))0x1407F8020;
static void(__fastcall* MarkDuelistSlotPlayed)(__int64 slot) = (void(__fastcall*)(__int64))0x1407F8CB0;
static void(__fastcall* LobbySetState)(__int64 lobby, int state) = (void(__fastcall*)(__int64, int))0x1407B57A0;

static bool RandomBit()
{
    unsigned int value = 0;
    return rand_s(&value) == 0 ? (value & 1) != 0 : (GetTickCount64() / 16) % 2 != 0;   // GetTickCount64 alone steps by ~16 ms
}

bool __fastcall Patch_DoJankenAndPlayerSelection(__int64 lobby, float deltaTime)
{
    // Duel__NetSession's state 1 shows the lobby widget (vtable +24 = show/hide, the session hides it again at state 2), so its Janken
    // panels drew for the frames before the skip. Hidden once per duel; the lobby still updates (the session calls it, not the drawing).
    static bool hidden = false;
    const int state = *(int*)(lobby + 3704);
    if (state >= 15 || state <= 1)   // finished, or a new lobby (an abandoned one never reached 15)
        hidden = false;
    else if (!hidden && !IsDuelMultiplayer())
    {
        using SetVisible_t = void(__fastcall*)(__int64 widget, bool visible);
        reinterpret_cast<SetVisible_t>((*(void***)lobby)[3])(lobby, false);
        hidden = true;
    }

    if (state == 2 && !IsDuelMultiplayer())
    {
        const int fader = **(int**)(lobby + 3456);   // state 2's test: not fading, and faded in
        if ((fader & 2) == 0 && (fader & 0x14) != 0)
        {
            int local = LocalPlayerSeat() & 1;   // StartingPlayer is a team (0/1), as Duel__Lobby__ResolveStartingPlayer_State0xB sets it
            SetStartingPlayer(JankenFirst == 1 ? local : JankenFirst == 2 ? local ^ 1 : RandomBit() ? local : local ^ 1);
            if (IsBattlePackMode())
            {
                if (__int64 slot = GetDuelistSlot(0xFFFFFFFD, BattlePackDuelistIndex()))
                    MarkDuelistSlotPlayed(slot);
            }
            SetDuelCounts(1);
            LobbySetState(lobby, 14);
        }
    }
    return reinterpret_cast<JankenUpdate_t>(YuGiOhEx::JankenAndPlayerSelection)(lobby, deltaTime);
}

void Patch_SetLPToCustomValue()
{
    using SetLPFunc = void(*)();
    ((SetLPFunc)YuGiOhEx::SetLP)();

    *(int*)0x140C8D370 = SetLP;
}

void ProcessConfig();

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    Logger::SetupLogger();
    ProcessConfig();
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
        Logger::WriteLog("DLL_PROCESS_ATTACH - Ready to BREAK THINGS!", MODULE_NAME, 0);
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());

        if (NoJanken == true)
        {
            Logger::WriteLog("Patched Janken", MODULE_NAME, 0);;
            DetourAttach(&(PVOID&)YuGiOhEx::JankenAndPlayerSelection, Patch_DoJankenAndPlayerSelection);
        }
        if (SetLP != 8000)
        {
            Logger::WriteLog(std::format("Changed LP To {}", SetLP), MODULE_NAME, 0);
            DetourAttach(&(PVOID&)YuGiOhEx::SetLP, Patch_SetLPToCustomValue);
        }
        DetourTransactionCommit();


        break;
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}

void ProcessConfig()
{

    NoJanken = GetPrivateProfileIntW(L"Yu-Gi-Oh-PatchMeOut", L"NoJanken", 1, L".\\Config.ini");
    wchar_t first[16] = {};
    GetPrivateProfileStringW(L"Yu-Gi-Oh-PatchMeOut", L"JankenFirst", L"random", first, 16, L".\\Config.ini");
    JankenFirst = _wcsicmp(first, L"P1") == 0 ? 1 : _wcsicmp(first, L"P2") == 0 ? 2 : 0;

    SetLP = GetPrivateProfileIntW(L"Yu-Gi-Oh-PatchMeOut", L"StartingLP", 8000, L".\\Config.ini");
}
