using System.IO;
using DeckData;
using Types;
using WolfX.Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The decks (main/deckdata_&lt;lang&gt;.bin + decks.zib/&lt;file&gt;.ydc, File Type Libraries/DeckData, docs/Decks.md): owner character, series,
    /// signature card, content pack, file name, title per language and the cards (main / extra / side). "Used by" lists the characters whose
    /// Free Duel deck it is and the story duels that use it (from chardata and dueldata).
    /// Opens the game's decks with Yu-Gi-Oh-Ex\decks.json on top. Changes to the game's decks are saved into deckdata_*.bin and decks.zib;
    /// new decks (in free ids: the game has 700 slots), and the cards of a game deck that holds custom cards, go to decks.json, which
    /// Yu-Gi-Oh-Campaign applies.
    /// </summary>
    public sealed class DeckEditor : UserControl, IGameEditor
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _save, _newDeck, _duplicate;
        private readonly ToolStripComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 45 };
        private readonly ToolStripComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly ToolStripTextBox _find = new() { Width = 170, ToolTipText = "Id, file name, title or owner" };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false, MultiSelect = false,
        };
        private readonly Label _heading = new() { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        private readonly TextBox _file = new() { Width = 220 };
        private readonly ComboBox _series = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly IdCombo _owner = new(220);
        private readonly Label _ownerInfo = new() { AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
        private readonly Button _signature = new() { AutoSize = true, Text = "(none)" };
        private readonly Button _noSignature = new() { AutoSize = true, Text = "None" };
        private readonly IdCombo _sku = new(220);
        private readonly CheckBox _unlocked = new() { Text = "Unlocked (a new deck; Yu-Gi-Oh-Campaign marks it unlocked in the save)", AutoSize = true };
        private readonly Label _usedBy = new() { AutoSize = true, MaximumSize = new Size(700, 0), Margin = new Padding(3, 4, 3, 6), ForeColor = Color.DimGray };
        private readonly DataGridView _texts = new()
        {
            Dock = DockStyle.Top, Height = 160, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        private readonly CardListEditor _main = new() { Dock = DockStyle.Fill, Title = "Main deck:", Capacity = DeckDataTable.MaxMain };
        private readonly CardListEditor _extra = new() { Dock = DockStyle.Fill, Title = "Extra deck:", Capacity = DeckDataTable.MaxExtra };
        private readonly CardListEditor _side = new() { Dock = DockStyle.Fill, Title = "Side deck:", Capacity = DeckDataTable.MaxSide };
        private readonly Label _cardsNote = new() { Dock = DockStyle.Top, Height = 20, ForeColor = Color.DimGray };
        private readonly Panel _editor = new() { Dock = DockStyle.Fill, Enabled = false, AutoScroll = true };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private DeckDataTable? _table, _baseline;
        private readonly Dictionary<uint, bool> _unlockedFlags = [];
        private Dictionary<char, byte[]> _deckDisk = [];   // deckdata as read, to write only what changed
        private CharacterTable? _characters;
        private StoryDuelTable? _duels;
        private ZibArchive? _zib;
        private GameFolderFiles? _gameFiles;
        private List<Deck> _rows = [];
        private bool _changed, _binding;
        private uint? _shown;

        private static readonly string[] SeriesChoices = ["-1: starter deck", "0: Duel Monsters", "1: GX", "2: 5D's", "3: ZEXAL", "4: ARC-V", "5: VRAINS"];

        private string JsonPath => _gameFiles!.ExPath(DeckJson.FileName);

        public IReadOnlyCollection<string> Files =>
            [.. DeckDataTable.AllLanguages.Select(DeckDataTable.GamePath), "decks.zib", .. CharacterTable.AllLanguages.Select(CharacterTable.GamePath), .. StoryDuelTable.AllLanguages.Select(StoryDuelTable.GamePath)];

        public string SavesTo => $"Standard: main\\deckdata_<lang>.bin and decks.zib (the game's decks). Additional (new decks, custom cards in decks): Yu-Gi-Oh-Ex\\{DeckJson.FileName} (needs Yu-Gi-Oh-Campaign).";

        private char Language => _language.SelectedItem is string text && text.Length > 0 ? text[0] : 'E';

        public bool Dirty => _changed;

        private void RefreshCharacterChoices()
        {
            if (_gameFiles?.CharacterChoices is not { } characters) return;
            _characters = characters;
            _owner.SetItems(GameNames.Characters(_characters, Language));
            _list.Invalidate();
        }

        public DeckEditor()
        {
            _owner.DropDown += (_, _) => RefreshCharacterChoices();
            VisibleChanged += (_, _) => { if (Visible) RefreshCharacterChoices(); };

            ListPick.FirstWhenShown(_list);   // open on the first entry, not an empty "Pick a ..." panel
            _save = Button("Save", "The game's decks into deckdata_*.bin and decks.zib, new decks into decks.json (Ctrl+S)", () => Save());
            _newDeck = Button("New deck", "Add an empty deck in the next free id (the game has 700 slots)", () => NewDeck(null));
            _duplicate = Button("Duplicate", "Add a copy of the selected deck (cards included) in the next free id", () => NewDeck(Selected()));
            _tools.Items.AddRange([_save, new ToolStripSeparator(), new ToolStripLabel("Language:")]);
            foreach (char language in DeckDataTable.AllLanguages)
                _language.Items.Add(language.ToString());
            _language.SelectedIndex = 0;
            _language.SelectedIndexChanged += (_, _) => { _list.Invalidate(); _owner.SetItems(GameNames.Characters(_characters, Language)); ShowSelected(); };
            _tools.Items.Add(_language);
            _tools.Items.Add(new ToolStripLabel("Show:"));
            _filter.Items.Add("All decks");
            _filter.Items.AddRange(DeckDataTable.SeriesNames);
            _filter.Items.AddRange(["Starter decks", "Changed / new"]);
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += (_, _) => Refill();
            _tools.Items.Add(_filter);
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _find.TextChanged += (_, _) => Refill();
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.AddRange([_newDeck, _duplicate,
                Button("Delete...", "Delete the selected deck: one you added, or one of the game's (taken out of deckdata in the Ex patch)", DeleteDeck),
                new ToolStripSeparator(),
                Button("Import deck...", "A .ydk (YGOPro / EDOPro / Master Duel export, by card password) or a .ydc (the game's format): into a new deck, or the selected deck's cards", ImportDeck),
                Button("Export deck...", "The selected deck's cards as a .ydk (passwords) or .ydc", ExportDeck)]);

            _list.Columns.Add("Id", 45);
            _list.Columns.Add("Title", 200);
            _list.Columns.Add("Owner", 140);
            _list.Columns.Add("Series", 85);
            _list.Columns.Add("Cards", 70);
            _list.Columns.Add("", 60);
            _list.RetrieveVirtualItem += (_, e) =>
            {
                if (e.ItemIndex >= _rows.Count) { e.Item = new ListViewItem(new string[_list.Columns.Count]); return; }   // stale index while the list shrinks
                var d = _rows[e.ItemIndex];
                e.Item = new ListViewItem([d.Id.ToString(), d.Title(Language), OwnerName(d.CharacterId), SeriesText(d.Series), CardsText(d), Note(d)]);
            };
            _list.SelectedIndexChanged += (_, _) => ShowSelected();

            _series.Items.AddRange(SeriesChoices);
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
            void Row(string label, params Control[] controls)
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
                var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                flow.Controls.AddRange(controls);
                grid.Controls.Add(flow);
            }
            Row("File name (decks.zib/<name>.ydc):", _file);
            Row("Owner (character):", _owner, _ownerInfo);
            Row("Series:", _series);
            Row("Signature card:", _signature, _noSignature);
            Row("Content pack (sku):", _sku);
            Row("", _unlocked);
            Row("Used by:", _usedBy);
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Language", ReadOnly = true, FillWeight = 10 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Title", FillWeight = 40 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Text 2 (unused by the game's menus)", FillWeight = 25 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Text 3", FillWeight = 25 });
            foreach (char language in DeckDataTable.AllLanguages)
                _texts.Rows.Add(language.ToString(), "", "", "");

            var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, MinimumSize = new Size(0, 280) };
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
            cards.Controls.Add(_main, 0, 0);
            cards.Controls.Add(_extra, 1, 0);
            cards.Controls.Add(_side, 2, 0);
            _editor.Controls.Add(cards);
            _editor.Controls.Add(_cardsNote);
            _editor.Controls.Add(_texts);
            _editor.Controls.Add(grid);

            _file.TextChanged += (_, _) => Edited();
            _owner.ValueChanged += (_, _) => Edited();
            _series.SelectedIndexChanged += (_, _) => Edited();
            _sku.ValueChanged += (_, _) => Edited();
            _unlocked.CheckedChanged += (_, _) => Edited();
            _texts.CellValueChanged += (_, _) => Edited();
            foreach (var list in new[] { _main, _extra, _side })
                list.Changed += (_, _) => CardsEdited();
            _signature.Click += (_, _) => PickSignature();
            _noSignature.Click += (_, _) => SetSignature(0xFFFF);

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
            Load += (_, _) => split.SplitterDistance = Math.Clamp(split.Width * 34 / 100, 280, 480);
            LiveTables.Decks = () => _table;
            VisibleChanged += (_, _) => { if (Visible) RefreshCharacters(); };   // characters added on the Characters page since this page was opened
            SetEditable(false);
            _status.Text = "Open the game data (File > Open).";
        }

        /// <summary>
        /// Takes the Characters page's current characters (new ones and unsaved changes too) for the owner list, the owner names and "Used by",
        /// keeping this page's own edits. Runs when the page is shown: the page is otherwise only read again after a save, and not at all while
        /// it has unsaved changes.
        /// </summary>
        private void RefreshCharacters()
        {
            if (_table == null || LiveTables.Characters?.Invoke() is not { } live)
                return;
            _characters = live;
            bool binding = _binding;
            _binding = true;   // SetItems must not count as an edit
            try { _owner.SetItems(GameNames.Characters(_characters, Language)); }
            finally { _binding = binding; }
            Refill();   // keeps the selected deck; shows its owner and "Used by" again
        }

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            if (!Open(language => files.Read(DeckDataTable.GamePath(language)), files.Read("decks.zib"),
                    language => files.Read(CharacterTable.GamePath(language)), language => files.Read(StoryDuelTable.GamePath(language)), files.Describe(DeckDataTable.GamePath('E'))))
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

        private void SetEditable(bool editable) => _save.Enabled = _newDeck.Enabled = _duplicate.Enabled = editable;

        // ---- text helpers ----

        private string OwnerName(uint id) => _characters?.Find((int)id) is Character c ? c.Name(Language) : $"#{id}";

        private static string SeriesText(int series) => series < 0 ? "starter" : series < DeckDataTable.SeriesNames.Length ? DeckDataTable.SeriesNames[series] : series.ToString();

        private static string CardsText(Deck d) => d.Cards == null ? "?" : $"{d.Cards.Main.Count}/{d.Cards.Extra.Count}/{d.Cards.Side.Count}";

        private string Note(Deck d)
        {
            if (_baseline == null)
                return "";
            var game = _baseline.Find(d.Id);
            return game == null ? "new" : game.SameRecord(d) && game.SameCards(d) ? "" : "changed";
        }

        private string UsedBy(Deck d)
        {
            var uses = new List<string>();
            foreach (var c in _characters?.Characters.Where(c => c.Deck == (int)d.Id) ?? [])
                uses.Add(c.Id == (int)d.CharacterId ? $"{c.Name(Language)}'s Free Duel deck" : $"{c.Name(Language)} shares this Free Duel deck (requires Yu-Gi-Oh-Campaign)");
            foreach (var duel in _duels?.Duels.Where(x => x.Decks.Contains((int)d.Id)) ?? [])
                uses.Add($"story duel {duel.Id} \"{duel.Title(Language)}\" ({(duel.Decks[0] == (int)d.Id ? "player" : "opponent")})");
            if (d.Series < 0)
                uses.Add("a starter deck");
            return uses.Count == 0 ? (_characters == null && _duels == null ? "(no chardata / dueldata to check)" : "nothing") : string.Join("\n", uses);
        }

        // ---- opening / saving ----

        private bool Open(Func<char, byte[]?> read, byte[]? zib, Func<char, byte[]?> readCharacters, Func<char, byte[]?> readDuels, string from)
        {
            Dictionary<char, byte[]> Files(Func<char, byte[]?> reader)
            {
                var files = new Dictionary<char, byte[]>();
                foreach (char language in DeckDataTable.AllLanguages)
                    if (reader(language) is byte[] bytes)
                        files[language] = bytes;
                return files;
            }
            var deckFiles = Files(read);
            if (deckFiles.Count == 0)
            {
                _status.Text = $"{from}: no deckdata_*.bin.";
                return false;
            }
            try
            {
                _table = DeckDataTable.Parse(deckFiles);
                _deckDisk = deckFiles;
                _zib = zib != null ? ZibArchive.Parse(zib) : null;
                int withCards = 0;
                if (_zib != null)
                {
                    // the game finds the .ydc files ignoring case
                    var byName = _zib.Entries.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Data, StringComparer.OrdinalIgnoreCase);
                    withCards = _table.AttachCards(name => byName.GetValueOrDefault(name));
                }
                var characterFiles = Files(readCharacters);
                _characters = characterFiles.Count > 0 ? CharacterTable.Parse(characterFiles) : null;
                var duelFiles = Files(readDuels);
                _duels = duelFiles.Count > 0 ? StoryDuelTable.Parse(duelFiles) : null;
                _baseline = _table.Clone();
                _unlockedFlags.Clear();
                int fromJson = DeckJson.Apply(DeckJson.Load(JsonPath), _table, _unlockedFlags);
                _gameFiles!.DeckChoices = _table;
                // the other content's JSON, for "Used by"
                if (_characters != null) CharacterJson.Apply(CharacterJson.Load(_gameFiles!.ExPath(CharacterJson.FileName)), _characters);
                if (_duels != null) StoryDuelJson.Apply(StoryDuelJson.Load(_gameFiles!.ExPath(StoryDuelJson.FileName)), _duels);
                _owner.SetItems(GameNames.Characters(_characters, Language));
                _sku.SetItems(GameNames.ContentPacks(_gameFiles));
                _changed = false;
                SetEditable(true);
                Refill();
                _status.Text = $"{from}: {_baseline.Decks.Count} decks (of {DeckDataTable.SlotCount} slots), languages {string.Join("", _table.Languages)}, " +
                    (_zib == null ? "no decks.zib so no cards" : $"cards for {withCards}") + (fromJson > 0 ? $"; {fromJson} from {DeckJson.FileName}" : "") + "." +
                    LanguageWarnings.Describe(_table.Warnings);
                return true;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException or OverflowException)
            {
                // why it didn't open, in full (the status line is one line)
                _status.Text = "Couldn't read the decks: " + ex.Message;
                MessageBox.Show(this, ex.Message, "Couldn't read the decks", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        /// <summary>
        /// Standard content: the game's decks (with your changes) go back into every language's deckdata_*.bin, and their cards into decks.zib.
        /// Additional content: new decks, and the cards of a game deck that now holds custom cards (its .ydc keeps the game's cards), go to
        /// decks.json; it is deleted when there is nothing in it.
        /// </summary>
        public bool Save()
        {
            if (_table == null || _baseline == null || _gameFiles == null)
                return false;
            var problems = _table.Problems();
            if (problems.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", problems.Take(30)), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                // Every deck fits the game's 700 slots, so all of them - new ones too - go into deckdata and decks.zib (the Ex patch). A deck
                // with custom cards keeps only the game's cards in its .ydc (the game checks them while it loads, before custom cards exist):
                // its whole list goes to decks.json, which Yu-Gi-Oh-Campaign applies once cards are in.
                static bool HasCustom(Deck d) => d.Cards != null && d.Cards.AllCards.Any(card => !GameContent.IsGameCard(card));
                var standard = _table.Clone();
                foreach (var d in standard.Decks.Where(HasCustom))
                {
                    if (_baseline.Find(d.Id) is { } game)
                        d.Cards = game.Cards;
                    else
                    {
                        var gameOnly = new YdcDeck { Header = d.Cards!.Header };
                        gameOnly.Main.AddRange(d.Cards.Main.Where(card => GameContent.IsGameCard(card)));
                        gameOnly.Extra.AddRange(d.Cards.Extra.Where(card => GameContent.IsGameCard(card)));
                        gameOnly.Side.AddRange(d.Cards.Side.Where(card => GameContent.IsGameCard(card)));
                        d.Cards = gameOnly;
                    }
                }

                var write = new Dictionary<string, byte[]>();
                foreach (char language in standard.Languages)
                {
                    byte[] bytes = standard.ToBytes(language);
                    if (!_deckDisk.TryGetValue(language, out var old) || !bytes.AsSpan().SequenceEqual(old))
                        write[DeckDataTable.GamePath(language)] = bytes;
                }
                int ydcs = 0;
                if (_zib != null)
                {
                    foreach (var d in standard.Decks.Where(d => d.Cards != null))
                    {
                        var game = _baseline.Find(d.Id);
                        if (game != null && game.SameCards(d) && game.FileName == d.FileName)
                            continue;
                        if (_zib.Find(d.FileName + ".ydc") is { } existing && existing.Data.AsSpan().SequenceEqual(d.Cards!.ToBytes()))
                            continue;
                        string name = _zib.Entries.FirstOrDefault(e => e.Name.Equals(d.FileName + ".ydc", StringComparison.OrdinalIgnoreCase))?.Name ?? d.FileName + ".ydc";
                        _zib.Set(name, d.Cards!.ToBytes());
                        ydcs++;
                    }
                    if (ydcs > 0)
                        write["decks.zib"] = _zib.ToBytes();
                }
                if (write.Count > 0)
                {
                    if (GameFolderFiles.GameRunning)
                    {
                        _status.Text = "Close the game first: decks are saved into its files.";
                        return false;
                    }
                    _gameFiles.Write(write);
                    foreach (char language in standard.Languages)
                        _deckDisk[language] = standard.ToBytes(language);
                }

                // decks.json: the new decks (marked "native": they are in deckdata; their unlock flag, and their cards when custom) and the
                // game decks whose cards include custom ones
                var array = new System.Text.Json.Nodes.JsonArray();
                foreach (var d in _table.Decks.OrderBy(d => d.Id))
                {
                    var game = _baseline.Find(d.Id);
                    if (game == null)
                    {
                        var node = DeckJson.ToJson(d, withCards: HasCustom(d), _unlockedFlags.TryGetValue(d.Id, out bool u) && u);
                        node["native"] = true;
                        array.Add(node);
                    }
                    else if (HasCustom(d) && !game.SameCards(d))
                        array.Add(DeckJson.ToJson(d, withCards: true, null));
                }
                int count = array.Count;
                if (count > 0)
                    DeckJson.Save(JsonPath, new System.Text.Json.Nodes.JsonObject
                    {
                        ["note"] = "Decks made or changed by WolfX. All of them are in main/deckdata_<lang>.bin and decks.zib (the Ex patch); \"native\" = a new deck " +
                            "(Yu-Gi-Oh-Campaign sets \"unlocked\" in the save). \"cards\" (Konami ids) = a list with custom cards, which the .ydc can't hold.",
                        ["decks"] = array,
                    });
                else if (File.Exists(JsonPath))
                    File.Delete(JsonPath);
                _changed = false;
                Refill();
                _status.Text = (write.Count > 0 ? $"Saved {string.Join(", ", write.Keys.Select(Path.GetFileName))} into {_gameFiles.Describe(DeckDataTable.GamePath('E'))}" : "The game's decks are unchanged") +
                    (count > 0 ? $"; {count} decks to {JsonPath} (Yu-Gi-Oh-Campaign applies it)." : ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        // ---- list ----

        private Deck? Selected() =>
            _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;

        private void Refill(uint? select = null)
        {
            if (_table == null)
                return;
            select ??= Selected()?.Id;
            string find = _find.Text.Trim();
            int filter = _filter.SelectedIndex;   // 0 all, 1-6 a series, 7 starters, 8 changed/new
            _rows = _table.Decks.OrderBy(d => d.Id).Where(d =>
                (filter < 1 || filter > 6 || d.Series == filter - 1) &&
                (filter != 7 || d.Series < 0) &&
                (filter != 8 || Note(d).Length > 0) &&
                (find.Length == 0 || d.Id.ToString() == find || d.FileName.Contains(find, StringComparison.OrdinalIgnoreCase) ||
                 d.Titles.Values.Any(t => t.Contains(find, StringComparison.OrdinalIgnoreCase)) || OwnerName(d.CharacterId).Contains(find, StringComparison.OrdinalIgnoreCase))).ToList();
            _list.VirtualListSize = _rows.Count;
            _list.SelectedIndices.Clear();
            int index = select is uint wanted ? _rows.FindIndex(d => d.Id == wanted) : -1;
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
            var d = Selected();
            _shown = d?.Id;
            _editor.Enabled = d != null;
            if (d == null)
            {
                _heading.Text = _table == null ? "" : "Pick a deck.";
                return;
            }
            _binding = true;
            try
            {
                bool isNew = _baseline?.Find(d.Id) == null;
                _heading.Text = $"{d.Title(Language)} ({d.Id}){(isNew ? " - new" : "")}";
                _file.Text = d.FileName;
                _owner.Value = (int)d.CharacterId;
                _series.SelectedIndex = Math.Clamp(d.Series + 1, 0, SeriesChoices.Length - 1);
                _sku.Value = d.Sku;
                _unlocked.Visible = isNew;
                _unlocked.Checked = _unlockedFlags.TryGetValue(d.Id, out bool u) && u;
                for (int i = 0; i < DeckDataTable.AllLanguages.Length; i++)
                {
                    char language = DeckDataTable.AllLanguages[i];
                    _texts.Rows[i].Cells[1].Value = d.Titles.GetValueOrDefault(language, "");
                    _texts.Rows[i].Cells[2].Value = d.Texts2.GetValueOrDefault(language, "");
                    _texts.Rows[i].Cells[3].Value = d.Texts3.GetValueOrDefault(language, "");
                }
                _main.Ids = d.Cards?.Main ?? [];
                _extra.Ids = d.Cards?.Extra ?? [];
                _side.Ids = d.Cards?.Side ?? [];
                _cardsNote.Text = d.Cards == null
                    ? (_zib == null ? "No decks.zib, so the cards can't be shown. Adding cards gives the deck its own list." : $"{d.FileName}.ydc isn't in decks.zib: adding cards makes it.")
                    : isNew ? "A new deck: its cards are saved to decks.json (Konami ids); Yu-Gi-Oh-Campaign puts them in the game."
                    : d.Cards.AllCards.Any(card => !GameContent.IsGameCard(card)) ? "It holds custom cards, so its cards are saved to decks.json (Yu-Gi-Oh-Campaign applies them)."
                    : $"Saved into decks.zib as {d.FileName}.ydc.";
                ShowInfo(d);
            }
            finally
            {
                _binding = false;
            }
        }

        private void ShowInfo(Deck d)
        {
            _ownerInfo.Text = _characters == null || _characters.Find((int)d.CharacterId) != null ? "" : "not a character";
            _signature.Text = d.SignatureCard == 0xFFFF ? "(none)" : $"{CardCatalog.NameOf(d.SignatureCard)} ({d.SignatureCard})";
            _usedBy.Text = UsedBy(d);
        }

        private Deck? Shown() => _shown is uint id ? _table?.Find(id) : null;

        private void Edited()
        {
            if (_binding || Shown() is not Deck d)
                return;
            d.FileName = _file.Text.Trim();
            d.CharacterId = (uint)Math.Max(0, _owner.Value);
            d.Series = _series.SelectedIndex - 1;
            d.Sku = _sku.Value;
            if (_unlocked.Visible)
                _unlockedFlags[d.Id] = _unlocked.Checked;
            for (int i = 0; i < DeckDataTable.AllLanguages.Length; i++)
            {
                char language = DeckDataTable.AllLanguages[i];
                void Take(int column, Dictionary<char, string> into)
                {
                    if (_texts.Rows[i].Cells[column].Value is string text && (text.Length > 0 || into.ContainsKey(language)))
                        into[language] = text;
                }
                Take(1, d.Titles);
                Take(2, d.Texts2);
                Take(3, d.Texts3);
            }
            _changed = true;
            ShowInfo(d);
            _list.Invalidate();
        }

        private void CardsEdited()
        {
            if (_binding || Shown() is not Deck d)
                return;
            d.Cards ??= new YdcDeck();
            d.Cards.Main.Clear();
            d.Cards.Main.AddRange(_main.Ids);
            d.Cards.Extra.Clear();
            d.Cards.Extra.AddRange(_extra.Ids);
            d.Cards.Side.Clear();
            d.Cards.Side.AddRange(_side.Ids);
            _changed = true;
            _list.Invalidate();
        }

        private void PickSignature()
        {
            using var picker = new CardPickerDialog(CardCatalog.Get(this), "Signature card", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) == DialogResult.OK && picker.Result.Count > 0)
                SetSignature((ushort)picker.Result[0].Card.Id);
        }

        private void SetSignature(ushort card)
        {
            if (Shown() is not Deck d)
                return;
            d.SignatureCard = card;
            _changed = true;
            ShowInfo(d);
            _list.Invalidate();
        }

        private void DeleteDeck()
        {
            if (_table == null || Selected() is not { } d)
                return;
            bool game = _baseline?.Find(d.Id) != null;
            var users = new List<string>();
            if (_characters != null)
                users.AddRange(_characters.Characters.Where(c => c.Deck == (int)d.Id).Select(c => $"duelist {c.Id} ({c.Name(Language)})"));
            if (_duels != null)
                users.AddRange(_duels.Duels.Where(sd => sd.Decks.Contains((int)d.Id)).Select(sd => $"story duel {sd.Id}"));
            string question = $"Delete deck {d.Id} \"{d.Title(Language)}\"?" +
                (game ? "\n\nIt is one of the game's own: it is taken out of deckdata (the Ex patch; the original stays in YGO_2020.dat)." : "") +
                (users.Count > 0 ? $"\n\nStill used by: {string.Join(", ", users.Take(10))}{(users.Count > 10 ? "..." : "")}. Give them another deck (Characters / Story editor), or they will have none." : "");
            if (MessageBox.Show(this, question, "Delete deck", MessageBoxButtons.YesNo, game || users.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _table.Decks.Remove(d);
            _changed = true;
            Refill();
            _status.Text = $"Deck {d.Id} deleted (saved when you press Save).";
        }

        private void ImportDeck()
        {
            if (_table == null || _gameFiles == null)
                return;
            using var dialog = new OpenFileDialog { Title = "Import a deck", Filter = "Deck files (*.ydk;*.ydc)|*.ydk;*.ydc|All files (*.*)|*.*" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            DeckFiles.Imported cards;
            try
            {
                cards = DeckFiles.Read(dialog.FileName, _gameFiles);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read the deck:\n{ex.Message}", "Import deck", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // into a new deck, or the picked deck's cards
            var target = Selected();
            if (target != null)
            {
                var answer = MessageBox.Show($"Make a new deck from {Path.GetFileName(dialog.FileName)}?\n\nYes: a new deck.\nNo: replace the cards of \"{target.Title(Language)}\" ({target.Id}).",
                    "Import deck", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel)
                    return;
                if (answer == DialogResult.Yes)
                    target = null;
            }
            if (target == null)
            {
                NewDeck(null);
                target = Selected();
                if (target == null)
                    return;
                target.Titles['E'] = Path.GetFileNameWithoutExtension(dialog.FileName);
            }
            target.Cards ??= new YdcDeck();
            target.Cards.Main.Clear();
            target.Cards.Main.AddRange(cards.Main.Take(DeckDataTable.MaxMain));
            target.Cards.Extra.Clear();
            target.Cards.Extra.AddRange(cards.Extra.Take(DeckDataTable.MaxExtra));
            target.Cards.Side.Clear();
            target.Cards.Side.AddRange(cards.Side.Take(DeckDataTable.MaxSide));
            _changed = true;
            Refill(target.Id);

            var notes = new List<string>();
            if (cards.Main.Count > DeckDataTable.MaxMain || cards.Extra.Count > DeckDataTable.MaxExtra || cards.Side.Count > DeckDataTable.MaxSide)
                notes.Add($"the game holds {DeckDataTable.MaxMain} / {DeckDataTable.MaxExtra} / {DeckDataTable.MaxSide}: the rest was left out");
            if (cards.Unknown.Count > 0)
                notes.Add($"{cards.Unknown.Count} password(s) match no card: {string.Join(", ", cards.Unknown.Distinct().Take(8))}{(cards.Unknown.Distinct().Count() > 8 ? "..." : "")}");
            _status.Text = $"Imported {cards.Main.Count} / {cards.Extra.Count} / {cards.Side.Count} cards into deck {target.Id}" +
                (notes.Count > 0 ? " - " + string.Join("; ", notes) : ". Save to keep it.");
        }

        private void ExportDeck()
        {
            if (_gameFiles == null || Selected() is not { } d || d.Cards == null)
            {
                _status.Text = "Pick a deck with cards to export.";
                return;
            }
            string name = string.Concat(d.Title(Language).Split(Path.GetInvalidFileNameChars()));
            using var dialog = new SaveFileDialog
            {
                Title = "Export the deck", FileName = name.Length > 0 ? name : d.FileName, DefaultExt = "ydk",
                Filter = "YGOPro deck (*.ydk)|*.ydk|Game deck (*.ydc)|*.ydc",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                DeckFiles.Write(dialog.FileName, d.Cards, _gameFiles, d.Title(Language));
                _status.Text = $"Exported to {dialog.FileName}.";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not write the deck:\n{ex.Message}", "Export deck", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void NewDeck(Deck? copyOf)
        {
            if (_table == null)
                return;
            if (_table.FreeId() is not uint id)
            {
                _status.Text = $"All {DeckDataTable.SlotCount} deck ids are used.";
                return;
            }
            var d = copyOf?.Clone() ?? new Deck { Series = 0, Sku = 1, Cards = new YdcDeck() };
            d.Id = d.Slot = id;
            d.FileName = copyOf != null ? $"{copyOf.FileName}_{id}" : $"custom_deck_{id}";
            if (copyOf == null)
                d.Titles['E'] = "New Deck";
            _table.Decks.Add(d);
            _changed = true;
            _find.Text = "";
            Refill(id);
            _status.Text = $"Deck {id} added. Assign it in Characters. Yu-Gi-Oh-Campaign permits shared Free Duel decks.";
        }
    }
}