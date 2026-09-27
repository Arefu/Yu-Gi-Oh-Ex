using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace StartingCollection
{
    /// <summary>One card the editors can offer: its Konami id and name.</summary>
    public sealed record CardEntry(int Id, string Name, bool IsCustom = false)
    {
        public override string ToString() => $"{Name} ({Id})";
    }

    /// <summary>
    /// The names of every card, so the editors can take names instead of Konami ids. Reads the game's own files
    /// (bin/CARD_IntID.bin maps a Konami id to an internal id, bin/CARD_Indx_&lt;lang&gt;.bin maps that to an offset in
    /// bin/CARD_Name_&lt;lang&gt;.bin, which holds zero terminated UTF-16 names) and, when there is one, the custom cards in
    /// Yu-Gi-Oh-Ex/cards.json.
    /// </summary>
    public sealed class CardDatabase
    {
        private readonly Dictionary<int, CardEntry> _byId = [];
        private List<CardEntry> _sorted = [];

        public IReadOnlyList<CardEntry> Cards => _sorted;

        public string Language { get; private set; } = "E";

        public List<string> Warnings { get; } = [];

        /// <summary>A database with no names: every id from 3900 up is a card called "#id". Used when the game's data can't be found, so ids still work.</summary>
        public static CardDatabase IdsOnly()
        {
            var database = new CardDatabase();
            for (int id = StartingCollectionBuilder.FirstKonamiId; id <= MaxCardId; id++)
                database._byId[id] = new CardEntry(id, $"#{id}");
            database._sorted = [.. database._byId.Values.OrderBy(card => card.Id)];
            database.Warnings.Add("No card names were found, so cards are shown by Konami id.");
            return database;
        }

        public static CardDatabase Load(string gameFolder, string language = "E")
        {
            var database = new CardDatabase { Language = language };
            var files = GameFiles.FromGameFolder(gameFolder);

            byte[]? intId = files.ReadBytes(Path.Combine("bin", "CARD_IntID.bin"));
            byte[]? index = files.ReadBytes(Path.Combine("bin", $"CARD_Indx_{language}.bin"));
            byte[]? names = files.ReadBytes(Path.Combine("bin", $"CARD_Name_{language}.bin"));
            if (intId == null || index == null || names == null)
                database.Warnings.Add($"The card name files for language {language} were not found in {gameFolder} (loose, or in YGO_2020.dat).");
            else
                database.ReadGameNames(intId, index, names);

            database.ReadCustomCards(Path.Combine(gameFolder, "Yu-Gi-Oh-Ex", "cards.json"));
            database._sorted = [.. database._byId.Values.OrderBy(card => card.Name, StringComparer.OrdinalIgnoreCase).ThenBy(card => card.Id)];
            return database;
        }

        private void ReadGameNames(byte[] intIdData, byte[] index, byte[] names)
        {
            var internalIds = new ushort[intIdData.Length / 2];
            for (int i = 0; i < internalIds.Length; i++)
                internalIds[i] = BinaryPrimitives.ReadUInt16LittleEndian(intIdData.AsSpan(i * 2));
            int indexCount = index.Length / 8;

            for (int i = 0; i < internalIds.Length; i++)
            {
                int internalId = internalIds[i];
                int konamiId = StartingCollectionBuilder.FirstKonamiId + i;
                if (internalId == 0 || internalId >= indexCount)
                    continue;

                long offset = BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(internalId * 8));
                if (offset >= names.Length)
                    continue;

                int end = (int)offset;
                while (end + 1 < names.Length && (names[end] != 0 || names[end + 1] != 0))
                    end += 2;

                string name = Encoding.Unicode.GetString(names, (int)offset, end - (int)offset);
                if (name.Length > 0)
                    _byId[konamiId] = new CardEntry(konamiId, name);
            }
        }

        private void ReadCustomCards(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
                var array = root as JsonArray ?? root?["cards"] as JsonArray;
                if (array == null)
                    return;

                foreach (var node in array.OfType<JsonObject>())
                {
                    if (node["id"] is JsonValue id && id.TryGetValue<int>(out int value))
                        _byId[value] = new CardEntry(value, node["name"]?.GetValue<string>() ?? $"Custom card {value}", true);
                }
            }
            catch (Exception ex)
            {
                Warnings.Add($"Could not read {path}: {ex.Message}");
            }
        }

        /// <summary>The highest card id the save can hold.</summary>
        public const int MaxCardId = 19999;

        /// <summary>The card for an id. An id with no known name is still a card (named "#id"), so the editors always work with Konami ids.</summary>
        public CardEntry ById(int id) => _byId.TryGetValue(id, out var card) ? card : new CardEntry(id, $"#{id}");

        /// <summary>True when the game's card names were read (false means only ids are available).</summary>
        public bool HasNames => Warnings.All(warning => !warning.StartsWith("No card names")) && _byId.Count > 0;

        public bool TryGet(int id, out CardEntry card)
        {
            card = ById(id);
            return id >= 1 && id <= MaxCardId;
        }

        /// <summary>The card's name, or "#id" for an id nobody has a name for.</summary>
        public string NameOf(int id) => _byId.TryGetValue(id, out var card) ? card.Name : $"#{id}";

        /// <summary>
        /// Every card whose name contains all the words typed (any case), or whose id is the number typed.
        /// An empty query returns everything.
        /// </summary>
        public IEnumerable<CardEntry> Search(string query)
        {
            query = query.Trim();
            if (query.Length == 0)
                return _sorted;

            if (int.TryParse(query, out int id) && id >= 1 && id <= MaxCardId)
                return [ById(id), .. _sorted.Where(card => card.Id != id && card.Name.Contains(query, StringComparison.OrdinalIgnoreCase))];

            string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return _sorted.Where(card => words.All(word => card.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>
        /// Turns lines of card names (or ids) into cards. A name matches when it is the card's exact name, otherwise when it is the
        /// only card containing it. Anything else is returned in <paramref name="unmatched"/>. Lines like "3x Dark Magician" and
        /// "Dark Magician x3" are understood; the count is returned with the card.
        /// </summary>
        public List<(CardEntry Card, int Count)> Match(string text, out List<string> unmatched)
        {
            var found = new List<(CardEntry, int)>();
            unmatched = [];
            foreach (string raw in text.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                string line = raw.Trim();
                if (line.Length == 0)
                    continue;

                int count = 1;
                var leading = System.Text.RegularExpressions.Regex.Match(line, @"^(\d+)\s*x\s+(.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                var trailing = System.Text.RegularExpressions.Regex.Match(line, @"^(.*?)\s+x\s*(\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (leading.Success)
                {
                    count = int.Parse(leading.Groups[1].Value);
                    line = leading.Groups[2].Value.Trim();
                }
                else if (trailing.Success)
                {
                    count = int.Parse(trailing.Groups[2].Value);
                    line = trailing.Groups[1].Value.Trim();
                }

                CardEntry? match = null;
                if (int.TryParse(line, out int id) && id >= 1 && id <= MaxCardId)
                    match = ById(id);
                else
                {
                    var exact = _sorted.Where(card => card.Name.Equals(line, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (exact.Count >= 1)
                        match = exact[0];
                    else
                    {
                        var partial = _sorted.Where(card => card.Name.Contains(line, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
                        if (partial.Count == 1)
                            match = partial[0];
                    }
                }

                if (match == null)
                    unmatched.Add(raw.Trim());
                else
                    found.Add((match, count));
            }
            return found;
        }
    }
}
