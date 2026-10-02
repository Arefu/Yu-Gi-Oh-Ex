using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Antlr4.Runtime;
using WolfEx.EffectScriptGenerated;

namespace WolfEx
{
    /// <summary>
    /// Compiles EffectScript source (Yu-Gi-Oh_Loader Plugins\Yu-Gi-Oh-Effects\Script\EffectScript.g4, described in
    /// docs/EffectLanguage.md) into the "effectClone" object Yu-Gi-Oh-Effects runs: a vanilla card whose handlers are borrowed
    /// plus the parameters (draw count, filters) that make it this card's effect. The plugin never parses the DSL.
    /// Compiled.Ok means the script is valid AND the runtime can run it; a valid script the runtime cannot run yet
    /// (chaining with 'then') is reported as an error instead of silently producing something that does nothing.
    /// </summary>
    internal static class EffectScriptCompiler
    {
        public sealed class Result
        {
            public bool Ok;
            public string Error = "";
            public JsonObject? Compiled;   // the card's "effectClone" value once Ok
        }

        // Vanilla cards whose handlers each action borrows (docs/EffectSystem.md sections 10-12).
        private const int DrawSource = 4844;      // Pot of Greed
        private const int SearchSource = 5328;    // Reinforcement of the Army
        private const int ReviveSource = 4842;    // Monster Reborn
        private const int DestroySource = 4659;   // Warrior Elimination (destroy-all row driven by a filter)
        private const int SendSource = 5236;      // Foolish Burial
        private const int TargetDestroySource = 4838;   // Remove Trap (Target 1 X; destroy it - filter row driven)
        private const int GainSource = 4345;      // Red Medicine
        private const int BurnSource = 4350;      // Hinotama
        // Axe of Despair: an Equip Spell for any monster whose only other effect (Tribute 1 monster to put it back on top of the Deck) is optional.
        // Its ATK/DEF come from the game's equip table, which Yu-Gi-Oh-Effects answers with the card's own "stats" (docs/EffectSystem.md section 31).
        private const int EquipSource = 4310;
        // Special Summon from the hand: Magnet Circle LV2's hand-only machine (0x140160080), any filter via the hand "summonable" scan.
        private const int SummonHandSource = 6572;
        // Special Summon from the Deck: Unexpected Dai's targeted machine; its "if you control no monsters" is its condition slot, which the
        // runtime replaces with "the list has a match" ("condition": "listHasMatch", docs/EffectSystem.md section 33).
        private const int SummonDeckSource = 11740;
        // Add from the GY to the hand: The Warrior Returning Alive ("Target 1 X in your GY; add it to your hand"), any filter via the GY scan.
        private const int SalvageSource = 5330;
        // Costs (slot 3 of the effect row): Lightning Vortex "Discard 1 card; ..." (its condition checks the hand), Delinquent Duo "Pay 1000 LP; ...".
        private const int DiscardCostSource = 5217;
        private const int PayCostSource = 4901;
        // "Tribute 1 monster; ...": Share the Pain's cost (0x1401FDDF0); its own condition is about the opponent, so the plugin's "canTributeOne" checks instead.
        private const int TributeCostSource = 4889;
        // Banish: the game's banish machines are monster rows, so a Spell keeps a Spell row (Remove Trap / Warrior Elimination) and borrows the
        // banish slots ("actionFrom"): Legendary Knight Hermos "target 1 monster; banish it" (0x140156ED0, filter row driven) and Armoroid
        // "banish all ..." (0x140157050). Both read the clone's own filter row (docs/EffectSystem.md section 35).
        private const int BanishTargetSource = 11887;
        private const int BanishAllSource = 7057;
        // Add from the banished cards: Dragoncarnation "Target 1 of your banished Dragon monsters; add it to your hand", any filter.
        private const int SalvageBanishedSource = 10561;
        // Costs paid with the card itself (docs/EffectSystem.md section 37). Each is the ROW of a game card activated from that place (the GY / hand
        // effects table, the field ignition table) whose slot 3 is that cost; the action's slots are composed onto it ("actionFrom") and "require"
        // keeps the game's own "this card is there and can pay" check, which the action card's condition knows nothing about.
        private const int BanishSelfSource = 9409;     // Rose Lover: "You can banish this card from your GY; ..." (cost 0x1401FA670)
        private const int DiscardSelfSource = 7572;    // Hecatrice: "You can discard this card to the GY; ..." (cost 0x1401FCEB0, on the hand-effect list)
        private const int TributeSelfSource = 10232;   // Planet Pathfinder: "You can Tribute this card; ..." (cost 0x1401F8BA0)
        // Special Summon this card from the hand: Watch Cat "If you control no monsters: You can Special Summon this card from your hand" (machine
        // 0x140161E20). Its condition is replaced by the game's plain "this card can be Special Summoned" check; if no_monsters adds Watch Cat's own.
        private const int SummonSelfSource = 13582;
        // Special Summon this card from the GY: Quillbolt Hedgehog "If this card is in your GY: You can Special Summon this card" (machine 0x140169F70);
        // its "you must control a Tuner" is replaced by the plain "this card in the GY can be Special Summoned" check.
        private const int SummonSelfGraveSource = 7701;
        // "Target 1 face-up monster; it gains N ATK until the end of this turn": Rush Recklessly (any face-up monster) / Inspiration (one you
        // control). N is the card's own: Yu-Gi-Oh-Effects answers the game's boost table (StatTable_BoostB) with "stats".
        // Detach N materials from this card: Thunder End Dragon's ignition row (cost 0x1401FF4D0, its check 0x1400FBFF0; N answered by Yu-Gi-Oh-Effects).
        private const int DetachSource = 9762;
        // "Target 1 card; return it to the hand": Spiritualism, a Spell whose target is the generic filter row (machine 0x140157350).
        private const int ReturnSource = 5246;
        private const int GainAtkSource = 4905;
        private const int GainAtkOwnSource = 11427;
        // Negation (docs/EffectSystem.md section 39): the negate-and-destroy machine 0x14015EA50 with Cond_NegateTargetMatches (0x1400FC410) as its
        // check; the runtime puts the card's own "negate" row into that check. Trap Jammer has no cost of its own (any cost composes onto it) and
        // its id falls through every per-card test of the check. Maryokutai is the monster version: a Quick Effect that Tributes itself.
        private const int NegateSource = 5921;        // Trap Jammer
        private const int NegateTributeSelfSource = 5260;   // Maryokutai

