using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace WolfEx
{
    /// <summary>
    /// The readable Fusion Material words Yu-Gi-Oh-Effects Fusion.cpp (MaterialCode) understands, and how the Required cards tab shows them:
    /// "Dragon", "LIGHT", "level:4", "level>=5", "level<=4", "archetype:12", "tuner", "monster", with "non-" in front for "not".
    /// </summary>
    internal static class MaterialCondition
    {
        public enum Category { Type, Attribute, Level, LevelOrHigher, LevelOrLower, Kind, Archetype, AnyMonster }

        public static readonly string[] CategoryNames = ["Type", "Attribute", "Level", "Level or higher", "Level or lower", "Kind", "Archetype", "Any monster"];

        // (word Fusion.cpp reads, what the list shows)
        public static readonly (string Word, string Text)[] Types =
        [
            ("Dragon", "Dragon"), ("Zombie", "Zombie"), ("Fiend", "Fiend"), ("Pyro", "Pyro"), ("SeaSerpent", "Sea Serpent"), ("Rock", "Rock"),
            ("Machine", "Machine"), ("Fish", "Fish"), ("Dinosaur", "Dinosaur"), ("Insect", "Insect"), ("Beast", "Beast"), ("BeastWarrior", "Beast-Warrior"),
            ("Plant", "Plant"), ("Aqua", "Aqua"), ("Warrior", "Warrior"), ("WingedBeast", "Winged Beast"), ("Fairy", "Fairy"), ("Spellcaster", "Spellcaster"),
            ("Thunder", "Thunder"), ("Reptile", "Reptile"), ("Psychic", "Psychic"), ("Wyrm", "Wyrm"), ("Cyberse", "Cyberse"), ("DivineBeast", "Divine-Beast"),
            ("Illusion", "Illusion"),
        ];
        public static readonly (string Word, string Text)[] AttributeWords = [("LIGHT", "LIGHT"), ("DARK", "DARK"), ("WATER", "WATER"), ("FIRE", "FIRE"), ("EARTH", "EARTH"), ("WIND", "WIND")];
        public static readonly (string Word, string Text)[] KindWords =
        [
            ("normal", "Normal"), ("effect", "Effect"), ("tuner", "Tuner"), ("ritual", "Ritual"), ("fusion", "Fusion"), ("synchro", "Synchro"),
            ("xyz", "Xyz"), ("synchroorxyz", "Synchro or Xyz"), ("pendulum", "Pendulum"), ("link", "Link"), ("gemini", "Gemini"),
        ];

        public sealed record Part(Category Category, string Word, bool Not);

        private static string Squash(string text) => Regex.Replace(text, "[ _-]", "").ToLowerInvariant();

        /// <summary>A material word -> its part, null when this dialog can't show it.</summary>
        public static Part? Parse(string raw)
        {
            string text = raw.Trim();
            bool not = false;
            if (text.StartsWith('!'))
            {
                not = true;
                text = text[1..];
            }
            else if (Squash(text).StartsWith("non"))
            {
                not = true;
                text = text[(text.IndexOfAny(['n', 'N']) + 3)..].TrimStart('-', ' ', '_');
            }
            string squashed = Squash(text);
            string kind = "";
            int colon = squashed.IndexOf(':');
            if (colon >= 0)
            {
                kind = squashed[..colon];
                squashed = squashed[(colon + 1)..];
            }
            if (kind.Length == 0 && squashed.StartsWith("level"))
            {
                kind = "level";
                squashed = squashed[5..];
            }
            if (kind == "level")
            {
                var category = squashed.StartsWith(">=") ? Category.LevelOrHigher : squashed.StartsWith("<=") ? Category.LevelOrLower : Category.Level;
                string number = squashed.TrimStart('>', '<', '=');
                return int.TryParse(number, out int level) && level is >= 1 and <= 12 ? new Part(category, level.ToString(), not) : null;
            }
            if (kind == "archetype")
                return int.TryParse(squashed, out int code) && code >= 1 ? new Part(Category.Archetype, code.ToString(), not) : null;
            if (kind.Length == 0 && squashed == "monster")
                return new Part(Category.AnyMonster, "monster", not);
            if (kind is "" or "race" or "type" && Types.FirstOrDefault(t => Squash(t.Word) == squashed).Word is { } race)
                return new Part(Category.Type, race, not);
            if (kind is "" or "attribute" && AttributeWords.FirstOrDefault(a => Squash(a.Word) == squashed).Word is { } attribute)
                return new Part(Category.Attribute, attribute, not);
            if (kind is "" or "kind" && KindWords.FirstOrDefault(k => k.Word == squashed).Word is { } word)
                return new Part(Category.Kind, word, not);
            return null;
        }

        /// <summary>The word Fusion.cpp reads.</summary>
        public static string Word(Part part)
        {
            string word = part.Category switch
            {
                Category.Level => $"level:{part.Word}",
                Category.LevelOrHigher => $"level>={part.Word}",
                Category.LevelOrLower => $"level<={part.Word}",
                Category.Archetype => $"archetype:{part.Word}",
                _ => part.Word,
            };
            return part.Not ? "non-" + word : word;
        }

        public static string Describe(Part part)
        {
            string text = part.Category switch
            {
                Category.Type => Types.FirstOrDefault(t => t.Word == part.Word).Text ?? part.Word,
                Category.Attribute => part.Word,
                Category.Level => $"Level {part.Word}",
                Category.LevelOrHigher => $"Level {part.Word} or higher",
                Category.LevelOrLower => $"Level {part.Word} or lower",
                Category.Kind => KindWords.FirstOrDefault(k => k.Word == part.Word).Text ?? part.Word,
                Category.Archetype => int.TryParse(part.Word, out int code) ? $"\"{ArchetypeCatalog.NameOf(code)}\" ({code})" : part.Word,
                _ => "any monster",
            };
            return part.Not ? "non-" + text : text;
        }

        public static string Describe(string raw) => Parse(raw) is { } part ? Describe(part) : raw;

        // kind word -> the game's material code (Fusion_CardMatchesMaterialCode, docs/EffectSystem.md section 36)
        private static readonly (string Word, int Code)[] KindCodes =
        [
            ("gemini", 72), ("normal", 73), ("effect", 74), ("synchro", 75), ("synchroorxyz", 81), ("xyz", 82), ("pendulum", 83), ("fusion", 89),
            ("tuner", 90), ("link", 95),
        ];

        /// <summary>A game material code (as the game's own tables hold them) -> the word Fusion.cpp reads back to it; null when there is none.</summary>
        public static string? WordOfCode(int code) => code switch
        {
            >= 1 and <= 24 => Types[code - 1].Word,
            >= 26 and <= 31 => AttributeWords[code - 26].Word,
            >= 32 and <= 43 => $"level:{code - 31}",
            >= 44 and <= 55 => $"level>={code - 43}",
            >= 56 and <= 67 => $"level<={code - 55}",
            >= 98 and <= 516 => $"archetype:{code - 98}",
            2497 => "ritual",
            2498 => "Illusion",
            2499 => "monster",
            _ => KindCodes.FirstOrDefault(k => k.Code == code).Word,
        };

        /// <summary>A code below 3000 the words cannot say: what it is.</summary>
        public static string DescribeCode(int code) => WordOfCode(code) is { } word ? Describe(word)
            : code == 94 ? "DARK Pendulum"
            : code == 96 ? "non-Token"
            : code == 97 ? "a rule in the game's code for this card (97)"
            : $"game material code {code}";
    }

    /// <summary>
    /// Builds one Fusion Material that is a condition: one or more parts that must all hold ("1 LIGHT Warrior monster"), or one of which must
    /// ("1 LIGHT or DARK monster"). The result is a word, an array of words (all), or {"any": [...]} - what Fusion.cpp reads.
    /// </summary>
    internal sealed class MaterialConditionDialog : Form
    {
        private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly ComboBox _value = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly CheckBox _not = new() { Text = "not (non-)", AutoSize = true, Margin = new Padding(6, 6, 3, 3) };
        private readonly ListBox _parts = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly RadioButton _all = new() { Text = "the monster must match all of these", AutoSize = true, Checked = true };
        private readonly RadioButton _any = new() { Text = "the monster must match one of these", AutoSize = true };
        private readonly List<MaterialCondition.Part> _list = [];

        /// <summary>The material, or null when the list is empty.</summary>
        public JsonNode? Result { get; private set; }

        public MaterialConditionDialog(JsonNode? current, string cardName)
        {
            Text = $"Fusion Material condition - {cardName}";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(620, 420);
            MinimumSize = new Size(560, 340);
            ShowInTaskbar = false;
            MinimizeBox = MaximizeBox = false;

            _category.Items.AddRange(MaterialCondition.CategoryNames);
            _category.SelectedIndexChanged += (_, _) => FillValues();
            _category.SelectedIndex = 0;

            var add = new Button { Text = "Add", AutoSize = true };
            add.Click += (_, _) => AddPart();
            var remove = new Button { Text = "Remove", AutoSize = true };
            remove.Click += (_, _) =>
            {
                if (_parts.SelectedIndex >= 0)
                {
                    _list.RemoveAt(_parts.SelectedIndex);
                    FillParts();
                }
            };
            var picker = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(6) };
            picker.Controls.AddRange([_category, _value, _not, add]);

            var mode = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(6, 0, 6, 0) };
            mode.Controls.AddRange([_all, _any]);

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            buttons.Controls.AddRange([cancel, ok, remove]);
            AcceptButton = ok;
            CancelButton = cancel;

            var listPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 6, 0) };
            listPanel.Controls.Add(_parts);
            Controls.Add(listPanel);
            Controls.Add(new Label { Text = "Parts of this material:", Dock = DockStyle.Top, Height = 20, Padding = new Padding(6, 4, 0, 0) });
            Controls.Add(picker);
            Controls.Add(mode);
            Controls.Add(buttons);

            LoadParts(current);
            FormClosing += (_, e) =>
            {
                if (DialogResult != DialogResult.OK)
                    return;
                if (_list.Count == 0 && _category.SelectedIndex >= 0)
                    AddPart();   // OK straight after picking one part: take it
                Result = Build();
            };
        }

        private void LoadParts(JsonNode? current)
        {
            IEnumerable<JsonNode?> words = current switch
            {
                JsonValue => [current],
                JsonArray array => array,
                JsonObject obj when obj["any"] is JsonArray any => any,
                _ => [],
            };
            _any.Checked = current is JsonObject;
            foreach (var node in words)
            {
                if (node is JsonValue value && value.TryGetValue<string>(out string? word) && MaterialCondition.Parse(word) is { } part)
                    _list.Add(part);
                else if (node != null)
                    MessageBox.Show($"{node.ToJsonString()} can't be shown here (a nested condition); it is left out if you press OK.", Text,
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            FillParts();
        }

        private MaterialCondition.Category Category => (MaterialCondition.Category)Math.Max(0, _category.SelectedIndex);

        private sealed record Choice(string Word, string Text)
        {
            public override string ToString() => Text;
        }

        private void FillValues()
        {
            _value.Items.Clear();
            IEnumerable<Choice> choices = Category switch
            {
                MaterialCondition.Category.Type => MaterialCondition.Types.Select(t => new Choice(t.Word, t.Text)),
                MaterialCondition.Category.Attribute => MaterialCondition.AttributeWords.Select(a => new Choice(a.Word, a.Text)),
                MaterialCondition.Category.Level or MaterialCondition.Category.LevelOrHigher or MaterialCondition.Category.LevelOrLower =>
                    Enumerable.Range(1, 12).Select(level => new Choice(level.ToString(), level.ToString())),
                MaterialCondition.Category.Kind => MaterialCondition.KindWords.Select(k => new Choice(k.Word, k.Text)),
                MaterialCondition.Category.Archetype => ArchetypeCatalog.Codes()
                    .Select(code => new Choice(code.ToString(), $"{ArchetypeCatalog.NameOf(code)} ({code})"))
                    .OrderBy(c => c.Text, StringComparer.OrdinalIgnoreCase),
                _ => [new Choice("monster", "any monster")],
            };
            _value.Items.AddRange(choices.Cast<object>().ToArray());
            if (_value.Items.Count > 0)
                _value.SelectedIndex = 0;
        }

        private void AddPart()
        {
            if (_value.SelectedItem is not Choice choice)
                return;
            _list.Add(new MaterialCondition.Part(Category, choice.Word, _not.Checked));
            _not.Checked = false;
            FillParts();
        }

        private void FillParts()
        {
            _parts.Items.Clear();
            foreach (var part in _list)
                _parts.Items.Add(MaterialCondition.Describe(part));
        }

        private JsonNode? Build()
        {
            if (_list.Count == 0)
                return null;
            var words = _list.Select(MaterialCondition.Word).ToList();
            if (words.Count == 1)
                return JsonValue.Create(words[0]);
            var array = new JsonArray(words.Select(w => (JsonNode)JsonValue.Create(w)!).ToArray());
            return _any.Checked ? new JsonObject { ["any"] = array } : array;
        }
    }
}
