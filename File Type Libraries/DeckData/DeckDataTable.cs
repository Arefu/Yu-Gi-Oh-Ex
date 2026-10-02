using System.Text.Json;
using System.Text.Json.Nodes;

namespace DeckData
{
    /// <summary>
    /// One deck with every language's texts and (when decks.zib was given) its cards. DeckRecord in the IDB (g_DeckRecords[700], 0x88), docs/Decks.md.
    /// </summary>
    public sealed class Deck
    {
        /// <summary>The deck's slot, 0-699 (the game's table has 700). Duels use id + 32.</summary>
        public uint Id { get; set; }

        /// <summary>Always equal to <see cref="Id"/> in the game's files; the game reads this copy.</summary>
        public uint Slot { get; set; }

        /// <summary>The series the deck is listed under (0 DM ... 5 VRAINS); -1 for the 5 starter decks.</summary>
        public int Series { get; set; }

        /// <summary>Konami id of the card shown on the deck's info panel; 0xFFFF = none.</summary>
        public ushort SignatureCard { get; set; } = 0xFFFF;

        public ushort Padding14 { get; set; }

        /// <summary>The owner (chardata id). A character appears in Free Duel only with a deck it owns; the deck list sorts by it.</summary>
        public uint CharacterId { get; set; }

        /// <summary>Content pack; -1 in the file = 1, the base game. A deck of another owned SKU has its cards granted.</summary>
        public int Sku { get; set; } = -1;

        /// <summary>The .ydc in decks.zib, without the extension.</summary>
        public string FileName { get; set; } = "";

        public Dictionary<char, string> Titles { get; } = [];

        /// <summary>A second text the game copies but nothing shown uses (6 English decks have one).</summary>
        public Dictionary<char, string> Texts2 { get; } = [];

        public Dictionary<char, string> Texts3 { get; } = [];

        /// <summary>The cards (from decks.zib); null when they weren't loaded or the .ydc is missing.</summary>
        public YdcDeck? Cards { get; set; }

        public string Title(char language = 'E') => Pick(Titles, language);

        public static string Pick(Dictionary<char, string> texts, char language) =>
            texts.TryGetValue(language, out var text) ? text : texts.TryGetValue('E', out var english) ? english : texts.Values.FirstOrDefault() ?? "";

        public Deck Clone()
        {
            var copy = new Deck
            {
                Id = Id, Slot = Slot, Series = Series, SignatureCard = SignatureCard, Padding14 = Padding14, CharacterId = CharacterId, Sku = Sku, FileName = FileName,
                Cards = Cards == null ? null : YdcDeck.Parse(Cards.ToBytes()),
            };
            foreach (var (l, t) in Titles) copy.Titles[l] = t;
            foreach (var (l, t) in Texts2) copy.Texts2[l] = t;
            foreach (var (l, t) in Texts3) copy.Texts3[l] = t;
            return copy;
        }

        public bool SameRecord(Deck other) =>
            Id == other.Id && Slot == other.Slot && Series == other.Series && SignatureCard == other.SignatureCard && Padding14 == other.Padding14 &&
            CharacterId == other.CharacterId && Sku == other.Sku && FileName == other.FileName &&
            SameTexts(Titles, other.Titles) && SameTexts(Texts2, other.Texts2) && SameTexts(Texts3, other.Texts3);

        public bool SameCards(Deck other) =>
            (Cards == null && other.Cards == null) ||
            (Cards != null && other.Cards != null && Cards.Main.SequenceEqual(other.Cards.Main) && Cards.Extra.SequenceEqual(other.Cards.Extra) && Cards.Side.SequenceEqual(other.Cards.Side));

        private static bool SameTexts(Dictionary<char, string> a, Dictionary<char, string> b) =>
            a.Keys.Union(b.Keys).All(l => a.GetValueOrDefault(l, "") == b.GetValueOrDefault(l, ""));
    }

    /// <summary>
    /// main/deckdata_&lt;lang&gt;.bin for every language at once (each file is a <see cref="DeckDataFile"/>; they have the same records and only
    /// the texts differ), plus the cards from decks.zib. Loaded by LoadDeckDataFile (0x1407FE500); the cards by LoadDeckTemplatesFromDecksZib.
    /// </summary>
    public sealed class DeckDataTable
    {
        public const int SlotCount = 700;

