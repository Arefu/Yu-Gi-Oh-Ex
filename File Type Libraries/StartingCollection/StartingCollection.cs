using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

using DeckData;
using Types;

namespace StartingCollection
{
    /// <summary>Finds a game file in the first of several folders that has it (the way the game prefers loose files to the archive).</summary>
    public sealed class GameFiles
    {
        public List<string> Roots { get; } = [];

        /// <summary>YGO_2020.toc / .dat in the game folder: where the files are when nothing has been extracted (null if there isn't one).</summary>
        public TocArchive? Toc { get; private set; }

        public GameFiles(IEnumerable<string> roots)
        {
            Roots.AddRange(roots.Where(Directory.Exists));
        }

        /// <summary>The usual places under the game folder that hold extracted data.</summary>
        public static GameFiles FromGameFolder(string gameFolder) => new(
        [
            gameFolder,
            Path.Combine(gameFolder, "YGO_2020"),
            Path.Combine(gameFolder, "MODS", "OVERRIDES", "REQ"),
            Path.Combine(gameFolder, "Remaining Files"),
        ])
        {
            Toc = TocArchive.TryOpen(Path.Combine(gameFolder, "YGO_2020.toc")),
        };

        /// <summary>
        /// WolfX sets this to the game data it has open (the game folder's .dat or an extracted folder of any name), so everything that reads
        /// the game's files here reads the same files the editors do. Null elsewhere (DuelIt).
        /// </summary>
        public static Func<string, byte[]?>? OpenData { get; set; }

        /// <summary>The file's bytes: a loose file in one of the folders if there is one, otherwise the copy inside YGO_2020.dat.</summary>
        public byte[]? ReadBytes(string relativePath)
        {
            if (OpenData?.Invoke(relativePath) is { } open)
                return open;
            string? path = Find(relativePath);
            return path != null ? System.IO.File.ReadAllBytes(path) : Toc?.Read(relativePath);
        }

        public string? Find(string relativePath)
        {
            foreach (string root in Roots)
            {
                string path = Path.Combine(root, relativePath);
                if (System.IO.File.Exists(path))
                    return path;
            }
            return null;
        }
    }

    public sealed class StartingDeck
    {
        public uint Id { get; set; }
        public string File { get; set; } = "";
        public string Title { get; set; } = "";
        public List<int> Main { get; set; } = [];
        public List<int> Extra { get; set; } = [];
        public List<int> Side { get; set; } = [];
    }

    public sealed class StartingCard
    {
        /// <summary>The Konami id the game shows the card under (when several ids share art, the highest one).</summary>
        public int Id { get; set; }

        /// <summary>The game counts copies per internal id, not per Konami id.</summary>
        public int InternalId { get; set; }

        public int Copies { get; set; }
    }

    public sealed class StartingCollectionData
    {
        public string Language { get; set; } = "E";

        /// <summary>The points a new profile starts with (from the game's new-save initialisation).</summary>
        public int StartingPoints { get; set; } = 1000;

        public List<StartingDeck> StarterDecks { get; set; } = [];
        public List<StartingCard> Cards { get; set; } = [];

        /// <summary>Files the data was read from.</summary>
        public Dictionary<string, string> Sources { get; set; } = [];

        public List<string> Warnings { get; set; } = [];

        public string Notes { get; set; } =
            "A new profile is given every card of the starter decks: each card in a deck adds one copy, up to 3 per internal card id " +
            "(YGO::SAVE::RebuildLiveUnlockCounts -> GrantDeckTemplateCards). Content pack decks are only granted when their pack is owned.";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        public static StartingCollectionData FromJson(string json) =>
            JsonSerializer.Deserialize<StartingCollectionData>(json, JsonOptions) ?? new StartingCollectionData();
    }

    public static class StartingCollectionBuilder
    {
        /// <summary>The decks a new profile owns: g_InitialDeckTemplateSlots in the game, in the order the game grants them.</summary>
        public static readonly uint[] StarterDeckIds = [380, 381, 383, 382, 384];

