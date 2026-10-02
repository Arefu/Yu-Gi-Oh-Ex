using System.Linq;
using System.Text.RegularExpressions;

namespace WolfEx
{
    /// <summary>
    /// Reads a whole card's text and writes the EffectScript for every effect in it that the game can run (docs/EffectSystem.md section 37):
    /// a trigger ("If this card is Normal Summoned:"), a condition the game checks ("If you control no monsters:"), a cost ("discard 1 card",
    /// "banish this card from your GY", "Tribute this card") and an action (draw, search, Special Summon, destroy, banish, ATK gain ...).
    /// Each effect becomes one EffectScript body; several are joined with 'also'. What is not written is kept: <see cref="Result.NotRun"/> lists
    /// the sentences the game will not run (other effects, restrictions such as "Cannot be Normal Summoned"), <see cref="Result.Looser"/> the
    /// parts of a written effect that were dropped ("and if you do, ...", "except this card", "in Defense Position").
    /// </summary>
    internal static class CardTextTranslator
    {
        public sealed record Result(string Script, int Effects, List<string> NotRun, List<string> Looser);

        private const RegexOptions Opt = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        private static Regex Rx(string pattern) => new("^" + pattern + "$", Opt);

        private static readonly (string Text, string Code)[] Races =
        {
            ("Sea Serpent", "SeaSerpent"), ("Winged Beast", "WingedBeast"), ("Beast-Warrior", "BeastWarrior"), ("Divine-Beast", "DivineBeast"),
            ("Creator God", "CreatorGod"), ("Dragon", "Dragon"), ("Zombie", "Zombie"), ("Fiend", "Fiend"), ("Pyro", "Pyro"), ("Rock", "Rock"),
            ("Machine", "Machine"), ("Fish", "Fish"), ("Dinosaur", "Dinosaur"), ("Insect", "Insect"), ("Beast", "Beast"), ("Plant", "Plant"),
            ("Aqua", "Aqua"), ("Warrior", "Warrior"), ("Fairy", "Fairy"), ("Spellcaster", "Spellcaster"), ("Thunder", "Thunder"),
            ("Reptile", "Reptile"), ("Psychic", "Psychic"), ("Wyrm", "Wyrm"), ("Cyberse", "Cyberse"),
        };
        private static readonly string[] Attributes = { "LIGHT", "DARK", "WATER", "FIRE", "EARTH", "WIND", "DIVINE" };
        // Monster kinds the game's list filters cannot test: read past (the selector becomes looser).
        private static readonly string[] Kinds = { "Normal", "Effect", "Tuner", "Ritual", "Fusion", "Synchro", "Xyz", "Pendulum", "Link", "Gemini",
            "Spirit", "Union", "Toon", "Flip", "Quick-Play", "Continuous", "Field", "Equip", "Counter" };

        // ---- sentences -------------------------------------------------------------------------------------------------------------

        // "You can only use each effect of this card once per turn" / "You can only use 1 this card effect per turn" / "... 1 of these effects ...".
        private static readonly Regex OncePerTurnEach = Rx("You can only use (?:each|1) .*(?:once per turn|per turn).*");
        // "You can only use this effect of this card once per turn": the effect just before it.
        private static readonly Regex OncePerTurnThis = Rx("You can only use (?:this|that|the) effect of this card once per turn.*");
        private static readonly Regex ActivateOncePerTurn = Rx("You can only activate 1 this card per turn.*");
        // Text that is a rule, not an effect the game would run for this card (kept in NotRun so it is seen).
        private static readonly Regex Restriction = new("^(Cannot|Must|This card cannot|This card can|This card's name|Neither|You can only|You cannot|Once per Chain|If this card is (?:used as|in the Spell & Trap Zone)|While|Gains|Loses|Each|All |Your opponent cannot|Monsters|Unaffected|You take no|Negate)", Opt);

        /// <summary>Why each NotRun sentence of the last Translate call was not written, in NotRun's order ("head: ...", "cost: ...",
        /// "action: ...", "rule: ...", "same kind: ..."). For the coverage harness (docs/EffectSystem.md section 40); the UI does not read it.</summary>
        public static List<string> LastReasons { get; } = [];
        [ThreadStatic] private static string? _why;
        private static string? Why(string reason)
        {
            _why = reason;
            return null;
        }