        /// <summary>The most cards per section (g_DeckSectionMaxSizes): a .ydc with more is rejected.</summary>
        public const int MaxMain = 60, MaxExtra = 15, MaxSide = 15;

        public static readonly char[] AllLanguages = ['E', 'F', 'G', 'I', 'J', 'S'];
        public static readonly string[] SeriesNames = ["Duel Monsters", "GX", "5D's", "ZEXAL", "ARC-V", "VRAINS"];

        public static string FileName(char language) => $"deckdata_{char.ToUpperInvariant(language)}.bin";
        public static string GamePath(char language) => $@"main\{FileName(language)}";

        public List<Deck> Decks { get; } = [];
        public List<char> Languages { get; } = [];

        public static DeckDataTable Parse(IDictionary<char, byte[]> files)
        {
            var table = new DeckDataTable();
            foreach (var (language, data) in files.OrderBy(f => f.Key == 'E' ? 0 : 1).ThenBy(f => f.Key))
            {
                char lang = char.ToUpperInvariant(language);
                var records = DeckDataFile.Parse(data).Records;
                if (table.Languages.Count == 0)
                {
                    foreach (var r in records)
                        table.Decks.Add(new Deck
                        {
                            Id = r.Id, Slot = r.Slot, Series = unchecked((int)r.Type), SignatureCard = r.SignatureCard, Padding14 = r.Padding14,
                            CharacterId = r.CharacterId, Sku = r.Sku, FileName = r.FileName,
                        });
                }
                else if (records.Count != table.Decks.Count)
                    throw new InvalidDataException($"{FileName(lang)} has {records.Count} decks, {FileName(table.Languages[0])} has {table.Decks.Count}.");
                for (int i = 0; i < records.Count; i++)
                {
                    var (d, r) = (table.Decks[i], records[i]);
                    if (d.Id != r.Id || d.Slot != r.Slot || d.Series != unchecked((int)r.Type) || d.SignatureCard != r.SignatureCard || d.Padding14 != r.Padding14 ||
                        d.CharacterId != r.CharacterId || d.Sku != r.Sku || d.FileName != r.FileName)
                        throw new InvalidDataException($"Deck {d.Id} differs between {FileName(table.Languages.FirstOrDefault(lang))} and {FileName(lang)} (only the texts may).");
                    d.Titles[lang] = r.Title;
                    d.Texts2[lang] = r.Text2;
                    d.Texts3[lang] = r.Text3;
                }
                table.Languages.Add(lang);
            }
            return table;
        }

        /// <summary>Reads each deck's cards: <paramref name="readYdc"/> gets "&lt;file&gt;.ydc" (decks.zib). Returns how many were found.</summary>
        public int AttachCards(Func<string, byte[]?> readYdc)
        {
            int found = 0;
            foreach (var deck in Decks)
            {
                if (deck.FileName.Length > 0 && readYdc(deck.FileName + ".ydc") is byte[] data)
                {
                    try
                    {
                        deck.Cards = YdcDeck.Parse(data);
                        found++;
                    }
                    catch (InvalidDataException) { }
                }
            }
            return found;
        }

        /// <summary>One language's file in the game's layout (a missing text uses English).</summary>
        public byte[] ToBytes(char language)
        {
            language = char.ToUpperInvariant(language);
            var file = new DeckDataFile();
            foreach (var d in Decks)
                file.Records.Add(new DeckRecord
                {
                    Id = d.Id, Slot = d.Slot, Type = unchecked((uint)d.Series), SignatureCard = d.SignatureCard, Padding14 = d.Padding14, CharacterId = d.CharacterId,
                    Sku = d.Sku, FileName = d.FileName, Title = Deck.Pick(d.Titles, language), Text2 = Deck.Pick(d.Texts2, language), Text3 = Deck.Pick(d.Texts3, language),
                });
            return file.ToBytes();
        }

        public DeckDataTable Clone()
        {
            var copy = new DeckDataTable();
            copy.Languages.AddRange(Languages);
            copy.Decks.AddRange(Decks.Select(d => d.Clone()));
            return copy;
        }

