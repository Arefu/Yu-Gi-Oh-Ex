namespace Wolf.Editors
{
    /// <summary>
    /// Pick any number of archetypes for a card (WolfX and WolfEx). The engine has no per-card limit: membership is one sorted card list per
    /// archetype (bin/CARD_Named.bin, Is_CardInNamedArchetype 0x14076CFF0 binary-searches it), and custom cards' archetypes are an unbounded
    /// list in cards.json that Yu-Gi-Oh-Cards turns into the same per-archetype lists.
    /// </summary>
    public sealed class ArchetypePickerDialog : Form
    {
        private readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "Filter by name or code..." };
        private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        private readonly Label _count = new() { Dock = DockStyle.Bottom, Height = 20, ForeColor = SystemColors.GrayText };
        private readonly HashSet<int> _selected;
        private readonly Func<IEnumerable<(int Code, string Text)>> _all;
        private readonly Func<string, int>? _addNew;
        private bool _filling;

        public List<int> Result => _selected.OrderBy(code => code).ToList();

        /// <param name="all">Every archetype, as (code, text to show).</param>
        /// <param name="addNew">Makes a new archetype from a name and returns its code; null hides the button.</param>
        public ArchetypePickerDialog(string cardName, IEnumerable<int> current, Func<IEnumerable<(int Code, string Text)>> all, Func<string, int>? addNew = null)
        {
            _selected = [.. current];
            _all = all;
            _addNew = addNew;
            Text = $"Archetypes - {cardName}";
            Size = new Size(460, 580);
            MinimumSize = new Size(320, 300);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var clear = new Button { Text = "Clear all", AutoSize = true };
            buttons.Controls.AddRange([ok, cancel, clear]);
            if (addNew != null)
            {
                var add = new Button { Text = "New archetype...", AutoSize = true };
                add.Click += (_, _) => NewArchetype();
                buttons.Controls.Add(add);
            }

            // tab order: filter, list, then the buttons
            Controls.Add(_list);
            Controls.Add(_filter);
            Controls.Add(_count);
            Controls.Add(buttons);
            _filter.TabIndex = 0;
            _list.TabIndex = 1;
            buttons.TabIndex = 2;
            ok.TabIndex = 0;
            cancel.TabIndex = 1;
            AcceptButton = ok;
            CancelButton = cancel;

            _filter.TextChanged += (_, _) => Fill();
            _list.ItemCheck += (_, e) =>
            {
                if (_filling)
                    return;
                int code = ((Item)_list.Items[e.Index]).Code;
                if (e.NewValue == CheckState.Checked) _selected.Add(code); else _selected.Remove(code);
                BeginInvoke(UpdateCount);
            };
            clear.Click += (_, _) => { _selected.Clear(); Fill(); };
            Fill();
        }

        private sealed record Item(int Code, string Text)
        {
            public override string ToString() => Text;
        }

        private void UpdateCount() => _count.Text = $"  {_selected.Count} selected (no limit)";

        private void Fill()
        {
            string filter = _filter.Text.Trim();
            var known = _all().ToDictionary(a => a.Code, a => a.Text);
            _filling = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            // selected ones first, so what the card is in shows without scrolling
            foreach (int code in known.Keys.Concat(_selected).Distinct().OrderBy(c => _selected.Contains(c) ? 0 : 1).ThenBy(c => c))
            {
                string text = known.TryGetValue(code, out var name) ? name : $"{code} - (unknown archetype)";
                if (filter.Length > 0 && !text.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                _list.Items.Add(new Item(code, text), _selected.Contains(code));
            }
            _list.EndUpdate();
            _filling = false;
            UpdateCount();
        }

        private void NewArchetype()
        {
            using var prompt = new Form
            {
                Text = "New archetype", Size = new Size(380, 130), StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false,
            };
            var box = new TextBox { Left = 10, Top = 12, Width = 340, TabIndex = 0 };
            var ok = new Button { Text = "Add", Left = 190, Top = 44, DialogResult = DialogResult.OK, TabIndex = 1 };
            var cancel = new Button { Text = "Cancel", Left = 275, Top = 44, DialogResult = DialogResult.Cancel, TabIndex = 2 };
            prompt.Controls.AddRange([box, ok, cancel]);
            prompt.AcceptButton = ok;
            prompt.CancelButton = cancel;
            if (prompt.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(box.Text) || _addNew == null)
                return;
            _selected.Add(_addNew(box.Text.Trim()));
            Fill();
        }
    }
}
