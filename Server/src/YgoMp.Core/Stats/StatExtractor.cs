using System.Globalization;

namespace YgoMp.Core.Stats;

// Turns one finished duel (its record + one seat's DuelRecorder log) into stat increments for every period.
//
// Stat keys:  <family>:<dimension>      e.g. played:any, played:race=dragon, summon:frame=fusion, duels:won, damage:max
// Periods:    all, month (2026-10), week (2026-W42) - see Periods.
public sealed class StatExtractor(CardCatalog catalog, StatsRules rules)
{
    public IReadOnlyList<StatIncrement> Extract(DuelRecord duel, DuelLog log)
    {
        // Sides: the engine has 2 sides; in a tag duel partners share a side by seat parity (docs/MultiplayerSystem.md).
        // The log's own seat tells us which parity is which side.
        int localParity = log.LocalSeat % 2;
        IEnumerable<string> PlayersOnSide(int side)
        {
            int parity = side == log.LocalSide ? localParity : 1 - localParity;
            return duel.Seats.Where(s => s.Seat % 2 == parity).Select(s => s.PlayerId);
        }

        var counts = new Dictionary<(string Player, string Stat), (long Value, StatAggregate Agg)>();
        void Add(string player, string stat, long value, StatAggregate agg = StatAggregate.Sum)
        {
            var key = (player, stat);
            if (!counts.TryGetValue(key, out var current))
                counts[key] = (value, agg);
            else
                counts[key] = (agg switch
                {
                    StatAggregate.Max => Math.Max(current.Value, value),
                    StatAggregate.Min => Math.Min(current.Value, value),
                    _ => current.Value + value,
                }, agg);
        }

        // Per-duel results.
        foreach (var seat in duel.Seats)
        {
            Add(seat.PlayerId, "duels:played", 1);
            if (duel.Outcome == Protocol.DuelOutcome.Draw)
                Add(seat.PlayerId, "duels:drawn", 1);
            else if (duel.WinnerSeat is int winner)
                Add(seat.PlayerId, winner % 2 == seat.Seat % 2 ? "duels:won" : "duels:lost", 1);
        }

        foreach (var line in log.Lines)
        {
            switch (line.Keyword)
            {
                case "MOVE":
                {
                    if (!line.Fields.TryGetValue("kind", out var kind) || rules.CategoryOf(kind) is not { } category)
                        break;
                    if (DuelLog.Int(line.Fields, "slot") is not int slot || !log.SlotToCard.TryGetValue(slot, out int cardId))
                        break;
                    // MOVE carries no side; the card's owner is whoever had the slot in their CARDS list. Until that's
                    // split per side, attribute by "to" location's side bit (location = zone<<1 | side | index<<6).
                    if (DuelLog.Int(line.Fields, "to") is not int to)
                        break;
                    bool summon = rules.SummonCategories.Contains(category);
                    string? from = summon && DuelLog.Int(line.Fields, "from") is int f ? ZoneSource(f) : null;
                    foreach (var player in PlayersOnSide(to & 1))
                    {
                        foreach (var stat in CardStats(category, cardId))
                            Add(player, stat, 1);
                        if (summon)
                        {
                            foreach (var stat in CardStats("summoned", cardId))
                                Add(player, stat, 1);
                            if (from is not null)
                            {
                                Add(player, $"{category}:from={from}", 1);
                                Add(player, $"summoned:from={from}", 1);
                            }
                        }
                        if (rules.PlayedCategories.Contains(category))
                            foreach (var stat in CardStats("played", cardId))
                                Add(player, stat, 1);
                    }
                    break;
                }
                case "LP":
                {
                    if (line.Fields.GetValueOrDefault("kind") != "damage" || DuelLog.Int(line.Fields, "side") is not int side
                        || DuelLog.Int(line.Fields, "amount") is not int amount)
                        break;
                    // side = who took the damage; the other side dealt it.
                    foreach (var player in PlayersOnSide(1 - side))
                    {
                        Add(player, "damage:total", amount);
                        Add(player, "damage:max", amount, StatAggregate.Max);
                    }
                    break;
                }
                case "CHAIN":
                {
                    if (DuelLog.Int(line.Fields, "side") is not int side || DuelLog.Int(line.Fields, "len") is not int len)
                        break;
                    foreach (var player in PlayersOnSide(side))
                    {
                        Add(player, "chain:links", 1);
                        Add(player, "chain:longest", len, StatAggregate.Max);
                    }
                    break;
                }
            }
        }

        var periods = Periods.For(duel.EndedAt);
        return counts
            .SelectMany(kv => periods.Select(p => new StatIncrement(kv.Key.Player, kv.Key.Stat, p, kv.Value.Value, kv.Value.Agg)))
            .ToList();
    }

    // Where a summon came from (location = zone<<1 | side | index<<6; zones as DuelIt.Zones).
    public static string ZoneSource(int location) => ((location & 0x3F) >> 1) switch
    {
        13 => "hand",
        14 => "extra",
        15 => "deck",
        16 => "grave",
        17 => "banished",
        _ => "field",
    };

    public static string ZoneName(int location) => ((location & 0x3F) >> 1) switch
    {
        int z and >= 0 and <= 4 => $"Monster {z + 1}",
        5 or 6 => "Extra Monster",
        int z and >= 7 and <= 11 => $"S/T {z - 6}",
        12 => "Field",
        13 => "Hand",
        14 => "Extra Deck",
        15 => "Deck",
        16 => "GY",
        17 => "Banished",
        int z => $"zone {z}",
    };

    // family:any, family:card=<id>, and one key per known card property.
    private IEnumerable<string> CardStats(string family, int cardId)
    {
        yield return $"{family}:any";
        yield return $"{family}:card={cardId}";
        if (catalog.Get(cardId) is not { } card)
            yield break;
        if (!string.IsNullOrEmpty(card.Kind)) yield return $"{family}:kind={card.Kind}";
        if (!string.IsNullOrEmpty(card.Frame)) yield return $"{family}:frame={card.Frame}";
        if (!string.IsNullOrEmpty(card.Attribute)) yield return $"{family}:attribute={card.Attribute}";
        if (!string.IsNullOrEmpty(card.Race)) yield return $"{family}:race={card.Race}";
        if (card.Level is int level) yield return $"{family}:level={level}";
        foreach (var archetype in card.Archetypes)
            yield return $"{family}:archetype={archetype}";
    }
}

public static class Periods
{
    public const string All = "all";

    public static string[] For(DateTimeOffset at) => [All, Month(at), Week(at)];

    public static string Month(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static string Week(DateTimeOffset at) => $"{ISOWeek.GetYear(at.UtcDateTime)}-W{ISOWeek.GetWeekOfYear(at.UtcDateTime):00}";

    // "all" / "month" / "week" -> the current period key; anything else is taken as an explicit key.
    public static string Resolve(string? period, DateTimeOffset now) => period switch
    {
        null or "" or "all" => All,
        "month" => Month(now),
        "week" => Week(now),
        _ => period,
    };
}
