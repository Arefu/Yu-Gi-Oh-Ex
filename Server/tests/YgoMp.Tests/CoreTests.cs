using System.Buffers.Binary;
using YgoMp.Core;
using YgoMp.Core.Arbiter;
using YgoMp.Core.Stats;
using YgoMp.Protocol;

namespace YgoMp.Tests;

public class FrameTests
{
    [Fact]
    public void Encode_MatchesP2PCppLayout()
    {
        // P2P.cpp EncodeFrame: kind, u32 seq, u32 seat, u16 eventType, u32 length, payload - little-endian.
        var bytes = new Frame(FrameKind.EventReport, 0x01020304, 1, 0x0007, new byte[] { 0xAA, 0xBB }).Encode();
        Assert.Equal(new byte[] { 2, 4, 3, 2, 1, 1, 0, 0, 0, 7, 0, 2, 0, 0, 0, 0xAA, 0xBB }, bytes);
        Assert.True(Frame.TryDecode(bytes, out var frame));
        Assert.Equal(0x01020304u, frame.Sequence);
        Assert.Equal((ushort)7, frame.EventType);
        Assert.Equal(new byte[] { 0xAA, 0xBB }, frame.Payload.ToArray());
    }

    [Theory]
    [InlineData(new byte[] { 1, 0, 0 })]                                             // too short
    [InlineData(new byte[] { 9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })]          // unknown kind
    [InlineData(new byte[] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0, 0, 0 })]          // length says 5, none present
    public void TryDecode_RejectsMalformed(byte[] bytes) => Assert.False(Frame.TryDecode(bytes, out _));
}

public class ArbiterTests
{
    private static Frame Action(uint seq, uint claimedSeat = 99) => new(FrameKind.Action, seq, claimedSeat, 0, new byte[] { 1 });

    private static Frame Hash(uint seq, uint eventIndex, ulong hash)
    {
        var payload = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, eventIndex);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(4), hash);
        return new(FrameKind.StateHash, seq, 0, 0, payload);
    }

    private static Frame Result(uint seq, DuelOutcome outcome, byte winner) => new(FrameKind.DuelResult, seq, 0, 0, new byte[] { (byte)outcome, winner, 1 });

    [Fact]
    public void StampsSeat_FromConnection_NotFromClient()
    {
        var arbiter = new DuelArbiter(2);
        var decision = arbiter.Accept(1, Action(1, claimedSeat: 0));
        Assert.Equal(FrameVerdict.Relay, decision.Verdict);
        Assert.Equal(1u, decision.Frame.Seat);
    }

    [Fact]
    public void DropsReplayedOrOutOfOrderSequence()
    {
        var arbiter = new DuelArbiter(2);
        Assert.Equal(FrameVerdict.Relay, arbiter.Accept(0, Action(5)).Verdict);
        Assert.Equal(FrameVerdict.Drop, arbiter.Accept(0, Action(5)).Verdict);
        Assert.Equal(FrameVerdict.Drop, arbiter.Accept(0, Action(4)).Verdict);
        Assert.Equal(FrameVerdict.Relay, arbiter.Accept(1, Action(1)).Verdict);   // sequences are per seat
        Assert.Equal(2, arbiter.ViolationCount);
    }

    [Fact]
    public void AgreedResult_NoDesync_IsAgreed()
    {
        var arbiter = new DuelArbiter(2);
        arbiter.Accept(0, Hash(1, 10, 0xABC));
        arbiter.Accept(1, Hash(1, 10, 0xABC));
        arbiter.Accept(0, Result(2, DuelOutcome.Win, 1));
        arbiter.Accept(1, Result(2, DuelOutcome.Win, 1));
        var (status, result) = arbiter.Conclude();
        Assert.Equal(DuelStatus.Agreed, status);
        Assert.Equal((byte)1, result!.Value.WinnerSeat);
    }

    [Fact]
    public void HashMismatch_IsDesynced()
    {
        var arbiter = new DuelArbiter(2);
        arbiter.Accept(0, Hash(1, 10, 1));
        arbiter.Accept(1, Hash(1, 10, 2));
        arbiter.Accept(0, Result(2, DuelOutcome.Win, 0));
        arbiter.Accept(1, Result(2, DuelOutcome.Win, 0));
        Assert.Equal(DuelStatus.Desynced, arbiter.Conclude().Status);
        Assert.Equal(10u, arbiter.FirstDesyncEvent);
    }

    [Fact]
    public void DifferentResults_IsDisputed_MissingResult_IsAbandoned()
    {
        var disputed = new DuelArbiter(2);
        disputed.Accept(0, Result(1, DuelOutcome.Win, 0));
        disputed.Accept(1, Result(1, DuelOutcome.Win, 1));
        Assert.Equal(DuelStatus.Disputed, disputed.Conclude().Status);

        var abandoned = new DuelArbiter(2);
        abandoned.Accept(0, Result(1, DuelOutcome.Win, 0));
        Assert.Equal(DuelStatus.Abandoned, abandoned.Conclude().Status);
    }
}

