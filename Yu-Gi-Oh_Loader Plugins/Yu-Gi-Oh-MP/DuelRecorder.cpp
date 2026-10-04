#include <Windows.h>

#include <atomic>
#include <cstdint>
#include <format>
#include <mutex>
#include <string>

#include "DuelRecorder.h"
#include "Logger.h"
#include "Stubs.h"
#include "YuGiOh/YuGiOh-DUELSTATE.h"

// Game addresses (YuGiOh.exe.i64; the exe is not relocated). Duel::PlayerState and the getters are documented in
// docs/EffectSystem.md section 28 and docs/MultiplayerSystem.md.
namespace
{
    constexpr const char* kLogFile = "Duels";

    // The duel state's layout is shared with Yu-Gi-Oh-GUI and AntiCheat (YuGiOh-DUELSTATE.h).
    namespace DS = YGO::DUELSTATE;

    // The zone names Duels.log uses (DuelIt reads them), with the shared pile layout.
    struct Pile { const char* Name; const DS::Pile& Layout; };
    constexpr Pile kHand = { "hand", DS::Hand }, kDeck = { "deck", DS::Deck }, kExtra = { "extra", DS::ExtraDeck };

    template <typename R> R CallGame(uintptr_t address) { return reinterpret_cast<R(__fastcall*)()>(address)(); }
    bool IsDuelMultiplayer() { return CallGame<uint8_t>(0x1407691D0) != 0; }
    bool IsTagDuel() { return CallGame<uint8_t>(0x1407694D0) != 0; }
    int LiveMatchType() { return CallGame<uint8_t>(0x140768E20); }
    bool IsBattlePack() { return CallGame<uint8_t>(0x140769190) != 0; }
    bool IsCampaign() { return CallGame<uint8_t>(0x1407691A0) != 0; }
    bool IsChallenge() { return CallGame<uint8_t>(0x1407691B0) != 0; }
    bool IsRoundBased() { return CallGame<uint8_t>(0x1407691F0) != 0; }
    int LocalPlayerSeat() { return CallGame<int>(0x140768F80); }

    template <typename T> T Read(uintptr_t address) { return *reinterpret_cast<volatile T*>(address); }

    int LifePoints(int side)
    {
        // The key is 16 bits (movzx in Duel__Msg__Handle_25_LP_Set, 0x1401264D0); the word after it is unrelated.
        return DS::LifePoints(side);
    }

    std::string LpPair() { return std::format("{}/{}", LifePoints(0), LifePoints(1)); }

