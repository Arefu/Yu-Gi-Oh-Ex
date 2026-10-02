using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Types
{
    /// <summary>
    /// One duelist (main/chardata_&lt;lang&gt;.bin record, 0x38 bytes; CharacterRecord in the IDB, docs/Characters.md).
    /// </summary>
    public sealed class Character
    {
        /// <summary>0-239 (the game's table has 240 slots, 191 used).</summary>
        public int Id { get; set; }

        /// <summary>The series tab (0-5); -1 = no series, unlocked in every new profile.</summary>
        public int Series { get; set; }

        /// <summary>The character's deck (deckdata index; duels use deck id + 32); -1 = none. Free Duel needs a deck the character owns.</summary>
        public int Deck { get; set; } = -1;

        /// <summary>1 = a Free Duel opponent and counted in the collection (0 for the characters without a deck).</summary>
        public int Selectable { get; set; }

        /// <summary>The content pack (skudata); -1 in the file = 1, the base game.</summary>
        public int Sku { get; set; } = -1;

        /// <summary>File +0x14: never read by the game (0 for all but 3 characters).</summary>
        public int Unused14 { get; set; }

        /// <summary>The arena of duels against this character (Set_CurrentArenaId).</summary>
        public int Arena { get; set; }

        /// <summary>File +0x1C: always 0.</summary>
        public int Field1C { get; set; }

        /// <summary>ASCII; the portrait is "&lt;key&gt;_neutral" in the pdui/chars sprite sheet.</summary>
        public string Key { get; set; } = "";

        public Dictionary<char, string> Names { get; } = [];
        public Dictionary<char, string> Bios { get; } = [];

        public string Name(char language = 'E') => Pick(Names, language);
        public string Bio(char language = 'E') => Pick(Bios, language);

        private static string Pick(Dictionary<char, string> texts, char language) =>
            texts.TryGetValue(language, out var text) ? text : texts.TryGetValue('E', out var english) ? english : texts.Values.FirstOrDefault() ?? "";

        public Character Clone()
        {
            var fresh = new Character
            {
                Id = Id, Series = Series, Deck = Deck, Selectable = Selectable, Sku = Sku, Unused14 = Unused14, Arena = Arena, Field1C = Field1C, Key = Key,
            };
            foreach (var (language, text) in Names) fresh.Names[language] = text;
            foreach (var (language, text) in Bios) fresh.Bios[language] = text;
            return fresh;
        }

        public bool SameAs(Character other) =>
            Id == other.Id && Series == other.Series && Deck == other.Deck && Selectable == other.Selectable && Sku == other.Sku &&
            Unused14 == other.Unused14 && Arena == other.Arena && Field1C == other.Field1C && Key == other.Key &&
            SameTexts(Names, other.Names) && SameTexts(Bios, other.Bios);

        private static bool SameTexts(Dictionary<char, string> a, Dictionary<char, string> b) =>
            a.Count == b.Count && a.All(t => b.TryGetValue(t.Key, out var text) && text == t.Value);
    }

    /// <summary>
    /// main/chardata_&lt;lang&gt;.bin, all languages together: u32 count, u32 0, count x 0x38-byte records
    /// {i32 id, series, deck, selectable, sku, unused, arena, 0; u64 offsets of key (ASCII), name, bio (UTF-16)}, then the strings, zero
    /// terminated, in record order with no padding. Every language has the same records and keys; only names and bios differ.
    /// Loaded by YGO::GAME::LoadCharacterData (0x1407FED60) into g_CharacterRecords[240].
    /// </summary>
    public sealed class CharacterTable
    {
        public const int RecordSize = 0x38;
        public const int SlotCount = 240;

        /// <summary>The languages with a chardata file.</summary>
        public static readonly char[] AllLanguages = ['E', 'F', 'G', 'I', 'J', 'S'];

        public static string FileName(char language) => $"chardata_{char.ToUpperInvariant(language)}.bin";
        public static string GamePath(char language) => $@"main\{FileName(language)}";

        public List<Character> Characters { get; } = [];
        public List<char> Languages { get; } = [];
        public uint HeaderPad { get; set; }

        public static CharacterTable Parse(IDictionary<char, byte[]> files)
        {
            var table = new CharacterTable();
            foreach (var (language, data) in files.OrderBy(f => f.Key == 'E' ? 0 : 1).ThenBy(f => f.Key))
            {
                char lang = char.ToUpperInvariant(language);
                var (pad, records) = ParseOne(data, lang);
                if (table.Languages.Count == 0)
                {
                    table.HeaderPad = pad;
                    table.Characters.AddRange(records);
                }
                else
                {
                    if (records.Count != table.Characters.Count)
                        throw new InvalidDataException($"{FileName(lang)} has {records.Count} characters, {FileName(table.Languages[0])} has {table.Characters.Count}.");
                    for (int i = 0; i < records.Count; i++)
                    {
                        var (a, b) = (table.Characters[i], records[i]);
                        if (a.Id != b.Id || a.Series != b.Series || a.Deck != b.Deck || a.Selectable != b.Selectable || a.Sku != b.Sku ||
                            a.Unused14 != b.Unused14 || a.Arena != b.Arena || a.Field1C != b.Field1C || a.Key != b.Key)
                            throw new InvalidDataException($"Character {a.Id} differs between {FileName(table.Languages[0])} and {FileName(lang)} (only names and bios may).");
                        a.Names[lang] = b.Names[lang];
                        a.Bios[lang] = b.Bios[lang];
                    }
                }
                table.Languages.Add(lang);
            }
            return table;
        }

        private static (uint Pad, List<Character> Records) ParseOne(byte[] data, char language)
        {
            if (data.Length < 8)
                throw new InvalidDataException("chardata starts with a u32 count and 4 bytes.");
            uint count = BitConverter.ToUInt32(data, 0);
            if (8 + (long)count * RecordSize > data.Length)
                throw new InvalidDataException($"chardata says {count} characters but is only {data.Length} bytes.");
            var records = new List<Character>();
            for (int i = 0; i < count; i++)
            {
                int at = 8 + i * RecordSize;
                int F(int field) => BitConverter.ToInt32(data, at + field * 4);
                var c = new Character
                {
                    Id = F(0), Series = F(1), Deck = F(2), Selectable = F(3), Sku = F(4), Unused14 = F(5), Arena = F(6), Field1C = F(7),
                    Key = ReadAscii(data, BitConverter.ToUInt64(data, at + 0x20)),
                };
                c.Names[language] = ReadUtf16(data, BitConverter.ToUInt64(data, at + 0x28));
                c.Bios[language] = ReadUtf16(data, BitConverter.ToUInt64(data, at + 0x30));
                records.Add(c);
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

        /// <summary>One language's file in the game's layout (a missing name or bio uses English).</summary>
        public byte[] ToBytes(char language)
        {
            language = char.ToUpperInvariant(language);
            using var records = new MemoryStream();
            using var pool = new MemoryStream();
            long poolStart = 8 + (long)Characters.Count * RecordSize;
            var w = new BinaryWriter(records);
            w.Write((uint)Characters.Count);
            w.Write(HeaderPad);
            foreach (var c in Characters)
            {
                foreach (int value in new[] { c.Id, c.Series, c.Deck, c.Selectable, c.Sku, c.Unused14, c.Arena, c.Field1C })
                    w.Write(value);
                w.Write((ulong)(poolStart + pool.Length));
                pool.Write(Encoding.ASCII.GetBytes(c.Key));
                pool.WriteByte(0);
                w.Write((ulong)(poolStart + pool.Length));
                pool.Write(Encoding.Unicode.GetBytes(c.Name(language)));
                pool.Write([0, 0]);
                w.Write((ulong)(poolStart + pool.Length));
                pool.Write(Encoding.Unicode.GetBytes(c.Bio(language)));
                pool.Write([0, 0]);
            }
            w.Flush();
            pool.Position = 0;
            pool.CopyTo(records);
            return records.ToArray();
        }

        public CharacterTable Clone()
        {
            var copy = new CharacterTable { HeaderPad = HeaderPad };
            copy.Languages.AddRange(Languages);
            copy.Characters.AddRange(Characters.Select(c => c.Clone()));
            return copy;
        }

        public Character? Find(int id) => Characters.FirstOrDefault(c => c.Id == id);

        /// <summary>The first id (0-239) no character uses.</summary>
        public int? FreeId() => Enumerable.Range(1, SlotCount - 1).Cast<int?>().FirstOrDefault(id => Find(id!.Value) == null);
    }

    /// <summary>
    /// WolfEx's form: Yu-Gi-Oh-Ex\characters.json, read by Yu-Gi-Oh-Campaign. Only characters that differ from the game's (or are new),
    /// each whole:
    /// <code>
    /// { "characters": [ { "id": 192, "key": "yugimuto", "series": 0, "deck": 5, "selectable": 1, "sku": 1, "arena": 2, "unlocked": true,
    ///                     "name": { "E": "My Duelist" }, "bio": { "E": "..." } } ] }
    /// </code>
    /// "unlocked" (new characters; default true) = Yu-Gi-Oh-Campaign sets the character's unlock bit in the save.
    /// </summary>
    public static class CharacterJson
    {
        public const string FileName = "characters.json";

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

        public static JsonObject ToJson(Character c, bool? unlocked)
        {
            var json = new JsonObject
            {
                ["id"] = c.Id, ["key"] = c.Key, ["series"] = c.Series, ["deck"] = c.Deck, ["selectable"] = c.Selectable, ["sku"] = c.Sku, ["arena"] = c.Arena,
            };
            if (c.Unused14 != 0)
                json["unused14"] = c.Unused14;
            if (unlocked != null)
                json["unlocked"] = unlocked.Value;
            json["name"] = Texts(c.Names);
            json["bio"] = Texts(c.Bios);
            return json;
        }

        private static JsonObject Texts(Dictionary<char, string> texts)
        {
            var json = new JsonObject();
            foreach (var (language, text) in texts.OrderBy(t => t.Key))
                json[language.ToString()] = text;
            return json;
        }

        /// <summary>Every character that differs from <paramref name="baseline"/> or isn't in it. <paramref name="unlocked"/>: new characters' flag.</summary>
        public static JsonObject Diff(CharacterTable baseline, CharacterTable table, IReadOnlyDictionary<int, bool>? unlocked = null)
        {
            var array = new JsonArray();
            foreach (var c in table.Characters.OrderBy(c => c.Id))
            {
                var game = baseline.Find(c.Id);
                if (game != null && game.SameAs(c))
                    continue;
                bool? flag = game == null ? (unlocked != null && unlocked.TryGetValue(c.Id, out bool u) ? u : true) : null;
                array.Add(ToJson(c, flag));
            }
            return new JsonObject
            {
                ["note"] = "Duelists (main/chardata_<lang>.bin) that differ from the game's or are new, made by WolfEx; read by Yu-Gi-Oh-Campaign. " +
                    "The game has 240 slots (ids 0-239). key = portrait (\"<key>_neutral\" in pdui/chars). series: 0-5 tab, -1 none. deck: deckdata index " +
                    "(the character must own the deck to appear in Free Duel). unlocked (new characters): set in the save.",
                ["characters"] = array,
            };
        }

        /// <summary>Applies the JSON onto the table (replacing or adding characters). Returns how many; <paramref name="unlocked"/> gets the new ones' flags.</summary>
        public static int Apply(JsonObject root, CharacterTable table, IDictionary<int, bool>? unlocked = null)
        {
            int applied = 0;
            foreach (var node in (root["characters"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Int(node["id"]) is not int id || id < 0 || id >= CharacterTable.SlotCount)
                    continue;
                var existing = table.Find(id);
                var c = existing?.Clone() ?? new Character { Id = id, Sku = 1, Selectable = 1 };
                c.Key = Str(node["key"]) ?? c.Key;
                c.Series = Int(node["series"]) ?? c.Series;
                c.Deck = Int(node["deck"]) ?? c.Deck;
                c.Selectable = Int(node["selectable"]) ?? c.Selectable;
                c.Sku = Int(node["sku"]) ?? c.Sku;
                c.Arena = Int(node["arena"]) ?? c.Arena;
                c.Unused14 = Int(node["unused14"]) ?? c.Unused14;
                ReadTexts(node["name"], c.Names);
                ReadTexts(node["bio"], c.Bios);
                if (existing != null)
                    table.Characters[table.Characters.IndexOf(existing)] = c;
                else
                {
                    table.Characters.Add(c);
                    if (unlocked != null)
                        unlocked[id] = node["unlocked"] is JsonValue flag && flag.TryGetValue(out bool value) ? value : true;
                }
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