        public const int MaxCopies = 3;

        public const int FirstKonamiId = 3900;
        private const int LastKonamiId = 14968;

        /// <summary>Reads the game files and works out what a new profile owns.</summary>
        public static StartingCollectionData Build(GameFiles files, string language = "E")
        {
            var result = new StartingCollectionData { Language = language };

            string deckDataPath = Path.Combine("main", $"deckdata_{language}.bin");
            byte[]? deckDataBytes = files.ReadBytes(deckDataPath);
            byte[]? decksArchive = files.ReadBytes("decks.zib");
            byte[]? internalIdBytes = files.ReadBytes(Path.Combine("bin", "CARD_IntID.bin"));

            if (deckDataBytes == null) result.Warnings.Add($"main/deckdata_{language}.bin was not found.");
            if (decksArchive == null) result.Warnings.Add("decks.zib was not found.");
            if (internalIdBytes == null) result.Warnings.Add("bin/CARD_IntID.bin was not found.");
            if (deckDataBytes == null || decksArchive == null || internalIdBytes == null)
                return result;

            result.Sources["deckdata"] = deckDataPath;
            result.Sources["decks"] = "decks.zib";
            result.Sources["internalIds"] = Path.Combine("bin", "CARD_IntID.bin");

            var deckData = DeckDataFile.Parse(deckDataBytes);
            var decks = ZibArchive.Parse(decksArchive);
            ushort[] internalIds = ReadInternalIds(internalIdBytes);

            var copies = new Dictionary<int, int>();          // internal id -> copies
            var canonicalId = new Dictionary<int, int>();     // internal id -> Konami id
            for (int id = FirstKonamiId; id <= LastKonamiId; id++)
            {
                int internalId = InternalIdOf(internalIds, id);
                if (internalId != 0)
                    canonicalId[internalId] = id; // the game's own reverse table: the last (highest) id wins
            }

            foreach (uint deckId in StarterDeckIds)
            {
                var record = deckData.Find(deckId);
                if (record == null)
                {
                    result.Warnings.Add($"Starter deck {deckId} is not in deckdata.");
                    continue;
                }

                byte[]? ydc = decks.Entries.FirstOrDefault(entry => entry.Name.Equals(record.FileName + ".ydc", StringComparison.OrdinalIgnoreCase))?.Data;
                if (ydc == null)
                {
                    result.Warnings.Add($"{record.FileName}.ydc is not in decks.zib.");
                    continue;
                }

                var deck = YdcDeck.Parse(ydc);
                result.StarterDecks.Add(new StartingDeck
                {
                    Id = deckId,
                    File = record.FileName,
                    Title = record.Title,
                    Main = deck.Main.Select(card => (int)card).ToList(),
                    Extra = deck.Extra.Select(card => (int)card).ToList(),
                    Side = deck.Side.Select(card => (int)card).ToList(),
                });

                foreach (ushort card in deck.AllCards)
                {
                    int internalId = InternalIdOf(internalIds, card);
                    if (internalId == 0)
                        continue; // the game ignores cards it has no internal id for

                    copies.TryGetValue(internalId, out int current);
                    copies[internalId] = Math.Min(MaxCopies, current + 1);
                }
            }

            foreach (var (internalId, count) in copies.OrderBy(pair => canonicalId.GetValueOrDefault(pair.Key)))
            {
                result.Cards.Add(new StartingCard
                {
                    Id = canonicalId.GetValueOrDefault(internalId),
                    InternalId = internalId,
                    Copies = count,
                });
            }

            return result;
        }

        /// <summary>bin/CARD_IntID.bin is a plain table of 16 bit internal ids, indexed by Konami id - 3900.</summary>
        public static ushort[] ReadInternalIds(byte[] data)
        {
            var ids = new ushort[data.Length / 2];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i * 2));
            return ids;
        }

        public static int InternalIdOf(ushort[] table, int konamiId)
        {
            int index = konamiId - FirstKonamiId;
            return (uint)index < table.Length ? table[index] : 0;
        }
    }
}
