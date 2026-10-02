using System.IO;
using CARD_Named;
using Types;
using WolfX.Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The card links table (bin/CARD_Link.bin, File Type Libraries/CardLink): which cards, archetypes and counters each card's text
    /// mentions. Left: the cards with links; right: the selected card's targets, with buttons to add a card, an archetype or a counter.
    /// Opens the game's table with Yu-Gi-Oh-Ex\cardlinks.json on top. Saving puts every link between things the game has into CARD_Link.bin,
    /// and the links to or from a custom card or archetype into cardlinks.json.
    /// The game loads this file but no code reads it (docs/CardLink.md), so edits don't change anything in the game yet.
    /// </summary>
    public sealed class CardLinkEditor : UserControl, IGameEditor, ICardFocus
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _save, _newCard;
        private readonly ToolStripComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        private readonly ToolStripTextBox _find = new() { Width = 170, ToolTipText = "Konami id or part of a card name (the card or one of its targets)" };
        private readonly ListView _cards = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false,
            MultiSelect = false,
        };
        private readonly ListView _targets = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = true,
        };
        private readonly Label _heading = new() { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        private readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Bottom, Height = 34, WrapContents = false };
        private readonly Button _addCard = new() { Text = "Add card...", AutoSize = true };
        private readonly Button _addArchetype = new() { Text = "Archetypes...", AutoSize = true };
        private readonly Button _addCounter = new() { Text = "Add counter...", AutoSize = true };
        private readonly Button _remove = new() { Text = "Remove", AutoSize = true };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private sealed record Row(int Card, int Count, string Note);

        private CardLinkTable? _table;
        private CardLinkTable? _baseline;            // the game's own table
        private byte[] _saved = [];
        private GameFolderFiles? _gameFiles;
        private readonly SplitContainer _split = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        private readonly HashSet<int> _newCards = []; // cards added with "New card..." that have no links yet
        private HashSet<string> _openedProblems = [];   // problems the file already had (the game's has a duplicate link)
        private List<Row> _rows = [];

        private string JsonPath => _gameFiles!.ExPath(CardLinkJson.FileName);

        public IReadOnlyCollection<string> Files => [CardLinkTable.GamePath];

        public string SavesTo => $"Standard: {CardLinkTable.GamePath}. Additional (links with custom cards or archetypes): Yu-Gi-Oh-Ex\\{CardLinkJson.FileName} (no plugin reads it yet; neither does the game).";

        public CardLinkEditor()
        {

            ListPick.FirstWhenShown(_cards);   // open on the first entry, not an empty "Pick a ..." panel
            _save = Button("Save", "Links between the game's own cards into CARD_Link.bin, links with custom cards or archetypes into cardlinks.json (Ctrl+S)", () => Save());
            _newCard = Button("New card...", "Give links to a card that has none yet (a custom card, for example)", NewCard);
            _tools.Items.AddRange([_save, new ToolStripSeparator(), new ToolStripLabel("Show:")]);
            _filter.Items.AddRange(["All cards with links", "Changed", "Problems"]);
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += (_, _) => Refill();
            _tools.Items.Add(_filter);
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _find.TextChanged += (_, _) => Refill();
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(_newCard);

            _cards.Columns.Add("Konami id", 75);
            _cards.Columns.Add("Card", 260);
            _cards.Columns.Add("Links", 50);
            _cards.Columns.Add("", 120);
            _cards.RetrieveVirtualItem += (_, e) =>
            {
                var row = _rows[e.ItemIndex];
                e.Item = new ListViewItem([row.Card.ToString(), CardName(row.Card), row.Count.ToString(), row.Note]);
            };
            _cards.SelectedIndexChanged += (_, _) => ShowTargets();

            _targets.Columns.Add("Target", 70);
            _targets.Columns.Add("Kind", 75);
            _targets.Columns.Add("Name", 280);
            _targets.Columns.Add("", 110);
            _targets.SelectedIndexChanged += (_, _) => _remove.Enabled = _targets.SelectedItems.Count > 0;
            _targets.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                    RemoveSelected();
            };

            _addCard.Click += (_, _) => AddCards();
            _addArchetype.Click += (_, _) => PickArchetypes();
            _addCounter.Click += (_, _) => AddCounter();
            _remove.Click += (_, _) => RemoveSelected();
            _buttons.Controls.AddRange([_addCard, _addArchetype, _addCounter, _remove]);

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_targets);
            right.Controls.Add(_heading);
            right.Controls.Add(_buttons);
            _split.Panel1.Controls.Add(_cards);
            _split.Panel2.Controls.Add(right);

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(_split);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) => _split.SplitterDistance = Math.Max(250, _split.Width * 45 / 100);
            SetEditable(false);
            _status.Text = "Open the game data (File > Open).";
        }

        public bool Dirty => _table != null && !_table.ToBytes().AsSpan().SequenceEqual(_saved);

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
            _buttons.Enabled = editable;
            _newCard.Enabled = editable;
            _save.Enabled = editable;
        }

        // ---- names ----

        private static string CardName(int id) => CardCatalog.NameOf(id);

        private static string TargetName(int target, CardLinkTargetKind kind) => kind switch
        {
            CardLinkTargetKind.Card => CardName(target),
            CardLinkTargetKind.Archetype => Card_Named.NameOf(target),
            CardLinkTargetKind.Counter => CardLinkTable.CounterName(target),
            _ => "",
        };

        // ---- opening / saving ----

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
            _filter.SelectedIndex = 0;
            _find.Text = "";
            Refill(konamiId);
        }

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            var bytes = files.Read(CardLinkTable.GamePath);
            if (bytes == null)
            {
                _table = _baseline = null;
                SetEditable(false);
                _status.Text = $"The open data has no {CardLinkTable.GamePath}.";
                return;
            }
            try
            {
                _baseline = CardLinkTable.Parse(bytes);
                _table = _baseline.Clone();
                int fromJson = CardLinkJson.Apply(CardLinkJson.Load(JsonPath), _table);
                Opened(files.Describe(CardLinkTable.GamePath), fromJson);
            }
            catch (InvalidDataException ex)
            {
                _status.Text = ex.Message;
            }
        }

        private void Opened(string from, int fromJson)
        {
            _saved = _table!.ToBytes();
            _openedProblems = [.. _table.Problems()];
            _newCards.Clear();
            SetEditable(true);
            Refill();
            var problems = _table.Problems();
            _status.Text = $"{from}: {_table.Links.Count} links from {_table.Cards().Count} cards" +
                (fromJson > 0 ? $"; {fromJson} changes from {CardLinkJson.FileName}" : "") +
                (problems.Count > 0 ? $". {problems.Count} problems: {problems[0]}" : ", no problems") +
                ". (The game loads this file but doesn't use it yet.)";
        }

        /// <summary>A link the game's own file can hold: between a game card and a game card, a game archetype or a counter.</summary>
        private static bool IsStandard(CardLink link) =>
            GameContent.IsGameCard(link.Card) && CardLinkTable.KindOf(link.Target) switch
            {
                CardLinkTargetKind.Card => GameContent.IsGameCard(link.Target),
                CardLinkTargetKind.Archetype => link.Target < GameContent.FirstCustomArchetype,
                _ => true,
            };

        /// <summary>
        /// Standard content: the links between things the game has go into bin\CARD_Link.bin (removing a game link removes it there).
        /// Additional content: links with a custom card or archetype go to cardlinks.json; it is deleted when there are none.
        /// </summary>
        public bool Save()
        {
            if (_table == null || _baseline == null || _gameFiles == null)
                return false;
            var problems = _table.Problems().Where(p => !_openedProblems.Contains(p)).ToList();
            if (problems.Count > 0 && MessageBox.Show(this, "Your changes add problems:\n\n" + string.Join("\n", problems.Take(8)) + "\n\nSave anyway?",
                    "Card links", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;
            try
            {
                var standard = new CardLinkTable();
                standard.Links.AddRange(_table.Links.Where(IsStandard));
                byte[] bytes = standard.ToBytes();
                bool binChanged = !bytes.AsSpan().SequenceEqual(_baseline.ToBytes());
                if (binChanged)
                    _gameFiles.Write(CardLinkTable.GamePath, bytes);
                _baseline = standard;

                var root = CardLinkJson.Diff(standard, _table, TargetName);
                int cards = ((System.Text.Json.Nodes.JsonArray)root["cards"]!).Count;
                if (cards > 0)
                    CardLinkJson.Save(JsonPath, root);
                else if (File.Exists(JsonPath))
                    File.Delete(JsonPath);
                _saved = _table.ToBytes();
                Refill();
                _status.Text = (binChanged ? $"Saved {CardLinkTable.GamePath} into {_gameFiles.Describe(CardLinkTable.GamePath)}" : "CARD_Link.bin unchanged") +
                    (cards > 0 ? $"; links of {cards} cards with custom cards or archetypes to {JsonPath}." : ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        // ---- the card list ----

        private HashSet<int> ChangedCards()
        {
            if (_baseline == null || _table == null)
                return [];
            var before = _baseline.Links.ToHashSet();
            var after = _table.Links.ToHashSet();
            return before.Except(after).Concat(after.Except(before)).Select(link => link.Card).ToHashSet();
        }

        private void Refill(int? select = null)
        {
            if (_table == null)
                return;
            select ??= SelectedCard();
            var counts = _table.Links.GroupBy(link => link.Card).ToDictionary(g => g.Key, g => g.Count());
            foreach (int card in _newCards)
                counts.TryAdd(card, 0);
            var changed = ChangedCards();
            var problemCards = ProblemCards();
            string find = _find.Text.Trim();

            bool Matches(int card)
            {
                if (find.Length == 0 || card.ToString() == find || CardName(card).Contains(find, StringComparison.OrdinalIgnoreCase))
                    return true;
                return _table.TargetsOf(card).Any(target => target.ToString() == find ||
                    TargetName(target, CardLinkTable.KindOf(target)).Contains(find, StringComparison.OrdinalIgnoreCase));
            }

            var rows = new List<Row>();
            foreach (var (card, count) in counts.OrderBy(c => c.Key))
            {
                bool show = _filter.SelectedIndex switch
                {
                    1 => changed.Contains(card) || _newCards.Contains(card),
                    2 => problemCards.Contains(card),
                    _ => true,
                };
                if (!show || !Matches(card))
                    continue;
                string note = _newCards.Contains(card) && count == 0 ? "new (no links yet)"
                    : _baseline != null && !_baseline.Links.Any(link => link.Card == card) ? "new"
                    : changed.Contains(card) ? "changed" : "";
                if (problemCards.Contains(card))
                    note = note.Length > 0 ? note + ", problem" : "problem";
                rows.Add(new Row(card, count, note));
            }
            _rows = rows;
            _cards.VirtualListSize = rows.Count;
            _cards.SelectedIndices.Clear();
            int index = select is int wanted ? rows.FindIndex(row => row.Card == wanted) : -1;
            if (index >= 0)
            {
                _cards.SelectedIndices.Add(index);
                _cards.EnsureVisible(index);
            }
            _cards.Invalidate();
            ShowTargets();
        }

        private HashSet<int> ProblemCards()
        {
            var cards = new HashSet<int>();
            var seen = new HashSet<CardLink>();
            for (int i = 0; i < _table!.Links.Count; i++)
            {
                var link = _table.Links[i];
                if ((i > 0 && link.Card < _table.Links[i - 1].Card) || !seen.Add(link) || link.Card == link.Target ||
                    CardLinkTable.KindOf(link.Target) == CardLinkTargetKind.Unknown)
                    cards.Add(link.Card);
            }
            return cards;
        }

        private int? SelectedCard() =>
            _cards.SelectedIndices.Count > 0 && _cards.SelectedIndices[0] < _rows.Count ? _rows[_cards.SelectedIndices[0]].Card : null;

        // ---- the selected card's targets ----

        private void ShowTargets()
        {
            _targets.BeginUpdate();
            _targets.Items.Clear();
            int? card = SelectedCard();
            if (card is int id && _table != null)
            {
                _heading.Text = $"{CardName(id)} ({id}) mentions:";
                var gameTargets = _baseline?.TargetsOf(id).ToHashSet();
                foreach (int target in _table.TargetsOf(id))
                {
                    var kind = CardLinkTable.KindOf(target);
                    string note = gameTargets != null && !gameTargets.Contains(target) ? "added" : "";
                    _targets.Items.Add(new ListViewItem([target.ToString(), CardLinkTable.KindName(kind), TargetName(target, kind), note]) { Tag = target });
                }
                if (gameTargets != null)
                {
                    foreach (int target in gameTargets.Where(t => !_table.Contains(id, t)))
                    {
                        var kind = CardLinkTable.KindOf(target);
                        _targets.Items.Add(new ListViewItem([target.ToString(), CardLinkTable.KindName(kind), TargetName(target, kind), "removed"])
                        {
                            ForeColor = SystemColors.GrayText, Tag = null,
                        });
                    }
                }
            }
            else
                _heading.Text = _table == null ? "" : "Pick a card.";
            _targets.EndUpdate();
            _buttons.Enabled = card != null;
            _remove.Enabled = false;
        }

        private void Changed(int card, string message)
        {
            Refill(card);
            _status.Text = message;
        }

        private void AddCards()
        {
            if (SelectedCard() is not int card || _table == null)
                return;
            using var picker = new CardPickerDialog(CardCatalog.Get(this), $"Cards {CardName(card)} mentions", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;
            int added = picker.Result.Count(pick => _table.Add(card, pick.Card.Id));
            Changed(card, $"Added {added} card links to {CardName(card)}.");
        }

        /// <summary>Archetypes: the picker shows the card's current archetype targets checked; unchecking one removes it.</summary>
        private void PickArchetypes()
        {
            if (SelectedCard() is not int card || _table == null)
                return;
            var current = _table.TargetsOf(card).Where(t => CardLinkTable.KindOf(t) == CardLinkTargetKind.Archetype).ToList();
            using var dialog = new ArchetypePickerDialog(CardName(card), current,
                () => Card_Named.Names.Keys.Order().Select(code => (code, $"{code}: {Card_Named.NameOf(code)}")));
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            var wanted = dialog.Result.Where(code => code is >= 1 and < CardLinkTable.FirstCounter).ToHashSet();
            int removed = current.Count(code => !wanted.Contains(code) && _table.Remove(card, code));
            int added = wanted.Count(code => _table.Add(card, code));
            Changed(card, $"{CardName(card)}: {added} archetype links added, {removed} removed.");
        }

        private void AddCounter()
        {
            if (SelectedCard() is not int card || _table == null)
                return;
            if (CounterPicker.Pick(this) is int counter)
                Changed(card, _table.Add(card, counter) ? $"{CardName(card)} now mentions {CardLinkTable.CounterName(counter)}." : "That link is already there.");
        }

        private void RemoveSelected()
        {
            if (SelectedCard() is not int card || _table == null)
                return;
            var targets = _targets.SelectedItems.Cast<ListViewItem>().Select(item => item.Tag).OfType<int>().ToList();
            int removed = targets.Count(target => _table.Remove(card, target));
            Changed(card, $"Removed {removed} links from {CardName(card)}.");
        }

        private void NewCard()
        {
            if (_table == null)
                return;
            using var picker = new CardPickerDialog(CardCatalog.Get(this), "Give links to a card", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                return;
            int card = picker.Result[0].Card.Id;
            if (_table.TargetsOf(card).Count == 0)
                _newCards.Add(card);
            _find.Text = "";
            Changed(card, $"{CardName(card)} ({card}): add the cards, archetypes and counters its text mentions.");
        }

        /// <summary>Picks a counter kind: the known ones, or any number 1000 and up.</summary>
        private static class CounterPicker
        {
            public static int? Pick(IWin32Window owner)
            {
                using var form = new Form
                {
                    Text = "Add counter", Size = new Size(360, 460), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false,
                    MinimizeBox = false, MaximizeBox = false,
                };
                var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
                foreach (var (code, name) in CardLinkTable.CounterNames.OrderBy(c => c.Value))
                    list.Items.Add(new Item(code, name));
                var number = new NumericUpDown { Minimum = CardLinkTable.FirstCounter, Maximum = CardLinkTable.FirstCardId - 1, Width = 80, Value = 1000 };
                list.SelectedIndexChanged += (_, _) =>
                {
                    if (list.SelectedItem is Item item)
                        number.Value = item.Code;
                };
                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, Padding = new Padding(4) };
                bottom.Controls.AddRange([new Label { Text = "Code:", AutoSize = true, Margin = new Padding(3, 8, 3, 3) }, number, ok, cancel]);
                list.DoubleClick += (_, _) => form.DialogResult = DialogResult.OK;
                form.Controls.Add(list);
                form.Controls.Add(bottom);
                form.AcceptButton = ok;
                form.CancelButton = cancel;
                return form.ShowDialog(owner) == DialogResult.OK ? (int)number.Value : null;
            }

            private sealed record Item(int Code, string Name)
            {
                public override string ToString() => $"{Name} ({Code})";
            }
        }
    }
}
