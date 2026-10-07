using System.IO;
using DeckData;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The duelists (main/chardata_&lt;lang&gt;.bin, File Type Libraries/CharData, docs/Characters.md): name and bio per language, portrait key,
    /// series tab, deck, arena, selectable, content pack. Deck titles and owners come from deckdata_E.bin when it's there (a character only
    /// appears in Free Duel when it owns its deck).
    /// Opens the game's duelists with Yu-Gi-Oh-Ex\characters.json on top. Changes to the game's characters are saved into chardata_*.bin;
    /// new characters (in free ids: the game has 240 slots) go to characters.json, which Yu-Gi-Oh-Campaign applies (and unlocks them).
    /// </summary>
    public sealed class CharacterEditor : UserControl, IGameEditor
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _save, _newCharacter, _duplicate;
        private readonly ToolStripComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 45 };
        private readonly ToolStripComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly ToolStripTextBox _find = new() { Width = 170, ToolTipText = "Id, key or part of a name" };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false, MultiSelect = false,
        };
        private readonly Label _heading = new() { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        private readonly TextBox _key = new() { Width = 200 };
        private readonly ComboBox _series = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly IdCombo _deck = new(260);
        private readonly Label _deckInfo = new() { AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
        private readonly CheckBox _selectable = new() { Text = "Selectable (Free Duel opponent, counted in the collection)", AutoSize = true };
        private readonly IdCombo _sku = new(220);
        private readonly IdCombo _arena = new(220);
        private readonly CheckBox _unlocked = new() { Text = "Unlocked (a new character; Yu-Gi-Oh-Campaign sets it in the save)", AutoSize = true };
        private readonly DataGridView _texts = new()
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        };
        private readonly Panel _editor = new() { Dock = DockStyle.Fill, Enabled = false, AutoScroll = true };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private CharacterTable? _table, _baseline;
        private readonly Dictionary<int, bool> _unlockedFlags = [];
        private DeckDataFile? _decks;
        private List<Character> _rows = [];
        private GameFolderFiles? _gameFiles;
        private bool _changed, _binding;
        private int? _shown;

        private static readonly string[] SeriesNames = ["-1: none (unlocked from the start)", "0: Duel Monsters", "1: GX", "2: 5D's", "3: ZEXAL", "4: ARC-V", "5: VRAINS"];

        private string JsonPath => _gameFiles!.ExPath(CharacterJson.FileName);

        public IReadOnlyCollection<string> Files => [.. CharacterTable.AllLanguages.Select(CharacterTable.GamePath), @"main\deckdata_E.bin"];

        public string SavesTo => $"Standard: main\\chardata_<lang>.bin (the game's characters). Additional (new characters): Yu-Gi-Oh-Ex\\{CharacterJson.FileName} (needs Yu-Gi-Oh-Campaign).";

        private char Language => _language.SelectedItem is string text && text.Length > 0 ? text[0] : 'E';

        public bool Dirty => _changed;

        public CharacterEditor()
        {

            ListPick.FirstWhenShown(_list);   // open on the first entry, not an empty "Pick a ..." panel
            _save = Button("Save", "The game's characters into chardata_*.bin, new characters into characters.json (Ctrl+S)", () => Save());
            _newCharacter = Button("New character", "Add a character in the next free id (the game has 240 slots)", () => NewCharacter(null));
            _duplicate = Button("Duplicate", "Add a copy of the selected character in the next free id", () => NewCharacter(Selected()));
            _tools.Items.AddRange([_save, new ToolStripSeparator(), new ToolStripLabel("Language:")]);
            foreach (char language in CharacterTable.AllLanguages)
                _language.Items.Add(language.ToString());
            _language.SelectedIndex = 0;
            _language.SelectedIndexChanged += (_, _) => _list.Invalidate();
            _tools.Items.Add(_language);
            _tools.Items.Add(new ToolStripLabel("Show:"));
            _filter.Items.AddRange(["All characters", "Selectable", "Changed / new"]);
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += (_, _) => Refill();
            _tools.Items.Add(_filter);
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _find.TextChanged += (_, _) => Refill();
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.AddRange([_newCharacter, _duplicate]);

            _list.Columns.Add("Id", 45);
            _list.Columns.Add("Name", 190);
            _list.Columns.Add("Key (portrait)", 130);
            _list.Columns.Add("Series", 70);
            _list.Columns.Add("Deck", 180);
            _list.Columns.Add("", 70);
            _list.RetrieveVirtualItem += (_, e) =>
            {
                if (e.ItemIndex >= _rows.Count) { e.Item = new ListViewItem(new string[_list.Columns.Count]); return; }   // stale index while the list shrinks
                var c = _rows[e.ItemIndex];
                e.Item = new ListViewItem([c.Id.ToString(), c.Name(Language), c.Key, c.Series.ToString(), DeckText(c), Note(c)]);
            };
            _list.SelectedIndexChanged += (_, _) => ShowSelected();

            _series.Items.AddRange(SeriesNames);
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
            void Row(string label, params Control[] controls)
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
                var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                flow.Controls.AddRange(controls);
                grid.Controls.Add(flow);
            }
            Row("Key (portrait \"<key>_neutral\"):", _key);
            Row("Series tab:", _series);
            Row("Deck:", _deck, _deckInfo);
            Row("", _selectable);
            Row("Content pack (sku):", _sku);
            Row("Arena:", _arena);
            Row("", _unlocked);
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Language", ReadOnly = true, FillWeight = 10 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name", FillWeight = 25 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Bio", FillWeight = 65, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
            foreach (char language in CharacterTable.AllLanguages)
                _texts.Rows.Add(language.ToString(), "", "");

            var textsPanel = new Panel { Dock = DockStyle.Fill };
            textsPanel.Controls.Add(_texts);
            _editor.Controls.Add(textsPanel);
            _editor.Controls.Add(grid);

            foreach (Control control in new Control[] { _key, _series, _deck, _selectable, _sku, _arena, _unlocked })
            {
                switch (control)
                {
                    case TextBox box: box.TextChanged += (_, _) => Edited(); break;
                    case IdCombo id: id.ValueChanged += (_, _) => Edited(); break;
                    case ComboBox combo: combo.SelectedIndexChanged += (_, _) => Edited(); break;
                    case NumericUpDown number: number.ValueChanged += (_, _) => Edited(); break;
                    case CheckBox check: check.CheckedChanged += (_, _) => Edited(); break;
                }
            }
            _texts.CellValueChanged += (_, _) => Edited();

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_editor);
            right.Controls.Add(_heading);
            var split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(right);

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) => split.SplitterDistance = Math.Clamp(split.Width * 38 / 100, 280, 520);
            SetEditable(false);
            _status.Text = "Open the game data (File > Open).";
        }

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            if (!Open(language => files.Read(CharacterTable.GamePath(language)), files.Read(@"main\deckdata_E.bin"), files.Describe(CharacterTable.GamePath('E'))))
            {
                _table = _baseline = null;
                SetEditable(false);
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

        private void SetEditable(bool editable) => _save.Enabled = _newCharacter.Enabled = _duplicate.Enabled = editable;

        // ---- text helpers ----

        private string DeckText(Character c)
        {
            if (c.Deck < 0)
                return "(none)";
            var deck = _decks?.Find((uint)c.Deck);
            return deck == null ? $"{c.Deck}" : $"{c.Deck}: {deck.Title}";
        }

        private string Note(Character c)
        {
            if (_baseline == null)
                return "";
            var game = _baseline.Find(c.Id);
            return game == null ? "new" : game.SameAs(c) ? "" : "changed";
        }

        // ---- opening / saving ----

        private bool Open(Func<char, byte[]?> read, byte[]? deckData, string from)
        {
            var files = new Dictionary<char, byte[]>();
            foreach (char language in CharacterTable.AllLanguages)
                if (read(language) is byte[] bytes)
                    files[language] = bytes;
            if (files.Count == 0)
            {
                _status.Text = $"{from}: no chardata_*.bin.";
                return false;
            }
            try
            {
                _table = CharacterTable.Parse(files);
                _decks = deckData != null ? DeckDataFile.Parse(deckData) : null;
                if (_decks != null)
                    DeckJson.ApplyTo(_decks, DeckJson.Load(Path.Combine(Path.GetDirectoryName(JsonPath)!, DeckJson.FileName)), 'E');   // new and changed decks from decks.json
                _baseline = _table.Clone();
                _unlockedFlags.Clear();
                int fromJson = CharacterJson.Apply(CharacterJson.Load(JsonPath), _table, _unlockedFlags);
                _deck.SetItems(GameNames.Decks(_decks).Prepend((-1, "none")));
                _sku.SetItems(GameNames.ContentPacks(_gameFiles));
                _arena.SetItems(GameNames.Arenas(_gameFiles, "none"));
                _changed = false;
                SetEditable(true);
                Refill();
                _status.Text = $"{from}: {_baseline.Characters.Count} characters (of {CharacterTable.SlotCount} slots), languages {string.Join("", _table.Languages)}" +
                    (fromJson > 0 ? $"; {fromJson} from {CharacterJson.FileName}" : "") + (_decks == null ? "; no deckdata_E.bin, so no deck names" : "") + ".";
                return true;
            }
            catch (InvalidDataException ex)
            {
                _status.Text = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Standard content: the game's characters (with your changes) go back into every language's chardata_*.bin.
        /// Additional content: new characters go to characters.json; it is deleted when there are none.
        /// </summary>
        public bool Save()
        {
            if (_table == null || _baseline == null || _gameFiles == null)
                return false;
            var problems = _table.Characters.Where(c => c.Key.Length == 0 || c.Key.Any(ch => ch > 127)).Select(c => $"Character {c.Id}: the key must be ASCII and not empty.").ToList();
            if (problems.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                var standard = _table.Clone();
                standard.Characters.RemoveAll(c => _baseline.Find(c.Id) == null);
                standard.Characters.Sort((a, b) => a.Id.CompareTo(b.Id));
                var write = new Dictionary<string, byte[]>();
                foreach (char language in standard.Languages)
                {
                    byte[] bytes = standard.ToBytes(language);
                    if (!bytes.AsSpan().SequenceEqual(_baseline.ToBytes(language)))
                        write[CharacterTable.GamePath(language)] = bytes;
                }
                if (write.Count > 0)
                    _gameFiles.Write(write);

                var root = CharacterJson.Diff(standard, _table, _unlockedFlags);
                int count = ((System.Text.Json.Nodes.JsonArray)root["characters"]!).Count;
                if (count > 0)
                    CharacterJson.Save(JsonPath, root);
                else if (File.Exists(JsonPath))
                    File.Delete(JsonPath);
                _baseline = standard;
                _changed = false;
                Refill();
                _status.Text = (write.Count > 0 ? $"Saved chardata for {write.Count} languages into {_gameFiles.Describe(CharacterTable.GamePath('E'))}" : "The game's characters are unchanged") +
                    (count > 0 ? $"; {count} new characters to {JsonPath} (Yu-Gi-Oh-Campaign applies it)." : ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        // ---- list ----

        private Character? Selected() =>
            _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;

        private void Refill(int? select = null)
        {
            if (_table == null)
                return;
            select ??= Selected()?.Id;
            string find = _find.Text.Trim();
            _rows = _table.Characters.OrderBy(c => c.Id).Where(c =>
                (_filter.SelectedIndex != 1 || c.Selectable != 0) &&
                (_filter.SelectedIndex != 2 || Note(c).Length > 0) &&
                (find.Length == 0 || c.Id.ToString() == find || c.Key.Contains(find, StringComparison.OrdinalIgnoreCase) ||
                 c.Names.Values.Any(n => n.Contains(find, StringComparison.OrdinalIgnoreCase)))).ToList();
            _list.VirtualListSize = _rows.Count;
            _list.SelectedIndices.Clear();
            int index = select is int wanted ? _rows.FindIndex(c => c.Id == wanted) : -1;
            if (index >= 0)
            {
                _list.SelectedIndices.Add(index);
                _list.EnsureVisible(index);
            }
            _list.Invalidate();
            ShowSelected();
        }

        private void ShowSelected()
        {
            var c = Selected();
            _shown = c?.Id;
            _editor.Enabled = c != null;
            if (c == null)
            {
                _heading.Text = _table == null ? "" : "Pick a character.";
                return;
            }
            _binding = true;
            try
            {
                bool isNew = _baseline?.Find(c.Id) == null;
                _heading.Text = $"{c.Name(Language)} ({c.Id}){(isNew ? " - new" : "")}";
                _key.Text = c.Key;
                _series.SelectedIndex = Math.Clamp(c.Series + 1, 0, SeriesNames.Length - 1);
                _deck.Value = c.Deck;
                _selectable.Checked = c.Selectable != 0;
                _sku.Value = c.Sku;
                _arena.Value = c.Arena;
                _unlocked.Visible = isNew;
                _unlocked.Checked = !_unlockedFlags.TryGetValue(c.Id, out bool u) || u;
                for (int i = 0; i < CharacterTable.AllLanguages.Length; i++)
                {
                    char language = CharacterTable.AllLanguages[i];
                    _texts.Rows[i].Cells[1].Value = c.Names.GetValueOrDefault(language, "");
                    _texts.Rows[i].Cells[2].Value = c.Bios.GetValueOrDefault(language, "");
                }
                ShowDeckInfo(c);
            }
            finally
            {
                _binding = false;
            }
        }

        private void ShowDeckInfo(Character c)
        {
            if (c.Deck < 0)
            {
                _deckInfo.Text = c.Selectable != 0 ? "no deck: won't be listed in Free Duel" : "";
                return;
            }
            var deck = _decks?.Find((uint)c.Deck);
            _deckInfo.Text = deck == null ? (_decks == null ? "" : "not a deck in deckdata")
                : deck.CharacterId == c.Id ? "(owned by this character)"
                : $"belongs to character {deck.CharacterId}: Free Duel won't list this one";
        }

        private void Edited()
        {
            if (_binding || _table == null || _shown is not int id || _table.Find(id) is not Character c)
                return;
            c.Key = _key.Text.Trim();
            c.Series = _series.SelectedIndex - 1;
            c.Deck = _deck.Value;
            c.Selectable = _selectable.Checked ? 1 : 0;
            c.Sku = _sku.Value;
            c.Arena = _arena.Value;
            if (_unlocked.Visible)
                _unlockedFlags[id] = _unlocked.Checked;
            for (int i = 0; i < CharacterTable.AllLanguages.Length; i++)
            {
                char language = CharacterTable.AllLanguages[i];
                if (_texts.Rows[i].Cells[1].Value is string name && (name.Length > 0 || c.Names.ContainsKey(language)))
                    c.Names[language] = name;
                if (_texts.Rows[i].Cells[2].Value is string bio && (bio.Length > 0 || c.Bios.ContainsKey(language)))
                    c.Bios[language] = bio;
            }
            _changed = true;
            ShowDeckInfo(c);
            _list.Invalidate();
        }

        private void NewCharacter(Character? copyOf)
        {
            if (_table == null)
                return;
            if (_table.FreeId() is not int id)
            {
                _status.Text = $"All {CharacterTable.SlotCount} character ids are used.";
                return;
            }
            var c = copyOf?.Clone() ?? new Character { Key = _table.Characters.FirstOrDefault()?.Key ?? "", Series = 0, Selectable = 1, Sku = 1 };
            c.Id = id;
            if (copyOf == null)
            {
                c.Names['E'] = "New Duelist";
                c.Bios['E'] = "";
            }
            _table.Characters.Add(c);
            _unlockedFlags[id] = true;
            _changed = true;
            _find.Text = "";
            Refill(id);
            _status.Text = $"Character {id} added. Its key picks the portrait (an existing character's key borrows theirs). For Free Duel it needs a deck it owns.";
        }
    }
}
