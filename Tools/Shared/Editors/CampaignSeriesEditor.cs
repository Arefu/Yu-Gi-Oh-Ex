using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The Campaign series page: new campaign series past the game's 6 (Yu-Gi-Oh-Ex\campaigns.json, <see cref="CampaignSeriesFile"/>, read by
    /// Yu-Gi-Oh-Campaign). Each has a number (6-63), a name per language, the game series whose logo it shows, and when it opens: from the
    /// start (the default), never, or once the last duel of another series is won. Its duels are made on the Story editor (pick the series
    /// there). The game's own series can't be changed here: the game has no file for them.
    /// </summary>
    public sealed class CampaignSeriesEditor : UserControl, IGameEditor
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly NumericUpDown _id = new() { Minimum = CampaignSeriesFile.FirstId, Maximum = CampaignSeriesFile.LastId, Width = 60 };
        private readonly ComboBox _logo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly ComboBox _unlock = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        private readonly DataGridView _names = new()
        {
            Dock = DockStyle.Top, Height = 190, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        private readonly Label _info = new() { Dock = DockStyle.Top, AutoSize = false, Height = 44, Padding = new Padding(4) };
        private readonly Panel _editor = new() { Dock = DockStyle.Fill, Enabled = false, AutoScroll = true };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly List<int> _unlockChoices = [];   // _unlock index -> CampaignSeriesFile.FromStart / Never / a series number
        private List<string> _logoKeys = [.. CampaignSeriesFile.GameLogoKeys];   // _logo index -> "logo_<key>" in pdui\SeriesLogo
        private const int Stock = 6;                       // the list starts with the game's 6 series (shown, not editable)

        private CampaignSeriesFile _file = new();
        private HashSet<string> _openedProblems = [];   // problems the file already had: only new ones block a save
        private GameFolderFiles? _gameFiles;
        private bool _changed, _binding;

        public CampaignSeriesEditor()
        {
            var save = Button("Save", "Save campaigns.json (Ctrl+S)", () => Save());
            var add = Button("New series", "Add a series after the last one (its duels: Story editor, pick the series)", AddSeries);
            var remove = Button("Delete series...", "Delete the picked series: a new one, or one of the game's (left out of the campaign's series list; its duels and progress stay)", RemoveSeries);
            var restore = Button("Restore", "Put a deleted game series back on the series list", RestoreSeries);
            _tools.Items.AddRange([save, new ToolStripSeparator(), add, remove, restore]);


            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
            void Row(string label, Control control)
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
                grid.Controls.Add(control);
            }
            Row("Series number (6-63):", _id);
            Row("Logo (pdui\\SeriesLogo):", _logo);
            Row("Opens:", _unlock);
            _names.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Language", ReadOnly = true, FillWeight = 20 });
            _names.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name (an empty language uses English)", FillWeight = 80 });
            foreach (char language in StoryDuelTable.AllLanguages)
                _names.Rows.Add(language.ToString(), "");
            _editor.Controls.Add(_names);
            _editor.Controls.Add(new Label { Text = "Name on the series list:", Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4, 6, 0, 2) });
            _editor.Controls.Add(grid);
            _editor.Controls.Add(_info);

            _list.SelectedIndexChanged += (_, _) => Bind();
            _id.ValueChanged += (_, _) => Commit();
            _logo.SelectedIndexChanged += (_, _) => Commit();
            _unlock.SelectedIndexChanged += (_, _) => Commit();
            _names.CellValueChanged += (_, _) => Commit();

            var split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(_editor);
            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) => split.SplitterDistance = Math.Clamp(split.Width * 30 / 100, 220, 360);
            _status.Text = "Open the game data (File > Open).";
        }

        public IReadOnlyCollection<string> Files => [];

        public string SavesTo => $"Additional: Yu-Gi-Oh-Ex\\{CampaignSeriesFile.FileName}, new campaign series (needs Yu-Gi-Oh-Campaign). Their duels: Story editor.";

        public bool Dirty => _changed;

        private string Path => _gameFiles!.ExPath(CampaignSeriesFile.FileName);

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            _logoKeys = CampaignSeriesFile.LogoKeys(files);
            _logo.Items.Clear();
            foreach (string key in _logoKeys)
            {
                int game = Array.IndexOf(CampaignSeriesFile.GameLogoKeys, key);
                _logo.Items.Add(game >= 0 ? $"{key} ({StoryDuelTable.SeriesNames[game]})" : $"{key} (new, in the patched sheet)");
            }
            try
            {
                _file = CampaignSeriesFile.Load(Path);
            }
            catch (Exception ex)
            {
                _file = new CampaignSeriesFile();
                _status.Text = $"Could not read {CampaignSeriesFile.FileName}: {ex.Message}";
            }
            _openedProblems = [.. _file.Problems()];
            _changed = false;
            Fill(_file.Entries.Count > 0 ? Stock : 0);
            _status.Text = $"{_file.Entries.Count} new series. The game has 6; a new one is listed after them and borrows series 6's save space while it is picked.";
        }

        public bool Save()
        {
            if (_gameFiles == null)
                return false;
            // a toolbar click doesn't take the focus: finish what is being typed (a grid cell, a number box) so it is saved too
            _names.EndEdit();
            _ = _id.Value;   // reading Value commits text typed into the box
            Commit();
            var problems = _file.Problems().Where(p => !_openedProblems.Contains(p)).ToList();
            if (problems.Count > 0)
            {
                MessageBox.Show(string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                _file.Save(Path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save:\n{ex.Message}", "Campaign series", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            _changed = false;
            _status.Text = $"Saved {CampaignSeriesFile.FileName} ({_file.Entries.Count} series).";
            return true;
        }

        private CampaignSeriesFile.Series? Selected => _list.SelectedIndex >= Stock && _list.SelectedIndex - Stock < _file.Entries.Count ? _file.Entries[_list.SelectedIndex - Stock] : null;

        private void Fill(int select)
        {
            _binding = true;
            try
            {
                _list.Items.Clear();
                for (int i = 0; i < Stock; i++)
                    _list.Items.Add($"{i + 1}: {StoryDuelTable.SeriesNames[i]} (game{(_file.Hidden.Contains(i) ? ", deleted" : "")})");
                foreach (var s in _file.Entries)
                    _list.Items.Add($"{s.Id + 1}: {s.Name()}");
            }
            finally { _binding = false; }
            _list.SelectedIndex = Math.Clamp(select, 0, _list.Items.Count - 1);
        }

        // "Opens" choices: from the start, never, after the last duel of each other series (the game's and the new ones)
        private void FillUnlock(CampaignSeriesFile.Series s)
        {
            _unlock.Items.Clear();
            _unlockChoices.Clear();
            _unlock.Items.Add("From the start");
            _unlockChoices.Add(CampaignSeriesFile.FromStart);
            _unlock.Items.Add("Never (stays locked)");
            _unlockChoices.Add(CampaignSeriesFile.Never);
            foreach (int series in CampaignSeriesFile.AllSeries().Where(n => n != s.Id))
            {
                _unlock.Items.Add($"After the last duel of {series + 1}: {CampaignSeriesFile.NameOf(series)} is won");
                _unlockChoices.Add(series);
            }
        }

        private void Bind()
        {
            if (_binding)
                return;
            var s = Selected;
            _editor.Enabled = s != null;
            if (s == null)
            {
                int game = _list.SelectedIndex;
                _info.Text = game >= 0 && game < Stock
                    ? $"{StoryDuelTable.SeriesNames[game]}: one of the game's series (logo logo_{CampaignSeriesFile.GameLogoKeys[game]}, its own unlock rules). Its duels are on the Story editor; new series can open after its last duel."
                    : "No new series yet: New series adds one.";
                return;
            }
            _binding = true;
            try
            {
                _id.Value = Math.Clamp(s.Id, CampaignSeriesFile.FirstId, CampaignSeriesFile.LastId);
                string logoKey = s.LogoKey.Length > 0 ? s.LogoKey : CampaignSeriesFile.GameLogoKeys[Math.Clamp(s.Logo, 1, 6) - 1];
                _logo.SelectedIndex = Math.Max(0, _logoKeys.IndexOf(logoKey));
                FillUnlock(s);
                _unlock.SelectedIndex = Math.Max(0, _unlockChoices.IndexOf(s.UnlockAfter));
                for (int i = 0; i < StoryDuelTable.AllLanguages.Length; i++)
                    _names.Rows[i].Cells[1].Value = s.Names.GetValueOrDefault(StoryDuelTable.AllLanguages[i], "");
                _info.Text = $"Series {s.Id + 1} on the list. Its duels are the Story editor's duels with this series (orders 1-{StoryDuelTable.MaxOrder}); " +
                             "new series' progress is kept in the save file (Yu-Gi-Oh-Core's save extension).";
            }
            finally { _binding = false; }
        }

        private void Commit()
        {
            if (_binding || Selected is not { } s)
                return;
            s.Id = (int)_id.Value;
            if (_logo.SelectedIndex >= 0 && _logo.SelectedIndex < _logoKeys.Count)
            {
                string key = _logoKeys[_logo.SelectedIndex];
                int game = Array.IndexOf(CampaignSeriesFile.GameLogoKeys, key);
                s.Logo = game >= 0 ? game + 1 : 1;
                s.LogoKey = game >= 0 ? "" : key;
            }
            if (_unlock.SelectedIndex >= 0 && _unlock.SelectedIndex < _unlockChoices.Count)
                s.UnlockAfter = _unlockChoices[_unlock.SelectedIndex];
            for (int i = 0; i < StoryDuelTable.AllLanguages.Length; i++)
                s.Names[StoryDuelTable.AllLanguages[i]] = _names.Rows[i].Cells[1].Value as string ?? "";
            _changed = true;
            _binding = true;
            try { _list.Items[_list.SelectedIndex] = $"{s.Id + 1}: {s.Name()}"; }
            finally { _binding = false; }
        }

        private void AddSeries()
        {
            int id = CampaignSeriesFile.FirstId;
            while (_file.Entries.Any(s => s.Id == id))
                ++id;
            if (id > CampaignSeriesFile.LastId)
            {
                _status.Text = "All series numbers are used.";
                return;
            }
            _file.Entries.Add(new CampaignSeriesFile.Series { Id = id, Names = { ['E'] = "New Series" } });
            _changed = true;
            Fill(Stock + _file.Entries.Count - 1);
            _status.Text = $"Series {id + 1} added. Give it duels on the Story editor (New duel with this series picked).";
        }

        private void RemoveSeries()
        {
            int pick = _list.SelectedIndex;
            if (pick >= 0 && pick < Stock)
            {
                if (_file.Hidden.Contains(pick))
                    return;
                if (MessageBox.Show($"Delete {StoryDuelTable.SeriesNames[pick]} from the campaign? It is left out of the series list (Yu-Gi-Oh-Campaign); its duels, " +
                        "unlocks and progress stay, and Restore brings it back. Things that expect it (the overall %, new series set to open after it) may misbehave.",
                        "Delete series", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;
                _file.Hidden.Add(pick);
                _changed = true;
                Fill(pick);
                return;
            }
            if (Selected is not { } s)
                return;
            if (MessageBox.Show($"Remove {s.Name()}? Its duels stay in storyduels.json but no list shows them until they are moved to a series.",
                    "Remove series", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            int index = _list.SelectedIndex;
            _file.Entries.Remove(s);
            _changed = true;
            Fill(index);
        }

        private void RestoreSeries()
        {
            int pick = _list.SelectedIndex;
            if (pick >= 0 && pick < Stock && _file.Hidden.Remove(pick))
            {
                _changed = true;
                Fill(pick);
            }
        }

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                Save();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
