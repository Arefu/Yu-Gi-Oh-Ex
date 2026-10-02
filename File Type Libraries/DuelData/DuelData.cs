using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Types
{
    /// <summary>
    /// One campaign story duel (main/dueldata_&lt;lang&gt;.bin record, 0x5C bytes; StoryDuelFileRecord / StoryDuelRecord in the IDB,
    /// docs/StoryDuels.md). Side 0 is the player, side 1 the opponent (swapped in a reverse duel).
    /// </summary>
    public sealed class StoryDuel
    {
        /// <summary>1-225 (the game's table has 226 slots; 0 = empty slot).</summary>
        public int Id { get; set; }

        /// <summary>The campaign series (0 DM, 1 GX, 2 5D's, 3 ZEXAL, 4 ARC-V, 5 VRAINS).</summary>
        public int Series { get; set; }

        /// <summary>Position in the series' duel list, from 1. Order 1 is the series' tutorial duel and has no reverse duel.</summary>
        public int Order { get; set; }

        /// <summary>The characters (chardata ids): [0] player, [1] opponent.</summary>
        public int[] Characters { get; } = [0, 0];

        /// <summary>The decks (deckdata index; duels use + 32): [0] player, [1] opponent. The game marks them as story decks of this duel.</summary>
        public int[] Decks { get; } = [0, 0];

        /// <summary>Costume suffix per side: the portrait becomes "&lt;character key&gt;_&lt;costume&gt;_neutral" ("" = normal).</summary>
        public string[] Costumes { get; } = ["", ""];

        public int Arena { get; set; }

        /// <summary>Pack (packdefdata id) unlocked the first time this duel is won; -1 = none.</summary>
        public int RewardPack { get; set; } = -1;

        /// <summary>Content pack (skudata); -1 in the file = 1, the base game. The duel is listed only if its SKU is owned.</summary>
        public int Sku { get; set; } = -1;

        /// <summary>1 = not needed for "beat every story duel of a character to unlock their deck" (the two crossover duels).</summary>
        public int ExcludeFromDeckUnlock { get; set; }

        /// <summary>ASCII; names the dialog scripts &lt;key&gt;_INTRO / _OUTRO / _OUTRO_LOSE in scriptdata_&lt;lang&gt;.bin.</summary>
        public string Key { get; set; } = "";

        public Dictionary<char, string> Titles { get; } = [];
        public Dictionary<char, string> Descriptions { get; } = [];

        /// <summary>Shown after losing the duel.</summary>
        public Dictionary<char, string> Tips { get; } = [];

        public string Title(char language = 'E') => Pick(Titles, language);
        public string Description(char language = 'E') => Pick(Descriptions, language);
        public string Tip(char language = 'E') => Pick(Tips, language);

        private static string Pick(Dictionary<char, string> texts, char language) =>
            texts.TryGetValue(language, out var text) ? text : texts.TryGetValue('E', out var english) ? english : texts.Values.FirstOrDefault() ?? "";

        public StoryDuel Clone()
        {
            var fresh = new StoryDuel
            {
                Id = Id, Series = Series, Order = Order, Arena = Arena, RewardPack = RewardPack, Sku = Sku, ExcludeFromDeckUnlock = ExcludeFromDeckUnlock, Key = Key,
            };
            for (int side = 0; side < 2; side++)
            {
                fresh.Characters[side] = Characters[side];
                fresh.Decks[side] = Decks[side];
                fresh.Costumes[side] = Costumes[side];
            }
            foreach (var (language, text) in Titles) fresh.Titles[language] = text;
            foreach (var (language, text) in Descriptions) fresh.Descriptions[language] = text;
            foreach (var (language, text) in Tips) fresh.Tips[language] = text;
            return fresh;
        }

        /// <summary>Everything except the translated texts.</summary>
        public bool SameNumbers(StoryDuel other) =>
            Id == other.Id && Series == other.Series && Order == other.Order && Arena == other.Arena && RewardPack == other.RewardPack &&
            Sku == other.Sku && ExcludeFromDeckUnlock == other.ExcludeFromDeckUnlock && Key == other.Key &&
            Characters.SequenceEqual(other.Characters) && Decks.SequenceEqual(other.Decks) && Costumes.SequenceEqual(other.Costumes);

        public bool SameAs(StoryDuel other) =>
            SameNumbers(other) && SameTexts(Titles, other.Titles) && SameTexts(Descriptions, other.Descriptions) && SameTexts(Tips, other.Tips);

        private static bool SameTexts(Dictionary<char, string> a, Dictionary<char, string> b) =>
            a.Count == b.Count && a.All(t => b.TryGetValue(t.Key, out var text) && text == t.Value);
    }

    /// <summary>
    /// main/dueldata_&lt;lang&gt;.bin, all languages together: u32 count, u32 0, count x 0x5C-byte records
    /// {i32 id, series, order, character[2], deck[2], arena, rewardPack, sku, excludeFromDeckUnlock; u64 offsets (packed, not aligned) of
    /// key, costume[2] (ASCII), title, description, tip (UTF-16)}, then the strings, zero terminated, in record order with no padding.
    /// Every language has the same records, keys and costumes; only the three texts differ.
    /// Loaded by LoadStoryDuelData (0x1407FF6D0) into g_StoryDuelRecords[226].
    /// </summary>
    public sealed class StoryDuelTable
    {
        public const int RecordSize = 0x5C;
        public const int SlotCount = 226;

        /// <summary>The save keeps 24 bytes per (series, order) in a 1208-byte block per series: orders above 48 would run into the next series.</summary>
        public const int MaxOrder = 48;

        public static readonly string[] SeriesNames = ["Duel Monsters", "GX", "5D's", "ZEXAL", "ARC-V", "VRAINS"];

        /// <summary>The languages with a dueldata file.</summary>
        public static readonly char[] AllLanguages = ['E', 'F', 'G', 'I', 'J', 'S'];

        public static string FileName(char language) => $"dueldata_{char.ToUpperInvariant(language)}.bin";
        public static string GamePath(char language) => $@"main\{FileName(language)}";

        public List<StoryDuel> Duels { get; } = [];
        public List<char> Languages { get; } = [];
        public uint HeaderPad { get; set; }

        public static StoryDuelTable Parse(IDictionary<char, byte[]> files)
        {
            var table = new StoryDuelTable();
            foreach (var (language, data) in files.OrderBy(f => f.Key == 'E' ? 0 : 1).ThenBy(f => f.Key))
            {
                char lang = char.ToUpperInvariant(language);
                var (pad, records) = ParseOne(data, lang);
                if (table.Languages.Count == 0)
                {
                    table.HeaderPad = pad;
                    table.Duels.AddRange(records);
                }
                else
                {
                    if (records.Count != table.Duels.Count)
                        throw new InvalidDataException($"{FileName(lang)} has {records.Count} duels, {FileName(table.Languages[0])} has {table.Duels.Count}.");
                    for (int i = 0; i < records.Count; i++)
                    {
                        var (a, b) = (table.Duels[i], records[i]);
                        if (!a.SameNumbers(b))
                            throw new InvalidDataException($"Story duel {a.Id} differs between {FileName(table.Languages[0])} and {FileName(lang)} (only the texts may).");
                        a.Titles[lang] = b.Titles[lang];
                        a.Descriptions[lang] = b.Descriptions[lang];
                        a.Tips[lang] = b.Tips[lang];
                    }
                }
                table.Languages.Add(lang);
            }
            return table;
        }

        private static (uint Pad, List<StoryDuel> Records) ParseOne(byte[] data, char language)
        {
            if (data.Length < 8)
                throw new InvalidDataException("dueldata starts with a u32 count and 4 bytes.");
            uint count = BitConverter.ToUInt32(data, 0);
            if (8 + (long)count * RecordSize > data.Length)
                throw new InvalidDataException($"dueldata says {count} duels but is only {data.Length} bytes.");
            var records = new List<StoryDuel>();
            for (int i = 0; i < count; i++)
            {
                int at = 8 + i * RecordSize;
                int F(int field) => BitConverter.ToInt32(data, at + field * 4);
                ulong O(int index) => BitConverter.ToUInt64(data, at + 0x2C + index * 8);
                var d = new StoryDuel
                {
                    Id = F(0), Series = F(1), Order = F(2), Arena = F(7), RewardPack = F(8), Sku = F(9), ExcludeFromDeckUnlock = F(10),
                    Key = ReadAscii(data, O(0)),
                };
                d.Characters[0] = F(3); d.Characters[1] = F(4);
                d.Decks[0] = F(5); d.Decks[1] = F(6);
                d.Costumes[0] = ReadAscii(data, O(1)); d.Costumes[1] = ReadAscii(data, O(2));
                d.Titles[language] = ReadUtf16(data, O(3));
                d.Descriptions[language] = ReadUtf16(data, O(4));
                d.Tips[language] = ReadUtf16(data, O(5));
                records.Add(d);
            }
            return (BitConverter.ToUInt32(data, 4), records);
        }

        private static string ReadAscii(byte[] data, ulong offset)
        {
            int start = checked((int)offset), end = Array.IndexOf(data, (byte)0, start);
            return Encoding.ASCII.GetString(data, start, (end < 0 ? data.Length : end) - start);
        }

        private static string ReadUtf16(byte[] data, ulong offset)
        {
            int start = checked((int)offset), end = start;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0))
                end += 2;
            return Encoding.Unicode.GetString(data, start, end - start);
        }

        /// <summary>One language's file in the game's layout (a missing text uses English).</summary>
        public byte[] ToBytes(char language)
        {
            language = char.ToUpperInvariant(language);
            using var records = new MemoryStream();
            using var pool = new MemoryStream();
            long poolStart = 8 + (long)Duels.Count * RecordSize;
            var w = new BinaryWriter(records);
            w.Write((uint)Duels.Count);
            w.Write(HeaderPad);
            foreach (var d in Duels)
            {
                foreach (int value in new[] { d.Id, d.Series, d.Order, d.Characters[0], d.Characters[1], d.Decks[0], d.Decks[1], d.Arena, d.RewardPack, d.Sku, d.ExcludeFromDeckUnlock })
                    w.Write(value);
                foreach (var ascii in new[] { d.Key, d.Costumes[0], d.Costumes[1] })
                {
                    w.Write((ulong)(poolStart + pool.Length));
                    pool.Write(Encoding.ASCII.GetBytes(ascii));
                    pool.WriteByte(0);
                }
                foreach (var text in new[] { d.Title(language), d.Description(language), d.Tip(language) })
                {
                    w.Write((ulong)(poolStart + pool.Length));
                    pool.Write(Encoding.Unicode.GetBytes(text));
                    pool.Write([0, 0]);
                }
            }
            w.Flush();
            pool.Position = 0;
            pool.CopyTo(records);
            return records.ToArray();
        }

        public StoryDuelTable Clone()
        {
            var copy = new StoryDuelTable { HeaderPad = HeaderPad };
            copy.Languages.AddRange(Languages);
            copy.Duels.AddRange(Duels.Select(d => d.Clone()));
            return copy;
        }

        public StoryDuel? Find(int id) => Duels.FirstOrDefault(d => d.Id == id);

        /// <summary>The first id (1-225) no duel uses.</summary>
        public int? FreeId() => Enumerable.Range(1, SlotCount - 1).Cast<int?>().FirstOrDefault(id => Find(id!.Value) == null);

        /// <summary>The next order after the last duel of a series.</summary>
        public int NextOrder(int series) => Duels.Where(d => d.Series == series).Select(d => d.Order).DefaultIfEmpty(0).Max() + 1;

        /// <summary>Problems the game would have with the table (empty = fine).</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            foreach (var d in Duels)
            {
                if (d.Id <= 0 || d.Id >= SlotCount)
                    problems.Add($"Duel {d.Id}: the id must be 1-{SlotCount - 1}.");
                if (d.Series < 0 || d.Series > 5)
                    problems.Add($"Duel {d.Id}: the series must be 0-5.");
                if (d.Order < 1 || d.Order > MaxOrder)
                    problems.Add($"Duel {d.Id}: the order must be 1-{MaxOrder}.");
                if (string.IsNullOrEmpty(d.Key))
                    problems.Add($"Duel {d.Id}: needs a key.");
            }
            foreach (var group in Duels.GroupBy(d => d.Id).Where(g => g.Count() > 1))
                problems.Add($"Id {group.Key} is used by {group.Count()} duels.");
            foreach (var group in Duels.GroupBy(d => (d.Series, d.Order)).Where(g => g.Count() > 1))
                problems.Add($"{SeriesName(group.Key.Series)} order {group.Key.Order} is used by duels {string.Join(", ", group.Select(d => d.Id))} (only one is listed).");
            return problems;
        }

        public static string SeriesName(int series) => series >= 0 && series < SeriesNames.Length ? SeriesNames[series] : $"series {series}";
    }

    /// <summary>
    /// WolfEx's form: Yu-Gi-Oh-Ex\storyduels.json, read by Yu-Gi-Oh-Campaign. Only duels that differ from the game's (or are new), each whole:
    /// <code>
    /// { "duels": [ { "id": 186, "series": 0, "order": 33, "key": "MyDuel", "player": { "character": 104, "deck": 5, "costume": "" },
    ///                "opponent": { "character": 94, "deck": 6, "costume": "" }, "arena": 1, "rewardPack": -1, "sku": 1,
    ///                "title": { "E": "..." }, "description": { "E": "..." }, "tip": { "E": "..." } } ] }
    /// </code>
    /// </summary>
    public static class StoryDuelJson
    {
        public const string FileName = "storyduels.json";

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

        public static JsonObject ToJson(StoryDuel d)
        {
            var json = new JsonObject
            {
                ["id"] = d.Id, ["series"] = d.Series, ["order"] = d.Order, ["key"] = d.Key,
                ["player"] = Side(d, 0), ["opponent"] = Side(d, 1),
                ["arena"] = d.Arena, ["rewardPack"] = d.RewardPack, ["sku"] = d.Sku,
            };
            if (d.ExcludeFromDeckUnlock != 0)
                json["excludeFromDeckUnlock"] = d.ExcludeFromDeckUnlock;
            json["title"] = Texts(d.Titles);
            json["description"] = Texts(d.Descriptions);
            json["tip"] = Texts(d.Tips);
            return json;
        }

        private static JsonObject Side(StoryDuel d, int side) => new()
        {
            ["character"] = d.Characters[side], ["deck"] = d.Decks[side], ["costume"] = d.Costumes[side],
        };

        private static JsonObject Texts(Dictionary<char, string> texts)
        {
            var json = new JsonObject();
            foreach (var (language, text) in texts.OrderBy(t => t.Key))
                json[language.ToString()] = text;
            return json;
        }

        /// <summary>Every duel that differs from <paramref name="baseline"/> or isn't in it.</summary>
        public static JsonObject Diff(StoryDuelTable baseline, StoryDuelTable table)
        {
            var array = new JsonArray();
            foreach (var d in table.Duels.OrderBy(d => d.Id))
            {
                var game = baseline.Find(d.Id);
                if (game != null && game.SameAs(d))
                    continue;
                array.Add(ToJson(d));
            }
            return new JsonObject
            {
                ["note"] = "Campaign story duels (main/dueldata_<lang>.bin) that differ from the game's or are new, made by WolfEx; read by Yu-Gi-Oh-Campaign. " +
                    "The game has 226 slots (ids 1-225). series 0-5 (DM, GX, 5D's, ZEXAL, ARC-V, VRAINS); order = position in the series list from 1 (max 48). " +
                    "player/opponent: chardata character, deckdata deck, portrait costume suffix. rewardPack: pack unlocked on the first win (-1 none). " +
                    "key names the dialog scripts <key>_INTRO/_OUTRO/_OUTRO_LOSE (scriptdata).",
                ["duels"] = array,
            };
        }

        /// <summary>Applies the JSON onto the table (replacing or adding duels). Returns how many.</summary>
        public static int Apply(JsonObject root, StoryDuelTable table)
        {
            int applied = 0;
            foreach (var node in (root["duels"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Int(node["id"]) is not int id || id <= 0 || id >= StoryDuelTable.SlotCount)
                    continue;
                var existing = table.Find(id);
                var d = existing?.Clone() ?? new StoryDuel { Id = id, Sku = 1 };
                d.Series = Int(node["series"]) ?? d.Series;
                d.Order = Int(node["order"]) ?? d.Order;
                d.Key = Str(node["key"]) ?? d.Key;
                d.Arena = Int(node["arena"]) ?? d.Arena;
                d.RewardPack = Int(node["rewardPack"]) ?? d.RewardPack;
                d.Sku = Int(node["sku"]) ?? d.Sku;
                d.ExcludeFromDeckUnlock = Int(node["excludeFromDeckUnlock"]) ?? d.ExcludeFromDeckUnlock;
                string[] sides = ["player", "opponent"];
                for (int side = 0; side < 2; side++)
                {
                    if (node[sides[side]] is not JsonObject s)
                        continue;
                    d.Characters[side] = Int(s["character"]) ?? d.Characters[side];
                    d.Decks[side] = Int(s["deck"]) ?? d.Decks[side];
                    d.Costumes[side] = Str(s["costume"]) ?? d.Costumes[side];
                }
                ReadTexts(node["title"], d.Titles);
                ReadTexts(node["description"], d.Descriptions);
                ReadTexts(node["tip"], d.Tips);
                if (existing != null)
                    table.Duels[table.Duels.IndexOf(existing)] = d;
                else
                    table.Duels.Add(d);
                applied++;
            }
            return applied;
        }

        private static void ReadTexts(JsonNode? node, Dictionary<char, string> into)
        {
            if (node is JsonValue single && single.TryGetValue(out string? text))
                into['E'] = text;
            else if (node is JsonObject languages)
                foreach (var (language, value) in languages)
                    if (language.Length > 0 && value is JsonValue v && v.TryGetValue(out string? t))
                        into[char.ToUpperInvariant(language[0])] = t;
        }

        private static int? Int(JsonNode? node) => node is JsonValue value && value.TryGetValue(out int number) ? number : null;

        private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }
}
