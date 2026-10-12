using System.Text.Json;
using System.Text.Json.Nodes;
using Types;
using Wolf.Editors;

namespace WolfEx
{
    /// <summary>A game card on the Effects page (mode "Game cards"): its text, the game's effect as the Effect library reads it, and its override.</summary>
    internal sealed class GameEffectCard
    {
        public int Id;
        public string Name = "";
        public string Kind = "";
        public string Text = "";
        public string GameScript = "";       // effect_reference.json's reading of the game's effect ("" = none read)
        public bool Overridden;              // effects.json "overridden": the game plays EffectSource / Effect instead of its own effect
        public string EffectSource = "";
        public JsonObject? Effect;           // compiled: the "effectClone" Yu-Gi-Oh-Effects runs; null with Overridden = no effect at all
        public JsonObject? Lock;             // "lock" of its own: a continuous lock added while the card keeps the game's effect (Lock.cpp)

        public override string ToString() => (Overridden ? "✎ " : "") + Name + "  (" + Id + ")";
    }

    /// <summary>
    /// Yu-Gi-Oh-Ex\effects.json: the game's own cards with a changed effect, {"cards": [{"id", "name", "overridden", "effectScript", "effectClone"}]}.
    /// Yu-Gi-Oh-Effects (EffectClone.cpp) plays a card with "overridden": true as a clone of its "effectClone" source, keeping its own id;
    /// without "effectClone" the card has no effect. docs/EffectSystem.md section 43.
    /// </summary>
    internal static class GameEffectsFile
    {
        public const string FileName = "effects.json";

        public static Dictionary<int, JsonObject> Load(string path)
        {
            var entries = new Dictionary<int, JsonObject>();
            if (ContentLoadState.Read(path) is { } root)
                foreach (JsonObject entry in ContentLoadState.Cards(root))
                {
                    ContentLoadState.Fields<string>(entry, "name", "effectScript");
                    ContentLoadState.Fields<bool>(entry, "overridden");
                    foreach (string field in new[] { "effectClone", "lock" })
                        if (entry.ContainsKey(field) && entry[field] is not JsonObject)
                            throw new InvalidDataException("Expected an object: " + field);
                    entries[entry["id"]!.GetValue<int>()] = (JsonObject)entry.DeepClone();
                }
            return entries;
        }

        public static JsonObject Entry(GameEffectCard card)
        {
            var entry = new JsonObject { ["id"] = card.Id, ["name"] = card.Name, ["overridden"] = card.Overridden };
            if (!string.IsNullOrEmpty(card.EffectSource))
                entry["effectScript"] = card.EffectSource;
            if (card.Effect != null)
                entry["effectClone"] = card.Effect.DeepClone();
            if (card.Lock != null)
                entry["lock"] = card.Lock.DeepClone();
            return entry;
        }

        public static string Text(IEnumerable<GameEffectCard> overridden) =>
            new JsonObject { ["cards"] = new JsonArray(overridden.OrderBy(card => card.Id).Select(card => (JsonNode)Entry(card)).ToArray()) }
                .ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        /// <summary>Every game card (names from the card catalog, English text from bin\CARD_Desc_E), with the effect_reference.json reading.</summary>
        public static List<GameEffectCard> ReadGameCards(IWin32Window? owner)
        {
            var reference = new Dictionary<int, (string Script, string Text, string Kind)>();
            string path = Path.Combine(AppContext.BaseDirectory, "effect_reference.json");
            try
            {
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path))?["cards"] is JsonArray cards)
                    foreach (var node in cards.OfType<JsonObject>())
                        if (node["id"]?.GetValue<int>() is int id)
                            reference[id] = (node["script"]?.GetValue<string>() ?? "", node["text"]?.GetValue<string>() ?? "", node["kind"]?.GetValue<string>() ?? "");
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidOperationException)
            {
                // no reference: the cards still list, without the game's effect as script
            }

            CardIdMap? ids = null;
            CardTextTable? english = null;
            if (GameFolderFiles.Current is { } files)
            {
                try
                {
                    ids = files.Read(CardIdMap.GamePath) is { } map ? CardIdMap.Parse(map) : null;
                    if (files.Read(CardTextTable.IndxPath('E')) is { } indx && files.Read(CardTextTable.NamePath('E')) is { } names &&
                        files.Read(CardTextTable.DescPath('E')) is { } descs)
                        english = CardTextTable.Parse(indx, names, descs);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
                {
                    // texts are a convenience here
                }
            }

            var list = new List<GameEffectCard>();
            foreach (var entry in WolfX.Types.CardCatalog.Get(owner).Cards)
            {
                if (entry.IsCustom || entry.Id <= 0 || entry.Id >= 15300)
                    continue;
                reference.TryGetValue(entry.Id, out var known);
                int i = ids?.InternalOf(entry.Id) ?? 0;
                string text = english != null && i > 0 && i < english.Descs.Count ? english.Descs[i] : known.Text ?? "";
                list.Add(new GameEffectCard { Id = entry.Id, Name = entry.Name, Kind = known.Kind ?? "", Text = text, GameScript = known.Script ?? "" });
            }
            return list;
        }
    }
}
