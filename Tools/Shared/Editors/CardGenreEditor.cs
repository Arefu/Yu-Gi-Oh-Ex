using System.IO;
using Types;
using WolfX.Types;

namespace Wolf.Editors
{
    /// <summary>
    /// Card genres (bin/CARD_Genre.bin, File Type Libraries/CardGenre, docs/CardGenre.md): the effect categories on the card details page
    /// ("Recover LP", "Special Summon"...), which the duel code (AI) also checks. Left: the cards; right: the selected card's genres as check
    /// boxes. The file is indexed by internal id, so CARD_IntID.bin is needed to know which card is which.
    /// Opens the game's genres with Yu-Gi-Oh-Ex\genres.json on top. Saving puts every card the game has (CARD_IntID.bin) into CARD_Genre.bin
    /// and only the cards it has no place for (custom cards) into genres.json, which Yu-Gi-Oh-MoreCards applies.
    /// </summary>
    public sealed class CardGenreEditor : UserControl, IGameEditor, ICardFocus
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _save, _newCard;
        private readonly ToolStripComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        private readonly ToolStripTextBox _find = new() { Width = 170, ToolTipText = "Konami id or part of a card name" };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false, MultiSelect = false,
        };
        private readonly CheckedListBox _genres = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        private readonly Label _heading = new() { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        private readonly Label _hint = new()
        {
            Dock = DockStyle.Bottom, Height = 34, ForeColor = SystemColors.GrayText,
            Text = "\"hidden\": the game clears these bits when it reads the file. \"unused\": the game has a name for it but no card uses it.",
        };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private sealed record Row(int Card, ulong Mask, string Note);

        private CardIdMap? _idMap;
        private CardGenreTable? _table;                           // as read
        private Dictionary<int, ulong> _cards = [], _baseline = [];
        private readonly HashSet<int> _newCards = [];
        private List<Row> _rows = [];
        private GameFolderFiles? _gameFiles;
        private bool _changed, _showing;
        private readonly SplitContainer _split = new() { Dock = DockStyle.Fill };

        private string JsonPath => _gameFiles!.ExPath(CardGenreJson.FileName);

        public bool Dirty => _changed;

        public IReadOnlyCollection<string> Files => [CardGenreTable.GamePath, CardIdMap.GamePath];

        public string SavesTo => $"Standard: {CardGenreTable.GamePath}. Additional (cards the game doesn't have): Yu-Gi-Oh-Ex\\{CardGenreJson.FileName} (needs Yu-Gi-Oh-MoreCards).";

        public CardGenreEditor()
        {

            ListPick.FirstWhenShown(_list);   // open on the first entry, not an empty "Pick a ..." panel
            _save = Button("Save", "The game's cards into CARD_Genre.bin, custom cards into genres.json (Ctrl+S)", () => Save());
            _newCard = Button("New card...", "Give genres to a card that has none yet (a custom card, for example)", NewCard);
            _tools.Items.AddRange([_save, new ToolStripSeparator(), new ToolStripLabel("Show:")]);
            _filter.Items.AddRange(["Cards with genres", "Changed", "All cards"]);
            foreach (var genre in CardGenreTable.Genres)
                _filter.Items.Add($"Has: {genre.Name}");
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += (_, _) => Refill();
            _tools.Items.Add(_filter);
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _find.TextChanged += (_, _) => Refill();
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(_newCard);

            _list.Columns.Add("Konami id", 75);
            _list.Columns.Add("Card", 240);
            _list.Columns.Add("Genres", 420);
            _list.Columns.Add("", 90);
            _list.RetrieveVirtualItem += (_, e) =>
            {
                var row = _rows[e.ItemIndex];
                e.Item = new ListViewItem([row.Card.ToString(), CardCatalog.NameOf(row.Card), CardGenreTable.Describe(row.Mask), row.Note]);
            };
            _list.SelectedIndexChanged += (_, _) => ShowCard();

            foreach (var genre in CardGenreTable.Genres)
                _genres.Items.Add($"{genre.Bit,2}  {genre.Name}  ({genre.Key}){(genre.Hidden ? "  - hidden" : genre.Unused ? "  - unused" : "")}");
            _genres.ItemCheck += (_, e) => GenreChecked(e.Index, e.NewValue == CheckState.Checked);

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_genres);
            right.Controls.Add(_heading);
            right.Controls.Add(_hint);
            _split.Panel1.Controls.Add(_list);
            _split.Panel2.Controls.Add(right);

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(_split);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) => _split.SplitterDistance = Math.Max(300, _split.Width * 62 / 100);
            SetEditable(false);
            _status.Text = "Open the game data (File > Open).";
        }

        public bool CardOnly
        {
            get => _split.Panel1Collapsed;
            set
            {
                _split.Panel1Collapsed = value;
                _tools.Visible = !value;
            }
        }

        public void ShowCard(int konamiId)
        {
            if (_table == null)
                return;
            if (!_rows.Any(row => row.Card == konamiId))
                _newCards.Add(konamiId);
            if (_filter.SelectedIndex >= 3)
                _filter.SelectedIndex = 0;
            _find.Text = "";
            Refill(konamiId);
        }

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            var genre = files.Read(CardGenreTable.GamePath);
            var intIds = files.Read(CardIdMap.GamePath);
            if (genre == null || intIds == null)
            {
                _table = null;
                SetEditable(false);
                _status.Text = "The open data has no bin\\CARD_Genre.bin or bin\\CARD_IntID.bin.";
                return;
            }
            Open(genre, intIds, files.Describe(CardGenreTable.GamePath));
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

        private void SetEditable(bool editable)
        {
            _save.Enabled = _newCard.Enabled = editable;
            _genres.Enabled = editable && SelectedCard() != null;
        }

        // ---- opening / saving ----

        private void Open(byte[] genre, byte[] intIds, string from)
        {
            try
            {
                CardCatalog.Get(this);
                _idMap = CardIdMap.Parse(intIds);
                _table = CardGenreTable.Parse(genre);
                _cards = [];
                foreach (var (internalId, konami) in _idMap.KonamiByInternal())
                    if (internalId < CardGenreTable.EntryCount)
                        _cards[konami] = _table.Masks[internalId];
                _baseline = new Dictionary<int, ulong>(_cards);   // the game's, before genres.json
                var unknown = new List<string>();
                int fromJson = CardGenreJson.Apply(CardGenreJson.Load(JsonPath), _cards, unknown);
                _newCards.Clear();
                _changed = false;
                SetEditable(true);
                Refill();
                _status.Text = $"{from}: {_cards.Count(c => c.Value != 0)} cards with genres" + (fromJson > 0 ? $"; {fromJson} cards from {CardGenreJson.FileName}" : "") +
                    (unknown.Count > 0 ? $". Unknown genres in the JSON: {string.Join(", ", unknown.Distinct().Take(5))}" : "") + ".";
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                _status.Text = ex.Message;
            }
        }

        /// <summary>
        /// Standard content: every card the game has (in CARD_IntID.bin) goes into bin\CARD_Genre.bin, which is indexed by internal id.
        /// Additional content: the cards it has no place for (custom cards) go to genres.json; it is deleted when there are none.
        /// </summary>
        public bool Save()
        {
            if (_table == null || _idMap == null || _gameFiles == null)
                return false;
            try
            {
                var table = new CardGenreTable();
                _table.Masks.CopyTo(table.Masks, 0);
                var inBin = new Dictionary<int, ulong>();
                foreach (var (internalId, konami) in _idMap.KonamiByInternal())
                {
                    if (internalId >= CardGenreTable.EntryCount)
                        continue;
                    table.Masks[internalId] = _cards.GetValueOrDefault(konami);
                    inBin[konami] = table.Masks[internalId];
                }
                byte[] bytes = table.ToBytes();
                bool binChanged = !bytes.AsSpan().SequenceEqual(_table.ToBytes());
                if (binChanged)
                    _gameFiles.Write(CardGenreTable.GamePath, bytes);
                _table = table;

                var root = CardGenreJson.Diff(inBin, _cards, id => CardCatalog.NameOf(id));
                int custom = ((System.Text.Json.Nodes.JsonArray)root["cards"]!).Count;
                if (custom > 0)
                    CardGenreJson.Save(JsonPath, root);
                else if (File.Exists(JsonPath))
                    File.Delete(JsonPath);
                _baseline = new Dictionary<int, ulong>(_cards);
                _changed = false;
                Refill();
                _status.Text = (binChanged ? $"Saved {CardGenreTable.GamePath} into {_gameFiles.Describe(CardGenreTable.GamePath)}" : "CARD_Genre.bin unchanged") +
                    (custom > 0 ? $"; {custom} custom cards to {JsonPath} (Yu-Gi-Oh-MoreCards applies it)." : ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        // ---- list ----

        private void Refill(int? select = null)
        {
            if (_table == null)
                return;
            select ??= SelectedCard();
            string find = _find.Text.Trim();
            int filter = _filter.SelectedIndex;
            IEnumerable<int> cards = filter switch
            {
                1 => _cards.Keys.Where(k => _cards[k] != _baseline.GetValueOrDefault(k) || !_baseline.ContainsKey(k)).Concat(_newCards),
                2 => CardCatalog.Get(this).Cards.Select(c => c.Id).Concat(_cards.Keys).Concat(_newCards),
                >= 3 => _cards.Where(c => CardGenreTable.Has(c.Value, filter - 3)).Select(c => c.Key),
                _ => _cards.Where(c => c.Value != 0).Select(c => c.Key).Concat(_newCards),
            };
            _rows = cards.Distinct().Order()
                .Where(card => find.Length == 0 || card.ToString() == find || CardCatalog.NameOf(card).Contains(find, StringComparison.OrdinalIgnoreCase))
                .Select(card =>
                {
                    ulong mask = _cards.GetValueOrDefault(card);
                    string note = !_baseline.ContainsKey(card) ? "new" : mask != _baseline.GetValueOrDefault(card) ? "changed" : "";
                    return new Row(card, mask, note);
                }).ToList();
            _list.VirtualListSize = _rows.Count;
            _list.SelectedIndices.Clear();
            int index = select is int wanted ? _rows.FindIndex(row => row.Card == wanted) : -1;
            if (index >= 0)
            {
                _list.SelectedIndices.Add(index);
                _list.EnsureVisible(index);
            }
            _list.Invalidate();
            ShowCard();
        }

        private int? SelectedCard() =>
            _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]].Card : null;

        private void ShowCard()
        {
            _showing = true;
            try
            {
                int? card = SelectedCard();
                ulong mask = card is int id ? _cards.GetValueOrDefault(id) : 0;
                for (int i = 0; i < CardGenreTable.Genres.Count; i++)
                    _genres.SetItemChecked(i, CardGenreTable.Has(mask, CardGenreTable.Genres[i].Bit));
                _heading.Text = card is int c ? $"{CardCatalog.NameOf(c)} ({c}):" : _table == null ? "" : "Pick a card.";
                _genres.Enabled = card != null && _table != null;
            }
            finally
            {
                _showing = false;
            }
        }

        private void GenreChecked(int index, bool on)
        {
            if (_showing || SelectedCard() is not int card)
                return;
            var genre = CardGenreTable.Genres[index];
            _cards[card] = CardGenreTable.With(_cards.GetValueOrDefault(card), genre.Bit, on);
            _changed = true;
            _status.Text = $"{CardCatalog.NameOf(card)}: {(on ? "added" : "removed")} {genre.Name}." +
                (genre.Hidden ? " (The game clears this bit when it reads the file.)" : "");
            int at = _rows.FindIndex(row => row.Card == card);
            if (at >= 0)
                _rows[at] = _rows[at] with { Mask = _cards[card] };
            _list.Invalidate();
        }

        private void NewCard()
        {
            if (_table == null)
                return;
            using var picker = new CardPickerDialog(CardCatalog.Get(this), "Give genres to a card", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                return;
            int card = picker.Result[0].Card.Id;
            _newCards.Add(card);
            _cards.TryAdd(card, 0);
            _find.Text = "";
            if (_filter.SelectedIndex >= 3)
                _filter.SelectedIndex = 0;
            Refill(card);
            _status.Text = $"{CardCatalog.NameOf(card)} ({card}): tick its genres." +
                (_idMap!.InternalOf(card) == 0 ? $" It isn't in CARD_IntID.bin, so it is saved to {CardGenreJson.FileName}." : "");
        }
    }
}
