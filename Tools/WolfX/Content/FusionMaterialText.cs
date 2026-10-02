using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WolfEx
{
    /// <summary>
    /// Reads a Fusion Monster's material line ("1 LIGHT Warrior monster + 1 \"Dark Magician\"", "2 \"Gem-Knight\" monsters",
    /// "1 Fusion, Synchro, Xyz, or Link Monster + 1 Spellcaster monster") and writes the "fusion" recipe that Yu-Gi-Oh-Effects Fusion.cpp
    /// gives the game: one entry per material, each a card id, a readable code ("Dragon", "DARK", "level>=5", "archetype:12", "tuner",
    /// "monster"), an array of codes that must all hold, or {"any": [...]} for one of them (docs/EffectSystem.md section 36).
    /// Conditions the game's material check cannot see ("with different names", "in your GY", "on the field") are dropped and listed
    /// in <see cref="Result.Dropped"/> so the recipe is known to be looser than the text.
    /// </summary>
    internal static class FusionMaterialText
    {
        public sealed record Result(JsonArray Materials, List<string> Dropped);

        private const int MaxMaterials = 5;

        // Card texts use today's names; the game's card list has some older ones.
        private static readonly Dictionary<string, string> GameNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Red-Eyes Black Dragon"] = "Red-Eyes B. Dragon",
        };

        private static readonly (string Text, string Code)[] Races =
        {
            ("Sea Serpent", "SeaSerpent"), ("Winged Beast", "WingedBeast"), ("Beast-Warrior", "BeastWarrior"), ("Divine-Beast", "DivineBeast"),
            ("Dragon", "Dragon"), ("Zombie", "Zombie"), ("Fiend", "Fiend"), ("Pyro", "Pyro"), ("Rock", "Rock"), ("Machine", "Machine"),
            ("Fish", "Fish"), ("Dinosaur", "Dinosaur"), ("Insect", "Insect"), ("Beast", "Beast"), ("Plant", "Plant"), ("Aqua", "Aqua"),
            ("Warrior", "Warrior"), ("Fairy", "Fairy"), ("Spellcaster", "Spellcaster"), ("Thunder", "Thunder"), ("Reptile", "Reptile"),
            ("Psychic", "Psychic"), ("Wyrm", "Wyrm"), ("Cyberse", "Cyberse"), ("Illusion", "Illusion"),
        };
        private static readonly string[] Attributes = { "LIGHT", "DARK", "WATER", "FIRE", "EARTH", "WIND" };
        private static readonly string[] Kinds = { "Normal", "Effect", "Tuner", "Ritual", "Fusion", "Synchro", "Xyz", "Pendulum", "Link", "Gemini" };

        // Qualifiers the material check cannot express: dropped (and reported).
        private static readonly Regex Qualifier = new(
            "\\s*(?:\\((?:[^)]*)\\)|,? ?except .*|with .*|that .*|in (?:your|the|your opponent's) .*|on the field.*|you control.*|" +
            "equipped .*|Special Summoned .*|from your .*|that cannot .*)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex Count = new("^(?<n>\\d+)(?<plus>\\+)? (?<rest>.+)$", RegexOptions.Compiled);
        private static readonly Regex MonsterWord = new("\\s*\\b(?:monsters?|Monster Cards?)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex QuotedOnly = new("^\"[^\"]+\"(?:(?:,| or|, or) \"[^\"]+\")*$", RegexOptions.Compiled);
        private static readonly Regex Quoted = new("\"(?<name>[^\"]+)\"", RegexOptions.Compiled);

        /// <summary>The material line of a Fusion Monster's text: the first line, or the line after "[ Monster Effect ]" for Pendulums.</summary>
        public static string MaterialLine(string description)
        {
            var lines = description.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            int header = lines.FindIndex(l => l.StartsWith("[ Monster Effect ]", StringComparison.OrdinalIgnoreCase));
            if (header >= 0)
                lines = lines.Skip(header + 1).ToList();
            // "(This card is always treated as ...)" comes before the materials on some cards.
            return lines.FirstOrDefault(l => !l.StartsWith('(')) ?? "";
        }

        /// <summary>null (and why) when the line is not a material list this can write.</summary>
        public static Result? TryParse(string description, Func<string, int> cardIdByName, Func<string, List<int>> archetypeCodes, out string why)
        {
            why = "";
            string line = MaterialLine(description);
            if (line.Length == 0 || line.EndsWith('.') || line.Contains(':') || line.StartsWith("Must", StringComparison.OrdinalIgnoreCase))
            {
                why = "no material line";
                return null;
            }

            // '"Blue-Eyes Ultimate Dragon" or 3 "Blue-Eyes" monsters': two recipes, the game holds one - the first that it can take.
            var alternative = Regex.Match(line, "^(?<first>.+?) or (?=\\d)");
            if (alternative.Success && !alternative.Groups["first"].Value.Contains(" + "))
            {
                var first = ParseLine(alternative.Groups["first"].Value, cardIdByName, archetypeCodes, out why);
                if (first != null)
                    return first;
                line = line.Substring(alternative.Length);
            }
            return ParseLine(line, cardIdByName, archetypeCodes, out why);
        }

        private static Result? ParseLine(string line, Func<string, int> cardIdByName, Func<string, List<int>> archetypeCodes, out string why)
        {
            why = "";
            var materials = new JsonArray();
            var dropped = new List<string>();
            foreach (string rawTerm in SplitPlus(line))
            {
                string term = rawTerm.Trim();
                int copies = 1;
                var count = Count.Match(term);
                if (count.Success)
                {
                    copies = int.Parse(count.Groups["n"].Value);
                    term = count.Groups["rest"].Value;
                    if (count.Groups["plus"].Success)
                        dropped.Add($"{copies}+ read as {copies}");
                }

                // '"Red-Eyes Black Dragon" or 1 Dragon Effect Monster': the first choice only.
                var orCount = Regex.Match(term, " or \\d.*$");
                if (orCount.Success)
                {
                    dropped.Add(orCount.Value.Trim());
                    term = term.Substring(0, orCount.Index);
                }

                var qualifier = Qualifier.Match(term);
                if (qualifier.Success && qualifier.Index > 0)
                {
                    dropped.Add(qualifier.Value.Trim());
                    term = term.Substring(0, qualifier.Index).Trim().TrimEnd(',');
                }

                JsonNode? node = Term(term, cardIdByName, archetypeCodes, out why);
                if (node == null)
                {
                    why = $"\"{rawTerm.Trim()}\": {why}";
                    return null;
                }
                for (int i = 0; i < copies; i++)
                    materials.Add(node.DeepClone());
            }

            if (materials.Count < 2 || materials.Count > MaxMaterials)
            {
                why = $"{materials.Count} materials (the game takes 2 to {MaxMaterials})";
                return null;
            }
            return new Result(materials, dropped);
        }

        private static IEnumerable<string> SplitPlus(string line)
        {
            int depth = 0, start = 0;
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"') quoted = !quoted;
                else if (!quoted && c == '(') depth++;
                else if (!quoted && c == ')') depth--;
                else if (!quoted && depth == 0 && c == '+' && i > 0 && line[i - 1] == ' ' && i + 1 < line.Length && line[i + 1] == ' ')
                {
                    yield return line.Substring(start, i - start);
                    start = i + 1;
                }
            }
            yield return line.Substring(start);
        }

        private static JsonNode? Term(string term, Func<string, int> cardIdByName, Func<string, List<int>> archetypeCodes, out string why)
        {
            why = "";
            // Position words the material check cannot see.
            term = Regex.Replace(term, "\\b(?:face-up|face-down|Defense Position|Attack Position) ", "", RegexOptions.IgnoreCase);
            bool monsterWord = MonsterWord.IsMatch(term);
            string body = MonsterWord.Replace(term, "").Trim();

            // "Dark Magician" / "A" or "B": card names (a quoted name followed by "monster" is an archetype, handled below).
            if (!monsterWord && QuotedOnly.IsMatch(body))
            {
                var ids = new List<int>();
                foreach (System.Text.RegularExpressions.Match m in Quoted.Matches(body))
                {
                    string cardName = m.Groups["name"].Value;
                    int id = cardIdByName(cardName);
                    if (id == 0 && GameNames.TryGetValue(cardName, out var gameName))
                        id = cardIdByName(gameName);
                    if (id == 0)
                    {
                        why = $"no card named \"{m.Groups["name"].Value}\"";
                        return null;
                    }
                    ids.Add(id);
                }
                if (ids.Count == 1)
                    return ids[0];
                return new JsonObject { ["any"] = new JsonArray(ids.Select(id => (JsonNode)id).ToArray()) };
            }
            if (body.Length == 0)
                return "monster";

            // Words -> slots, each slot one or more alternatives ("LIGHT or DARK", "Fusion, Synchro, Xyz, or Link", "Fiend and/or Zombie").
            body = Regex.Replace(body, "Level (\\d+) or higher", "level>=$1", RegexOptions.IgnoreCase);
            body = Regex.Replace(body, "Level (\\d+) or lower", "level<=$1", RegexOptions.IgnoreCase);
            body = Regex.Replace(body, "Level (\\d+) or (\\d+)", "level:$1 or level:$2", RegexOptions.IgnoreCase);
            body = Regex.Replace(body, "Level (\\d+)", "level:$1", RegexOptions.IgnoreCase);
            // Multi-word races to one word, outside quoted names.
            body = string.Join("\"", body.Split('"').Select((part, i) => i % 2 == 1 ? part :
                Races.Aggregate(part, (text, race) => Regex.Replace(text, "\\b" + Regex.Escape(race.Text) + "\\b(?:-Type)?", race.Code))));

            var slots = new List<List<JsonNode>>();
            bool joinNext = false;
            foreach (var token in Tokens(body))
            {
                if (token is "or" or "and/or")
                {
                    joinNext = true;
                    continue;
                }
                string word = token.TrimEnd(',');
                JsonNode? code = Word(word, archetypeCodes);
                if (code == null)
                {
                    why = $"\"{word}\" is not a race, attribute, Level, kind or known archetype";
                    return null;
                }
                if (joinNext && slots.Count > 0)
                    slots[^1].Add(code);
                else
                    slots.Add([code]);
                joinNext = token.EndsWith(',');
            }
            if (slots.Count == 0)
                return "monster";

            var nodes = slots.Select(slot => slot.Count == 1 ? slot[0] : new JsonObject { ["any"] = new JsonArray(slot.ToArray()) }).ToList();
            return nodes.Count == 1 ? nodes[0] : new JsonArray(nodes.ToArray());
        }

        private static IEnumerable<string> Tokens(string body)
        {
            foreach (System.Text.RegularExpressions.Match m in Regex.Matches(body, "\"[^\"]+\",?|\\S+"))
                yield return m.Value;
        }

        private static JsonNode? Word(string word, Func<string, List<int>> archetypeCodes)
        {
            bool negate = word.StartsWith("non-", StringComparison.OrdinalIgnoreCase);
            string w = negate ? word.Substring(4) : word;
            string? code = null;
            if (w.StartsWith("level", StringComparison.OrdinalIgnoreCase))
                code = w;
            else if (Races.Any(r => r.Code == w))
                code = w;
            else if (Attributes.Contains(w, StringComparer.OrdinalIgnoreCase))
                code = w.ToUpperInvariant();
            else if (Kinds.Contains(w, StringComparer.OrdinalIgnoreCase))
                code = w.ToLowerInvariant();
            if (code != null)
                return negate ? "non-" + code : code;

            // "Gem-Knight" (or a bare word such as Toon): an archetype. Custom cards often carry a custom code for an archetype the game
            // also has, so this can be one of several codes.
            // A bare word (Toon) only when it is exactly an archetype's name: "Illusion" is a Type the game lacks, not an archetype.
            bool quoted = w.StartsWith('"');
            if (!quoted && ArchetypeCatalog.CodeOf(w) == 0)
                return null;
            var codes = archetypeCodes(w.Trim('"'));
            if ((codes == null || codes.Count == 0) && quoted && !negate)
            {
                // No archetype ("Forbidden One"): the game cards named with it, when they are few.
                var ids = EffectScriptCompiler.NamedCards().Where(card => card.Name.Contains(w.Trim('"'), StringComparison.OrdinalIgnoreCase))
                    .Select(card => card.Id).Distinct().ToList();
                if (ids.Count >= 1 && ids.Count <= 12)
                    return ids.Count == 1 ? ids[0] : new JsonObject { ["any"] = new JsonArray(ids.Select(id => (JsonNode)id).ToArray()) };
            }
            if (codes == null || codes.Count == 0 || (negate && codes.Count > 1))
                return null;
            if (codes.Count == 1)
                return (negate ? "non-" : "") + $"archetype:{codes[0]}";
            return new JsonObject { ["any"] = new JsonArray(codes.Select(c => (JsonNode)$"archetype:{c}").ToArray()) };
        }

        private static Dictionary<int, List<int>>? _members;

        /// <summary>
        /// The archetype codes a quoted name stands for: the catalog's own name, the game archetype whose cards (archetype_members.json,
        /// from CARD_Named.bin) are mostly named with it, and the code most custom cards named with it carry.
        /// </summary>
        public static List<int> ArchetypeCodesFor(string name, IEnumerable<(string Name, List<int> Archetypes)> customCards)
        {
            var codes = new List<int>();
            int exact = ArchetypeCatalog.CodeOf(name);
            if (exact > 0)
                codes.Add(exact);

            if (_members == null)
            {
                _members = [];
                string path = Path.Combine(AppContext.BaseDirectory, "archetype_members.json");
                try
                {
                    if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject root)
                        foreach (var (key, list) in root)
                            if (int.TryParse(key, out int code) && list is JsonArray ids)
                                _members[code] = ids.Select(id => id!.GetValue<int>()).ToList();
                }
                catch { /* no membership file: names only */ }
            }
            var namesById = new Dictionary<int, string>();
            foreach (var (cardName, id) in EffectScriptCompiler.NamedCards())
                namesById.TryAdd(id, cardName);

            int best = 0;
            double bestRatio = 0;
            int bestCount = 0;
            foreach (var (code, ids) in _members)
            {
                int named = ids.Count(id => namesById.TryGetValue(id, out var n) && n.Contains(name, StringComparison.OrdinalIgnoreCase));
                if (named < 2)
                    continue;
                double ratio = (double)named / ids.Count;
                if (ratio >= 0.6 && (ratio > bestRatio || (ratio == bestRatio && named > bestCount)))
                {
                    best = code;
                    bestRatio = ratio;
                    bestCount = named;
                }
            }
            if (best > 0 && !codes.Contains(best))
                codes.Add(best);

            var named2 = customCards.Where(card => card.Name != null && card.Name.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
            var custom = named2.SelectMany(card => card.Archetypes.Where(code => code >= ArchetypeCatalog.FirstCustomCode).Distinct())
                .GroupBy(code => code).OrderByDescending(g => g.Count()).FirstOrDefault();
            if (custom != null && custom.Count() * 2 >= named2.Count && !codes.Contains(custom.Key))
                codes.Add(custom.Key);
            return codes;
        }
    }
}
