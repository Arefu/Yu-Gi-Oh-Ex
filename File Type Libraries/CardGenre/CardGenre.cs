using System.Text.Json;
using System.Text.Json.Nodes;

namespace Types
{
    /// <summary>One genre bit: its key (the exe's ICON_ID_GENRE_* name) and the name the game shows.</summary>
    public sealed record CardGenreInfo(int Bit, string Key, string Name, bool Hidden = false, bool Unused = false);

    /// <summary>
    /// bin/CARD_Genre.bin: each card's genres, the effect categories on the card details page ("Recover LP", "Special Summon",
    /// "Destroy Monster"...), which the duel code also checks (Has_CardGenre, 50+ call sites, e.g. the AI's card evaluation).
    /// No header: 10166 u64 bitmasks, one per INTERNAL card id (CARD_INTID.bin maps a Konami id to it); bit n = genre n (<see cref="Genres"/>).
    /// Setup_CardPropTable loads it; Get_GenreFromKonamiId (0x14076D420) reads it for Setup_FullCardProps -> FULL_CARD_PROPS.field_38,
    /// masking out bits 39-45 (Normal and the six attributes), so those never reach the game even though the file sets them. Custom cards
    /// (Konami ids past 14968) get entry 0, no genres. docs/CardGenre.md.
    /// </summary>
    public sealed class CardGenreTable
    {
        public const string GamePath = @"bin\CARD_Genre.bin";
        public const int EntryCount = 10166;
        public const int FileSize = EntryCount * 8;

        /// <summary>The bits Get_GenreFromKonamiId clears (39-45).</summary>
        public const ulong HiddenMask = 0x3F8000000000UL;

        /// <summary>The 54 genres the game has names for (UI strings 806-865). Bits 50-53 are never set in the game's file.</summary>
        public static readonly IReadOnlyList<CardGenreInfo> Genres =
        [
            new(0, "LPUP", "Recover LP"), new(1, "LPDOWN", "Damage LP"), new(2, "DRAW", "Help Draw"), new(3, "SPSUMMON", "Special Summon"),
            new(4, "DISABLE", "Negate effect"), new(5, "DECKSEARCH", "Search Deck"), new(6, "USEGRAVE", "Recover from Graveyard"),
            new(7, "POWER", "Increase/Decrease ATK/DEF"), new(8, "POSITION", "Change battle position"), new(9, "CONTROL", "Set controls"),
            new(10, "BREAKMONST", "Destroy Monster"), new(11, "BREAKMAGIC", "Destroy Spell Card"), new(12, "HANDDES", "Destroy Hand"),
            new(13, "DECKDES", "Destroy Deck"), new(14, "REMOVECARD", "Remove Card"), new(15, "CARDBACK", "Return Card"),
            new(16, "SPEAR", "Piercing"), new(17, "DIRECTATK", "Direct Attack"), new(18, "MANYATK", "Attack multiple times"),
            new(19, "UNBREAK", "Cannot be destroyed"), new(20, "LIMITATK", "Limit Attack"), new(21, "CANTSUMMON", "Cannot Normal Summon"),
            new(22, "REVERSE", "Flip Effect Monster"), new(23, "TOON", "Toon Monster"), new(24, "SPIRIT", "Spirit Monster"),
            new(25, "UNION", "Union Monster"), new(26, "DUAL", "Gemini Monster"), new(27, "LEVELUP", "LV Monster"),
            new(28, "ORIGINAL", "Original", Unused: true), new(29, "FUSION", "Fusion Material Monster"), new(30, "RITUAL", "Ritual"),
            new(31, "TOKEN", "Token"), new(32, "COUNTER", "Counter"), new(33, "GAMBLE", "Gamble"), new(34, "ATTR", "Attribute-related"),
            new(35, "TYPE", "Type-related"), new(36, "TUNER", "Tuner"), new(37, "SYNC", "Synchro Monster"), new(38, "DROPGRAVE", "Send to Graveyard"),
            new(39, "NORMAL", "Normal Monster", Hidden: true), new(40, "ATTR_LIGHT", "Light Attribute", Hidden: true),
            new(41, "ATTR_DARK", "Dark Attribute", Hidden: true), new(42, "ATTR_EARTH", "Earth Attribute", Hidden: true),
            new(43, "ATTR_WATER", "Water Attribute", Hidden: true), new(44, "ATTR_FIRE", "Fire Attribute", Hidden: true),
            new(45, "ATTR_WIND", "Wind Attribute", Hidden: true), new(46, "XYZ", "Xyz Monster"), new(47, "LVUPDOWN", "Level Modifier"),
            new(48, "PENDULUM", "Pendulum"), new(49, "LINK", "Link Monster"), new(50, "ATTR_DIVINE", "Divine Attribute", Unused: true),
            new(51, "NEWCARD", "New Card", Unused: true), new(52, "GAMEORIGINAL", "Game Original", Unused: true),
            new(53, "VARIATION", "Card Variation", Unused: true),
        ];

        /// <summary>The genre bitmask per internal id.</summary>
        public ulong[] Masks { get; } = new ulong[EntryCount];

        public static CardGenreTable Load(string path) => Parse(File.ReadAllBytes(path));

        public static CardGenreTable Parse(byte[] data)
        {
            if (data.Length != FileSize)
                throw new InvalidDataException($"CARD_Genre.bin is {FileSize} bytes ({EntryCount} u64); this one is {data.Length}.");
            var table = new CardGenreTable();
            Buffer.BlockCopy(data, 0, table.Masks, 0, FileSize);
            return table;
        }

