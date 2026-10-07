using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WolfEx
{
    /// <summary>
    /// Reads a Synchro, Xyz or Link Monster's material line into the "synchro" / "xyz" / "link" objects Yu-Gi-Oh-Effects SynchroXyz.cpp reads:
    /// "1 DARK Tuner + 1+ non-Tuner Dragon monsters", "\"Junk Synchron\" + 1+ non-Tuner monsters", "1 Tuner + 2 non-Tuner monsters",
    /// "3 Level 4 LIGHT monsters", "2+ Level 6 \"Zoodiac\" monsters". Each side is one condition (the game's tables hold one code per side);
    /// what else the text says is reported in why.
    /// </summary>
    internal static class ExtraDeckMaterialText
    {
        private static readonly string[] Attributes = ["LIGHT", "DARK", "WATER", "FIRE", "EARTH", "WIND"];
        private static readonly Regex Quoted = new("\"(?<name>[^\"]+)\"", RegexOptions.Compiled);
        private static readonly Regex XyzLine = new(
            "^(?<n>\\d+)(?<plus>\\+| or more)? Level (?<level>\\d+) (?<cond>.*?)\\s*(?:monsters?|Monsters?)(?<rest>.*)$", RegexOptions.Compiled);
        private static readonly Regex CountPrefix = new("^(?<n>\\d+)(?<plus>\\+| or more)?\\s+", RegexOptions.Compiled);

        /// <summary>One condition from a few words: an Attribute, a Type, a kind, a quoted archetype (or, with cardId, a quoted card).</summary>
        private static JsonNode? Condition(string words, Func<string, int>? cardId, Func<string, List<int>> archetypeCodes, List<string> dropped)
        {
            words = Regex.Replace(words, "\\b(monsters?|Monsters?|Cards?)\\b", "").Replace("-Type", "").Trim().Trim(',').Trim();
            if (words.Length == 0)
                return null;
            var found = new List<JsonNode>();
            var quoted = Quoted.Match(words);
            if (quoted.Success)
            {
                string name = quoted.Groups["name"].Value;
                // "Junk Synchron" alone, as a Tuner: that card; "Blackwing" as a word before Tuner: the archetype
                bool alone = words.Trim() == quoted.Value;
                int id = alone && cardId != null ? cardId(name) : 0;
                if (id > 0)
                    found.Add(id);
                else if (archetypeCodes(name).FirstOrDefault(code => code < ArchetypeCatalog.FirstCustomCode) is int code and > 0)
                    found.Add($"archetype:{code}");
                else
                    dropped.Add($"\"{name}\" (not a game archetype or card)");
                words = words.Remove(quoted.Index, quoted.Length);
            }
            foreach (string word in words.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string w = word.Trim(',');
                if (Attributes.Contains(w))
                    found.Add(w);
                else if (MaterialCondition.Types.Take(24).FirstOrDefault(t => t.Text.Equals(w, StringComparison.OrdinalIgnoreCase) ||
                             t.Text.Equals(w.TrimEnd('s'), StringComparison.OrdinalIgnoreCase)) is { Word: not null } type)
                    found.Add(type.Word);
                else if (w is "Normal" or "Gemini" or "Pendulum" or "Synchro")
                    found.Add(w.ToLowerInvariant());
                else if (w.Length > 0)
                    dropped.Add(w);
            }
            if (found.Count > 1)
                dropped.Add($"only the first condition is kept ({string.Join(", ", found.Skip(1).Select(n => n.ToJsonString()))}): the game holds one per side");
            return found.FirstOrDefault();
        }

        public static JsonObject? Synchro(string description, Func<string, int> cardId, Func<string, List<int>> archetypeCodes, out string why)
        {
            why = "";
            string line = FusionMaterialText.MaterialLine(description);
            int plus = line.IndexOf(" + ", StringComparison.Ordinal);
            if (plus < 0 || !line.Contains("non-Tuner", StringComparison.OrdinalIgnoreCase))
            {
                why = "no \"Tuner + non-Tuner\" line";
                return null;
            }
            var dropped = new List<string>();
            string left = line[..plus].Trim(), right = line[(plus + 3)..].Trim().TrimEnd('.');

            var leftCount = CountPrefix.Match(left);
            if (leftCount.Success)
            {
                if (leftCount.Groups["n"].Value != "1" || leftCount.Groups["plus"].Success)
                {
                    why = $"{leftCount.Value.Trim()} Tuners (the game's table has one Tuner)";
                    return null;
                }
                left = left[leftCount.Length..];
            }
            bool synchroTuner = Regex.IsMatch(left, "Tuner Synchro|Synchro Tuner", RegexOptions.IgnoreCase);
            left = Regex.Replace(left, "\\b(Tuners?|Synchro)\\b", "").Trim();
            JsonNode? tuner = synchroTuner ? JsonValue.Create("synchro") : Condition(left, cardId, archetypeCodes, dropped);
            if (synchroTuner && left.Length > 0 && !Regex.IsMatch(left, "^(monsters?|Monsters?)$"))
                dropped.Add(left);

            var rightCount = CountPrefix.Match(right);
            if (!rightCount.Success)
            {
                why = "no count of non-Tuners";
                return null;
            }
            int nonTuners = int.Parse(rightCount.Groups["n"].Value);
            bool orMore = rightCount.Groups["plus"].Success;
            right = right[rightCount.Length..].Replace("non-Tuner", "", StringComparison.OrdinalIgnoreCase);
            JsonNode? nonTuner = Condition(right, cardId, archetypeCodes, dropped);

            var synchro = new JsonObject();
            if (tuner != null)
                synchro["tuner"] = tuner;
            if (nonTuner != null)
                synchro["nonTuner"] = nonTuner;
            synchro["materials"] = 1 + nonTuners;
            if (!orMore)
                synchro["exactly"] = true;
            why = string.Join("; ", dropped);
            return synchro;
        }

        // Link kinds the check knows on every material (Link_CardIsValidMaterial): word in the text -> word SynchroXyz.cpp reads
        private static readonly (string Text, string Word)[] LinkKinds = [("Effect", "effect"), ("Normal", "normal"), ("Pendulum", "pendulum"), ("Xyz", "xyz"), ("Link", "link")];

        /// <summary>
        /// "2+ Effect Monsters", "2 Spellcaster monsters", "2 Level 4 or lower Cyberse monsters", "2 monsters, except Tokens",
        /// "2+ monsters, including a \"Trickstar\" monster" -> {"condition", "material", "including"}: what an "N+" line names goes in condition,
        /// what an exact "N" line names in material (as Konami's table does: Decode Talker 74 first, Link Spider 73 second); "except Tokens" and a
        /// Level take the first slot. Both hold for every material; "including" for one of them. The count is the Link Rating's.
        /// </summary>
        public static JsonObject? Link(string description, Func<string, int> cardId, Func<string, List<int>> archetypeCodes, out string why)
        {
            why = "";
            string line = FusionMaterialText.MaterialLine(description).Trim().TrimEnd('.');
            var count = CountPrefix.Match(line);
            if (!count.Success)
            {
                why = "no \"N monsters\" line";
                return null;
            }
            line = line[count.Length..];
            // Konami's placement: "2+ Effect Monsters" -> the first slot, "1 Normal Monster" / "2 Spellcaster monsters" -> the second.
            // "except Tokens" and a Level go first; the game tests both slots on every material either way.
            string main = count.Groups["plus"].Success ? "condition" : "material";
            var dropped = new List<string>();
            var link = new JsonObject();
            void Put(string key, JsonNode value, string what)
            {
                string other = key == "condition" ? "material" : "condition";
                if (link[key] == null)
                    link[key] = value;
                else if (link[other] == null)
                    link[other] = value;
                else
                    dropped.Add($"{what} (the game holds two conditions for every material)");
            }

            string? including = null;
            int at = line.IndexOf("including", StringComparison.OrdinalIgnoreCase);
            if (at >= 0)
            {
                including = line[(at + "including".Length)..].Trim();
                line = line[..at].TrimEnd(',', ' ');
            }
            if (Regex.IsMatch(line, "except Tokens?", RegexOptions.IgnoreCase))
            {
                Put("condition", "notToken", "except Tokens");
                line = Regex.Replace(line, ",?\\s*except Tokens?", "", RegexOptions.IgnoreCase);
            }
            var level = Regex.Match(line, "Level (?<n>\\d+)(?<how> or (?:lower|higher))?", RegexOptions.IgnoreCase);
            if (level.Success)
            {
                string how = level.Groups["how"].Value.Trim().ToLowerInvariant();
                string word = how.EndsWith("lower") ? $"level<={level.Groups["n"].Value}" : how.EndsWith("higher") ? $"level>={level.Groups["n"].Value}" : $"level:{level.Groups["n"].Value}";
                Put("condition", word, level.Value);
                line = line.Remove(level.Index, level.Length);
            }
            foreach (var (text, word) in LinkKinds)
            {
                var kind = Regex.Match(line, $"\\b{text}\\b");
                if (!kind.Success)
                    continue;
                Put(main, word, text);
                line = line.Remove(kind.Index, kind.Length);
            }
            if (Condition(line, null, archetypeCodes, dropped) is { } material)
                Put(main, material, material.ToJsonString());

            if (including != null)
            {
                string part = Regex.Replace(including, "^(at least )?(a|an|1)\\s+", "", RegexOptions.IgnoreCase).Trim();
                var kind = Regex.Match(part, "\\b(Link|Synchro|Tuner)\\b");
                if (kind.Success)
                {
                    link["including"] = kind.Value.ToLowerInvariant();
                    part = part.Remove(kind.Index, kind.Length);
                    if (Regex.Replace(part, "\\b(monsters?|Monsters?)\\b", "").Trim().Length > 0)
                        dropped.Add($"including: {part.Trim()}");
                }
                else if (Condition(part, cardId, archetypeCodes, dropped) is { } one)
                    link["including"] = one;
            }
            why = string.Join("; ", dropped);
            return link;
        }

        public static JsonObject? Xyz(string description, Func<string, List<int>> archetypeCodes, out int level, out string why)
        {
            why = "";
            level = 0;
            string line = FusionMaterialText.MaterialLine(description);
            var match = XyzLine.Match(line);
            if (!match.Success)
            {
                why = "no \"N Level X monsters\" line";
                return null;
            }
            var dropped = new List<string>();
            level = int.Parse(match.Groups["level"].Value);
            int count = int.Parse(match.Groups["n"].Value);
            if (match.Groups["plus"].Success)
                dropped.Add($"{count}+ read as {count}");
            if (match.Groups["rest"].Value.Trim().Trim('.') is { Length: > 0 } rest)
                dropped.Add(rest);
            var xyz = new JsonObject();
            if (Condition(match.Groups["cond"].Value, null, archetypeCodes, dropped) is { } material)
            {
                if (material.ToJsonString() == "\"synchro\"")
                    dropped.Add("Synchro (the Xyz check has no kind Synchro)");
                else
                    xyz["material"] = material;
            }
            xyz["materials"] = count;
            why = string.Join("; ", dropped);
            return xyz;
        }
    }
}
