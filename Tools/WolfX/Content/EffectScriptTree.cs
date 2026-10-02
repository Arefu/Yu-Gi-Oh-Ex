using System.Text.Json.Nodes;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using WolfEx.EffectScriptGenerated;

namespace WolfEx
{
    /// <summary>
    /// An EffectScript as a plain tree for the block editor (Content\Blocks\effect-blocks.js): every rule is {"r": rule name, "c": [children]},
    /// every token its text. Nothing here knows the language, so a new rule in EffectScript.g4 only needs its blocks added on the page.
    /// </summary>
    internal static class EffectScriptTree
    {
        private sealed class Errors : BaseErrorListener, IAntlrErrorListener<int>
        {
            public string? First;

            public override void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int column, string msg, RecognitionException e) =>
                First ??= $"line {line}:{column} {msg}";

            public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int column, string msg, RecognitionException e) =>
                First ??= $"line {line}:{column} {msg}";
        }

        /// <summary>{"tree": ..., "error": null} for a script that parses, {"tree": null, "error": "line 1:4 ..."} otherwise; empty text = {"empty": true}.</summary>
        public static string ToJson(string script)
        {
            if (string.IsNullOrWhiteSpace(script))
                return new JsonObject { ["empty"] = true }.ToJsonString();
            var errors = new Errors();
            var lexer = new EffectScriptLexer(new AntlrInputStream(script));
            lexer.RemoveErrorListeners();
            lexer.AddErrorListener(errors);
            var parser = new EffectScriptParser(new CommonTokenStream(lexer));
            parser.RemoveErrorListeners();
            parser.AddErrorListener(errors);
            var tree = parser.script();
            if (errors.First != null)
                return new JsonObject { ["error"] = errors.First }.ToJsonString();
            return new JsonObject { ["tree"] = Node(tree) }.ToJsonString();
        }

        private static JsonNode? Node(IParseTree tree)
        {
            if (tree is ITerminalNode token)
                return token.Symbol.Type == TokenConstants.EOF ? null : JsonValue.Create(token.GetText());
            var rule = (ParserRuleContext)tree;
            var children = new JsonArray();
            for (int i = 0; i < rule.ChildCount; i++)
                if (Node(rule.GetChild(i)) is { } child)
                    children.Add(child);
            return new JsonObject { ["r"] = EffectScriptParser.ruleNames[rule.RuleIndex], ["c"] = children };
        }
    }
}
