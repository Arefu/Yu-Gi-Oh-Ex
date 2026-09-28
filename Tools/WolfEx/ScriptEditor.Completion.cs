using System.Text.RegularExpressions;
using ScintillaNET;

namespace WolfEx
{
    /// <summary>
    /// Ctrl+Space IntelliSense for the EffectScript editor: a list of what can come next at the caret (worked out from the text before it) and a
    /// hint next to the highlighted entry saying what it does. Typing two letters offers the list on its own.
    /// </summary>
    internal sealed partial class ScriptEditor
    {
        private static readonly Dictionary<string, string> Docs = new()
        {
            ["effect"] = "effect \"Name\" { ... }\r\nStarts an effect. The name is optional. Inside: an optional trigger (monsters), then the action(s).",
            ["on"] = "on <trigger>:\r\nMakes this a MONSTER effect that happens on the trigger (Normal Summon, FLIP, sent to the GY ...). Leave it out for a Spell.",
            ["normal_summoned"] = "When this card is Normal Summoned.",
            ["special_summoned"] = "When this card is Special Summoned.",
            ["summoned"] = "When this card is Summoned (Normal, Flip or Special).",
            ["normal_or_special_summoned"] = "If this card is Normal or Special Summoned.",
            ["flip"] = "FLIP: when this card is flipped face-up.",
            ["destroyed_by_battle"] = "When this card is destroyed by battle and sent to the GY.",
            ["sent_to_grave"] = "If this card is sent to the GY (by any means).",
            ["sent_from_field_to_grave"] = "If this card is sent from the field to the GY.",
            ["draw"] = "draw(N)\r\nYou draw N cards.",
            ["gain_lp"] = "gain_lp(N)\r\nYou gain N Life Points.",
            ["burn"] = "burn(N)\r\nInflict N damage to your opponent.",
            ["search"] = "search(deck, <selector>)\r\nAdd 1 matching card from your Deck to your hand (you pick from a list).",
            ["revive"] = "revive(grave | opponent_grave | either_grave, <selector>)\r\nSpecial Summon 1 matching monster from a Graveyard.",
            ["send_to_grave"] = "send_to_grave(deck, <selector>)\r\nSend 1 matching card from your Deck to the Graveyard.",
            ["destroy"] = "destroy(all | target, [own | opponent | any,] <selector>)\r\nall = every matching monster; target = you choose 1 matching card (monster, or spell for a Spell/Trap).",
            ["once_per_turn"] = "once_per_turn;\r\nYou can only use this effect once per turn (per card name). Written last, after the action.",
            ["then"] = "a then b\r\nRuns a, then b. Only draw / gain_lp / burn can come before another action.",
            ["deck"] = "Your Deck.",
            ["grave"] = "Your Graveyard.",
            ["opponent_grave"] = "Your opponent's Graveyard.",
            ["either_grave"] = "Either player's Graveyard.",
            ["all"] = "Every matching monster on the field.",
            ["target"] = "Choose 1 matching card as the target.",
            ["own"] = "Cards you control.",
            ["opponent"] = "Cards your opponent controls.",
            ["any"] = "Cards on either side of the field.",
            ["monster"] = "Only monsters.",
            ["spell"] = "Only Spell Cards (for destroy(target, ...): the Spell & Trap zones).",
            ["trap"] = "Only Trap Cards.",
            ["card"] = "Any kind of card.",
            ["where"] = "where <condition> [and <condition> ...]\r\nNarrows the selection.",
            ["and"] = "Adds another condition.",
            ["race"] = "race = Dragon\r\nThe monster's Type.",
            ["attribute"] = "attribute = Dark\r\nThe monster's Attribute.",
            ["level"] = "level <= 4   (=, <=, >=, <, >)\r\nThe monster's Level, 0 to 15. destroy(all/target) takes <= or >=.",
            ["atk"] = "atk <= 1500\r\nOnly this test exists in the game so far: ATK 1500 or less (Sangan style).",
            ["archetype"] = "archetype = \"Dark World\"\r\nCards of an archetype (a name from Archetypes.json, or its code number).",
            ["name"] = "name = 4867\r\nCards with the same name as the game card with this Konami id (search / send only).",
        };

        // These are properties, not static fields: static initialisers of a partial class run in no defined order across its files, and they read Values from the other file.
        private static Dictionary<string, string>? _valueDocs;
        private static Dictionary<string, string> ValueDocs => _valueDocs ??= Values.ToDictionary(v => v, v => "A monster " + (Array.IndexOf(Values, v) < 25 ? "Type" : "Attribute") + ".");

        private static readonly string[] Triggers = { "normal_summoned", "special_summoned", "summoned", "normal_or_special_summoned", "flip", "destroyed_by_battle", "sent_to_grave", "sent_from_field_to_grave" };
        private static readonly string[] Kinds = { "monster", "spell", "trap", "card" };
        private static readonly string[] Fields = { "race", "attribute", "level", "atk", "archetype", "name" };
        private static string[] Races => Values.Take(25).ToArray();
        private static string[] Attributes => Values.Skip(25).ToArray();
        private static readonly string[] EqualsOnly = { "=" };
        private static readonly string[] Comparisons = { "=", "<=", ">=", "<", ">" };

