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
                var body = tree.effect()?.effectBody() ?? tree.effectBody();
                var actions = body.chain().action();
                var compiled = CompileAction(actions[^1]);
                if (body.limit() != null)
                    compiled["oncePerTurn"] = true;
                if (body.trigger() is { } trigger)
                {
                    // A monster effect: borrow a vanilla MONSTER with this trigger and this action instead of a Normal Spell.
                    string name = trigger.triggerName().GetText();
                    compiled["from"] = TriggerSource(name, ActionKey(actions[^1]));
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
                return new Result { Ok = true, Compiled = compiled };
            }
            catch (ScriptError e)
            {
                return new Result { Ok = false, Error = e.Message };
            }
        }

        // trigger_sources.json (next to WolfEx, made by docs\effect-scriptsuild_effect_reference.py): trigger -> action -> game cards to borrow from.
        private static JsonNode? _sources;

        private static string ActionKey(EffectScriptParser.ActionContext action)
        {
            if (action.drawAction() != null) return "draw";
            if (action.gainAction() != null || action.burnAction() != null) return "burn";   // the life point handler takes any amount, either direction
            if (action.searchAction() != null) return "search";
            if (action.reviveAction() != null) return "revive";
            if (action.sendAction() != null) return "send_to_grave";
            var destroy = action.destroyAction();
            return destroy.scope().GetText() == "all" ? "destroy_all" : "destroy_target";
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

        private static JsonObject CompileAction(EffectScriptParser.ActionContext action)
        {
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
