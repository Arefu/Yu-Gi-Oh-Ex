using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using YgoMp.Core.Arbiter;
using YgoMp.Core.Stats;
using YgoMp.Protocol;

namespace YgoMp.Server;

// One live duel: the sockets of its seats plus its arbiter. Every inbound frame goes through the arbiter under one
// lock, so ordering is a single total order the server stamps - no peer-to-peer injection path exists.
public sealed class RelayRoom(Core.Lobby.Lobby lobby, ILogger logger)
{
    private readonly object m_Lock = new();
    private readonly WebSocket?[] m_Sockets = new WebSocket?[lobby.SeatCount];
    private readonly SemaphoreSlim[] m_SendLocks = Enumerable.Range(0, lobby.SeatCount).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public Core.Lobby.Lobby Lobby => lobby;
    public DuelArbiter Arbiter { get; } = new(lobby.SeatCount);
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
    public bool EverConnected { get; private set; }

    public bool AnyConnected { get { lock (m_Lock) return m_Sockets.Any(s => s is not null); } }

    // Runs until the socket closes. Returns when this seat disconnects.
    public async Task RunSeatAsync(int seat, WebSocket socket, CancellationToken ct)
    {
        WebSocket? replaced;
        lock (m_Lock)
        {
            replaced = m_Sockets[seat];
            m_Sockets[seat] = socket;
            EverConnected = true;
        }
        if (replaced is not null)
            replaced.Abort();   // reconnect: the newest socket for a seat wins

        await NoticeOthersAsync(seat, new { type = "seatConnected", seat }, ct);

        var buffer = new byte[Frame.HeaderSize + Frame.MaxPayload];
        try
        {
            while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                int length = 0;
                ValueWebSocketReceiveResult result;
                do
                {
                    if (length == buffer.Length)
                        throw new InvalidDataException("frame too large");
                    result = await socket.ReceiveAsync(buffer.AsMemory(length), ct);
                    length += result.Count;
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                    break;
                }
                if (result.MessageType != WebSocketMessageType.Binary || !Frame.TryDecode(buffer.AsMemory(0, length).ToArray(), out var frame))
                {
                    logger.LogWarning("Duel {Duel} seat {Seat}: malformed message ({Length} bytes)", lobby.SessionId, seat, length);
                    continue;
                }

                ArbiterDecision decision;
                lock (m_Lock)
                    decision = Arbiter.Accept(seat, frame);

                switch (decision.Verdict)
                {
                    case FrameVerdict.Relay:
                        await SendToOthersAsync(seat, decision.Frame.Encode(), ct);
                        break;
                    case FrameVerdict.Drop:
                        logger.LogWarning("Duel {Duel} seat {Seat}: dropped {Kind} - {Reason}", lobby.SessionId, seat, frame.Kind, decision.Reason);
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or InvalidDataException)
        {
            logger.LogInformation("Duel {Duel} seat {Seat}: socket ended ({Message})", lobby.SessionId, seat, ex.Message);
        }
        finally
        {
            lock (m_Lock)
                if (m_Sockets[seat] == socket)
                    m_Sockets[seat] = null;
        }
        await NoticeOthersAsync(seat, new { type = "seatDisconnected", seat }, CancellationToken.None);
    }

    private Task NoticeOthersAsync(int fromSeat, object notice, CancellationToken ct) =>
        SendToOthersAsync(fromSeat, new Frame(FrameKind.ServerNotice, 0, (uint)fromSeat, 0, Encoding.UTF8.GetBytes(JsonSerializer.Serialize(notice))).Encode(), ct);

    private async Task SendToOthersAsync(int fromSeat, byte[] bytes, CancellationToken ct)
    {
        for (int seat = 0; seat < m_Sockets.Length; seat++)
        {
            if (seat == fromSeat)
                continue;
            WebSocket? socket;
            lock (m_Lock)
                socket = m_Sockets[seat];
            if (socket is not { State: WebSocketState.Open })
                continue;

            await m_SendLocks[seat].WaitAsync(ct);
            try
            {
                await socket.SendAsync(bytes, WebSocketMessageType.Binary, true, ct);
            }
            catch (WebSocketException)
            {
                // that seat's own receive loop notices and cleans up
            }
            finally
            {
                m_SendLocks[seat].Release();
            }
        }
    }
}

public sealed class RelayRegistry(Core.Lobby.LobbyService lobbies, DuelService duels, ILogger<RelayRegistry> logger)
{
    private readonly ConcurrentDictionary<string, RelayRoom> m_Rooms = new();

    public RelayRoom GetOrCreate(Core.Lobby.Lobby lobby) => m_Rooms.GetOrAdd(lobby.SessionId, _ => new RelayRoom(lobby, logger));

    // Called after a seat disconnects. A room closes once every seat has reported a result, or once nobody is
    // connected (then the duel is concluded as whatever the arbiter has: usually Abandoned).
    public async Task OnSeatLeftAsync(RelayRoom room)
    {
        if (room.AnyConnected && !room.Arbiter.AllResultsIn)
            return;
        if (!m_Rooms.TryRemove(room.Lobby.SessionId, out _))
            return;

        lobbies.Close(room.Lobby);
        var duel = await duels.ConcludeAsync(room.Lobby, room.Arbiter, room.StartedAt, CancellationToken.None);
        logger.LogInformation("Duel {Duel} concluded: {Status} ({Notes})", duel.Id, duel.Status, duel.Notes ?? "no notes");
    }
}