        /// <summary>Always a result; Effects == 0 (and an empty Script) when nothing in the text can be written.</summary>
        public static Result Translate(string name, string kind, string icon, string description, IReadOnlyCollection<int> ownArchetypes,
            Func<string, int> cardIdByName, Func<string, List<int>> archetypeCodes)
        {
            if (string.IsNullOrWhiteSpace(description) || kind.EndsWith("Normal") || kind is "Token" or "Skill")
                return new Result("", 0, [], []);
            bool isMonster = kind is not ("Spell" or "Trap");
            string text = EffectPart(description, kind);
            if (name.Length > 0)
                text = text.Replace("\"" + name + "\"", "this card");
            text = Regex.Replace(text, "\\bthis face-up card\\b", "this card", Opt);
            text = Regex.Replace(text, "\\bGraveyard\\b", "GY");
            text = Regex.Replace(text, "\\bLife Points\\b", "LP");
            text = Regex.Replace(text, "-Type\\b", "");

            var notRun = new List<string>();
            var looser = new List<string>();
            LastReasons.Clear();
            var bodies = new List<(string Script, bool Opt)>();
            var kinds = new HashSet<string>();
            bool allOnce = false;
            bool bulletsAllowed = true;
            foreach (string raw in Sentences(text))
            {
                // A bullet belongs to the sentence before it: under a choice the game cannot run ("You can activate 1 of these effects"), it is not run.
                bool bullet = raw.StartsWith(Bullet);
                string sentence = bullet ? raw.Substring(Bullet.Length).Trim() : raw;
                if (bullet && !bulletsAllowed)
                {
                    notRun.Add(sentence);
                    LastReasons.Add("bullet: under a choice / condition the game cannot run");
                    continue;
                }
                if (OncePerTurnEach.IsMatch(sentence) || ActivateOncePerTurn.IsMatch(sentence))
                {
                    allOnce = true;
                    continue;
                }
                if (OncePerTurnThis.IsMatch(sentence))
                {
                    if (bodies.Count > 0)
                        bodies[^1] = (bodies[^1].Script, true);
                    continue;
                }
                if (Restriction.IsMatch(sentence))
                {
                    // "Gains these effects while ...": its bullets only hold under that condition, so they are not run either.
                    if (!bullet)
                        bulletsAllowed = !ChoiceHeader.IsMatch(sentence);
                    notRun.Add(sentence);
                    LastReasons.Add("rule: " + Restriction.Match(sentence).Value.Trim());
                    continue;
                }
                if (!bullet && PlainChoice.IsMatch(sentence))
                {
                    // "(Once per turn:) Activate 1 of these effects;": the choice is read as its first bullet (one effect of a kind is kept below).
                    bulletsAllowed = true;
                    if (sentence.Contains("once per turn", StringComparison.OrdinalIgnoreCase))
                        allOnce = true;
                    looser.Add($"only the first choice of \"{Short(sentence)}\"");
                    continue;
                }
                var context = new Context(isMonster, kind, icon, ownArchetypes, cardIdByName, archetypeCodes);
                _why = null;
                string? script = Effect(sentence, context);
                if (!bullet)
                    bulletsAllowed = script != null || !ChoiceHeader.IsMatch(sentence);
                if (script == null)
                {
                    notRun.Add(sentence);
                    LastReasons.Add(_why ?? "unknown");
                    continue;
                }
                // The game asks a card for its effects table by table (and per event), so one effect of each kind is all a card can show; a
                // second one of the same kind would never be found (Yu-Gi-Oh-Effects RouteByRow, docs/EffectSystem.md section 37).
                if (!kinds.Add(context.EffectKind))
                {
                    notRun.Add(sentence);
                    LastReasons.Add("same kind: " + context.EffectKind);
                    looser.Add($"a second {context.EffectKind} effect is not run ({Short(sentence)})");
                    continue;
                }
                looser.AddRange(context.Looser.Select(l => $"{l} ({Short(sentence)})"));
                bodies.Add((script, context.OncePerTurn));
            }
            if (bodies.Count == 0)
                return new Result("", 0, notRun, looser);
            string joined = string.Join(" also ", bodies.Select(b => b.Script + ((b.Opt || allOnce) ? "; once_per_turn" : "")));
            return new Result(joined + ";", bodies.Count, notRun, looser);
        }

        private const string Bullet = "\u25CF ";
        private static readonly Regex PlainChoice = new("^(?:Once per turn: )?(?:You can )?(?:activate|apply) (?:1|one) of (?:these|the following) effects\\b", Opt);
        private static readonly Regex ChoiceHeader =new("(?:these effects|the following effects?|following effects)", Opt);

        private static string Short(string sentence) => sentence.Length <= 60 ? sentence : sentence.Substring(0, 57) + "...";

        // The part of the text that holds this card's effects: no material line (Extra Deck monsters), the monster effect of a Pendulum.
        private static string EffectPart(string description, string kind)
        {
            var lines = description.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            int monster = lines.FindIndex(l => l.StartsWith("[ Monster Effect ]", StringComparison.OrdinalIgnoreCase));
            if (monster >= 0)
                lines = lines.Skip(monster + 1).ToList();
            lines = lines.Where(l => !l.StartsWith("[ Pendulum Effect ]", StringComparison.OrdinalIgnoreCase) && !l.StartsWith("----")).ToList();
            bool extraDeck = kind.Contains("Fusion") || kind.Contains("Synchro") || kind.Contains("Xyz") || kind.Contains("Link");
            if (lines.Count > 0 && extraDeck && !lines[0].EndsWith('.') && !lines[0].Contains(':'))
                lines.RemoveAt(0);
            if (lines.Count > 0 && lines[0].StartsWith('(') && lines[0].EndsWith(')'))
                lines.RemoveAt(0);   // "(This card is always treated as ...)"
            return string.Join(" ", lines.Select(l => l.StartsWith('\u25CF') ? "\n" + Bullet + l.TrimStart('\u25CF', ' ') + "\n" : l));
        }

        // Sentences, split at ". " outside quotes; bullet lines are their own sentences.
        private static IEnumerable<string> Sentences(string text)
        {
            foreach (string block in text.Split('\n'))
            {
                bool quoted = false;
                int depth = 0, start = 0;
                for (int i = 0; i < block.Length; i++)
                {
                    char c = block[i];
                    if (c == '"') quoted = !quoted;
                    else if (!quoted && c == '(') depth++;
                    else if (!quoted && c == ')') depth = Math.Max(0, depth - 1);
                    else if (!quoted && depth == 0 && c == '.' && (i + 1 == block.Length || block[i + 1] == ' '))
                    {
                        string s = block.Substring(start, i - start).Trim();
                        if (s.Length > 0) yield return s;
                        start = i + 1;
                    }
                }
                string rest = block.Substring(start).Trim().TrimEnd('.');
                if (rest.Length > 0) yield return rest;
            }
        }

