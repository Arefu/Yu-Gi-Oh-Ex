using System.Text.Json.Nodes;

namespace WolfEx
{
    /// <summary>
    /// "Effect library": the game's own cards with their effect written in EffectScript, wherever the language can say it. Made by
    /// docs/effect-scripts/build_effect_reference.py from the game's effect tables and shipped next to WolfEx as effect_reference.json.
    /// It exists to learn the language from cards you know, to copy a script as the start of a new card's effect, and to see which
    /// vanilla card a script would borrow its behaviour from (the "borrows" line = the effectClone "from" of that script).
    /// </summary>
    internal sealed class EffectLibraryPanel : UserControl
    {
        private sealed record Entry(int Id, string Name, string Kind, string Text, string Script, string Trigger, bool Complete, string[] Notes)
        {
            public override string ToString() => (Complete ? "" : "~ ") + Name;
        }

        private readonly List<Entry> _all = [];
        private readonly TextBox _filter = new() { PlaceholderText = "Filter: name, text or script", Dock = DockStyle.Top };
        private readonly CheckBox _onlyComplete = new() { Text = "Only cards that are exactly this effect", Dock = DockStyle.Top, AutoSize = true };
        private readonly CheckBox _spellsOnly = new() { Text = "Spells and Traps only", Dock = DockStyle.Top, AutoSize = true };
        private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly Label _heading = new() { AutoSize = true, Font = new Font(FontFamily.GenericSansSerif, 11F, FontStyle.Bold), Padding = new Padding(0, 0, 0, 4) };
        private readonly TextBox _text = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BackColor = SystemColors.Control };
        private readonly ScriptEditor _script = new() { Dock = DockStyle.Fill, ReadOnly = true };
        private readonly Label _notes = new() { AutoSize = true, MaximumSize = new Size(700, 0), ForeColor = Color.DarkGoldenrod };
        private readonly Label _status = new() { AutoSize = true };
        private readonly Button _use = new() { Text = "Use as template for the card selected in the Effects tab", AutoSize = true };
        private readonly Button _copy = new() { Text = "Copy script", AutoSize = true };

        /// <summary>Raised when the user wants the script in the Effects tab (the handler switches tabs and returns whether it was applied).</summary>
        public Func<string, bool>? UseTemplate;

        public EffectLibraryPanel()
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 300 };
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_list);
            left.Controls.Add(_spellsOnly);
            left.Controls.Add(_onlyComplete);
            left.Controls.Add(_filter);
            split.Panel1.Controls.Add(left);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(8) };
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 35F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 65F));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
            buttons.Controls.Add(_use);
            buttons.Controls.Add(_copy);
            right.Controls.Add(_heading, 0, 0);
            right.Controls.Add(_text, 0, 1);
            right.Controls.Add(_script, 0, 2);
            right.Controls.Add(_notes, 0, 3);
            right.Controls.Add(buttons, 0, 4);
            right.Controls.Add(_status, 0, 5);
            split.Panel2.Controls.Add(right);
            Controls.Add(split);

            _filter.TextChanged += (_, _) => Refill();
            _onlyComplete.CheckedChanged += (_, _) => Refill();
            _spellsOnly.CheckedChanged += (_, _) => Refill();
            _list.SelectedIndexChanged += (_, _) => Show(_list.SelectedItem as Entry);
            _copy.Click += (_, _) =>
            {
                if (_list.SelectedItem is Entry entry)
                {
                    Clipboard.SetText(entry.Script);
                    _status.Text = "Copied.";
                }
            };
            _use.Click += (_, _) =>
            {
                if (_list.SelectedItem is not Entry entry)
                    return;
                bool ok = UseTemplate?.Invoke(entry.Script) ?? false;
                _status.Text = ok ? "Inserted in the Effects tab." : "Select a card in the Effects tab first.";
            };

            Load();
            Refill();
        }

        private void Load()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "effect_reference.json");
            if (!File.Exists(path))
            {
                _status.Text = "effect_reference.json is missing next to WolfEx (make it with docs\\effect-scripts\\build_effect_reference.py).";
                return;
            }
            try
            {
                if (JsonNode.Parse(File.ReadAllText(path))?["cards"] is not JsonArray cards)
                    return;
                foreach (var node in cards.OfType<JsonObject>())
                {
                    _all.Add(new Entry(node["id"]?.GetValue<int>() ?? 0, node["name"]?.GetValue<string>() ?? "", node["kind"]?.GetValue<string>() ?? "",
                        node["text"]?.GetValue<string>() ?? "", node["script"]?.GetValue<string>() ?? "", node["trigger"]?.GetValue<string>() ?? "",
                        node["complete"]?.GetValue<bool>() ?? false, (node["notes"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToArray() ?? []));
                }
                _status.Text = $"{_all.Count} game cards with an effect the language can describe. A leading ~ means the card has more text than this effect.";
            }
            catch (Exception e)
            {
                _status.Text = "Could not read effect_reference.json: " + e.Message;
            }
        }

        private void Refill()
        {
            string filter = _filter.Text.Trim();
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var entry in _all)
            {
                if (_onlyComplete.Checked && !entry.Complete)
                    continue;
                if (_spellsOnly.Checked && entry.Kind != "Spell" && entry.Kind != "Trap")
                    continue;
                if (filter.Length > 0 && !entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) && !entry.Text.Contains(filter, StringComparison.OrdinalIgnoreCase)
                    && !entry.Script.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                _list.Items.Add(entry);
            }
            _list.EndUpdate();
            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
            else
                Show(null);
        }

        private void Show(Entry? entry)
        {
            if (entry == null)
            {
                _heading.Text = "";
                _text.Text = "";
                _script.ReadOnly = false;
                _script.Text = "";
                _script.ReadOnly = true;
                _notes.Text = "";
                return;
            }

            _heading.Text = $"{entry.Name}  (id {entry.Id}, {entry.Kind})";
            _text.Text = entry.Text;
            _script.ReadOnly = false;
            _script.Text = entry.Script + $"{Environment.NewLine}// borrows the behaviour of game card {entry.Id}: effectClone {{ \"from\": {entry.Id} }}";
            _script.ReadOnly = true;
            _notes.Text = entry.Notes.Length == 0 ? "" : "Not expressed by the script: " + string.Join("; ", entry.Notes);
        }
    }
}
