using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WolfEx
{
    /// <summary>
    /// Summoning data for every card of a cards.json, read from the card texts (WolfX.exe --attach-summoning; docs/EffectSystem.md 36 and 40):
    /// <list type="bullet">
    /// <item>a Fusion Monster without "fusion" gets its recipe from its material line (<see cref="FusionMaterialText"/>, as the Effects
    /// tab's "Attach from card text" does);</item>
    /// <item>a Ritual Spell without an effect gets Black Luster Ritual's (<c>as("Black Luster Ritual");</c>, the Summoning tab's button: it
    /// Ritual Summons the monsters its table rows name);</item>
    /// <item>a Ritual Monster is paired with the Ritual Spell its text or the spell's text names ("ritualSpell" / "ritualMonsters"); one no
    /// spell names gets no pair and is summoned by the generic Ritual Spells only (Yu-Gi-Oh-Effects gives it a spell-0 row).</item>
    /// </list>
    /// Only the file's own Ritual Monsters are paired: a pair for one of the game's would replace that monster's own Ritual Spell.
    /// </summary>
    internal static class SummoningFromText
    {
        private static readonly JsonSerializerOptions Write = new()
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        private static string Text(JsonNode? node, string key) => node?[key] is JsonValue v && v.TryGetValue(out string? s) ? s : "";

        /// <summary>
        /// Effects from the card texts (WolfX.exe --attach-effects; CardTextTranslator, the Effects tab's "Attach from card text"), for the
        /// cards without one. Only a card whose WHOLE text translates gets its script: a card that ran part of its text would play wrong.
        /// The rest are listed in <paramref name="partialCsv"/> with what the translator could not run, the list to work through by hand.
        /// </summary>
        public static string AttachEffects(string path, string partialCsv)
        {
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var cards = root["cards"]!.AsArray().OfType<JsonObject>().ToList();
            var byName = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards)
                byName.TryAdd(Text(card, "name"), card);
            int CardId(string name) => byName.TryGetValue(name, out var c) ? (int)c["id"]! : EffectScriptCompiler.TryCardIdByName(name);
            static List<int> Archetypes(JsonObject c) => c["archetypes"] is JsonArray a ? a.Select(x => (int)x!).ToList() : [];
            var customArchetypes = cards.Select(c => (Text(c, "name"), Archetypes(c))).ToList();
            var archetypeCache = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            List<int> ArchetypeCodes(string name) => archetypeCache.TryGetValue(name, out var cached) ? cached
                : archetypeCache[name] = FusionMaterialText.ArchetypeCodesFor(name, customArchetypes);
            static string Csv(string s) => "\"" + s.Replace("\"", "\"\"").Replace("\r\n", " ").Replace('\n', ' ') + "\"";

            int candidates = 0, whole = 0, partial = 0, none = 0;
            var rows = new StringBuilder("id,name,kind,effects read,not run,why not run,script so far\n");
            foreach (var card in cards)
            {
                string kind = Text(card, "kind");
                bool effectCard = kind.Contains("Effect") || kind is "Spell" or "Trap" || kind.StartsWith("Pendulum") || kind.Contains("Toon")
                    || kind.Contains("Spirit") || kind.Contains("Union") || kind.Contains("Gemini");
                if (!effectCard || card["effectScript"] != null || card["effectClone"] != null || string.IsNullOrWhiteSpace(Text(card, "description")))
                    continue;
                candidates++;
                var result = CardTextTranslator.Translate(Text(card, "name"), kind, Text(card, "icon"), Text(card, "description"), Archetypes(card),
                    CardId, ArchetypeCodes);
                var compiled = result.Effects > 0 ? EffectScriptCompiler.Compile(result.Script) : null;
                if (compiled is { Ok: true } && result.NotRun.Count == 0)
                {
                    card["effectScript"] = result.Script;
                    card["effectClone"] = compiled.Compiled!.DeepClone();
                    if (result.Looser.Count > 0)
                        card["effectLooser"] = string.Join("; ", result.Looser);
                    whole++;
                    continue;
                }
                if (result.Effects > 0)
                    partial++;
                else
                    none++;
                rows.Append($"{card["id"]},{Csv(Text(card, "name"))},{kind},{result.Effects},{Csv(string.Join(" | ", result.NotRun))}," +
                            $"{Csv(string.Join(" | ", CardTextTranslator.LastReasons))},{Csv(result.Effects > 0 ? result.Script : "")}\n");
            }
            File.WriteAllText(path, root.ToJsonString(Write) + "\n", new UTF8Encoding(false));
            File.WriteAllText(partialCsv, rows.ToString(), new UTF8Encoding(false));
            return $"Cards with an effect to write: {candidates}\nWhole text translated, effect attached: {whole}\n" +
                   $"Part translated (listed, not attached): {partial}\nNothing translated (listed): {none}\n";
        }

        public static string Attach(string path)
        {
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var cards = root["cards"]!.AsArray().OfType<JsonObject>().ToList();
            var report = new StringBuilder();

            var byName = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in cards)
                byName.TryAdd(Text(card, "name"), card);
            int CardId(string name) => byName.TryGetValue(name, out var c) ? (int)c["id"]! : EffectScriptCompiler.TryCardIdByName(name);
            var customArchetypes = cards.Select(c => (Text(c, "name"), c["archetypes"] is JsonArray a ? a.Select(x => (int)x!).ToList() : new List<int>())).ToList();
            var archetypeCache = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            List<int> ArchetypeCodes(string name) => archetypeCache.TryGetValue(name, out var cached) ? cached
                : archetypeCache[name] = FusionMaterialText.ArchetypeCodesFor(name, customArchetypes);

            // ---- Fusion recipes
            int fusions = 0, recipes = 0;
            foreach (var card in cards.Where(c => Text(c, "kind").Contains("Fusion", StringComparison.OrdinalIgnoreCase)))
            {
                fusions++;
                if (card["fusion"] != null)
                {
                    recipes++;
                    continue;
                }
                var recipe = FusionMaterialText.TryParse(Text(card, "description"), CardId, ArchetypeCodes, out string why);
                if (recipe == null)
                {
                    report.AppendLine($"Fusion not read: {card["id"]} {Text(card, "name")}: {why}");
                    continue;
                }
                card["fusion"] = recipe.Materials.DeepClone();
                if (recipe.Dropped.Count > 0)
                    card["fusionLooser"] = string.Join("; ", recipe.Dropped);
                recipes++;
            }

            // ---- Synchro, Xyz and Link materials (ExtraDeckMaterialText, the Summoning tab's "Read card text"): what the text says beyond
            // the one condition per side the game's tables hold is kept in "<key>Looser"
            var extra = new Dictionary<string, (int Kinds, int Read)>();
            foreach (var card in cards)
            {
                string kind = Text(card, "kind"), description = Text(card, "description");
                string key = kind.StartsWith("Synchro", StringComparison.OrdinalIgnoreCase) ? "synchro"
                    : kind.StartsWith("Xyz", StringComparison.OrdinalIgnoreCase) ? "xyz"
                    : kind.StartsWith("Link", StringComparison.OrdinalIgnoreCase) ? "link" : "";
                if (key.Length == 0)
                    continue;
                var (kinds, read) = extra.GetValueOrDefault(key);
                kinds++;
                if (card[key] != null)
                {
                    extra[key] = (kinds, read + 1);
                    continue;
                }
                string why;
                JsonObject? materials;
                if (key == "synchro")
                    materials = ExtraDeckMaterialText.Synchro(description, CardId, ArchetypeCodes, out why);
                else if (key == "link")
                    materials = ExtraDeckMaterialText.Link(description, CardId, ArchetypeCodes, out why);
                else
                {
                    materials = ExtraDeckMaterialText.Xyz(description, ArchetypeCodes, out int level, out why);
                    int rank = card["level"] is JsonValue r && r.TryGetValue(out int rv) ? rv : 0;
                    if (materials != null && level != rank)
                        why = (why.Length > 0 ? why + "; " : "") + $"the text says Level {level}, the game uses the card's Rank ({rank})";
                }
                if (materials == null)
                {
                    report.AppendLine($"{key} not read: {card["id"]} {Text(card, "name")}: {why}");
                    extra[key] = (kinds, read);
                    continue;
                }
                card[key] = materials;
                if (why.Length > 0)
                    card[key + "Looser"] = why;
                extra[key] = (kinds, read + 1);
            }
            foreach (var (key, (kinds, read)) in extra)
                report.Insert(0, $"{key}: {kinds} cards, with materials now: {read}\n");

            // ---- Ritual Spells and Monsters
            static bool IsRitualMonster(JsonObject c) => Text(c, "kind").StartsWith("Ritual", StringComparison.OrdinalIgnoreCase);
            static bool IsRitualSpell(JsonObject c) => Text(c, "kind") == "Spell" && Text(c, "icon") == "Ritual";
            var spells = cards.Where(IsRitualSpell).ToList();
            var monsters = cards.Where(IsRitualMonster).ToList();

            var clone = EffectScriptCompiler.Compile("as(\"Black Luster Ritual\");");
            int spellEffects = 0;
            foreach (var spell in spells.Where(s => s["effectClone"] == null && s["effectScript"] == null))
            {
                if (!clone.Ok)
                {
                    report.AppendLine($"Ritual Spell effect not compiled: {clone.Error}");
                    break;
                }
                spell["effectScript"] = "as(\"Black Luster Ritual\");";
                spell["effectClone"] = clone.Compiled!.DeepClone();
                spellEffects++;
            }

            // a monster's own text ("You can Ritual Summon this card with "X".") wins; else a spell that names it
            var quoted = new Regex("\"([^\"]+)\"");
            int paired = 0;
            foreach (var monster in monsters.Where(m => m["ritualSpell"] == null))
            {
                string text = Text(monster, "description");
                var own = Regex.Match(text, "Ritual Summon(?:ed)? (?:this card )?(?:with|by) \"([^\"]+)\"", RegexOptions.IgnoreCase);
                JsonObject? spell = own.Success && byName.TryGetValue(own.Groups[1].Value, out var s1) && IsRitualSpell(s1) ? s1 : null;
                spell ??= spells.FirstOrDefault(s => quoted.Matches(Text(s, "description")).Any(m => m.Groups[1].Value.Equals(Text(monster, "name"), StringComparison.OrdinalIgnoreCase)));
                if (spell == null && own.Success && CardId(own.Groups[1].Value) is int gameSpell and > 0)
                {
                    monster["ritualSpell"] = gameSpell;   // a game Ritual Spell named on a new monster
                    paired++;
                    continue;
                }
                if (spell == null)
                {
                    report.AppendLine($"Ritual Monster without a Ritual Spell of its own (generic spells only): {monster["id"]} {Text(monster, "name")}");
                    continue;
                }
                monster["ritualSpell"] = (int)spell["id"]!;
                paired++;
            }

            File.WriteAllText(path, root.ToJsonString(Write) + "\n", new UTF8Encoding(false));
            report.Insert(0, $"Fusion Monsters: {fusions}, with a recipe now: {recipes}\nRitual Spells: {spells.Count}, given Black Luster Ritual's effect: {spellEffects}\n" +
                             $"Ritual Monsters: {monsters.Count}, paired with a Ritual Spell: {paired}\n");
            return report.ToString();
        }
    }
}