        private sealed class Context(bool isMonster, string kind, string icon, IReadOnlyCollection<int> ownArchetypes,
            Func<string, int> cardIdByName, Func<string, List<int>> archetypeCodes)
        {
            public bool IsMonster = isMonster;
            public string Kind = kind;
            public string Icon = icon;
            public IReadOnlyCollection<int> OwnArchetypes = ownArchetypes;
            public Func<string, int> CardIdByName = cardIdByName;
            public Func<string, List<int>> ArchetypeCodes = archetypeCodes;
            public bool OncePerTurn;
            public bool InGrave;   // the head said "If this card is in your GY"
            public string? Negate;   // the head was "When <a card / effect> is activated": negate(...)'s arguments
            public string EffectKind = "";   // which of the card's effect slots this effect takes: its trigger, "ignition", "hand/GY" or "spell"
            public List<string> Looser = [];
        }

        // ---- one effect ----------------------------------------------------------------------------------------------------------------

        private static string? Effect(string sentence, Context cx)
        {
            cx.Negate = null;
            string s = sentence;
            bool quick = false;
            if (s.Contains("(Quick Effect)"))
            {
                quick = true;
                s = s.Replace(" (Quick Effect)", "").Replace("(Quick Effect) ", "").Replace("(Quick Effect)", "");
            }

            string? trigger = null;
            string? require = null;
            string head = "";
            int colon = TopLevelIndex(s, ": ");
            if (s.StartsWith("FLIP:", StringComparison.OrdinalIgnoreCase))
            {
                trigger = "flip";
                s = s.Substring(5).Trim();
            }
            else if (colon > 0 && TopLevelIndex(s.Substring(0, colon), "; ") < 0)
            {
                head = s.Substring(0, colon).Trim();
                s = s.Substring(colon + 2).Trim();
                if (!Head(head, cx, out trigger, out require))
                    return Why("head: " + head);
            }
            else if (quick && s.StartsWith("You can", StringComparison.OrdinalIgnoreCase))
            {
                // "(Quick Effect): You can ..." with no condition.
            }

            s = Regex.Replace(s, "^you can ", "", Opt).Trim();
            // Old wording: "banish this card from your GY to target 1 ...;" / "..., then target 1 ...;"
            s = Regex.Replace(s, "^(banish this card from your GY|Tribute this card|discard this card)(?: to|, then) (target .+)$", "$1; $2", Opt);

            // The cost before "; " (a target clause there is part of the action).
            string? cost = null;
            int semi = TopLevelIndex(s, "; ");
            if (semi > 0)
            {
                string first = s.Substring(0, semi).Trim();
                string? c = Cost(first);
                if (c != null)
                {
                    cost = c;
                    s = s.Substring(semi + 2).Trim();
                }
                else if (!first.StartsWith("target ", StringComparison.OrdinalIgnoreCase))
                    return Why("cost: " + first);   // a cost the game cannot pay through a borrowed card
            }

            _why = null;
            string? action = Action(s, cx);
            if (action == null)
                return Why((_why ?? "selector") + ": " + s);
            bool negate = action.StartsWith("negate(");
            if (negate != (cx.Negate != null))
                return Why("chain response that is not a negation: " + action);
            if (negate)
            {
                // Spells/Traps: Trap Jammer's row. Monsters: only a Quick Effect that Tributes itself has a donor (Maryokutai).
                if (cx.IsMonster && cost != "tribute_self")
                    return Why("negate: monster negation with cost " + (cost ?? "none"));
                if (cost is "banish_self" or "discard_self" || (cost != null && cost.StartsWith("detach(")))
                    return Why("negate: cost " + cost);
                cx.EffectKind = cx.IsMonster ? "ignition" : "spell";
                return (require != null ? $"if {require}: " : "") + (cost != null ? $"cost {cost}: " : "") + action;
            }

            if (quick && cx.IsMonster)
                cx.Looser.Add("Quick Effect read as an ignition effect");
            bool selfSummon = action.StartsWith("special_summon(self");
            if (require != null && require.StartsWith("controls(") && Regex.IsMatch(action, "^(?:destroy|banish|return_to_hand)\\("))
                return Why("conflict: if controls(...) with " + action.Split('(')[0]);
            bool detachCost = cost != null && cost.StartsWith("detach(");
            bool selfCost = cost is "banish_self" or "discard_self" or "tribute_self" || detachCost;
            if (selfSummon && (trigger != null || selfCost))
            {
                if (trigger != null) return Why("self summon with a trigger: " + trigger);   // the game offers these on its own events (per card)
                return Why("self summon with cost " + cost);
            }
            if (selfCost && trigger != null)
            {
                cx.Looser.Add("trigger dropped: activated by paying the cost");
                trigger = null;
            }
            if (cx.IsMonster && trigger == null && !selfCost && !selfSummon)
                trigger = "ignition";
            if (!cx.IsMonster && trigger != null)
            {
                // A Spell/Trap with "When this card is activated:" etc.: its activation is the effect.
                if (trigger != "spell_activation") return Why("spell/trap with a trigger: " + trigger);
                trigger = null;
            }
            if (detachCost && !cx.Kind.Contains("Xyz"))
                return Why("detach on a non-Xyz");
            if (!cx.IsMonster && selfCost && cost != "banish_self")
                return Why("spell/trap with self cost " + cost);
            // An Equip Spell's only runnable effect is the equip itself; a Continuous / Field card's effect runs when the card is activated.
            if (!cx.IsMonster && cx.Icon == "Equip" && !action.StartsWith("equip(") && cost != "banish_self")
                return Why("equip spell effect other than equip: " + action.Split('(')[0]);
            if (!cx.IsMonster && cx.Icon is "Continuous" or "Field" && cost != "banish_self")
                cx.Looser.Add("runs once, when the card is activated");

            // Destroying / banishing your own card was only the way to the follow-up, which is not run: no use alone.
            if (Regex.IsMatch(action, "^(?:destroy|banish)\\((?:target|all), own") && cx.Looser.Any(l => l.StartsWith("not run:")))
                return Why("follow-up: own card removed only for a follow-up");
            cx.EffectKind = trigger is not null and not "ignition" ? trigger
                : cost is "tribute_self" || detachCost || trigger == "ignition" ? "ignition"
                : selfCost || selfSummon ? "hand/GY"
                : "spell";

            var script = new System.Text.StringBuilder();
            if (trigger != null) script.Append($"on {trigger}: ");
            if (require != null) script.Append($"if {require}: ");
            if (cost != null) script.Append($"cost {cost}: ");
            script.Append(action);
            return script.ToString();
        }

