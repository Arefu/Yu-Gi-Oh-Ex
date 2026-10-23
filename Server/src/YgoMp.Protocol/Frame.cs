using System.Buffers.Binary;

namespace YgoMp.Protocol;

// One WebSocket binary message on /relay/{sessionId} = one frame. Must stay byte-identical to EncodeFrame in
// Yu-Gi-Oh-MP/P2P.cpp. All integers little-endian.
//
//   byte    Kind
//   uint32  Sequence      strictly increasing per seat per session (the arbiter's anti-replay check)
//   uint32  Seat          what the client claims; the server overwrites it with the seat its token owns
//   uint16  EventType     the game's channel-3 event word for EventReport, 0 otherwise
//   uint32  PayloadLength
//   byte[]  Payload
public enum FrameKind : byte
{
    Action = 1,       // opaque engine bytes, relayed to the other seats (P2P.cpp sends + receives these)
    EventReport = 2,  // one channel-3 event mirrored to the server for tracking, never relayed
    StateHash = 3,    // payload = uint32 engine event index + uint64 AntiCheat::ComputeStateHash (client side not built yet)
    DuelResult = 4,   // payload = DuelResultPayload (client side not built yet)
    ServerNotice = 5, // server -> client, payload = UTF-8 JSON (desync, opponent left, ...). P2P.cpp ignores it today.
}

public readonly record struct Frame(FrameKind Kind, uint Sequence, uint Seat, ushort EventType, ReadOnlyMemory<byte> Payload)
{
    public const int HeaderSize = 15;
    public const int MaxPayload = 64 * 1024;

    public static bool TryDecode(ReadOnlyMemory<byte> data, out Frame frame)
    {
        frame = default;
        var span = data.Span;
        if (span.Length < HeaderSize)
            return false;

        var kind = (FrameKind)span[0];
        if (!Enum.IsDefined(kind))
            return false;

        uint length = BinaryPrimitives.ReadUInt32LittleEndian(span[11..]);
        if (length > MaxPayload || HeaderSize + length != (uint)span.Length)
            return false;

        frame = new Frame(kind,
            BinaryPrimitives.ReadUInt32LittleEndian(span[1..]),
            BinaryPrimitives.ReadUInt32LittleEndian(span[5..]),
            BinaryPrimitives.ReadUInt16LittleEndian(span[9..]),
            data.Slice(HeaderSize, (int)length));
        return true;
    }

    public byte[] Encode()
    {
        var bytes = new byte[HeaderSize + Payload.Length];
        bytes[0] = (byte)Kind;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(1), Sequence);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(5), Seat);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(9), EventType);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(11), (uint)Payload.Length);
        Payload.Span.CopyTo(bytes.AsSpan(HeaderSize));
        return bytes;
    }
}

// DuelResult payload: byte Outcome (DuelOutcome), byte WinnerSeat (0xFF = none), byte Reason (the game's win reason).
public readonly record struct DuelResultPayload(DuelOutcome Outcome, byte WinnerSeat, byte Reason)
{
    public static bool TryDecode(ReadOnlySpan<byte> payload, out DuelResultPayload result)
    {
        result = default;
        if (payload.Length < 3 || !Enum.IsDefined((DuelOutcome)payload[0]))
            return false;
        result = new DuelResultPayload((DuelOutcome)payload[0], payload[1], payload[2]);
        return true;
    }
}

public enum DuelOutcome : byte
{
    Win = 1,
    Draw = 2,
    Aborted = 3,
}
