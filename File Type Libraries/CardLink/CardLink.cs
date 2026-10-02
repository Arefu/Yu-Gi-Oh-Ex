using System.Text.Json;
using System.Text.Json.Nodes;

namespace Types
{
    /// <summary>What a link's target number means.</summary>
    public enum CardLinkTargetKind
    {
        /// <summary>A Konami card id (3900 and up; custom cards 15300+).</summary>
        Card,
        /// <summary>A CARD_Named archetype code (the game's are 1-418, custom archetypes come after).</summary>
        Archetype,
        /// <summary>A kind of counter, 1000 and up (1000 Spell Counter, 1048 A-Counter...).</summary>
        Counter,
        /// <summary>0, or a number between the counters and the first card id.</summary>
        Unknown,
    }

    /// <summary>One link: <see cref="Card"/>'s text mentions <see cref="Target"/>.</summary>
    public readonly record struct CardLink(int Card, int Target);

    /// <summary>
    /// bin/CARD_Link.bin: which cards, archetypes and counters each card's text mentions. No header: u16 pairs (card Konami id, target), 4
    /// bytes each, sorted by the card (a card's own targets stay in the order they were written). The game's file has 5722 links from 4494
    /// cards. A target is a Konami id (3900+), a CARD_Named archetype code (1-418) or a counter kind (1000+, see <see cref="CounterNames"/>).
    /// Examples: Exodia the Forbidden One -> its four limbs; Amazoness Archers -> archetype 6 (Amazoness); Endymion cards -> 1000 (Spell Counter).
    ///
    /// The exe (Setup_CardPropTable 0x14076BFC6) loads it into g_CardDataFiles.CardLink (0x14275ABA0 size / +8 data) and frees it on a
    /// language change, but nothing reads it: every user of g_pCardDataFiles was checked (docs/CardLink.md). Changing it has no effect
    /// in the game today; it's kept for a plugin (a "mentioned cards" view, related cards for custom cards) to use.
    /// </summary>
    public sealed class CardLinkTable
    {
        public const string GamePath = @"bin\CARD_Link.bin";
        public const int EntrySize = 4;
        public const int FirstCardId = 3900;
        public const int FirstCounter = 1000;

        /// <summary>
        /// Counter kinds seen in the game's file, named from the text of the cards that link to them. 1001, 1015, 1021, 1044-1047 and 1052 are
        /// never used.
        /// </summary>
        public static readonly IReadOnlyDictionary<int, string> CounterNames = new Dictionary<int, string>
        {
            [1000] = "Spell Counter", [1002] = "Clock Counter", [1003] = "Hyper-Venom Counter", [1004] = "Crystal Counter",
            [1005] = "Chronicle Counter", [1006] = "Bushido Counter", [1007] = "D Counter", [1008] = "Shine Counter", [1009] = "Gate Counter",
            [1010] = "Worm Counter", [1011] = "Morph Counter", [1012] = "Flower Counter", [1013] = "Plant Counter", [1014] = "Psychic Counter",
            [1016] = "Junk Counter", [1017] = "Genex Counter", [1018] = "Dragonic Counter", [1019] = "Ocean Counter",
            [1020] = "Black Feather Counter", [1022] = "Karakuri Counter", [1023] = "Spellstone Counter", [1024] = "Thunder Counter",
            [1025] = "Nut Counter", [1026] = "Greed Counter", [1027] = "Chaos Counter", [1028] = "Payback Counter", [1029] = "Destiny Counter",
            [1030] = "You Got It Boss! Counter", [1031] = "Shark Counter", [1032] = "Pumpkin Counter", [1033] = "Rising Sun Counter",
            [1034] = "Hi-Five the Sky Counter", [1035] = "Balloon Counter", [1036] = "Yosen Counter", [1037] = "Symphonic Counter",
            [1038] = "Performage Counter", [1039] = "Kaiju Counter", [1040] = "Defect Counter", [1041] = "Athlete Counter",
            [1042] = "Borrel Counter", [1043] = "Summon Counter", [1048] = "A-Counter", [1049] = "Ice Counter", [1050] = "Venom Counter",
            [1051] = "Fog Counter", [1053] = "Wedge Counter", [1054] = "Guard Counter", [1055] = "String Counter", [1056] = "Cubic Counter",
            [1057] = "Zushin Counter", [1058] = "Predator Counter", [1059] = "Scale Counter", [1060] = "Patrol Counter",
            [1061] = "Signal Counter", [1062] = "Venemy Counter",
        };

