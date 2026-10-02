using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Types
{
    /// <summary>
    /// One step of a story scene (scriptdata line: four UTF-8 strings; StoryScriptLine in the IDB, docs/StoryScenes.md).
    /// <list type="bullet">
    /// <item><c>Speaker</c> "command": <c>Position</c> BG (background pdui/dialog_bg/&lt;Expression&gt;), PROP_ON (prop pdui/dialog_props/&lt;Expression&gt;) or PROP_OFF.</item>
    /// <item><c>Speaker</c> "infn8": the narrator box (empty text hides it).</item>
    /// <item>Otherwise a character key: <c>Position</c> LEFT / CENTER / RIGHT / NONE (leave), with FADEIN / FADEOUT; "" = stay where they are.
    /// <c>Expression</c> "smile", "neutral", "dark_smile" (costume_expression)...; "" keeps the current one.</item>
    /// </list>
    /// </summary>
    public sealed class ScriptLine
    {
        public const string Command = "command";
        public const string Narrator = "infn8";

        public string Speaker { get; set; } = "";
        public string Position { get; set; } = "";
        public string Expression { get; set; } = "";

        /// <summary>Per language. {GAMERTAG} becomes the player's name.</summary>
        public Dictionary<char, string> Texts { get; } = [];

        public string Text(char language = 'E') =>
            Texts.TryGetValue(language, out var text) ? text : Texts.TryGetValue('E', out var english) ? english : Texts.Values.FirstOrDefault() ?? "";

        public bool IsCommand => Speaker.Equals(Command, StringComparison.OrdinalIgnoreCase);
        public bool IsNarrator => Speaker.Equals(Narrator, StringComparison.OrdinalIgnoreCase);

        public ScriptLine Clone()
        {
            var copy = new ScriptLine { Speaker = Speaker, Position = Position, Expression = Expression };
            foreach (var (language, text) in Texts) copy.Texts[language] = text;
            return copy;
        }

        /// <summary>Same step; a missing text counts as empty (the JSON leaves empty texts out).</summary>
        public bool SameAs(ScriptLine other) =>
            Speaker == other.Speaker && Position == other.Position && Expression == other.Expression &&
            Texts.Keys.Union(other.Texts.Keys).All(language => Texts.GetValueOrDefault(language, "") == other.Texts.GetValueOrDefault(language, ""));
    }

    /// <summary>One scene: a named run of lines. The game looks scenes up by name, ignoring case: &lt;duel key&gt;_INTRO, _OUTRO (won), _OUTRO_LOSE.</summary>
    public sealed class StoryScript
    {
        public string Name { get; set; } = "";
        public List<ScriptLine> Lines { get; } = [];

        public StoryScript Clone()
        {
            var copy = new StoryScript { Name = Name };
            copy.Lines.AddRange(Lines.Select(l => l.Clone()));
            return copy;
        }

        public bool SameAs(StoryScript other) =>
            Name == other.Name && Lines.Count == other.Lines.Count && Lines.Zip(other.Lines).All(p => p.First.SameAs(p.Second));
    }

    /// <summary>
    /// main/scriptdata_&lt;lang&gt;.bin, all languages together: {u64 linesOffset, u32 scriptCount, u32 lineCount}, scriptCount x
    /// {u32 firstLine, u32 lastLine (inclusive), u64 nameOffset}, the names, lineCount x {u64 speaker, position, expression, text}, then the
    /// line strings in order. All strings UTF-8, zero terminated, unshared, no padding. Scripts cover the lines in order. Every language has
    /// the same scripts and lines; only the texts differ. Loaded by LoadStoryScriptData (0x14074A290) into g_pStoryScriptFile.
    /// </summary>
    public sealed class StoryScriptTable
    {
        public static readonly char[] AllLanguages = ['E', 'F', 'G', 'I', 'J', 'S'];

        /// <summary>The scene suffixes, in the order the editor shows them.</summary>
        public static readonly string[] SceneSuffixes = ["_INTRO", "_OUTRO", "_OUTRO_LOSE"];

        public static string FileName(char language) => $"scriptdata_{char.ToUpperInvariant(language)}.bin";
        public static string GamePath(char language) => $@"main\{FileName(language)}";

        public List<StoryScript> Scripts { get; } = [];
        public List<char> Languages { get; } = [];

        public static StoryScriptTable Parse(IDictionary<char, byte[]> files)
        {
            var table = new StoryScriptTable();
            foreach (var (language, data) in files.OrderBy(f => f.Key == 'E' ? 0 : 1).ThenBy(f => f.Key))
            {
                char lang = char.ToUpperInvariant(language);
                var scripts = ParseOne(data, lang);
                if (table.Languages.Count == 0)
                    table.Scripts.AddRange(scripts);
                else
                {
                    if (scripts.Count != table.Scripts.Count)
                        throw new InvalidDataException($"{FileName(lang)} has {scripts.Count} scripts, {FileName(table.Languages[0])} has {table.Scripts.Count}.");
                    for (int i = 0; i < scripts.Count; i++)
                    {
                        var (a, b) = (table.Scripts[i], scripts[i]);
                        if (a.Name != b.Name || a.Lines.Count != b.Lines.Count)
                            throw new InvalidDataException($"Script {a.Name} differs between {FileName(table.Languages[0])} and {FileName(lang)}.");
                        for (int j = 0; j < a.Lines.Count; j++)
                        {
                            var (x, y) = (a.Lines[j], b.Lines[j]);
                            if (x.Speaker != y.Speaker || x.Position != y.Position || x.Expression != y.Expression)
                                throw new InvalidDataException($"Script {a.Name} line {j} differs between {FileName(table.Languages[0])} and {FileName(lang)} (only the text may).");
                            x.Texts[lang] = y.Texts[lang];
                        }
                    }
                }
                table.Languages.Add(lang);
            }
            return table;
        }

        private static List<StoryScript> ParseOne(byte[] data, char language)
        {
            if (data.Length < 16)
                throw new InvalidDataException("scriptdata starts with a 16-byte header.");
            long linesAt = (long)BitConverter.ToUInt64(data, 0);
            int scriptCount = BitConverter.ToInt32(data, 8), lineCount = BitConverter.ToInt32(data, 12);
            if (16 + (long)scriptCount * 16 > data.Length || linesAt + (long)lineCount * 32 > data.Length)
                throw new InvalidDataException($"scriptdata says {scriptCount} scripts / {lineCount} lines but is only {data.Length} bytes.");
            var lines = new ScriptLine[lineCount];
            for (int j = 0; j < lineCount; j++)
            {
                int at = checked((int)(linesAt + j * 32));
                var line = new ScriptLine
                {
                    Speaker = ReadUtf8(data, BitConverter.ToUInt64(data, at)),
                    Position = ReadUtf8(data, BitConverter.ToUInt64(data, at + 8)),
                    Expression = ReadUtf8(data, BitConverter.ToUInt64(data, at + 16)),
                };
                line.Texts[language] = ReadUtf8(data, BitConverter.ToUInt64(data, at + 24));
                lines[j] = line;
            }
            var scripts = new List<StoryScript>();
            int expected = 0;
            for (int i = 0; i < scriptCount; i++)
            {
                int at = 16 + i * 16;
                int first = BitConverter.ToInt32(data, at), last = BitConverter.ToInt32(data, at + 4);
                var script = new StoryScript { Name = ReadUtf8(data, BitConverter.ToUInt64(data, at + 8)) };
                if (first != expected || last < first - 1 || last >= lineCount)
                    throw new InvalidDataException($"Script {script.Name} covers lines {first}-{last}; expected them to follow on from {expected}.");
                script.Lines.AddRange(lines[first..(last + 1)]);
                expected = last + 1;
                scripts.Add(script);
            }
            if (expected != lineCount)
                throw new InvalidDataException($"scriptdata has {lineCount - expected} lines no script uses.");
            return scripts;
        }

        private static string ReadUtf8(byte[] data, ulong offset)
        {
            int start = checked((int)offset), end = Array.IndexOf(data, (byte)0, start);
            return Encoding.UTF8.GetString(data, start, (end < 0 ? data.Length : end) - start);
        }

        /// <summary>One language's file in the game's layout (a missing text uses English). An empty script can't be stored and is skipped.</summary>
        public byte[] ToBytes(char language)
        {
            language = char.ToUpperInvariant(language);
            var scripts = Scripts.Where(s => s.Lines.Count > 0).ToList();
            int lineCount = scripts.Sum(s => s.Lines.Count);
            using var output = new MemoryStream();
            var w = new BinaryWriter(output);
            var names = new MemoryStream();
            long namesAt = 16 + (long)scripts.Count * 16;
            w.Write(0UL);   // lines offset, filled in below
            w.Write(scripts.Count);
            w.Write(lineCount);
            int first = 0;
            foreach (var script in scripts)
            {
                w.Write(first);
                w.Write(first + script.Lines.Count - 1);
                w.Write((ulong)(namesAt + names.Length));
                names.Write(Encoding.UTF8.GetBytes(script.Name));
                names.WriteByte(0);
                first += script.Lines.Count;
            }
            w.Flush();
            names.Position = 0;
            names.CopyTo(output);
            long linesAt = output.Length, poolAt = linesAt + (long)lineCount * 32;
            var pool = new MemoryStream();
            foreach (var line in scripts.SelectMany(s => s.Lines))
            {
                foreach (string text in new[] { line.Speaker, line.Position, line.Expression, line.Text(language) })
                {
                    w.Write((ulong)(poolAt + pool.Length));
                    pool.Write(Encoding.UTF8.GetBytes(text));
                    pool.WriteByte(0);
                }
            }
            w.Flush();
            pool.Position = 0;
            pool.CopyTo(output);
            output.Position = 0;
            w.Write((ulong)linesAt);
            w.Flush();
            return output.ToArray();
        }

        public StoryScriptTable Clone()
        {
            var copy = new StoryScriptTable();
            copy.Languages.AddRange(Languages);
            copy.Scripts.AddRange(Scripts.Select(s => s.Clone()));
            return copy;
        }

        /// <summary>The script the game would play for this name (first match, ignoring case).</summary>
        public StoryScript? Find(string name) => Scripts.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// WolfEx's form: Yu-Gi-Oh-Ex\storyscripts.json, read by Yu-Gi-Oh-Campaign. Only scenes that differ from the game's (or are new), each whole:
    /// <code>
    /// { "scripts": [ { "name": "MyDuel_INTRO", "lines": [
    ///     { "who": "command", "position": "BG", "expression": "classic_school" },
    ///     { "who": "yugimuto", "position": "LEFT", "expression": "smile", "text": { "E": "Let's duel!" } } ] } ] }
    /// </code>
    /// A scene listed with no lines is removed.
    /// </summary>
    public static class StoryScriptJson
    {
        public const string FileName = "storyscripts.json";

        private static readonly JsonSerializerOptions WriteOptions = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static JsonObject Load(string path)
        {
            if (!File.Exists(path))
                return [];
            try
            {
                return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? [];
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{path} isn't valid JSON: {ex.Message}");
            }
        }

        public static void Save(string path, JsonObject root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(WriteOptions));
        }

        public static JsonObject ToJson(StoryScript script)
        {
            var lines = new JsonArray();
            foreach (var line in script.Lines)
            {
                var json = new JsonObject { ["who"] = line.Speaker };
                if (line.Position.Length > 0) json["position"] = line.Position;
                if (line.Expression.Length > 0) json["expression"] = line.Expression;
                if (line.Texts.Values.Any(t => t.Length > 0))
                {
                    var texts = new JsonObject();
                    foreach (var (language, text) in line.Texts.OrderBy(t => t.Key))
                        texts[language.ToString()] = text;
                    json["text"] = texts;
                }
                lines.Add(json);
            }
            return new JsonObject { ["name"] = script.Name, ["lines"] = lines };
        }

        /// <summary>Every scene that differs from <paramref name="baseline"/>, is new, or was removed (written with no lines).</summary>
        public static JsonObject Diff(StoryScriptTable baseline, StoryScriptTable table)
        {
            var array = new JsonArray();
            foreach (var script in table.Scripts)
            {
                var game = baseline.Find(script.Name);
                if (game != null && game.SameAs(script))
                    continue;
                array.Add(ToJson(script));
            }
            foreach (var game in baseline.Scripts.Where(g => table.Find(g.Name) == null))
                array.Add(new JsonObject { ["name"] = game.Name, ["lines"] = new JsonArray() });
            return new JsonObject
            {
                ["note"] = "Story scenes (main/scriptdata_<lang>.bin) that differ from the game's or are new, made by WolfEx; read by Yu-Gi-Oh-Campaign. " +
                    "name = <duel key>_INTRO, _OUTRO (after a win) or _OUTRO_LOSE. who: a character key, \"command\" (position BG / PROP_ON / PROP_OFF, " +
                    "expression = the picture) or \"infn8\" (narrator). position: LEFT, CENTER, RIGHT, NONE (+ FADEIN / FADEOUT), empty = stay. " +
                    "A scene with no lines is removed.",
                ["scripts"] = array,
            };
        }

        /// <summary>Applies the JSON onto the table (replacing, adding or removing scenes). Returns how many.</summary>
        public static int Apply(JsonObject root, StoryScriptTable table)
        {
            int applied = 0;
            foreach (var node in (root["scripts"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Str(node["name"]) is not string name || name.Length == 0)
                    continue;
                var script = new StoryScript { Name = name };
                foreach (var lineNode in (node["lines"] as JsonArray ?? []).OfType<JsonObject>())
                {
                    var line = new ScriptLine
                    {
                        Speaker = Str(lineNode["who"]) ?? "",
                        Position = Str(lineNode["position"]) ?? "",
                        Expression = Str(lineNode["expression"]) ?? "",
                    };
                    if (lineNode["text"] is JsonValue single && single.TryGetValue(out string? english))
                        line.Texts['E'] = english;
                    else if (lineNode["text"] is JsonObject languages)
                        foreach (var (language, value) in languages)
                            if (language.Length > 0 && value is JsonValue v && v.TryGetValue(out string? t))
                                line.Texts[char.ToUpperInvariant(language[0])] = t;
                    script.Lines.Add(line);
                }
                int index = table.Scripts.FindIndex(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (script.Lines.Count == 0)
                {
                    if (index >= 0)
                        table.Scripts.RemoveAt(index);
                }
                else if (index >= 0)
                    table.Scripts[index] = script;
                else
                    table.Scripts.Add(script);
                applied++;
            }
            return applied;
        }

        private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }
}