public class StatsTests
{
    private const string Log = """
        [12:30:07] DUEL_BEGIN id=x date=2026-10-14T12:30:07 mode=Online online=1 tag=0 localSeat=0 localSide=0 startSide=0 rng=0x1234
        [12:30:07] SEAT seat=0 name="Yugi Muto"
        [12:30:07] CARDS side=0 zone=deck cards=82:4007,83:5000
        [12:30:07] CARDS side=1 zone=deck cards=90:4007
        MOVE slot=82 kind=9 from=0x1E to=0x1A
        MOVE slot=90 kind=9 from=0x1F to=0x1B
        MOVE slot=83 kind=99 from=0x1E to=0x1A
        LP side=1 kind=damage amount=3000 lp=8000/5000
        LP side=1 kind=damage amount=1000 lp=8000/4000
        CHAIN side=0 len=3
        DUEL_END outcome=1 winner=0 reason=1 events=10
        """;

    [Fact]
    public void Extract_CountsPlaysByCardProperties_DamageAndResults()
    {
        var catalog = new CardCatalog { Cards = { [4007] = new CardInfo { Kind = "monster", Race = "dragon", Archetypes = ["blue-eyes"] } } };
        var rules = new StatsRules { MoveKinds = { ["9"] = "summon" } };
        var ended = new DateTimeOffset(2026, 10, 14, 12, 40, 0, TimeSpan.Zero);
        var duel = new DuelRecord("d1", 2, 1, null, ended, ended, DuelStatus.Agreed, DuelOutcome.Win, 0, 1,
            [new DuelSeatRecord(0, "alice", null), new DuelSeatRecord(1, "bob", null)], null);

        var stats = new StatExtractor(catalog, rules).Extract(duel, DuelLog.Parse(Log))
            .Where(s => s.Period == "all")
            .ToDictionary(s => (s.PlayerId, s.Stat), s => s.Value);

        Assert.Equal(1, stats[("alice", "summon:race=dragon")]);
        Assert.Equal(1, stats[("bob", "summon:archetype=blue-eyes")]);
        Assert.Equal(1, stats[("alice", "played:any")]);                   // kind 99 isn't mapped, so not counted
        Assert.Equal(4000, stats[("alice", "damage:total")]);
        Assert.Equal(3000, stats[("alice", "damage:max")]);
        Assert.Equal(3, stats[("alice", "chain:longest")]);
        Assert.Equal(1, stats[("alice", "duels:won")]);
        Assert.Equal(1, stats[("bob", "duels:lost")]);
        Assert.False(stats.ContainsKey(("bob", "damage:total")));
    }

    [Fact]
    public void Periods_AreAllMonthAndIsoWeek()
    {
        Assert.Equal(["all", "2026-10", "2026-W42"], Periods.For(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero)));
    }
}