        private void InitCompletion()
        {
            AutoCSeparator = '\n';
            AutoCIgnoreCase = true;
            AutoCSelectionChange += (_, e) => ShowHint(e.Text);
            AutoCCancelled += (_, _) => CallTipCancel();
            AutoCCompleted += (_, _) => CallTipCancel();
            KeyDown += (_, e) =>
            {
                if (e.Control && e.KeyCode == Keys.Space)
                {
                    e.SuppressKeyPress = true;
                    e.Handled = true;
                    ShowCompletion(automatic: false);
                }
            };
        }

        private void ShowHint(string item)
        {
            string key = item.Trim('"');
            string? hint = Docs.TryGetValue(key, out var d) ? d : ValueDocs.TryGetValue(key, out var v) && v.Length > 0 ? v : Comparisons.Contains(key) ? "Comparison." : null;
            if (hint == null)
            {
                CallTipCancel();
                return;
            }
            CallTipShow(CurrentPosition, hint);
        }

        /// <summary>What can follow at the caret, judged from the text before it. The second value is the part of the word already typed.</summary>
        private (List<string> Items, int Typed, bool Quoted) Suggest()
        {
            int position = CurrentPosition;
            string before = GetTextRange(0, position);
            int lineStart = before.LastIndexOf('\n') + 1;
            string line = before[lineStart..];

            // Inside an open string: only archetype names make sense.
            bool openQuote = line.Count(c => c == '"') % 2 == 1;
            var tokens = Token.Matches(before).Cast<Match>().Where(m => !m.Groups["comment"].Success).ToList();
            string partial = Regex.Match(before, "[A-Za-z_0-9]*$").Value;
            var previous = tokens.Where(m => m.Index + m.Length <= before.Length - partial.Length).ToList();
            string last = previous.Count > 0 ? previous[^1].Value : "";
            string beforeLast = previous.Count > 1 ? previous[^2].Value : "";

            if (before.TrimEnd().EndsWith("//") || Regex.IsMatch(line, "//"))
                return ([], 0, false);

            if (openQuote)
            {
                string typed = line[(line.LastIndexOf('"') + 1)..];
                return (ArchetypeNames(), typed.Length, false);
            }

            // Value after "field op"
            if (previous.Count >= 2 && Regex.IsMatch(last, "^(=|<=|>=|<|>)$"))
            {
                return beforeLast switch
                {
                    "race" => (Races.ToList(), partial.Length, false),
                    "attribute" => (Attributes.ToList(), partial.Length, false),
                    "archetype" => (ArchetypeNames().Select(n => "\"" + n + "\"").ToList(), partial.Length, true),
                    _ => ([], 0, false),
                };
            }
            if (Fields.Contains(last))
                return ((last is "race" or "attribute" or "archetype" or "name" ? EqualsOnly : last == "atk" ? new[] { "<=" } : Comparisons).ToList(), partial.Length, false);

            switch (last)
            {
                case "{": return (new[] { "on" }.Concat(new[] { "draw", "gain_lp", "burn", "search", "revive", "send_to_grave", "destroy" }).ToList(), partial.Length, false);
                case "on": return (Triggers.ToList(), partial.Length, false);
                case ":" when Triggers.Contains(beforeLast): return (Actions.ToList(), partial.Length, false);
                case "then": return (Actions.ToList(), partial.Length, false);
                case "where":
                case "and": return (Fields.ToList(), partial.Length, false);
                case ")": return (new List<string> { "then", "once_per_turn" }, partial.Length, false);
                case ";" when previous.Count > 0 && !previous.Any(m => m.Value == "once_per_turn"): return (new List<string> { "once_per_turn" }, partial.Length, false);
                case "(":
                    return beforeLast switch
                    {
                        "search" or "send_to_grave" => (new List<string> { "deck" }, partial.Length, false),
                        "revive" => (new List<string> { "grave", "opponent_grave", "either_grave" }, partial.Length, false),
                        "destroy" => (new List<string> { "all", "target" }, partial.Length, false),
                        _ => ([], 0, false),
                    };
                case ",":
                    // second argument: a side for destroy, else a selector
                    return InDestroy(previous) && !previous.Any(m => m.Value is "own" or "opponent" or "any")
                        ? (new List<string> { "own", "opponent", "any" }, partial.Length, false)
                        : (Kinds.Concat(Fields).ToList(), partial.Length, false);
            }
            if (Kinds.Contains(last))
                return (new List<string> { "where" }, partial.Length, false);
            if (last is "own" or "opponent" or "any" or "deck" or "grave" or "opponent_grave" or "either_grave" or "all" or "target" && previous.Count > 0)
                return (new List<string> { "monster", "spell", "trap", "card" }, partial.Length, false);
            return (Actions.Concat(Keywords).Distinct().ToList(), partial.Length, false);
        }

        private static bool InDestroy(List<Match> previous)
        {
            for (int i = previous.Count - 1; i >= 0; i--)
            {
                if (previous[i].Value == "destroy")
                    return true;
                if (previous[i].Value == ";" || previous[i].Value == "{")
                    return false;
            }
            return false;
        }

        private static List<string> ArchetypeNames() =>
            ArchetypeCatalog.Codes().Select(ArchetypeCatalog.NameOf).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        private void ShowCompletion(bool automatic)
        {
            var (items, typed, _) = Suggest();
            if (automatic && typed < 2)
                return;
            var filtered = items.Where(i => typed == 0 || i.Trim('"').StartsWith(GetTextRange(CurrentPosition - typed, typed), StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count == 0)
                return;
            AutoCShow(typed, string.Join("\n", filtered.OrderBy(i => i, StringComparer.OrdinalIgnoreCase)));
        }
    }
}
