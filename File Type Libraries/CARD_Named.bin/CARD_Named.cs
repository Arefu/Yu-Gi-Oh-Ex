using System.Text.Json;

namespace CARD_Named
{
    /// <summary>
    /// bin/CARD_Named.bin: the game's archetypes. A code (1..418 in the game) is the index of a list of Konami ids; the engine's
    /// Is_CardInNamedArchetype(id, code) is a binary search of that list (see the ygo-effects-moonshot-plan memory).
    /// Layout, all u16: [0] archetype count, [1] total ids, then per archetype (offset, length) into the pool that follows.
    /// Lists are sorted ascending. A card can be in any number of archetypes.
    /// </summary>
    public static class Card_Named
    {
        /// <summary>Archetype code -> the Konami ids in it (sorted).</summary>
        public static Dictionary<int, List<int>> CardsInArchetype = [];

        /// <summary>The game's archetype count as read (its highest code + 1); codes from here up are custom archetypes.</summary>
        public static int ArchetypeCount;

        private static Dictionary<int, Dictionary<string, string>>? _names;

        /// <summary>
        /// Code -> language ("E" English, "F", "G", "I", "S", "J") -> name, from the embedded Archetypes.json (made by Archetypes.ps1
        /// from the game's own files: each list is named by the YGOPRODeck archetype most of its cards have, and the per-language
        /// names are the game's "Related to: ..." tags in taginfo_&lt;lang&gt;.bin). Codes nobody could name are "Archetype N".
        /// </summary>
        public static Dictionary<int, Dictionary<string, string>> Names
        {
            get
            {
                if (_names != null)
                    return _names;

                _names = [];
                using var stream = typeof(Card_Named).Assembly.GetManifestResourceStream("CARD_Named.Archetypes.json");
                if (stream != null)
                {
                    using var doc = JsonDocument.Parse(stream);
                    foreach (var entry in doc.RootElement.GetProperty("archetypes").EnumerateArray())
                    {
                        int code = entry.GetProperty("code").GetInt32();
                        var names = new Dictionary<string, string> { ["E"] = entry.GetProperty("name").GetString() ?? $"Archetype {code}" };
                        if (entry.TryGetProperty("names", out var localized))
                        {
                            foreach (var pair in localized.EnumerateObject())
                                names[pair.Name] = pair.Value.GetString() ?? names["E"];
                        }
                        _names[code] = names;
                    }
                }
                return _names;
            }
        }

        /// <summary>The archetype's name in a language (falls back to English).</summary>
        public static string NameOf(int code, string language = "E") =>
            Names.TryGetValue(code, out var names) ? (names.TryGetValue(language, out var name) ? name : names["E"]) : $"Archetype {code}";

        /// <summary>Every archetype code this Konami id is in (one or many).</summary>
        public static List<int> ArchetypesOf(int konamiId) =>
            CardsInArchetype.Where(pair => pair.Value.BinarySearch(konamiId) >= 0).Select(pair => pair.Key).OrderBy(code => code).ToList();

        public const string GamePath = @"bin\CARD_Named.bin";

        public static void Load(string path) => Parse(File.ReadAllBytes(path));

        public static void Parse(byte[] bytes)
        {
            var u16 = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, u16, 0, u16.Length * 2);

            ArchetypeCount = u16.Length > 0 ? u16[0] : 0;
            CardsInArchetype = [];
            for (int code = 0; code < ArchetypeCount; code++)
            {
                int offset = u16[2 + 2 * code];
                int length = u16[3 + 2 * code];
                int start = 2 + 2 * ArchetypeCount + offset;
                CardsInArchetype[code] = u16.Skip(start).Take(length).Select(id => (int)id).ToList();
            }
        }

        /// <summary>The file in the game's layout (an unchanged file round-trips byte for byte).</summary>
        public static byte[] ToBytes()
        {
            int count = CardsInArchetype.Count == 0 ? 0 : CardsInArchetype.Keys.Max() + 1;
            count = Math.Max(count, ArchetypeCount);

            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)count);
                writer.Write((ushort)CardsInArchetype.Values.Sum(list => list.Count));

                ushort offset = 0;
                for (int code = 0; code < count; code++)
                {
                    ushort length = (ushort)(CardsInArchetype.TryGetValue(code, out var list) ? list.Count : 0);
                    writer.Write(offset);
                    writer.Write(length);
                    offset += length;
                }
                for (int code = 0; code < count; code++)
                {
                    if (!CardsInArchetype.TryGetValue(code, out var list))
                        continue;
                    foreach (int id in list.OrderBy(id => id))
                        writer.Write((ushort)id);
                }
            }
            return stream.ToArray();
        }

        public static void Save(string path) => File.WriteAllBytes(path, ToBytes());
    }
}