        /// <summary>Every link, in file order.</summary>
        public List<CardLink> Links { get; } = [];

        public static CardLinkTargetKind KindOf(int target) => target switch
        {
            >= FirstCardId => CardLinkTargetKind.Card,
            >= FirstCounter => CardLinkTargetKind.Counter,
            >= 1 => CardLinkTargetKind.Archetype,
            _ => CardLinkTargetKind.Unknown,
        };

        public static string KindName(CardLinkTargetKind kind) => kind switch
        {
            CardLinkTargetKind.Card => "card",
            CardLinkTargetKind.Archetype => "archetype",
            CardLinkTargetKind.Counter => "counter",
            _ => "unknown",
        };

        public static string CounterName(int code) => CounterNames.TryGetValue(code, out var name) ? name : $"Counter {code}";

        public static CardLinkTable Load(string path) => Parse(File.ReadAllBytes(path));

        public static CardLinkTable Parse(byte[] data)
        {
            if (data.Length % EntrySize != 0)
                throw new InvalidDataException($"CARD_Link.bin is a list of 4-byte links; this one is {data.Length} bytes.");
            var table = new CardLinkTable();
            for (int offset = 0; offset < data.Length; offset += EntrySize)
                table.Links.Add(new CardLink(BitConverter.ToUInt16(data, offset), BitConverter.ToUInt16(data, offset + 2)));
            return table;
        }

        public byte[] ToBytes()
        {
            var data = new byte[Links.Count * EntrySize];
            for (int i = 0; i < Links.Count; i++)
            {
                BitConverter.TryWriteBytes(data.AsSpan(i * EntrySize), (ushort)Links[i].Card);
                BitConverter.TryWriteBytes(data.AsSpan(i * EntrySize + 2), (ushort)Links[i].Target);
            }
            return data;
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        public CardLinkTable Clone()
        {
            var copy = new CardLinkTable();
            copy.Links.AddRange(Links);
            return copy;
        }

        /// <summary>The cards that have links, ascending.</summary>
        public List<int> Cards() => Links.Select(link => link.Card).Distinct().Order().ToList();

        /// <summary>A card's targets, in file order.</summary>
        public List<int> TargetsOf(int card) => Links.Where(link => link.Card == card).Select(link => link.Target).ToList();

        public bool Contains(int card, int target) => Links.Contains(new CardLink(card, target));

        /// <summary>
        /// Adds a link after the card's last one (or where the card belongs in the sorted order, for a card with none yet), so the file
        /// stays sorted by card. Returns false if it's already there.
        /// </summary>
        public bool Add(int card, int target)
        {
            if (card is < 1 or > ushort.MaxValue || target is < 1 or > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(card), "Card ids and targets are 1-65535 (u16).");
            if (Contains(card, target))
                return false;
            int at = Links.FindLastIndex(link => link.Card <= card) + 1;
            Links.Insert(at, new CardLink(card, target));
            return true;
        }

        public bool Remove(int card, int target) => Links.Remove(new CardLink(card, target));

        /// <summary>Removes every link of a card; returns how many.</summary>
        public int RemoveCard(int card) => Links.RemoveAll(link => link.Card == card);

        /// <summary>Things that look wrong: not sorted by card, a link twice, a card linking to itself, a target of 0.</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            var seen = new HashSet<CardLink>();
            for (int i = 0; i < Links.Count; i++)
            {
                var link = Links[i];
                if (i > 0 && link.Card < Links[i - 1].Card)
                    problems.Add($"Link {i} ({link.Card} -> {link.Target}) is out of order (after card {Links[i - 1].Card}).");
                if (!seen.Add(link))
                    problems.Add($"{link.Card} -> {link.Target} is in the file twice.");
                if (link.Card == link.Target)
                    problems.Add($"{link.Card} links to itself.");
                if (KindOf(link.Target) == CardLinkTargetKind.Unknown)
                    problems.Add($"{link.Card} -> {link.Target}: not a card, archetype or counter.");
            }
            return problems;
        }
    }