        private static int TopLevelIndex(string s, string token)
        {
            bool quoted = false;
            int depth = 0;
            for (int i = 0; i + token.Length <= s.Length; i++)
            {
                char c = s[i];
                if (c == '"') quoted = !quoted;
                else if (!quoted && c == '(') depth++;
                else if (!quoted && c == ')') depth = Math.Max(0, depth - 1);
                else if (!quoted && depth == 0 && string.CompareOrdinal(s, i, token, 0, token.Length) == 0)
                    return i;
            }
            return -1;
        }

        // ---- triggers and conditions -------------------------------------------------------------------------------------------------------

        private static readonly (Regex Rx, string Trigger)[] Triggers =
        {
            (Rx("(?:If|When) this card is Normal or Special Summoned"), "normal_or_special_summoned"),
            (Rx("(?:If|When) this card is Normal Summoned"), "normal_summoned"),
            (Rx("(?:If|When) this card is Special Summoned"), "special_summoned"),
            (Rx("(?:If|When) this card is (?:Summoned|Normal or Flip Summoned|Flip Summoned|Normal Summoned or flipped face-up)"), "summoned"),
            (Rx("(?:If|When) this card is flipped face-up"), "flip"),
            (Rx("(?:If|When) this card is destroyed by battle(?: and sent to the GY)?"), "destroyed_by_battle"),
            (Rx("(?:If|When) this card is sent from the field to the GY"), "sent_from_field_to_grave"),
            (Rx("(?:If|When) this card is sent to the GY"), "sent_to_grave"),
            (Rx("(?:Once per turn, )?during your (?:next )?Standby Phase"), "standby_phase"),
            (Rx("(?:Once per turn, )?during (?:the|each|your) End Phase"), "end_phase"),
            (Rx("(?:If|When) this card destroys (?:a|an opponent's) monster by battle(?: and sends it to the GY)?"), "destroys_by_battle"),
            (Rx("(?:If|When) this card inflicts battle damage to your opponent"), "battle_damage"),
            (Rx("(?:If|When) this card declares an attack"), "attack_declared"),
        };
        // Triggers that are close to one the game has: written with that one, the difference noted.
        private static readonly (Regex Rx, string Trigger, string Note)[] LooseTriggers =
        {
            (Rx("(?:If|When) this card is Special Summoned (?:from|by|because|while|during|in) .+"), "special_summoned", "only when Special Summoned "),
            (Rx("(?:If|When) this card is Normal Summoned (?:from|by|while|during) .+"), "normal_summoned", "only when Normal Summoned "),
            (Rx("(?:If|When) this card (?:on the field |in the Monster Zone |in your possession )?is destroyed(?: by battle or card effect| by card effect| by an opponent's card(?: effect)?)?(?: and sent to the GY)?"), "sent_from_field_to_grave", "destroyed, not only sent to the GY"),
            (Rx("(?:If|When) this card (?:on the field |in your possession )?is sent to the GY (?:by|because|as|to|for) .+"), "sent_to_grave", "sent to the GY "),
            (Rx("(?:If|When) this card is (?:Tributed|detached from an Xyz Monster.*)"), "sent_to_grave", "Tributed / detached read as sent to the GY"),
            (Rx("(?:If|When) this card is (?:Synchro|Xyz|Link|Fusion|Ritual|Pendulum|Tribute) Summoned(?: .*)?"), "special_summoned", "Synchro / Xyz / Link / Fusion / Ritual / Tribute Summon read as any Special Summon"),
        };

