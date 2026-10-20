using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WolfEx
{
    /// <summary>
    /// BOB for pages (docs/PageScript.md): a page written as a short script, compiled here into the version 2 page file Yu-Gi-Oh-RIX already
    /// runs (docs/PageEngine.md) plus, for "button on ..." lines, a menu file. The plugin never sees the script, the same way EffectScript
    /// works for effects. Widget fields the compiler doesn't know pass straight through ("name value"), so a widget RIX learns later can be
    /// written before this file knows about it. <see cref="Decompile"/> turns a version 2 page file back into a script.
    /// </summary>
    internal static class PageScript
    {
        public sealed class Error(int line, int column, string message) : Exception($"line {line}:{column} {message}")
        {
            public int Line { get; } = line;
            public int Column { get; } = column;
        }

        public sealed record Result(JsonObject Page, JsonObject? Menus);

        /// <summary>Indented output; the resolver is needed to write the values the compiler creates.</summary>
        public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver() };

        // script word -> the page file's "kind"
        private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase)
        {
            ["menu"] = "menu", ["slider"] = "slider", ["digits"] = "digits", ["card"] = "cardInfo", ["cardInfo"] = "cardInfo",
            ["series"] = "seriesTabs", ["seriesTabs"] = "seriesTabs", ["characters"] = "characters", ["decks"] = "decks",
            ["deck"] = "deckInfo", ["deckInfo"] = "deckInfo", ["cards"] = "cardGrid", ["cardGrid"] = "cardGrid", ["image"] = "image", ["text"] = "text",
            ["user"] = "user", ["related"] = "relatedCards", ["relatedCards"] = "relatedCards", ["box"] = "cardBox", ["cardBox"] = "cardBox",
            ["deckcards"] = "deckView", ["deckView"] = "deckView", ["progress"] = "progress", ["option"] = "option",
            ["pack"] = "pack", ["tag"] = "tag", ["spinner"] = "spinner",
        };

        // the field a value written straight after the widget word fills ("text "Hello"", "digits 4", "card 4007")
        private static readonly Dictionary<string, string> Positional = new()
        {
            ["menu"] = "id", ["slider"] = "label", ["digits"] = "count", ["cardInfo"] = "card", ["deckInfo"] = "deck",
            ["cardGrid"] = "cards", ["cardBox"] = "cards", ["image"] = "resource", ["text"] = "text", ["characters"] = "series",
            ["deckView"] = "deck", ["progress"] = "value", ["option"] = "label", ["pack"] = "pack", ["tag"] = "text",
        };

        // fields written in any case in a script -> their spelling in the page file
        private static readonly string[] KnownFields =
        [
            "id", "x", "y", "width", "height", "items", "source", "spacing", "action", "bind", "min", "max", "default", "onChange", "description",
            "label", "count", "card", "series", "deck", "side", "cards", "filterX", "filterY", "resource", "sprite", "alpha", "text", "textSize",
            "align", "colour", "visible", "enabled", "values", "toggle", "title", "cols", "rows", "cellWidth", "cellHeight", "gap", "frame",
            "mainLimit", "arrows", "valueWidth", "labelWidth", "pack",
        ];

        public static readonly string[] Widgets = ["menu", "slider", "digits", "card", "series", "characters", "decks", "deck", "cards", "image", "text", "user", "related", "box", "deckcards", "progress", "option", "pack", "tag", "spinner"];
        public static readonly string[] Actions = ["page", "back", "goto", "call", "quit", "set", "toggle", "add", "show_card", "reset_filter", "message", "confirm", "video", "popup", "if",
            "give_dp", "take_dp", "give_card", "take_card", "unlock", "lock", "unlock_character", "sound"];
        public static readonly string[] Keywords = ["header", "button", "edit", "hidden", "value", "use", "choice", "rows", "on", "at", "size", "bind", "default", "description", "enabled", "onchange", "yes", "else", "and", "not", "key", "true", "false"];

        // ---------------------------------------------------------------- tokens

        private enum T { Word, Str, Num, Ref, Punct, End }

        private readonly record struct Token(T Kind, string Text, int Line, int Column);

        private static List<Token> Lex(string source)
        {
            var tokens = new List<Token>();
            int i = 0, line = 1, lineStart = 0;
            while (i < source.Length)
            {
                char c = source[i];
                int column = i - lineStart;
                if (c == '\n') { line++; lineStart = ++i; continue; }
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    while (i < source.Length && source[i] != '\n') i++;
                    continue;
                }
                if (c == '"')
                {
                    var text = new StringBuilder();
                    i++;
                    while (true)
                    {
                        if (i >= source.Length || source[i] == '\n')
                            throw new Error(line, column, "a text is missing its closing \"");
                        if (source[i] == '"') { i++; break; }
                        if (source[i] == '\\' && i + 1 < source.Length)
                        {
                            char e = source[++i];
                            text.Append(e == 'n' ? '\n' : e);
                            i++;
                            continue;
                        }
                        text.Append(source[i++]);
                    }
                    tokens.Add(new Token(T.Str, text.ToString(), line, column));
                    continue;
                }
                if (char.IsDigit(c) || (c == '-' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
                {
                    int start = i++;
                    while (i < source.Length && (char.IsDigit(source[i]) || source[i] == '.')) i++;
                    tokens.Add(new Token(T.Num, source[start..i], line, column));
                    continue;
                }
                if (char.IsLetter(c) || c == '_')
                {
                    int start = i;
                    while (i < source.Length && (char.IsLetterOrDigit(source[i]) || source[i] == '_' || source[i] == '.')) i++;
                    string word = source[start..i];
                    // var:name / config:Section/Key - the name runs to a space or punctuation (config keys hold '-' and '/')
                    if (i < source.Length && source[i] == ':' && (word.Equals("var", StringComparison.OrdinalIgnoreCase) || word.Equals("config", StringComparison.OrdinalIgnoreCase) || word.Equals("game", StringComparison.OrdinalIgnoreCase) || word.Equals("value", StringComparison.OrdinalIgnoreCase)))
                    {
                        int nameStart = ++i;
                        while (i < source.Length && !char.IsWhiteSpace(source[i]) && ",;}()[]=!<>".IndexOf(source[i]) < 0)
                        {
                            if (source[i] == '{')
                            {
                                // a value inside the name: game:owned.{var:card}
                                for (int depth = 0; i < source.Length; i++)
                                {
                                    if (source[i] == '{') depth++;
                                    else if (source[i] == '}' && --depth == 0) break;
                                    else if (source[i] == '\n') throw new Error(line, column, "a '{' in a name is missing its '}'");
                                }
                                if (i >= source.Length)
                                    throw new Error(line, column, "a '{' in a name is missing its '}'");
                            }
                            i++;
                        }
                        if (i == nameStart)
                            throw new Error(line, column, $"{word}: needs a name");
                        tokens.Add(new Token(T.Ref, word.ToLowerInvariant() + ":" + source[nameStart..i], line, column));
                        continue;
                    }
                    tokens.Add(new Token(T.Word, word, line, column));
                    continue;
                }
                if (c == '-' && i + 1 < source.Length && source[i + 1] == '>') { tokens.Add(new Token(T.Punct, "->", line, column)); i += 2; continue; }
                if (c == '!' && i + 1 < source.Length && source[i + 1] == '=') { tokens.Add(new Token(T.Punct, "!=", line, column)); i += 2; continue; }
                if (c == '>' && i + 1 < source.Length && source[i + 1] == '=') { tokens.Add(new Token(T.Punct, ">=", line, column)); i += 2; continue; }
                if (c == '<') { tokens.Add(new Token(T.Punct, "<", line, column)); i++; continue; }
                if ("{}()[],;=".IndexOf(c) >= 0) { tokens.Add(new Token(T.Punct, c.ToString(), line, column)); i++; continue; }
                throw new Error(line, column, $"'{c}' is not part of the language");
            }
            tokens.Add(new Token(T.End, "", line, i - lineStart));
            return tokens;
        }

        // ---------------------------------------------------------------- compiler

        /// <summary>Compiles a page script. <paramref name="pageName"/> is the file name (pages\&lt;name&gt;.json), used for menu button keys.</summary>
        public static Result Compile(string source, string pageName) => new Parser(Lex(source), pageName).Script();

        private sealed class Parser(List<Token> tokens, string pageName)
        {
            private int _at;
            private readonly HashSet<string> _ids = new(StringComparer.OrdinalIgnoreCase);

            private Token Peek => tokens[_at];
            private Token Next() => tokens[_at++];
            private bool IsWord(string word) => Peek.Kind == T.Word && Peek.Text.Equals(word, StringComparison.OrdinalIgnoreCase);
            private bool IsPunct(string p) => Peek.Kind == T.Punct && Peek.Text == p;
            private Error Fail(string message) => new(Peek.Line, Peek.Column, message);
            private static string Shown(Token t) => t.Kind == T.End ? "the end of the script" : t.Kind == T.Str ? $"\"{t.Text}\"" : $"'{t.Text}'";

            private bool Accept(string p)
            {
                if (!IsPunct(p)) return false;
                _at++;
                return true;
            }

            private bool AcceptWord(string word)
            {
                if (!IsWord(word)) return false;
                _at++;
                return true;
            }

            private void Expect(string p)
            {
                if (!Accept(p))
                    throw Fail($"expected '{p}' but found {Shown(Peek)}");
            }

            private string ExpectString(string what)
            {
                if (Peek.Kind != T.Str)
                    throw Fail($"expected {what} (a text in quotes) but found {Shown(Peek)}");
                return Next().Text;
            }

            private string ExpectRef(string what, bool game = false)
            {
                if (Peek.Kind != T.Ref)
                    throw Fail($"expected {what} (var:name or config:Section/Key) but found {Shown(Peek)}");
                if (!game && Peek.Text.StartsWith("game:", StringComparison.Ordinal))
                    throw Fail($"{Peek.Text} can only be read; use give_dp / take_dp / give_card ... to change the game");
                return Next().Text;
            }

            private double ExpectNumber(string what)
            {
                if (Peek.Kind != T.Num)
                    throw Fail($"expected {what} (a number) but found {Shown(Peek)}");
                return double.Parse(Next().Text, CultureInfo.InvariantCulture);
            }

            private static JsonNode Number(double value) => value == Math.Floor(value) && Math.Abs(value) < int.MaxValue ? JsonValue.Create((int)value) : JsonValue.Create(value);

            public Result Script()
            {
                var page = new JsonObject { ["version"] = 2 };
                var elements = new JsonArray();
                var buttons = new JsonArray();
                var edits = new JsonArray();
                var values = new JsonObject();
                var uses = new JsonArray();
                bool header = false;
                while (Peek.Kind != T.End)
                {
                    if (Accept(";"))
                        continue;
                    if (AcceptWord("header"))
                    {
                        if (header)
                            throw Fail("the page already has a header");
                        page["header"] = ExpectString("the header text");
                        header = true;
                        Expect(";");
                    }
                    else if (AcceptWord("button"))
                        buttons.Add(MenuButton(buttons.Count));
                    else if (AcceptWord("edit"))
                        edits.Add(GameButtonEdit());
                    else if (AcceptWord("value"))
                    {
                        // value name = "text";   (a number is fine too) - read as {value:name}, or by name inside {= ...}
                        if (Peek.Kind != T.Word)
                            throw Fail($"expected the value's name but found {Shown(Peek)}");
                        string name = Next().Text;
                        if (values.ContainsKey(name))
                            throw Fail($"the value {name} is already defined");
                        Expect("=");
                        values[name] = Peek.Kind is T.Str or T.Num ? Next().Text : throw Fail($"expected the value's text (in quotes) or a number but found {Shown(Peek)}");
                        Expect(";");
                    }
                    else if (AcceptWord("use"))
                    {
                        // use "card-store-prices";   another page file's values, here too
                        uses.Add(ExpectName("the page file's name"));
                        Expect(";");
                    }
                    else if (Peek.Kind == T.Word && Kinds.TryGetValue(Peek.Text, out string? kind))
                    {
                        Next();
                        elements.Add(Widget(kind));
                    }
                    else
                        throw Fail($"expected header, value, use, button, edit or a widget ({string.Join(", ", Widgets)}) but found {Shown(Peek)}");
                }
                if (!header)
                    page["header"] = pageName;
                if (uses.Count > 0)
                    page["use"] = uses;
                if (values.Count > 0)
                    page["values"] = values;
                page["elements"] = elements;
                JsonObject? menus = null;
                if (buttons.Count > 0 || edits.Count > 0)
                {
                    menus = [];
                    if (buttons.Count > 0) menus["buttons"] = buttons;
                    if (edits.Count > 0) menus["edit"] = edits;
                }
                return new Result(page, menus);
            }

            // button on main "Label" "description" [page name] [key "k"] [if ...] -> action;   (a button on a game menu: menus\<page>.json)
            private JsonObject MenuButton(int index)
            {
                if (!AcceptWord("on"))
                    throw Fail("a button outside a menu needs 'on <game menu>', e.g. button on main \"My Page\" -> page(\"my-page\");");
                if (Peek.Kind != T.Word && Peek.Kind != T.Str)
                    throw Fail($"expected the game menu's name (main, options, ...) but found {Shown(Peek)}");
                var button = new JsonObject { ["key"] = $"bob.{pageName}.{index + 1}", ["menu"] = Next().Text };
                button["label"] = ExpectString("the button's label");
                if (Peek.Kind == T.Str)
                    button["description"] = Next().Text;
                while (!Accept(";"))
                {
                    if (AcceptWord("page"))
                        button["page"] = Peek.Kind is T.Word or T.Str ? Next().Text : throw Fail("expected the game menu page's name");
                    else if (AcceptWord("key"))
                        button["key"] = ExpectString("the button's key");
                    else if (AcceptWord("description"))
                        button["description"] = ExpectString("the description");
                    else if (Accept("->"))
                        button["action"] = Action();
                    else if (Peek.Kind == T.End)
                        throw Fail("expected ';' after the button");
                    else
                        throw Fail($"expected page, key, description, -> or ';' but found {Shown(Peek)}");
                }
                if (button["action"] == null)
                    throw Fail("the button needs '-> action'");
                return button;
            }

            // edit cardShop ["Label" ["description"]] [hidden] [-> action];   one of the game's own main menu buttons (docs/MenuFiles.md names)
            private JsonObject GameButtonEdit()
            {
                if (Peek.Kind != T.Word && Peek.Kind != T.Str)
                    throw Fail($"expected one of the game's buttons (cardShop, deckEditor, quitGame ...) but found {Shown(Peek)}");
                var edit = new JsonObject { ["item"] = Next().Text };
                if (Peek.Kind == T.Str)
                {
                    edit["label"] = Next().Text;
                    if (Peek.Kind == T.Str)
                        edit["description"] = Next().Text;
                }
                while (!Accept(";"))
                {
                    if (AcceptWord("hidden"))
                        edit["hidden"] = true;
                    else if (AcceptWord("description"))
                        edit["description"] = ExpectString("the description");
                    else if (Accept("->"))
                        edit["action"] = Action();
                    else
                        throw Fail($"expected a label, hidden, -> or ';' but found {Shown(Peek)}");
                }
                return edit;
            }

            private JsonObject Widget(string kind)
            {
                var widget = new JsonObject { ["kind"] = kind };
                if (Positional.TryGetValue(kind, out string? field) && (Peek.Kind is T.Str or T.Num or T.Ref || (Peek.Kind == T.Word && !IsKeywordHere())))
                    widget[field] = Value();
                Fields(widget, kind == "menu");
                if (kind == "menu")
                {
                    if (widget["id"] is JsonValue id && !_ids.Add(id.ToString()))
                        throw Fail($"two menus are called \"{id}\"");
                    if (widget["items"] == null)
                        widget["items"] = new JsonArray();
                }
                return widget;
            }

            private bool IsKeywordHere() => IsWord("at") || IsWord("size") || IsWord("if") || IsWord("rows") || KnownFields.Any(IsWord);

            // the fields after a widget or a menu item, up to ';' (or a menu's { items })
            private void Fields(JsonObject target, bool items)
            {
                while (true)
                {
                    if (Accept(";"))
                        return;
                    if (items && IsPunct("{"))
                    {
                        Next();
                        target["items"] = Items(target);
                        Accept(";");
                        return;
                    }
                    if (Peek.Kind == T.End || IsPunct("}"))
                        throw Fail($"expected ';' to end the {(string?)target["kind"] ?? "item"}");
                    if (AcceptWord("at"))
                    {
                        target["x"] = Number(ExpectNumber("x"));
                        Expect(",");
                        target["y"] = Number(ExpectNumber("y"));
                    }
                    else if (AcceptWord("size"))
                    {
                        target["width"] = Number(ExpectNumber("the width"));
                        Expect(",");
                        target["height"] = Number(ExpectNumber("the height"));
                    }
                    else if (AcceptWord("if"))
                        target["visible"] = Condition();
                    else if (AcceptWord("enabled"))
                        target["enabled"] = Condition();
                    else if (Accept("->"))
                        target["action"] = Action();
                    else if (AcceptWord("onchange"))
                    {
                        Accept("->");
                        target["onChange"] = Action();
                    }
                    else if (AcceptWord("rows"))
                    {
                        // rows 2 = a card box's grid height; rows "plugin.list" = a plugin fills the menu
                        if (Peek.Kind == T.Str)
                            target["source"] = Next().Text;
                        else
                            target["rows"] = Value();
                    }
                    else if (Peek.Kind == T.Word)
                    {
                        string name = Next().Text;
                        string key = KnownFields.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? name;
                        target[key] = Value();
                    }
                    else
                        throw Fail($"expected a field (at, size, bind, if, -> ...) but found {Shown(Peek)}");
                }
            }

            // { button "Start" -> page("next"); choice "Difficulty" ["Easy", "Hard"] bind var:d; toggle "Music" bind var:m; }
            private JsonArray Items(JsonObject menu)
            {
                var items = new JsonArray();
                while (!Accept("}"))
                {
                    if (Accept(";"))
                        continue;
                    var item = new JsonObject();
                    if (AcceptWord("button"))
                    {
                        item["label"] = ExpectString("the button's label");
                        if (Peek.Kind == T.Str) item["description"] = Next().Text;
                    }
                    else if (AcceptWord("choice"))
                    {
                        item["label"] = ExpectString("the choice's label");
                        if (Peek.Kind == T.Str) item["description"] = Next().Text;
                        if (!IsPunct("["))
                            throw Fail("a choice needs its values: [\"Easy\", \"Hard\"] (or \"Label\" = \"value\" pairs)");
                        item["values"] = Choices();
                    }
                    else if (AcceptWord("toggle"))
                    {
                        item["label"] = ExpectString("the toggle's label");
                        if (Peek.Kind == T.Str) item["description"] = Next().Text;
                        item["toggle"] = true;
                    }
                    else if (AcceptWord("rows"))
                    {
                        // rows "plugin.list" -> action;  = the menu is filled by a plugin's list
                        menu["source"] = ExpectString("the list source a plugin registered");
                        if (Accept("->"))
                            menu["action"] = Action();
                        Expect(";");
                        continue;
                    }
                    else if (Peek.Kind == T.End)
                        throw Fail("the menu is missing its closing '}'");
                    else
                        throw Fail($"expected button, choice, toggle or rows inside a menu but found {Shown(Peek)}");
                    Fields(item, false);
                    items.Add(item);
                }
                return items;
            }

            private JsonArray Choices()
            {
                Expect("[");
                var values = new JsonArray();
                while (!Accept("]"))
                {
                    string label = Peek.Kind is T.Num or T.Word ? Next().Text : ExpectString("a value");
                    if (Accept("="))
                        values.Add(new JsonObject { ["label"] = label, ["value"] = Peek.Kind is T.Num or T.Word ? Next().Text : ExpectString("the value") });
                    else
                        values.Add(label);
                    if (!IsPunct("]"))
                        Expect(",");
                }
                return values;
            }

            // a value after a field name: "text", 12, var:x, true / false, a bare word (left, owned), [a, b], or an action in { }
            private JsonNode Value()
            {
                var t = Peek;
                switch (t.Kind)
                {
                    case T.Str: Next(); return JsonValue.Create(t.Text);
                    case T.Ref: Next(); return JsonValue.Create(t.Text);
                    case T.Num: Next(); return Number(double.Parse(t.Text, CultureInfo.InvariantCulture));
                    case T.Word:
                        Next();
                        if (t.Text.Equals("true", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(true);
                        if (t.Text.Equals("false", StringComparison.OrdinalIgnoreCase)) return JsonValue.Create(false);
                        return JsonValue.Create(t.Text);
                }
                if (IsPunct("["))
                {
                    Next();
                    var list = new JsonArray();
                    while (!Accept("]"))
                    {
                        list.Add(Value());
                        if (!IsPunct("]"))
                            Expect(",");
                    }
                    return list;
                }
                throw Fail($"expected a value but found {Shown(t)}");
            }

            // var:x | not var:x | var:x = "v" | var:x != "v", joined with 'and'
            private JsonNode Condition()
            {
                var parts = new JsonArray();
                do
                {
                    bool not = AcceptWord("not");
                    string reference = ExpectRef("a variable, config line or game value", game: true);
                    int colon = reference.IndexOf(':');
                    var part = new JsonObject { [reference[..colon]] = reference[(colon + 1)..] };
                    if (Accept("="))
                        part["equals"] = ValueText();
                    else if (Accept("!="))
                    {
                        part["equals"] = ValueText();
                        not = !not;
                    }
                    else if (Accept(">="))
                        part["atLeast"] = ExpectNumberOrRef();
                    else if (Accept("<"))
                    {
                        part["atLeast"] = ExpectNumberOrRef();
                        not = !not;
                    }
                    if (not)
                        part["not"] = true;
                    parts.Add(part);
                }
                while (AcceptWord("and"));
                return parts.Count == 1 ? parts[0]!.DeepClone() : parts;
            }

            // the right side of >= / <: a number, a value (game:price.{var:card}, sent as "{...}" for RIX to expand) or a text
            private JsonNode ExpectNumberOrRef() => Amount("a number or a value");

            // an amount: a number, or "{var:x}" text RIX expands when the action runs
            private JsonNode Amount(string what) => Peek.Kind == T.Num ? Number(ExpectNumber(what))
                : Peek.Kind == T.Ref ? JsonValue.Create("{" + Next().Text + "}")
                : Peek.Kind == T.Str ? JsonValue.Create(Next().Text) : throw Fail($"expected {what} but found {Shown(Peek)}");

            // give_dp(500) / take_dp(500) [-> when it worked] [else when the profile has too little]
            private JsonObject Transaction(JsonObject step)
            {
                if (Accept("->") || AcceptWord("then"))
                    step["then"] = Action();
                if (AcceptWord("else"))
                    step["else"] = Action();
                return step;
            }

            private JsonObject CardStep(string key)
            {
                Expect("(");
                var step = new JsonObject { [key] = Amount("a card id") };
                if (Accept(","))
                    step["copies"] = Number(ExpectNumber("the copies"));
                Expect(")");
                return step;
            }

            private string ValueText() => Peek.Kind is T.Str or T.Num or T.Word or T.Ref ? Next().Text : throw Fail($"expected a value but found {Shown(Peek)}");

            // one action, or { action; action; } run in order
            private JsonNode Action()
            {
                if (Accept("{"))
                {
                    var steps = new JsonArray();
                    while (!Accept("}"))
                    {
                        if (Accept(";"))
                            continue;
                        if (Peek.Kind == T.End)
                            throw Fail("the actions are missing their closing '}'");
                        steps.Add(Step());
                    }
                    return steps.Count == 1 ? steps[0]!.DeepClone() : steps;
                }
                return Step();
            }

            private JsonObject Step()
            {
                if (Peek.Kind != T.Word)
                    throw Fail($"expected an action ({string.Join(", ", Actions)}) but found {Shown(Peek)}");
                var t = Next();
                string name = t.Text.ToLowerInvariant();
                switch (name)
                {
                    case "back": Bare(); return new JsonObject { ["back"] = true };
                    case "quit": Bare(); return new JsonObject { ["quit"] = true };
                    case "reset_filter": Bare(); return new JsonObject { ["resetFilter"] = true };
                    case "page": return new JsonObject { ["page"] = OneArg(() => ExpectName("the page's name")) };
                    case "video": return new JsonObject { ["video"] = OneArg(() => ExpectName("the video's name")) };
                    case "call": return new JsonObject { ["call"] = OneArg(() => ExpectString("the plugin action's name")) };
                    case "message": return new JsonObject { ["message"] = OneArg(() => ExpectString("the message")) };
                    case "show_card": return new JsonObject { ["showCard"] = OneArg(ValueText) };
                    case "goto":
                    {
                        Expect("(");
                        JsonNode target = Peek.Kind == T.Num ? Number(ExpectNumber("a screen id")) : JsonValue.Create(ExpectName("the screen's name"));
                        Expect(")");
                        return new JsonObject { ["goto"] = target };
                    }
                    case "set":
                    {
                        Expect("(");
                        string target = ExpectRef("what to set");
                        Expect(",");
                        string value = ValueText();
                        Expect(")");
                        return new JsonObject { ["set"] = target, ["value"] = value };
                    }
                    case "toggle": return new JsonObject { ["toggle"] = OneArg(() => ExpectRef("what to toggle")) };
                    case "add":
                    {
                        Expect("(");
                        string target = ExpectRef("what to add to");
                        double amount = 1;
                        if (Accept(","))
                            amount = ExpectNumber("the amount");
                        Expect(")");
                        return new JsonObject { ["add"] = target, ["value"] = Number(amount) };
                    }
                    case "confirm":
                    {
                        // confirm("Sure?") -> action;   (the action runs on Yes)
                        string text = OneArg(() => ExpectString("the question"));
                        var step = new JsonObject { ["confirm"] = text };
                        if (Accept("->") || AcceptWord("yes"))
                            step["yes"] = Action();
                        return step;
                    }
                    case "if":
                    {
                        // if var:x = "1" -> action else action
                        var step = new JsonObject { ["if"] = Condition() };
                        if (!Accept("->"))
                            AcceptWord("then");
                        step["then"] = Action();
                        if (AcceptWord("else"))
                            step["else"] = Action();
                        return step;
                    }
                    case "give_dp": return Transaction(new JsonObject { ["giveDp"] = OneNode(() => Amount("the DP")) });
                    case "take_dp": return Transaction(new JsonObject { ["takeDp"] = OneNode(() => Amount("the DP")) });
                    case "give_card": return CardStep("giveCard");
                    case "take_card": return Transaction(CardStep("takeCard"));
                    case "unlock":
                    case "lock":
                    {
                        string what = OneArg(() => ExpectName("card_shop, battle_packs or duelist_challenge")).ToLowerInvariant();
                        if (what is not ("card_shop" or "battle_packs" or "duelist_challenge"))
                            throw new Error(t.Line, t.Column, $"{name} takes card_shop, battle_packs or duelist_challenge, not {what}");
                        return new JsonObject { [name] = what };
                    }
                    case "unlock_character": return new JsonObject { ["unlockCharacter"] = OneNode(() => Amount("a character id (0-239)")) };
                    case "sound": return new JsonObject { ["sound"] = OneNode(() => Number(ExpectNumber("a sound number"))) };
                    case "popup":
                    {
                        // popup "Title" [at x, y] { button "A" -> ...; button "B" -> ...; }
                        var popup = new JsonObject { ["title"] = ExpectString("the popup's title") };
                        if (AcceptWord("at"))
                        {
                            popup["x"] = Number(ExpectNumber("x"));
                            Expect(",");
                            popup["y"] = Number(ExpectNumber("y"));
                        }
                        if (!IsPunct("{"))
                            throw Fail("a popup needs its buttons in { }");
                        Next();
                        var holder = new JsonObject();
                        popup["items"] = Items(holder);
                        return new JsonObject { ["popup"] = popup };
                    }
                }
                // anything else: name(value) / name - a step RIX learns later can be written before the compiler knows it
                if (Accept("("))
                {
                    var value = Value();
                    Expect(")");
                    return new JsonObject { [t.Text] = value };
                }
                return new JsonObject { [t.Text] = true };
            }

            private void Bare()
            {
                if (Accept("("))
                    Expect(")");
            }

            private string OneArg(Func<string> read)
            {
                Expect("(");
                string value = read();
                Expect(")");
                return value;
            }

            private JsonNode OneNode(Func<JsonNode> read)
            {
                Expect("(");
                var value = read();
                Expect(")");
                return value;
            }

            private string ExpectName(string what) => Peek.Kind is T.Str or T.Word ? Next().Text : throw Fail($"expected {what} but found {Shown(Peek)}");
        }

        // ---------------------------------------------------------------- page file -> script

        /// <summary>A version 2 page file (and its menu file, if any) written as a script. Throws for a version 1 page.</summary>
        public static string Decompile(JsonObject page, JsonObject? menus = null)
        {
            if ((int?)page["version"] != 2)
                throw new InvalidOperationException("only version 2 pages can be written as a script (this one is the page designer's)");
            var w = new StringBuilder();
            if (page["header"] is JsonValue header)
                w.Append("header ").Append(Quote(header.ToString())).Append(";\n");
            foreach (var used in (page["use"] as JsonArray ?? []).OfType<JsonValue>())
                w.Append("use ").Append(Quote(used.ToString())).Append(";\n");
            if (page["values"] is JsonObject values)
                foreach (var (name, value) in values)
                    w.Append("value ").Append(name).Append(" = ").Append(Quote(value?.ToString() ?? "")).Append(";\n");
            if (menus?["buttons"] is JsonArray buttons)
            {
                foreach (var b in buttons.OfType<JsonObject>())
                {
                    w.Append("button on ").Append(Name((string?)b["menu"] ?? "main")).Append(' ').Append(Quote((string?)b["label"] ?? ""));
                    if (b["description"] is JsonValue d) w.Append(' ').Append(Quote(d.ToString()));
                    if (b["page"] is JsonValue p) w.Append(" page ").Append(Name(p.ToString()));
                    if (b["key"] is JsonValue k) w.Append(" key ").Append(Quote(k.ToString()));
                    w.Append(" -> ").Append(ActionText(b["action"], 1)).Append(";\n");
                }
            }
            if (menus?["edit"] is JsonArray edits)
            {
                foreach (var e in edits.OfType<JsonObject>())
                {
                    w.Append("edit ").Append(Name((string?)e["item"] ?? ""));
                    if (e["label"] is JsonValue l) w.Append(' ').Append(Quote(l.ToString()));
                    if (e["description"] is JsonValue d) w.Append(e["label"] == null ? " description " : " ").Append(Quote(d.ToString()));
                    if (e["hidden"] is JsonValue h && h.TryGetValue(out bool hide) && hide) w.Append(" hidden");
                    if (e["action"] != null) w.Append(" -> ").Append(ActionText(e["action"], 1));
                    w.Append(";\n");
                }
            }
            foreach (var element in (page["elements"] as JsonArray ?? []).OfType<JsonObject>())
            {
                w.Append('\n');
                string kind = (string?)element["kind"] ?? "text";
                string word = Kinds.FirstOrDefault(k => k.Value == kind && Widgets.Contains(k.Key)).Key ?? kind;
                w.Append(word);
                var skip = new HashSet<string> { "kind", "items" };
                if (Positional.TryGetValue(kind, out string? field) && element[field] is JsonValue first && !(kind == "menu" && !IsName(first.ToString())))
                {
                    w.Append(' ').Append(ValueText(first));
                    skip.Add(field);
                }
                w.Append(FieldsText(element, skip, 1));
                if (kind == "menu")
                {
                    w.Append(" {\n");
                    if (element["source"] is JsonValue source)
                    {
                        w.Append("    rows ").Append(Quote(source.ToString()));
                        if (element["action"] != null) w.Append(" -> ").Append(ActionText(element["action"], 2));
                        w.Append(";\n");
                    }
                    foreach (var item in (element["items"] as JsonArray ?? []).OfType<JsonObject>())
                        w.Append("    ").Append(ItemText(item, 2)).Append(";\n");
                    w.Append("}\n");
                }
                else
                    w.Append(";\n");
            }
            return w.ToString().TrimStart('\n');
        }

        private static string ItemText(JsonObject item, int depth)
        {
            var w = new StringBuilder();
            var skip = new HashSet<string> { "label", "description" };
            if (item["values"] is JsonArray values)
            {
                w.Append("choice ");
                skip.Add("values");
            }
            else if (item["toggle"] is JsonValue t && t.TryGetValue(out bool on) && on)
            {
                w.Append("toggle ");
                skip.Add("toggle");
            }
            else
                w.Append("button ");
            w.Append(Quote((string?)item["label"] ?? ""));
            if (item["description"] is JsonValue d) w.Append(' ').Append(Quote(d.ToString()));
            if (item["values"] is JsonArray list)
                w.Append(" [").Append(string.Join(", ", list.Select(v => v is JsonObject o ? $"{Quote((string?)o["label"] ?? "")} = {Quote((string?)o["value"] ?? "")}" : Quote(v?.ToString() ?? "")))).Append(']');
            w.Append(FieldsText(item, skip, depth));
            return w.ToString();
        }

        private static string FieldsText(JsonObject o, HashSet<string> skip, int depth)
        {
            var w = new StringBuilder();
            if (o["x"] != null && o["y"] != null && !skip.Contains("x"))
            {
                w.Append(" at ").Append(o["x"]!.ToJsonString()).Append(", ").Append(o["y"]!.ToJsonString());
                skip.Add("x");
                skip.Add("y");
            }
            if (o["width"] != null && o["height"] != null)
            {
                w.Append(" size ").Append(o["width"]!.ToJsonString()).Append(", ").Append(o["height"]!.ToJsonString());
                skip.Add("width");
                skip.Add("height");
            }
            if (o["source"] != null && o["kind"] != null && (string?)o["kind"] == "menu")
            {
                skip.Add("source");
                skip.Add("action");   // written as rows "..." -> action inside the menu
            }
            foreach (var (key, value) in o)
            {
                if (skip.Contains(key) || value == null)
                    continue;
                switch (key)
                {
                    case "visible": w.Append(" if ").Append(ConditionText(value)); break;
                    case "enabled": w.Append(" enabled ").Append(ConditionText(value)); break;
                    case "action": w.Append(" -> ").Append(ActionText(value, depth)); break;
                    case "onChange": w.Append(" onchange ").Append(ActionText(value, depth)); break;
                    default: w.Append(' ').Append(key).Append(' ').Append(ValueText(value)); break;
                }
            }
            return w.ToString();
        }

        private static string ConditionText(JsonNode when)
        {
            if (when is JsonArray all)
                return string.Join(" and ", all.Where(p => p != null).Select(p => ConditionText(p!)));
            if (when is not JsonObject o)
                return "var:true";
            string reference = o["var"] != null ? "var:" + o["var"] : o["game"] != null ? "game:" + o["game"] : o["value"] != null ? "value:" + o["value"] : "config:" + o["config"];
            bool not = o["not"] is JsonValue n && n.TryGetValue(out bool b) && b;
            if (o["atLeast"] is JsonNode least)
                return $"{reference} {(not ? "<" : ">=")} {AmountText(least)}";
            if (o["equals"] is JsonNode equals)
                return $"{reference} {(not ? "!=" : "=")} {Quote(equals.ToString())}";
            return (not ? "not " : "") + reference;
        }

        private static string ActionText(JsonNode? action, int depth)
        {
            if (action is JsonArray steps)
            {
                string pad = new(' ', depth * 4);
                return "{\n" + string.Concat(steps.Select(s => pad + StepText(s, depth + 1) + ";\n")) + new string(' ', (depth - 1) * 4) + "}";
            }
            return StepText(action, depth);
        }

        private static string StepText(JsonNode? step, int depth)
        {
            if (step is not JsonObject o || o.Count == 0)
                return "back";
            string Arg(string key) => o[key] is JsonValue v ? ValueText(v) : "\"\"";
            if (o.ContainsKey("page")) return $"page({Arg("page")})";
            if (o.ContainsKey("back")) return "back";
            if (o.ContainsKey("goto")) return $"goto({Arg("goto")})";
            if (o.ContainsKey("video")) return $"video({Arg("video")})";
            if (o.ContainsKey("call")) return $"call({Quote((string?)o["call"] ?? "")})";
            if (o.ContainsKey("quit")) return "quit";
            if (o.ContainsKey("set")) return $"set({o["set"]}, {Quote(o["value"]?.ToString() ?? "")})";
            if (o.ContainsKey("toggle")) return $"toggle({o["toggle"]})";
            if (o.ContainsKey("add")) return $"add({o["add"]}, {o["value"]?.ToJsonString() ?? "1"})";
            if (o.ContainsKey("showCard")) return $"show_card({Arg("showCard")})";
            if (o.ContainsKey("resetFilter")) return "reset_filter";
            string Tail() => (o["then"] != null ? " -> " + ActionText(o["then"], depth) : "") + (o["else"] != null ? " else " + ActionText(o["else"], depth) : "");
            string Amt(string key) => AmountText(o[key]!);
            string Copies() => o["copies"] != null ? ", " + o["copies"]!.ToJsonString() : "";
            if (o.ContainsKey("giveDp")) return $"give_dp({Amt("giveDp")})" + Tail();
            if (o.ContainsKey("takeDp")) return $"take_dp({Amt("takeDp")})" + Tail();
            if (o.ContainsKey("giveCard")) return $"give_card({Amt("giveCard")}{Copies()})";
            if (o.ContainsKey("takeCard")) return $"take_card({Amt("takeCard")}{Copies()})" + Tail();
            if (o.ContainsKey("unlock")) return $"unlock({o["unlock"]})";
            if (o.ContainsKey("lock")) return $"lock({o["lock"]})";
            if (o.ContainsKey("unlockCharacter")) return $"unlock_character({Amt("unlockCharacter")})";
            if (o.ContainsKey("sound")) return $"sound({o["sound"]!.ToJsonString()})";
            if (o.ContainsKey("message")) return $"message({Quote((string?)o["message"] ?? "")})";
            if (o.ContainsKey("confirm"))
                return $"confirm({Quote((string?)o["confirm"] ?? "")})" + (o["yes"] != null ? " -> " + ActionText(o["yes"], depth) : "");
            if (o.ContainsKey("if"))
                return $"if {ConditionText(o["if"]!)} -> {ActionText(o["then"], depth)}" + (o["else"] != null ? " else " + ActionText(o["else"], depth) : "");
            if (o["popup"] is JsonObject popup)
            {
                var w = new StringBuilder("popup ").Append(Quote((string?)popup["title"] ?? ""));
                if (popup["x"] != null && popup["y"] != null) w.Append(" at ").Append(popup["x"]!.ToJsonString()).Append(", ").Append(popup["y"]!.ToJsonString());
                string pad = new(' ', depth * 4);
                w.Append(" {\n");
                foreach (var item in (popup["items"] as JsonArray ?? []).OfType<JsonObject>())
                    w.Append(pad).Append(ItemText(item, depth + 1)).Append(";\n");
                return w.Append(new string(' ', (depth - 1) * 4)).Append('}').ToString();
            }
            var (key, value) = o.First();
            return value is JsonValue b && b.TryGetValue(out bool yes) && yes ? key : $"{key}({ValueText(value!)})";
        }

        private static string AmountText(JsonNode value) =>
            value is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.ToString() is var t
                ? (t.StartsWith('{') && t.EndsWith('}') && IsRef(t[1..^1]) ? t[1..^1] : Quote(t))
                : value.ToJsonString(Indented);

        private static string ValueText(JsonNode value) => value switch
        {
            JsonArray list => "[" + string.Join(", ", list.Select(v => v == null ? "\"\"" : ValueText(v))) + "]",
            JsonValue v when v.GetValueKind() == JsonValueKind.String => IsRef(v.ToString()) ? v.ToString() : Quote(v.ToString()),
            _ => value.ToJsonString(Indented),
        };

        private static bool IsRef(string s) =>
            (s.StartsWith("var:", StringComparison.Ordinal) || s.StartsWith("config:", StringComparison.Ordinal) || s.StartsWith("game:", StringComparison.Ordinal) || s.StartsWith("value:", StringComparison.Ordinal)) && RefName(s) && s.Length > s.IndexOf(':') + 1;

        // a value name: no spaces or punctuation outside { } (balanced)
        private static bool RefName(string s)
        {
            int depth = 0;
            foreach (char c in s)
            {
                if (c == '{') depth++;
                else if (c == '}') { if (--depth < 0) return false; }
                else if (depth == 0 && " ,;()[]=!\"<>".IndexOf(c) >= 0) return false;
            }
            return depth == 0;
        }

        private static bool IsName(string s) => s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') && s.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.')
            && !Keywords.Contains(s, StringComparer.OrdinalIgnoreCase) && !KnownFields.Contains(s, StringComparer.OrdinalIgnoreCase) && s != "at" && s != "size" && s != "if";

        private static string Name(string s) => IsName(s) ? s : Quote(s);

        private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    }
}
