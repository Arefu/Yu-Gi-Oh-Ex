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
        private readonly CheckBox _unlocked = new() { Text = "Unlocked from start (requires Yu-Gi-Oh-Campaign)", AutoSize = true };
        private readonly DataGridView _texts = new()
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        };
        private readonly Panel _editor = new() { Dock = DockStyle.Fill, Enabled = false, AutoScroll = true };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        // duel rules against a duelist (Yu-Gi-Oh-Ex\duelistrules.json, Yu-Gi-Oh-Campaign DuelistRules.cpp): id -> starting LP
        private readonly NumericUpDown _ruleLp = new() { Minimum = 0, Maximum = 999999, Increment = 500, Width = 90 };
        private readonly Dictionary<int, int> _ruleLps = [];
        private string RulesPath => _gameFiles!.ExPath("duelistrules.json");
        private CharacterTable? _table, _baseline;   // _baseline: the game's own characters (not the ones WolfX added, even once they are in chardata)
        private Dictionary<char, byte[]> _onDisk = [];   // chardata as read, to write only what changed
        private readonly Dictionary<int, bool> _unlockedFlags = [];
        private DeckDataFile? _decks;
        private List<Character> _rows = [];
        private GameFolderFiles? _gameFiles;
        private bool _changed, _binding;
        private int? _shown;

        private static readonly string[] SeriesNames = ["-1: none (unlocked from the start)", "0: Duel Monsters", "1: GX", "2: 5D's", "3: ZEXAL", "4: ARC-V", "5: VRAINS"];

        // the series a duelist can be under: none, the game's 6, then the new series (campaigns.json) - Free Duel lists them under it
        private readonly List<int> _seriesValues = [];

        private void FillSeries()
        {
            bool binding = _binding;
            _binding = true;
            try
            {
                _seriesValues.Clear();
                _series.Items.Clear();
                for (int i = 0; i < SeriesNames.Length; i++)
                {
                    _seriesValues.Add(i - 1);
                    _series.Items.Add(SeriesNames[i]);
                }
                foreach (var series in CampaignSeriesFile.Current.Entries)
                {
                    _seriesValues.Add(series.Id);
                    _series.Items.Add($"{series.Id}: {series.Name()} (new series)");
                }
            }
            finally { _binding = binding; }
        }

        private string JsonPath => _gameFiles!.ExPath(CharacterJson.FileName);

        public IReadOnlyCollection<string> Files => [.. CharacterTable.AllLanguages.Select(CharacterTable.GamePath), @"main\deckdata_E.bin"];

        public string SavesTo => $"Standard: main\\chardata_<lang>.bin (the game's characters). Additional (new characters): Yu-Gi-Oh-Ex\\{CharacterJson.FileName} (needs Yu-Gi-Oh-Campaign).";

        private char Language => _language.SelectedItem is string text && text.Length > 0 ? text[0] : 'E';

        public bool Dirty => _changed;

        public CharacterEditor()
        {
            _deck.DropDown += (_, _) => RefreshDeckChoices();
            VisibleChanged += (_, _) => { if (Visible) RefreshDeckChoices(); };

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
            _tools.Items.AddRange([_newCharacter, _duplicate,
                Button("Delete...", "Delete the selected duelist: one you added, or one of the game's (taken out of chardata in the Ex patch)", DeleteCharacter),
                new ToolStripSeparator(),
                Button("Menu portrait...", "The duelist's portrait on the menus and Free Duel: sprite <key>_neutral of pdui\\chars (replaced, or added for a new key), saved into the Ex patch", SetMenuPortrait),
                Button("Story sprite...", "The duelist's story scene picture: pdui\\dialog_chars\\<key>_<mood>.png (neutral, smile, anger, surprise, shy...), saved into the Ex patch", SetStorySprite)]);

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

            FillSeries();
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
            Row("Starting LP against them (0 = the game's):", _ruleLp);
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
            LiveTables.Characters = () => _table;
            VisibleChanged += (_, _) => { if (Visible) RefreshDecks(); };   // decks added on the Decks page since this page was opened
            SetEditable(false);
            _status.Text = "Open the game data (File > Open).";
        }

        /// <summary>
        /// Takes the Decks page's current decks (new ones and unsaved changes too) for the deck list and the deck labels, keeping this page's own
        /// edits. Runs when the page is shown: the page is otherwise only read again after a save, and not at all while it has unsaved changes.
        /// </summary>
        private void RefreshDecks()
        {
            if (_table == null || LiveTables.Decks?.Invoke() is not { } live || !live.Languages.Any())
                return;
            try
            {
                _decks = DeckDataFile.Parse(live.ToBytes(live.Languages.Contains('E') ? 'E' : live.Languages.First()));
            }
            catch (Exception ex)   // best effort: an unsaved deck the table can't write yet must not break the page
            {
                _status.Text = "Couldn't refresh the deck list from the Decks page: " + ex.Message;
                return;
            }
            bool binding = _binding;
            _binding = true;   // SetItems must not count as an edit
            try { _deck.SetItems(GameNames.Decks(_decks).Prepend((-1, "none"))); }
            finally { _binding = binding; }
            Refill();   // keeps the selected character; shows its deck and the deck's owner again
        }

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            try { CampaignSeriesFile.Load(files.ExPath(CampaignSeriesFile.FileName)); } catch { }
            FillSeries();
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
            if (_gameFiles?.DeckChoices?.Find((uint)c.Deck) is { } live)
                return $"{c.Deck}: {live.Title(Language)}";
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
                _onDisk = files;
                _decks = deckData != null ? DeckDataFile.Parse(deckData) : null;
                if (_decks != null)
                    DeckJson.ApplyTo(_decks, DeckJson.Load(Path.Combine(Path.GetDirectoryName(JsonPath)!, DeckJson.FileName)), 'E');   // new and changed decks from decks.json
                _baseline = _table.Clone();
                _unlockedFlags.Clear();
                int fromJson = CharacterJson.Apply(CharacterJson.Load(JsonPath), _table, _unlockedFlags);
                _gameFiles!.CharacterChoices = _table;
                _deck.SetItems(GameNames.Decks(_decks).Prepend((-1, "none")));
                _sku.SetItems(GameNames.ContentPacks(_gameFiles));
                _arena.SetItems(GameNames.Arenas(_gameFiles, "none"));
                _changed = false;
                SetEditable(true);
                Refill();
                _status.Text = $"{from}: {_baseline.Characters.Count} characters (of {CharacterTable.SlotCount} slots), languages {string.Join("", _table.Languages)}" +
                    (fromJson > 0 ? $"; {fromJson} from {CharacterJson.FileName}" : "") + (_decks == null ? "; no deckdata_E.bin, so no deck names" : "") + "." + LanguageWarnings.Describe(_table.Warnings);
                return true;
            }
            catch (InvalidDataException ex)
            {
                _status.Text = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Every character (the game's with your changes, and new ones: the game has 240 slots) goes into every language's chardata_*.bin.
        /// characters.json lists the new ones ("native") and their unlock flag; it is deleted when there are none.
        /// </summary>
        public bool Save()
        {
            if (_table == null || _baseline == null || _gameFiles == null)
                return false;
            _ = _ruleLp.Value;   // a toolbar click keeps the focus in the box: commit what was typed
            _texts.EndEdit();
            var problems = _table.Characters.Where(c => c.Key.Length == 0 || c.Key.Any(ch => ch > 127)).Select(c => $"Character {c.Id}: the key must be ASCII and not empty.").ToList();
            if (problems.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                // Every character fits the game's 240 slots, so all of them - new ones too - go into chardata (the Ex patch). characters.json
                // only keeps which ones are new ("native": true) and whether each starts unlocked, which Yu-Gi-Oh-Campaign sets in the save.
                var standard = _table.Clone();
                standard.Characters.Sort((a, b) => a.Id.CompareTo(b.Id));
                var write = new Dictionary<string, byte[]>();
                foreach (char language in standard.Languages)
                {
                    byte[] bytes = standard.ToBytes(language);
                    if (!_onDisk.TryGetValue(language, out var old) || !bytes.AsSpan().SequenceEqual(old))
                        write[CharacterTable.GamePath(language)] = bytes;
                }
                if (write.Count > 0)
                {
                    if (GameFolderFiles.GameRunning)
                    {
                        _status.Text = "Close the game first: characters are saved into its files.";
                        return false;
                    }
                    _gameFiles.Write(write);
                    foreach (char language in standard.Languages)
                        _onDisk[language] = standard.ToBytes(language);
                }

                var added = new System.Text.Json.Nodes.JsonArray();
                foreach (var c in standard.Characters.Where(c => _baseline.Find(c.Id) == null))
                    added.Add(new System.Text.Json.Nodes.JsonObject
                    {
                        ["id"] = c.Id, ["key"] = c.Key, ["native"] = true, ["unlocked"] = !_unlockedFlags.TryGetValue(c.Id, out bool u) || u,
                    });
                int count = added.Count;
                if (count > 0)
                    CharacterJson.Save(JsonPath, new System.Text.Json.Nodes.JsonObject
                    {
                        ["note"] = "New duelists made by WolfX. They are in main/chardata_<lang>.bin (the Ex patch); this file says which are new and whether each starts unlocked (Yu-Gi-Oh-Campaign sets it in the save).",
                        ["characters"] = added,
                    });
                else if (File.Exists(JsonPath))
                    File.Delete(JsonPath);
                foreach (int gone in _ruleLps.Keys.Where(id => _table.Find(id) == null).ToList())
                    _ruleLps.Remove(gone);
                if (_ruleLps.Count > 0)
                {
                    var rules = new System.Text.Json.Nodes.JsonArray();
                    foreach (var (id, lp) in _ruleLps.OrderBy(r => r.Key))
                        rules.Add(new System.Text.Json.Nodes.JsonObject { ["id"] = id, ["lp"] = lp });
                    File.WriteAllText(RulesPath, new System.Text.Json.Nodes.JsonObject { ["duelists"] = rules }
                        .ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }) + "\n");
                }
                else if (File.Exists(RulesPath))
                    File.Delete(RulesPath);
                _changed = false;
                Refill();
                _status.Text = (write.Count > 0 ? $"Saved chardata for {write.Count} languages into {_gameFiles.Describe(CharacterTable.GamePath('E'))}" : "Chardata unchanged") +
                    (count > 0 ? $"; {count} new duelist(s) are in it, listed in {CharacterJson.FileName} for unlocking (Yu-Gi-Oh-Campaign)." : ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        // ---- list ----

        // ---- the duelist's art (game files, the Ex patch) ----

        private byte[]? PickPng(string title)
        {
            using var dialog = new OpenFileDialog { Title = title, Filter = "PNG picture (*.png)|*.png" };
            return dialog.ShowDialog(this) == DialogResult.OK ? File.ReadAllBytes(dialog.FileName) : null;
        }

        private Character? ArtTarget()
        {
            if (_gameFiles == null || Selected() is not { } c || c.Key.Trim().Length == 0)
            {
                _status.Text = "Pick a duelist with a key first.";
                return null;
            }
            if (GameFolderFiles.GameRunning)
            {
                _status.Text = "Close the game first: the art goes into its files.";
                return null;
            }
            return c;
        }

        private void SetMenuPortrait()
        {
            if (ArtTarget() is not { } c || PickPng($"Menu portrait for {c.Name(Language)} ({c.Key}_neutral)") is not { } png)
                return;
            try
            {
                _status.Text = SheetEdit.SetSprite(_gameFiles!, @"pdui\chars", c.Key + "_neutral", png);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not set the portrait:\n{ex.Message}", "Menu portrait", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetStorySprite()
        {
            if (ArtTarget() is not { } c)
                return;
            // the moods the game's own duelists have, and any other name
            using var form = new Form
            {
                Text = "Story sprite", Size = new Size(420, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false,
            };
            form.Controls.Add(new Label { Text = $"Mood ({c.Key}_<mood>.png in pdui\\dialog_chars):", Location = new Point(12, 12), AutoSize = true });
            var mood = new ComboBox { Location = new Point(12, 36), Width = 380 };
            mood.Items.AddRange(["neutral", "smile", "anger", "surprise", "shy", "sad"]);
            mood.Text = "neutral";
            var ok = new System.Windows.Forms.Button { Text = "Pick picture...", DialogResult = DialogResult.OK, Location = new Point(292, 72), Width = 100 };
            form.Controls.AddRange([mood, ok]);
            form.AcceptButton = ok;
            if (form.ShowDialog(this) != DialogResult.OK || mood.Text.Trim().Length == 0)
                return;
            string path = $@"pdui\dialog_chars\{c.Key}_{mood.Text.Trim()}.png";
            if (PickPng($"Story sprite for {c.Name(Language)} ({path})") is not { } png)
                return;
            try
            {
                _gameFiles!.Write(path, png);
                _status.Text = $"{path} saved into {_gameFiles.Describe(path)}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not save the sprite:\n{ex.Message}", "Story sprite", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

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
                _series.SelectedIndex = Math.Max(0, _seriesValues.IndexOf(c.Series));
                _deck.Value = c.Deck;
                _selectable.Checked = c.Selectable != 0;
                _sku.Value = c.Sku;
                _arena.Value = c.Arena;
                _unlocked.Visible = true;
                _unlocked.Checked = _unlockedFlags.TryGetValue(c.Id, out bool u) ? u : isNew;
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
            if (_gameFiles?.DeckChoices?.Find((uint)c.Deck) is { } live)
            {
                _deckInfo.Text = live.CharacterId == c.Id ? "(owned by this character)"
                    : $"shared deck (requires Yu-Gi-Oh-Campaign; original owner {live.CharacterId})";
                return;
            }
            var deck = _decks?.Find((uint)c.Deck);
            _deckInfo.Text = deck == null ? (_decks == null ? "" : "not a deck in deckdata")
                : deck.CharacterId == c.Id ? "(owned by this character)"
                : $"shared deck (requires Yu-Gi-Oh-Campaign; original owner {deck.CharacterId})";
        }

        private void RefreshDeckChoices()
        {
            if (_gameFiles?.DeckChoices is not { } decks) return;
            _deck.SetItems(decks.Decks.OrderBy(d => d.Id).Select(d => ((int)d.Id, d.Title(Language))).Prepend((-1, "none")));
            _list.Invalidate();
        }

        private void Edited()
        {
            if (_binding || _table == null || _shown is not int id || _table.Find(id) is not Character c)
                return;
            c.Key = _key.Text.Trim();
            if (_series.SelectedIndex >= 0 && _series.SelectedIndex < _seriesValues.Count)
                c.Series = _seriesValues[_series.SelectedIndex];
            c.Deck = _deck.Value;
            c.Selectable = _selectable.Checked ? 1 : 0;
            c.Sku = _sku.Value;
            c.Arena = _arena.Value;
            if (_ruleLp.Value > 0)
                _ruleLps[c.Id] = (int)_ruleLp.Value;
            else
                _ruleLps.Remove(c.Id);
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

        private void DeleteCharacter()
        {
            if (_table == null || Selected() is not { } c)
                return;
            bool game = _baseline?.Find(c.Id) != null;
            var owned = _decks?.Records.Where(d => d.CharacterId == c.Id).Select(d => $"{d.Id}").ToList() ?? [];
            string question = $"Delete duelist {c.Id} \"{c.Name(Language)}\"?" +
                (game ? "\n\nIt is one of the game's own: it is taken out of chardata (the Ex patch; the original stays in YGO_2020.dat)." : "") +
                (owned.Count > 0 ? $"\n\nDecks {string.Join(", ", owned)} name it as their owner; they stay, owned by nobody (set a new owner on Decks)." : "") +
                "\n\nStory duels that use it (Story editor) will show an empty character until you change them.";
            if (MessageBox.Show(this, question, "Delete duelist", MessageBoxButtons.YesNo, game ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _table.Characters.Remove(c);
            _unlockedFlags.Remove(c.Id);
            _changed = true;
            Refill();
            _status.Text = $"Duelist {c.Id} deleted (saved when you press Save).";
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