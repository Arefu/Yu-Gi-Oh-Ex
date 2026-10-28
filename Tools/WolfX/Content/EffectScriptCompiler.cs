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
        // set(deck, ...): Altergeist Marionetter's monster row runs the game's set-from-Deck machine (0x14022A0A0) and is in none of its per-id /
        // per-archetype (Fire Fist 148, Metalfoes 275) branches; a Spell activates it from Pot of Greed's Normal Spell row (docs/EffectSystem.md s63)
        private const int SetSource = 13228;
        // fusion_summon(...): Polymerization's row and machine; its Fusion list row is replaced by the filter (deck.scan "fusion", section 64)
        private const int FusionSource = 4837;
        private const int FusionBanishSource = 6433;   // Dragon's Mirror: banish the materials from your field / GY (section 94)
        private const int ReviveSource = 4842;    // Monster Reborn
        private const int DestroySource = 4659;   // Warrior Elimination (destroy-all row driven by a filter)
        private const int SendSource = 5236;      // Foolish Burial
        private const int TargetDestroySource = 4838;   // Remove Trap (Target 1 X; destroy it - filter row driven)
        private const int GainSource = 4345;      // Red Medicine
        private const int BurnSource = 4350;      // Hinotama
        // lock_turn(...): the card whose resolve step adds the turn marker the lock builder reads; a Spell borrows Magic Deflector's row.
        private const int LockTurnSpellRow = 10298;   // Magic Deflector (Normal Spell)
        private static readonly Dictionary<string, int> LockTurnSources = new()
        {
            ["trap"] = 8223,               // Trap Stun: Traps on the field negated this turn
            ["spell"] = 10298,             // Magic Deflector: Equip / Field / Continuous / Quick-Play Spells negated this turn
            ["defense_position"] = 4799,   // Gorgon's Eye: Defense Position monsters' effects negated this turn
        };
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
        // Pay LP (slot 3 of the effect row): Delinquent Duo "Pay 1000 LP; ...". Discard / Tribute / send / reveal costs have no donor any more
        // (raw "costPick", docs/EffectSystem.md section 90).
        private const int PayCostSource = 4901;
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
                var continuous = tree.continuous();
                var lockJson = CompileLock(continuous.Select(c => c.lockStatement()).OfType<EffectScriptParser.LockStatementContext>().ToArray());
                var handJson = CompileHandLimit(continuous.Select(c => c.handLimitStatement()).OfType<EffectScriptParser.HandLimitStatementContext>().ToArray());
                var auraJson = CompileAuras(continuous.Select(c => c.auraStatement()).OfType<EffectScriptParser.AuraStatementContext>().ToArray());
                var protectJson = CompileProtects(continuous.Select(c => c.protectStatement()).OfType<EffectScriptParser.ProtectStatementContext>().ToArray());
                var nameJson = CompileNameAs(continuous.Select(c => c.nameStatement()).OfType<EffectScriptParser.NameStatementContext>().ToArray());
                var onlyOne = continuous.Select(c => c.onlyOneStatement()).OfType<EffectScriptParser.OnlyOneStatementContext>().ToArray();
                if (onlyOne.Length > 1)
                    throw new ScriptError("A card has one only_one(...).");
                var direct = continuous.Where(c => c.directStatement() != null).ToArray();
                if (direct.Length > 1)
                    throw new ScriptError("A card has one attack_directly.");
                var result = CompileScript(tree);
                if (direct.Length == 1)
                {
                    result ??= [];
                    // Yu-Gi-Oh-Effects Lock.cpp, docs s76; with a while clause {"while": [tests]} (s89)
                    result["attackDirectly"] = direct[0].whileClause() is { } when ? new JsonObject { ["while"] = CompileWhile(when) } : true;
                }
                var xyzAlso = continuous.Select(c => c.xyzAlsoStatement()).OfType<EffectScriptParser.XyzAlsoStatementContext>().ToArray();
                if (xyzAlso.Length > 1)
                    throw new ScriptError("A card has one xyz_also(...).");
                if (xyzAlso.Length == 1)
                {
                    result ??= [];
                    result["xyzAlso"] = CompileXyzAlso(xyzAlso[0]);
                }
                if (onlyOne.Length == 1)
                {
                    result ??= [];
                    // "onlyOne": "you" | "field" (Yu-Gi-Oh-Effects Lock.cpp answers Card_GetOnlyOneKind 1 / 2; docs s75)
                    result["onlyOne"] = onlyOne[0].GetText().Contains("(field)") ? "field" : "you";
                }
                if (nameJson != null)
                {
                    result ??= [];
                    result["nameAs"] = nameJson;
                }
                if (auraJson != null)
                {
                    result ??= [];
                    result["aura"] = auraJson;
                }
                if (protectJson != null)
                {
                    result ??= [];
                    result["protect"] = protectJson;
                }
                if (lockJson != null)
                {
                    result ??= [];
                    result["lock"] = lockJson;
                }
                if (handJson != null)
                {
                    result ??= [];
                    result["handLimit"] = handJson;
                }
                if (result == null)
                    throw new ScriptError("The script is empty.");
                return new Result { Ok = true, Compiled = result };
            }
            catch (ScriptError e)
            {
                return new Result { Ok = false, Error = e.Message };
            }
        }

        // The narrow monster locks: lock(monster, light) negates only LIGHT monsters' effects. Each is one slot of the game's lock state
        // (Lock.cpp kFilters), so one per lock, for both players' monsters.
        public static readonly string[] LockMonsterFilters = ["light", "a_counter", "except_koaki_meiru", "from_main_deck", "from_extra_deck",
            "fusion_synchro_xyz", "extra_monster_zone", "except_tribute_summoned", "summoned_this_turn", "defense_position"];

        // aura(atk N, def N, own | opponent | any | self, selector) -> "aura": [{"atk", "def", "side", "race", "attribute", "archetype"}] (Yu-Gi-Oh-Effects
        // Lock.cpp adds them to Calc_EffectiveAtkDef_Modifiers' result while the card is face-up and not negated; docs/EffectSystem.md s69).
        private static JsonArray? CompileAuras(EffectScriptParser.AuraStatementContext[] statements)
        {
            if (statements.Length == 0)
                return null;
            var list = new JsonArray();
            foreach (var statement in statements)
            {
                var aura = new JsonObject();
                string side = "you";
                foreach (var arg in statement.auraArg())
                {
                    if (arg.auraStat() is { } stat)
                    {
                        int n = int.Parse(stat.NUMBER().GetText()) * (stat.GetText().Contains('-') ? -1 : 1);
                        string key = stat.GetChild(0).GetText();
                        if (aura.ContainsKey(key)) throw new ScriptError($"aura: one {key} amount per aura.");
                        aura[key] = n;
                    }
                    else if (arg.countTest() is { } per)
                    {
                        if (aura.ContainsKey("per")) throw new ScriptError("aura: one per ... count per aura.");
                        aura["per"] = CountJson(per, false);
                    }
                    else if (arg.selector() is { } selector)
                    {
                        foreach (var c in Conditions(selector))
                        {
                            if (c.Op != "=") throw new ScriptError($"aura: {c.Field} only supports '='.");
                            switch (c.Field)
                            {
                                case "race": CheckName("race", c.Value, Races); aura["race"] = c.Value; break;
                                case "attribute": CheckName("attribute", c.Value, Attributes); aura["attribute"] = c.Value; break;
                                case "archetype": aura["archetype"] = ArchetypeCode(c); break;
                                default: throw new ScriptError($"aura: '{c.Field}' is not supported (race, attribute, archetype).");
                            }
                        }
                    }
                    else
                        side = arg.GetText() switch { "own" => "you", "opponent" => "opponent", "any" => "both", _ => "self" };
                }
                if (!aura.ContainsKey("atk") && !aura.ContainsKey("def"))
                    throw new ScriptError("aura: give atk N and / or def N.");
                aura["side"] = side;
                AddWhile(aura, statement);
                list.Add(aura);
            }
            return list;
        }

        // "while <tests>:" before a continuous statement -> "while": [tests] on its rule (Yu-Gi-Oh-Effects Lock.cpp Holds; docs/EffectSystem.md s89)
        private static void AddWhile(JsonObject rule, Antlr4.Runtime.ParserRuleContext statement)
        {
            if (statement.Parent is EffectScriptParser.ContinuousContext { } continuous && continuous.whileClause() is { } when)
                rule["while"] = CompileWhile(when);
        }

        private static JsonArray CompileWhile(EffectScriptParser.WhileClauseContext when) =>
            new(when.whileTest().Select(test => (JsonNode)(test.countTest() is { } count
                ? CountJson(count, test.GetChild(0).GetText() == "not")
                : test.GetChild(0).GetText() switch
                {
                    "lp_at_most" => new JsonObject { ["count"] = "lp", ["max"] = int.Parse(test.NUMBER().GetText()) },
                    "defense_position" => new JsonObject { ["count"] = "defense" },
                    _ => new JsonObject { ["count"] = "materials" },   // has_material
                })).ToArray());

        // controls / in_grave / in_hand / banished(N, side, other, selector) or materials -> {"count": "field" | "grave" | "hand" | "banished" |
        // "materials", "side", "min", "max", "other", "kind", "race", "attribute", "archetype", "name"}; 'not' = fewer than N (max N - 1).
        private static JsonObject CountJson(EffectScriptParser.CountTestContext test, bool not)
        {
            string place = test.GetChild(0).GetText();
            var json = new JsonObject
            {
                ["count"] = place switch { "controls" => "field", "in_grave" => "grave", "in_hand" => "hand", "banished" => "banished", _ => "materials" },
            };
            int min = 1;
            foreach (var arg in test.countArg())
            {
                if (arg.NUMBER() is { } number)
                    min = int.Parse(number.GetText());
                else if (arg.side() is { } side)
                    json["side"] = side.GetText() switch { "own" => "you", "opponent" => "opponent", _ => "both" };
                else if (arg.selector() is { } selector)
                {
                    if (KindOf(selector) is var kind and not "card")
                        json["kind"] = kind;
                    foreach (var c in Conditions(selector))
                    {
                        if (c.Op != "=") throw new ScriptError($"{place}: {c.Field} only supports '='.");
                        switch (c.Field)
                        {
                            case "race": CheckName("race", c.Value, Races); json["race"] = Squash(c.Value); break;
                            case "attribute": CheckName("attribute", c.Value, Attributes); json["attribute"] = Squash(c.Value); break;
                            case "archetype": json["archetype"] = ArchetypeCode(c); break;
                            case "name": json["name"] = c.IsNumber ? int.Parse(c.Value) : CardIdByName(c.Value); break;
                            default: throw new ScriptError($"{place}: '{c.Field}' is not supported (race, attribute, archetype, name).");
                        }
                    }
                }
                else
                    json["other"] = true;
            }
            if (not)
            {
                json["min"] = 0;
                json["max"] = min - 1;
            }
            else if (min != 1)
                json["min"] = min;
            return json;
        }

        // xyz_also(selector, xyz_monster, rank N | rank_at_least N | rank_at_most N) -> "xyzAlso": {"kind", "race", "attribute", "archetype", "name",
        // "xyz", "rankMin", "rankMax"} (Yu-Gi-Oh-Effects Lock.cpp answers Xyz_CanUseAsAltMaterial; docs/EffectSystem.md section 94)
        private static JsonObject CompileXyzAlso(EffectScriptParser.XyzAlsoStatementContext statement)
        {
            var json = new JsonObject();
            if (KindOf(statement.selector()) is "spell" or "trap")
                throw new ScriptError("xyz_also: the material is a monster you control.");
            foreach (var c in Conditions(statement.selector()))
            {
                if (c.Op != "=") throw new ScriptError($"xyz_also: {c.Field} only supports '='.");
                switch (c.Field)
                {
                    case "race": CheckName("race", c.Value, Races); json["race"] = Squash(c.Value); break;
                    case "attribute": CheckName("attribute", c.Value, Attributes); json["attribute"] = Squash(c.Value); break;
                    case "archetype": json["archetype"] = ArchetypeCode(c); break;
                    case "name": json["name"] = c.IsNumber ? int.Parse(c.Value) : CardIdByName(c.Value); break;
                    default: throw new ScriptError($"xyz_also: '{c.Field}' is not supported (race, attribute, archetype, name; rank N for the Rank).");
                }
            }
            foreach (var arg in statement.xyzAlsoArg())
            {
                string word = arg.GetChild(0).GetText();
                if (word == "xyz_monster") { json["xyz"] = true; continue; }
                int rank = int.Parse(arg.NUMBER().GetText());
                if (rank < 1 || rank > 13) throw new ScriptError("xyz_also: a Rank is 1 to 13.");
                if (word is "rank" or "rank_at_least") json["rankMin"] = rank;
                if (word is "rank" or "rank_at_most") json["rankMax"] = rank;
            }
            return json;
        }

        // indestructible(battle | effects | opponent_effects, own | opponent | any | self, selector) -> "protect": [{"from": "battle" | "effect" | "both",
        // "by": "any" | "opponent", "side", "race", "attribute", "archetype"}] (Yu-Gi-Oh-Effects Lock.cpp answers Battle_CanDestroyByBattle /
        // Destroy_CanEffectDestroy while the card is face-up and not negated; docs/EffectSystem.md s71).
        private static JsonArray? CompileProtects(EffectScriptParser.ProtectStatementContext[] statements)
        {
            if (statements.Length == 0)
                return null;
            var list = new JsonArray();
            foreach (var statement in statements)
            {
                string verb = statement.GetChild(0).GetText();
                if (verb != "indestructible")
                {
                    var shield = CompileShield(statement, verb);
                    AddWhile(shield, statement);
                    list.Add(shield);
                    continue;
                }
                var protect = new JsonObject();
                bool battle = false, effects = false, opponentOnly = false;
                string side = "self";
                foreach (var arg in statement.protectArg())
                {
                    if (arg.selector() is { } selector)
                    {
                        AddCover(protect, selector, verb);
                        continue;
                    }
                    switch (arg.GetText())
                    {
                        case "battle": battle = true; break;
                        case "effects": effects = true; break;
                        case "opponent_effects": effects = true; opponentOnly = true; break;
                        case var w when ProtectSide(w) is { } s: side = s; break;
                        default: throw new ScriptError($"indestructible: '{arg.GetText()}' belongs to untargetable(...) / unaffected(...).");
                    }
                }
                if (!battle && !effects)
                    throw new ScriptError("indestructible: say battle, effects and / or opponent_effects.");
                protect["from"] = battle && effects ? "both" : battle ? "battle" : "effect";
                if (opponentOnly)
                    protect["by"] = "opponent";
                protect["side"] = side;
                AddWhile(protect, statement);
                list.Add(protect);
            }
            return list;
        }

        // name_as(card, field | grave) -> "nameAs": {"id": N, "where": "field" | "grave"} (Yu-Gi-Oh-Effects Lock.cpp; "grave" = field and GY, the
        // game's Get_ProxyCardId path; docs/EffectSystem.md s73). One per card.
        private static JsonObject? CompileNameAs(EffectScriptParser.NameStatementContext[] statements)
        {
            if (statements.Length == 0)
                return null;
            if (statements.Length > 1)
                throw new ScriptError("A card has one name_as(...).");
            var s = statements[0];
            int id = s.NUMBER() != null ? int.Parse(s.NUMBER().GetText()) : CardIdByName(s.STRING().GetText().Trim('"'));
            string where = s.GetText().Contains(",field") ? "field" : "grave";
            return new JsonObject { ["id"] = id, ["where"] = where };
        }

        private static string? ProtectSide(string word) => word switch { "own" => "you", "opponent" => "opponent", "any" => "both", "self" => "self", _ => null };

        // the protect filter tests side / Type / Attribute / archetype (Lock.cpp reuses the aura's)
        private static void AddCover(JsonObject protect, EffectScriptParser.SelectorContext selector, string verb)
        {
            foreach (var c in Conditions(selector))
            {
                if (c.Op != "=") throw new ScriptError($"{verb}: {c.Field} only supports '='.");
                switch (c.Field)
                {
                    case "race": CheckName("race", c.Value, Races); protect["race"] = c.Value; break;
                    case "attribute": CheckName("attribute", c.Value, Attributes); protect["attribute"] = c.Value; break;
                    case "archetype": protect["archetype"] = ArchetypeCode(c); break;
                    default: throw new ScriptError($"{verb}: '{c.Field}' is not supported (race, attribute, archetype).");
                }
            }
        }

        // untargetable(effects | opponent_effects | attacks, ...) and unaffected(spells | traps | monster_effects | opponent_..., ...) ->
        // "protect": [{"from": "target" | "attack" | "spells" | "traps" | "monsters" (a list when several), "by", "side", filter}] (docs s72)
        private static JsonObject CompileShield(EffectScriptParser.ProtectStatementContext statement, string verb)
        {
            var protect = new JsonObject();
            var from = new List<string>();
            bool? opponentOnly = null;
            string side = "self";
            void Kind(string kind, bool opponent)
            {
                if (opponentOnly is { } o && o != opponent)
                    throw new ScriptError($"{verb}: mixing your opponent's and anyone's effects needs two statements.");
                opponentOnly = opponent;
                if (!from.Contains(kind)) from.Add(kind);
            }
            foreach (var arg in statement.protectArg())
            {
                if (arg.selector() is { } selector)
                {
                    AddCover(protect, selector, verb);
                    continue;
                }
                string w = arg.GetText();
                if (ProtectSide(w) is { } s) { side = s; continue; }
                switch (verb, w)
                {
                    case ("untargetable", "effects"): Kind("target", false); break;
                    case ("untargetable", "opponent_effects"): Kind("target", true); break;
                    case ("untargetable", "attacks"): if (!from.Contains("attack")) from.Add("attack"); break;   // attacks are always the opponent's
                    case ("unaffected", "spells"): Kind("spells", false); break;
                    case ("unaffected", "traps"): Kind("traps", false); break;
                    case ("unaffected", "monster_effects"): Kind("monsters", false); break;
                    case ("unaffected", "opponent_spells"): Kind("spells", true); break;
                    case ("unaffected", "opponent_traps"): Kind("traps", true); break;
                    case ("unaffected", "opponent_monster_effects"): Kind("monsters", true); break;
                    default: throw new ScriptError($"{verb}: '{w}' is not one of its words.");
                }
            }
            if (from.Count == 0)
                throw new ScriptError(verb == "untargetable" ? "untargetable: say effects, opponent_effects and / or attacks." : "unaffected: say spells, traps and / or monster_effects (or their opponent_ forms).");
            protect["from"] = from.Count == 1 ? from[0] : new JsonArray(from.Select(f => (JsonNode)f).ToArray());
            if (opponentOnly == true)
                protect["by"] = "opponent";
            protect["side"] = side;
            return protect;
        }

        // hand_limit(N, ...) -> "handLimit": {"limit": N, "side": "opponent" | "you" | "both", "zone": ...} (Yu-Gi-Oh-Effects Lock.cpp). One per card.
        private static JsonObject? CompileHandLimit(EffectScriptParser.HandLimitStatementContext[] statements)
        {
            if (statements.Length == 0)
                return null;
            if (statements.Length > 1)
                throw new ScriptError("A card has one hand_limit(...).");
            int limit = int.Parse(statements[0].NUMBER().GetText());
            if (limit > 60)
                throw new ScriptError("hand_limit: the limit must be 0..60.");
            string? side = null;
            string? zone = null;   // not written: Yu-Gi-Oh-Effects takes it from the card (Spell / Trap -> Spell & Trap Zone)
            foreach (var arg in statements[0].lockArg())
            {
                switch (arg.GetText())
                {
                    case "opponent": side = "opponent"; break;
                    case "own": side = "you"; break;
                    case "both": side = "both"; break;
                    case "monster_zone": zone = "monster"; break;
                    case "spelltrap_zone": zone = "spelltrap"; break;
                    case "any_zone": zone = "any"; break;
                    default: throw new ScriptError($"hand_limit: unknown word '{arg.GetText()}' (opponent, own, both; monster_zone, spelltrap_zone, any_zone).");
                }
            }
            if (side == null)
                throw new ScriptError("hand_limit: say whose hand it limits: opponent, own or both (e.g. hand_limit(3, opponent)).");
            var hand = new JsonObject { ["limit"] = limit, ["side"] = side };
            if (zone != null)
                hand["zone"] = zone;
            return hand;
        }

        // lock(...) -> the "lock" object Yu-Gi-Oh-Effects Lock.cpp reads (docs/EffectSystem.md section 44). One lock per card.
        private static JsonObject? CompileLock(EffectScriptParser.LockStatementContext[] statements)
        {
            if (statements.Length == 0)
                return null;
            if (statements.Length > 1)
                throw new ScriptError("A card has one lock(...): put every word in it, e.g. lock(trap, spell).");
            var json = new JsonObject { ["traps"] = false };
            string side = "both";
            string? zone = null;   // not written: from the card
            foreach (var arg in statements[0].lockArg())
            {
                switch (arg.GetText())
                {
                    case "trap": json["traps"] = true; break;
                    case "spell": json["spells"] = true; break;
                    case "monster": json["monsters"] = true; break;
                    case "continuous_trap": json["continuousTraps"] = true; break;
                    case "continuous_spell": json["continuousSpells"] = true; break;
                    case "field_spell": json["fieldSpells"] = true; break;
                    case "equip_spell": json["equipSpells"] = true; break;
                    case "both": side = "both"; break;
                    case "opponent": side = "opponent"; break;
                    case "own": side = "you"; break;
                    case "monster_zone": zone = "monster"; break;
                    case "spelltrap_zone": zone = "spelltrap"; break;
                    case "any_zone": zone = "any"; break;
                    case var word when LockMonsterFilters.Contains(word):
                        if (json["monsterFilter"] != null)
                            throw new ScriptError("lock: one monster filter per lock (" + json["monsterFilter"] + " and " + word + ").");
                        json["monsters"] = true;
                        json["monsterFilter"] = word;
                        break;
                    default:
                        throw new ScriptError($"lock: unknown word '{arg.GetText()}' (trap, spell, monster, continuous_trap, continuous_spell, field_spell, " +
                                              "equip_spell; both, opponent, own; monster_zone, spelltrap_zone, any_zone; " + string.Join(", ", LockMonsterFilters) + ").");
                }
            }
            if (json.Count == 1 && json["traps"]!.GetValue<bool>() == false)
                throw new ScriptError("lock(...) needs at least one of trap, spell, monster, continuous_trap, continuous_spell, field_spell, equip_spell.");
            if (json["monsterFilter"] != null && side != "both")
                throw new ScriptError("lock: a monster filter covers both players' monsters (the game keeps one slot for it); leave out opponent / own.");
            json["side"] = side;
            if (zone != null)
                json["zone"] = zone;
            return json;
        }

        // The card's effect without its lock: null when the script is only a lock(...).
        private static JsonObject? CompileScript(EffectScriptParser.ScriptContext tree)
        {
            {
                if (tree.asScript() is { } asScript)
                {
                    // The whole behaviour of one game card. In a duel Yu-Gi-Oh-MoreCards lends this card that id when it is free, so every
                    // id-keyed rule of the engine treats it as that card (docs/EffectSystem.md section 32).
                    return CompileAs(asScript);
                }

                // One body per effect: effect { } blocks, or bodies joined with 'also'. The first is the card's main effect, the others become "parts".
                var bodies = tree.effect().Length > 0 ? tree.effect().Select(e => e.effectBody()).ToArray() : tree.effectBody();
                var names = tree.effect().Length > 0 ? tree.effect().Select(e => e.STRING()?.GetText().Trim('"') ?? "").ToArray() : new string[bodies.Length];
                if (bodies.Length == 0)
                    return null;
                var main = CompileBody(bodies[0]);
                if (bodies.Length > 1)
                {
                    // "label": what the choose-an-effect prompt shows for each effect (Yu-Gi-Oh-Effects hooks the prompt's text, see EffectClone.cpp
                    // Hook_ChoiceText): the effect's name, else a short summary of what it does.
                    main["label"] = Label(names[0], bodies[0]);
                    var compiled = new List<JsonObject> { main };
                    var parts = new JsonArray();
                    for (int i = 1; i < bodies.Length; i++)
                    {
                        var other = bodies[i];
                        if (other.chain()?.action().Length > 1)
                            throw new ScriptError("'then' is only supported in a card's first effect so far.");
                        var part = CompileBody(other);
                        part["label"] = Label(names[i], other);
                        parts.Add(part);
                        compiled.Add(part);
                    }
                    LimitGroups(tree, bodies, compiled);
                    main["parts"] = parts;
                }
                return main;
            }
        }

        // Effects joined with 'or' are one "activate 1 of these effects" group: each gets "limitGroup" = the index of the group's first effect,
        // and once_per_turn on any of them limits them all (Yu-Gi-Oh-Effects gives the group one of the engine's four once-per-turn keys,
        // AssignLimitClasses; docs/EffectSystem.md section 58). A group of one is a plain effect.
        private static void LimitGroups(EffectScriptParser.ScriptContext tree, EffectScriptParser.EffectBodyContext[] bodies, List<JsonObject> compiled)
        {
            var group = new int[bodies.Length];
            int index = -1;
            bool joinedByOr = false;
            foreach (var child in tree.children ?? [])
            {
                if (child is EffectScriptParser.EffectContext or EffectScriptParser.EffectBodyContext)
                {
                    index++;
                    group[index] = joinedByOr && index > 0 ? group[index - 1] : index;
                    joinedByOr = false;
                }
                else if (child is Antlr4.Runtime.Tree.ITerminalNode token && token.GetText() == "or")
                    joinedByOr = true;
            }
            foreach (var members in Enumerable.Range(0, bodies.Length).GroupBy(i => group[i]).Where(g => g.Count() > 1))
            {
                if (members.Count() > 4)
                    throw new ScriptError("at most 4 effects can be joined with 'or' (the game's choose-an-effect prompt shows 4).");
                // the group's limit: per card name when any member says so, else per copy when any member says so
                var keys = members.Where(i => bodies[i].limit() != null).Select(i => LimitKey(bodies[i].limit())).ToList();
                if (keys.Contains("usesPerTurn"))
                    throw new ScriptError("effects joined with 'or' share one limit; uses_per_turn goes on an effect of its own.");
                string? key = keys.Contains("oncePerDuel") ? "oncePerDuel" : keys.Contains("oncePerTurn") ? "oncePerTurn" : keys.FirstOrDefault();
                foreach (int i in members)
                {
                    compiled[i]["limitGroup"] = members.Key;
                    compiled[i].Remove("oncePerTurn");
                    compiled[i].Remove("oncePerTurnPerCopy");
                    compiled[i].Remove("oncePerDuel");
                    if (key != null)
                        compiled[i][key] = true;
                }
            }
        }

        // The prompt text of one effect: its name, else its actions in a few words ("Draw 1 card, then inflict 500 damage").
        private static string Label(string? name, EffectScriptParser.EffectBodyContext body)
        {
            if (!string.IsNullOrWhiteSpace(name))
                return name.Trim();
            if (body.asScript() is { } asScript)
                return "Use the effect of " + asScript.GetChild(2).GetText().Trim('"');
            var actions = body.chain()?.action() ?? [];
            static string Number(Antlr4.Runtime.Tree.IParseTree node) =>
                System.Text.RegularExpressions.Regex.Match(node.GetText(), @"\d+").Value;
            var words = actions.Select(a =>
            {
                string n = Number(a);
                return a.GetChild(0).GetChild(0).GetText() switch
                {
                    "draw" => n == "1" ? "Draw 1 card" : $"Draw {n} cards",
                    "burn" or "damage" => $"Inflict {n} damage",
                    "gain_lp" => $"Gain {n} LP",
                    "search" => "Add a card from your Deck to your hand",
                    "set" => "Set a card from your Deck",
                    "fusion_summon" => "Fusion Summon a Fusion Monster",
                    "revive" => "Special Summon a monster from the GY",
                    "special_summon" => "Special Summon a monster",
                    "add_to_hand" => "Add a card to your hand",
                    "send_to_grave" => a.GetChild(0).GetChild(2).GetText() == "hand" ? "Send a card from your hand to the GY" : "Send a card from your Deck to the GY",
                    "destroy" => "Destroy",
                    "banish" => a.GetChild(0) is EffectScriptParser.BanishFromActionContext from
                        ? (from.GetChild(2).GetText() == "hand" ? "Banish a card from your hand" : "Banish a card from your Deck")
                        : "Banish",
                    "gain_atk" => $"A monster gains {n} ATK",
                    "return_to_hand" => "Return a card to the hand",
                    "negate" => "Negate an activation",
                    "equip" => "Equip",
                    var other => other,
                };
            });
            string label = string.Join(", then ", words);
            return label.Length > 0 ? char.ToUpperInvariant(label[0]) + label[1..] : "Effect";
        }

        // Draw / life point actions the plugin runs itself ("steps", Yu-Gi-Oh-Effects Slot0_RunSteps): used for a chain of them, or one that
        // names a player (draw(1, opponent), gain_lp(500, any), damage(300, own)). A single plain draw / gain_lp / burn keeps its game card.
        private static bool IsImmediate(EffectScriptParser.ActionContext a) =>
            a.drawAction() != null || a.gainAction() != null || a.burnAction() != null || a.damageAction() != null;

        private static bool UsesSteps(EffectScriptParser.ActionContext[] actions) =>
            actions.All(IsImmediate) && (actions.Length > 1 || actions.Any(a => a.drawAction()?.side() != null || a.gainAction()?.side() != null || a.damageAction() != null));

        private static JsonObject StepsAction(EffectScriptParser.ActionContext[] actions)
        {
            static string Who(EffectScriptParser.SideContext? side, string fallback) => side?.GetText() switch
            {
                null => fallback,
                "own" => "own",
                "opponent" => "opponent",
                _ => "both",   // any
            };
            var steps = new JsonArray();
            foreach (var a in actions)
            {
                if (a.drawAction() is { } draw)
                    steps.Add(new JsonObject { ["op"] = "draw", ["n"] = int.Parse(draw.NUMBER().GetText()), ["who"] = Who(draw.side(), "own") });
                else if (a.gainAction() is { } gain)
                    steps.Add(new JsonObject { ["op"] = "gain", ["n"] = int.Parse(gain.NUMBER().GetText()), ["who"] = Who(gain.side(), "own") });
                else if (a.burnAction() is { } burn)
                    steps.Add(new JsonObject { ["op"] = "damage", ["n"] = int.Parse(burn.NUMBER().GetText()), ["who"] = "opponent" });
                else if (a.damageAction() is { } damage)
                    steps.Add(new JsonObject { ["op"] = "damage", ["n"] = int.Parse(damage.NUMBER().GetText()), ["who"] = Who(damage.side(), "opponent") });
            }
            // the row (how the card is activated) is Pot of Greed's; what it does is the steps
            return new JsonObject { ["from"] = DrawSource, ["condition"] = "always", ["steps"] = steps };
        }

        private static JsonObject CompileAs(EffectScriptParser.AsScriptContext source)
        {
            int from = source.NUMBER() != null ? int.Parse(source.NUMBER().GetText()) : CardIdByName(source.STRING().GetText().Trim('"'));
            var result = new JsonObject { ["from"] = from };
            if (source.statPart().Length > 0)
                result["stats"] = Stats(source.statPart());
            // only the effect: Yu-Gi-Oh-Cards does not lend this card the source's id, so it keeps its own kind, stats and identity
            if (source.effectOnly() != null)
                result["effectOnly"] = true;
            return result;
        }

        // on <trigger>: as(...): the card's own monster trigger (a row borrowed from a game monster with it) runs the source card's action
        // ("actionFrom", docs/EffectSystem.md section 33) - e.g. a monster that uses a Trap's effect.
        private static JsonObject CompileTriggeredAs(EffectScriptParser.TriggerContext trigger, EffectScriptParser.AsScriptContext source)
        {
            var compiled = CompileAs(source);
            string name = trigger.triggerName().GetText();
            compiled["actionFrom"] = compiled["from"]!.GetValue<int>();
            if (name == "in_grave")
            {
                if (trigger.summonConditions() != null)
                    throw new ScriptError("'on in_grave' takes no (from ..., by ...) conditions.");
                compiled["from"] = SummonSelfGraveSource;
                AddRequire(compiled, "inGrave");
            }
            else
            {
                if (OwnTriggerKind(name))
                {
                    // the plugin's own trigger row; the action stays the as(...) card (actionFrom)
                    compiled["ownTrigger"] = true;
                    compiled["effectOnly"] = true;
                }
                else
                    compiled["from"] = AnyTriggerSource(name);
                AddTriggerConditions(compiled, name, trigger.summonConditions());
            }
            compiled["trigger"] = name;
            return compiled;
        }

        // no_special_summon(extra, resolved, kind, except selector) -> "summonLock": {"from": "extra" | "any", "when": "resolve" (absent =
        // activation), "kind", "race", "attribute", "archetype"}
        // (Yu-Gi-Oh-Effects EffectClone: the activation marker + Summon_CheckPlayerRestrictions; docs/EffectSystem.md section 77)
        private static readonly string[] SummonLockKinds = ["fusion", "synchro", "xyz", "link", "ritual"];
        private static JsonObject CompileSummonLock(EffectScriptParser.NoSummonContext noSummon)
        {
            var lockJson = new JsonObject { ["from"] = "any" };
            foreach (var arg in noSummon.noSummonArg())
            {
                if (arg.selector() is { } selector)
                {
                    foreach (var c in Conditions(selector))
                    {
                        if (c.Op != "=") throw new ScriptError($"no_special_summon: {c.Field} only supports '='.");
                        switch (c.Field)
                        {
                            case "race": CheckName("race", c.Value, Races); lockJson["race"] = c.Value; break;
                            case "attribute": CheckName("attribute", c.Value, Attributes); lockJson["attribute"] = c.Value; break;
                            case "archetype": lockJson["archetype"] = ArchetypeCode(c); break;
                            default: throw new ScriptError($"no_special_summon: '{c.Field}' is not supported (race, attribute, archetype).");
                        }
                    }
                    continue;
                }
                string w = arg.GetText();
                if (w == "extra")
                    lockJson["from"] = "extra";
                else if (w == "resolved")
                    lockJson["when"] = "resolve";   // "for the rest of this turn after this card resolves": locked once it has resolved
                else if (SummonLockKinds.Contains(w))
                    lockJson["kind"] = w;
                else
                    throw new ScriptError($"no_special_summon: '{w}' is not one of extra, resolved, fusion, synchro, xyz, link, ritual, except <selector>.");
            }
            return lockJson;
        }

        // once_per_duel -> "oncePerDuel" (classes 9 / 10); uses_per_turn(N) -> "usesPerTurn": N (2-9, per copy, classes 1-4; docs s74)
        private static void ApplyLimit(JsonObject compiled, EffectScriptParser.LimitContext limit)
        {
            string key = LimitKey(limit);
            if (key != "usesPerTurn")
            {
                compiled[key] = true;
                return;
            }
            int n = int.Parse(limit.NUMBER().GetText());
            if (n < 1 || n > 9)
                throw new ScriptError("uses_per_turn(N): N is 1..9.");
            if (n == 1)
                compiled["oncePerTurnPerCopy"] = true;
            else
                compiled["usesPerTurn"] = n;
        }

        // once_per_turn = per card name ("oncePerTurn", the engine's classes 5-8); once_per_turn(copy) = each copy on the field ("oncePerTurnPerCopy",
        // classes 1-4: a marker on the card's own zone, which the engine only checks for a card on the field; docs/EffectSystem.md section 59).
        private static string LimitKey(EffectScriptParser.LimitContext limit)
        {
            string head = limit.GetChild(0).GetText();
            if (head == "once_per_duel")
                return "oncePerDuel";
            if (head == "uses_per_turn")
                return "usesPerTurn";
            string? word = limit.IDENT()?.GetText();
            return word switch
            {
                null or "name" => "oncePerTurn",
                "copy" => "oncePerTurnPerCopy",
                _ => throw new ScriptError($"once_per_turn({word}): use once_per_turn (per card name) or once_per_turn(copy) (each copy on the field)."),
            };
        }

        // One effect: its action(s), trigger and once-per-turn limit -> one effectClone step.
        private static JsonObject CompileBody(EffectScriptParser.EffectBodyContext body)
        {
            if (body.asScript() is { } cloneStatement)
                return body.trigger() is { } asTrigger ? CompileTriggeredAs(asTrigger, cloneStatement) : CompileAs(cloneStatement);
            {
                var actions = body.chain().action();
                var compiled = UsesSteps(actions) ? StepsAction(actions) : CompileAction(actions[^1]);
                if (body.limit() is { } limit)
                    ApplyLimit(compiled, limit);
                if (body.noSummon() is { } noSummon)
                    compiled["summonLock"] = CompileSummonLock(noSummon);
                // a Quick Effect: Yu-Gi-Oh-Effects answers spell speed 2 for it (its donor card's speed would decide otherwise)
                if (body.quickMark() != null)
                    compiled["quick"] = true;
                // every condition of "if a and b:" is one more Yu-Gi-Oh-Effects "require" check (all must hold)
                foreach (var require in body.requireClause()?.condName() ?? [])
                {
                    if (require.selector() is { } named && Conditions(named).ToList() is [{ Field: "name" } nameCond])
                    {
                        // "If you control "X"": by name (Yu-Gi-Oh-Effects Check_ControlsName -> the game's Count_FaceUpSameNameOnField;
                        // docs/EffectSystem.md section 78) - the filter row cannot test a name
                        if (!nameCond.IsNumber || nameCond.Op != "=") throw new ScriptError("controls(card where name = <the game's card id number>).");
                        compiled["controlsName"] = int.Parse(nameCond.Value);
                        AddRequire(compiled, "controlsName");
                    }
                    else if (require.selector() is { } controls)
                    {
                        // "If you control a ... monster": the game's field scan through the effect's own filter row (Quillbolt Hedgehog's check).
                        if (compiled.ContainsKey("filter"))
                            throw new ScriptError("if controls(...) cannot be used with a destroy / banish / target action (both need the effect's one filter row).");
                        var controlFilter = DestroyFilter("own", controls);
                        if (KindOf(controls) is "spell" or "trap")
                            controlFilter["kind"] = "spelltrap";
                        compiled["filter"] = controlFilter;
                        AddRequire(compiled, "controlsMatch");
                    }
                    else if (require.NUMBER() is { } lpLimit)
                    {
                        // "If your LP are N or less" (Yu-Gi-Oh-Effects Check_LifePoints; docs/EffectSystem.md section 82)
                        compiled["lpAtMost"] = int.Parse(lpLimit.GetText());
                        AddRequire(compiled, "lpAtMost");
                    }
                    else
                        AddRequire(compiled, require.GetText() switch
                        {
                            "battle_phase" => "battlePhase",                 // Yu-Gi-Oh-Effects Check_Phase (sections 81, 93)
                            "your_battle_phase" => "yourBattlePhase",
                            "opponent_battle_phase" => "opponentBattlePhase",
                            "main_phase" => "mainPhase",
                            "your_main_phase" => "yourMainPhase",
                            "opponent_main_phase" => "opponentMainPhase",
                            "main_or_battle_phase" => "mainOrBattlePhase",
                            "your_main_or_battle_phase" => "yourMainOrBattlePhase",
                            "opponent_main_or_battle_phase" => "opponentMainOrBattlePhase",
                            "your_turn" => "yourTurn",
                            "opponent_turn" => "opponentTurn",
                            "battled" => "battledThisTurn",                  // marker 1024 on this card's zone (section 93)
                            "lp_lower" => "lpLower",                         // your LP are lower than your opponent's (Check_LifePoints)
                            _ => "noMonsters",
                        });
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
                    if (actions[^1].summonAction()?.summonZones().GetText() is "self" or "self_grave")
                        throw new ScriptError($"special_summon(self) cannot have the cost {self.GetText()}.");
                    (int donor, string? check, string? costSlot) = self.GetText() switch
                    {
                        "banish_self" => (BanishSelfSource, "canBanishSelfFromGrave", null),
                        "discard_self" => (DiscardSelfSource, "canDiscardSelf", null),
                        // the same hand / field rows with the game's send-to-GY costs in slot 3 (Yu-Gi-Oh-Effects "costSlot", section 84);
                        // canDiscardSelf = "this card is in your hand" (the game's send-from-hand rows use the same condition)
                        "send_self" => (DiscardSelfSource, "canDiscardSelf", "sendSelfFromHand"),
                        "send_self_field" => (TributeSelfSource, null, "sendSelfFromField"),
                        "banish_self_hand" => (DiscardSelfSource, "canDiscardSelf", "banishSelfFromHand"),     // Cost_BanishSelfFromHand
                        "banish_self_field" => (TributeSelfSource, null, "banishSelfFromField"),              // Cost_BanishSelfFromField
                        "tribute_self_hand" => (DiscardSelfSource, "canDiscardSelf", "tributeSelfFromHand"),   // Cost_TributeSelfFromHand
                        _ => (TributeSelfSource, "canTributeSelf", null),
                    };
                    ComposeOnto(compiled, donor);
                    if (check != null)
                        AddRequire(compiled, check);
                    if (costSlot != null)
                    {
                        compiled["costSlot"] = costSlot;
                        compiled["effectOnly"] = true;   // the donor only lends its row's place, not its identity
                    }
                }
                else if (body.costClause() is { } detachCost && detachCost.detachCost() is { } detach)
                {
                    if (body.trigger() != null)
                        throw new ScriptError("cost detach(N) is an ignition effect of the Xyz Monster; it cannot have an 'on ...:' trigger.");
                    int count = int.Parse(detach.NUMBER().GetText());
                    if (count < 1 || count > 5) throw new ScriptError("detach takes 1 to 5 materials.");
                    ComposeOnto(compiled, DetachSource);
                    compiled["detach"] = count;
                    AddRequire(compiled, "canDetach");
                }
                else if (body.costClause() is { } tributeClause && tributeClause.tributeCost() is { } tribute)
                {
                    // "Tribute N (X) monsters": the game's Cost_TributeOne / Cost_TributePickedMonsters as this card, the zone test from this
                    // filter (Yu-Gi-Oh-Effects raw "costPick", docs/EffectSystem.md section 90) - no donor card
                    compiled["costPick"] = PickCostJson("tribute", tribute.NUMBER().GetText(), tribute.selector(), "field");
                }
                else if (body.costClause() is { } discardClause && discardClause.discardCost() is { } discardPick)
                {
                    // "Discard N (X) cards": Cost_DiscardPickedHandCards as this card, its hand picker asking this filter (section 90)
                    compiled["costPick"] = PickCostJson("discard", discardPick.NUMBER().GetText(), discardPick.selector(), "hand");
                }
                else if (body.costClause() is { } handClause && handClause.pickCost() is { } handPick && handPick.GetChild(0).GetText() is "send_hand" or "reveal" or "send_hand_field")
                {
                    // "send N X from your hand to the GY" (Cost_SendPickedHandCardsToGrave), "reveal 1 X in your hand" (Cost_RevealPickedHandCards),
                    // "send 1 X from your hand or field to the GY" (Cost_SendPickedHandOrFieldCardToGrave), section 90
                    string verb = handPick.GetChild(0).GetText() switch { "reveal" => "reveal", "send_hand_field" => "sendHandOrField", _ => "send" };
                    compiled["costPick"] = PickCostJson(verb, handPick.NUMBER()?.GetText() ?? "1", handPick.selector(), "hand");
                }
                else if (body.costClause() is { } pickClause && pickClause.pickCost() is { } pick)
                {
                    // "banish N X from your GY (and this card)": Yu-Gi-Oh-Effects "costPick" - the game's Cost_BanishPickedCards with this list
                    // (docs/EffectSystem.md section 85), run as this card (raw, section 90). With this card, it is activated from the GY like banish_self.
                    int count = int.Parse(pick.NUMBER().GetText());
                    if (count < 1 || count > 5) throw new ScriptError("banish_grave / banish_self_and take 1 to 5 cards.");
                    var costPick = new JsonObject { ["verb"] = "banish", ["raw"] = true, ["count"] = count, ["deck"] = DeckFilter(pick.selector(), "grave") };
                    if (pick.GetChild(0).GetText() == "banish_self_and")
                    {
                        if (body.trigger() != null)
                            throw new ScriptError("cost banish_self_and(...) is activated from the GY; it cannot have an 'on ...:' trigger.");
                        costPick["self"] = true;
                        ComposeOnto(compiled, BanishSelfSource);
                        compiled["effectOnly"] = true;
                        AddRequire(compiled, "canBanishSelfFromGrave");
                    }
                    compiled["costPick"] = costPick;
                }
                else if (body.costClause() is { } cost2)
                {
                    // Pay LP: the game's cost function borrowed from a card that pays exactly this cost (slot 3), with this card's own amount.
                    compiled["cost"] = new JsonObject { ["from"] = PayCostSource, ["amount"] = int.Parse(cost2.payCost().NUMBER().GetText()), ["check"] = "always" };   // Delinquent Duo's own condition is "the opponent has a card in hand"
                }
                if (body.trigger() is { } graveTrigger && graveTrigger.triggerName().GetText() == "in_grave")
                {
                    // "If this card is in your GY: You can ...": activated from the GY. The GY / hand effects table (table 2) is asked for a card
                    // there; Quillbolt Hedgehog's row in it has no cost, and the action's slots are composed onto it ("actionFrom"). "inGrave"
                    // keeps it to the GY (the same table answers for the banished cards); docs/EffectSystem.md section 51.
                    if (graveTrigger.summonConditions() != null)
                        throw new ScriptError("'on in_grave' takes no (from ..., by ...) conditions.");
                    if (actions[^1].summonAction()?.summonZones().GetText() is "self" or "self_grave")
                        throw new ScriptError("special_summon(self) from the GY is special_summon(self_grave); it needs no 'on in_grave:'.");
                    ComposeOnto(compiled, SummonSelfGraveSource);
                    compiled["trigger"] = "in_grave";
                    AddRequire(compiled, "inGrave");
                }
                else if (body.trigger() is { } trigger)
                {
                    if (actions[^1].summonAction()?.summonZones().GetText() is "self" or "self_grave")
                        throw new ScriptError("special_summon(self) is activated from the hand; it cannot have an 'on ...:' trigger.");
                    // A monster effect: borrow a vanilla MONSTER with this trigger and this action instead of a Normal Spell.
                    string name = trigger.triggerName().GetText();
                    // a raw action (section 88) never takes a game card that happens to do the same: the trigger row is own or only a place
                    bool raw = compiled["rawRow"]?.GetValue<bool>() == true;
                    // ignition never takes a game card: its own table-3 row is the shape of a plain ignition row (OwnTriggerKind), and a borrowed
                    // one brought the donor's slot-3 cost along (Caam's "shuffle 2 Gusto monsters" prompt on every ignition, issue #39)
                    bool direct = !raw & name != "ignition" & TryTriggerSource(name, ActionKey(actions[^1]), out int source);
                    // a move trigger with conditions is always the plugin's own (it checks the move reference itself)
                    if (IsMoveTriggerName(name) && trigger.summonConditions() != null)
                        direct = false;
                    if (OwnTriggerKind(name) && !direct)
                    {
                        // No game card behind the trigger: Yu-Gi-Oh-Effects' own row and its own phase row ("ownTrigger", docs/EffectSystem.md
                        // section 61). "from" stays the action's card (the composed slots); the card keeps its own id.
                        compiled["ownTrigger"] = true;
                        compiled["effectOnly"] = true;
                        if (name == "in_hand")
                            AddRequire(compiled, "inHand");   // table 2 also answers for the GY: the hand only (Check_InHand)
                    }
                    else if (direct)
                        compiled["from"] = source;
                    else
                    {
                        // No game monster has this trigger with this action: compose one. The row (trigger, table, event wiring) comes from any
                        // monster with the trigger, the action's slots from the action's usual card ("actionFrom", docs/EffectSystem.md section 33).
                        ComposeOnto(compiled, AnyTriggerSource(name));
                    }
                    compiled["trigger"] = name;
                    AddTriggerConditions(compiled, name, trigger.summonConditions());
                }
                if (actions.Length > 1 && !compiled.ContainsKey("steps"))
                {
                    // The runtime's resolver runs the earlier actions in order, each its own game machine, then the card's last action (the one
                    // the row and trigger come from, docs/EffectSystem.md section 53). Only the last one gets the activation's target.
                    var before = new JsonArray();
                    foreach (var earlier in actions[..^1])
                    {
                        // "Target 1 X; shuffle it into the Deck, then draw 1 card": a first move(target, ...) step reads the targets this effect
                        // chooses when it is activated - the card's row gets the targeting slots and the filter ("targetSlots", section 86)
                        bool targetFirst = earlier == actions[0] && (earlier.moveAction()?.moveWhat().GetChild(0).GetText() == "target" || earlier.targetAction() != null)
                            && !System.Text.RegularExpressions.Regex.IsMatch(actions[^1].GetText(), "target") && !compiled.ContainsKey("filter");
                        if (!targetFirst && System.Text.RegularExpressions.Regex.IsMatch(earlier.GetText(), "target"))
                            throw new ScriptError("only the last action with 'then' can target (the target is chosen when the card is activated); put the targeting action last.");
                        var step = CompileAction(earlier);
                        if (targetFirst)
                        {
                            compiled["targetSlots"] = true;
                            compiled["filter"] = step["filter"]!.DeepClone();
                        }
                        before.Add(step);
                    }
                    compiled["before"] = before;
                    // Magnet Circle LV2's machine (0x140160080) picks the monster when the card is activated, so a card an earlier step put in
                    // the hand was never offered (in-duel 2026-10-21). Unexpected Dai's generic machine (0x140163830) collects at resolution:
                    // with the hand scan it offers the hand as it is then. Its activation check is "a match exists", which the earlier steps
                    // may only make true, so it is "always".
                    if (compiled["from"]?.GetValue<int>() == SummonHandSource && !compiled.ContainsKey("actionFrom"))
                    {
                        compiled["from"] = SummonDeckSource;
                        compiled["condition"] = "always";
                    }
                    // Searches and Special Summons in a chain run on the plugin's own pick-and-act machine (docs/EffectSystem.md section 54):
                    // the list is built when the step runs, the source card only gives the row and the filter.
                    foreach (var step in before.OfType<JsonObject>().Append(compiled))
                        MarkOwnMachine(step);
                }
                return compiled;
            }
        }

        // Put the action onto another row (a trigger monster's, a self-cost card's, the GY / field ignition table's): the row's card becomes "from".
        // A donor action keeps running as its own card ("actionFrom", section 33). A raw action (no donor, docs/EffectSystem.md section 88) keeps
        // its raw slots ("rawSlots") and only takes the row's PLACE - its table, event wiring and slot 3 - until that place has an own row too.
        private static void ComposeOnto(JsonObject compiled, int rowCard)
        {
            if (compiled["rawRow"]?.GetValue<bool>() == true)
            {
                compiled.Remove("rawRow");
                compiled["rawSlots"] = true;
                compiled["effectOnly"] = true;   // the row's card lends only its row's place, never its identity
            }
            else if (!compiled.ContainsKey("actionFrom"))   // an action that is already composed (banish) keeps its own action card
                compiled["actionFrom"] = compiled["from"]!.GetValue<int>();
            compiled["from"] = rowCard;
        }

        // "own": "toHand" for a search / salvage, "summon" for a Special Summon from a list ("deck.scan" ending in Summon), "toGrave" for a send.
        private static void MarkOwnMachine(JsonObject step)
        {
            if (step.ContainsKey("own") || step["deck"] is not JsonObject deck || deck["scan"]?.GetValue<string>() is not string scan)
                return;
            int source = step["actionFrom"]?.GetValue<int>() ?? step["from"]!.GetValue<int>();
            if (scan.EndsWith("Summon"))
                step["own"] = "summon";
            else if (source is SearchSource or SalvageSource)
                step["own"] = "toHand";
            else if (source == SendSource)
                step["own"] = "toGrave";
        }

        // A move the game has no list machine for (send from the hand, banish from the Deck / hand): Foolish Burial's row with the plugin's own
        // pick-and-act machine (Foolish Burial's movers, docs/EffectSystem.md section 55); its activation check counts through this filter.
        private static string MoveDestination(string dest) => dest switch
        {
            "to_hand" => "hand",
            "to_grave" => "grave",
            "to_banished" => "banished",
            "to_deck" => "deck",
            "to_deck_top" => "deckTop",
            "to_deck_bottom" => "deckBottom",
            "destroy" => "destroy",
            _ => "extra",
        };

        private static JsonObject OwnMove(string own, JsonObject filter) =>
            new() { ["from"] = SendSource, ["condition"] = "listHasMatch", ["deck"] = filter, ["own"] = own };

        // A trigger's (...) list: "watch" for the watchers (always written: the runtime reads it for every watcher), "moveCondition" for the move
        // triggers, "summonCondition" for the summon triggers.
        private static void AddTriggerConditions(JsonObject compiled, string name, EffectScriptParser.SummonConditionsContext? conditions)
        {
            if (IsWatchTriggerName(name))
                compiled["watch"] = CompileWatchConditions(name, conditions);
            else if (conditions == null)
                return;
            else if (IsMoveTriggerName(name))
                compiled["moveCondition"] = CompileMoveConditions(name, conditions);
            else
                compiled["summonCondition"] = CompileSummonConditions(name, conditions);
        }

        private static bool IsWatchTriggerName(string trigger) =>
            trigger is "card_destroyed" or "card_left_field" or "card_sent_to_grave" or "card_banished";

        // on card_destroyed(is monster where race = Insect, own, from monster, by battle, other, in_grave) -> "watch" (Yu-Gi-Oh-Effects WatchMatches,
        // docs/EffectSystem.md section 91)
        private static readonly string[] WatchZones = ["field", "monster", "spelltrap", "hand", "deck", "grave"];
        private static JsonObject CompileWatchConditions(string trigger, EffectScriptParser.SummonConditionsContext? conditions)
        {
            var result = new JsonObject();
            var from = new JsonArray();
            foreach (var condition in conditions?.summonCondition() ?? [])
            {
                if (condition.summonFrom() is { } zone)
                {
                    string z = zone.GetText();
                    if (!WatchZones.Contains(z))
                        throw new ScriptError($"'from {z}' for '{trigger}': the zones are {string.Join(", ", WatchZones)}.");
                    if (trigger is "card_destroyed" or "card_left_field" && z is "hand" or "deck" or "grave")
                        throw new ScriptError($"'{trigger}' is a card leaving the field: from field, monster or spelltrap.");
                    from.Add(z);
                }
                else if (condition.selector() is { } watched)
                {
                    string kind = KindOf(watched);
                    if (kind is "monster" or "spell" or "trap")
                        result["kind"] = kind;
                    else if (kind is "fusion" or "synchro" or "xyz" or "link" or "ritual")
                        (result["kind"], result["cardKind"]) = ("monster", kind);
                    foreach (var c in Conditions(watched))
                    {
                        if (c.Op != "=") throw new ScriptError($"is ...: {c.Field} only supports '='.");
                        switch (c.Field)
                        {
                            case "race": CheckName("race", c.Value, Races); result["race"] = c.Value; break;
                            case "attribute": CheckName("attribute", c.Value, Attributes); result["attribute"] = c.Value; break;
                            case "archetype": result["archetype"] = ArchetypeCode(c); break;
                            case "name": result["name"] = c.IsNumber ? int.Parse(c.Value) : CardIdByName(c.Value); break;
                            default: throw new ScriptError($"is ...: '{c.Field}' is not supported (race, attribute, archetype, name).");
                        }
                    }
                }
                else if (condition.side() is { } side)
                {
                    if (side.GetText() != "any")
                        result["side"] = side.GetText() == "own" ? "you" : "opponent";
                }
                else if (condition.summonBy() is { } by)
                {
                    if (result.ContainsKey("by"))
                        throw new ScriptError("one 'by ...' per trigger.");
                    if (by.GetText() is not ("battle" or "effect" or "opponent"))
                        throw new ScriptError($"'by {by.GetText()}' for '{trigger}': by battle, by effect or by opponent (an opponent's card effect).");
                    if (by.GetText() == "battle" && trigger is "card_sent_to_grave" or "card_banished")
                        throw new ScriptError($"'{trigger}' reads Eval_CardMovedTriggers, which a card destroyed by battle does not pass: use card_destroyed(..., by battle).");
                    result["by"] = by.GetText();
                }
                else if (condition.GetText() == "other")
                    result["other"] = true;
                else if (condition.GetText() == "in_grave")
                    result["inGrave"] = true;
                else
                    throw new ScriptError($"'{condition.GetText()}' for '{trigger}': is <card>, own / opponent, from <zone>, by battle | effect | opponent, other, in_grave.");
            }
            if (from.Count > 0)
                result["from"] = from;
            return result;
        }

        // on special_summoned(from grave, by effect): -> "summonCondition" (Yu-Gi-Oh-Effects checks it against the summon event, docs/EffectSystem.md section 46).
        private static readonly string[] SummonZones = ["hand", "deck", "extra", "grave", "banished", "spelltrap"];
        private static readonly string[] KindWords = ["fusion", "synchro", "xyz", "ritual", "link"];

        private static JsonObject CompileSummonConditions(string trigger, EffectScriptParser.SummonConditionsContext conditions)
        {
            // the summon reaction triggers read the same summon record (Yu-Gi-Oh-Effects SummonMatches, section 68)
            if (trigger is not ("normal_summoned" or "special_summoned" or "summoned" or "normal_or_special_summoned"
                or "opponent_special_summoned" or "opponent_summoned" or "monster_special_summoned" or "monster_summoned"))
                throw new ScriptError($"'{trigger}' is not a summon trigger; (from ..., by ...) only go with normal_summoned / special_summoned / summoned and the summon reactions.");
            var result = new JsonObject();
            var from = new JsonArray();
            foreach (var condition in conditions.summonCondition())
            {
                if (condition.summonFrom() is { } zone)
                {
                    string z = zone.GetText() == "gy" ? "grave" : zone.GetText();
                    if (!SummonZones.Contains(z))
                        throw new ScriptError($"'from {z}': the zones are {string.Join(", ", SummonZones)}.");
                    from.Add(z);
                }
                else if (condition.selector() is { } summoned)
                {
                    // the summoned monster (summon reactions, section 87): Type / Attribute / archetype / kind
                    var card = new JsonObject();
                    if (KindOf(summoned) is "fusion" or "synchro" or "xyz" or "link" or "ritual")
                        card["kind"] = KindOf(summoned);
                    foreach (var c in Conditions(summoned))
                    {
                        if (c.Op != "=") throw new ScriptError($"is ...: {c.Field} only supports '='.");
                        switch (c.Field)
                        {
                            case "race": CheckName("race", c.Value, Races); card["race"] = c.Value; break;
                            case "attribute": CheckName("attribute", c.Value, Attributes); card["attribute"] = c.Value; break;
                            case "archetype": card["archetype"] = ArchetypeCode(c); break;
                            default: throw new ScriptError($"is ...: '{c.Field}' is not supported (race, attribute, archetype).");
                        }
                    }
                    result["card"] = card;
                }
                else if (condition.side() is { } toSide)
                {
                    if (toSide.GetText() != "any")
                        result["to"] = toSide.GetText();   // own / opponent: whose field the monster was summoned to
                }
                else if (condition.summonBy() is { } by)
                {
                    if (result.ContainsKey("by"))
                        throw new ScriptError("one 'by ...' per trigger.");
                    string text = by.GetText();
                    if (by.NUMBER() != null)
                        result["by"] = new JsonObject { ["card"] = int.Parse(text) };
                    else if (by.STRING() != null)
                    {
                        string archetype = text.Trim('"');
                        int code = ArchetypeCatalog.CodeOf(archetype);
                        if (code == 0) throw new ScriptError($"unknown archetype \"{archetype}\" (see Archetypes.json).");
                        result["by"] = new JsonObject { ["archetype"] = code };
                    }
                    else if (text is "effect" or "not_effect" or "own" or "spell" or "trap" or "monster")
                        result["by"] = text;
                    else
                        throw new ScriptError($"'by {text}': use effect, not_effect, own, spell, trap, monster, an archetype \"Name\" or a card id.");
                }
                else
                {
                    string word = condition.GetChild(0).GetText();
                    if (KindWords.Contains(word))
                        result["proper"] = true;   // Summoned as its kind; the card's own kind says which (an Xyz Monster is Xyz Summoned ...)
                    else if (word == "pendulum")
                        result["pendulum"] = true;
                    else if (word == "tribute")
                        result["tributes"] = condition.NUMBER() is { } n ? int.Parse(n.GetText()) : 1;
                    else
                        throw new ScriptError($"'{word}': the summon conditions are from ..., by ..., fusion / synchro / xyz / ritual / link, pendulum, tribute [N].");
                }
            }
            if (from.Count > 0)
                result["from"] = from;
            return result;
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
            if (action.gainAction() != null || action.burnAction() != null || action.damageAction() != null) return "burn";
            if (action.searchAction() != null) return "search";
            if (action.reviveAction() != null) return "revive";
            if (action.sendAction() != null || action.banishFromAction() != null) return "send_to_grave";   // same donor row (Foolish Burial)
            if (action.equipAction() != null) throw new ScriptError("equip(...) is a Spell; it cannot have an 'on ...:' trigger.");
            if (action.gainAtkAction() != null) return "gain_atk";
            if (action.returnAction() != null) return "return_target";
            // several zones (or banished / extra) have no game monster to borrow from: the caller composes them onto any monster with the trigger
            if (action.summonAction() is { } summon) return summon.summonZones().GetText() == "grave" ? "revive" : "special_summon_" + string.Join("_", Zones(summon));
            if (action.salvageAction() != null) return "add_from_grave";
            if (action.banishAction() is { } banishKey) return banishKey.scope().GetText() == "all" ? "banish_all" : "banish_target";
            if (action.negateAction() != null) return "negate";
            if (action.lockTurnAction() != null) return "lock_turn";
            if (action.setAction() != null) return "set_from_deck";   // no game monster to borrow: composed (actionFrom = the set machine)
            if (action.fusionAction() != null) return "fusion_summon";   // composed onto the trigger's row (actionFrom = Polymerization)
            if (action.moveAction() is { } moveKey) return "move_" + moveKey.moveWhat().GetChild(0).GetText();   // no game monster: always composed
            if (action.targetAction() is { } targetKey) return targetKey.GetChild(0).GetText();   // raw, as move: always composed (section 92)
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

        // Triggers Yu-Gi-Oh-Effects can give a card with no game card behind them ("ownTrigger", docs/EffectSystem.md section 61): the phase
        // triggers (its own row in the phase tables, calling the game's handler family for whose phase it is).
        private static bool OwnTriggerKind(string trigger) =>
            trigger == "ignition"   // a row in the field ignition table (table 3), offered by the game's activation menu (section 96)
            || trigger is "standby_phase" or "opponent_standby_phase" or "each_standby_phase" or "end_phase" or "opponent_end_phase" or "each_end_phase"
            || trigger is "grave_end_phase" or "banished_end_phase"   // the game's End Phase "arrived this turn" handler + the plugin's arrival marker (section 79)
            || trigger is "damage_step_battles" or "damage_step_attacks"   // offered after the start-of-Damage-Step evaluator (section 80)
            // start of the Battle Phase: offered before the game's own pass; end: a row in the end-of-Battle-Phase table (section 93)
            || trigger is "battle_phase_start" or "your_battle_phase_start" or "opponent_battle_phase_start" or "battle_phase_end" or "your_battle_phase_end"
            || IsMoveTriggerName(trigger)   // move triggers: offered from the hooked move evaluator with the move reference checked (section 65)
            || trigger == "in_hand"         // "If this card is in your hand: You can ...": the hand-effect id test answers yes (section 66)
            || trigger is "attack_declared" or "opponent_attack_declared" or "attacked"    // offered after the attack evaluator (section 67)
            || trigger is "opponent_special_summoned" or "opponent_summoned" or "monster_special_summoned" or "monster_summoned"   // after the summon-reaction pass (section 68)
            || IsWatchTriggerName(trigger);   // after the leave-the-field / move evaluators (section 91)

        private static bool IsMoveTriggerName(string trigger) =>
            trigger is "sent_to_grave" or "sent_from_field_to_grave" or "destroyed_by_battle" or "banished";

        // on banished(from grave) / on sent_to_grave(from hand, from field, by effect) -> "moveCondition" (Yu-Gi-Oh-Effects MoveMatches).
        private static readonly string[] MoveZones = ["hand", "deck", "grave", "field", "monster", "spelltrap"];
        private static JsonObject CompileMoveConditions(string trigger, EffectScriptParser.SummonConditionsContext conditions)
        {
            var result = new JsonObject();
            var from = new JsonArray();
            foreach (var condition in conditions.summonCondition())
            {
                if (condition.summonFrom() is { } zone)
                {
                    string z = zone.GetText() == "gy" ? "grave" : zone.GetText();
                    if (!MoveZones.Contains(z))
                        throw new ScriptError($"'from {z}' for '{trigger}': the zones are {string.Join(", ", MoveZones)}.");
                    from.Add(z);
                }
                else if (condition.summonBy() is { } by && by.GetText() is "battle" or "effect")
                {
                    if (result.ContainsKey("by"))
                        throw new ScriptError("one 'by ...' per trigger.");
                    result["by"] = by.GetText();
                }
                else
                    throw new ScriptError($"'{condition.GetText()}' for '{trigger}': a move trigger takes from <zone> and by battle | by effect.");
            }
            if (from.Count > 0)
                result["from"] = from;
            return result;
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

            if (action.moveAction() is { } move)
            {
                // Yu-Gi-Oh-Effects' own move step (docs/EffectSystem.md section 86)
                var what = move.moveWhat();
                string dest = move.moveDest().GetText();
                // No donor (docs/EffectSystem.md section 88): a raw row whose slots are engine functions - Yu-Gi-Oh-Effects' Slot0_OwnMove,
                // Target_Generic_FromFilterRow (slot 1, this "filter") and Slot4_ActivationPrompt_Text281 / the multi-target picker (slot 4).
                if (what.GetChild(0).GetText() == "target")
                {
                    var moveFilter = DestroyFilter(what.side()?.GetText() ?? "any", what.selector());
                    string kind = KindOf(what.selector());
                    if (kind == "card") kind = "monster";
                    moveFilter["target"] = true;
                    moveFilter["kind"] = kind == "monster" ? "monster" : "spelltrap";
                    var moveSpec = new JsonObject { ["scope"] = "target", ["to"] = MoveDestination(dest) };
                    if (what.NUMBER() is { } count)
                    {
                        int n = int.Parse(count.GetText());
                        if (n < 1 || n > 5) throw new ScriptError("move(target, N, ...): N is 1 to 5.");
                        if (n > 1) moveSpec["count"] = n;   // up to N targets: the game's multi-target picker (section 86)
                    }
                    return new JsonObject { ["rawRow"] = true, ["filter"] = moveFilter, ["move"] = moveSpec };
                }
                if (what.GetChild(0).GetText() == "self")
                    return new JsonObject { ["rawRow"] = true, ["condition"] = "always", ["move"] = new JsonObject { ["scope"] = "self", ["to"] = MoveDestination(dest) } };
                // picked from the hand / GY when it resolves: the own pick-and-act step with a Deck destination
                string own = dest switch
                {
                    "to_deck" => "toDeck",
                    "to_deck_top" => "toDeckTop",
                    "to_deck_bottom" => "toDeckBottom",
                    "to_extra" => "toExtra",
                    _ => throw new ScriptError($"move(hand / grave, ..., {dest}): use add_to_hand / send_to_grave / banish for that; move picks cards to the Deck or Extra Deck."),
                };
                var fromZones = what.moveFrom().Select(z => z.GetText() == "grave" ? "grave" : "hand").ToList();
                if (own == "toExtra" && fromZones.Contains("hand"))
                    throw new ScriptError("move(hand, ..., to_extra): only GY cards go to the Extra Deck this way.");
                var listMove = new JsonObject { ["rawRow"] = true, ["condition"] = "listHasMatch", ["deck"] = ZonesFilter(what.choices(), fromZones, PlainScans), ["own"] = own };
                if (what.NUMBER() is { } listCount)
                {
                    // up to N, picked one after another (section 95)
                    int n = int.Parse(listCount.GetText());
                    if (n < 1 || n > 10) throw new ScriptError("move(grave / hand, N, ...): N is 1 to 10.");
                    if (n > 1) listMove["ownCount"] = n;
                }
                return listMove;
            }

            if (action.targetAction() is { } targetDo)
            {
                // the game's own target machines run raw (docs/EffectSystem.md section 92); the filter adds the game's own tests for the verb:
                // the battle position can be changed (0x80000) / control can switch (0x20000), and the position asked for
                var targetFilter = DestroyFilter(targetDo.side()?.GetText() ?? "any", targetDo.selector());
                string targetKind = KindOf(targetDo.selector());
                if (targetKind is not ("card" or "monster"))
                    throw new ScriptError($"{targetDo.GetChild(0).GetText()}: only a monster can be targeted.");
                targetFilter["target"] = true;
                targetFilter["kind"] = "monster";
                string verb = targetDo.GetChild(0).GetText();
                if (verb == "take_control") targetFilter["canChangeControl"] = true;
                else if (verb is "change_position" or "set_face_down") targetFilter["canChangePosition"] = true;
                if (targetDo.targetPosition() is { } position)
                    targetFilter["position"] = position.GetText();
                // "change its battle position" / an ATK change / a negation: a face-up monster (the position machine flips a face-down one)
                if (verb is "change_atk" or "negate_effects" || (verb == "change_position" && targetFilter["position"] == null))
                    targetFilter["faceUp"] = true;
                var targetStep = new JsonObject
                {
                    ["rawRow"] = true,
                    ["filter"] = targetFilter,
                    ["targetDo"] = verb switch
                    {
                        "change_position" => "position", "set_face_down" => "faceDown", "take_control" => "control", "negate_effects" => "negate", _ => "atk",
                    },
                };
                if (verb == "change_atk")
                {
                    int amount = int.Parse(targetDo.NUMBER().GetText());
                    if (amount == 0 || amount > 32767) throw new ScriptError("change_atk: the amount is 1 to 32767 (with '-' for a loss).");
                    targetStep["amount"] = targetDo.children.Any(c => c.GetText() == "-") ? -amount : amount;
                }
                return targetStep;
            }

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
                var zones = Zones(summon);
                if (zones.Count > 1 && zones.Any(z => z is "self" or "self_grave"))
                    throw new ScriptError("special_summon(self) / special_summon(self_grave) cannot be joined with other zones.");
                if (zones.Distinct().Count() != zones.Count)
                    throw new ScriptError("special_summon: a zone is listed twice.");
                if (zones[0] == "self")
                {
                    if (summon.choices() != null) throw new ScriptError("special_summon(self) takes no selector.");
                    return new JsonObject { ["from"] = SummonSelfSource, ["condition"] = "always", ["require"] = new JsonArray("inHand", "canSummonSelf") };
                }
                if (zones[0] == "self_grave")
                {
                    if (summon.choices() != null) throw new ScriptError("special_summon(self_grave) takes no selector.");
                    return new JsonObject { ["from"] = SummonSelfGraveSource, ["condition"] = "always", ["require"] = new JsonArray("canSummonSelfFromGrave") };
                }
                if (zones.Count == 1 && zones[0] is "hand" or "deck" or "grave")   // one zone keeps its proven game card
                    return zones[0] switch
                    {
                        "hand" => new JsonObject { ["from"] = SummonHandSource, ["deck"] = DeckFilter(summon.choices(), "handSummon") },
                        "deck" => new JsonObject { ["from"] = SummonDeckSource, ["condition"] = "listHasMatch", ["deck"] = DeckFilter(summon.choices(), "deckSummon") },
                        _ => new JsonObject { ["from"] = ReviveSource, ["deck"] = DeckFilter(summon.choices(), "graveSummon") },
                    };
                // Several zones, the banished cards or the Extra Deck: one list over every zone (one "or" alternative per zone and selector, each
                // with its zone's "can be Special Summoned" scan) on Unexpected Dai's row, picked and summoned by the plugin's own step (the game's
                // summon queue takes a card from any zone; docs/EffectSystem.md section 57).
                return new JsonObject { ["from"] = SummonDeckSource, ["condition"] = "listHasMatch", ["deck"] = ZonesFilter(summon.choices(), zones), ["own"] = "summon" };
            }

            if (action.salvageAction() is { } salvage)
            {
                bool banished = salvage.GetChild(2).GetText() == "banished";
                int salvageCount = salvage.NUMBER() is { } n ? int.Parse(n.GetText()) : 1;
                if (salvageCount < 1 || salvageCount > 10) throw new ScriptError("add_to_hand(..., N): N is 1 to 10.");
                // up to N: Yu-Gi-Oh-Effects' own pick-and-act step, round N times (its GY / banished movers; section 95)
                if (salvageCount > 1)
                    return new JsonObject { ["rawRow"] = true, ["condition"] = "listHasMatch", ["deck"] = ZonesFilter(salvage.choices(), [banished ? "banished" : "grave"], PlainScans), ["own"] = "toHand", ["ownCount"] = salvageCount };
                return banished
                    ? new JsonObject { ["from"] = SalvageBanishedSource, ["deck"] = DeckFilter(salvage.choices(), "banished") }
                    : new JsonObject { ["from"] = SalvageSource, ["deck"] = DeckFilter(salvage.choices(), "grave") };
            }

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
            {
                if (draw.side() != null)
                    throw new ScriptError("draw(N, player) can only be chained with other draw / gain_lp / damage actions so far.");
                return new JsonObject { ["from"] = DrawSource, ["draw"] = int.Parse(draw.NUMBER().GetText()) };
            }

            if (action.searchAction() is { } search)
                return new JsonObject { ["from"] = SearchSource, ["deck"] = DeckFilter(search.choices(), "deck") };
            if (action.setAction() is { } set)
                return new JsonObject { ["from"] = DrawSource, ["actionFrom"] = SetSource, ["deck"] = DeckFilter(set.choices(), "deck") };
            if (action.fusionAction() is { } fusion)
            {
                // the fusion Spells share one machine (Slot0_FusionSummon_StateMachine) and pick the material route by the card's id:
                // Polymerization = hand / field, Dragon's Mirror = banish from the field / GY (section 94); the list row is ours
                int fusionFrom = fusion.fusionRoute()?.GetText() == "banish_materials" ? FusionBanishSource : FusionSource;
                return new JsonObject { ["from"] = fusionFrom, ["deck"] = DeckFilter(fusion.choices(), "fusion") };
            }

            if (action.reviveAction() is { } revive)
            {
                string scan = revive.graveyard().GetText() switch
                {
                    "grave" => "graveSummon",
                    "opponent_grave" => "opponentGrave",
                    _ => "bothGravesSummon",
                };
                return new JsonObject { ["from"] = ReviveSource, ["deck"] = DeckFilter(revive.choices(), scan) };
            }

            if (action.sendAction() is { } send)
                return send.GetChild(2).GetText() == "deck"
                    ? new JsonObject { ["from"] = SendSource, ["deck"] = DeckFilter(send.choices(), "deck") }
                    : OwnMove("toGrave", DeckFilter(send.choices(), "hand"));

            if (action.banishFromAction() is { } banishFrom)
                return OwnMove("banish", DeckFilter(banishFrom.choices(), banishFrom.GetChild(2).GetText()));

            if (action.gainAction()?.side() != null)
                throw new ScriptError("gain_lp(N, player) can only be chained with other draw / gain_lp / damage actions so far.");
            if (action.gainAction() is { } gain)
                return new JsonObject { ["from"] = GainSource, ["lp"] = new JsonObject { ["gain"] = int.Parse(gain.NUMBER().GetText()) } };

            if (action.lockTurnAction() is { } lockTurn)
            {
                string what = lockTurn.lockTurnArg().GetText();
                if (!LockTurnSources.TryGetValue(what, out int marker))
                    throw new ScriptError($"lock_turn: unknown '{what}' ({string.Join(", ", LockTurnSources.Keys)}).");
                var turn = new JsonObject { ["from"] = LockTurnSpellRow, ["condition"] = "always" };
                if (marker != LockTurnSpellRow)
                    turn["actionFrom"] = marker;
                return turn;
            }
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
        // Several alternatives ("monster where race = Warrior or monster where race = Dragon"): the first is the filter, the others go in
        // "or" with the same scan; Yu-Gi-Oh-Effects runs one scan per alternative and keeps each card once.
        private static List<string> Zones(EffectScriptParser.SummonActionContext summon) =>
            summon.summonZones().summonZone().Select(z => z.GetText()).ToList();

        // The "can be Special Summoned" list scan of each zone (Yu-Gi-Oh-Effects BuildDeckFilter; banishedSummon / extraSummon are the game's own
        // scans for Return from the Different Dimension and Starlight Road).
        private static readonly Dictionary<string, string> SummonScans = new()
        {
            ["hand"] = "handSummon", ["deck"] = "deckSummon", ["grave"] = "graveSummon", ["banished"] = "banishedSummon", ["extra"] = "extraSummon",
        };

        // One filter over several zones: every selector in every zone, the first one the filter itself and the rest its "or" alternatives.
        // The plain list scan of each zone (any matching card, no summon check): a move picks from these.
        private static readonly Dictionary<string, string> PlainScans = new() { ["hand"] = "hand", ["grave"] = "grave", ["deck"] = "deck", ["banished"] = "banished" };

        private static JsonObject ZonesFilter(EffectScriptParser.ChoicesContext? choices, List<string> zones, Dictionary<string, string>? scans = null)
        {
            scans ??= SummonScans;
            EffectScriptParser.SelectorContext?[] selectors = choices?.selector() is { Length: > 0 } list ? list : [null];
            var all = zones.SelectMany(z => selectors.Select(s => DeckFilter(s, scans[z]))).ToList();
            var deck = all[0];
            if (all.Count > 1)
                deck["or"] = new JsonArray(all.Skip(1).Select(f => (JsonNode)f).ToArray());
            return deck;
        }

        // A raw pick cost (Yu-Gi-Oh-Effects "costPick", docs/EffectSystem.md section 90): the verb's game cost machine runs as this card, its
        // count from the hooks, its candidates from this filter (the plugin sets the scan the machine's picker asks: one hand index, own field).
        private static JsonObject PickCostJson(string verb, string number, EffectScriptParser.SelectorContext? selector, string scan)
        {
            int count = int.Parse(number);
            if (count < 1 || count > 5) throw new ScriptError($"{verb}: 1 to 5 cards.");
            if (verb == "tribute" && KindOf(selector) is "spell" or "trap") throw new ScriptError("tribute: monsters only.");
            return new JsonObject { ["verb"] = verb, ["raw"] = true, ["count"] = count, ["deck"] = DeckFilter(selector, scan) };
        }

        private static JsonObject DeckFilter(EffectScriptParser.ChoicesContext? choices, string scan)
        {
            var selectors = choices?.selector() ?? [];
            var deck = DeckFilter(selectors.FirstOrDefault(), scan);
            if (selectors.Length > 1)
                deck["or"] = new JsonArray(selectors.Skip(1).Select(s => (JsonNode)DeckFilter(s, scan)).ToArray());
            return deck;
        }

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
            // "except <this card>" unless the selector says 'including self' (Yu-Gi-Oh-Effects: the row's name test, when no archetype / card
            // filter already uses it).
            if (selector?.includingSelf() == null)
                deck["exceptSelf"] = true;
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