        public Deck? Find(uint id) => Decks.FirstOrDefault(d => d.Id == id);

        /// <summary>The first id (1-699) no deck uses (the game's decks start at 1; 0 is left alone).</summary>
        public uint? FreeId() => Enumerable.Range(1, SlotCount - 1).Select(i => (uint)i).Cast<uint?>().FirstOrDefault(id => Find(id!.Value) == null);

        /// <summary>Problems the game would have (empty = fine).</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            foreach (var d in Decks)
            {
                if (d.Id >= SlotCount)
                    problems.Add($"Deck {d.Id}: the id must be 0-{SlotCount - 1}.");
                if (d.FileName.Length == 0 || d.FileName.Any(c => c > 127 || c == '/' || c == '\\'))
                    problems.Add($"Deck {d.Id}: needs a file name (ASCII, no slashes).");
                if (d.Cards != null && (d.Cards.Main.Count > MaxMain || d.Cards.Extra.Count > MaxExtra || d.Cards.Side.Count > MaxSide))
                    problems.Add($"Deck {d.Id}: at most {MaxMain} main, {MaxExtra} extra and {MaxSide} side cards (the game rejects the deck otherwise).");
            }
            foreach (var group in Decks.GroupBy(d => d.Id).Where(g => g.Count() > 1))
                problems.Add($"Id {group.Key} is used by {group.Count()} decks.");
            foreach (var group in Decks.Where(d => d.FileName.Length > 0).GroupBy(d => d.FileName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                problems.Add($"File name {group.Key} is used by decks {string.Join(", ", group.Select(d => d.Id))} (they would share cards).");
            return problems;
        }
    }

    /// <summary>
    /// WolfEx's form: Yu-Gi-Oh-Ex\decks.json, read by Yu-Gi-Oh-Campaign. Only decks that differ from the game's (or are new), each whole; "cards"
    /// only when the card list differs from the game's .ydc (or the deck is new):
    /// <code>
    /// { "decks": [ { "id": 546, "file": "my_deck", "series": 0, "character": 192, "signatureCard": 4007, "sku": 1, "unlocked": true,
    ///                "title": { "E": "My Deck" }, "cards": { "main": [4007, ...], "extra": [], "side": [] } } ] }
    /// </code>
    /// "unlocked" (new decks; default false) = Yu-Gi-Oh-Campaign marks the deck unlocked in the save (its recipe appears for the player).
    /// </summary>
    public static class DeckJson
    {
        public const string FileName = "decks.json";

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

        private static JsonObject Texts(Dictionary<char, string> texts)
        {
            var json = new JsonObject();
            foreach (var (language, text) in texts.OrderBy(t => t.Key))
                json[language.ToString()] = text;
            return json;
        }

        private static JsonArray Ids(IEnumerable<ushort> ids) => new(ids.Select(id => (JsonNode)JsonValue.Create((int)id)).ToArray());

        public static JsonObject ToJson(Deck d, bool withCards, bool? unlocked)
        {
            var json = new JsonObject
            {
                ["id"] = d.Id, ["file"] = d.FileName, ["series"] = d.Series, ["character"] = d.CharacterId, ["signatureCard"] = d.SignatureCard, ["sku"] = d.Sku,
            };
            if (unlocked != null)
                json["unlocked"] = unlocked.Value;
            json["title"] = Texts(d.Titles);
            if (d.Texts2.Values.Any(t => t.Length > 0)) json["text2"] = Texts(d.Texts2);
            if (d.Texts3.Values.Any(t => t.Length > 0)) json["text3"] = Texts(d.Texts3);
            if (withCards && d.Cards != null)
                json["cards"] = new JsonObject { ["main"] = Ids(d.Cards.Main), ["extra"] = Ids(d.Cards.Extra), ["side"] = Ids(d.Cards.Side) };
            return json;
        }

