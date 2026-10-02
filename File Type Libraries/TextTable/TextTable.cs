using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Types
{
    /// <summary>Which text table (the pair's file name prefix).</summary>
    public enum TextTableKind
    {
        /// <summary>bin/WORD_Indx_#.bin + WORD_Text_#.bin: the words on card frames (attributes, types, card kinds...).</summary>
        Word,

        /// <summary>bin/DLG_Indx_#.bin + DLG_Text_#.bin: the duel's prompts and effect choice texts.</summary>
        Dlg,
    }

    /// <summary>
    /// An Indx + Text pair (YGO::CARDS::Setup_CardPropTable 0x14076BFC6 loads both kinds, through LoadFileFromArchives, so loose copies
    /// in YGO_2020\bin replace them):
    ///
    ///   _Indx_#.bin   (count + 1) x u32 byte offset into the text; the last one is the end of the text (no string there)
    ///   _Text_#.bin   UTF-16LE strings in order, each with a null, padded with zeros to 4 bytes
    ///
    /// The game keeps (index size / 4) as the count and reads string i at Text + Indx[i]. After loading it turns full-width A-Z and
    /// a-z (U+FF21-FF5A) into ASCII. Getters: Get_WordText 0x14076D7B0, Get_DlgText 0x14076D770.
    /// </summary>
    public sealed class TextTable
    {
        /// <summary>The language letters the game ships these in.</summary>
        public static readonly char[] Languages = ['E', 'F', 'G', 'I', 'J', 'R', 'S'];

        public const string GameFolder = "bin";

        public List<string> Entries { get; } = [];

        public static string Prefix(TextTableKind kind) => kind == TextTableKind.Word ? "WORD" : "DLG";

        public static string IndexName(TextTableKind kind, char language) => $"{Prefix(kind)}_Indx_{char.ToUpperInvariant(language)}.bin";

        public static string TextName(TextTableKind kind, char language) => $"{Prefix(kind)}_Text_{char.ToUpperInvariant(language)}.bin";

        public static string IndexGamePath(TextTableKind kind, char language) => Path.Combine(GameFolder, IndexName(kind, language));

        public static string TextGamePath(TextTableKind kind, char language) => Path.Combine(GameFolder, TextName(kind, language));

        /// <summary>Loads a pair from a folder (the folder that has WORD_Indx_E.bin etc.).</summary>
        public static TextTable Load(string folder, TextTableKind kind, char language) =>
            Parse(File.ReadAllBytes(Path.Combine(folder, IndexName(kind, language))), File.ReadAllBytes(Path.Combine(folder, TextName(kind, language))));

        public static TextTable Parse(byte[] index, byte[] text)
        {
            if (index.Length % 4 != 0)
                throw new InvalidDataException("The index isn't a whole number of u32 offsets.");
            int offsets = index.Length / 4;
            var table = new TextTable();
            for (int i = 0; i < offsets; i++)
            {
                int start = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(4 * i)));
                if (start > text.Length)
                    throw new InvalidDataException($"Offset {i} (0x{start:X}) is past the end of the text.");
                // the last offset marks the end of the text: no string there
                if (i == offsets - 1 && start == text.Length)
                    break;
                int end = start;
                while (end + 1 < text.Length && (text[end] != 0 || text[end + 1] != 0))
                    end += 2;
                table.Entries.Add(Encoding.Unicode.GetString(text, start, end - start));
            }
            return table;
        }

        /// <summary>The two files' bytes, laid out as the game's own (they round-trip byte for byte).</summary>
        public (byte[] Index, byte[] Text) ToBytes()
        {
            using var text = new MemoryStream();
            var index = new byte[4 * (Entries.Count + 1)];
            for (int i = 0; i < Entries.Count; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(4 * i), (uint)text.Position);
                text.Write(Encoding.Unicode.GetBytes(Entries[i]));
                text.Write([0, 0]);
                while (text.Position % 4 != 0)
                    text.WriteByte(0);
            }
            BinaryPrimitives.WriteUInt32LittleEndian(index.AsSpan(4 * Entries.Count), (uint)text.Position);
            return (index, text.ToArray());
        }

        public void Save(string folder, TextTableKind kind, char language)
        {
            var (index, text) = ToBytes();
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, IndexName(kind, language)), index);
            File.WriteAllBytes(Path.Combine(folder, TextName(kind, language)), text);
        }

        /// <summary>
        /// What a WORD entry is for (the game reads it as a group start + a card value: Get_WordBracket +1, Get_WordAttribute +10,
        /// Get_WordRace +100, Get_WordKind +200).
        /// </summary>
        public static string WordMeaning(int index) => index switch
        {
            0 => "(unused)",
            >= 1 and <= 3 => $"bracket {index - 1}",
            >= 10 and <= 19 => $"attribute {index - 10}",
            20 or 21 => index == 20 ? "Spell (small)" : "Trap (small)",
            >= 50 and <= 59 => $"spell/trap icon {index - 50}",
            >= 60 and <= 69 => $"spell type {index - 60}",
            >= 70 and <= 79 => $"trap type {index - 70}",
            >= 100 and <= 129 => $"type (race) {index - 100}",
            130 or 131 => index == 130 ? "SPELL CARD" : "TRAP CARD",
            >= 200 and <= 299 => $"card kind {index - 200}",
            _ => "",
        };
    }

    /// <summary>
    /// Yu-Gi-Oh-Ex/text.json, which the Yu-Gi-Oh-Cards plugin reads (Text.cpp: it hooks Get_WordText / Get_DlgText):
    ///
    ///   { "word": { "246": { "E": "/Custom/Effect", "F": "..." } },
    ///     "dlg":  { "688": { "E": "Select @21@0 @3card@0." } } }
    ///
    /// A number past the game's entries adds one, a number it has replaces that language's text. Only what differs from the game is
    /// written, so the game's own text stays in its files.
    /// </summary>
    public static class TextTableJson
    {
        public const string FileName = "text.json";

        public static string Key(TextTableKind kind) => kind == TextTableKind.Word ? "word" : "dlg";

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

        /// <summary>Puts the JSON's entries for this table into the tables (a language the JSON leaves out keeps the game's text, or gets English for a new entry).</summary>
        public static int Apply(JsonObject root, TextTableKind kind, IDictionary<char, TextTable> tables)
        {
            if (root[Key(kind)] is not JsonObject section)
                return 0;
            int applied = 0;
            foreach (var (number, value) in section)
            {
                if (!int.TryParse(number, out int index) || index < 0 || value is not JsonObject languages)
                    continue;
                var texts = languages.Where(l => l.Key.Length > 0 && l.Value is JsonValue)
                    .ToDictionary(l => char.ToUpperInvariant(l.Key[0]), l => l.Value!.GetValue<string>());
                if (texts.Count == 0)
                    continue;
                string fallback = texts.GetValueOrDefault('E') ?? texts.Values.First();
                foreach (var (language, table) in tables)
                {
                    while (table.Entries.Count <= index)
                        table.Entries.Add("");
                    if (texts.TryGetValue(language, out string? text))
                        table.Entries[index] = text;
                    else if (table.Entries[index].Length == 0)
                        table.Entries[index] = fallback;   // a new entry with no text for this language: the plugin falls back to English too
                }
                applied++;
            }
            return applied;
        }

        /// <summary>This table's section: every entry that differs from the game's (baseline) or is past its end, per language.</summary>
        public static JsonObject Section(IDictionary<char, TextTable> tables, IDictionary<char, List<string>> baseline)
        {
            var section = new JsonObject();
            int count = tables.Values.Select(t => t.Entries.Count).DefaultIfEmpty(0).Max();
            for (int index = 0; index < count; index++)
            {
                var languages = new JsonObject();
                foreach (var (language, table) in tables.OrderBy(t => t.Key))
                {
                    if (index >= table.Entries.Count)
                        continue;
                    var game = baseline.TryGetValue(language, out var own) ? own : null;
                    bool added = game == null || index >= game.Count;
                    string text = table.Entries[index];
                    // a new entry's untranslated copies of the English text are left out: the plugin falls back to English
                    if (added && language != 'E' && tables.TryGetValue('E', out var english) && index < english.Entries.Count && english.Entries[index] == text)
                        continue;
                    if (added || game![index] != text)
                        languages[language.ToString()] = text;
                }
                if (languages.Count > 0)
                    section[index.ToString()] = languages;
            }
            return section;
        }

        /// <summary>Writes this table's section into text.json, keeping the other table's section as it is.</summary>
        public static void Save(string path, TextTableKind kind, JsonObject section)
        {
            var root = Load(path);
            if (section.Count > 0)
                root[Key(kind)] = section;
            else
                root.Remove(Key(kind));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        }
    }
}
