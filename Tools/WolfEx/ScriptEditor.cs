using System.Text;
using System.Text.RegularExpressions;
using ScintillaNET;

namespace WolfEx
{
    /// <summary>
    /// The EffectScript source editor: a Scintilla control with line numbers, syntax colours (actions, keywords, numbers, strings, comments),
    /// keyword completion and a red squiggle where the compiler found an error. The colours are done by this class (Lexer.Container): the
    /// language is small, so the whole text is re-coloured on every change.
    /// </summary>
    internal sealed partial class ScriptEditor : Scintilla
    {
        private const int StyleKeyword = 1, StyleAction = 2, StyleNumber = 3, StyleString = 4, StyleComment = 5, StyleOperator = 6, StyleWord = 7;
        private const int ErrorIndicator = 8;

        private static readonly string[] Actions = { "draw", "gain_lp", "burn", "search", "revive", "send_to_grave", "destroy" };
        private static readonly string[] Keywords = { "effect", "then", "where", "and", "deck", "grave", "opponent_grave", "either_grave", "all", "target", "own", "opponent", "any",
            "monster", "spell", "trap", "card", "race", "attribute", "level", "atk", "archetype", "name", "on", "once_per_turn",
            "normal_summoned", "special_summoned", "summoned", "normal_or_special_summoned", "flip", "destroyed_by_battle", "sent_to_grave", "sent_from_field_to_grave" };
        private static readonly string[] Values = { "Dragon", "Zombie", "Fiend", "Pyro", "SeaSerpent", "Rock", "Machine", "Fish", "Dinosaur", "Insect", "Beast", "BeastWarrior", "Plant", "Aqua",
            "Warrior", "WingedBeast", "Fairy", "Spellcaster", "Thunder", "Reptile", "Psychic", "Wyrm", "Cyberse", "DivineBeast", "Light", "Dark", "Water", "Fire", "Earth", "Wind", "Divine" };

        private static readonly Regex Token = new(
            "(?<comment>//[^\\r\\n]*)|(?<string>\"[^\"\\r\\n]*\"?)|(?<number>\\d+)|(?<op><=|>=|[=<>])|(?<word>[A-Za-z_][A-Za-z_0-9]*)|(?<punct>[{}(),;])",
            RegexOptions.Compiled);

        public ScriptEditor()
        {
            // No lexer is set: with none, Scintilla asks the container for styling (StyleNeeded) - see Colourise().
            WrapMode = WrapMode.None;
            TabWidth = 4;
            IndentWidth = 4;
            UseTabs = false;
            ScrollWidth = 400;
            AutoCIgnoreCase = true;

            StyleResetDefault();
            Styles[Style.Default].Font = "Consolas";
            Styles[Style.Default].Size = 11;
            StyleClearAll();

            Styles[StyleKeyword].ForeColor = Color.FromArgb(0x00, 0x4F, 0xC4);
            Styles[StyleKeyword].Bold = true;
            Styles[StyleAction].ForeColor = Color.FromArgb(0x8B, 0x1A, 0x9B);
            Styles[StyleAction].Bold = true;
            Styles[StyleNumber].ForeColor = Color.FromArgb(0xB0, 0x5A, 0x00);
            Styles[StyleString].ForeColor = Color.FromArgb(0xA3, 0x15, 0x15);
            Styles[StyleComment].ForeColor = Color.FromArgb(0x5A, 0x8A, 0x3C);
            Styles[StyleComment].Italic = true;
            Styles[StyleOperator].ForeColor = Color.FromArgb(0x55, 0x55, 0x55);
            Styles[StyleWord].ForeColor = Color.FromArgb(0x1E, 0x6E, 0x6E);

            Margins[0].Type = MarginType.Number;
            Margins[0].Width = 36;
            Styles[Style.LineNumber].ForeColor = Color.Gray;

            Indicators[ErrorIndicator].Style = IndicatorStyle.Squiggle;
            Indicators[ErrorIndicator].ForeColor = Color.Red;

            StyleNeeded += (_, e) => Colourise();
            TextChanged += (_, _) =>
            {
                ClearError();
                Colourise();
            };
            CharAdded += OnCharAdded;
            InitCompletion();
        }

        /// <summary>Puts a red squiggle under the position an error message like "line 3:12 mismatched input" names (any other message: the whole first line).</summary>
        public void ShowError(string message)
        {
            ClearError();
            var match = Regex.Match(message, "line (\\d+):(\\d+)");
            int line = match.Success ? Math.Max(0, int.Parse(match.Groups[1].Value) - 1) : 0;
            if (line >= Lines.Count)
                return;
            int column = match.Success ? int.Parse(match.Groups[2].Value) : 0;

            var lineText = Lines[line].Text.TrimEnd('\r', '\n');
            int start = Math.Min(column, Math.Max(0, lineText.Length - 1));
            int length = Math.Max(1, Regex.Match(lineText.Substring(Math.Min(start, lineText.Length)), "^\\w+").Length);
            int startByte = Lines[line].Position + Encoding.UTF8.GetByteCount(lineText.Substring(0, Math.Min(start, lineText.Length)));
            int lengthBytes = Encoding.UTF8.GetByteCount(lineText.Substring(Math.Min(start, lineText.Length), Math.Min(length, Math.Max(0, lineText.Length - start))));
            IndicatorCurrent = ErrorIndicator;
            IndicatorFillRange(startByte, Math.Max(1, lengthBytes));
        }

        public void ClearError()
        {
            IndicatorCurrent = ErrorIndicator;
            IndicatorClearRange(0, TextLength);
        }

        private void Colourise()
        {
            string text = Text;
            var bytes = new List<(int Style, int Length)>();
            int cursor = 0;
            foreach (Match m in Token.Matches(text))
            {
                if (m.Index > cursor)
                    bytes.Add((Style.Default, Encoding.UTF8.GetByteCount(text.Substring(cursor, m.Index - cursor))));

                int style = Style.Default;
                if (m.Groups["comment"].Success) style = StyleComment;
                else if (m.Groups["string"].Success) style = StyleString;
                else if (m.Groups["number"].Success) style = StyleNumber;
                else if (m.Groups["op"].Success) style = StyleOperator;
                else if (m.Groups["word"].Success)
                {
                    string word = m.Value;
                    style = Array.IndexOf(Actions, word) >= 0 ? StyleAction
                        : Array.IndexOf(Keywords, word) >= 0 ? StyleKeyword
                        : Array.IndexOf(Values, word) >= 0 ? StyleWord : Style.Default;
                }
                bytes.Add((style, Encoding.UTF8.GetByteCount(m.Value)));
                cursor = m.Index + m.Length;
            }
            if (cursor < text.Length)
                bytes.Add((Style.Default, Encoding.UTF8.GetByteCount(text.Substring(cursor))));

            StartStyling(0);
            foreach (var (style, length) in bytes)
                if (length > 0)
                    SetStyling(length, style);
        }

        // Typing letters offers the actions and keywords (and the race / attribute names after "= ").
        private void OnCharAdded(object? sender, CharAddedEventArgs e)
        {
            if (!char.IsLetter((char)e.Char) && e.Char != '_')
                return;

            ShowCompletion(automatic: true);   // Ctrl+Space asks for the same list on demand (ScriptEditor.Completion.cs)
        }
    }
}