        /// <summary>Every deck that differs from <paramref name="baseline"/> or isn't in it. <paramref name="unlocked"/>: new decks' flag.</summary>
        public static JsonObject Diff(DeckDataTable baseline, DeckDataTable table, IReadOnlyDictionary<uint, bool>? unlocked = null)
        {
            var array = new JsonArray();
            foreach (var d in table.Decks.OrderBy(d => d.Id))
            {
                var game = baseline.Find(d.Id);
                bool sameRecord = game != null && game.SameRecord(d), sameCards = game != null && game.SameCards(d);
                if (sameRecord && sameCards)
                    continue;
                bool? flag = game == null ? (unlocked != null && unlocked.TryGetValue(d.Id, out bool u) && u) : null;
                array.Add(ToJson(d, withCards: game == null || !sameCards, flag));
            }
            return new JsonObject
            {
                ["note"] = "Decks (main/deckdata_<lang>.bin + decks.zib/<file>.ydc) that differ from the game's or are new, made by WolfEx; read by Yu-Gi-Oh-Campaign. " +
                    "The game has 700 slots (ids 0-699; duels use id + 32). series: 0-5, -1 starter. character: the owner (chardata id). signatureCard: Konami id " +
                    "(65535 none). cards: Konami ids, at most 60 main / 15 extra / 15 side; left out = the game's .ydc. unlocked (new decks): set in the save.",
                ["decks"] = array,
            };
        }

        /// <summary>Applies the JSON onto the table (replacing or adding decks). Returns how many; <paramref name="unlocked"/> gets new decks' flags.</summary>
        public static int Apply(JsonObject root, DeckDataTable table, IDictionary<uint, bool>? unlocked = null)
        {
            int applied = 0;
            foreach (var node in (root["decks"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Int(node["id"]) is not int id || id < 0 || id >= DeckDataTable.SlotCount)
                    continue;
                var existing = table.Find((uint)id);
                var d = existing?.Clone() ?? new Deck { Id = (uint)id, Slot = (uint)id, Sku = 1 };
                d.FileName = Str(node["file"]) ?? d.FileName;
                d.Series = Int(node["series"]) ?? d.Series;
                if (Int(node["character"]) is int character) d.CharacterId = (uint)character;
                if (Int(node["signatureCard"]) is int card) d.SignatureCard = (ushort)card;
                d.Sku = Int(node["sku"]) ?? d.Sku;
                ReadTexts(node["title"], d.Titles);
                ReadTexts(node["text2"], d.Texts2);
                ReadTexts(node["text3"], d.Texts3);
                if (node["cards"] is JsonObject cards)
                {
                    var ydc = new YdcDeck();
                    ydc.Main.AddRange(Cards(cards["main"]));
                    ydc.Extra.AddRange(Cards(cards["extra"]));
                    ydc.Side.AddRange(Cards(cards["side"]));
                    d.Cards = ydc;
                }
                if (existing != null)
                    table.Decks[table.Decks.IndexOf(existing)] = d;
                else
                {
                    table.Decks.Add(d);
                    if (unlocked != null)
                        unlocked[(uint)id] = node["unlocked"] is JsonValue flag && flag.TryGetValue(out bool value) && value;
                }
                applied++;
            }
            return applied;
        }

        /// <summary>Applies the JSON's records (not cards) to one language's file: for editors that only need deck titles and owners.</summary>
        public static void ApplyTo(DeckDataFile file, JsonObject root, char language)
        {
            foreach (var node in (root["decks"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (Int(node["id"]) is not int id || id < 0 || id >= DeckDataTable.SlotCount)
                    continue;
                var record = file.Find((uint)id);
                if (record == null)
                    file.Records.Add(record = new DeckRecord { Id = (uint)id, Slot = (uint)id, Sku = 1 });
                if (Int(node["character"]) is int character) record.CharacterId = (uint)character;
                if (Int(node["series"]) is int series) record.Type = unchecked((uint)series);
                record.FileName = Str(node["file"]) ?? record.FileName;
                var titles = new Dictionary<char, string>();
                ReadTexts(node["title"], titles);
                if (titles.Count > 0)
                    record.Title = Deck.Pick(titles, language);
            }
        }

        private static IEnumerable<ushort> Cards(JsonNode? node) =>
            (node as JsonArray ?? []).OfType<JsonValue>().Select(v => v.TryGetValue(out int id) ? id : -1).Where(id => id is > 0 and <= 0xFFFF).Select(id => (ushort)id);

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
