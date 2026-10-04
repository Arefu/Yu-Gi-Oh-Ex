using System.Text.RegularExpressions;
using ScintillaNET;

namespace WolfEx
{
    /// <summary>
    /// Ctrl+Space IntelliSense for the EffectScript editor: a list of what can come next at the caret (worked out from the text before it) and a
    /// hint above the line saying what the highlighted entry does. Typing two letters offers the fitting words on their own; Ctrl+Space also
    /// lists every other word after them. An icon marks each entry's kind (Action, Keyword, Trigger, Field, Value, "text", operator).
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
            ["standby_phase"] = "During your Standby Phase (while this card is face-up on the field).",
            ["end_phase"] = "During your End Phase (while this card is face-up on the field).",
            ["destroys_by_battle"] = "When this card destroys a monster by battle.",
            ["battle_damage"] = "When this card inflicts battle damage to your opponent (by a direct attack, for the sources the game has).",
            ["attack_declared"] = "When this card declares an attack. (No clean game card to borrow from yet.)",
            ["ignition"] = "Once per turn: you can activate this effect (from the field, in your Main Phase).",
            ["equip"] = "equip(atk N[, def N])\r\nEquip Spell: the equipped monster gains N ATK / DEF. The card must have the Equip icon.",
            ["as"] = "as(<Konami id> | \"Card name\") [with atk N, def N];\r\nBehave exactly like that game card (its whole effect). In a duel the card plays under that id when it is free.",
            ["with"] = "with atk N, def N\r\nThis card's own ATK/DEF amounts for an equip / boost card.",
            ["def"] = "def N\r\nA DEF amount.",
            ["special_summon"] = "special_summon(hand | deck | grave, <selector>)\r\nSpecial Summon 1 matching monster from your hand, Deck or GY.",
            ["add_to_hand"] = "add_to_hand(grave, <selector>)\r\nAdd 1 matching card from your GY to your hand.",
            ["hand"] = "Your hand.",
            ["banish"] = "banish(all | target, [own | opponent | any,] <selector>)\r\nall = every matching card on the field; target = you choose 1 matching card. Same filters as destroy.",
            ["banished"] = "Your banished cards.",
            ["negate"] = "negate(spell | trap | monster | spelltrap | any | \"Card\" [, opponent] [, your_turn | opponent_turn] [, battle_phase] [, targets_one] [, effect] [, normal | counter | field | equip | continuous | quickplay | ritual])\r\nA Spell/Trap (or with cost tribute_self, a monster Quick Effect): negate the activation, and if you do, destroy it.",
            ["cost"] = "cost discard(N): ... / cost pay_lp(N): ...\r\nA cost paid when the effect is activated, written before the action.",
            ["discard"] = "discard(N)\r\nCost: discard N cards.",
            ["pay_lp"] = "pay_lp(N)\r\nCost: pay N Life Points.",
            ["also"] = "a; also b;\r\nA second effect on the same card (each effect borrows its own game card).",
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

        private static readonly string[] Triggers = { "normal_summoned", "special_summoned", "summoned", "normal_or_special_summoned", "flip", "destroyed_by_battle", "sent_to_grave", "sent_from_field_to_grave",
            "standby_phase", "end_phase", "destroys_by_battle", "battle_damage", "attack_declared", "ignition" };
        private static readonly string[] Kinds = { "monster", "spell", "trap", "card" };
        private static readonly string[] Fields = { "race", "attribute", "level", "atk", "archetype", "name" };
        private static string[] Races => Values.Take(25).ToArray();
        private static string[] Attributes => Values.Skip(25).ToArray();
        private static readonly string[] EqualsOnly = { "=" };
        private static readonly string[] Comparisons = { "=", "<=", ">=", "<", ">" };

        // The icon in front of each entry of the list (the number after the type separator picks it), like Visual Studio's.
        private const char TypeSeparator = '\u001F';
        private const int IconAction = 1, IconKeyword = 2, IconTrigger = 3, IconField = 4, IconValue = 5, IconText = 6, IconOperator = 7;

        private static int IconOf(string item) =>
            item.StartsWith('"') ? IconText
            : Actions.Contains(item) ? IconAction
            : Triggers.Contains(item) ? IconTrigger
            : Fields.Contains(item) ? IconField
            : Values.Contains(item) ? IconValue
            : Comparisons.Contains(item) ? IconOperator
            : IconKeyword;

        /// <summary>Every word EffectScript knows: Ctrl+Space lists these after the ones that fit at the caret.</summary>
        private static IEnumerable<string> Everything =>
            Actions.Concat(Keywords).Concat(Triggers).Concat(Fields).Concat(Kinds).Concat(Values).Concat(Docs.Keys).Distinct(StringComparer.OrdinalIgnoreCase);

        private void RegisterIcons()
        {
            foreach (var info in KindInfo)
            {
                using var bitmap = IconBitmap(info.Kind);
                RegisterRgbaImage(info.Kind, bitmap);
            }
        }

