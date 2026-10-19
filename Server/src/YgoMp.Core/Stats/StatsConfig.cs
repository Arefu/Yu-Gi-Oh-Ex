using System.Text.Json;
using System.Text.Json.Serialization;

namespace YgoMp.Core.Stats;

// card-catalog.json: what the server knows about each card id so "Dragon-type summoned" etc. can be counted.
// To be exported from the game data by WolfX (CARD_Props + genres + archetypes) plus custom cards from cards.json.
// Values are free-form lowercase words; every non-empty one becomes a stat dimension.
public sealed class CardInfo
{
    public string? Kind { get; set; }        // monster / spell / trap
    public string? Frame { get; set; }       // normal / effect / fusion / ritual / synchro / xyz / link / ...
    public string? Attribute { get; set; }
    public string? Race { get; set; }        // dragon / warrior / ...
    public int? Level { get; set; }
    public string[] Archetypes { get; set; } = [];
}

public sealed class CardCatalog
{
    public Dictionary<int, CardInfo> Cards { get; set; } = new();

    public CardInfo? Get(int id) => Cards.GetValueOrDefault(id);
}

// stats.json: how DuelRecorder MOVE kinds map to play categories, plus the anti-farming rules.
public sealed class StatsRules
{
    // Decoded 2026-10-14 from 763 MOVE lines of a real Duels.log (kind vs from/to zone, DuelIt.Zones):
    //   2 Hand->Monster = Normal Summon         3 Hand/Extra/Deck/GY->Monster = Special Summon
    //   4 Hand->S/T = Spell/Trap activated       5 Hand->Monster or S/T = Set
    //   6/7/8 Monster->GY = left the field       9 Deck->Hand = draw        10 Hand->GY = discard
    //   12 S/T->GY = Spell/Trap sent to the GY   0 = everything else (returns, banishes, ...)
    // Not seen as a MOVE: activating a card that was Set (it flips in place), so those activations aren't counted yet.
    public static Dictionary<string, string> DefaultMoveKinds() => new()
    {
        ["2"] = "summon",
        ["3"] = "special_summon",
        ["4"] = "activate",
        ["5"] = "set",
        ["6"] = "field_to_grave",
        ["7"] = "field_to_grave",
        ["8"] = "field_to_grave",
        ["9"] = "draw",
        ["10"] = "discard",
        ["12"] = "spelltrap_to_grave",
    };

    // MOVE kind -> category. Empty in stats.json = DefaultMoveKinds().
    public Dictionary<string, string> MoveKinds { get; set; } = DefaultMoveKinds();

    public string? CategoryOf(string kind)
    {
        var map = MoveKinds.Count > 0 ? MoveKinds : DefaultMoveKinds();
        return map.GetValueOrDefault(kind);
    }

    // Categories that also count toward summoned:* (Normal + Special).
    public string[] SummonCategories { get; set; } = ["summon", "special_summon"];

    // Categories that also count toward played:* (the "total cards played" family).
    public string[] PlayedCategories { get; set; } = ["summon", "special_summon", "set", "activate"];

    // Duels shorter than this many logged events don't count (anti-farming).
    public int MinEvents { get; set; } = 20;

    // At most this many counted duels per day against the same opponent (anti-farming).
    public int MaxDailyDuelsVsSameOpponent { get; set; } = 5;

    // Count duels vs the AI (DuelStatus.Local) toward stats and boards. On for testing; turn it off on a public server
    // (nobody else saw those duels, so they're trivially farmable).
    public bool CountLocalDuels { get; set; } = true;
}

// boards.json
public sealed class BoardDefinition
{
    public required string Id { get; set; }
    public required string Title { get; set; }
    public required string Stat { get; set; }
    public string Order { get; set; } = "desc";
    public string[] Periods { get; set; } = ["all", "month", "week"];

    [JsonIgnore]
    public bool Descending => !string.Equals(Order, "asc", StringComparison.OrdinalIgnoreCase);
}

public static class StatsJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static T LoadOrDefault<T>(string path, Func<T> fallback)
    {
        if (!File.Exists(path))
        {
            var value = fallback();
            File.WriteAllText(path, JsonSerializer.Serialize(value, Options));
            return value;
        }
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options) ?? fallback();
    }
}