        private static bool Head(string head, Context cx, out string? trigger, out string? require)
        {
            trigger = null;
            require = null;
            head = Regex.Replace(head, " \\(except during the Damage Step\\)$", "", Opt).Trim().TrimEnd(',');
            foreach (var (rx, t) in Triggers)
                if (rx.IsMatch(head))
                {
                    trigger = t;
                    return true;
                }
            foreach (var (rx, t, note) in LooseTriggers)
                if (rx.IsMatch(head))
                {
                    trigger = t;
                    cx.Looser.Add(note.EndsWith(' ') ? note + head.Substring(head.IndexOf(" is ", StringComparison.Ordinal) + 4) : note);
                    return true;
                }
            if (Regex.IsMatch(head, "^Once per turn$", Opt) || Regex.IsMatch(head, "^During your Main Phase$", Opt) || Regex.IsMatch(head, "^Once per turn, during your Main Phase$", Opt))
            {
                if (head.StartsWith("Once", StringComparison.OrdinalIgnoreCase))
                    cx.OncePerTurn = true;
                return true;
            }
            if (Regex.IsMatch(head, "^During (?:the|either player's) Main Phase$", Opt) || Regex.IsMatch(head, "^During your opponent's (?:turn|Main Phase)$", Opt)
                || Regex.IsMatch(head, "^During (?:either player's|any) turn$", Opt))
            {
                cx.Looser.Add("timing read as your own Main Phase");
                return true;
            }
            if (Regex.IsMatch(head, "^If you control no monsters$", Opt))
            {
                require = "no_monsters";
                return true;
            }
            if (Regex.IsMatch(head, "^If this card is in your GY$", Opt))
            {
                cx.InGrave = true;   // "Special Summon this card" is then from the GY; a cost (banish this card) says it itself
                return true;
            }
            if (Regex.IsMatch(head, "^If this card is in your hand$", Opt))
                return true;   // the cost (discard this card) or "Special Summon this card" says it
            System.Text.RegularExpressions.Match control;
            if ((control = Regex.Match(head, "^If you control (?:a|an) (?<sel>.+?)(?:, except this card)?$", Opt)).Success && !head.Contains(" and ") && !head.Contains(" or ")
                && Selector(control.Groups["sel"].Value, cx, out var controlSel, singleCondition: true) && controlSel != ", card")
            {
                require = "controls(" + controlSel.TrimStart(',', ' ') + ")";
                return true;
            }
            if (!cx.IsMonster && Regex.IsMatch(head, "^When this card is activated$", Opt))
            {
                trigger = "spell_activation";
                return true;
            }
            if (NegateHead(head, cx, out var negateRequire) is { } negateArgs)
            {
                cx.Negate = negateArgs;
                require = negateRequire;
                return true;
            }
            return false;
        }

        // "When your opponent activates a Trap Card", "When a Spell/Trap Card, or monster effect, is activated", "When a card or effect is activated
        // that targets ..." -> the words of negate(...) (EffectScript, docs/EffectSystem.md section 39), or null.
        private static string? NegateHead(string head, Context cx, out string? require)
        {
            require = null;
            var m = Regex.Match(head, "^When (?:(?<opp>your opponent|a player|either player) activates (?<what>.+?)|(?<what>.+?),? is activated)(?<rest>(?: (?:that|which|in|during|while|on|from|to|targeting|including) .+)?)$", Opt);
            if (!m.Success)
                return null;
            string what = Regex.Replace(m.Groups["what"].Value, "^(?:a|an|the|1) ", "", Opt).Replace(",", "").Trim();
            string rest = m.Groups["rest"].Value.Trim();
            var words = new List<string>();
            string? property = null;
            var pm = Regex.Match(what, "^(?<prop>Normal|Counter|Field|Equip|Continuous|Quick-Play|Ritual) (?<kind>Spell|Trap)(?: Card)?$", Opt);
            if (pm.Success)
            {
                property = pm.Groups["prop"].Value.ToLowerInvariant().Replace("-", "");
                words.Add(pm.Groups["kind"].Value.ToLowerInvariant());
            }
            else
            {
                string? kind = what.ToLowerInvariant() switch
                {
                    "spell card" or "spell" => "spell",
                    "trap card" or "trap" => "trap",
                    "spell/trap card" or "spell/trap" or "spell or trap card" => "spelltrap",
                    "monster effect" or "monster's effect" or "effect monster's effect" => "monster",
                    "card or effect" or "spell/trap card or monster effect" or "spell card trap card or monster effect" or "monster effect spell or trap card"
                        or "spell/trap card or effect" or "monster effect or spell/trap card" => "any",
                    "spell card or effect" => "spell",
                    "trap card or effect" => "trap",
                    _ => null,
                };
                if (kind == null)
                    return null;
                words.Add(kind);
                if (what.EndsWith(" or effect", StringComparison.OrdinalIgnoreCase) && kind != "any")
                    words.Add("effect");   // a Spell/Trap effect too, not only the card's activation
            }
            if (m.Groups["opp"].Value.Equals("your opponent", StringComparison.OrdinalIgnoreCase))
                words.Add("opponent");
            if (rest.Length > 0)
            {
                // "while you control a X": the game's own "you control a matching card" check (require controls(...)); any other "while / if"
                // condition is not dropped - the card would be stronger than printed.
                System.Text.RegularExpressions.Match control;
                if ((control = Regex.Match(rest, "^while you control (?:a|an) (?<sel>.+?)$", Opt)).Success && !rest.Contains(" or ") && !rest.Contains(" and ")
                    && Selector(control.Groups["sel"].Value, cx, out var controlSel, singleCondition: true) && controlSel != ", card")
                    require = "controls(" + controlSel.TrimStart(',', ' ') + ")";
                else if (Regex.IsMatch(rest, "^(?:while|if|during your|during your opponent's) ", Opt))
                    return null;
                else if (Regex.IsMatch(rest, "^during the Battle Phase$", Opt))
                    words.Add("battle_phase");
                else if (Regex.IsMatch(rest, "^that targets exactly 1 .+\\(and no other cards\\)$", Opt))
                {
                    words.Add("targets_one");
                    cx.Looser.Add("negates any card that targets exactly 1 card (not only " + rest.Substring(rest.IndexOf("1 ", StringComparison.Ordinal) + 2) + ")");
                }
                else
                    cx.Looser.Add("negates any such activation, not only " + rest);
            }
            if (property != null)
                words.Add(property);
            return string.Join(", ", words);
        }