        public byte[] ToBytes()
        {
            var data = new byte[FileSize];
            Buffer.BlockCopy(Masks, 0, data, 0, FileSize);
            return data;
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        public static bool Has(ulong mask, int bit) => (mask >> bit & 1) != 0;

        public static ulong With(ulong mask, int bit, bool on) => on ? mask | 1UL << bit : mask & ~(1UL << bit);

        /// <summary>The genres in a mask, as keys ("DRAW", "SPSUMMON").</summary>
        public static List<string> Keys(ulong mask) => Genres.Where(g => Has(mask, g.Bit)).Select(g => g.Key).ToList();

        /// <summary>The genres in a mask, as the game's names.</summary>
        public static string Describe(ulong mask) => string.Join(", ", Genres.Where(g => Has(mask, g.Bit)).Select(g => g.Name));

        /// <summary>A mask from genre keys, names or bit numbers; unknown entries are returned in <paramref name="unknown"/>.</summary>
        public static ulong FromKeys(IEnumerable<string> keys, List<string>? unknown = null)
        {
            ulong mask = 0;
            foreach (string key in keys)
            {
                var genre = Genres.FirstOrDefault(g => g.Key.Equals(key, StringComparison.OrdinalIgnoreCase) || g.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
                if (genre != null)
                    mask |= 1UL << genre.Bit;
                else if (int.TryParse(key, out int bit) && bit is >= 0 and < 64)
                    mask |= 1UL << bit;
                else
                    unknown?.Add(key);
            }
            return mask;
        }
    }

    /// <summary>
    /// WolfEx's form: Yu-Gi-Oh-Ex\genres.json, the cards whose genres differ from the game's, keyed by Konami id (custom cards too), each
    /// with its whole genre list:
    /// <code>{ "cards": [ { "card": 15300, "name": "My Card", "genres": [ "DRAW", "SPSUMMON" ] } ] }</code>
    /// A genre can be written as its key, its game name ("Help Draw") or its bit number. "name" is for people.
    /// </summary>
    public static class CardGenreJson
    {
        public const string FileName = "genres.json";

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

        private static ulong MaskOf(IDictionary<int, ulong> cards, int card) => cards.TryGetValue(card, out ulong mask) ? mask : 0;

        public static void Save(string path, JsonObject root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(WriteOptions));
        }

        /// <summary>Every card whose mask differs from the game's (a card missing from <paramref name="baseline"/> counts as 0).</summary>
        public static JsonObject Diff(IDictionary<int, ulong> baseline, IDictionary<int, ulong> cards, Func<int, string?>? cardName = null)
        {
            var array = new JsonArray();
            foreach (int card in baseline.Keys.Concat(cards.Keys).Distinct().Order())
            {
                ulong before = MaskOf(baseline, card), after = MaskOf(cards, card);
                if (before == after)
                    continue;
                var entry = new JsonObject { ["card"] = card };
                if (cardName?.Invoke(card) is string name)
                    entry["name"] = name;
                var genres = new JsonArray();
                foreach (string key in CardGenreTable.Keys(after))
                    genres.Add(key);
                entry["genres"] = genres;
                array.Add(entry);
            }
            return new JsonObject
            {
                ["note"] = "Card genres (the effect categories on the card details page, also used by the duel AI) that differ from the game's bin/CARD_Genre.bin, " +
                    "made by WolfEx. Each card lists all its genres. Genre keys: " + string.Join(" ", CardGenreTable.Genres.Select(g => g.Key)) + ".",
                ["cards"] = array,
            };
        }

        /// <summary>A custom card's genres as its cards.json entry keeps them ("genres": [ "DRAW", "SPSUMMON" ]); null when it has none.</summary>
        public static JsonArray? ToCardJson(ulong mask)
        {
            if (mask == 0)
                return null;
            var genres = new JsonArray();
            foreach (string key in CardGenreTable.Keys(mask))
                genres.Add(key);
            return genres;
        }

        /// <summary>A cards.json "genres" list (keys, game names or bit numbers) as a mask.</summary>
        public static ulong FromCardJson(JsonNode? node, List<string>? unknown = null) =>
            CardGenreTable.FromKeys((node as JsonArray ?? []).Select(item => item switch
            {
                JsonValue value when value.TryGetValue(out string? text) => text,
                JsonValue value when value.TryGetValue(out int bit) => bit.ToString(),
                _ => null,
            }).OfType<string>(), unknown);

        /// <summary>Sets each listed card's mask. Returns how many cards it changed; unknown genre names go in <paramref name="unknown"/>.</summary>
        public static int Apply(JsonObject root, IDictionary<int, ulong> cards, List<string>? unknown = null)
        {
            int changed = 0;
            foreach (var entry in (root["cards"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (entry["card"] is not JsonValue id || !id.TryGetValue(out int card))
                    continue;
                var keys = (entry["genres"] as JsonArray ?? []).Select(node => node switch
                {
                    JsonValue value when value.TryGetValue(out string? text) => text,
                    JsonValue value when value.TryGetValue(out int bit) => bit.ToString(),
                    _ => null,
                }).OfType<string>();
                ulong mask = CardGenreTable.FromKeys(keys, unknown);
                if (MaskOf(cards, card) != mask)
                    changed++;
                cards[card] = mask;
            }
            return changed;
        }
    }
}
