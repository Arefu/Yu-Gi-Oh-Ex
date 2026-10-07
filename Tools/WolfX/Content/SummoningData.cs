using System.Text.Json;
using System.Text.Json.Nodes;

namespace WolfEx
{
    /// <summary>
    /// The game's own summoning requirements (summon_tables.json next to WolfX, made by docs/effect-scripts/build_summon_tables.py from the
    /// exe's tables): what a game card needs before summoning.json changes it, in the same keys cards.json uses.
    /// </summary>
    internal static class GameSummonTables
    {
        private static JsonObject? _root;

        private static JsonObject Root
        {
            get
            {
                if (_root != null)
                    return _root;
                try
                {
                    string path = Path.Combine(AppContext.BaseDirectory, "summon_tables.json");
                    _root = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject : null;
                }
                catch (JsonException)
                {
                    _root = null;
                }
                return _root ??= [];
            }
        }

        /// <summary>The game's requirements of this card as cards.json keys ("fusion", "ritualSpell", "ritualMonsters", "synchro", "xyz", "link").</summary>
        public static JsonObject For(int id)
        {
            var data = new JsonObject();
            string key = id.ToString();
            if (Root["fusion"]?[key] is JsonArray fusion)
                data["fusion"] = fusion.DeepClone();
            if (Root["ritual"] is JsonArray ritual)
            {
                var monsters = new JsonArray();
                foreach (var row in ritual.OfType<JsonArray>())
                {
                    int monster = row[0]!.GetValue<int>(), spell = row[1]!.GetValue<int>();
                    if (monster == id)
                        data["ritualSpell"] = spell;
                    if (spell == id)
                        monsters.Add(monster);
                }
                if (monsters.Count > 0)
                    data["ritualMonsters"] = monsters;
            }
            if (Root["synchro"]?[key] is JsonObject synchro)
            {
                var copy = new JsonObject();
                foreach (var (name, value) in synchro)
                    if (!(value is JsonValue v && ((v.TryGetValue<int>(out int n) && n == 0 && name is "tuner" or "nonTuner") || (v.TryGetValue<bool>(out bool b) && !b))))
                        copy[name] = value?.DeepClone();
                data["synchro"] = copy;
            }
            if (Root["xyz"]?[key] is JsonObject xyz)
            {
                var copy = new JsonObject();
                foreach (var (name, value) in xyz)
                    if (!(name == "material" && value is JsonValue v && v.TryGetValue<int>(out int n) && n == 0))
                        copy[name] = value?.DeepClone();
                data["xyz"] = copy;
            }
            if (Root["link"]?[key] is JsonObject link)
            {
                var copy = new JsonObject();
                foreach (var (name, value) in link)
                    if (!(value is JsonValue v && v.TryGetValue<int>(out int n) && n == 0))
                        copy[name] = value?.DeepClone();
                data["link"] = copy;
            }
            return data;
        }
    }

    /// <summary>
    /// Yu-Gi-Oh-Ex\summoning.json: changed summoning requirements of the game's own cards, {"cards": [{"id", "name", keys...}]} in the keys
    /// cards.json uses. Yu-Gi-Oh-Effects reads it after cards.json (Fusion.cpp, Ritual.cpp, SynchroXyz.cpp; docs/EffectSystem.md section 41).
    /// </summary>
    internal sealed class SummoningFile
    {
        public const string FileName = "summoning.json";
        private static readonly string[] Keys = ["fusion", "fusionLooser", "ritualSpell", "ritualMonsters", "synchro", "xyz", "link"];

        private readonly string _path;
        private readonly SortedDictionary<int, JsonObject> _cards = [];

        public bool Dirty { get; private set; }

        private SummoningFile(string path) => _path = path;

        public static SummoningFile Load(string path)
        {
            var file = new SummoningFile(path);
            try
            {
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject root && root["cards"] is JsonArray cards)
                    foreach (var entry in cards.OfType<JsonObject>())
                        if (entry["id"] is JsonValue id && id.TryGetValue<int>(out int value))
                            file._cards[value] = (JsonObject)entry.DeepClone();
            }
            catch (JsonException)
            {
                // a broken file starts empty; saving writes a good one
            }
            return file;
        }

        public bool Has(int id) => _cards.ContainsKey(id);

        /// <summary>The card's entry, or null when it uses the game's requirements.</summary>
        public JsonObject? Get(int id) => _cards.GetValueOrDefault(id);

        /// <summary>Keeps this data as the card's requirements (the editor changes it in place afterwards).</summary>
        public void Set(int id, string name, JsonObject data)
        {
            data["id"] = id;
            data["name"] = name;
            _cards[id] = data;
            Dirty = true;
        }

        public void Touch() => Dirty = true;

        public void Remove(int id)
        {
            if (_cards.Remove(id))
                Dirty = true;
        }

        public IEnumerable<int> Ids => _cards.Keys;

        public void Save()
        {
            var list = new JsonArray();
            foreach (var (id, entry) in _cards)
            {
                if (!Keys.Any(entry.ContainsKey))
                    continue;
                var copy = new JsonObject { ["id"] = id, ["name"] = entry["name"]?.DeepClone() };
                foreach (string key in Keys)
                    if (entry[key] is { } value)
                        copy[key] = value.DeepClone();
                list.Add(copy);
            }
            if (list.Count == 0)
            {
                if (File.Exists(_path))
                    File.Delete(_path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.WriteAllText(_path, new JsonObject { ["cards"] = list }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            Dirty = false;
        }
    }
}
