using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CARD_Named;

namespace WolfEx
{
    /// <summary>
    /// Archetype code -> name for the card editor. The game's own archetypes (codes below 419) are named from the known game files
    /// (CARD_Named.bin, named by Archetypes.ps1); custom archetypes (419 up) are the ones in Yu-Gi-Oh-Ex/Archetypes.json. The vanilla
    /// engine only knows its own codes, so a custom archetype is only ever seen by our own code (the Yu-Gi-Oh-Cards hook and, later,
    /// effect scripts) - see the ygo-effects-moonshot-plan memory.
    /// </summary>
    internal static class ArchetypeCatalog
    {
        public const int FirstCustomCode = 419;

        private static string _path = "";
        private static readonly Dictionary<int, string> _custom = [];

        /// <summary>
        /// Reads the custom archetypes from Yu-Gi-Oh-Ex/Archetypes.json ({ "archetypes": [ { "code": 419, "name": "...", "names": { "E": "..." } } ] };
        /// the file also lists the game's own, which come from the library's embedded copy and are ignored here).
        /// </summary>
        public static string? LoadError { get; private set; }

        private static JsonObject ReadRoot()
        {
            if (!File.Exists(_path)) return new JsonObject { ["archetypes"] = new JsonArray() };
            var root = JsonNode.Parse(File.ReadAllText(_path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (root is not JsonObject obj || obj["archetypes"] is not JsonArray entries)
                throw new InvalidDataException("Expected an object with an archetypes array; the existing file was left unchanged.");
            var codes = new HashSet<int>();
            foreach (var entry in entries)
                if (entry is not JsonObject item || item["code"] is not JsonValue value || !value.TryGetValue<int>(out int code) ||
                    code <= 0 || !codes.Add(code) || item["name"] is not JsonValue name || !name.TryGetValue<string>(out _))
                    throw new InvalidDataException("Invalid or duplicate archetype entry; the existing file was left unchanged.");
            return obj;
        }

        public static void Load(string extraCardsFolder)
        {
            _path = Path.Combine(extraCardsFolder, "Archetypes.json");
            _custom.Clear();
            LoadError = null;
            try
            {
                var entries = (JsonArray)ReadRoot()["archetypes"]!;
                foreach (var entry in entries.Cast<JsonObject>())
                {
                    int code = entry["code"]!.GetValue<int>();
                    if (code >= FirstCustomCode) _custom[code] = entry["name"]!.GetValue<string>();
                }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException)
            {
                _custom.Clear();
                LoadError = $"Could not read {_path}: {ex.Message} Repair the file before adding an archetype.";
            }
        }

        public static string NameOf(int code) => _custom.TryGetValue(code, out var name) ? name : Card_Named.NameOf(code);

        /// <summary>The code of an archetype by its English name (case-insensitive); 0 when there is none.</summary>
        public static int CodeOf(string name) =>
            Codes().FirstOrDefault(code => string.Equals(NameOf(code), name, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<int> Codes() =>
            Card_Named.Names.Keys.Where(code => code > 0 && code < FirstCustomCode).Concat(_custom.Keys).Distinct().OrderBy(code => code);

        public static string Describe(IEnumerable<int> codes)
        {
            var list = codes.OrderBy(code => code).Select(code => $"{code} {NameOf(code)}").ToList();
            return list.Count == 0 ? "(none)" : string.Join(", ", list);
        }

        /// <summary>A new custom archetype: the next free code from 419 up, added to Archetypes.json (the rest of the file is kept as it is).</summary>
        public static int Add(string name)
        {
            if (string.IsNullOrWhiteSpace(_path)) throw new InvalidOperationException("Pick the game folder first.");
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("An archetype needs a name.");
            // Re-read before writing: a failed load or an external edit must never become an empty replacement.
            JsonObject root = ReadRoot();
            var entries = (JsonArray)root["archetypes"]!;
            int highest = entries.Cast<JsonObject>().Select(e => e["code"]!.GetValue<int>()).DefaultIfEmpty(FirstCustomCode - 1).Max();
            int code = Math.Max(FirstCustomCode, checked(highest + 1));
            entries.Add(new JsonObject { ["code"] = code, ["name"] = name, ["names"] = new JsonObject { ["E"] = name } });
            var options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            ContentSaveBatch.Write(new Dictionary<string, byte[]> { [_path] = ContentSaveBatch.Text(root.ToJsonString(options) + Environment.NewLine) });
            _custom[code] = name;
            LoadError = null;
            return code;
        }
    }

    /// <summary>Pick any number of archetypes for a card, or make a new one (the shared picker, with the catalog's names and custom codes).</summary>
    internal static class ArchetypeDialog
    {
        public static Wolf.Editors.ArchetypePickerDialog Create(string cardName, IEnumerable<int> current) =>
            new(cardName, current,
                () => ArchetypeCatalog.Codes().Select(code => (code, $"{code} - {ArchetypeCatalog.NameOf(code)}{(code >= ArchetypeCatalog.FirstCustomCode ? "  (custom)" : "")}")),
                ArchetypeCatalog.Add);
    }
}
