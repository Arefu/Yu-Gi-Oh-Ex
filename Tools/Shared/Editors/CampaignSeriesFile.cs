using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// Yu-Gi-Oh-Ex\campaigns.json: campaign series past the game's 6, read by Yu-Gi-Oh-Campaign (Series.cpp, docs/CampaignSeries.md).
    /// <code>
    /// { "series": [ { "id": 6, "name": { "E": "TEST" }, "logo": 1, "unlock": "start" } ] }
    /// </code>
    /// "logo": 1-6 = a game series' logo, or a name = the "logo_&lt;name&gt;" sprite of pdui\SeriesLogo.dfymoo (a new logo added to that sheet
    /// in the Ex patch; the game's are classic, gx, 5ds, zexal, arcv, vrains).
    /// "unlock": "start" (the default: open from the start), "never", or { "after": n } = once the LAST duel of series n (a game series 0-5
    /// or another new one) is won. A series' duels are storyduels.json duels with its id as "series" (the Story editor makes them).
    /// </summary>
    public sealed class CampaignSeriesFile
    {
        public const string FileName = "campaigns.json";
        public const int FirstId = 6;
        public const int LastId = 63;
        public const int Never = -1;
        public const int FromStart = -2;

        public sealed class Series
        {
            public int Id;
            public Dictionary<char, string> Names = [];
            public int Logo = 1;                  // the game series 1-6 whose logo it shows (when LogoKey is empty)
            public string LogoKey = "";           // or a "logo_<key>" sprite of pdui\SeriesLogo.dfymoo (a new logo in the patched sheet)
            public int UnlockAfter = FromStart;   // FromStart, Never, or the series whose last duel opens it

            public string Name(char language = 'E') =>
                Names.TryGetValue(language, out var name) && name.Length > 0 ? name : Names.TryGetValue('E', out var english) && english.Length > 0 ? english : $"Series {Id + 1}";
        }

        public List<Series> Entries { get; } = [];

        /// <summary>"hiddenSeries": the game's series (0-5) "deleted": left out of the series list by Yu-Gi-Oh-Campaign.</summary>
        public SortedSet<int> Hidden { get; } = [];

        /// <summary>The game's series logo keys, in series order ("logo_&lt;key&gt;" in pdui\SeriesLogo.dfymoo).</summary>
        public static readonly string[] GameLogoKeys = ["classic", "gx", "5ds", "zexal", "arcv", "vrains"];

        public const string LogoSheet = @"pdui\SeriesLogo.dfymoo";

        /// <summary>Every "logo_&lt;key&gt;" sprite in the sheet as the game reads it now (a patched sheet's new logos included).</summary>
        public static List<string> LogoKeys(GameFolderFiles files)
        {
            var keys = new List<string>();
            if (files.Read(LogoSheet) is { } data)
                foreach (string line in Encoding.ASCII.GetString(data).Split('\n'))
                    if (line.StartsWith("n logo_"))
                        keys.Add(line[7..].Trim());
            return keys.Count > 0 ? keys : [.. GameLogoKeys];
        }

        /// <summary>The file the editors last read or saved (the Story editor names new series from it).</summary>
        public static CampaignSeriesFile Current { get; private set; } = new();

        /// <summary>Raised when the Campaign series page saves, so the Story editor's series lists follow.</summary>
        public static event Action? Saved;

        /// <summary>A series' name: the game's six, a new one from <see cref="Current"/>, else "series n".</summary>
        public static string NameOf(int series, char language = 'E') =>
            series >= 0 && series < StoryDuelTable.SeriesNames.Length ? StoryDuelTable.SeriesNames[series]
            : Current.Entries.FirstOrDefault(s => s.Id == series) is Series s ? s.Name(language) : $"series {series}";

        /// <summary>The game's six and the new ones, in list order.</summary>
        public static IReadOnlyList<int> AllSeries() => [.. Enumerable.Range(0, StoryDuelTable.SeriesNames.Length), .. Current.Entries.Select(s => s.Id)];

        public static CampaignSeriesFile Load(string path)
        {
            var file = new CampaignSeriesFile();
            if (File.Exists(path))
            {
                var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (root?["hiddenSeries"] is JsonArray hidden)
                    foreach (var value in hidden.OfType<JsonValue>())
                        if (value.TryGetValue(out int series) && series >= 0 && series < FirstId)
                            file.Hidden.Add(series);
                if (root?["series"] is JsonArray array)
                    foreach (var node in array.OfType<JsonObject>())
                    {
                        if (node["id"]?.GetValue<int>() is not int id)
                            continue;
                        var series = new Series { Id = id, Logo = node["logo"] is JsonValue logo && logo.TryGetValue<int>(out int l) ? l : 1 };
                        if (node["logo"] is JsonValue key && key.TryGetValue<string>(out var logoKey))
                            series.LogoKey = logoKey;
                        if (node["name"] is JsonValue single && single.TryGetValue<string>(out var text))
                            series.Names['E'] = text;
                        else if (node["name"] is JsonObject names)
                            foreach (var (language, value) in names)
                                if (language.Length > 0 && value is JsonValue v && v.TryGetValue<string>(out var t))
                                    series.Names[char.ToUpperInvariant(language[0])] = t;
                        series.UnlockAfter = node["unlock"] switch
                        {
                            JsonValue rule when rule.TryGetValue<string>(out var word) && word == "never" => Never,
                            JsonObject after when after["after"] is JsonValue n && n.TryGetValue<int>(out int number) => number,
                            _ => FromStart,
                        };
                        file.Entries.Add(series);
                    }
            }
            Current = file;
            return file;
        }

        /// <summary>Problems the plugin would skip a series for (empty = fine).</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            foreach (var s in Entries)
            {
                if (s.Id < FirstId || s.Id > LastId)
                    problems.Add($"Series {s.Id}: the number must be {FirstId}-{LastId} (0-5 are the game's).");
                if (s.UnlockAfter == s.Id)
                    problems.Add($"{s.Name()}: it can't wait for its own last duel.");
            }
            foreach (var group in Entries.GroupBy(s => s.Id).Where(g => g.Count() > 1))
                problems.Add($"Series number {group.Key} is used {group.Count()} times.");
            return problems;
        }

        /// <summary>Writes the file (deleted when there are no new series) and makes it <see cref="Current"/>.</summary>
        public void Save(string path)
        {
            Current = this;
            if (Entries.Count == 0 && Hidden.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                Saved?.Invoke();
                return;
            }
            var array = new JsonArray();
            foreach (var s in Entries)
            {
                var names = new JsonObject();
                foreach (var (language, name) in s.Names.OrderBy(n => n.Key))
                    if (name.Trim().Length > 0)
                        names[language.ToString()] = name;
                var node = new JsonObject { ["id"] = s.Id, ["name"] = names };
                node["logo"] = s.LogoKey.Length > 0 ? s.LogoKey : Math.Clamp(s.Logo, 1, 6);
                node["unlock"] = s.UnlockAfter switch
                {
                    FromStart => "start",
                    Never => "never",
                    int after => new JsonObject { ["after"] = after },
                };
                array.Add(node);
            }
            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var root = new JsonObject { ["series"] = array };
            if (Hidden.Count > 0)
                root["hiddenSeries"] = new JsonArray(Hidden.Select(h => (JsonNode?)h).ToArray());
            File.WriteAllText(path, root.ToJsonString(options) + "\n", new UTF8Encoding(false));
            Saved?.Invoke();
        }
    }
}