    /// <summary>
    /// WolfEx's form of CARD_Link.bin: Yu-Gi-Oh-Ex\cardlinks.json, only the differences from the game's file, per card:
    /// <code>
    /// { "cards": [ { "card": 15300, "name": "My Card",
    ///                "add":    [ { "target": 4007, "kind": "card", "name": "Blue-Eyes White Dragon" }, { "target": 6, "kind": "archetype" } ],
    ///                "remove": [ { "target": 1000 } ] } ] }
    /// </code>
    /// Only "card" and "target" are read ("name" and "kind" are there for people). Instead of "target" a person may write
    /// { "card": id }, { "archetype": code } or { "counter": code }.
    /// </summary>
    public static class CardLinkJson
    {
        public const string FileName = "cardlinks.json";

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

        /// <summary>The JSON for everything in <paramref name="table"/> that differs from <paramref name="baseline"/> (the game's file).</summary>
        /// <param name="nameOf">Names for the "name" fields (card id or target, with its kind); null leaves them out.</param>
        public static JsonObject Diff(CardLinkTable baseline, CardLinkTable table, Func<int, CardLinkTargetKind, string?>? nameOf = null)
        {
            var before = baseline.Links.ToHashSet();
            var after = table.Links.ToHashSet();
            var cards = new JsonArray();
            foreach (int card in before.Concat(after).Select(link => link.Card).Distinct().Order())
            {
                var added = table.Links.Where(link => link.Card == card && !before.Contains(link)).ToList();
                var removed = baseline.Links.Where(link => link.Card == card && !after.Contains(link)).ToList();
                if (added.Count == 0 && removed.Count == 0)
                    continue;
                var entry = new JsonObject { ["card"] = card };
                if (nameOf?.Invoke(card, CardLinkTargetKind.Card) is string cardName)
                    entry["name"] = cardName;
                if (added.Count > 0)
                    entry["add"] = Targets(added, nameOf);
                if (removed.Count > 0)
                    entry["remove"] = Targets(removed, nameOf);
                cards.Add(entry);
            }
            return new JsonObject
            {
                ["note"] = "Changes to the game's bin/CARD_Link.bin (which cards, archetypes and counters a card's text mentions), made by WolfEx. " +
                    "Only card and target are read; kind and name are for people. Targets: 3900+ card, 1-999 archetype code, 1000+ counter.",
                ["cards"] = cards,
            };
        }

        private static JsonArray Targets(IEnumerable<CardLink> links, Func<int, CardLinkTargetKind, string?>? nameOf)
        {
            var array = new JsonArray();
            foreach (var link in links)
            {
                var kind = CardLinkTable.KindOf(link.Target);
                var target = new JsonObject { ["target"] = link.Target, ["kind"] = CardLinkTable.KindName(kind) };
                if (nameOf?.Invoke(link.Target, kind) is string name)
                    target["name"] = name;
                array.Add(target);
            }
            return array;
        }

        /// <summary>Applies the JSON's removes, then its adds, to the table. Returns how many links changed.</summary>
        public static int Apply(JsonObject root, CardLinkTable table)
        {
            int changed = 0;
            foreach (var entry in (root["cards"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (ReadInt(entry["card"]) is not int card)
                    continue;
                foreach (int target in ReadTargets(entry["remove"]))
                    changed += table.Remove(card, target) ? 1 : 0;
                foreach (int target in ReadTargets(entry["add"]))
                    changed += table.Add(card, target) ? 1 : 0;
            }
            return changed;
        }

        private static IEnumerable<int> ReadTargets(JsonNode? node)
        {
            foreach (var item in node as JsonArray ?? [])
            {
                int? target = item switch
                {
                    JsonValue value => ReadInt(value),
                    JsonObject target1 => ReadInt(target1["target"]) ?? ReadInt(target1["card"]) ?? ReadInt(target1["archetype"]) ?? ReadInt(target1["counter"]),
                    _ => null,
                };
                if (target is int t and >= 1 and <= ushort.MaxValue)
                    yield return t;
            }
        }

        private static int? ReadInt(JsonNode? node) => node is JsonValue value && value.TryGetValue(out int number) ? number : null;

        public static void Save(string path, JsonObject root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(WriteOptions));
        }
    }
}
