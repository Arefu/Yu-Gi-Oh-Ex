using System.Buffers.Binary;
using YgoMp.Protocol;

namespace YgoMp.Core.Arbiter;

public enum FrameVerdict
{
    Relay,    // forward to the other seats (Action)
    Record,   // keep, don't forward (EventReport / StateHash / DuelResult)
    Drop,     // protocol violation, see Reason
}

public readonly record struct ArbiterDecision(FrameVerdict Verdict, Frame Frame, string? Reason = null);

// The referee for one duel session. It can't run the rules (the engine is inside the game exe and both clients
// simulate the duel in lockstep - docs/MultiplayerSystem.md), so it arbitrates what it CAN own:
//   - identity: a frame's seat is the seat the socket's token owns, never what the client wrote
//   - ordering: Sequence must strictly increase per seat (anti-replay / anti-injection)
//   - agreement: every seat's state hash at the same engine event index must match (desync = flagged)
//   - outcome: the result counts only when every seat reports the same one
// Not thread-safe on its own; the relay room serializes calls.
public sealed class DuelArbiter
{
    private readonly long[] m_LastSequence;
    private readonly DuelResultPayload?[] m_Results;
    private readonly Dictionary<uint, ulong?[]> m_PendingHashes = new();

    public DuelArbiter(int seatCount)
    {
        SeatCount = seatCount;
        m_LastSequence = Enumerable.Repeat(-1L, seatCount).ToArray();
        m_Results = new DuelResultPayload?[seatCount];
    }

    public int SeatCount { get; }
    public uint? FirstDesyncEvent { get; private set; }
    public int ViolationCount { get; private set; }
    public IReadOnlyList<DuelResultPayload?> Results => m_Results;

    public ArbiterDecision Accept(int seat, Frame frame)
    {
        if (seat < 0 || seat >= SeatCount)
            return Violation(frame, "seat out of range");
        if (frame.Kind == FrameKind.ServerNotice)
            return Violation(frame, "clients can't send ServerNotice");
        if (frame.Sequence <= m_LastSequence[seat])
            return Violation(frame, $"sequence {frame.Sequence} not after {m_LastSequence[seat]}");

        m_LastSequence[seat] = frame.Sequence;
        frame = frame with { Seat = (uint)seat };

        switch (frame.Kind)
        {
            case FrameKind.Action:
                return new(FrameVerdict.Relay, frame);

            case FrameKind.StateHash:
                if (frame.Payload.Length < 12)
                    return Violation(frame, "short StateHash");
                AddHash(seat, BinaryPrimitives.ReadUInt32LittleEndian(frame.Payload.Span), BinaryPrimitives.ReadUInt64LittleEndian(frame.Payload.Span[4..]));
                return new(FrameVerdict.Record, frame);

            case FrameKind.DuelResult:
                if (!DuelResultPayload.TryDecode(frame.Payload.Span, out var result))
                    return Violation(frame, "bad DuelResult");
                if (m_Results[seat] is { } previous && previous != result)
                    return Violation(frame, "seat changed its reported result");
                m_Results[seat] = result;
                return new(FrameVerdict.Record, frame);

            default:
                return new(FrameVerdict.Record, frame);
        }
    }

    public bool AllResultsIn => m_Results.All(r => r is not null);

    public (DuelStatus Status, DuelResultPayload? Result) Conclude()
    {
        if (!AllResultsIn)
            return (DuelStatus.Abandoned, m_Results.FirstOrDefault(r => r is not null));
        var first = m_Results[0]!.Value;
        if (m_Results.Any(r => r!.Value != first))
            return (DuelStatus.Disputed, null);
        if (FirstDesyncEvent is not null)
            return (DuelStatus.Desynced, first);
        return (DuelStatus.Agreed, first);
    }

    private void AddHash(int seat, uint eventIndex, ulong hash)
    {
        if (!m_PendingHashes.TryGetValue(eventIndex, out var hashes))
            m_PendingHashes[eventIndex] = hashes = new ulong?[SeatCount];
        hashes[seat] = hash;
        if (hashes.Any(h => h is null))
            return;

        m_PendingHashes.Remove(eventIndex);
        if (hashes.Distinct().Count() > 1 && (FirstDesyncEvent is null || eventIndex < FirstDesyncEvent))
            FirstDesyncEvent = eventIndex;
    }

    private ArbiterDecision Violation(Frame frame, string reason)
    {
        ViolationCount++;
        return new(FrameVerdict.Drop, frame, reason);
    }
}
