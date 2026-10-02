using System.Globalization;
using System.Text.RegularExpressions;

namespace DuelIt
{
    /// <summary>One line of Duels.log: its tag (MSG, MOVE, LP, ...) and key=value fields. Format: Yu-Gi-Oh-MP's DuelRecorder.h.</summary>
    public sealed class DuelEvent
    {
        public required int Line { get; init; }
        public required string Time { get; init; }
        public required string Tag { get; init; }
        public required Dictionary<string, string> Fields { get; init; }
        public required string Raw { get; init; }

        public string Get(string key, string fallback = "") => Fields.TryGetValue(key, out var value) ? value : fallback;

        /// <summary>A field as a number; accepts decimal and 0x hex.</summary>
        public int Int(string key, int fallback = 0)
        {
            if (!Fields.TryGetValue(key, out var text))
                return fallback;
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return int.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int hex) ? hex : fallback;
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;
        }

        /// <summary>"8000/7000" -> (8000, 7000).</summary>
        public (int Side0, int Side1)? LifePoints()
        {
            var parts = Get("lp").Split('/');
            if (parts.Length == 2 && int.TryParse(parts[0], out int a) && int.TryParse(parts[1], out int b))
                return (a, b);
            return null;
        }
    }

    /// <summary>Everything recorded for one duel, from DUEL_BEGIN up to the next DUEL_BEGIN (MATCH_END and STAT lines come after DUEL_END).</summary>
    public sealed class DuelRecord
    {
        public required DuelEvent Begin { get; init; }
        public List<DuelEvent> Events { get; } = [];

        public string Id => Begin.Get("id", $"line {Begin.Line}");
        public string Mode => Begin.Get("mode", "?");
        public bool Online => Begin.Int("online") != 0;
        public bool Tag => Begin.Int("tag") != 0;
        public int LocalSide => Begin.Int("localSide");
        public DuelEvent? End => Events.LastOrDefault(e => e.Tag == "DUEL_END");

        public string Summary
        {
            get
            {
                string date = Begin.Get("date").Replace('T', ' ');
                string kind = Online ? $"Online {Begin.Get("match")}" : Mode;
                if (Tag)
                    kind += " (tag)";
                // Recordings before 2026-09-30 evening have no DUEL_END in Campaign (the OnDuelEnd tap never fired there); their
                // DuelResult message (0x05) or MATCH_END still says the duel finished.
                var duelResult = Events.LastOrDefault(e => e.Tag == "MSG" && e.Int("code") == 0x05);
                string result = End != null ? $"{DuelText.Winner(End.Int("winner"), LocalSide)}, {DuelText.WinReason(End.Int("reason"))}"
                    : duelResult != null ? DuelText.DuelResult(duelResult.Int("a1"), duelResult.Int("a2"), duelResult.Int("a3"), LocalSide)
                    : Events.Any(e => e.Tag == "MATCH_END") ? "finished"
                    : "unfinished";
                return $"{date}  {kind}  -  {result}  ({Events.Count} events)";
            }
        }
    }

    public static class DuelLog
    {
        // "12:30:07.756 INFO  [Yu-Gi-Oh-MP] MSG n=1 code=0x25 ..." (the Yu-Gi-Oh-Console line format)
        private static readonly Regex LineFormat = new(@"^(\d\d:\d\d:\d\d\.\d{3}) (\w+)\s+\[([^\]]*)\] (.*)$", RegexOptions.Compiled);
        private static readonly Regex FieldFormat = new(@"(\w+)=(""([^""]*)""|\S*)", RegexOptions.Compiled);

        public static List<DuelRecord> Load(string path)
        {
            // The game may have the file open for appending; share it.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            return Parse(reader);
        }

        public static List<DuelRecord> Parse(TextReader reader)
        {
            var duels = new List<DuelRecord>();
            DuelRecord? current = null;
            int number = 0;
            string? text;
            while ((text = reader.ReadLine()) != null)
            {
                number++;
                var match = LineFormat.Match(text);
                if (!match.Success)
                    continue;

                string message = match.Groups[4].Value;
                int space = message.IndexOf(' ');
                string tag = space < 0 ? message : message[..space];
                if (tag.Length == 0 || !tag.All(c => char.IsUpper(c) || c == '_'))
                    continue;   // "session ..." lines and anything else that isn't an event

                var fields = new Dictionary<string, string>(StringComparer.Ordinal);
                if (space >= 0)
                    foreach (Match field in FieldFormat.Matches(message[(space + 1)..]))
                        fields[field.Groups[1].Value] = field.Groups[3].Success ? field.Groups[3].Value : field.Groups[2].Value;

                var duelEvent = new DuelEvent { Line = number, Time = match.Groups[1].Value, Tag = tag, Fields = fields, Raw = message };
                if (tag == "DUEL_BEGIN")
                {
                    current = new DuelRecord { Begin = duelEvent };
                    duels.Add(current);
                }
                current?.Events.Add(duelEvent);
            }
            return duels;
        }
    }
}