        // ---- costs ---------------------------------------------------------------------------------------------------------------------------

        private static string? Cost(string text)
        {
            System.Text.RegularExpressions.Match m;
            if (Regex.IsMatch(text, "^banish this card from your GY$", Opt)) return "banish_self";
            if (Regex.IsMatch(text, "^discard this card(?: to the GY)?$", Opt)) return "discard_self";
            if (Regex.IsMatch(text, "^Tribute this card$", Opt)) return "tribute_self";
            if ((m = Regex.Match(text, "^discard (\\d+|1|a|one) cards?$", Opt)).Success) return $"discard({Number(m.Groups[1].Value)})";
            if ((m = Regex.Match(text, "^pay (\\d+) LP$", Opt)).Success) return $"pay_lp({m.Groups[1].Value})";
            if (Regex.IsMatch(text, "^Tribute 1 (?:other )?monster$", Opt)) return "tribute(1)";
            if (Regex.IsMatch(text, "^send 1 (?:other )?card from your hand to the GY$", Opt)) return "discard(1)";   // read as a discard
            if ((m = Regex.Match(text, "^detach (\\d|1|a|one) (?:Xyz )?materials? from this card$", Opt)).Success) return $"detach({Number(m.Groups[1].Value)})";
            return null;
        }

        private static string Number(string word) => word.ToLowerInvariant() switch { "a" or "one" => "1", _ => word };

        // ---- actions -------------------------------------------------------------------------------------------------------------------------

        private const string Where = "(?: on the field| your opponent controls| you control| your opponent has)?";

        private static string? Action(string text, Context cx)
        {
            string s = text.Trim().TrimEnd('.');
            // Follow-ups the game will not run: dropped and noted.
            foreach (string tail in new[] { ", and if you do, ", ", then ", ", also ", ". Then, ", ", but ", ", and ", " and if you do, " })
            {
                int at = TopLevelIndex(s, tail);
                if (at > 0)
                {
                    cx.Looser.Add("not run:" + s.Substring(at + 1));
                    s = s.Substring(0, at);
                }
            }
            if (Regex.IsMatch(s, ", except this card$", Opt))
            {
                cx.Looser.Add("except this card");
                s = Regex.Replace(s, ", except this card$", "", Opt);
            }
            if (Regex.IsMatch(s, " in (?:face-up )?Defense Position$", Opt))
            {
                cx.Looser.Add("in Defense Position");
                s = Regex.Replace(s, " in (?:face-up )?Defense Position$", "", Opt);
            }

            System.Text.RegularExpressions.Match m;
            if (cx.Negate != null && Rx("negate (?:the|that|its) (?:activation|Summon|effect)(?: and (?:the |its )?effect)?(?: of that card)?").IsMatch(s))
            {
                // Negate-and-destroy is one machine in the game: "and if you do, destroy it" is what it does, not a dropped follow-up. Without it the
                // destroy is extra.
                int dropped = cx.Looser.FindIndex(l => l.StartsWith("not run:") && Regex.IsMatch(l, "^not run: ?(?:and if you do, )?destroy (?:it|that card|that monster)$", Opt));
                if (dropped >= 0) cx.Looser.RemoveAt(dropped);
                else cx.Looser.Add("also destroys the negated card");
                bool effectToo = Regex.IsMatch(s, "negate (?:the|that|its) effect", Opt) && !cx.Negate.Contains("monster") && !cx.Negate.Contains("effect");
                return $"negate({cx.Negate}{(effectToo ? ", effect" : "")})";
            }
            if ((m = Rx("draw (\\d+) cards?").Match(s)).Success) return $"draw({m.Groups[1].Value})";
            if ((m = Rx("(?:you )?gain (\\d+) LP").Match(s)).Success) return $"gain_lp({m.Groups[1].Value})";
            if ((m = Rx("inflict (\\d+) damage to your opponent").Match(s)).Success) return $"burn({m.Groups[1].Value})";
            if ((m = Rx("(?:the )?equipped monster gains (\\d+) ATK(?: and DEF)?").Match(s)).Success && cx.Icon == "Equip")
                return $"equip(atk {m.Groups[1].Value}{(s.Contains("and DEF") ? $", def {m.Groups[1].Value}" : "")})";
            if ((m = Rx("Special Summon this card(?: from your GY)?").Match(s)).Success && cx.IsMonster && (cx.InGrave || s.EndsWith("from your GY", StringComparison.OrdinalIgnoreCase)))
                return "special_summon(self_grave)";
            if ((m = Rx("Special Summon this card(?: from your hand)?").Match(s)).Success && cx.IsMonster) return "special_summon(self)";
            if ((m = Rx("target 1 face-up monster(?<side> on the field| you control)?; (?:it|that target) gains (?<n>\\d+) ATK(?: until the end of this turn)?").Match(s)).Success)
                return $"gain_atk({(m.Groups["side"].Value.Trim() == "you control" ? "own, " : "")}{m.Groups["n"].Value})";

            if ((m = Rx("add 1 (?<sel>.+?) from your Deck to your hand").Match(s)).Success)
                return Selector(m.Groups["sel"].Value, cx, out var sel) ? $"search(deck{sel})" : null;
            if ((m = Rx("send 1 (?<sel>.+?) from your Deck to the GY").Match(s)).Success)
                return Selector(m.Groups["sel"].Value, cx, out var sel) ? $"send_to_grave(deck{sel})" : null;
            if ((m = Rx("Special Summon 1 (?<sel>.+?) from your (?<zone>hand|Deck|GY|hand or GY|hand or Deck|Deck or GY|hand, Deck, or GY)").Match(s)).Success)
            {
                string zone = m.Groups["zone"].Value.ToLowerInvariant();
                if (zone.Contains(" or ") || zone.Contains(','))
                    cx.Looser.Add($"from your {zone} read as from your {zone.Split(' ', ',')[0]}");
                zone = zone.Split(' ', ',')[0] switch { "hand" => "hand", "deck" => "deck", _ => "grave" };
                if (!Selector(m.Groups["sel"].Value, cx, out var sel, monsterOnly: true)) return null;
                return zone == "grave" ? $"revive(grave{sel})" : $"special_summon({zone}{sel})";
            }
            if ((m = Rx("target 1 (?<sel>.+?) in (?<gy>your GY|your opponent's GY|either GY|any GY); Special Summon (?:it|that target)(?: to your field)?").Match(s)).Success)
            {
                string gy = m.Groups["gy"].Value.ToLowerInvariant() switch { "your gy" => "grave", "your opponent's gy" => "opponent_grave", _ => "either_grave" };
                return Selector(m.Groups["sel"].Value, cx, out var sel, monsterOnly: true) ? $"revive({gy}{sel})" : null;
            }
            if ((m = Rx("(?:target 1 (?<sel>.+?) in your GY; add (?:it|that target) to your hand|add 1 (?<sel>.+?) from your GY to your hand)").Match(s)).Success)
                return Selector(m.Groups["sel"].Value, cx, out var sel) ? $"add_to_hand(grave{sel})" : null;
            if ((m = Rx("target 1 of your banished (?<sel>.+?); add (?:it|that target) to your hand").Match(s)).Success)
                return Selector(m.Groups["sel"].Value, cx, out var sel) ? $"add_to_hand(banished{sel})" : null;
            if ((m = Rx("target 1 (?<sel>.+?)(?<where>" + Where + "); return (?:it|that target) to the (?:owner's )?hand").Match(s)).Success)
            {
                string side = m.Groups["where"].Value.Trim() switch { "your opponent controls" or "your opponent has" => "opponent", "you control" => "own", _ => "any" };
                return Selector(m.Groups["sel"].Value, cx, out var sel, singleCondition: true) ? $"return_to_hand(target, {side}{sel})" : null;
            }
            if ((m = Rx("target 1 (?<sel>.+?)(?<where>" + Where + "); (?<verb>destroy|banish) (?:it|that target)").Match(s)).Success)
                return Removal(m.Groups["verb"].Value.ToLowerInvariant(), "target", m.Groups["sel"].Value, m.Groups["where"].Value, cx);
            if ((m = Rx("(?<verb>destroy|banish) all (?<sel>.+?)(?<where> on the field| your opponent controls| you control)").Match(s)).Success)
                return Removal(m.Groups["verb"].Value.ToLowerInvariant(), "all", m.Groups["sel"].Value, m.Groups["where"].Value, cx);
            return Why("action");
        }

