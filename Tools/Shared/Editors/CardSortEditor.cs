using System.IO;
using StartingCollection;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The trunk's "sort by name" order (bin/CARD_Sort_# + CARD_Sort2_#, File Type Libraries/CardSort): the cards in each language's name
    /// order, whether that still matches the names (it goes stale when cards are renamed), and a rebuild from the names for the
    /// Latin-script languages, saved into the game data. Custom cards have no place in these files: Yu-Gi-Oh-MoreCards ranks them
    /// (NameSortRankFor, FULL_CARD_PROPS +0x18) using their cards.json names.
    /// </summary>
    public sealed class CardSortEditor : UserControl, IGameEditor
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly ToolStripButton _rebuild, _rebuildAll;
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false,
        };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private readonly Dictionary<char, CardNameSort> _tables = [];
        private readonly Dictionary<char, CardDatabase> _names = [];
        private readonly HashSet<char> _changed = [];
        private Dictionary<int, int> _konamiByInternal = [];
        private GameFolderFiles? _gameFiles;
        private char _current = 'E';

        public bool Dirty => _changed.Count > 0;

        public IReadOnlyCollection<string> Files =>
            [CardIdMap.GamePath, .. CardNameSort.Languages.SelectMany(l => new[] { SortPath(l), Sort2Path(l) })];

        public string SavesTo => "Standard: bin\\CARD_Sort_<lang>.bin and CARD_Sort2_<lang>.bin. Custom cards are ranked by Yu-Gi-Oh-MoreCards from their cards.json names.";

        private static string SortPath(char language) => Path.Combine(CardNameSort.GameFolder, CardNameSort.SortName(language));

        private static string Sort2Path(char language) => Path.Combine(CardNameSort.GameFolder, CardNameSort.Sort2Name(language));

        public CardSortEditor()
        {
            _tools.Items.Add(Button("Save", "Save the languages you rebuilt into the game data", () => Save()));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("Language:"));
            _language.SelectedIndexChanged += (_, _) =>
            {
                if (_language.SelectedItem is LanguageItem item)
                    Show(item.Letter);
            };
            _tools.Items.Add(_language);
            _rebuild = Button("Rebuild from names", "Sort this language's cards by their names again (after renaming cards)", () => Rebuild([_current]));
            _rebuildAll = Button("Rebuild all", "Rebuild every language sorted by name as written (E F G I S)", () => Rebuild(CardNameSort.RebuildableLanguages));
            _tools.Items.Add(_rebuild);
            _tools.Items.Add(_rebuildAll);

            _list.Columns.Add("Position", 70);
            _list.Columns.Add("Internal id", 80);
            _list.Columns.Add("Konami id", 80);
            _list.Columns.Add("Name", 420);
            _list.Columns.Add("", 120);
            _list.RetrieveVirtualItem += (_, e) => e.Item = e.ItemIndex < _tables[_current].Count ? Row(e.ItemIndex) : new ListViewItem(new string[_list.Columns.Count]);   // stale index while the list shrinks

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(_list);
            Controls.Add(_tools);
            Controls.Add(status);
            _status.Text = "Open the game data (File > Open).";
        }

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        private sealed record LanguageItem(char Letter)
        {
            public override string ToString() => $"{HowToPlayFile.LanguageName(Letter)} ({Letter})";
        }

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            _tables.Clear();
            _names.Clear();
            _changed.Clear();
            _language.Items.Clear();
            _list.VirtualListSize = 0;
            if (files.Read(CardIdMap.GamePath) is not { } intId)
            {
                _status.Text = "The open data has no bin\\CARD_IntID.bin (needed to name the cards).";
                return;
            }
            _konamiByInternal = CardNameSort.KonamiIdsByInternal(intId);
            foreach (char language in CardNameSort.Languages)
            {
                try
                {
                    if (files.Read(SortPath(language)) is { } sort && files.Read(Sort2Path(language)) is { } sort2)
                    {
                        _tables[language] = CardNameSort.Parse(sort, sort2);
                        _language.Items.Add(new LanguageItem(language));
                    }
                }
                catch (InvalidDataException) { }
            }
            if (_language.Items.Count > 0)
                _language.SelectedIndex = 0;
            else
                _status.Text = "The open data has no bin\\CARD_Sort_#.bin files.";
        }

        /// <summary>The names for a language (read once, from the open data).</summary>
        private CardDatabase Names(char language)
        {
            if (!_names.TryGetValue(language, out var database))
                _names[language] = database = CardDatabase.Load(_gameFiles!.GameFolder, language.ToString());
            return database;
        }

        private string NameOfInternal(char language, int internalId) =>
            internalId != 0 && _konamiByInternal.TryGetValue(internalId, out int konami) ? Names(language).NameOf(konami) : "";

        private void Show(char language)
        {
            _current = language;
            var table = _tables[language];
            _list.VirtualListSize = table.Count;
            _list.Invalidate();
            bool rebuildable = CardNameSort.RebuildableLanguages.Contains(language);
            _rebuild.Enabled = rebuildable;
            int outOfOrder = rebuildable ? table.OutOfOrder(id => NameOfInternal(language, id)) : -1;
            _status.Text = $"{HowToPlayFile.LanguageName(language)}: {table.Count} cards, " +
                (table.IsConsistent() ? "the two files agree" : "the two files DON'T agree (rebuild them)") + ", " +
                (rebuildable ? $"{outOfOrder} out of name order" : "sorted by the Japanese reading (not rebuilt here)") +
                (_changed.Contains(language) ? ". Rebuilt, not saved." : ".");
        }

        private ListViewItem Row(int position)
        {
            var table = _tables[_current];
            int internalId = table.Order[position];
            int konami = _konamiByInternal.GetValueOrDefault(internalId);
            string name = NameOfInternal(_current, internalId);
            string note = position > 1 && CardNameSort.RebuildableLanguages.Contains(_current) &&
                          StringComparer.OrdinalIgnoreCase.Compare(NameOfInternal(_current, table.Order[position - 1]), name) > 0 ? "out of order" : "";
            return new ListViewItem([position.ToString(), internalId.ToString(), konami == 0 ? "" : konami.ToString(), name, note]);
        }

        private void Rebuild(IEnumerable<char> languages)
        {
            if (_gameFiles == null)
                return;
            var done = new List<string>();
            foreach (char language in languages.Where(_tables.ContainsKey))
            {
                int moved = _tables[language].Rebuild(id => NameOfInternal(language, id));
                if (moved > 0)
                    _changed.Add(language);
                done.Add($"{language}: {moved} moved");
            }
            Show(_current);
            _status.Text = "Rebuilt " + string.Join(", ", done) + ". Save to write the files.";
        }

        public bool Save()
        {
            if (_gameFiles == null || _changed.Count == 0)
            {
                _status.Text = "Nothing to save.";
                return true;
            }
            try
            {
                var write = new Dictionary<string, byte[]>();
                foreach (char language in _changed)
                {
                    var (sort, sort2) = _tables[language].ToBytes();
                    write[SortPath(language)] = sort;
                    write[Sort2Path(language)] = sort2;
                }
                _gameFiles.Write(write);
                _status.Text = $"Saved the name order for {string.Join(", ", _changed)} into {_gameFiles.Describe(SortPath(_changed.First()))}.";
                _changed.Clear();
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }
    }
}