        private static readonly string[] Races = { "dragon", "zombie", "fiend", "pyro", "seaserpent", "rock", "machine", "fish", "dinosaur",
            "insect", "beast", "beastwarrior", "plant", "aqua", "warrior", "wingedbeast", "fairy", "spellcaster", "thunder", "reptile",
            "psychic", "wyrm", "cyberse", "divinebeast", "creatorgod" };
        private static readonly string[] Attributes = { "light", "dark", "water", "fire", "earth", "wind", "divine" };

        // ANTLR splits error listeners by what they watch: the lexer reports over raw token TYPES
        // (IAntlrErrorListener<int>), the parser over actual Token instances (IAntlrErrorListener<IToken>,
        // which is all BaseErrorListener implements) - one class needs to implement both to catch errors
        // from either stage.
        private sealed class CollectingErrorListener : BaseErrorListener, IAntlrErrorListener<int>
        {
            public string? FirstError;

            public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line,
                int charPositionInLine, string msg, RecognitionException e) => Record(line, charPositionInLine, msg);

            public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line,
                int charPositionInLine, string msg, RecognitionException e) => Record(line, charPositionInLine, msg);

            private void Record(int line, int charPositionInLine, string msg) => FirstError ??= $"line {line}:{charPositionInLine} {msg}";
        }

        private sealed class ScriptError : System.Exception
        {
            public ScriptError(string message) : base(message) { }
        }

        public static Result Compile(string source)
        {
            var input = new AntlrInputStream(source);
            var lexer = new EffectScriptLexer(input);
            var errors = new CollectingErrorListener();
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(errors);

            var tokens = new CommonTokenStream(lexer);
            var parser = new EffectScriptParser(tokens);
            parser.RemoveErrorListeners();
            parser.AddErrorListener(errors);

            var tree = parser.script();
            if (errors.FirstError != null)
                return new Result { Ok = false, Error = errors.FirstError };

            try
            {
                if (tree.asScript() is { } asScript)
                {
                    // The whole behaviour of one game card. In a duel Yu-Gi-Oh-MoreCards lends this card that id when it is free, so every
                    // id-keyed rule of the engine treats it as that card (docs/EffectSystem.md section 32).
                    int from = asScript.NUMBER() != null ? int.Parse(asScript.NUMBER().GetText()) : CardIdByName(asScript.STRING().GetText().Trim('"'));
                    var asCompiled = new JsonObject { ["from"] = from };
                    if (asScript.statPart().Length > 0)
                        asCompiled["stats"] = Stats(asScript.statPart());
                    return new Result { Ok = true, Compiled = asCompiled };
                }

                // One body per effect: effect { } blocks, or bodies joined with 'also'. The first is the card's main effect, the others become "parts".
                var bodies = tree.effect().Length > 0 ? tree.effect().Select(e => e.effectBody()).ToArray() : tree.effectBody();
                var main = CompileBody(bodies[0]);
                if (bodies.Length > 1)
                {
                    var parts = new JsonArray();
                    foreach (var other in bodies.Skip(1))
                    {
                        if (other.chain().action().Length > 1)
                            throw new ScriptError("'then' is only supported in a card's first effect so far.");
                        parts.Add(CompileBody(other));
                    }
                    main["parts"] = parts;
                }
                return new Result { Ok = true, Compiled = main };
            }
            catch (ScriptError e)
            {
                return new Result { Ok = false, Error = e.Message };
            }
        }

        // One effect: its action(s), trigger and once-per-turn limit -> one effectClone step.
        private static JsonObject CompileBody(EffectScriptParser.EffectBodyContext body)
        {
            {
                var actions = body.chain().action();
                var compiled = CompileAction(actions[^1]);
                if (body.limit() != null)
                    compiled["oncePerTurn"] = true;
                if (body.requireClause() is { } require)
                {
                    if (require.condName().selector() is { } controls)
                    {
                        // "If you control a ... monster": the game's field scan through the effect's own filter row (Quillbolt Hedgehog's check).
                        if (compiled.ContainsKey("filter"))
                            throw new ScriptError("if controls(...) cannot be used with destroy / banish (both need the effect's one filter row).");
                        var controlFilter = DestroyFilter("own", controls);
                        if (KindOf(controls) is "spell" or "trap")
                            controlFilter["kind"] = "spelltrap";
                        compiled["filter"] = controlFilter;
                        AddRequire(compiled, "controlsMatch");
                    }
                    else
                        AddRequire(compiled, "noMonsters");
                }
                if (actions[^1].negateAction() != null)
                {
                    if (body.trigger() != null)
                        throw new ScriptError("negate(...) is activated in response to a card or effect; it cannot have an 'on ...:' trigger.");
                    if (actions.Length > 1)
                        throw new ScriptError("negate(...) cannot be chained with 'then'.");
                    if (body.costClause()?.selfCost() is { } negateSelf)
                    {
                        if (negateSelf.GetText() != "tribute_self")
                            throw new ScriptError($"negate(...) with cost {negateSelf.GetText()} has no game card to borrow from yet (tribute_self does: a monster's Quick Effect).");
                        compiled["from"] = NegateTributeSelfSource;   // its own row Tributes itself (slot 3) and checks that it can
                        return compiled;
                    }
                    if (body.costClause()?.detachCost() != null)
                        throw new ScriptError("negate(...) with cost detach(N) has no game card to borrow from yet.");
                }
                if (body.costClause() is { } cost && cost.selfCost() is { } self)
                {
                    // The card pays with itself: the row of a game card activated from that place, the action composed onto it.
                    if (body.trigger() != null)
                        throw new ScriptError($"cost {self.GetText()} already says where the effect is activated; it cannot have an 'on ...:' trigger.");
                    if (actions[^1].summonAction()?.summonZone().GetText() is "self" or "self_grave")
                        throw new ScriptError($"special_summon(self) cannot have the cost {self.GetText()}.");
                    (int donor, string check) = self.GetText() switch
                    {
                        "banish_self" => (BanishSelfSource, "canBanishSelfFromGrave"),
                        "discard_self" => (DiscardSelfSource, "canDiscardSelf"),
                        _ => (TributeSelfSource, "canTributeSelf"),
                    };
                    if (!compiled.ContainsKey("actionFrom"))   // an action that is already composed (banish) keeps its own action card
                        compiled["actionFrom"] = compiled["from"]!.GetValue<int>();
                    compiled["from"] = donor;
                    AddRequire(compiled, check);
                }
                else if (body.costClause() is { } detachCost && detachCost.detachCost() is { } detach)
                {
                    if (body.trigger() != null)
                        throw new ScriptError("cost detach(N) is an ignition effect of the Xyz Monster; it cannot have an 'on ...:' trigger.");
                    int count = int.Parse(detach.NUMBER().GetText());
                    if (count < 1 || count > 5) throw new ScriptError("detach takes 1 to 5 materials.");
                    if (!compiled.ContainsKey("actionFrom"))
                        compiled["actionFrom"] = compiled["from"]!.GetValue<int>();
                    compiled["from"] = DetachSource;
                    compiled["detach"] = count;
                    AddRequire(compiled, "canDetach");
                }
                else if (body.costClause() is { } tributeClause && tributeClause.tributeCost() is { } tribute)
                {
                    if (int.Parse(tribute.NUMBER().GetText()) != 1) throw new ScriptError("tribute(N): only 1 so far.");
                    compiled["cost"] = new JsonObject { ["from"] = TributeCostSource, ["check"] = "canTributeOne" };
                }
                else if (body.costClause() is { } cost2)
                {
                    // The game's cost functions borrowed from a card that pays exactly this cost (slot 3), with this card's own amount.
                    compiled["cost"] = cost2.discardCost() is { } discard
                        ? new JsonObject { ["from"] = DiscardCostSource, ["amount"] = int.Parse(discard.NUMBER().GetText()) }
                        : new JsonObject { ["from"] = PayCostSource, ["amount"] = int.Parse(cost2.payCost().NUMBER().GetText()) };
                }
                if (body.trigger() is { } trigger)
                {
                    if (actions[^1].summonAction()?.summonZone().GetText() is "self" or "self_grave")
                        throw new ScriptError("special_summon(self) is activated from the hand; it cannot have an 'on ...:' trigger.");
                    // A monster effect: borrow a vanilla MONSTER with this trigger and this action instead of a Normal Spell.
                    string name = trigger.triggerName().GetText();
                    if (TryTriggerSource(name, ActionKey(actions[^1]), out int source))
                        compiled["from"] = source;
                    else
                    {
                        // No game monster has this trigger with this action: compose one. The row (trigger, table, event wiring) comes from any
                        // monster with the trigger, the action's slots from the action's usual card ("actionFrom", docs/EffectSystem.md section 33).
                        if (!compiled.ContainsKey("actionFrom"))   // an action that is already composed (banish) keeps its own action card
                            compiled["actionFrom"] = compiled["from"]!.GetValue<int>();
                        compiled["from"] = AnyTriggerSource(name);
                    }
                    compiled["trigger"] = name;
                }
                if (actions.Length > 1)
                {
                    // The runtime runs "immediate" effects (draw, life points: they finish in one call) ahead of the card's last action, which may
                    // be interactive (search, revive, ...). An interactive action followed by anything is not runnable yet (docs/EffectLanguage.md).
                    var before = new JsonArray();
                    foreach (var earlier in actions[..^1])
                    {
                        if (earlier.drawAction() == null && earlier.gainAction() == null && earlier.burnAction() == null)
                            throw new ScriptError("only draw, gain_lp and burn can come before another action with 'then'; put search / revive / destroy / send_to_grave last.");
                        before.Add(CompileAction(earlier));
                    }
                    compiled["before"] = before;
                }
                return compiled;
            }
        }

        // "require": the game's own checks run before the effect's condition (Yu-Gi-Oh-Effects knows them by these names).
        private static void AddRequire(JsonObject compiled, string check)
        {
            if (compiled["require"] is not JsonArray list)
                compiled["require"] = list = new JsonArray();
            if (!list.Any(n => n?.GetValue<string>() == check))
                list.Add(check);
        }

        // trigger_sources.json (next to WolfEx, made by docs\effect-scripts\build_effect_reference.py): trigger -> action -> game cards to borrow from.
        private static JsonNode? _sources;

        private static string ActionKey(EffectScriptParser.ActionContext action)
        {
            if (action.drawAction() != null) return "draw";
            if (action.gainAction() != null || action.burnAction() != null) return "burn";   // the life point handler takes any amount, either direction
            if (action.searchAction() != null) return "search";
            if (action.reviveAction() != null) return "revive";
            if (action.sendAction() != null) return "send_to_grave";
            if (action.equipAction() != null) throw new ScriptError("equip(...) is a Spell; it cannot have an 'on ...:' trigger.");
            if (action.gainAtkAction() != null) return "gain_atk";
            if (action.returnAction() != null) return "return_target";
            if (action.summonAction() is { } summon) return summon.summonZone().GetText() == "grave" ? "revive" : "special_summon_" + summon.summonZone().GetText();
            if (action.salvageAction() != null) return "add_from_grave";
            if (action.banishAction() is { } banishKey) return banishKey.scope().GetText() == "all" ? "banish_all" : "banish_target";
            if (action.negateAction() != null) return "negate";
            var destroy = action.destroyAction();
            return destroy.scope().GetText() == "all" ? "destroy_all" : "destroy_target";
        }

        private static bool TryTriggerSource(string trigger, string action, out int source)
        {
            source = 0;
            LoadSources();
            if (_sources?["sources"]?[trigger]?[action] is JsonArray list && list.Count > 0)
            {
                source = list[0]!["id"]!.GetValue<int>();
                return true;
            }
            return false;
        }

        // Any game monster with this trigger (the first source of its first action), for a composed effect.
        private static int AnyTriggerSource(string trigger)
        {
            LoadSources();
            if (_sources?["sources"]?[trigger] is JsonObject actions)
                foreach (var (_, list) in actions)
                    if (list is JsonArray a && a.Count > 0)
                        return a[0]!["id"]!.GetValue<int>();
            throw new ScriptError($"no game monster has the trigger 'on {trigger}' to borrow it from.");
        }

        private static void LoadSources()
        {
            if (_sources != null)
                return;
            string path = Path.Combine(AppContext.BaseDirectory, "trigger_sources.json");
            if (!File.Exists(path))
                throw new ScriptError("trigger_sources.json is missing next to WolfX (make it with docs/effect-scripts/build_effect_reference.py).");
            _sources = JsonNode.Parse(File.ReadAllText(path));
        }

        private static int TriggerSource(string trigger, string action)
        {
            if (_sources == null)
            {
                string path = Path.Combine(AppContext.BaseDirectory, "trigger_sources.json");
                if (!File.Exists(path))
                    throw new ScriptError("trigger_sources.json is missing next to WolfEx (make it with docs/effect-scripts/build_effect_reference.py).");
                _sources = JsonNode.Parse(File.ReadAllText(path));
            }
            var list = _sources?["sources"]?[trigger]?[action] as JsonArray;
            if (list == null || list.Count == 0)
            {
                var known = (_sources?["sources"]?[trigger] as JsonObject)?.Select(p => p.Key) ?? Enumerable.Empty<string>();
                throw new ScriptError($"no game card has 'on {trigger}' together with '{action}' to borrow from" +
                    (known.Any() ? $" (for {trigger} the game has: {string.Join(", ", known)})." : "."));
            }
            return list[0]!["id"]!.GetValue<int>();
        }

        // atk N / def N -> { "atk": N, "def": N } (Yu-Gi-Oh-Effects "stats": the amounts the game's equip / boost tables give).
        private static JsonObject Stats(EffectScriptParser.StatPartContext[] parts)
        {
            var stats = new JsonObject();
            foreach (var part in parts)
            {
                string key = part.GetChild(0).GetText();
                if (stats.ContainsKey(key)) throw new ScriptError($"'{key}' is given twice.");
                int value = int.Parse(part.NUMBER().GetText());
                if (value > 32767) throw new ScriptError($"{key} {value} is too large.");
                stats[key] = value;
            }
            return stats;
        }

        // as("Card name"): every game card's name (card_names.json next to WolfX, written by docs/effect-scripts/build_effect_reference.py).
        private static System.Collections.Generic.Dictionary<string, int>? _namedCards;

        /// <summary>Every game card's (English name, id) from card_names.json.</summary>
        internal static IEnumerable<(string Name, int Id)> NamedCards()
        {
            TryCardIdByName("");
            return _namedCards!.Select(pair => (pair.Key, pair.Value));
        }

        /// <summary>A game card's id by its English name (card_names.json), 0 when there is none.</summary>
        internal static int TryCardIdByName(string name)
        {
            try { return CardIdByName(name); }
            catch (ScriptError) { return 0; }
        }

        private static int CardIdByName(string name)
        {
            if (_namedCards == null)
            {
                _namedCards = new System.Collections.Generic.Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase);
                string path = Path.Combine(AppContext.BaseDirectory, "card_names.json");
                if (File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject names)
                    foreach (var (n, id) in names)
                        if (id != null)
                            _namedCards.TryAdd(n, id.GetValue<int>());
            }
            if (_namedCards.TryGetValue(name, out int found))
                return found;
            throw new ScriptError($"no game card is named \"{name}\" (card_names.json); check the spelling or use its Konami id: as(<id>).");
        }

        private static JsonObject CompileAction(EffectScriptParser.ActionContext action)
        {
            if (action.equipAction() is { } equip)
                return new JsonObject { ["from"] = EquipSource, ["stats"] = Stats(equip.statPart()) };

            if (action.returnAction() is { } bounce)
            {
                var returnFilter = DestroyFilter(bounce.side()?.GetText() ?? "any", bounce.selector());
                string kind = KindOf(bounce.selector());
                if (kind == "card") kind = "monster";
                returnFilter["target"] = true;
                returnFilter["kind"] = kind == "monster" ? "monster" : "spelltrap";
                return new JsonObject { ["from"] = ReturnSource, ["filter"] = returnFilter };
            }

            if (action.gainAtkAction() is { } gainAtk)
            {
                int atk = int.Parse(gainAtk.NUMBER().GetText());
                if (atk > 32767) throw new ScriptError($"gain_atk {atk} is too large.");
                string atkSide = gainAtk.side()?.GetText() ?? "any";
                if (atkSide == "opponent") throw new ScriptError("gain_atk(opponent, ...) has no game card to borrow from; use any or own.");
                return new JsonObject { ["from"] = atkSide == "own" ? GainAtkOwnSource : GainAtkSource, ["stats"] = new JsonObject { ["atk"] = atk } };
            }

            if (action.summonAction() is { } summon)
            {
                if (summon.summonZone().GetText() == "self")
                {
                    if (summon.selector() != null) throw new ScriptError("special_summon(self) takes no selector.");
                    return new JsonObject { ["from"] = SummonSelfSource, ["condition"] = "always", ["require"] = new JsonArray("inHand", "canSummonSelf") };
                }
                if (summon.summonZone().GetText() == "self_grave")
                {
                    if (summon.selector() != null) throw new ScriptError("special_summon(self_grave) takes no selector.");
                    return new JsonObject { ["from"] = SummonSelfGraveSource, ["condition"] = "always", ["require"] = new JsonArray("canSummonSelfFromGrave") };
                }
                return summon.summonZone().GetText() switch
                {
                    "hand" => new JsonObject { ["from"] = SummonHandSource, ["deck"] = DeckFilter(summon.selector(), "handSummon") },
                    "deck" => new JsonObject { ["from"] = SummonDeckSource, ["condition"] = "listHasMatch", ["deck"] = DeckFilter(summon.selector(), "deckSummon") },
                    _ => new JsonObject { ["from"] = ReviveSource, ["deck"] = DeckFilter(summon.selector(), "graveSummon") },
                };
            }

            if (action.salvageAction() is { } salvage)
                return salvage.GetChild(2).GetText() == "banished"
                    ? new JsonObject { ["from"] = SalvageBanishedSource, ["deck"] = DeckFilter(salvage.selector(), "banished") }
                    : new JsonObject { ["from"] = SalvageSource, ["deck"] = DeckFilter(salvage.selector(), "grave") };

            if (action.banishAction() is { } banish)
            {
                // Same filter vocabulary as destroy; the Spell row of the matching destroy card, the banish slots of a banish card.
                var banishFilter = DestroyFilter(banish.side()?.GetText() ?? "any", banish.selector());
                if (banish.scope().GetText() == "target")
                {
                    string kind = KindOf(banish.selector());
                    if (kind == "card") kind = "monster";
                    banishFilter["target"] = true;
                    banishFilter["kind"] = kind == "monster" ? "monster" : "spelltrap";
                    return new JsonObject { ["from"] = TargetDestroySource, ["actionFrom"] = BanishTargetSource, ["filter"] = banishFilter };
                }
                return new JsonObject { ["from"] = DestroySource, ["actionFrom"] = BanishAllSource, ["filter"] = banishFilter };
            }

            if (action.negateAction() is { } negate)
                return new JsonObject { ["from"] = NegateSource, ["negate"] = NegateSpec(negate) };

            if (action.drawAction() is { } draw)
                return new JsonObject { ["from"] = DrawSource, ["draw"] = int.Parse(draw.NUMBER().GetText()) };

            if (action.searchAction() is { } search)
                return new JsonObject { ["from"] = SearchSource, ["deck"] = DeckFilter(search.selector(), "deck") };

            if (action.reviveAction() is { } revive)
            {
                string scan = revive.graveyard().GetText() switch
                {
                    "grave" => "graveSummon",
                    "opponent_grave" => "opponentGrave",
                    _ => "bothGravesSummon",
                };
                return new JsonObject { ["from"] = ReviveSource, ["deck"] = DeckFilter(revive.selector(), scan) };
            }

            if (action.sendAction() is { } send)
                return new JsonObject { ["from"] = SendSource, ["deck"] = DeckFilter(send.selector(), "deck") };

            if (action.gainAction() is { } gain)
                return new JsonObject { ["from"] = GainSource, ["lp"] = new JsonObject { ["gain"] = int.Parse(gain.NUMBER().GetText()) } };

            if (action.burnAction() is { } burn)
                return new JsonObject { ["from"] = BurnSource, ["lp"] = new JsonObject { ["damage"] = int.Parse(burn.NUMBER().GetText()) } };

            var destroy = action.destroyAction();
            var filter = DestroyFilter(destroy.side()?.GetText() ?? "any", destroy.selector());
            if (destroy.scope().GetText() == "all" && KindOf(destroy.selector()) is "spell" or "trap")
                throw new ScriptError("destroy(all, ...) only targets monsters; use destroy(target, spell) for one Spell/Trap.");
            if (destroy.scope().GetText() == "target")
            {
                string kind = KindOf(destroy.selector());
                if (kind == "card") kind = "monster";
                filter["target"] = true;
                filter["kind"] = kind == "monster" ? "monster" : "spelltrap";
                return new JsonObject { ["from"] = TargetDestroySource, ["filter"] = filter };
            }
            return new JsonObject { ["from"] = DestroySource, ["filter"] = filter };
        }

        // negate(what[, words...]) -> the runtime's "negate" block (Yu-Gi-Oh-Effects BuildNegate).
        private static JsonObject NegateSpec(EffectScriptParser.NegateActionContext negate)
        {
            var args = negate.negateArg();
            var spec = new JsonObject();
            string first = args[0].GetText();
            if (args[0].NUMBER() != null)
            {
                int id = int.Parse(first);
                if (id < 3000) throw new ScriptError($"negate({id}): a card id is 3000 or more.");
                spec["what"] = id;
            }
            else if (args[0].STRING() != null)
                spec["what"] = CardIdByName(first.Trim('"'));
            else
                spec["what"] = first switch
                {
                    "spell" or "trap" or "monster" or "spelltrap" or "any" => first,
                    "card" => "any",
                    _ => throw new ScriptError($"negate({first}, ...): the first argument is spell, trap, monster, spelltrap, any or a card."),
                };
            foreach (var arg in args.Skip(1))
            {
                string word = arg.GetText();
                switch (word)
                {
                    case "opponent": spec["opponentOnly"] = true; break;
                    case "your_turn": spec["yourTurn"] = true; break;
                    case "opponent_turn": spec["opponentTurn"] = true; break;
                    case "battle_phase": spec["battlePhase"] = true; break;
                    case "targets_one": spec["targetsOne"] = true; break;
                    case "effect": spec["activation"] = false; break;
                    case "normal" or "counter" or "field" or "equip" or "continuous" or "quickplay" or "ritual":
                        if (spec.ContainsKey("property")) throw new ScriptError("negate: only one Spell/Trap property.");
                        if (spec["what"] is JsonValue v && v.TryGetValue<string>(out var w) && w is "monster")
                            throw new ScriptError($"negate(monster, {word}): a property is for Spell/Trap Cards.");
                        spec["property"] = word;
                        break;
                    default:
                        throw new ScriptError($"negate: unknown word '{word}' (opponent, your_turn, opponent_turn, battle_phase, targets_one, effect, normal, counter, field, equip, continuous, quickplay, ritual).");
                }
            }
            return spec;
        }

        private sealed record Cond(string Field, string Op, string Value, bool IsNumber);

        private static Cond[] Conditions(EffectScriptParser.SelectorContext? selector) =>
            selector?.condition().Select(c => new Cond(c.field().GetText(), c.op().GetText(), c.value().GetText().Trim('"'),
                c.value().NUMBER() != null)).ToArray() ?? System.Array.Empty<Cond>();

        private static string Squash(string s) => new string(s.Where(ch => ch != ' ' && ch != '-' && ch != '_').ToArray()).ToLowerInvariant();

        private static void CheckName(string field, string value, string[] known)
        {
            if (!known.Contains(Squash(value)))
                throw new ScriptError($"unknown {field} \"{value}\".");
        }

        // archetype = "Dark World" (a name from Archetypes.json / the game's archetype list) or a code number
        private static int ArchetypeCode(Cond c)
        {
            if (c.IsNumber) return int.Parse(c.Value);
            int code = ArchetypeCatalog.CodeOf(c.Value);
            if (code == 0) throw new ScriptError($"unknown archetype \"{c.Value}\" (see Archetypes.json).");
            return code;
        }

        private static string KindOf(EffectScriptParser.SelectorContext? selector) => selector?.kind()?.GetText() ?? "card";

        // search / revive: a monster/spell/trap type plus any mix of race, attribute and one level comparison.
        private static JsonObject DeckFilter(EffectScriptParser.SelectorContext? selector, string scan)
        {
            var deck = new JsonObject { ["scan"] = scan };
            string kind = KindOf(selector);
            if (kind != "card")
                deck["type"] = kind;

            foreach (var c in Conditions(selector))
            {
                switch (c.Field)
                {
                    case "race":
                        if (c.Op != "=") throw new ScriptError("race only supports '='.");
                        CheckName("race", c.Value, Races);
                        deck["race"] = c.Value;
                        break;
                    case "attribute":
                        if (c.Op != "=") throw new ScriptError("attribute only supports '='.");
                        CheckName("attribute", c.Value, Attributes);
                        deck["attribute"] = c.Value;
                        break;
                    case "level":
                        if (!c.IsNumber) throw new ScriptError("level needs a number.");
                        int level = int.Parse(c.Value);
                        (int adjusted, string op) = c.Op switch
                        {
                            "=" => (level, "eq"),
                            "<=" => (level, "le"),
                            ">=" => (level, "ge"),
                            "<" => (level - 1, "le"),
                            _ => (level + 1, "ge"),
                        };
                        if (adjusted < 0 || adjusted > 15) throw new ScriptError("level must end up between 0 and 15.");
                        if (deck.ContainsKey("level")) throw new ScriptError("only one level condition is supported.");
                        deck["level"] = adjusted;
                        deck["levelOp"] = op;
                        break;
                    case "atk":
                        // The game's list rows have exactly one ATK test: "1500 or less" (row flag 0x2000, Sangan / Salvage style).
                        if (c.Op != "<=" || !c.IsNumber || c.Value != "1500")
                            throw new ScriptError("atk: only 'atk <= 1500' exists in the game's list rows so far.");
                        deck["atk1500"] = true;
                        break;
                    case "archetype":
                        if (c.Op != "=") throw new ScriptError("archetype only supports '='.");
                        deck["archetype"] = ArchetypeCode(c);
                        break;
                    case "name":
                        if (!c.IsNumber || c.Op != "=") throw new ScriptError("name = <the game's card id number> (the card's Konami id).");
                        deck["card"] = int.Parse(c.Value);
                        break;
                    default:
                        throw new ScriptError($"'{c.Field}' conditions are not supported by search/revive yet.");
                }
            }
            return deck;
        }

        // destroy all: the game's row filter matches ONE thing (race, or attribute, or a level bound, or archetype).
        private static JsonObject DestroyFilter(string side, EffectScriptParser.SelectorContext? selector)
        {
            var filter = new JsonObject { ["side"] = side };
            var conditions = Conditions(selector);
            if (conditions.Length > 1)
                throw new ScriptError("destroy(all, ...) supports one condition (the game's filter rows match a single property).");
            if (KindOf(selector) is "spell" or "trap" && conditions.Length > 0)
                throw new ScriptError("spell/trap destroy cannot filter by race, attribute or level.");

            foreach (var c in conditions)
            {
                switch (c.Field)
                {
                    case "race":
                        if (c.Op != "=") throw new ScriptError("race only supports '='.");
                        CheckName("race", c.Value, Races);
                        filter["race"] = c.Value;
                        break;
                    case "attribute":
                        if (c.Op != "=") throw new ScriptError("attribute only supports '='.");
                        CheckName("attribute", c.Value, Attributes);
                        filter["attribute"] = c.Value;
                        break;
                    case "level":
                        if (!c.IsNumber) throw new ScriptError("level needs a number.");
                        int level = int.Parse(c.Value);
                        switch (c.Op)
                        {
                            case "<=": filter["level_max"] = level; break;
                            case "<": filter["level_max"] = level - 1; break;
                            case ">=": filter["level_min"] = level; break;
                            case ">": filter["level_min"] = level + 1; break;
                            default: throw new ScriptError("use <= or >= (or < / >) for level in destroy(all, ...).");
                        }
                        break;
                    case "archetype":
                        if (c.Op != "=") throw new ScriptError("archetype only supports '='.");
                        filter["archetype"] = ArchetypeCode(c);
                        break;
                    default:
                        throw new ScriptError($"'{c.Field}' conditions are not supported by destroy yet.");
                }
            }
            return filter;
        }
    }
}