    // Yu-Gi-Oh-MoreCards lends vanilla ids to custom cards for the length of a duel; this gives the custom card's real id.
    uint16_t RealCardId(uint16_t id)
    {
        using Fn = unsigned short(__cdecl*)(unsigned short);
        static const Fn lookup = reinterpret_cast<Fn>(GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-MoreCards.dll"), "Card_GetRealIdForBorrowed"));
        if (!lookup)
            return id;
        const unsigned short real = lookup(id);
        return real ? real : id;
    }

    std::atomic<bool> g_InDuel = false;
    std::atomic<uint32_t> g_Events = 0;
    std::atomic<uint32_t> g_DuelCount = 0;
    std::atomic<int> g_StartSide = -1;
    std::mutex g_SeatLock;
    std::string g_SeatNames[4];

    void Write(const std::string& line)
    {
        ++g_Events;
        Logger::WriteLogTo(kLogFile, line, MODULE_NAME, 0);
    }

    void EndDuel(int outcome, int winner, int reason, const char* source);

    std::string CardList(int side, const Pile& pile)
    {
        const uint32_t count = DS::PileCount(side, pile.Layout);
        std::string out;
        for (uint32_t i = 0; i < count && i < 256; ++i)
        {
            // Packed card word: low 14 bits = card id, bit 14 + bits 23-30 = the card's slot (its index for the whole duel).
            const uint32_t word = DS::PileWord(side, pile.Layout, static_cast<int>(i));
            const uint32_t slot = DS::InstanceOf(word);
            if (DS::CardIdOf(word) == 0)   // the hand count is already 5 at Engine_Init but its entries are still empty
                continue;
            if (!out.empty())
                out += ',';
            out += std::format("{}:{}", slot, RealCardId(DS::CardIdOf(word)));
        }
        return out;
    }

    void BeginDuel()
    {
        g_Events = 0;
        g_InDuel = true;
        const uint32_t index = ++g_DuelCount;

        SYSTEMTIME now;
        GetLocalTime(&now);
        const bool online = IsDuelMultiplayer();
        const char* mode = online ? "Online" : IsCampaign() ? "Campaign" : IsChallenge() ? "Challenge" : IsBattlePack() ? "BattlePack" : "Free";
        const char* match = online ? (LiveMatchType() == 1 ? "Ranked" : "Friendly") : "-";
        const int seat = LocalPlayerSeat();

        Write(std::format("DUEL_BEGIN id={:04}{:02}{:02}-{:02}{:02}{:02}-{} date={:04}-{:02}-{:02}T{:02}:{:02}:{:02} mode={} online={} tag={} match={} rounds={} localSeat={} localSide={} startSide={} rng=0x{:X}",
            now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond, index,
            now.wYear, now.wMonth, now.wDay, now.wHour, now.wMinute, now.wSecond,
            mode, online ? 1 : 0, IsTagDuel() ? 1 : 0, match, IsRoundBased() ? 1 : 0, seat, seat & 1, g_StartSide.load(),
            Read<uint32_t>(DS::PlayerState + DS::RngOffset)));

        {
            std::lock_guard lock(g_SeatLock);
            for (int i = 0; i < 4; ++i)
                if (!g_SeatNames[i].empty())
                    Write(std::format("SEAT seat={} name=\"{}\"", i, g_SeatNames[i]));
        }
        for (int side = 0; side < 2; ++side)
            for (const Pile& pile : { kDeck, kExtra, kHand })
                Write(std::format("CARDS side={} zone={} cards={}", side, pile.Name, CardList(side, pile)));

        Logger::WriteLog(std::format("Duel {} started ({}{}) - recording to Duels.log", index, mode, online ? std::format(", {}", match) : ""), MODULE_NAME, 0);
    }
}

void DuelRecorder::Tap_EngineInit(const u64*, size_t, bool after, u64)
{
    if (after)   // decks are loaded and shuffled inside Engine_Init
        BeginDuel();
}

// Duel__MsgQueue__PumpAndMirror pops g_DuelMsgQueue.Entries[0] into Front and starts executing it. Push itself can't be
// hooked (Stubs.h), so compare the queue before and after: if Count dropped, the entry that was first got popped. This
// is execution order (what the player sees, LP already applied by the time the next one runs). HighWordPrefix (0x6D)
// entries carry the upper 16 bits of the next message's args and are folded into it rather than written.
void DuelRecorder::Tap_PumpAndMirror(const u64*, size_t, bool after, u64)
{
    struct DuelMsg { uint16_t CodeAndSide, Arg1, Arg2, Arg3; };
    static uint32_t countBefore = 0;
    static DuelMsg first{};
    static uint16_t high[3]{};

    const uint32_t count = Read<uint32_t>(DS::MsgQueueCount);
    if (!after)
    {
        countBefore = count;
        if (count)
            first = *reinterpret_cast<const DuelMsg*>(DS::MsgQueue + 0x10);
        return;
    }
    if (!g_InDuel || countBefore == 0 || count >= countBefore)
        return;

    const uint32_t code = first.CodeAndSide & 0xFFF;
    if (code == 0x6D)
    {
        high[0] = first.Arg1;
        high[1] = first.Arg2;
        high[2] = first.Arg3;
        return;
    }
    const int32_t a1 = static_cast<int32_t>(first.Arg1 | (high[0] << 16));
    const int32_t a2 = static_cast<int32_t>(first.Arg2 | (high[1] << 16));
    const int32_t a3 = static_cast<int32_t>(first.Arg3 | (high[2] << 16));
    high[0] = high[1] = high[2] = 0;
    Write(std::format("MSG n={} code=0x{:02X} name={} side={} a1={} a2={} a3={} lp={}", g_Events.load(), code, Stubs::DuelMsgName(code),
        (first.CodeAndSide >> 15) & 1, a1, a2, a3, LpPair()));

    // DuelResult: a1 = winner (1 the local side, 2 the other side, 3 draw - PlayerState.field_3792's encoding), a2 / a3 = the
    // win reason recorded for side 0 / side 1 (the winner's is set). Seen: a1=2 a3=2 = the AI won by Deck-out.
    if (code == 0x05)
    {
        const int localSide = LocalPlayerSeat() & 1;
        const int winnerSide = a1 == 1 ? localSide : a1 == 2 ? 1 - localSide : -1;
        const int reason = winnerSide == 0 ? a2 : winnerSide == 1 ? a3 : (a2 ? a2 : a3);
        EndDuel(a1, a1, reason, "DuelResult");
    }
}

void DuelRecorder::Tap_OnCardMove(const u64* a, size_t, bool after, u64)
{
    if (!after)
        Write(std::format("MOVE slot={} kind={} from=0x{:X} to=0x{:X}", static_cast<int32_t>(a[0]), static_cast<int32_t>(a[1]),
            static_cast<uint16_t>(a[2]), static_cast<uint16_t>(a[3])));
}

void DuelRecorder::Tap_OnChainResolve(const u64* a, size_t, bool after, u64)
{
    if (!after)
        Write(std::format("CHAIN side={} len={}", static_cast<uint32_t>(a[0]), static_cast<int32_t>(a[2])));
}

void DuelRecorder::Tap_LPDamage(const u64* a, size_t, bool after, u64)
{
    if (after)
        Write(std::format("LP side={} kind=damage amount={} lp={}", static_cast<uint32_t>(a[1]) & 1, static_cast<uint32_t>(a[2]), LpPair()));
}

void DuelRecorder::Tap_LPGain(const u64* a, size_t, bool after, u64)
{
    if (after)
        Write(std::format("LP side={} kind=gain amount={} lp={}", static_cast<uint32_t>(a[1]) & 1, static_cast<int32_t>(a[2]), LpPair()));
}

namespace
{
    // Writes DUEL_END once per duel: from DuelResult (0x05) as it executes, or from YGO__DUEL__OnDuelEnd, whichever comes first.
    // OnDuelEnd never fired in Campaign duels (2026-09-30), DuelResult always did.
    void EndDuel(int outcome, int winner, int reason, const char* source)
    {
        bool expected = true;
        if (!g_InDuel.compare_exchange_strong(expected, false))
            return;
        const uint32_t events = g_Events.load();
        Write(std::format("DUEL_END outcome={} winner={} reason={} lp={} events={} from={}", outcome, winner, reason, LpPair(), events, source));
        Logger::FlushLogTo(kLogFile);
        g_StartSide = -1;
        Logger::WriteLog(std::format("Duel ended: winner {}, win reason {} ({} events recorded)", winner, reason, events), MODULE_NAME, 0);
    }
}

void DuelRecorder::Tap_OnDuelEnd(const u64* a, size_t, bool after, u64)
{
    if (!after)
        EndDuel(static_cast<int32_t>(a[0]), Read<uint8_t>(DS::PlayerState + DS::WinnerOffset), static_cast<int>(Read<uint32_t>(DS::LastWinReason)), "OnDuelEnd");
}

void DuelRecorder::Tap_SetRoundResult(const u64* a, size_t, bool after, u64)
{
    if (!after)
        Write(std::format("ROUND round={} outcome={} localParity={}", static_cast<int32_t>(a[0]), static_cast<int32_t>(a[1]), static_cast<int32_t>(a[2])));
}

void DuelRecorder::Tap_FinishAndUpdateSave(const u64* a, size_t, bool after, u64)
{
    if (!after)
    {
        Write(std::format("MATCH_END dp={}", static_cast<int32_t>(a[2])));
        Logger::FlushLogTo(kLogFile);
    }
}

void DuelRecorder::Tap_UpdateSaveStat(const u64* a, size_t, bool after, u64)
{
    if (!after)
        Write(std::format("STAT stat={} delta={}", static_cast<uint32_t>(a[1]), static_cast<int64_t>(a[2])));
}

void DuelRecorder::Tap_SetStartingPlayer(const u64* a, size_t, bool after, u64)
{
    if (after)
        return;
    g_StartSide = static_cast<int>(a[0] & 0xFF);
    if (g_InDuel)
        Write(std::format("START side={}", g_StartSide.load()));
}

void DuelRecorder::Tap_LiveSeatsSetName(const u64* a, size_t, bool after, u64)
{
    const int seat = static_cast<int>(a[0]);
    const wchar_t* name = reinterpret_cast<const wchar_t*>(a[1]);
    if (after || seat < 0 || seat >= 4 || !name)
        return;

    char utf8[256]{};
    WideCharToMultiByte(CP_UTF8, 0, name, static_cast<int>(wcsnlen(name, 64)), utf8, sizeof(utf8) - 1, nullptr, nullptr);
    std::string clean = utf8;
    for (char& c : clean)
        if (c == '"')
            c = '\'';
    std::lock_guard lock(g_SeatLock);
    g_SeatNames[seat] = clean;
}

void DuelRecorder::Tap_NetEventReceive(const u64* a, size_t, bool after, u64)
{
    if (after)
        Write(std::format("NET_RECV data=\"{}\"", Stubs::HexDump(reinterpret_cast<const void*>(a[0]), 48, 48)));
}

void DuelRecorder::OnNetSend(uint32_t sequence, uint16_t type, const uint8_t* payload, size_t size)
{
    Write(std::format("NET_SEND seq={} type={} code={} payload=\"{}\"", sequence, Stubs::EventTypeName(type), type, Stubs::HexDump(payload, size, 128)));
}