        private static string? Removal(string verb, string scope, string selText, string where, Context cx)
        {
            string side = where.Trim() switch { "your opponent controls" or "your opponent has" => "opponent", "you control" => "own", _ => "any" };
            if (!Selector(selText, cx, out var sel, singleCondition: true)) return null;
            if (scope == "all" && (sel.Contains("spell") || sel.Contains("trap") || sel == ", card"))
            {
                if (sel != ", card") return null;
                sel = ", monster";
                cx.Looser.Add("all cards read as all monsters");
            }
            return $"{verb}({scope}, {side}{sel})";
        }

        // ---- selectors -------------------------------------------------------------------------------------------------------------------

        /// <summary>
        /// "Level 4 or lower LIGHT Warrior monster", "\"Gem-Knight\" card", "Spell/Trap", "\"Polymerization\"" -> ", monster where ..." (empty for
        /// any card). false when a word cannot be expressed.
        /// </summary>
        private static bool Selector(string text, Context cx, out string selector, bool monsterOnly = false, bool singleCondition = false)
        {
            selector = "";
            string t = text.Trim();
            var conditions = new List<(int Rank, string Text)>();
            System.Text.RegularExpressions.Match m;

            // Alternatives ('"A" monster or 1 "B" Spell', '"A" or "B" monster'): the game's list rows hold one; the first is offered.
            if ((m = Regex.Match(t, ",? or 1 .+$", Opt)).Success && m.Index > 0)
            {
                cx.Looser.Add("only the first choice:" + m.Value.TrimStart(','));
                t = t.Substring(0, m.Index);
            }
            if ((m = Regex.Match(t, "^(?<first>\"[^\"]+\")(?:,? or|, and/or| and/or) \"[^\"]+\"(?<rest>.*)$", Opt)).Success)
            {
                cx.Looser.Add($"only the first name of {t}");
                t = m.Groups["first"].Value + m.Groups["rest"].Value;
            }

            // A quoted card name alone: that card ('add 1 "Polymerization"').
            if ((m = Rx("\"(?<name>[^\"]+)\"").Match(t)).Success)
            {
                int id = cx.CardIdByName(m.Groups["name"].Value);
                if (id == 0) return false;
                if (singleCondition) return false;   // destroy / banish filters take no card name
                selector = (monsterOnly ? ", monster" : ", card") + $" where name = {id}";
                return true;
            }

            if ((m = Regex.Match(t, " with (\\d+) or less ATK$", Opt)).Success)
            {
                if (m.Groups[1].Value == "1500") conditions.Add((4, "atk <= 1500"));
                else cx.Looser.Add($"with {m.Groups[1].Value} or less ATK");
                t = t.Substring(0, m.Index);
            }
            else if ((m = Regex.Match(t, " with .+$", Opt)).Success)
            {
                cx.Looser.Add(m.Value.Trim());
                t = t.Substring(0, m.Index);
            }
            foreach (string q in new[] { " except this card", ", except this card", " other than this card" })
                if (t.EndsWith(q, StringComparison.OrdinalIgnoreCase))
                {
                    cx.Looser.Add("except this card");
                    t = t.Substring(0, t.Length - q.Length);
                }
            t = Regex.Replace(t, "^(?:other |face-up |face-down |Set |of your |of your opponent's )+", "", Opt);

            string kind;
            if ((m = Regex.Match(t, "\\s*\\b(?<k>monsters?|cards?|Spell/Trap(?: Cards?)?|Spells?(?: Cards?)?|Traps?(?: Cards?)?|Spell or Trap(?: Cards?)?|Monster Cards?)$", Opt)).Success)
            {
                string k = m.Groups["k"].Value.ToLowerInvariant();
                kind = k.StartsWith("monster") ? "monster" : k.StartsWith("spell/trap") || k.StartsWith("spell or trap") ? "spelltrap" : k.StartsWith("spell") ? "spell" : k.StartsWith("trap") ? "trap" : "card";
                t = t.Substring(0, m.Index).Trim();
            }
            else
                return false;
            if (monsterOnly && kind != "monster") return false;
            if (kind == "spelltrap")
            {
                kind = singleCondition ? "spell" : "card";   // the destroy/banish filter's Spell & Trap zones; a search reads any card
                if (!singleCondition) cx.Looser.Add("Spell/Trap read as any card");
            }

            // Level, attribute, race, archetype, kind words.
            t = Regex.Replace(t, "Level (\\d+) or (lower|higher)", m2 => $"level{(m2.Groups[2].Value.Equals("lower", StringComparison.OrdinalIgnoreCase) ? "<=" : ">=")}{m2.Groups[1].Value}", Opt);
            t = Regex.Replace(t, "Level (\\d+)", "level=$1", Opt);
            t = string.Join("\"", t.Split('"').Select((part, i) => i % 2 == 1 ? part :
                Races.Aggregate(part, (txt, race) => Regex.Replace(txt, "\\b" + Regex.Escape(race.Text) + "\\b", race.Code))));
            foreach (System.Text.RegularExpressions.Match token in Regex.Matches(t, "\"[^\"]+\"|\\S+"))
            {
                string w = token.Value.TrimEnd(',');
                if (w.StartsWith("level", StringComparison.OrdinalIgnoreCase))
                {
                    string op = w.Contains("<=") ? "<=" : w.Contains(">=") ? ">=" : "=";
                    string n = Regex.Match(w, "\\d+").Value;
                    if (int.Parse(n) > 15) return false;
                    conditions.Add((3, $"level {op} {n}"));
                }
                else if (Attributes.Contains(w, StringComparer.OrdinalIgnoreCase))
                    conditions.Add((2, $"attribute = {char.ToUpperInvariant(w[0])}{w.Substring(1).ToLowerInvariant()}"));
                else if (Races.Any(r => r.Code == w))
                    conditions.Add((1, $"race = {w}"));
                else if (w.StartsWith('"'))
                {
                    int code = ArchetypeFor(w.Trim('"'), cx);
                    if (code == 0) return false;
                    conditions.Add((0, $"archetype = {code}"));
                }
                else if (Kinds.Contains(w, StringComparer.OrdinalIgnoreCase) || w.Equals("non-Tuner", StringComparison.OrdinalIgnoreCase))
                    cx.Looser.Add($"{w} not checked");
                else if (w is "or" or "and/or" || w.StartsWith("non-", StringComparison.OrdinalIgnoreCase))
                    return false;   // "LIGHT or DARK", "non-DARK": one condition per property only
                else
                    return false;
            }
            if (kind is "spell" or "trap" && conditions.Any(c => c.Rank is 1 or 2 or 3)) return false;
            if (conditions.Count > 0 && kind != "monster" && conditions.Any(c => c.Rank is 1 or 2 or 3)) kind = "monster";
            if (singleCondition && conditions.Count > 1)
            {
                var keep = conditions.OrderBy(c => c.Rank).First();
                cx.Looser.AddRange(conditions.Where(c => c != keep).Select(c => $"{c.Text} not checked"));
                conditions = [keep];
            }
            if (singleCondition && kind is "spell" or "trap" && conditions.Count > 0) return false;
            selector = ", " + kind + (conditions.Count > 0 ? " where " + string.Join(" and ", conditions.Select(c => c.Text)) : "");
            return true;
        }

        // The archetype code for a quoted name: the card's own code when it is one of the candidates (custom cards carry a custom code for an
        // archetype the game also has, Gem-Knight 84 / 547), else the first.
        private static int ArchetypeFor(string name, Context cx)
        {
            var codes = cx.ArchetypeCodes(name);
            if (codes == null || codes.Count == 0)
                return 0;
            foreach (int code in codes)
                if (cx.OwnArchetypes.Contains(code))
                    return code;
            return codes[0];
        }
    }
}
