using System.Text.Json;
using YgoMp.Core.Stats;

namespace YgoMp.Server;

// The ban list this server plays with, from <data>/banlist.json, in the same format as Yu-Gi-Oh-Ex\banlists\*.json
// (BanList.h; WolfX Forbidden & Limited > Save as MP list):
//   { "name": "...", "forbidden": [Konami ids], "limited": [...], "semiLimited": [...] }   every other card at 3
// No file = "game": clients keep the game's own list. {"mode": "off"} = every card at 3.
// Re-read when the file changes, so a host can swap lists without a restart (clients pick it up on their next connect).
public sealed class ServerBanList(string path)
{
    private readonly object m_Lock = new();
    private DateTime m_LoadedStamp;
    private BanListInfo m_Current = BanListInfo.Game;

    public BanListInfo Current()
    {
        lock (m_Lock)
        {
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (stamp == m_LoadedStamp)
                return m_Current;
            m_LoadedStamp = stamp;
            try
            {
                m_Current = stamp == DateTime.MinValue
                    ? BanListInfo.Game
                    : (JsonSerializer.Deserialize<BanListInfo>(File.ReadAllText(path), StatsJson.Options) ?? BanListInfo.Game).Normalized();
            }
            catch (JsonException)
            {
                m_Current = BanListInfo.Game;   // a broken file must not take the server down; clients keep their list
            }
            return m_Current;
        }
    }
}

public sealed class BanListInfo
{
    public static readonly BanListInfo Game = new() { Mode = "game", Name = "Game list" };

    public string Mode { get; set; } = "custom";   // game / off / custom
    public string Name { get; set; } = "Server list";
    public ushort[] Forbidden { get; set; } = [];
    public ushort[] Limited { get; set; } = [];
    public ushort[] SemiLimited { get; set; } = [];

    public BanListInfo Normalized()
    {
        if (Mode is not ("game" or "off" or "custom"))
            Mode = "custom";
        return this;
    }
}
