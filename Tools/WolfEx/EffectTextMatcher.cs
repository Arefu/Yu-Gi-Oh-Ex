using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WolfEx
{
    /// <summary>
    /// Recognises a Normal Spell's card text when its FIRST sentence is exactly one of the effects the game can already run (draw, gain
    /// Life Points, burn, search the Deck, revive from your GY, send from the Deck to the GY, destroy all) and returns the EffectScript
    /// for it (which EffectScriptCompiler then compiles like any other). Later sentences (Graveyard effects, restrictions, "you can only
    /// activate 1 per turn") are returned as "rest": they are not run by the game yet and are kept so nothing is hidden.
    /// The same rules as Desktop\New folder\Attach-Effects.py, which does this for a whole cards.json without the editor.
    /// </summary>
    internal static class EffectTextMatcher
    {
        public sealed record Match(string Script, string Rest);

        private static readonly Regex HopT = new("\\s*You can only activate 1 \"[^\"]+\" per turn\\.?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SentenceSplit = new("(?<=\\.) (?=[A-Z\"])", RegexOptions.Compiled);

        private static readonly string[] Races = { "Dragon", "Zombie", "Fiend", "Pyro", "Sea Serpent", "Rock", "Machine", "Fish", "Dinosaur", "Insect", "Beast",
            "Beast-Warrior", "Plant", "Aqua", "Warrior", "Winged Beast", "Fairy", "Spellcaster", "Thunder", "Reptile", "Psychic", "Wyrm", "Cyberse", "Divine-Beast" };
        private static readonly string[] Attributes = { "Light", "Dark", "Water", "Fire", "Earth", "Wind", "Divine" };

        private static string Alt(IEnumerable<string> words) => string.Join("|", words.OrderByDescending(w => w.Length).Select(Regex.Escape));

        // [Level N [or lower|or higher]] [ATTRIBUTE] [Race[-Type]] ["Archetype"] (monster|Spell|Trap|card)
        private static readonly string Selector =
            "(?:Level (?<lv>\\d+)(?<lvop> or lower| or higher)? )?(?:(?<attr>" + Alt(Attributes) + ") )?(?:(?<race>" + Alt(Races) + ")(?:-Type)? )?" +
            "(?:\"(?<name>[^\"]+)\" )?(?<kind>monster|Spell Card|Trap Card|Spell|Trap|card)(?<atk> with 1500 or less ATK)?";

        private static Regex Rx(string pattern) => new("^" + pattern + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Draw = Rx("Draw (?<n>\\d+) cards?\\.?");
        private static readonly Regex Gain = Rx("(?:You )?gain (?<n>\\d+) (?:Life Points|LP)\\.?");
        private static readonly Regex Gain2 = Rx("Increase your Life Points by (?<n>\\d+) points\\.?");
        private static readonly Regex Burn = Rx("Inflict (?<n>\\d+) (?:points of )?damage to your opponent\\.?");
        private static readonly Regex Search = Rx("Add 1 " + Selector + " from your Deck to your hand\\.?");
        private static readonly Regex Revive1 = Rx("Target 1 " + Selector + " in your (?:GY|Graveyard); Special Summon (?:it|that target)\\.?");
        private static readonly Regex Revive2 = Rx("Special Summon 1 " + Selector + " from your (?:GY|Graveyard)\\.?");
        private static readonly Regex Send = Rx("Send 1 " + Selector + " from your Deck to the (?:GY|Graveyard)\\.?");
        private static readonly Regex DestroyAllKind = Rx("Destroy all (?:face-up )?(?:(?<attr>" + Alt(Attributes) + ") |(?<race>" + Alt(Races) + ")(?:-Type)? )?monsters (?<side>on the field|your opponent controls|you control)\\.?");
        private static readonly Regex DestroyAllLevel = Rx("Destroy all monsters (?<side>on the field|your opponent controls|you control) that are Level (?<lv>\\d+) or (?<op>lower|higher)\\.?");

        /// <summary>null when the text is not a plain supported effect.</summary>
        public static Match? TryMatch(string description)
        {
            string t = Regex.Replace(description.Replace("\r\n", " ").Replace('\n', ' '), "\\s+", " ").Trim();
            t = HopT.Replace(t, "").Trim();
            var parts = SentenceSplit.Split(t);
            string first = parts[0].Trim();
            string rest = string.Join(" ", parts.Skip(1)).Trim();
            string? script = First(first);
            return script == null ? null : new Match(script, rest);
        }

        private static string? First(string t)
        {
            System.Text.RegularExpressions.Match m;
            if ((m = Draw.Match(t)).Success) return $"draw({m.Groups["n"].Value});";
            if ((m = Gain.Match(t)).Success || (m = Gain2.Match(t)).Success) return $"gain_lp({m.Groups["n"].Value});";
            if ((m = Burn.Match(t)).Success) return $"burn({m.Groups["n"].Value});";

            if ((m = Search.Match(t)).Success && Where(m) is { } s1) return $"search(deck, {s1});";
            if (((m = Revive1.Match(t)).Success || (m = Revive2.Match(t)).Success) && Where(m) is { } s2 && s2.StartsWith("monster"))
                return $"revive(grave, {s2});";
            if ((m = Send.Match(t)).Success && Where(m) is { } s3) return $"send_to_grave(deck, {s3});";

            if ((m = DestroyAllKind.Match(t)).Success && (m.Groups["attr"].Success || m.Groups["race"].Success))
            {
                string cond = m.Groups["attr"].Success ? $"attribute = {Squash(m.Groups["attr"].Value)}" : $"race = {Squash(m.Groups["race"].Value)}";
                return $"destroy(all, {Side(m.Groups["side"].Value)}, monster where {cond});";
            }
            if ((m = DestroyAllLevel.Match(t)).Success)
                return $"destroy(all, {Side(m.Groups["side"].Value)}, monster where level {(m.Groups["op"].Value.Equals("lower", StringComparison.OrdinalIgnoreCase) ? "<=" : ">=")} {m.Groups["lv"].Value});";
            return null;
        }

        private static string Side(string text) => text.ToLowerInvariant() switch { "your opponent controls" => "opponent", "you control" => "own", _ => "any" };
        private static string Squash(string v) => v.Replace(" ", "").Replace("-", "");

        /// <summary>"monster where race = Pyro and level <= 4" for a Selector match, or null when it cannot be expressed.</summary>
        private static string? Where(System.Text.RegularExpressions.Match m)
        {
            string kind = m.Groups["kind"].Value.ToLowerInvariant().Split(' ')[0];
            var conditions = new List<string>();
            if (m.Groups["race"].Success) conditions.Add($"race = {Squash(m.Groups["race"].Value)}");
            if (m.Groups["attr"].Success) conditions.Add($"attribute = {m.Groups["attr"].Value}");
            if (m.Groups["lv"].Success)
            {
                string op = m.Groups["lvop"].Value.Trim().ToLowerInvariant() switch { "or lower" => "<=", "or higher" => ">=", _ => "=" };
                if (int.Parse(m.Groups["lv"].Value) > 15) return null;
                conditions.Add($"level {op} {m.Groups["lv"].Value}");
            }
            if (m.Groups["atk"].Success) conditions.Add("atk <= 1500");
            if (m.Groups["name"].Success)
            {
                if (ArchetypeCatalog.CodeOf(m.Groups["name"].Value) == 0) return null;   // a card name, not an archetype: not expressed here
                conditions.Add($"archetype = \"{m.Groups["name"].Value}\"");
            }
            if ((m.Groups["race"].Success || m.Groups["attr"].Success || m.Groups["lv"].Success) && kind != "monster") return null;
            return conditions.Count == 0 ? kind : $"{kind} where {string.Join(" and ", conditions)}";
        }
    }
}
