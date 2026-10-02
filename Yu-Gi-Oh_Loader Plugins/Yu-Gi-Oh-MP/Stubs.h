#pragma once

#include <Windows.h>
#include <intrin.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <string>
#include <type_traits>

// Passthrough logging stubs for every multiplayer/duel-flow function we know about (docs/MultiplayerSystem.md,
// docs/StatsAndMatchResults.md). One line per function in EngineHooks.cpp:
//
//   Stub<0x14081E060, "Duel__LiveManager_TransportSend", "data:hex@1,size,targetSeat:i32", Info, u64(u64, u64, u64)>
//
// - The signature only fixes how many arguments there are and which are float/double (they live in XMM registers).
//   Every integer/pointer argument is forwarded as a full 64-bit register and every stub returns u64, so whatever the
//   original leaves in RAX/the argument registers passes through untouched even where IDA's types are narrower than
//   the truth. The per-parameter formats below only affect what gets logged.
// - Every stub logs its first kMaxLogged (3) calls, then one "further calls not logged" line. Level Quiet = never
//   logged (the stub exists only for its tap); Debug = console.log only; Info = console too.
// - Tap (optional last parameter) = a function called with the raw arguments before the original runs (after=false) and
//   again after it returned (after=true, with its result), on every call. DuelRecorder uses these to write Duels.log.
// - Each log line names the caller (return address, an IDA address since the exe isn't relocated).
//
// DON'T stub Duel__MsgQueue__Push (0x14000F310). The exe is link-time optimised: Push only touches rax/rcx/r10/r11, so
// its ~2500 callers keep live values in rdx/r8/r9 across the call (e.g. CheckTurnOrTimeLimit stores edx right after it,
// 0x140148034). A C++ hook uses those registers freely and the End Phase never advanced (AI turn hang, 2026-09-30).
// Every other stubbed function was checked: no caller reads rcx/rdx/r8-r11 after the call before writing it
// (scratchpad regscan.py; re-run it when adding a hot/leaf function).
//
// Parameter formats (after "name:"):
//   (none)  64-bit value, decimal if it fits 32 bits else hex     ptr/hex  0x hex
//   i8 u8 i16 u16 i32 u32 i64 bool                                float    the float/double argument
//   cstr / wstr   string at the pointer (safe read)               p32/p64  value the pointer points at
//   msg     DuelMsgCode name + side bit                           evt      DuelNetEventType name
//   hexN    N bytes at the pointer                                hexN+O   N bytes at pointer+O
//   hex@K   bytes at the pointer, count = argument K
//   hex@*K  bytes at the pointer, count = *(u64*)argument K (in/out size pointers)
namespace Stubs
{
    using u64 = uint64_t;

    enum Level : int { Quiet = -1, Info = 0, Debug = 69 };

    constexpr uint32_t kMaxLogged = 3;

    // args = the raw arguments (floats as double bits), argCount of them; result is only meaningful when after is true.
    using TapFn = void(*)(const u64* args, size_t argCount, bool after, u64 result);

    template <size_t N>
    struct FixedString
    {
        char Value[N]{};
        constexpr FixedString(const char (&text)[N]) { std::copy_n(text, N, Value); }
    };

    // Formats one call and writes it to the log. 'after' is set for stubs that log after the original ran (receive
    // functions, so out-buffers are filled), together with the original's result.
    void LogCall(const char* name, const char* params, const u64* args, size_t argCount, uint32_t call, int level,
                 void* returnAddress, bool after = false, u64 result = 0);
    bool Attach(void** original, void* hook, const char* name);

    template <typename T>
    u64 ToRaw(T value)
    {
        if constexpr (std::is_floating_point_v<T>)
        {
            const double d = value;
            u64 bits;
            std::memcpy(&bits, &d, sizeof bits);
            return bits;
        }
        else
            return (u64)value;
    }

    template <uintptr_t Addr, FixedString Name, FixedString Params, Level Lvl, typename Sig, TapFn Tap = nullptr>
    struct Stub;

    template <uintptr_t Addr, FixedString Name, FixedString Params, Level Lvl, TapFn Tap, typename... A>
    struct Stub<Addr, Name, Params, Lvl, u64(A...), Tap>
    {
        using Fn = u64(*)(A...);
        static inline Fn Original = reinterpret_cast<Fn>(Addr);
        static inline std::atomic<uint32_t> Calls = 0;

        static u64 Hook(A... args)
        {
            const u64 raw[sizeof...(A) + 1] = { ToRaw(args)..., 0 };
            if constexpr (Lvl != Quiet)
            {
                if (Calls.load(std::memory_order_relaxed) < kMaxLogged)
                {
                    const uint32_t call = ++Calls;
                    if (call <= kMaxLogged)
                        LogCall(Name.Value, Params.Value, raw, sizeof...(A), call, Lvl, _ReturnAddress());
                }
            }
            if constexpr (Tap != nullptr)
                Tap(raw, sizeof...(A), false, 0);
            const u64 result = Original(args...);
            if constexpr (Tap != nullptr)
                Tap(raw, sizeof...(A), true, result);
            return result;
        }

        static bool Attach() { return Stubs::Attach(reinterpret_cast<void**>(&Original), reinterpret_cast<void*>(&Hook), Name.Value); }
    };

    // For polled receive functions: calls the original first and only logs calls that returned nonzero (something was
    // actually received), after the call so out-buffers can be dumped. The tap (after=true only) also only sees those.
    template <uintptr_t Addr, FixedString Name, FixedString Params, Level Lvl, typename Sig, TapFn Tap = nullptr>
    struct RecvStub;

    template <uintptr_t Addr, FixedString Name, FixedString Params, Level Lvl, TapFn Tap, typename... A>
    struct RecvStub<Addr, Name, Params, Lvl, u64(A...), Tap>
    {
        using Fn = u64(*)(A...);
        static inline Fn Original = reinterpret_cast<Fn>(Addr);
        static inline std::atomic<uint32_t> Logged = 0;

        static u64 Hook(A... args)
        {
            const u64 result = Original(args...);
            if ((result & 0xFF) != 0)   // most of these return bool/char; the upper bits of RAX are not meaningful
            {
                const u64 raw[sizeof...(A) + 1] = { ToRaw(args)..., 0 };
                if constexpr (Lvl != Quiet)
                {
                    if (Logged.load(std::memory_order_relaxed) < kMaxLogged)
                    {
                        const uint32_t call = ++Logged;
                        if (call <= kMaxLogged)
                            LogCall(Name.Value, Params.Value, raw, sizeof...(A), call, Lvl, _ReturnAddress(), true, result);
                    }
                }
                if constexpr (Tap != nullptr)
                    Tap(raw, sizeof...(A), true, result);
            }
            return result;
        }

        static bool Attach() { return Stubs::Attach(reinterpret_cast<void**>(&Original), reinterpret_cast<void*>(&Hook), Name.Value); }
    };

    // Shared formatting helpers, also used by the hand-written hooks in EngineHooks.cpp and by DuelRecorder.
    std::string HexDump(const void* address, size_t size, size_t cap = 256);
    const char* EventTypeName(unsigned type);
    const char* DuelMsgName(unsigned code);
}
