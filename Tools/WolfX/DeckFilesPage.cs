using DeckData;
using Wolf.Editors;
using WolfX.Types;

namespace WolfX
{
    /// <summary>
    /// The .ydc deck files in decks.zib: every one, with its card counts and which of the game's decks (deckdata, the Decks page) uses it -
    /// most are the Decks page's decks, but the archive also has files no deck points at. Pick one to see and edit its main / extra / side
    /// deck; export it as a .ydc, replace it from one, or add a .ydc to the archive. Saved into decks.zib in the game data.
    /// </summary>
    internal sealed class DeckFilesPage : UserControl, IGameEditor
    {
        private const string Zib = "decks.zib";

        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripTextBox _find = new() { Width = 160, ToolTipText = "Part of a file name or of the deck's title" };
        private readonly ToolStripComboBox _show = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, MultiSelect = false, VirtualMode = true,
        };
        private readonly CardListEditor _main = new() { Title = "Main deck (up to 60):", Capacity = 60, Dock = DockStyle.Fill };
        private readonly CardListEditor _extra = new() { Title = "Extra deck (up to 15):", Capacity = 15, Dock = DockStyle.Fill };
        private readonly CardListEditor _side = new() { Title = "Side deck (up to 15):", Capacity = 15, Dock = DockStyle.Fill };
        private readonly Label _heading = new() { Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 11f, FontStyle.Bold), Padding = new Padding(4, 4, 0, 0), UseMnemonic = false };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private GameFolderFiles? _files;
        private global::Types.ZibArchive? _archive;
        private readonly Dictionary<string, List<Deck>> _usedBy = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _changed = new(StringComparer.Ordinal);
        private List<global::Types.ZibArchive.Entry> _rows = [];
        private global::Types.ZibArchive.Entry? _shown;
        private bool _binding;

        public DeckFilesPage()
        {
            _show.Items.AddRange(["All deck files", "Not used by a deck", "Used by a deck", "Changed"]);
            _show.SelectedIndex = 0;
            _tools.Items.Add(Button("Save", "Save decks.zib into the game data (Ctrl+S)", () => Save()));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripLabel("Show:"));
            _tools.Items.Add(_show);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("Export .ydc...", "Save the picked deck file to disk", Export));
            _tools.Items.Add(Button("Replace from .ydc...", "Replace the picked deck's cards with a .ydc file's", ReplaceFrom));
            _tools.Items.Add(Button("Add .ydc...", "Add .ydc files to decks.zib (a deck uses one when its file name is set on the Decks page)", AddFiles));

            _list.Columns.Add("File", 200);
            _list.Columns.Add("Main", 46, HorizontalAlignment.Right);
            _list.Columns.Add("Extra", 46, HorizontalAlignment.Right);
            _list.Columns.Add("Side", 46, HorizontalAlignment.Right);
            _list.Columns.Add("Used by", 260);
            _list.RetrieveVirtualItem += (_, e) =>
            {
                var entry = _rows[e.ItemIndex];
                var deck = Parse(entry);
                e.Item = new ListViewItem([entry.Name, $"{deck?.Main.Count}", $"{deck?.Extra.Count}", $"{deck?.Side.Count}", UsedBy(entry)])
                {
                    ForeColor = _changed.Contains(entry.Name) ? Color.FromArgb(170, 90, 0) : _usedBy.ContainsKey(Stem(entry)) ? SystemColors.WindowText : SystemColors.GrayText,
                };
            };
            _list.SelectedIndexChanged += (_, _) => ShowSelected();
            _find.TextChanged += (_, _) => Refill();
            _show.SelectedIndexChanged += (_, _) => Refill();
            foreach (var cards in new[] { _main, _extra, _side })
                cards.Changed += (_, _) => Edited();

            // main on the left, extra above side on the right
            var decks = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            decks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            decks.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            decks.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            decks.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            decks.Controls.Add(_main, 0, 0);
            decks.SetRowSpan(_main, 2);
            decks.Controls.Add(_extra, 1, 0);
            decks.Controls.Add(_side, 1, 1);
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(decks);
            right.Controls.Add(_heading);
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(right);
            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) => split.SplitterDistance = Math.Clamp(Width * 45 / 100, 300, 620);
            ListPick.FirstWhenShown(_list);
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

        private static string Stem(global::Types.ZibArchive.Entry entry) => Path.GetFileNameWithoutExtension(entry.Name);

        private static YdcDeck? Parse(global::Types.ZibArchive.Entry entry)
        {
            try { return YdcDeck.Parse(entry.Data); }
            catch (InvalidDataException) { return null; }
        }

        private string UsedBy(global::Types.ZibArchive.Entry entry) =>
            _usedBy.TryGetValue(Stem(entry), out var decks) ? string.Join(", ", decks.Select(d => $"{d.Id} {d.Title()}")) : "(not used by any deck)";

        // ---- IGameEditor ----

        public bool Dirty => _changed.Count > 0;

        public IReadOnlyCollection<string> Files => [Zib, .. DeckDataTable.AllLanguages.Select(DeckDataTable.GamePath)];

        public string SavesTo => "Standard: decks.zib (the game's .ydc deck files). Which deck uses which file, its owner and titles are on Campaign > Decks.";

        public void Open(GameFolderFiles files)
        {
            _files = files;
            _changed.Clear();
            _usedBy.Clear();
            _archive = null;
            try
            {
                _archive = files.Read(Zib) is { } zib ? global::Types.ZibArchive.Parse(zib) : null;
                var tables = DeckDataTable.AllLanguages.Where(l => files.Exists(DeckDataTable.GamePath(l)))
                    .ToDictionary(l => l, l => files.Read(DeckDataTable.GamePath(l))!);
                if (tables.Count > 0)
                    foreach (var deck in DeckDataTable.Parse(tables).Decks.Where(d => d.FileName.Length > 0))
                    {
                        if (!_usedBy.TryGetValue(deck.FileName, out var list))
                            _usedBy[deck.FileName] = list = [];
                        list.Add(deck);
                    }
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                _status.Text = "Couldn't read the decks: " + ex.Message;
            }
            Refill();
        }

        private void Refill()
        {
            var all = _archive?.Entries.Where(e => e.Name.EndsWith(".ydc", StringComparison.OrdinalIgnoreCase)).ToList() ?? [];
            string find = _find.Text.Trim();
            _rows = all
                .Where(e => _show.SelectedIndex switch
                {
                    1 => !_usedBy.ContainsKey(Stem(e)),
                    2 => _usedBy.ContainsKey(Stem(e)),
                    3 => _changed.Contains(e.Name),
                    _ => true,
                })
                .Where(e => find.Length == 0 || e.Name.Contains(find, StringComparison.OrdinalIgnoreCase) || UsedBy(e).Contains(find, StringComparison.OrdinalIgnoreCase))
                .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            string? keep = _shown?.Name;
            _binding = true;
            try
            {
                _list.SelectedIndices.Clear();
                _list.VirtualListSize = _rows.Count;
            }
            finally { _binding = false; }
            int row = keep == null ? -1 : _rows.FindIndex(e => e.Name == keep);
            if (row >= 0)
            {
                _list.SelectedIndices.Add(row);
                _list.EnsureVisible(row);
            }
            _list.Invalidate();
            ShowSelected();
            int unused = all.Count(e => !_usedBy.ContainsKey(Stem(e)));
            _status.Text = _archive == null ? "decks.zib isn't in the open data." :
                $"decks.zib: {all.Count} deck files, {unused} not used by any deck" + (_changed.Count > 0 ? $", {_changed.Count} changed (not saved)" : "") +
                (_files != null ? $" - {_files.Describe(Zib)}" : "");
        }

        private void ShowSelected()
        {
            if (_binding)
                return;
            _binding = true;
            try
            {
                _shown = _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;
                var deck = _shown == null ? null : Parse(_shown);
                _main.Ids = deck?.Main ?? [];
                _extra.Ids = deck?.Extra ?? [];
                _side.Ids = deck?.Side ?? [];
                foreach (var cards in new[] { _main, _extra, _side })
                    cards.Enabled = deck != null;
                _heading.Text = _shown == null ? "Pick a deck file." : deck == null ? $"{_shown.Name}: not a readable deck file" : $"{_shown.Name}  -  {UsedBy(_shown)}";
            }
            finally { _binding = false; }
        }

        private void Edited()
        {
            if (_binding || _shown == null || Parse(_shown) is not { } deck)
                return;
            var updated = new YdcDeck { Header = deck.Header };
            updated.Main.AddRange(_main.Ids);
            updated.Extra.AddRange(_extra.Ids);
            updated.Side.AddRange(_side.Ids);
            _shown.Data = updated.ToBytes();
            _changed.Add(_shown.Name);
            _list.Invalidate();
        }

        private void Export()
        {
            if (_shown == null)
                return;
            using var dialog = new SaveFileDialog { FileName = _shown.Name, Filter = "Deck files (*.ydc)|*.ydc" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                File.WriteAllBytes(dialog.FileName, _shown.Data);
                _status.Text = $"Exported {_shown.Name} to {dialog.FileName}.";
            }
        }

        private void ReplaceFrom()
        {
            if (_shown == null)
                return;
            using var dialog = new OpenFileDialog { Title = $"Replace the cards of {_shown.Name} with", Filter = "Deck files (*.ydc)|*.ydc" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            byte[] data = File.ReadAllBytes(dialog.FileName);
            try { YdcDeck.Parse(data); }
            catch (InvalidDataException ex)
            {
                MessageBox.Show(this, $"{dialog.FileName} isn't a deck file: {ex.Message}", "Deck files", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _shown.Data = data;
            _changed.Add(_shown.Name);
            Refill();
        }

        private void AddFiles()
        {
            if (_archive == null)
                return;
            using var dialog = new OpenFileDialog { Title = "Add deck files to decks.zib", Filter = "Deck files (*.ydc)|*.ydc", Multiselect = true };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            foreach (string path in dialog.FileNames)
            {
                string name = Path.GetFileName(path);
                try
                {
                    byte[] data = File.ReadAllBytes(path);
                    YdcDeck.Parse(data);
                    _archive.Set(name, data);
                    _changed.Add(name);
                }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException)
                {
                    MessageBox.Show(this, $"{name}: {ex.Message}", "Deck files", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            Refill();
        }

        public bool Save()
        {
            if (_files == null || _archive == null || _changed.Count == 0)
                return true;
            try
            {
                _files.Write(new Dictionary<string, byte[]> { [Zib] = _archive.ToBytes() });
                int count = _changed.Count;
                _changed.Clear();
                Refill();
                _status.Text = $"Saved {count} deck file(s) into decks.zib ({_files.Describe(Zib)}).";
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
