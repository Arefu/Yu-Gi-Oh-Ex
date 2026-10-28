using System.Linq;
using System.Text.RegularExpressions;

namespace WolfEx
{
    /// <summary>
    /// Reads a whole card's text and writes the EffectScript for every effect in it that the game can run (docs/EffectSystem.md section 37):
    /// a trigger ("If this card is Normal Summoned:"), a condition the game checks ("If you control no monsters:"), a cost ("discard 1 card",
    /// "banish this card from your GY", "Tribute this card") and an action (draw, search, Special Summon, destroy, banish, ATK gain ...).
    /// Each effect becomes one EffectScript body; several are joined with 'also', the bullets of "activate 1 of these effects" with 'or'
    /// (effect "bullet text" { } blocks, so the prompt shows each bullet). What is not written is kept: <see cref="Result.NotRun"/> lists
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
        // once per Duel (classes 9 / 10) and N uses per turn (per copy, Get_UseLimitPerCopyCount; docs/EffectSystem.md section 74)
        private static readonly Regex OtherUseLimit = Rx("You can only use (?:this|that|the) effect of this card (?<n>once per Duel|(?:up to )?twice per turn|thrice per turn|up to four times per turn)(?<prev>, and its previous effect only once per turn)?\\.?");
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
            // Group >= 0: a bullet of "activate 1 of these effects" (the bullets of one header share the number); Name: the bullet's text.
            // Limit: LimitNone / LimitCopy ("Once per turn: ...") / LimitName ("You can only use this effect of X once per turn").
            var bodies = new List<(string Script, int Limit, int Group, string Name)>();
            var kinds = new HashSet<string>();
            bool allOnce = false;
            bool bulletsAllowed = true;
            string? choicePrefix = null;   // under an "activate 1 of these effects" header: its head / cost, put in front of each bullet
            int choiceGroup = -1;
            bool choiceEach = false;   // "... (but you can only use each effect of this card once per turn)": each bullet its own limit, no group
            int groups = 0;
            var auras = new List<string>();   // aura(...) statements: continuous ATK / DEF changes while this card is face-up
            string? pendingNoSummon = null;   // a "You cannot Special Summon the turn you activate this" sentence before any effect
            string? isMonsterOrSpellTrapAura(string s) => ContinuousRule(s, new Context(isMonster, kind, icon, ownArchetypes, cardIdByName, archetypeCodes));
            foreach (string raw in Sentences(text))
            {
                // A bullet belongs to the sentence before it: under a choice the game cannot run ("You can activate 1 of these effects"), it is not run.
                bool bullet = raw.StartsWith(Bullet);
                string sentence = bullet ? raw.Substring(Bullet.Length).Trim() : raw;
                if (!bullet)
                    choicePrefix = null;
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
                    // the effect just before it; the bullets of "activate 1 of these effects" are one effect
                    if (bodies.Count > 0)
                    {
                        int group = bodies[^1].Group;
                        for (int i = 0; i < bodies.Count; i++)
                            if (i == bodies.Count - 1 || (group >= 0 && bodies[i].Group == group))
                                bodies[i] = bodies[i] with { Limit = LimitName };
                    }
                    continue;
                }
                if (pendingNoSummon == null && !bodies.Any(b => b.Script.Contains(NoSummonMark))
                    && NoSummon(sentence, new Context(isMonster, kind, icon, ownArchetypes, cardIdByName, archetypeCodes)) is { } noSummon)
                {
                    // the plugin locks the card's controller when any effect of the card is activated: the effect it follows, or the first one
                    if (bodies.Count > 0)
                        bodies[^1] = bodies[^1] with { Script = bodies[^1].Script + NoSummonMark + noSummon + NoSummonMark };
                    else
                        pendingNoSummon = noSummon;
                    continue;
                }
                if (OtherUseLimit.Match(sentence) is { Success: true } other && bodies.Count > 0 && bodies[^1].Group < 0)
                {
                    int limit = other.Groups["n"].Value.ToLowerInvariant() switch
                    {
                        "once per duel" => LimitDuel,
                        "twice per turn" or "up to twice per turn" => LimitUses + 2,
                        "thrice per turn" => LimitUses + 3,
                        _ => LimitUses + 4,
                    };
                    bodies[^1] = bodies[^1] with { Limit = limit };
                    if (other.Groups["prev"].Success && bodies.Count > 1 && bodies[^2].Group < 0)
                        bodies[^2] = bodies[^2] with { Limit = LimitName };   // "..., and its previous effect only once per turn"
                    continue;
                }
                if (!bullet && !Restriction.IsMatch(sentence) && ChoiceHead.Match(sentence) is { Success: true } choice)
                {
                    // "[head:] [cost / target,] (You can) activate 1 of these effects[;]": each bullet becomes an effect of its own with the header's
                    // head and cost (EffectScript bodies joined with 'or'), the player picks one when it is activated, and they share the header's
                    // once-per-turn limit - unless it says each of them can be used once per turn (docs/EffectSystem.md section 58).
                    string pre = choice.Groups["pre"].Value.Trim();
                    choicePrefix = pre.Length == 0 ? "" : pre.EndsWith(':') ? pre + " " : pre + "; ";
                    string post = choice.Groups["post"].Value;
                    choiceEach = Regex.IsMatch(post, "\\beach (?:effect|of these effects)\\b.*once per turn", Opt);
                    choiceGroup = choiceEach ? -1 : groups++;
                    string rest = Regex.Replace(post, "\\(?\\s*,?\\s*but you can only use each (?:effect|of these effects)[^)]*once per turn\\)?", "", Opt).Trim(' ', ',', ';', '.', ':');
                    if (rest.Length > 0)
                        looser.Add($"not run: \"{rest}\" ({Short(sentence)})");
                    bulletsAllowed = true;
                    continue;
                }
                if (bullet && choicePrefix != null)
                {
                    string bulletText = sentence;
                    string whole = choicePrefix.EndsWith("; ") && bulletText.Length > 0
                        ? choicePrefix + char.ToLowerInvariant(bulletText[0]) + bulletText[1..]
                        : choicePrefix + bulletText;
                    var choiceContext = new Context(isMonster, kind, icon, ownArchetypes, cardIdByName, archetypeCodes);
                    _why = null;
                    string? choiceScript = Effect(whole, choiceContext);
                    if (choiceScript == null)
                    {
                        notRun.Add(sentence);
                        LastReasons.Add(_why ?? "unknown");
                        continue;
                    }
                    kinds.Add(choiceContext.EffectKind);
                    looser.AddRange(choiceContext.Looser.Select(l => $"{l} ({Short(sentence)})"));
                    bodies.Add((choiceScript, choiceEach ? LimitName : choiceContext.Limit, choiceGroup, bulletText));
                    continue;
                }
                // only_one (section 75): "You can only control 1 "<this card>"" / "There can only be 1 face-up "<this card>" on the field"
                // (the Cards mod writes the card's own name as "this card")
                if (!bullet && Regex.Match(sentence.Trim().TrimEnd('.'), "^(?:You can only control 1 (?:face-up )?(?:\"(?<own>[^\"]+)\"|(?<own>this card))|There can only be 1 face-up (?:\"(?<field>[^\"]+)\"|(?<field>this card)) on the field)$", Opt) is { Success: true } only
                    && (only.Groups["own"].Success ? only.Groups["own"].Value : only.Groups["field"].Value) is var who
                    && (who.Equals("this card", StringComparison.OrdinalIgnoreCase) || who.Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    auras.Add(only.Groups["own"].Success ? "only_one" : "only_one(field)");
                    continue;
                }
                // xyz_also (section 94): "(Once per turn, )you can also Xyz Summon this card by using 1 X you control (as material)"
                if (!bullet && kind.Contains("Xyz") && XyzAlso(sentence, new Context(isMonster, kind, icon, ownArchetypes, cardIdByName, archetypeCodes), looser) is { } alsoXyz)
                {
                    auras.Add(alsoXyz);
                    continue;
                }
                if (!bullet && isMonsterOrSpellTrapAura(sentence) is { } aura)
                {
                    auras.Add(aura);   // a continuous ATK / DEF change (EffectScript aura, docs/EffectSystem.md section 69)
                    continue;
                }
                if (!bullet && Covered(sentence, kind) is { } covered)
                {
                    // a summoning rule the card's kind or summoning data already gives the game (or, noted, one it can't enforce exactly)
                    if (covered.Length > 0)
                        looser.Add($"{covered} ({Short(sentence)})");
                    continue;
                }
                if (Restriction.IsMatch(sentence))
                {
                    // "Gains these effects while ...": its bullets only hold under that condition, so they are not run either.
                    if (!bullet)
                        bulletsAllowed = !ChoiceHeader.IsMatch(sentence);
                    notRun.Add(sentence);
                    LastReasons.Add("rule: " + sentence.Trim());   // the whole rule, so the harness can group the wordings
                    continue;
                }
                var context = new Context(isMonster, kind, icon, ownArchetypes, cardIdByName, archetypeCodes);
                _why = null;
                // "..., also you cannot Special Summon for the rest of this turn, except X": the effect, then a lock added when it has resolved
                string effectText = sentence;
                string? tailLock = pendingNoSummon == null && !bodies.Any(b => b.Script.Contains(NoSummonMark)) ? ResolveLock(ref effectText, context) : null;
                string? script = Effect(effectText, context);
                if (script != null && tailLock != null)
                    script += NoSummonMark + tailLock + NoSummonMark;
                if (!bullet)
                    bulletsAllowed = script != null || !ChoiceHeader.IsMatch(sentence);
                if (script == null)
                {
                    notRun.Add(sentence);
                    LastReasons.Add(_why ?? "unknown");
                    continue;
                }
                // Several effects of the same kind (two ignition effects, two for the same event, the bullets of "activate 1 of these effects")
                // are all kept: Yu-Gi-Oh-Effects offers the card once and asks which one, as the game's own cards do (docs/EffectSystem.md section 45).
                kinds.Add(context.EffectKind);
                looser.AddRange(context.Looser.Select(l => $"{l} ({Short(sentence)})"));
                bodies.Add((script, context.Limit, -1, ""));
            }
            if (pendingNoSummon != null)
            {
                if (bodies.Count > 0)
                    bodies[0] = bodies[0] with { Script = bodies[0].Script + NoSummonMark + pendingNoSummon + NoSummonMark };
                else
                {
                    notRun.Add("(summon restriction of an effect that was not translated)");
                    LastReasons.Add("rule: You cannot Special Summon the turn you activate this (no effect to attach it to)");
                }
            }
            string auraText = string.Concat(auras.Select(a => a + "; "));
            if (bodies.Count == 0)
                return auras.Count == 0 ? new Result("", 0, notRun, looser) : new Result(auraText.TrimEnd(), auras.Count, notRun, looser);
            // The compiler runs a 'then' chain only in a card's first effect: an ungrouped chained effect goes first, and any later chain keeps its
            // first action (noted).
            int chained = bodies.FindIndex(b => b.Script.Contains(" then "));
            if (chained > 0 && bodies[chained].Group < 0 && !bodies[0].Script.Contains(" then "))
            {
                var moved = bodies[chained];
                bodies.RemoveAt(chained);
                bodies.Insert(0, moved);
            }
            for (int i = 1; i < bodies.Count; i++)
                if (bodies[i].Script.Contains(" then "))
                {
                    looser.Add($"only the first action of a later effect's chain ({Short(bodies[i].Script)})");
                    bodies[i] = bodies[i] with { Script = Regex.Replace(bodies[i].Script, " then [^;]*", "") };
                }
            // "You can only use each effect of this card once per turn" makes every effect's limit per card name
            // a summon lock (NoSummonMark ... NoSummonMark in the script) is written after the limit, as the grammar orders them
            string Body((string Script, int Limit, int Group, string Name) b)
            {
                var lockMark = Regex.Match(b.Script, NoSummonMark + "(.*?)" + NoSummonMark);
                string script = lockMark.Success ? b.Script.Remove(lockMark.Index, lockMark.Length) : b.Script;
                return script + ((allOnce && b.Limit < LimitDuel ? LimitName : b.Limit) switch
                {
                    LimitName => "; once_per_turn",
                    LimitCopy => "; once_per_turn(copy)",
                    LimitDuel => "; once_per_duel",
                    >= LimitUses and var n => $"; uses_per_turn({n - LimitUses})",
                    _ => "",
                }) + (lockMark.Success ? "; " + lockMark.Groups[1].Value : "");
            }
            // the bullets of one "activate 1 of these effects" that were written: joined with 'or' (one choice, one limit)
            bool Joined(int i) => i > 0 && bodies[i].Group >= 0 && bodies[i].Group == bodies[i - 1].Group;
            if (!bodies.Where((b, i) => Joined(i)).Any())
                return new Result(auraText + string.Join(" also ", bodies.Select(Body)) + ";", bodies.Count + auras.Count, notRun, looser);
            // With a choice: effect { } blocks, so each bullet's text is what the choose-an-effect prompt shows.
            var blocks = new System.Text.StringBuilder();
            for (int i = 0; i < bodies.Count; i++)
            {
                if (i > 0)
                    blocks.Append(Joined(i) ? " or " : " ");
                string label = bodies[i].Group >= 0 ? " \"" + bodies[i].Name.Replace('"', '\'') + "\"" : "";
                // a hand-or-GY effect, or a "this card from your hand or field" cost, is two effects joined with 'or' (Effect): one block each
                string body = Body(bodies[i]);
                var orNext = Regex.Match(body, " or (?=(?:on |if |cost ))");
                int split = orNext.Success ? orNext.Index : -1;
                if (split > 0)
                {
                    string limit = Regex.Match(body, "; once_per_turn(?:\\(copy\\))?$").Value;
                    blocks.Append($"effect{label} {{ {body[..split]}{limit}; }} or effect{label} {{ {body[(split + 4)..]}; }}");
                }
                else
                    blocks.Append($"effect{label} {{ {body}; }}");
            }
            return new Result(auraText + blocks, bodies.Count + auras.Count, notRun, looser);
        }

        /// <summary>
        /// A summoning rule the game already has from elsewhere, so it is not an effect to run: "" when the card's kind or summoning data gives
        /// it exactly, a note when the game can't enforce it exactly (the card still plays), null when it is not one of these.
        /// <list type="bullet">
        /// <item>"Cannot be Normal Summoned/Set." on a card whose kind can't be Normal Summoned (the Special-Summon-only kinds, Extra Deck
        /// and Ritual monsters: KIND_TABLE DeckSection 1 / 2);</item>
        /// <item>"You can Ritual Summon this card with "X"." on a Ritual Monster: the Ritual table pair ("ritualSpell");</item>
        /// <item>"Must be Fusion / Synchro / Xyz / Link / Ritual Summoned." on that kind;</item>
        /// <item>"Must (first) be Special Summoned (with / by) ..." and "You can only Special Summon this card once per turn (this way)."
        /// on any monster: noted, the game doesn't hold the card to them.</item>
        /// </list>
        /// </summary>
        internal static string? Covered(string sentence, string kind)
        {
            string s = sentence.Trim().TrimEnd('.');
            bool extra = Regex.IsMatch(kind, "^(?:Fusion|Synchro|Xyz|Link)", Opt);
            bool ritual = kind.StartsWith("Ritual", StringComparison.OrdinalIgnoreCase);
            bool specialOnly = kind.Contains("SpecialSummonOnly", StringComparison.OrdinalIgnoreCase) || extra || ritual;
            if (Regex.IsMatch(s, "^Cannot be Normal Summoned(?:/Set| or Set)?$", Opt))
                return specialOnly ? "" : null;
            if (ritual && Regex.IsMatch(s, "^You can Ritual Summon this card with (?:\"[^\"]+\"|a \"[^\"]+\" card|any Ritual Spell)$", Opt))
                return "";
            var must = Regex.Match(s, "^Must be (Fusion|Synchro|Xyz|Link|Ritual) Summoned$", Opt);
            if (must.Success)
                return kind.StartsWith(must.Groups[1].Value, StringComparison.OrdinalIgnoreCase) ? "" : null;
            if (kind is "Spell" or "Trap")
                return null;
            if (Regex.IsMatch(s, "^Must (?:first )?be (?:Special Summoned|Special Summoned \\(from your [^)]+\\)) (?:with|by|from|in)\\b", Opt)
                || Regex.IsMatch(s, "^Must (?:first )?be Special Summoned by (?:card effect|its own effect|a card effect)", Opt)
                // "Must be either Fusion Summoned, or Special Summoned (from your Extra Deck) by ..." / "Must first be either ...": a Summon kind or a
                // card's procedure; the custom card plays by its kind and summoning data (noted)
                || Regex.IsMatch(s, "^Must (?:first )?be either (?:Fusion|Synchro|Xyz|Link|Ritual) Summoned,? or Special Summoned\\b", Opt))
                return "summon restriction not enforced";
            if (Regex.IsMatch(s, "^You can only Special Summon (?:this card|\"[^\"]+\") once per turn(?: this way)?$", Opt))
                return "once-per-turn summon limit not enforced";
            return null;
        }

        // Once-per-turn limits (docs/EffectSystem.md section 59): none, each copy on the field (EffectScript once_per_turn(copy), the engine's zone
        // markers), per card name (once_per_turn).
        private const int LimitNone = 0, LimitCopy = 1, LimitName = 2, LimitDuel = 3, LimitUses = 10;   // LimitUses + N = N uses per turn
        private const string NoSummonMark = "\u0002";

        // "You cannot Special Summon (monsters) (from the Extra Deck) (during) the turn you activate this card / effect (, except X monsters)"
        // -> no_special_summon(extra, kind, except selector) (docs/EffectSystem.md section 77). null = another wording, or an "except" the
        // rule cannot test (Level, ATK, names ...).
        private static string? NoSummon(string sentence, Context cx)
        {
            string s = sentence.Trim().TrimEnd('.');
            var m = Regex.Match(s, "^You cannot Special Summon(?: any)?(?: monsters)?(?<extra> from (?:the|your) Extra Deck)?(?:, except (?<exc1>[^,]+),)? (?:during )?the turn you activate (?:this (?:card|effect)|either of this card's effects|any of this card's effects)(?:, except (?<exc>.+))?$", Opt);
            if (!m.Success)
            {
                // "For the rest of this turn after this card resolves, you cannot Special Summon ...": locked once the card has resolved
                m = Regex.Match(s, "^For the rest of this turn after this card resolves, you cannot Special Summon(?: any)?(?: monsters)?(?<extra> from (?:the|your) Extra Deck)?(?:, except (?<exc>.+))?$", Opt);
                return m.Success ? NoSummonLock(m.Groups["extra"].Success, true, m.Groups["exc"].Value, cx) : null;
            }
            return NoSummonLock(m.Groups["extra"].Success, false, m.Groups["exc"].Success ? m.Groups["exc"].Value : m.Groups["exc1"].Value, cx);
        }

        // ", also / but you cannot Special Summon (from the Extra Deck) for the rest of this turn(, except X)" (or "also for the rest of this turn
        // (after this card resolves), you cannot Special Summon ...") ending an effect: the effect without it and the resolve-time lock, or null.
        private static readonly Regex ResolveLockTail = new("(?:,| and)? (?:also|but),? (?:(?<pre>for the rest of this turn(?: after this card resolves)?), )?you cannot Special Summon(?: any)?(?: monsters)?(?<extra> from (?:the|your) Extra Deck)?(?<post> for the rest of this turn)?(?:, except (?<exc>[^,;]+(?:,? (?:and|or) [^,;]+)?))?\\.?$", Opt);
        private static string? ResolveLock(ref string sentence, Context cx)
        {
            var m = ResolveLockTail.Match(sentence);
            if (!m.Success || (m.Groups["pre"].Success == m.Groups["post"].Success))
                return null;
            string? lockText = NoSummonLock(m.Groups["extra"].Success, true, m.Groups["exc"].Value, cx);
            if (lockText != null)
                sentence = sentence.Substring(0, m.Index).TrimEnd(' ', ',');
            return lockText;
        }

        private static string? NoSummonLock(bool extraOnly, bool resolved, string except, Context cx)
        {
            var args = new List<string>();
            if (extraOnly)
                args.Add("extra");
            if (resolved)
                args.Add("resolved");
            if (except.Length > 0)
            {
                var kindWord = Regex.Match(except, "\\b(Fusion|Synchro|Xyz|Link|Ritual)\\b", Opt);
                if (kindWord.Success)
                {
                    args.Add(kindWord.Value.ToLowerInvariant());
                    except = Regex.Replace(except, "\\s*\\b(?:Fusion|Synchro|Xyz|Link|Ritual)\\b", "", Opt).Trim();
                }
                except = Regex.Replace(except, "-Type\\b", "", Opt);
                if (!Regex.IsMatch(except, "^(?:Monsters?|monsters?)$", Opt))
                {
                    if (!Selector(Singular(except), cx, out var sel, monsterOnly: true) || sel.Contains("level") || sel.Contains("atk") || sel.Contains("name"))
                        return null;
                    args.Add("except " + sel.TrimStart(',', ' '));
                }
            }
            return "no_special_summon" + (args.Count > 0 ? "(" + string.Join(", ", args) + ")" : "");
        }

        private const string Bullet = "\u25CF ";
        // "[head:] [cost / target ...(, then | to)] (You can) activate 1 of these effects[ (but you can only use each effect ... once per turn)];":
        // pre = the head and / or cost the bullets share, post = what follows (a once-per-turn rule for each bullet, or text that is not run).
        private static readonly Regex ChoiceHead = new("^(?<pre>.*?)(?:,? then |\\s+to |\\s*)(?:you can )?(?:activate|apply) (?:1|one) of (?:these|the following) effects\\b(?<post>.*)$", Opt);
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
            // The effect's own once-per-turn limit from its head: LimitCopy for "Once per turn: ..." / "Once per turn, <head>: ..." (each copy),
            // LimitName when the engine can't count it per copy (an effect used off the field, see Effect). "You can only use ..." sentences are
            // read by Translate.
            public int Limit = LimitNone;
            public bool InGrave;   // the head said "If this card is in your GY"
            public bool InHand;    // the head said "If this card is in your hand"
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
                s = Regex.Replace(s, "^: ", "");   // "(Quick Effect): You can ..." with no head left
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
            // A summoning procedure: "Special Summon this card (from your hand) by discarding 1 card" -> "discard 1 card; Special Summon this card
            // from your hand": the cost, then the hand self summon (Watch Cat's row, a composed cost). In the game a procedure does not start a
            // Chain; here it is an effect that does (noted).
            s = Regex.Replace(s, "^Special Summon this card \\(from your hand\\)", "Special Summon this card from your hand", Opt);
            if (Regex.Match(s, "^Special Summon this card(?: from your hand)? by (?<verb>discarding|sending|Tributing|paying|banishing|revealing|returning|shuffling|detaching) (?<rest>.+)$", Opt) is { Success: true } procedure)
            {
                string verb = procedure.Groups["verb"].Value.ToLowerInvariant() switch
                {
                    "discarding" => "discard", "sending" => "send", "tributing" => "Tribute", "paying" => "pay", "banishing" => "banish",
                    "revealing" => "reveal", "returning" => "return", "shuffling" => "shuffle", _ => "detach",
                };
                s = $"{verb} {procedure.Groups["rest"].Value}; Special Summon this card from your hand";
                cx.Looser.Add("a summoning procedure, here an effect that starts a Chain");
            }
            // Old wording: "banish this card from your GY to target 1 ...;" / "..., then target 1 ...;"
            s = Regex.Replace(s, "^(banish this card from your GY|Tribute this card|discard this card|reveal this card in your hand|send this card (?:from (?:your hand|the field) )?to the GY|discard (?:\\d+|a|one) cards?|detach (?:\\d+|a|one) (?:Xyz )?materials? from this card)(?: to|, then) (target .+)$", "$1; $2", Opt);
            // "You can activate this effect; ...": no cost, the words only mark where the activation ends
            s = Regex.Replace(s, "^activate this effect; ", "", Opt);
            // "<any cost this reads>, then target ...; ..." = "<cost>; target ...; ..." (the fixed list above covers the oldest wordings)
            if (Regex.Match(s, "^(?<c>[^;]+?), then (?<t>target .+)$", Opt) is { Success: true } thenTarget
                && (Cost(thenTarget.Groups["c"].Value) != null || SelfCostPair(thenTarget.Groups["c"].Value) != null || PickCost(thenTarget.Groups["c"].Value, cx) != null))
                s = thenTarget.Groups["c"].Value + "; " + thenTarget.Groups["t"].Value;

            // The cost before "; " (a target clause there is part of the action).
            string? cost = null;
            string? costOr = null;   // "from your hand or field": the same effect again with this cost, joined with 'or'
            int semi = TopLevelIndex(s, "; ");
            if (semi > 0)
            {
                string first = s.Substring(0, semi).Trim();
                // after "If this card is in your GY": "You can banish it;" = banish this card from the GY
                string? c = Cost(first) ?? (cx.InGrave && Regex.IsMatch(first, "^banish it$", Opt) ? "banish_self" : null);
                if (c == "send_self_field" && cx.InHand && Regex.IsMatch(first, "^send this card to the GY$", Opt))
                    c = "send_self";   // "If this card is in your hand: You can send this card to the GY"
                if (c == null && SelfCostPair(first) is { } pair)
                    (c, costOr) = pair;
                c ??= PickCost(first, cx);
                // "banish this card" with no place: where the head put it (GY / hand), else a monster on the field
                if (c == null && Regex.IsMatch(first, "^banish this card$", Opt))
                    c = cx.InGrave ? "banish_self" : cx.InHand ? "banish_self_hand" : cx.IsMonster ? "banish_self_field" : null;
                // "You can reveal this card in your hand; ...": no cost at all - the game's reveal cards (Impcantation Chalislime 14109, Danger!
                // Thunderbird! 14067) are hand-effects table rows with an empty slot 3, revealing = activating it from the hand (docs section 83)
                if (c == null && cx.IsMonster && trigger == null && Regex.IsMatch(first, "^reveal this card in your hand$", Opt))
                {
                    cx.InHand = true;
                    s = s.Substring(semi + 2).Trim();
                }
                else if (c != null)
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
                if (cost != null && cost != "tribute_self" && !Regex.IsMatch(cost, "^(?:discard|pay_lp|tribute)\\(") || costOr != null)
                    return Why("negate: cost " + cost);
                cx.EffectKind = cx.IsMonster ? "ignition" : "spell";
                return (require != null ? $"if {require}: " : "") + (cost != null ? $"cost {cost}: " : "") + action;
            }

            // "(Quick Effect)": spell speed 2 (EffectScript "quick"; Yu-Gi-Oh-Effects answers the speed for it, docs/EffectSystem.md section 52)
            bool markQuick = quick && cx.IsMonster;
            // an ignition effect is only usable in your own Main Phase: a window outside it needs a Quick Effect, a wider one is noted
            if (require != null && cx.IsMonster && !markQuick && trigger == null)
            {
                if (Regex.IsMatch(require, "(?:^| )(?:your_|opponent_)?battle_phase\\b|(?:^| )opponent_"))
                    return Why("phase window outside your Main Phase on an effect that is not a Quick Effect");
                if (Regex.IsMatch(require, "(?:^| )(?:main_phase|main_or_battle_phase|your_main_or_battle_phase|your_turn)\\b"))
                    cx.Looser.Add("used in your own Main Phase only (not a Quick Effect)");
            }
            bool selfSummon = action.StartsWith("special_summon(self");
            if (require != null && require.Contains("controls(") && !require.Contains("where name =") && Regex.IsMatch(action, "(?:^| then )(?:destroy|banish|return_to_hand|change_position|set_face_down|take_control|negate_effects|change_atk|move\\(target)\\b"))
                return Why("conflict: if controls(...) with " + action.Split('(')[0]);
            bool detachCost = cost != null && cost.StartsWith("detach(");
            // a cost paid from the GY with this card (banish this card (and N X) from your GY) places the effect in the GY
            bool graveSelfCost = cost == "banish_self" || (cost != null && cost.StartsWith("banish_self_and("));
            bool fieldSelfCost = cost is "tribute_self" or "send_self_field" or "banish_self_field";
            bool selfCost = graveSelfCost || fieldSelfCost || cost is "discard_self" or "send_self" or "banish_self_hand" or "tribute_self_hand" || detachCost;
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
            // "If this card is in your GY: You can ...": activated from the GY (EffectScript on in_grave:). A self cost already places it
            // (banish this card from your GY), and "Special Summon this card" from the GY has its own donor.
            if (cx.InGrave && cx.IsMonster && trigger == null && !selfCost && !selfSummon)
                trigger = "in_grave";
            // "If this card is in your hand: You can <action>" with no self cost: an own hand effect (EffectScript on in_hand, section 66)
            if (cx.InHand && cx.IsMonster && trigger == null && !selfCost && !selfSummon)
                trigger = "in_hand";
            if (cx.IsMonster && trigger == null && !selfCost && !selfSummon)
                trigger = "ignition";
            // A Continuous / Field Spell or Trap watching other cards: offered from its zone by the plugin (docs/EffectSystem.md section 91)
            bool spellTrapWatch = !cx.IsMonster && trigger != null && trigger.StartsWith("card_");
            if (!cx.IsMonster && trigger != null && !spellTrapWatch)
            {
                // A Spell/Trap with "When this card is activated:" etc.: its activation is the effect.
                if (trigger != "spell_activation") return Why("spell/trap with a trigger: " + trigger);
                trigger = null;
            }
            if (detachCost && !cx.Kind.Contains("Xyz"))
                return Why("detach on a non-Xyz");
            if (!cx.IsMonster && selfCost && !graveSelfCost)
                return Why("spell/trap with self cost " + cost);
            // An Equip Spell's only runnable effect is the equip itself; a Continuous / Field card's effect runs when the card is activated.
            if (!cx.IsMonster && cx.Icon == "Equip" && !action.StartsWith("equip(") && !graveSelfCost)
                return Why("equip spell effect other than equip: " + action.Split('(')[0]);
            if (!cx.IsMonster && cx.Icon is "Continuous" or "Field" && !graveSelfCost && !spellTrapWatch)
                cx.Looser.Add("runs once, when the card is activated");

            // Destroying / banishing your own card was only the way to the follow-up, which is not run: no use alone.
            if (Regex.IsMatch(action, "^(?:destroy|banish)\\((?:target|all), own") && cx.Looser.Any(l => l.StartsWith("not run:")))
                return Why("follow-up: own card removed only for a follow-up");
            // A per-copy limit is a marker on the card's own zone, and the engine only checks it for a card on the field (Timing_Check_Class1
            // 0x1400F78E0: zone > 12 passes): an effect used from the GY / hand, or one that triggers once the card is in the GY, would have no
            // limit at all, so it is counted per card name instead.
            if (cx.Limit == LimitCopy && ((trigger != null && (Regex.IsMatch(trigger, "^(?:in_grave|in_hand|sent_to_grave|sent_from_field_to_grave|destroyed_by_battle|banished)\\b") || trigger.Contains("in_grave")))
                || graveSelfCost || cost is "discard_self" or "send_self" or "banish_self_hand" or "tribute_self_hand" || costOr != null || selfSummon))
            {
                cx.Limit = LimitName;
                cx.Looser.Add("once per turn counted per card name (the game counts per copy only on the field)");
            }
            cx.EffectKind = trigger is not null and not "ignition" ? trigger
                : fieldSelfCost || detachCost || trigger == "ignition" ? "ignition"
                : selfCost || selfSummon ? "hand/GY"
                : "spell";

            var script = new System.Text.StringBuilder();
            if (trigger != null) script.Append($"on {trigger}: ");
            if (require != null) script.Append($"if {require}: ");
            if (cost != null) script.Append($"cost {cost}: ");
            script.Append(action);
            if (markQuick)
                script.Append("; quick");
            // "If this card is in your hand or GY: You can ...": the same effect from either zone = a hand effect 'or' a GY effect (one choice
            // group, so they share one once-per-turn limit; the card is only ever in one of the zones)
            if (cx.InHand && trigger == "in_grave")
                return Regex.Replace(script.ToString(), "^on in_grave: ", "on in_hand: ") + " or " + script;
            // "... this card from your hand or field": one row per zone, as the game writes it (SelfCostPair)
            if (costOr != null)
                return script + " or " + script.ToString().Replace($"cost {cost}: ", $"cost {costOr}: ");
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
            // Whose phase: the engine's phase handlers test the turn player against the card's controller (docs/EffectSystem.md section 60).
            // "the" Standby / End Phase in card text is either player's.
            (Rx("(?:Once per turn, )?during your (?:next )?Standby Phase"), "standby_phase"),
            (Rx("During your opponent's Standby Phase"), "opponent_standby_phase"),
            (Rx("During (?:the|each|either player's|each player's) Standby Phase"), "each_standby_phase"),
            (Rx("(?:Once per turn, )?during your End Phase"), "end_phase"),
            (Rx("During your opponent's End Phase"), "opponent_end_phase"),
            (Rx("During (?:the|each|either player's|each player's) End Phase"), "each_end_phase"),
            // the game's "arrived this turn" End Phase handler (section 79): either player's End Phase
            (Rx("(?:Once per turn, )?[Dd]uring the End Phase, if this card is in the (?:GY|Graveyard) because it was sent there this turn"), "grave_end_phase"),
            (Rx("(?:Once per turn, )?[Dd]uring the End Phase, if this card is banished because it was banished this turn"), "banished_end_phase"),
            (Rx("(?:If|When) this card destroys (?:a|an opponent's) monster by battle(?: and sends it to the GY)?"), "destroys_by_battle"),
            (Rx("(?:If|When) this card inflicts battle damage to your opponent"), "battle_damage"),
            (Rx("(?:If|When) this card declares an attack"), "attack_declared"),
            // the plugin's own attack triggers (EffectSystem.md section 67): a monster on the field
            (Rx("When an opponent's monster declares an attack"), "opponent_attack_declared"),
            (Rx("When this card is targeted for an attack"), "attacked"),
            // the start-of-Damage-Step evaluator (section 80); "an opponent's monster" is the same battle (a monster is always the opponent's)
            (Rx("At the start of the Damage Step, if this card battles(?: an opponent's monster| a monster)?"), "damage_step_battles"),
            (Rx("At the start of the Damage Step, if this card attacks(?: an opponent's monster| a monster)?"), "damage_step_attacks"),
        };
        // Triggers that are close to one the game has: written with that one, the difference noted.
        private static readonly (Regex Rx, string Trigger, string Note)[] LooseTriggers =
        {
            (Rx("(?:If|When) this card is Special Summoned (?:from|by|because|while|during|in) .+"), "special_summoned", "only when Special Summoned "),
            (Rx("(?:If|When) this card is Normal Summoned (?:from|by|while|during) .+"), "normal_summoned", "only when Normal Summoned "),
            (Rx("(?:If|When) this card (?:on the field |in the Monster Zone |in your possession )?is destroyed(?: by battle or card effect| by card effect| by an opponent's card(?: effect)?)?(?: and sent to the GY)?"), "sent_from_field_to_grave", "destroyed, not only sent to the GY"),
            // "in its owner's possession / control / Spell & Trap Zone ... destroyed by an opponent's card (effect)": who destroyed it is not checked
            (Rx("(?:If|When) this card in its owner's (?:possession|control|Spell & Trap Zone|Monster Zone) is destroyed(?: by (?:an opponent's|your opponent's) (?:card(?: effect)?|attack or card effect))?(?: and sent to the GY)?"),
                "sent_from_field_to_grave", "destroyed by an opponent's card not checked (any destruction that sends it from the field to the GY)"),
            (Rx("(?:If|When) this card (?:on the field |in your possession )?is sent to the GY (?:by|because|as|to|for) .+"), "sent_to_grave", "sent to the GY "),
            (Rx("(?:If|When) this card is (?:Tributed|detached from an Xyz Monster.*)"), "sent_to_grave", "Tributed / detached read as sent to the GY"),
            (Rx("(?:If|When) this card is (?:Synchro|Xyz|Link|Fusion|Ritual|Pendulum|Tribute) Summoned(?: .*)?"), "special_summoned", "Synchro / Xyz / Link / Fusion / Ritual / Tribute Summon read as any Special Summon"),
        };

        // "If this card is Special Summoned from the GY by a card effect" / "If this card is Xyz Summoned" / "If this card is Tribute Summoned":
        // the summon trigger with its conditions (EffectScript summonConditions, docs/EffectSystem.md section 46). null = not wholly understood.
        private static readonly Regex SummonedAs = new("^(?:If|When) this card is (Normal|Special|Fusion|Synchro|Xyz|Ritual|Link|Pendulum|Tribute) Summoned(.*)$", Opt);
        private static readonly Regex SummonFrom = new("^ from (?:your |the |a |your opponent's )?(hand|Deck|GY|Graveyard|Extra Deck|banishment|banished cards|Main Deck)(?: or (?:your |the )?(hand|Deck|GY|Graveyard|Extra Deck))?", Opt);
        private static readonly Regex SummonBy = new("^ (?:by|with) (?:a card effect|card effects?|an effect|the effect of a \"([^\"]+)\" (?:card|monster)|a \"([^\"]+)\" card's effect|a Spell Card(?: effect)?|a Trap Card(?: effect)?|a Spell effect|a Trap effect|a monster effect|the effect of a monster|its own effect)", Opt);

        private static string? SummonHead(string head)
        {
            var m = SummonedAs.Match(head);
            if (!m.Success)
                return null;
            string how = m.Groups[1].Value.ToLowerInvariant();
            string trigger = how is "normal" or "tribute" ? "normal_summoned" : "special_summoned";
            var parts = new List<string>();
            if (how is "fusion" or "synchro" or "xyz" or "ritual" or "link" or "pendulum")
                parts.Add(how);
            else if (how == "tribute")
                parts.Add("tribute");
            string rest = m.Groups[2].Value;
            while (rest.Length > 0)
            {
                if (SummonFrom.Match(rest) is { Success: true } from)
                {
                    foreach (Group g in from.Groups.Values.Skip(1).Where(g => g.Success))
                        parts.Add("from " + g.Value.ToLowerInvariant() switch
                        {
                            "hand" => "hand",
                            "deck" or "main deck" => "deck",
                            "extra deck" => "extra",
                            "gy" or "graveyard" => "grave",
                            _ => "banished",
                        });
                    rest = rest[from.Length..];
                }
                else if (SummonBy.Match(rest) is { Success: true } by)
                {
                    string text = by.Value.ToLowerInvariant();
                    string? archetype = by.Groups[1].Success ? by.Groups[1].Value : by.Groups[2].Success ? by.Groups[2].Value : null;
                    parts.Add(archetype != null ? $"by \"{archetype}\""
                        : text.Contains("spell") ? "by spell" : text.Contains("trap") ? "by trap" : text.Contains("monster") ? "by monster"
                        : text.Contains("its own") ? "by own" : "by effect");
                    rest = rest[by.Length..];
                }
                else
                    return null;   // more than the conditions know ("while you control ...", "in Defense Position" ...): left to the loose reading
            }
            // An archetype the catalog does not know would not compile: leave such a card to the loose reading.
            if (parts.FirstOrDefault(p => p.StartsWith("by \"")) is { } named && ArchetypeCatalog.CodeOf(named[4..^1]) == 0)
                return null;
            return parts.Count == 0 ? trigger : $"{trigger}({string.Join(", ", parts)})";
        }

        // "If this card is banished (from the GY)", "If this card is sent from the hand or field to the GY (by a card effect)": the plugin's own move
        // triggers with their conditions (EffectScript banished / sent_to_grave(from ..., by ...), docs/EffectSystem.md section 65). null = not one.
        private static readonly string MoveZoneWords = "the hand|your hand|the Deck|your Deck|the GY|your GY|the field|the Monster Zone|your Monster Zone|the Spell & Trap Zone|your Spell & Trap Zone|hand|Deck|GY|field|Monster Zone|Spell & Trap Zone";
        private static string MoveZone(string word) => Regex.Replace(word, "^(?:the|your) ", "", Opt).ToLowerInvariant() switch
        {
            "hand" => "hand", "deck" => "deck", "gy" => "grave", "field" => "field", "monster zone" => "monster", _ => "spelltrap",
        };

        private static string? MoveHead(string head)
        {
            var m = Regex.Match(head, $"^(?:If|When) this card is banished(?: from (?<z>(?:{MoveZoneWords})(?:,? or (?:{MoveZoneWords}))*))?$", Opt);
            if (m.Success)
                return m.Groups["z"].Success ? $"banished({string.Join(", ", Regex.Split(m.Groups["z"].Value, ",? or ").Select(z => "from " + MoveZone(z)))})" : "banished";
            m = Regex.Match(head, $"^(?:If|When) this card is sent from (?<z>(?:{MoveZoneWords})(?:,? or (?:{MoveZoneWords}))*) to (?:the|your) GY(?<by> by (?:a )?card effect)?$", Opt);
            if (m.Success)
            {
                var parts = Regex.Split(m.Groups["z"].Value, ",? or ").Select(z => "from " + MoveZone(z)).Distinct().ToList();
                if (m.Groups["by"].Success) parts.Add("by effect");
                return $"sent_to_grave({string.Join(", ", parts)})";
            }
            if (Regex.IsMatch(head, "^(?:If|When) this card is sent to the GY, except from the field$", Opt))
                return "sent_to_grave(from hand, from deck)";
            if (Regex.IsMatch(head, "^(?:If|When) this card is sent to the GY by (?:a )?card effect$", Opt))
                return "sent_to_grave(by effect)";
            return null;
        }

        // Reacting to another card's summon (EffectScript opponent_special_summoned / opponent_summoned / monster_special_summoned, docs/EffectSystem.md
        // section 68), with where the monster came from: "If your opponent Special Summons a monster(s) (face-up) from the Extra Deck". null = not one.
        private static string? ReactionHead(string head, Context cx)
        {
            var m = Regex.Match(head, "^(?:If|When) (?:(?<opp>your opponent Special Summons)|(?<oppAny>your opponent Normal or Special Summons)) (?:a monster\\(s\\)|a monster|monsters)(?<rest>.*)$", Opt);
            string? trigger = m.Success ? (m.Groups["opp"].Success ? "opponent_special_summoned" : "opponent_summoned") : null;
            if (trigger == null)
            {
                m = Regex.Match(head, "^(?:If|When) (?:a monster\\(s\\) is|a monster is|monsters are) Special Summoned(?<rest>.*)$", Opt);
                if (m.Success) trigger = "monster_special_summoned";
            }
            if (trigger == null)
                return null;
            string rest = m.Groups["rest"].Value;
            if (Regex.IsMatch(rest, "^ face-up", Opt))
            {
                rest = rest[8..];
                cx.Looser.Add("face-up not checked");
            }
            if (rest.Length == 0)
                return trigger;
            var from = Regex.Match(rest, "^ from (?:the |your |their |your opponent's )?(?<z>Extra Deck|GY|Graveyard|hand|Deck)$", Opt);
            if (!from.Success)
                return null;   // "with 2000 or more ATK", "to your opponent's field" ...: not read
            string zone = from.Groups["z"].Value.ToLowerInvariant() switch { "extra deck" => "extra", "hand" => "hand", "deck" => "deck", _ => "grave" };
            return $"{trigger}(from {zone})";
        }

        // "If a (face-up) <X> monster(s) you control is destroyed by battle or card effect(, while this card is in your GY)", "If a card(s) your opponent
        // controls leaves the field by an opponent's card effect", "If a monster(s) is sent from your opponent's hand or Deck to the GY": the
        // watcher triggers (EffectScript card_destroyed / card_left_field / card_sent_to_grave / card_banished, docs/EffectSystem.md section 91).
        // null = not one.
        private static string? WatchHead(string head, Context cx)
        {
            string h = head.Trim();
            var conditions = new List<string>();
            if (Regex.Match(h, ",? (?:while|and) this card is in your (?:GY|Graveyard)$", Opt) is { Success: true } grave)
            {
                conditions.Add("in_grave");
                h = h[..grave.Index];
            }
            else if (Regex.Match(h, ",? while this card is (?:in the Monster Zone|on the field|face-up on the field|in your Monster Zone)$", Opt) is { Success: true } onField)
                h = h[..onField.Index];
            h = Regex.Replace(h, ",? even during the Damage Step$", "", Opt);
            var m = Regex.Match(h, "^(?:If|When) (?<who>(?:a|an|another|1 or more) .+?|your .+?) (?<verb>is|are|leaves|leave) (?<rest>.+)$", Opt);
            if (!m.Success)
                return null;
            string who = m.Groups["who"].Value, verb = m.Groups["verb"].Value, rest = m.Groups["rest"].Value;
            // whose card: "you control" / "your opponent controls" / "on the field" / "your <X> monster" / "owned by your opponent"
            if (Regex.Match(who, " (?<side>you control|your opponent controls|on the field|in your possession|owned by your opponent|in your Monster Zone|in your Spell & Trap Zone)$", Opt) is { Success: true } where)
            {
                string side = where.Groups["side"].Value.ToLowerInvariant();
                if (side is "you control" or "in your possession" or "in your monster zone" or "in your spell & trap zone") conditions.Add("own");
                else if (side is "your opponent controls" or "owned by your opponent") conditions.Add("opponent");
                if (side == "in your monster zone") conditions.Add("from monster");
                if (side == "in your spell & trap zone") conditions.Add("from spelltrap");
                who = who[..where.Index];
            }
            if (Regex.IsMatch(who, "^your ", Opt))
            {
                conditions.Add("own");
                who = "a " + who[5..];
            }
            if (Regex.IsMatch(who, "^another ", Opt))
            {
                conditions.Add("other");
                who = "a " + who[8..];
            }
            if (Regex.IsMatch(who, ", except this card$", Opt))
            {
                conditions.Add("other");
                who = Regex.Replace(who, ", except this card$", "", Opt);
            }
            who = Regex.Replace(who, "^(?:a|an|1 or more) (?:face-up )?", "", Opt);
            who = Regex.Replace(who, "\\(s\\)", "", Opt);
            if (!Regex.IsMatch(who, "(?:^|\\s)(?:monster|card|Monster Card|Spell|Trap|Spell/Trap|Spell Card|Trap Card)$", Opt))
                return null;
            if (Regex.IsMatch(who, "^(?:card)$", Opt))
                who = "";
            else
            {
                if (!Selector(Singular(who), cx, out var sel) || sel.Contains("level"))
                    return null;
                if (sel.TrimStart(',', ' ') is { Length: > 0 } s && s != "card")
                    conditions.Insert(0, "is " + s);
            }
            string trigger;
            string r = rest.Trim();
            string? By(string phrase) => phrase.ToLowerInvariant() switch
            {
                "" or " by battle or card effect" or " by battle or by card effect" or " by card effect or battle" => "",
                " by battle" => "by battle",
                " by card effect" or " by a card effect" => "by effect",
                " by an opponent's card effect" or " by your opponent's card effect" or " by an opponent's card" or " by your opponent's card" => "by opponent",
                _ => null,
            };
            System.Text.RegularExpressions.Match v;
            if (verb is "is" or "are" && (v = Regex.Match(r, "^destroyed(?<by>(?: by [^,]+?)?)(?: and sent to the GY)?$", Opt)).Success)
            {
                trigger = "card_destroyed";
                if (By(v.Groups["by"].Value) is not { } by) return null;
                if (by.Length > 0) conditions.Add(by);
            }
            else if (verb is "leaves" or "leave" && (v = Regex.Match(r, "^the field(?<by>(?: by [^,]+?)?)$", Opt)).Success)
            {
                trigger = "card_left_field";
                if (By(v.Groups["by"].Value) is not { } by || by == "by battle") return null;
                if (by.Length > 0) conditions.Add(by);
            }
            else if (verb is "is" or "are" && (v = Regex.Match(r, "^(?<verb>sent|banished)(?: from (?<from>(?:your |your opponent's |the |their )?(?:hand|Deck|field|Main Deck)(?:(?:,? or |, and/or | and/or )(?:your |the |their )?(?:hand|Deck|field))*))?(?: to (?<gy>the GY|your GY|your opponent's GY|their GY))?(?<by>(?:,? by (?:a )?card effect| by an opponent's card effect| by your opponent's card effect)?)(?:, except by battle)?$", Opt)).Success)
            {
                bool sent = v.Groups["verb"].Value.Equals("sent", StringComparison.OrdinalIgnoreCase);
                if (sent && !v.Groups["gy"].Success) return null;
                trigger = sent ? "card_sent_to_grave" : "card_banished";
                string from = v.Groups["from"].Value;
                if (Regex.IsMatch(from, "your opponent's|their", Opt) && !conditions.Contains("own")) conditions.Add("opponent");
                foreach (string z in Regex.Split(Regex.Replace(from, "(?:your opponent's|your|the|their) ", "", Opt), ",? or |, and/or | and/or ").Where(z => z.Length > 0))
                    conditions.Add("from " + (z.ToLowerInvariant() switch { "hand" => "hand", "field" => "field", _ => "deck" }));
                string gy = v.Groups["gy"].Value.ToLowerInvariant();
                if (gy is "your opponent's gy" or "their gy" && !conditions.Contains("own") && !conditions.Contains("opponent")) conditions.Add("opponent");   // owner = controller (noted)
                else if (gy == "your gy" && !conditions.Contains("own") && !conditions.Contains("opponent")) conditions.Add("own");
                if (gy is "your gy" or "your opponent's gy" or "their gy") cx.Looser.Add("whose GY read as whose card it was");
                string by = v.Groups["by"].Value.Trim(' ', ',').ToLowerInvariant();
                if (by.Contains("opponent")) conditions.Add("by opponent");
                else if (by.Length > 0 || r.EndsWith(", except by battle", StringComparison.OrdinalIgnoreCase)) conditions.Add("by effect");
            }
            else
                return null;
            if (Regex.IsMatch(head, "\\bface-up\\b", Opt) && trigger is "card_destroyed" or "card_left_field")
                cx.Looser.Add("face-up not checked");
            var list = conditions.Distinct().ToList();
            if (list.Contains("own") && list.Contains("opponent"))
                return null;
            return list.Count == 0 ? trigger : $"{trigger}({string.Join(", ", list)})";
        }

        private static bool Head(string head, Context cx, out string? trigger, out string? require)
        {
            trigger = null;
            require = null;
            head = Regex.Replace(head, " \\(except during the Damage Step\\)$", "", Opt).Trim().TrimEnd(',');
            // "Once per turn" / "Once per turn, <head>": the head with a per-copy limit (each copy on the field; the engine's use-limit classes 1-4)
            if (Regex.IsMatch(head, "^Once per turn$", Opt))
            {
                cx.Limit = LimitCopy;
                return true;
            }
            if (Regex.Match(head, "^Once per turn, (?<rest>.+)$", Opt) is { Success: true } once)
            {
                string rest = once.Groups["rest"].Value;
                if (!Head(char.ToUpperInvariant(rest[0]) + rest[1..], cx, out trigger, out require))
                    return false;
                cx.Limit = LimitCopy;
                return true;
            }
            foreach (var (rx, t) in Triggers)
                if (rx.IsMatch(head))
                {
                    trigger = t;
                    return true;
                }
            // "At the start / end of the (your / your opponent's) Battle Phase(, if this card battled)": own Battle Phase triggers for a monster on
            // the field (the start-of-Battle-Phase step / the end-of-Battle-Phase handler table; docs/EffectSystem.md section 93)
            if (cx.IsMonster && Regex.Match(head, "^At the (?<when>start|end) of (?<who>the|each|either player's|your|your opponent's) Battle Phase(?<battled>, if this card battled(?: an opponent's monster| a monster)?(?: this turn)?)?$", Opt) is { Success: true } bp)
            {
                string who = bp.Groups["who"].Value.ToLowerInvariant() switch { "your" => "your_", "your opponent's" => "opponent_", _ => "" };
                if (bp.Groups["when"].Value.Equals("end", StringComparison.OrdinalIgnoreCase) && who == "opponent_")
                    return false;   // no handler family for the end of only your opponent's Battle Phase
                trigger = who + "battle_phase_" + bp.Groups["when"].Value.ToLowerInvariant();
                if (bp.Groups["battled"].Success)
                    require = "battled";   // a monster only ever battles an opponent's monster
                return true;
            }
            if (SummonHead(head) is { } summon)
            {
                trigger = summon;
                return true;
            }
            if (MoveHead(head) is { } moved)
            {
                trigger = moved;
                return true;
            }
            if (ReactionHead(head, cx) is { } reaction)
            {
                trigger = reaction;
                return true;
            }
            // a monster, or a Continuous / Field Spell or Trap (Offer_EffectOfNamedCardOnField scans the Spell & Trap Zones for a non-monster)
            if ((cx.IsMonster || cx.Icon is "Continuous" or "Field") && WatchHead(head, cx) is { } watch)
            {
                trigger = watch;
                return true;
            }
            foreach (var (rx, t, note) in LooseTriggers)
                if (rx.IsMatch(head))
                {
                    trigger = t;
                    cx.Looser.Add(note.EndsWith(' ') ? note + head.Substring(head.IndexOf(" is ", StringComparison.Ordinal) + 4) : note);
                    return true;
                }
            if (Regex.IsMatch(head, "^During your Main Phase$", Opt))
                return true;
            // a phase window, alone or before a condition: "During (the / your / your opponent's) Main (or Battle) Phase(, if <condition>)",
            // "During your (opponent's) turn(, if ...)" -> if <phase> [and <condition>] (Check_Phase, docs/EffectSystem.md section 93)
            if (Regex.Match(head, "^During (?<who>the|either player's|your|your opponent's) (?<when>Main Phase|Main or Battle Phase|Battle Phase|turn)(?:, (?:you can|(?<rest>if .+)))?$", Opt) is { Success: true } window
                && !(window.Groups["who"].Value is "the" or "either player's" && window.Groups["when"].Value == "turn"))
            {
                string who = window.Groups["who"].Value.ToLowerInvariant() switch { "your" => "your_", "your opponent's" => "opponent_", _ => "" };
                string phase = window.Groups["when"].Value.ToLowerInvariant() switch
                {
                    "main phase" => who + "main_phase",
                    "main or battle phase" => who + "main_or_battle_phase",
                    "battle phase" => who + "battle_phase",
                    _ => who + "turn",
                };
                string? inner = null;
                if (window.Groups["rest"].Success)
                {
                    string rest = window.Groups["rest"].Value;
                    if (!Head(char.ToUpperInvariant(rest[0]) + rest[1..], cx, out var innerTrigger, out inner) || innerTrigger != null)
                        return false;
                }
                // "During your Main Phase" is when an ignition effect can be used anyway
                if (phase == "your_main_phase")
                    phase = "";
                require = string.Join(" and ", new[] { phase, inner ?? "" }.Where(p => p.Length > 0));
                if (require.Length == 0)
                    require = null;
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
            // whose Battle Phase (Yu-Gi-Oh-Effects Check_BattlePhase: the duel's current phase + turn player; docs/EffectSystem.md section 81)
            if (Regex.Match(head, "^During (?<who>the|either player's|your|your opponent's) Battle Phase$", Opt) is { Success: true } battle)
            {
                require = battle.Groups["who"].Value.ToLowerInvariant() switch { "your" => "your_battle_phase", "your opponent's" => "opponent_battle_phase", _ => "battle_phase" };
                return true;
            }
            // your LP (Yu-Gi-Oh-Effects Check_LifePoints; docs/EffectSystem.md section 82)
            if (Regex.Match(head, "^(?:During your Main Phase, )?(?:if|If) your LP are (?<n>\\d+) or less$", Opt) is { Success: true } lp)
            {
                require = $"lp_at_most({lp.Groups["n"].Value})";
                return true;
            }
            if (Regex.IsMatch(head, "^(?:During your Main Phase, )?(?:if|If) your LP are lower than your opponent's$", Opt))
            {
                require = "lp_lower";
                return true;
            }
            if (Regex.IsMatch(head, "^(?:During your Main Phase, )?(?:if|If) this card is in your GY$", Opt))
            {
                cx.InGrave = true;   // "Special Summon this card" is then from the GY; a cost (banish this card) says it itself
                return true;
            }
            if (Regex.IsMatch(head, "^(?:During your Main Phase, )?(?:if|If) this card is in your GY, except the turn it was sent (?:there|to the GY)$", Opt)
                || Regex.IsMatch(head, "^During your Main Phase, except the turn this card was sent to the GY$", Opt))
            {
                cx.InGrave = true;
                cx.Looser.Add("can also be used the turn it was sent to the GY");
                return true;
            }
            if (Regex.IsMatch(head, "^(?:During your Main Phase, )?(?:if|If) this card is in your hand or GY$", Opt))
            {
                cx.InHand = cx.InGrave = true;   // one effect usable from either zone: written as a hand and a GY effect sharing one limit (Effect)
                return true;
            }
            if (Regex.IsMatch(head, "^(?:During your Main Phase, )?(?:if|If) this card is in your hand$", Opt))
            {
                cx.InHand = true;   // a self cost (discard this card) or "Special Summon this card" says it itself; else an own hand effect
                return true;
            }
            System.Text.RegularExpressions.Match control;
            // "If you control "X"": by name (controls(card where name = id) -> "controlsName", docs/EffectSystem.md section 78)
            if ((control = Regex.Match(head, "^If you control (?:a face-up )?\"(?<name>[^\"]+)\"$", Opt)).Success && cx.CardIdByName(control.Groups["name"].Value) is var nameId and > 0)
            {
                require = $"controls(card where name = {nameId})";
                return true;
            }
            if ((control = Regex.Match(head, "^If you control (?:a|an) (?<sel>.+?)(?:, except this card)?$", Opt)).Success && !head.Contains(" and ") && !head.Contains(" or ")
                && Selector(control.Groups["sel"].Value, cx, out var controlSel, singleCondition: true) && controlSel != ", card" && !controlSel.Contains("level = "))   // controls() tests a Level with <= / >= only
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
                if ((control = Regex.Match(rest, "^while you control (?:a face-up )?\"(?<name>[^\"]+)\"$", Opt)).Success && cx.CardIdByName(control.Groups["name"].Value) is var nameId and > 0)
                    require = $"controls(card where name = {nameId})";
                else if ((control = Regex.Match(rest, "^while you control (?:a|an) (?<sel>.+?)$", Opt)).Success && !rest.Contains(" or ") && !rest.Contains(" and ")
                    && Selector(control.Groups["sel"].Value, cx, out var controlSel, singleCondition: true) && controlSel != ", card" && !controlSel.Contains("level = "))   // controls() tests a Level with <= / >= only
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
            // the game's send-to-GY self costs (section 84): from the hand (a move, not a discard) / from the field
            if (Regex.IsMatch(text, "^send this card from your hand to the GY$", Opt)) return "send_self";
            if (Regex.IsMatch(text, "^send this (?:face-up )?card (?:from (?:the|your) field |you control )?to the GY$", Opt)) return "send_self_field";
            // banish / Tribute this card from the hand or the field (section 85: Cost_BanishSelfFromHand / Field, Cost_TributeSelfFromHand)
            if (Regex.IsMatch(text, "^banish this card from your hand$", Opt)) return "banish_self_hand";
            if (Regex.IsMatch(text, "^banish this (?:face-up )?card (?:you control|from (?:the|your) field)$", Opt)) return "banish_self_field";
            if (Regex.IsMatch(text, "^Tribute this card from your hand$", Opt)) return "tribute_self_hand";
            if ((m = Regex.Match(text, "^discard (\\d+|1|a|one) cards?$", Opt)).Success) return $"discard({Number(m.Groups[1].Value)})";
            if ((m = Regex.Match(text, "^pay (\\d+) LP$", Opt)).Success) return $"pay_lp({m.Groups[1].Value})";
            if (Regex.IsMatch(text, "^Tribute 1 (?:other )?monster$", Opt)) return "tribute(1)";
            if (Regex.IsMatch(text, "^send 1 (?:other )?card from your hand to the GY$", Opt)) return "send_hand(1, card)";   // a move, not a discard (section 90)
            if ((m = Regex.Match(text, "^detach (\\d|1|a|one) (?:Xyz )?materials? from this card$", Opt)).Success) return $"detach({Number(m.Groups[1].Value)})";
            return null;
        }

        // "send / banish / Tribute this card from your hand or field (or GY)": the game writes these as one row per zone (Artifact Lancea,
        // Battlin' Boxer Counterpunch), so the effect is written twice, joined with 'or' (one choice group, one limit; the card is only ever
        // in one of the zones). null = not this wording.
        private static (string First, string Second)? SelfCostPair(string text)
        {
            var m = Regex.Match(text, "^(?<verb>send|banish|Tribute) this card from your (?<a>hand|field|face-up field|GY) or (?<b>hand|field|face-up field|GY)(?: to the GY)?$", Opt);
            if (!m.Success)
                return null;
            string? Word(string zone) => (m.Groups["verb"].Value.ToLowerInvariant(), zone.Replace("face-up ", "").ToLowerInvariant()) switch
            {
                ("send", "hand") => "send_self", ("send", "field") => "send_self_field",
                ("banish", "hand") => "banish_self_hand", ("banish", "field") => "banish_self_field", ("banish", "gy") => "banish_self",
                ("tribute", "hand") => "tribute_self_hand", ("tribute", "field") => "tribute_self",
                _ => null,
            };
            string? a = Word(m.Groups["a"].Value), b = Word(m.Groups["b"].Value);
            return a != null && b != null && a != b ? (a, b) : null;
        }

        // "banish (this card and) N <X> from your GY" -> banish_grave(N, selector) / banish_self_and(N, selector) (section 85)
        // and the filtered pick costs with no donor (section 90): "discard N <X>" -> discard(N, X), "Tribute N <X> (you control)" -> tribute(N, X),
        // "send N <X> from your hand to the GY" -> send_hand(N, X), "reveal 1 <X> in your hand" -> reveal(X)
        private static string? PickCost(string text, Context cx)
        {
            System.Text.RegularExpressions.Match m;
            string Count(string word) => Number(word) is "an" ? "1" : Number(word);
            string? Filter(string what) => Selector(Singular(what), cx, out var sel) ? (sel.TrimStart(',', ' ') is { Length: > 0 } s ? s : "card") : null;
            if ((m = Regex.Match(text, "^banish (?<self>this card and )?(?<n>\\d+|a|an|one) (?<what>.+?) from your GY$", Opt)).Success)
                return Filter(m.Groups["what"].Value) is { } banished ? $"{(m.Groups["self"].Success ? "banish_self_and" : "banish_grave")}({Count(m.Groups["n"].Value)}, {banished})" : null;
            if ((m = Regex.Match(text, "^discard (?<n>\\d+|a|an|one) (?<what>.+?)$", Opt)).Success)
                return Filter(m.Groups["what"].Value) is { } discarded ? $"discard({Count(m.Groups["n"].Value)}, {discarded})" : null;
            if ((m = Regex.Match(text, "^Tribute (?<n>\\d+|a|an|one) (?<what>.+?)(?: you control)?$", Opt)).Success && !Regex.IsMatch(m.Groups["what"].Value, "\\bfrom\\b|\\bhand\\b", Opt))
                return Filter(m.Groups["what"].Value) is { } tributed && !tributed.StartsWith("spell") && !tributed.StartsWith("trap")
                    ? $"tribute({Count(m.Groups["n"].Value)}, {tributed})" : null;
            if ((m = Regex.Match(text, "^send (?<n>\\d+|a|an|one) (?<what>.+?) from your hand to the GY$", Opt)).Success)
                return Filter(m.Groups["what"].Value) is { } sent ? $"send_hand({Count(m.Groups["n"].Value)}, {sent})" : null;
            if ((m = Regex.Match(text, "^send (?:1|a|an|one) (?<what>.+?) from your hand or (?:face-up )?field to the GY$", Opt)).Success)
                return Filter(m.Groups["what"].Value) is { } handOrField ? $"send_hand_field({handOrField})" : null;
            if ((m = Regex.Match(text, "^reveal (?:1|a|an|one) (?<what>.+?) in your hand$", Opt)).Success)
                return Filter(m.Groups["what"].Value) is { } revealed ? $"reveal({revealed})" : null;
            return null;
        }

        private static string Number(string word) => word.ToLowerInvariant() switch { "a" or "one" => "1", _ => word };

        // ---- actions -------------------------------------------------------------------------------------------------------------------------

        private const string Where = "(?: on the field| your opponent controls| you control| your opponent has)?";

        // Follow-ups that are a whole action of their own run after it with "then" (the general resolver, docs/EffectSystem.md section 53);
        // the rest ("then Special Summon it") are dropped and noted.
        private static readonly string[] ChainTails = { ", then ", ". Then, ", ", also ", ", and if you do, ", " and if you do, " };

        private static string? Action(string text, Context cx)
        {
            string? first = ActionPart(text, cx, out var follow);
            if (first == null)
                return null;
            foreach (var (joint, tail) in follow)
            {
                // Only the last action may target (its target is picked at activation), as the compiler checks.
                int looser = cx.Looser.Count;
                string step = tail;
                if (Regex.IsMatch(step, "^you can ", Opt))
                {
                    step = step.Substring(8);
                    cx.Looser.Add("\"then you can\" is not optional: it always runs");
                }
                // a first move(target, ...) may be followed (the card's row takes over the targeting, section 86); any other target ends the chain
                bool targetMoveFirst = Regex.IsMatch(first, "^(?:move|change_position|set_face_down|take_control|negate_effects|change_atk)\\(target,") && !first.Contains(" then ");
                string? next = Regex.IsMatch(first, "target") && !targetMoveFirst ? null : Action(step, cx);
                if (next != null && targetMoveFirst && Regex.IsMatch(next, "target"))
                    next = null;
                // "then Special Summon this card": the hand-summon donor is activated from the hand by itself, it cannot be a later step
                if (next != null && next.StartsWith("special_summon(self", StringComparison.Ordinal))
                    next = null;
                if (next == null)
                {
                    cx.Looser.RemoveRange(looser, cx.Looser.Count - looser);
                    cx.Looser.Add("not run:" + joint.TrimEnd('.', ' ').TrimStart(',', '.') + " " + tail);
                    continue;
                }
                if (joint.Contains("if you do"))
                    cx.Looser.Add("\"and if you do\" read as then (runs even if the first part did nothing)");
                first += " then " + next;
            }
            return first;
        }

        private static string? ActionPart(string text, Context cx, out List<(string Joint, string Tail)> follow)
        {
            // "(even if this card leaves the field)": a result marker stays on its target whatever happens to this card (section 92)
            string s = Regex.Replace(text.Trim().TrimEnd('.'), " \\(even if this card leaves the field\\)", "", Opt);
            follow = new();
            foreach (string tail in ChainTails)
            {
                int at = TopLevelIndex(s, tail);
                if (at > 0)
                {
                    follow.Insert(0, (tail, s.Substring(at + tail.Length)));
                    s = s.Substring(0, at);
                }
            }
            // Follow-ups the game will not run: dropped and noted.
            foreach (string tail in new[] { ", but ", ", and " })
            {
                int at = TopLevelIndex(s, tail);
                if (at > 0)
                {
                    cx.Looser.Add("not run:" + s.Substring(at + 1));
                    s = s.Substring(0, at);
                }
            }
            // ", except this card" at the end, or closing a target clause ("target 1 X in your GY, except this card; Special Summon it"): the
            // list never offers a card with this card's name (EffectScript's default), which is what it says for the usual one-copy case
            if (Regex.IsMatch(s, ", except this card(?:$|(?=; ))", Opt))
            {
                cx.Looser.Add("except this card");
                s = Regex.Replace(s, ", except this card(?:$|(?=; ))", "", Opt);
            }
            // the battle position a Special Summon names (the summon machines let the game / player choose): noted
            if (Regex.Match(s, " in (?<pos>(?:face-up |face-down )?(?:Defense|Attack) Position)(?=$|, except this card$)", Opt) is { Success: true } position)
            {
                cx.Looser.Add("in " + position.Groups["pos"].Value);
                s = s.Remove(position.Index, position.Length);
            }
            // ", with a different name from ...": the list cannot test names against other zones (noted)
            if (Regex.IsMatch(s, ", with (?:a )?different names?\\b[^;]*$", Opt))
            {
                cx.Looser.Add("different-name rule not checked");
                s = Regex.Replace(s, ", with (?:a )?different names?\\b[^;]*$", "", Opt);
            }
            if (Regex.IsMatch(s, ", except this card$", Opt))
            {
                cx.Looser.Add("except this card");
                s = Regex.Replace(s, ", except this card$", "", Opt);
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
            // "add 1 X from your Deck or GY to your hand", "add up to 2 X from your Deck", "add 1 X from your GY or banishment", "add 1 of your X
            // that is banished or in your Deck": one own list over every zone named (EffectSystem.md section 98); "up to N" = picked one by one
            if ((m = Rx("add (?<up>up to )?(?<n>\\d+) (?:of your )?(?<sel>.+?) from your (?<zone>(?:Deck|GY|banishment)(?:(?:,? or |,? and/or )(?:your )?(?:Deck|GY|banishment))*) to your hand").Match(s)).Success
                || (m = Rx("add (?<up>up to )?(?<n>\\d+) of your (?<sel>.+?) that (?:is|are) (?<zone>banished or in your (?:Deck|GY)|in your (?:Deck|GY) or banished|in your Deck or GY) to your hand").Match(s)).Success)
            {
                int n = int.Parse(m.Groups["n"].Value);
                if (n > 1 && !m.Groups["up"].Success) return null;   // exactly N: the list is picked one by one, "up to"
                var zones = Regex.Matches(m.Groups["zone"].Value, "Deck|GY|banish", Opt)
                    .Select(z => z.Value.ToLowerInvariant() switch { "deck" => "deck", "gy" => "grave", _ => "banished" }).Distinct().ToList();
                if (!Choices(m.Groups["sel"].Value, cx, out var sel)) return null;
                string count = n > 1 ? $", {n}" : "";
                if (zones is ["grave"] or ["banished"])
                    return $"add_to_hand({zones[0]}{count}{sel})";
                return $"search({string.Join(" or ", zones)}{count}{sel})";
            }
            // "Fusion Summon 1 "X" Fusion Monster from your Extra Deck, using monsters from your hand or field as material": Polymerization's
            // machine with its Fusion list filtered (EffectSystem.md section 64); other material zones are noted (Polymerization's are used)
            // "... by banishing (the) Fusion Materials listed / mentioned on it (its materials) from your field and/or GY": Dragon's Mirror's route
            if ((m = Rx("Fusion Summon 1 (?<sel>.*?)Fusion Monster(?: from your Extra Deck)?,? by banishing (?:the )?(?:(?:Fusion )?[Mm]aterials (?:listed|mentioned) on it|its materials) from (?<from>your field(?: and/or|,? or) (?:your )?GY|your GY|your field|your field or GY|your field and/or GY)(?<inc>, including (?:a|an|1|this card)[^;]*)?").Match(s)).Success)
            {
                string from = m.Groups["from"].Value;
                if (!Regex.IsMatch(from, "field.*GY", Opt))
                    cx.Looser.Add($"materials from your field or GY (text: {from})");
                if (m.Groups["inc"].Success)
                    cx.Looser.Add($"material requirement not checked ({m.Groups["inc"].Value.TrimStart(',', ' ')})");
                string banishSel = m.Groups["sel"].Value.Trim();
                if (banishSel.Length == 0)
                    return "fusion_summon(banish_materials)";
                return Selector(banishSel + " monster", cx, out var bsel, monsterOnly: true) ? $"fusion_summon({bsel.TrimStart(',', ' ')}, banish_materials)" : null;
            }
            if ((m = Rx("Fusion Summon 1 (?<sel>.*?)Fusion Monster(?: from your Extra Deck)?(?:,? (?:by )?using (?<mat>.+?)(?: as (?:Fusion )?[Mm]aterials?)?)?(?<inc>, including (?:a|an|1|this card)[^;]*)?").Match(s)).Success)
            {
                string mat = m.Groups["mat"].Value;
                if (m.Groups["inc"].Success)
                    cx.Looser.Add($"material requirement not checked ({m.Groups["inc"].Value.TrimStart(',', ' ')})");
                if (mat.Length > 0 && !Regex.IsMatch(mat, "^(?:monsters|cards) from your hand (?:or|and/or) field$", Opt))
                    cx.Looser.Add($"materials from your hand or field (text: {mat})");
                string fusionSel = m.Groups["sel"].Value.Trim();
                if (fusionSel.Length == 0)
                    return "fusion_summon()";
                return Selector(fusionSel + " monster", cx, out var fsel, monsterOnly: true) ? $"fusion_summon({fsel.TrimStart(',', ' ')})" : null;
            }
            // "Set 1 "X" Spell/Trap directly from your Deck" (the game's set-from-Deck machine, EffectSystem.md section 63)
            if ((m = Rx("Set 1 (?<sel>.+?) (?:directly )?from your Deck(?: to your (?:Spell & Trap|Field) Zone)?").Match(s)).Success)
                return Selector(Regex.Replace(m.Groups["sel"].Value, "Spell/Trap(?: Card)?", "card", Opt), cx, out var sel) ? $"set(deck{sel})" : null;
            if ((m = Rx("send 1 (?<sel>.+?) from your (?<zone>Deck|hand) to the GY").Match(s)).Success)
                return Choices(m.Groups["sel"].Value, cx, out var sel) ? $"send_to_grave({m.Groups["zone"].Value.ToLowerInvariant()}{sel})" : null;
            if ((m = Rx("banish 1 (?<sel>.+?) from your (?<zone>Deck|hand)").Match(s)).Success)
                return Choices(m.Groups["sel"].Value, cx, out var sel) ? $"banish({m.Groups["zone"].Value.ToLowerInvariant()}{sel})" : null;
            if ((m = Rx("Special Summon 1 (?<sel>.+?) from your (?<zone>(?:hand|Deck|GY|Extra Deck)(?:(?:, or |, | or )(?:hand|Deck|GY|Extra Deck))*)").Match(s)).Success)
            {
                // "from your hand, Deck, or GY" -> special_summon(hand or deck or grave, ...): one list over every zone named
                var zones = Regex.Split(m.Groups["zone"].Value, ", or |, | or ")
                    .Select(z => z.ToLowerInvariant() switch { "hand" => "hand", "deck" => "deck", "extra deck" => "extra", _ => "grave" }).Distinct().ToList();
                if (!Choices(m.Groups["sel"].Value, cx, out var sel, monsterOnly: true)) return null;
                return $"special_summon({string.Join(" or ", zones)}{sel})";   // picked when it resolves, as the text says (no target)
            }
            // (not "... that is banished or in your GY": the banished-or-GY reading below)
            if ((m = Rx("target 1 (?<sel>.+?) in (?<gy>your GY|your opponent's GY|either GY|any GY); Special Summon (?:it|that target)(?: to your field)?").Match(s)).Success
                && !Regex.IsMatch(m.Groups["sel"].Value, "\\bbanished\\b", Opt))
            {
                string gy = m.Groups["gy"].Value.ToLowerInvariant() switch { "your gy" => "grave", "your opponent's gy" => "opponent_grave", _ => "either_grave" };
                return Choices(m.Groups["sel"].Value, cx, out var sel, monsterOnly: true) ? $"revive({gy}{sel})" : null;
            }
            if ((m = Rx("(?:target 1 (?<sel>.+?) in your GY; add (?:it|that target) to your hand|add 1 (?<sel>.+?) from your GY to your hand)").Match(s)).Success
                && !Regex.IsMatch(m.Groups["sel"].Value, "\\bbanished\\b", Opt))
                return Choices(m.Groups["sel"].Value, cx, out var sel) ? $"add_to_hand(grave{sel})" : null;
            if ((m = Rx("target 1 of your banished (?<sel>.+?); add (?:it|that target) to your hand").Match(s)).Success)
                return Choices(m.Groups["sel"].Value, cx, out var sel) ? $"add_to_hand(banished{sel})" : null;
            // banished (or banished-or-GY) monsters to Special Summon: the plugin's own summon list over those zones (EffectSystem.md section 57);
            // the card is picked when the effect resolves, not targeted when it is activated
            if ((m = Rx("target 1 of your (?<b>banished )?(?<sel>.+?)(?<both> that is banished or in your GY| that is in your GY or banished)?; Special Summon (?:it|that target)(?: to your field)?").Match(s)).Success
                && (m.Groups["b"].Success || m.Groups["both"].Success))
            {
                if (!Choices(Singular(m.Groups["sel"].Value), cx, out var sel, monsterOnly: true)) return null;
                cx.Looser.Add("picked when it resolves, not targeted");
                return $"special_summon({(m.Groups["both"].Success ? "grave or banished" : "banished")}{sel})";
            }
            if ((m = Rx("target 1 (?<sel>.+?)(?<where>" + Where + "); return (?:it|that target) to the (?:owner's )?hand").Match(s)).Success)
            {
                string side = m.Groups["where"].Value.Trim() switch { "your opponent controls" or "your opponent has" => "opponent", "you control" => "own", _ => "any" };
                return Selector(m.Groups["sel"].Value, cx, out var sel, singleCondition: true) ? $"return_to_hand(target, {side}{sel})" : null;
            }
            if ((m = Rx("target 1 (?<sel>.+?)(?<where>" + Where + "); (?<verb>destroy|banish) (?:it|that target)").Match(s)).Success)
                return Removal(m.Groups["verb"].Value.ToLowerInvariant(), "target", m.Groups["sel"].Value, m.Groups["where"].Value, cx);
            if ((m = Rx("(?<verb>destroy|banish) all (?<sel>.+?)(?<where> on the field| your opponent controls| you control)").Match(s)).Success)
                return Removal(m.Groups["verb"].Value.ToLowerInvariant(), "all", m.Groups["sel"].Value, m.Groups["where"].Value, cx);
            if (Move(s, cx) is { } move)
                return move;
            if (TargetVerb(s, cx) is { } targetVerb)
                return targetVerb;
            return Why("action");
        }

        // "(Once per turn, )you can also Xyz Summon this card by using 1 / a <Rank N (or lower | higher | or M)> <X> (Xyz) Monster you control
        // (as material)( (transfer its materials( to this card)))" -> xyz_also(selector[, xyz_monster][, rank ...]) (docs/EffectSystem.md section 94).
        // null = not one (a cost such as "by discarding 1 Spell and using ...", or a condition, is not read).
        private static string? XyzAlso(string sentence, Context cx, List<string> looser)
        {
            var m = Regex.Match(sentence.Trim().TrimEnd('.'), "^(?<once>Once per turn, )?(?:you|You) can also Xyz Summon this card by using (?:1 |a |an )?(?<what>.+?) you control(?: as (?:its )?(?:Xyz )?[Mm]aterial)?(?: \\((?:transfer|Transfer) its materials(?: to this card)?\\.?\\))?$", Opt);
            if (!m.Success)
                return null;
            string what = m.Groups["what"].Value.Trim();
            var args = new List<string>();
            var rank = Regex.Match(what, "^Rank (?<a>\\d+)(?: or (?:(?<lower>lower)|(?<higher>higher)|(?<b>\\d+)))? ", Opt);
            if (rank.Success)
            {
                int a = int.Parse(rank.Groups["a"].Value);
                if (rank.Groups["lower"].Success) args.Add($"rank_at_most {a}");
                else if (rank.Groups["higher"].Success) args.Add($"rank_at_least {a}");
                else if (rank.Groups["b"].Success)
                {
                    int b = int.Parse(rank.Groups["b"].Value);
                    if (b != a + 1) return null;   // "Rank 5 or 7": not a range
                    args.Add($"rank_at_least {a}");
                    args.Add($"rank_at_most {b}");
                }
                else args.Add($"rank {a}");
                what = what.Substring(rank.Length);
            }
            if (Regex.IsMatch(what, "\\bXyz Monsters?$", Opt))
            {
                args.Insert(0, "xyz_monster");
                what = Regex.Replace(what, "\\bXyz (Monsters?)$", "$1", Opt);
            }
            // a quoted name alone is that card ("by using "Number 62: ..." you control"); with a kind word it is an archetype
            if (!Selector(what, cx, out var selector))
                return null;
            if (m.Groups["once"].Success)
                looser.Add("once per turn not counted for the alternative Xyz Summon");
            looser.AddRange(cx.Looser);
            return $"xyz_also({selector.TrimStart(',', ' ')}{string.Concat(args.Select(x => ", " + x))})";
        }

        // "target 1 X (you control / your opponent controls / on the field); change it to (face-up) Defense / Attack Position | change its battle
        // position | change it to face-down Defense Position | take control of it until the End Phase" -> change_position / set_face_down /
        // take_control (the game's own target machines, docs/EffectSystem.md section 92). null = not one.
        private static string? TargetVerb(string s, Context cx)
        {
            var m = Rx("target 1 (?<sel>.+?)(?<where>" + Where + "); (?<verb>change (?:it|that target|that monster) to [^;]+|change (?:its|that target's|that monster's) battle position|take control of (?:it|that target|that monster)(?<until> until the (?:End Phase|end of (?:this|the) turn))?"
                + "|(?:it|that target|that monster) (?<atk>gains|loses) (?<n>\\d+) ATK(?<def>/DEF| and DEF)?(?<dur> until the end of (?:this|your opponent's|the next) turn)?"
                + "|negate (?:its|that target's|that monster's) effects(?<ndur> until the end of this turn| \\(until the end of this turn\\))?)").Match(s);
            if (!m.Success || Regex.IsMatch(m.Groups["sel"].Value, "\\b(?:GY|banished|hand|Deck)\\b", Opt))
                return null;
            string sel = m.Groups["sel"].Value, verb = m.Groups["verb"].Value;
            string? position = null;
            if (Regex.Match(sel, "^(?:face-up )?(?<p>Attack|Defense) Position ", Opt) is { Success: true } named)
            {
                position = named.Groups["p"].Value.ToLowerInvariant();
                sel = sel.Substring(named.Length);
            }
            string fn;
            string amount = "";
            if (m.Groups["atk"].Success)
            {
                // the game's ATK result marker, until the end of this turn (section 92)
                fn = "change_atk";
                amount = (m.Groups["atk"].Value.Equals("loses", StringComparison.OrdinalIgnoreCase) ? ", -" : ", ") + m.Groups["n"].Value;
                if (m.Groups["def"].Success)
                    cx.Looser.Add("DEF not changed");
                if (!m.Groups["dur"].Success)
                    cx.Looser.Add("lasts until the end of this turn");
                else if (!m.Groups["dur"].Value.Contains("this turn"))
                    cx.Looser.Add("lasts until the end of this turn");
            }
            else if (verb.StartsWith("negate", StringComparison.OrdinalIgnoreCase))
            {
                fn = "negate_effects";
                if (!m.Groups["ndur"].Success)
                    cx.Looser.Add("negated until the end of this turn");
            }
            else if (Regex.IsMatch(verb, "^change \\w+(?: \\w+)? to face-down Defense Position$", Opt))
                fn = "set_face_down";
            else if (Regex.Match(verb, "^change \\w+(?: \\w+)? to (?:face-up )?(?<to>Attack|Defense) Position$", Opt) is { Success: true } to)
            {
                // the machine changes the battle position, so the target is one in the other position
                string from = to.Groups["to"].Value.Equals("Attack", StringComparison.OrdinalIgnoreCase) ? "defense" : "attack";
                if (position != null && position != from)
                    return null;
                fn = "change_position";
                position = from;
            }
            else if (Regex.IsMatch(verb, "battle position$", Opt))
                fn = "change_position";
            else if (verb.StartsWith("take control", StringComparison.OrdinalIgnoreCase) && m.Groups["until"].Success)
            {
                fn = "take_control";
                if (!m.Groups["until"].Value.Contains("End Phase"))
                    cx.Looser.Add("control returns in the End Phase");
                if (position != null)
                    cx.Looser.Add($"{position} position not checked");
                position = null;
            }
            else
                return null;
            if (Regex.IsMatch(sel, "^face-up ", Opt) && fn == "take_control")
                cx.Looser.Add("face-up not checked");
            if (!Selector(sel, cx, out var selector, monsterOnly: true, singleCondition: true))
                return null;
            string side = m.Groups["where"].Value.Trim() switch { "your opponent controls" or "your opponent has" => "opponent", "you control" => "own", _ => "any" };
            return $"{fn}(target, {side}{selector}{(position != null ? ", " + position : "")}{amount})";
        }

        // ---- moves (EffectScript move(...), Yu-Gi-Oh-Effects' own move step; docs/EffectSystem.md section 86) ------------------------------
        private const string ItRx = "(?:it|that target|that card|that monster|them|those targets|those cards)";

        // A move's destination words -> the move(...) destination; null = not a move destination.
        private static string? MoveDest(string verbPhrase) => verbPhrase.ToLowerInvariant() switch
        {
            var v when Regex.IsMatch(v, "^shuffle .* into (?:the|your|their|its owner's) (?:main )?deck$") => "to_deck",
            var v when Regex.IsMatch(v, "^(?:place|put|return) .* on (?:the )?top of (?:the|your|their|its owner's) deck$") => "to_deck_top",
            var v when Regex.IsMatch(v, "^(?:place|put|return) .* on (?:the )?bottom of (?:the|your|their|its owner's) deck$") => "to_deck_bottom",
            var v when Regex.IsMatch(v, "^return .* to the extra deck$") => "to_extra",
            var v when Regex.IsMatch(v, "^(?:return .* to|add .* to) (?:the|its owner's|their owner's|your) hand$") => "to_hand",
            var v when Regex.IsMatch(v, "^send .* to the gy$") => "to_grave",
            var v when Regex.IsMatch(v, "^banish .*$") => "to_banished",
            var v when Regex.IsMatch(v, "^destroy (?:them|those targets|those cards|both)$") => "destroy",
            _ => null,
        };

        private static string? Move(string s, Context cx)
        {
            System.Text.RegularExpressions.Match m;
            // this card: "shuffle this card into the Deck", "return this card to the hand", "place this card on the bottom of the Deck" ...
            if ((m = Rx("(?<verb>(?:shuffle|return|place|put|send|banish|add) this card\\b.*)").Match(s)).Success && MoveDest(m.Groups["verb"].Value) is { } selfDest
                && !Regex.IsMatch(Regex.Replace(m.Groups["verb"].Value, " from (?:the|your) field", "", Opt), "\\bfrom\\b", Opt))
                return $"move(self, {selfDest})";
            // a target on the field: "target 1 X (you control / your opponent controls / on the field); shuffle it into the Deck"
            if ((m = Rx("target 1 (?<sel>.+?)(?<where>" + Where + "); (?<verb>(?:shuffle|place|put|return|send) " + ItRx + "\\b.*)").Match(s)).Success
                && !Regex.IsMatch(m.Groups["sel"].Value, "\\b(?:GY|banished|hand|Deck)\\b", Opt) && MoveDest(m.Groups["verb"].Value) is { } targetDest)
            {
                string side = m.Groups["where"].Value.Trim() switch { "your opponent controls" or "your opponent has" => "opponent", "you control" => "own", _ => "any" };
                return Selector(m.Groups["sel"].Value, cx, out var sel, singleCondition: true) ? $"move(target, {side}{sel}, {targetDest})" : null;
            }
            // up to N targets on the field: "target up to 2 cards your opponent controls; return them to the hand" (the game's multi-target picker)
            if ((m = Rx("target (?<up>up to )?(?<n>[2-5]|two|three|four|five) (?<sel>.+?)(?<where>" + Where + "); (?<verb>(?:shuffle|place|put|return|send|destroy|banish) (?:them|those targets|those cards|both)\\b.*)").Match(s)).Success
                && !Regex.IsMatch(m.Groups["sel"].Value, "\\b(?:GY|banished|hand|Deck|and|including)\\b", Opt) && MoveDest(m.Groups["verb"].Value) is { } multiDest)
            {
                string n = m.Groups["n"].Value.ToLowerInvariant() switch { "two" => "2", "three" => "3", "four" => "4", "five" => "5", var d => d };
                string side = m.Groups["where"].Value.Trim() switch { "your opponent controls" or "your opponent has" => "opponent", "you control" => "own", _ => "any" };
                if (!m.Groups["up"].Success)
                    cx.Looser.Add($"up to {n} targets (exactly {n} not required)");
                return Selector(Singular(m.Groups["sel"].Value), cx, out var sel, singleCondition: true) ? $"move(target, {n}, {side}{sel}, {multiDest})" : null;
            }
            // a card in the GY / hand to the Deck or Extra Deck: picked from the list when it resolves
            // "target 1 X in your GY; shuffle it into the Deck" / "shuffle 1 X from your GY into the Deck" / "place 1 X from your hand or GY on
            // the bottom of the Deck" / "return 1 X from your GY to the Extra Deck"
            string? zones = null, what = null, dest = null;
            bool targeted = false;
            if ((m = Rx("target 1 (?:of your )?(?<sel>.+?) in your (?<zones>GY|hand); (?<verb>(?:shuffle|place|put|return) " + ItRx + "\\b.*)").Match(s)).Success)
            {
                (zones, what, dest, targeted) = (m.Groups["zones"].Value, m.Groups["sel"].Value, MoveDest(m.Groups["verb"].Value), true);
            }
            else if ((m = Rx("(?<verb>shuffle|place|put|return) 1 (?<sel>.+?) from your (?<zones>GY|hand|hand or GY|GY or hand|hand and/or GY) (?<rest>into (?:the|your) Deck|on (?:the )?(?:top|bottom) of (?:the|your) Deck|to the Extra Deck)").Match(s)).Success)
                (zones, what, dest) = (m.Groups["zones"].Value, m.Groups["sel"].Value, MoveDest(m.Groups["verb"].Value + " x " + m.Groups["rest"].Value));
            // N cards: "target (up to) N X in your GY; shuffle them into the Deck / place them on the bottom of the Deck" -> move(grave, N, ...),
            // "target (up to) N of your banished X / N X in your GY; add them to your hand" -> add_to_hand(..., N, ...): picked one after
            // another when it resolves (section 95)
            if ((m = Rx("target (?<up>up to )?(?<n>[2-5]|two|three|four|five) (?:of your (?<b>banished )?(?<sel>.+?)|(?<sel>.+?) in your GY)(?<both> that are banished and/or in your GY| that are in your GY and/or banished)?; (?<verb>(?:shuffle|place|put|return|add) (?:them|those cards|those monsters|those targets)\\b.*)").Match(s)).Success
                && !m.Groups["both"].Success && !Regex.IsMatch(m.Groups["sel"].Value, "\\b(?:with different names|including|and)\\b", Opt))
            {
                string n = m.Groups["n"].Value.ToLowerInvariant() switch { "two" => "2", "three" => "3", "four" => "4", "five" => "5", var d => d };
                bool banished = m.Groups["b"].Success;
                string verbPhrase = m.Groups["verb"].Value;
                string? pileDest = MoveDest(verbPhrase);
                if (!m.Groups["up"].Success)
                    cx.Looser.Add($"up to {n} (exactly {n} not required)");
                if (!Choices(Singular(m.Groups["sel"].Value), cx, out var msel))
                    return null;
                cx.Looser.Add("picked when it resolves, not targeted");
                if (pileDest == "to_hand")
                    return $"add_to_hand({(banished ? "banished" : "grave")}, {n}{msel})";
                if (!banished && pileDest is "to_deck" or "to_deck_top" or "to_deck_bottom" or "to_extra")
                    return $"move(grave, {n}{msel}, {pileDest})";
                return null;
            }
            if (zones != null && dest is "to_deck" or "to_deck_top" or "to_deck_bottom" or "to_extra")
            {
                var from = Regex.Split(zones, " or | and/or ").Select(z => z.Equals("GY", StringComparison.OrdinalIgnoreCase) ? "grave" : "hand").Distinct().ToList();
                if (dest == "to_extra" && from.Contains("hand"))
                    return null;
                if (!Choices(Singular(what!), cx, out var sel))
                    return null;
                if (targeted)
                    cx.Looser.Add("picked when it resolves, not targeted");
                return $"move({string.Join(" or ", from)}{sel}, {dest})";
            }
            return null;
        }

        // A continuous rule (docs/EffectSystem.md section 89): "While <condition>, <rule>" -> "while <tests>: <rule>", and a rule of several parts
        // ("Your opponent cannot target this card with card effects, also it cannot be destroyed by your opponent's card effects") -> one statement
        // per part (each under the same while). null = not one.
        private static string? ContinuousRule(string sentence, Context cx)
        {
            string s = sentence.Trim().TrimEnd('.');
            string when = "", body = s;
            // "While you control a monster, except this card, ..." = "While you control another monster, ..."
            s = Regex.Replace(s, "^While you control (?:a|an) (?<what>[^,:]+), except this card, ", "While you control another ${what}, ", Opt);
            var w = Regex.Match(s, "^While (?<cond>[^,:]+), (?<body>[^:]+)$", Opt);
            if (w.Success)
            {
                if (WhileCondition(w.Groups["cond"].Value, cx) is not { } cond)
                    return null;
                when = cond.Length == 0 ? "" : $"while {cond}: ";
                body = w.Groups["body"].Value;
            }
            var statements = new List<string>();
            foreach (string part in Regex.Split(body, ",? also,? |, and (?=(?:it|this card|your opponent|you|monsters|neither)\\b)", Opt))
            {
                string p = part.Trim();
                p = Regex.Replace(p, "^it\\b", "This card", Opt);
                if (p.Length == 0)
                    return null;
                p = char.ToUpperInvariant(p[0]) + p[1..];
                string? statement = Aura(p, cx);
                // a while clause sits only before aura / indestructible / untargetable / unaffected / attack_directly
                if (statement == null || (when.Length > 0 && (statement.StartsWith("name_as") || statement.StartsWith("only_one"))))
                    return null;
                statements.Add(when + statement);
            }
            return statements.Count == 0 ? null : string.Join("; ", statements);
        }

        // "you control a "Blue-Eyes" monster and your opponent controls no monsters" -> "controls(...) and not controls(opponent, monster)"; ""
        // when it only says what the rule already needs ("this card is face-up on the field"); null = a condition the language has no test for.
        private static string? WhileCondition(string condition, Context cx)
        {
            var tests = new List<string>();
            foreach (string raw in Regex.Split(condition.Trim(), " and (?=(?:you|your|this card|a |an |there|\"))", Opt))
            {
                string c = raw.Trim();
                System.Text.RegularExpressions.Match m;
                string? test;
                if (Regex.IsMatch(c, "^(?:this card is )?(?:face-up on the field|on the field|in the Monster Zone|face-up in the Monster Zone|face-up in the Spell & Trap Zone|in your Monster Zone)$", Opt))
                    continue;
                if (Regex.IsMatch(c, "^this card has (?:Xyz )?(?:material|Materials?)(?: attached)?$", Opt))
                    test = "has_material";
                else if (Regex.IsMatch(c, "^this card is in (?:face-up )?Defense Position$", Opt))
                    test = "defense_position";
                else if ((m = Regex.Match(c, "^your LP are (\\d+) or less$", Opt)).Success)
                    test = $"lp_at_most({m.Groups[1].Value})";
                else if ((m = Regex.Match(c, "^you have (?<what>.+) in your (?<pile>GY|hand)$", Opt)).Success)
                    test = CountOf($"{m.Groups["what"].Value} in your {m.Groups["pile"].Value}", cx);
                else if ((m = Regex.Match(c, "^(?<what>.+) (?:is|are) in your GY$", Opt)).Success)
                    test = CountOf($"{m.Groups["what"].Value} in your GY", cx);
                else if ((m = Regex.Match(c, "^(?<what>.+) (?:is|are) on the field$", Opt)).Success)
                    test = CountOf($"{m.Groups["what"].Value} on the field", cx);
                else if ((m = Regex.Match(c, "^(?<n>\\d+) or more of your cards are banished$", Opt)).Success)
                    test = $"banished({m.Groups["n"].Value}, card)";
                else if ((m = Regex.Match(c, "^(?<who>you control|your opponent controls) (?<what>.+)$", Opt)).Success)
                    test = CountOf($"{m.Groups["what"].Value} {m.Groups["who"].Value}", cx);
                else
                    return null;
                if (test == null)
                    return null;
                tests.Add(test);
            }
            return string.Join(" and ", tests);
        }

        // "<how many> <cards> you control / your opponent controls / on the field / in your GY / in your hand / in your opponent's ..." ->
        // "controls(...)" / "in_grave(...)" / "in_hand(...)" (EffectScript count test, section 89). "no X" -> "not ...". null = not expressible.
        private static string? CountOf(string phrase, Context cx, bool forEach = false)
        {
            // "... you control, except this card" = other ... you control
            var except = Regex.Match(phrase.Trim(), "^(?<rest>.+?),? except this card$", Opt);
            if (except.Success)
                phrase = "other " + Regex.Replace(except.Groups["rest"].Value, "^(?:a|an|1) ", "", Opt);
            var m = Regex.Match(phrase.Trim(),"^(?<sel>.+?) (?<where>you control|your opponent controls|on the field|in your GY|in your opponent's GY|in either player's GY|in your hand|in your opponent's hand)$", Opt);
            if (!m.Success)
                return null;
            string where = m.Groups["where"].Value.ToLowerInvariant();
            string fn = where.Contains("gy") ? "in_grave" : where.Contains("hand") ? "in_hand" : "controls";
            string? side = where.Contains("opponent") ? "opponent" : where.Contains("either") || where == "on the field" ? "any" : null;
            string sel = m.Groups["sel"].Value.Trim();
            bool none = false, other = false;
            int n = 1;
            System.Text.RegularExpressions.Match q;
            if ((q = Regex.Match(sel, "^no ", Opt)).Success) { none = true; sel = sel[q.Length..]; }
            else if ((q = Regex.Match(sel, "^(\\d+) or more ", Opt)).Success) { n = int.Parse(q.Groups[1].Value); sel = sel[q.Length..]; }
            else if ((q = Regex.Match(sel, "^(?:a|an|1) ", Opt)).Success) sel = sel[q.Length..];
            if ((q = Regex.Match(sel, "^(?:another|other) ", Opt)).Success) { other = true; sel = sel[q.Length..]; }
            if (forEach && (none || n != 1))
                return null;
            string selector;
            if (Regex.IsMatch(sel, "^cards?$", Opt))
                selector = "card";
            else
            {
                int looser = cx.Looser.Count;
                if (!Selector(sel, cx, out var parsed) || cx.Looser.Count != looser || parsed.Contains("level") || parsed.Contains("atk"))
                    return null;   // a count tests kind / race / attribute / archetype / name only, exactly
                selector = parsed.TrimStart(',', ' ');
                if (selector.Length == 0)
                    selector = "card";
            }
            var args = new List<string>();
            if (n != 1) args.Add(n.ToString());
            if (side != null) args.Add(side);
            if (other) args.Add("other");
            args.Add(selector);
            return (none ? "not " : "") + $"{fn}({string.Join(", ", args)})";
        }

        // "(This card) gains N ATK (/DEF) for each <count>" -> aura(atk N, self, per <count>) (section 89)
        private static string? ForEachAura(string s, Context cx)
        {
            var m = Regex.Match(s, "^(?:This card |It )?(?<verb>gains|loses) (?<n>\\d+) (?<stat>ATK/DEF|ATK and DEF|ATK|DEF) for each (?<what>.+)$", Opt);
            if (!m.Success)
                return null;
            string what = m.Groups["what"].Value.Trim();
            string? per = Regex.IsMatch(what, "^(?:Xyz )?[Mm]aterial attached to (?:it|this card)$", Opt) ? "materials" : CountOf(what, cx, forEach: true);
            if (per == null)
                return null;
            string n = (m.Groups["verb"].Value.StartsWith("lose", StringComparison.OrdinalIgnoreCase) ? "-" : "") + m.Groups["n"].Value;
            string stat = m.Groups["stat"].Value;
            string stats = stat == "DEF" ? $"def {n}" : stat == "ATK" ? $"atk {n}" : $"atk {n}, def {n}";
            return $"aura({stats}, self, per {per})";
        }

        // A continuous ATK / DEF change while this card is face-up (EffectScript aura(...), docs/EffectSystem.md section 69): "Monsters your opponent
        // controls lose 500 ATK", "Dragon monsters you control gain 300 ATK/DEF", "All monsters on the field gain 200 ATK", "(This card) gains
        // 500 ATK". null = not one (a "for each", a condition, another stat ...).
        private static string? Aura(string sentence, Context cx)
        {
            if (Protect(sentence, cx) is { } protect)
                return protect;
            string s = sentence.Trim().TrimEnd('.');
            if (ForEachAura(s, cx) is { } forEach)
                return forEach;
            var m = Regex.Match(s, "^(?:This card |It )?(?<verb>gains|loses) (?<n>\\d+) (?<stat>ATK/DEF|ATK and DEF|ATK|DEF)$", Opt);
            string side = "self", sel = "";
            if (!m.Success)
            {
                m = Regex.Match(s, "^(?:All )?(?<sel>.*?)(?:monsters|monster) (?<side>you control|your opponent controls|on the field) (?<verb>gain|lose) (?<n>\\d+) (?<stat>ATK/DEF|ATK and DEF|ATK|DEF)$", Opt);
                if (!m.Success)
                    return null;
                side = m.Groups["side"].Value switch { "you control" => "own", "your opponent controls" => "opponent", _ => "any" };
                sel = m.Groups["sel"].Value.Trim();
                if (sel.Length > 0)
                {
                    if (!Selector(sel + " monster", cx, out var parsed, monsterOnly: true) || parsed.Contains("level") || parsed.Contains("atk"))
                        return null;   // the aura filter tests Type / Attribute / archetype only
                    sel = ", " + parsed.TrimStart(',', ' ');
                }
            }
            string n = (m.Groups["verb"].Value.StartsWith("lose", StringComparison.OrdinalIgnoreCase) ? "-" : "") + m.Groups["n"].Value;
            string stat = m.Groups["stat"].Value;
            string stats = stat == "DEF" ? $"def {n}" : stat == "ATK" ? $"atk {n}" : $"atk {n}, def {n}";
            return $"aura({stats}, {side}{sel})";
        }

        // "Cannot be destroyed" while this card is face-up (EffectScript indestructible(...), docs/EffectSystem.md section 71): "This card cannot be
        // destroyed by battle", "Cannot be destroyed by battle or card effects", "... by your opponent's card effects", "Warrior monsters you control
        // cannot be destroyed by battle". null = not one (a condition, "other" monsters, "once per turn" ...).
        private static string? Protect(string sentence, Context cx)
        {
            string s = sentence.Trim().TrimEnd('.');
            // attack_directly (section 76)
            if (Regex.IsMatch(s, "^(?:This card|It) can attack (?:your opponent )?directly$", Opt))
                return "attack_directly";
            // name_as(...) (section 73): "This card's name becomes "X" while on the field (or in the GY)"; field-only forms use the field table,
            // every other place (GY, hand, Deck) the game's own id path - which, like its Harpie Queen, also counts everywhere a card is looked up
            var n = Regex.Match(s, "^This card's name becomes \"(?<name>[^\"]+)\" while (?<where>on the field|face-up on the field|in the Field Zone|on the field or in the GY|in the GY|in the hand or on the field|in the hand, Deck, GY, or on the field)$", Opt);
            if (n.Success)
            {
                int id = cx.CardIdByName(n.Groups["name"].Value);
                if (id == 0)
                    return null;
                string where = n.Groups["where"].Value;
                return where is "on the field" or "face-up on the field" or "in the Field Zone" ? $"name_as({id}, field)" : $"name_as({id})";
            }
            // the same, said by the player: "Your opponent cannot target this card with card effects" / "... for attacks", "Your opponent's
            // monsters cannot target this card for attacks", "Neither player can target this card with card effects"
            if (Regex.IsMatch(s, "^Your opponent cannot target (?:this card|it) with card effects$", Opt))
                return "untargetable(opponent_effects)";
            if (Regex.IsMatch(s, "^Neither player can target (?:this card|it) with card effects$", Opt))
                return "untargetable(effects)";
            if (Regex.IsMatch(s, "^Your opponent(?:'s monsters)? cannot target (?:this card|it) for attacks$", Opt))
                return "untargetable(attacks)";
            // untargetable(...) (section 72): "This card cannot be targeted by (your opponent's) card effects", "... for attacks"
            var t = Regex.Match(s, "^(?:This card |It )?[Cc]annot be targeted (?<what>by card effects|by your opponent's card effects|for attacks(?:, but does not prevent your opponent from attacking you directly)?|for attacks or by card effects|for attacks or by your opponent's card effects|by card effects or for attacks)$", Opt);
            if (t.Success)
            {
                string w = t.Groups["what"].Value;
                var words = new List<string>();
                if (w.Contains("attacks")) words.Add("attacks");
                if (w.Contains("opponent's")) words.Add("opponent_effects");
                else if (w.Contains("card effects")) words.Add("effects");
                return $"untargetable({string.Join(", ", words)})";
            }
            // unaffected(...) (section 72): "This card is unaffected by (your opponent's) Spell / Trap / Spell and Trap / monster effects"
            // "... unaffected by (your opponent's) card effects" = by all three kinds
            var u = Regex.Match(s, "^(?:This card is |It is )?[Uu]naffected by (?<opp>your opponent's |an opponent's )?(?<what>Spell|Trap|Spell/Trap|Spell and Trap|Spell Card|Trap Card|Spell/Trap Card|Spell and Trap Card|monster|card) effects$", Opt);
            if (u.Success)
            {
                string w = u.Groups["what"].Value.ToLowerInvariant(), prefix = u.Groups["opp"].Success ? "opponent_" : "";
                var words = new List<string>();
                if (w.Contains("spell") || w == "card") words.Add(prefix + "spells");
                if (w.Contains("trap") || w == "card") words.Add(prefix + "traps");
                if (w.Contains("monster") || w == "card") words.Add(prefix + "monster_effects");
                return $"unaffected({string.Join(", ", words)})";
            }
            var m = Regex.Match(s, "^(?:(?<self>This card|It) )?(?<sel>(?:All )?.*?(?:monsters|monster) (?<side>you control|your opponent controls|on the field) )?cannot be destroyed by (?<what>battle|card effects|your opponent's card effects|an opponent's card effects|battle or (?:by )?card effects|battle or (?:by )?your opponent's card effects|battle or (?:by )?an opponent's card effects|card effects or (?:by )?battle)$", Opt);
            if (!m.Success || (m.Groups["self"].Success && m.Groups["sel"].Success))
                return null;
            string what = m.Groups["what"].Value.ToLowerInvariant();
            var args = new List<string>();
            if (what.Contains("battle")) args.Add("battle");
            if (what.Contains("opponent's")) args.Add("opponent_effects");
            else if (what.Contains("card effects")) args.Add("effects");
            if (m.Groups["sel"].Success)
            {
                args.Add(m.Groups["side"].Value switch { "you control" => "own", "your opponent controls" => "opponent", _ => "any" });
                string sel = Regex.Replace(m.Groups["sel"].Value, "(?:monsters|monster) (?:you control|your opponent controls|on the field) $", "", Opt);
                sel = Regex.Replace(sel, "^All ", "", Opt).Trim();
                if (sel.Length > 0)
                {
                    if (Regex.IsMatch(sel, "\\b(other|face-up|face-down|Attack Position|Defense Position|except)\\b", Opt))
                        return null;   // the protect filter tests side / Type / Attribute / archetype only
                    if (!Selector(sel + " monster", cx, out var parsed, monsterOnly: true) || parsed.Contains("level") || parsed.Contains("atk"))
                        return null;
                    args.Add(parsed.TrimStart(',', ' '));
                }
            }
            return $"indestructible({string.Join(", ", args)})";
        }

        // A choice list (EffectScript 'choices': selector or selector ...): one selector, or "A or B" with a full selector on each side
        // ('Plant monster or "Rikka" card'), or a Type list sharing its words ("Level 4 or lower Beast, Beast-Warrior, or Winged Beast monsters").
        private static bool Choices(string text, Context cx, out string choices, bool monsterOnly = false)
        {
            if (Selector(text, cx, out choices, monsterOnly: monsterOnly))
                return true;
            choices = "";
            const string Keep = "\u0001";   // "or lower" / "or higher" and quoted names are not alternatives
            string t = Regex.Replace(text, " or (lower|higher)", Keep + "$1", Opt);
            t = Regex.Replace(t, "\"[^\"]*\"", q => q.Value.Replace(" or ", Keep + "OR" + Keep).Replace(", ", Keep + "C" + Keep));
            string Restore(string s) => s.Replace(Keep + "OR" + Keep, " or ").Replace(Keep + "C" + Keep, ", ").Replace(Keep, " or ");
            var parts = new List<string>();
            // a Type or Attribute list ("LIGHT or DARK monster", "Beast, Beast-Warrior, or Winged Beast monsters")
            string raceAlt = string.Join("|", Races.Select(r => Regex.Escape(r.Text)).Concat(Attributes));
            var list = Regex.Match(t, $"^(?<pre>.*?)(?<races>(?:{raceAlt})(?:, (?:{raceAlt}))*,? or (?:{raceAlt})) (?<noun>monsters?|cards?)$", Opt);
            if (list.Success)
                foreach (string race in Regex.Split(list.Groups["races"].Value, ",? or |, "))
                    parts.Add(Restore($"{list.Groups["pre"].Value}{race} {list.Groups["noun"].Value}"));
            else
                parts.AddRange(Regex.Split(t, ",? or ").Select(Restore));
            if (parts.Count < 2)
                return false;
            var each = new List<string>();
            foreach (string part in parts)
            {
                string p = Regex.Replace(part.Trim(), "^(?:1|a|an) ", "", Opt);
                if (!Selector(Singular(p), cx, out var one, monsterOnly: monsterOnly))
                    return false;
                each.Add(one.TrimStart(',', ' '));
            }
            choices = ", " + string.Join(" or ", each);
            return true;
        }

        // "1 of your "X" monsters" names its kind in the plural: the selector reads the singular
        private static string Singular(string sel) => Regex.Replace(sel, "\\b(monster|card|Spell|Trap)s\\b", "$1", Opt);

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
                    if (n.Length == 0 || int.Parse(n) > 15) return false;   // "Level" with no number ("its original Level"): not a filter
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