        private void InitCompletion()
        {
            AutoCSeparator = '\n';
            AutoCTypeSeparator = TypeSeparator;
            AutoCIgnoreCase = true;
            AutoCOrder = Order.Custom;   // the entries that fit at the caret first, then everything else
            AutoCMaxHeight = 14;
            AutoCMaxWidth = 0;
            CallTipSetPosition(true);    // the word hint goes above the line, clear of the list
            RegisterIcons();
            // Scintilla's call tip closes the completion list (CallTipShow cancels it), so the hint for the highlighted entry is a tooltip of its own
            AutoCSelectionChange += (_, e) => ShowListHint(e.Text);
            AutoCCancelled += (_, _) => HideListHint();
            AutoCCompleted += (_, _) => HideListHint();
            LostFocus += (_, _) => HideListHint();
            UpdateUI += (_, _) => ShowWordHint();
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

        private static string? HintFor(string item)
        {
            int separator = item.IndexOf(TypeSeparator);
            string key = (separator >= 0 ? item[..separator] : item).Trim('"');
            return Docs.TryGetValue(key, out var d) ? d : ValueDocs.TryGetValue(key, out var v) && v.Length > 0 ? v : Comparisons.Contains(key) ? "Comparison." : null;
        }

        private readonly ToolTip _listHint = new() { UseAnimation = false, UseFading = false };
        private int _listLeft, _listWidth;

        /// <summary>The hint for the entry highlighted in the list, to the right of the list.</summary>
        private void ShowListHint(string item)
        {
            if (HintFor(item) is not { } hint)
            {
                HideListHint();
                return;
            }
            var list = ListScreenBounds();
            _listHint.Show(hint, this, PointToClient(new Point(list.Right + 4, list.Top)));
        }

        private void HideListHint()
        {
            _listHint.Hide(this);
            HideStrip();
        }

        private string _wordHinted = "";

        /// <summary>With no list open: the caret on a word EffectScript knows shows what it does above the line.</summary>
        private void ShowWordHint()
        {
            if (AutoCActive)
                return;
            if (_strip?.Visible == true)
                HideListHint();   // the list closed without an event (the caret moved off it)
            int position = CurrentPosition;
            int start = WordStartPosition(position, true), end = WordEndPosition(position, true);
            string word = end > start ? GetTextRange(start, end) : "";
            string? hint = word.Length > 0 && InCode() ? HintFor(word) : null;
            if (hint == null)
            {
                if (_wordHinted.Length > 0 || CallTipActive)
                    CallTipCancel();
                _wordHinted = "";
                return;
            }
            string key = $"{start}:{word}";
            if (key == _wordHinted && CallTipActive)
                return;
            _wordHinted = key;
            CallTipShow(start, hint);
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
                case "{": return (new[] { "on" }.Concat(Actions).ToList(), partial.Length, false);
                case "on": return (Triggers.ToList(), partial.Length, false);
                case "cost": return (new List<string> { "discard", "pay_lp" }, partial.Length, false);
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
                        "equip" => (new List<string> { "atk", "def" }, partial.Length, false),
                        "special_summon" => (new List<string> { "hand", "deck", "grave" }, partial.Length, false),
                        "add_to_hand" => (new List<string> { "grave", "banished" }, partial.Length, false),
                        "banish" => (new List<string> { "all", "target" }, partial.Length, false),
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
                if (previous[i].Value is "destroy" or "banish")
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
            var ordered = items.OrderBy(i => i, StringComparer.OrdinalIgnoreCase).ToList();
            // Ctrl+Space in code (not in a comment or a string): every word follows the ones that fit here, so nothing is out of reach
            if (!automatic && InCode())
                ordered.AddRange(Everything.Where(w => !items.Contains(w, StringComparer.OrdinalIgnoreCase)).OrderBy(w => w, StringComparer.OrdinalIgnoreCase));
            string prefix = GetTextRange(CurrentPosition - typed, typed);
            var filtered = ordered.Where(i => typed == 0 || i.Trim('"').StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count == 0)
                return;
            // inside a string the entries are archetype names
            bool code = InCode();
            filtered = filtered.Distinct(StringComparer.Ordinal).ToList();
            var kinds = filtered.ToDictionary(i => i, i => code ? IconOf(i) : IconText, StringComparer.Ordinal);
            var present = kinds.Values.Distinct().ToList();
            // the kinds picked on the filter strip (ScriptEditor.Filters.cs): only those, or everything when none is picked
            var shown = _onlyKinds.Count == 0 ? filtered : filtered.Where(i => _onlyKinds.Contains(kinds[i])).ToList();
            if (shown.Count == 0)
            {
                _onlyKinds.Clear();   // typing left none of the picked kinds: show everything again
                shown = filtered;
            }
            CallTipCancel();
            _wordHinted = "";
            _lastAutomatic = automatic;
            // where the list opens (under the word being typed) and about how wide it is, used when its window can't be found
            _listLeft = PointXFromPosition(CurrentPosition - typed);
            using (var font = new Font(Styles[Style.Default].Font, Styles[Style.Default].SizeF))
                _listWidth = shown.Max(i => TextRenderer.MeasureText(i, font).Width) + 40;
            AutoCShow(typed, string.Join("\n", shown.Select(i => $"{i}{TypeSeparator}{kinds[i]}")));
            if (!AutoCActive)
                return;
            ShowStrip(present);
            if (AutoCCurrent >= 0 && AutoCCurrent < shown.Count)
                ShowListHint(shown[AutoCCurrent]);   // the first entry is highlighted without a selection-change event
        }

        /// <summary>The caret is in code: not in a // comment or an open string.</summary>
        private bool InCode()
        {
            string before = GetTextRange(0, CurrentPosition);
            string line = before[(before.LastIndexOf('\n') + 1)..];
            return !line.Contains("//") && line.Count(c => c == '"') % 2 == 0;
        }
    }
}
