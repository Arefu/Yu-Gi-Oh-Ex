namespace YgoMp.Core.Stats;

// Parses one duel out of the DuelRecorder text format (Yu-Gi-Oh-MP/DuelRecorder.h): "KEYWORD key=value key="quoted" ...",
// optionally prefixed with the split-log "[timestamp] ". Unknown keywords are kept as plain lines and ignored.
public sealed class DuelLog
{
    public Dictionary<string, string> Begin { get; } = new();
    public Dictionary<int, int> SlotToCard { get; } = new();          // from CARDS slot:id lists
    public List<DuelLogLine> Lines { get; } = new();
    public Dictionary<string, string>? End { get; private set; }

    public int LocalSeat => Int(Begin, "localSeat") ?? 0;
    public int LocalSide => Int(Begin, "localSide") ?? 0;

    public static DuelLog Parse(string text)
    {
        var log = new DuelLog();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            int bracket = line.StartsWith('[') ? line.IndexOf("] ", StringComparison.Ordinal) : -1;
            if (bracket >= 0)
                line = line[(bracket + 2)..];
            if (line.Length == 0)
                continue;

            int space = line.IndexOf(' ');
            var keyword = space < 0 ? line : line[..space];
            var fields = ParseFields(space < 0 ? "" : line[(space + 1)..]);
            switch (keyword)
            {
                case "DUEL_BEGIN":
                    foreach (var (k, v) in fields)
                        log.Begin[k] = v;
                    break;
                case "CARDS":
                    if (fields.TryGetValue("cards", out var cards))
                        foreach (var pair in cards.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var parts = pair.Split(':');
                            if (parts.Length == 2 && int.TryParse(parts[0], out int slot) && int.TryParse(parts[1], out int id))
                                log.SlotToCard[slot] = id;
                        }
                    break;
                case "DUEL_END":
                    log.End = fields;
                    break;
            }
            log.Lines.Add(new DuelLogLine(keyword, fields));
        }
        return log;
    }

    public static int? Int(IReadOnlyDictionary<string, string> fields, string key)
    {
        if (!fields.TryGetValue(key, out var value))
            return null;
        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value[2..], System.Globalization.NumberStyles.HexNumber, null, out int hex) ? hex : null;
        return int.TryParse(value, out int n) ? n : null;
    }

    private static Dictionary<string, string> ParseFields(string text)
    {
        var fields = new Dictionary<string, string>();
        int i = 0;
        while (i < text.Length)
        {
            while (i < text.Length && text[i] == ' ')
                i++;
            int eq = text.IndexOf('=', i);
            if (eq < 0)
                break;
            var key = text[i..eq];
            i = eq + 1;
            string value;
            if (i < text.Length && text[i] == '"')
            {
                int close = text.IndexOf('"', i + 1);
                if (close < 0)
                    close = text.Length;
                value = text[(i + 1)..close];
                i = close + 1;
            }
            else
            {
                int end = text.IndexOf(' ', i);
                if (end < 0)
                    end = text.Length;
                value = text[i..end];
                i = end;
            }
            fields[key] = value;
        }
        return fields;
    }
}

public sealed record DuelLogLine(string Keyword, Dictionary<string, string> Fields);
