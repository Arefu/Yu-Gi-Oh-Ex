using System.IO;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// Edits the game's two Indx + Text tables in every language side by side (File Type Libraries/TextTable):
    /// WORD - the words on card frames, by group (attribute 10+, type 100+, card kind 200+...), and DLG - the duel's prompts.
    /// The grid shows every language; the box below edits the selected cell, with a preview of its @ colours.
    /// Opens the game's tables with Yu-Gi-Oh-Ex/text.json on top. Changes to the game's entries are saved into the game's own files;
    /// entries added after them (for custom card kinds, types or prompts, in every language at once) go to text.json, which
    /// Yu-Gi-Oh-MoreCards serves to the game.
    /// </summary>
    public sealed class TextTableEditor : UserControl, IGameEditor
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _add, _removeAdded;
        private readonly ToolStripComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly ToolStripTextBox _search = new() { Width = 180, ToolTipText = "Show only rows containing this (any language)" };
        private readonly DataGridView _grid = new()
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        };
        private readonly TextBox _text = new()
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10f),
        };
        private readonly Label _editing = new() { Dock = DockStyle.Top, Height = 20 };
        private readonly MarkupPreview _preview = new() { Dock = DockStyle.Bottom, Height = 70 };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private readonly Dictionary<char, TextTable> _tables = [];
        private TextTableKind _current = TextTableKind.Word;
        private int _originalCount;       // entries the table had when opened (added ones come after)
        private bool _syncing;
        private GameFolderFiles? _gameFiles;
        private readonly Dictionary<char, List<string>> _baseline = [];   // the game's own entries, as in its files
        private string _savedJson = "";

        private string JsonPath => _gameFiles!.ExPath(TextTableJson.FileName);

        public IReadOnlyCollection<string> Files =>
            [.. TextTable.Languages.SelectMany(l => new[] { TextTable.IndexGamePath(_current, l), TextTable.TextGamePath(_current, l) })];

        public string SavesTo => $"Standard: bin\\WORD_ / DLG_ Indx + Text files (changes to the game's entries). Additional (entries added after them): Yu-Gi-Oh-Ex\\{TextTableJson.FileName} (needs Yu-Gi-Oh-MoreCards).";

        private string SectionJson() => TextTableJson.Section(_tables, _baseline).ToJsonString();

        public TextTableEditor()
        {
            PreviewPopOut.Attach(_preview, "Text preview", doubleClick: true);
            _tools.Items.Add(Button("Save", "Changes to the game's entries into its files, added entries into text.json (Ctrl+S)", () => Save()));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("Table:"));
            _kind.Items.Add("WORD (card frame words)");
            _kind.Items.Add("DLG (duel prompts)");
            _kind.SelectedIndex = 0;
            _kind.SelectedIndexChanged += (_, _) => KindChosen();
            _tools.Items.Add(_kind);
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _search.TextChanged += (_, _) => ApplyFilter();
            _tools.Items.Add(_search);
            _tools.Items.Add(new ToolStripSeparator());
            _add = Button("Add entry", "Add an entry at the end, in every language (the English text is copied to the others until translated)", AddEntry);
            _removeAdded = Button("Remove last added", "Remove the last entry you added (the game's own entries stay)", RemoveLastAdded);
            _tools.Items.Add(_add);
            _tools.Items.Add(_removeAdded);

            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
            _grid.CurrentCellChanged += (_, _) => ShowCell();
            _text.TextChanged += (_, _) => TextEdited();

            var editor = new Panel { Dock = DockStyle.Fill };
            editor.Controls.Add(_text);
            editor.Controls.Add(_editing);
            editor.Controls.Add(_preview);
            editor.Controls.Add(new Label
            {
                Dock = DockStyle.Bottom, Height = 18, ForeColor = SystemColors.GrayText,
                Text = "@0-@9 @A-@G colours (@0 normal), %s = a card name the game fills in, new lines as typed.",
            });
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(_grid);
            split.Panel2.Controls.Add(editor);

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) => split.SplitterDistance = Math.Max(200, split.Height * 65 / 100);
            SetStatus("Open the game data (File > Open).");
        }

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            Open(_current);
        }

        public bool Dirty => _tables.Count > 0 && SectionJson() != _savedJson;

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

        private void SetStatus(string text) => _status.Text = text;

        // ---- opening / saving ----

        private (byte[]? Index, byte[]? Text) Read(TextTableKind kind, char language) =>
            _gameFiles == null ? (null, null) : (_gameFiles.Read(TextTable.IndexGamePath(kind, language)), _gameFiles.Read(TextTable.TextGamePath(kind, language)));

        private void Open(TextTableKind kind)
        {
            var problems = new List<string>();
            _tables.Clear();
            foreach (char language in TextTable.Languages)
            {
                var (index, text) = Read(kind, language);
                if (index == null || text == null)
                    continue;
                try
                {
                    var table = TextTable.Parse(index, text);
                    _tables[language] = table;
                }
                catch (InvalidDataException ex)
                {
                    problems.Add($"{language}: {ex.Message}");
                }
            }
            _current = kind;
            _syncing = true;
            _kind.SelectedIndex = kind == TextTableKind.Word ? 0 : 1;
            _syncing = false;
            _originalCount = _tables.Count > 0 ? _tables.Values.Max(t => t.Entries.Count) : 0;

            // the game's text is the baseline; text.json's entries go on top
            _baseline.Clear();
            int fromJson = 0;
            foreach (var (language, table) in _tables)
                _baseline[language] = [.. table.Entries];
            if (_gameFiles != null)
            {
                try
                {
                    fromJson = TextTableJson.Apply(TextTableJson.Load(JsonPath), kind, _tables);
                }
                catch (InvalidDataException ex)
                {
                    problems.Add(ex.Message);
                }
            }
            _savedJson = SectionJson();

            FillGrid();
            string where = _gameFiles == null ? "nothing open" : $"{_gameFiles.Describe(TextTable.IndexGamePath(kind, 'E'))} + {TextTableJson.FileName} ({fromJson} entries)";
            SetStatus(_tables.Count == 0
                ? $"No {TextTable.Prefix(kind)}_Indx/Text files in {where}."
                : $"{TextTable.Prefix(kind)} from {where}: {_originalCount} entries in {_tables.Count} languages ({string.Join(" ", _tables.Keys)})" +
                  (problems.Count > 0 ? ". Couldn't read: " + string.Join("; ", problems) : ""));
        }

        private void KindChosen()
        {
            if (_syncing)
                return;
            var kind = _kind.SelectedIndex == 0 ? TextTableKind.Word : TextTableKind.Dlg;
            if (kind == _current || _gameFiles == null)
            {
                _current = kind;
                return;
            }
            if (Dirty && MessageBox.Show(this, "The text tables have changes that are not saved. Discard them?", "Text tables",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                _syncing = true;
                _kind.SelectedIndex = _current == TextTableKind.Word ? 0 : 1;
                _syncing = false;
                return;
            }
            Open(kind);
        }

        /// <summary>
        /// Standard content: the game's entries (with your changes) go back into each language's Indx + Text files.
        /// Additional content: entries added after the game's go to this table's section of text.json.
        /// </summary>
        public bool Save()
        {
            if (_gameFiles == null || _tables.Count == 0)
                return false;
            try
            {
                var write = new Dictionary<string, byte[]>();
                foreach (var (language, table) in _tables)
                {
                    if (!_baseline.TryGetValue(language, out var game))
                        continue;
                    var standard = new TextTable();
                    standard.Entries.AddRange(table.Entries.Take(game.Count));
                    if (standard.Entries.SequenceEqual(game))
                        continue;
                    var (index, text) = standard.ToBytes();
                    write[TextTable.IndexGamePath(_current, language)] = index;
                    write[TextTable.TextGamePath(_current, language)] = text;
                    _baseline[language] = [.. standard.Entries];
                }
                if (write.Count > 0)
                    _gameFiles.Write(write);

                var section = TextTableJson.Section(_tables, _baseline);
                TextTableJson.Save(JsonPath, _current, section);
                if (TextTableJson.Load(JsonPath).Count == 0)
                    File.Delete(JsonPath);
                _savedJson = section.ToJsonString();
                FillGrid();
                SetStatus((write.Count > 0 ? $"Saved {TextTable.Prefix(_current)} for {write.Count / 2} languages into {_gameFiles.Describe(TextTable.IndexGamePath(_current, 'E'))}" : $"The game's {TextTable.Prefix(_current)} entries are unchanged") +
                    (section.Count > 0 ? $"; {section.Count} added entries to {JsonPath} (Yu-Gi-Oh-MoreCards reads it)." : "."));
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                SetStatus("Not saved: " + ex.Message);
                return false;
            }
        }

        // ---- grid ----

        private int RowCount => _tables.Count > 0 ? _tables.Values.Max(t => t.Entries.Count) : 0;

        private static string OneLine(string text) => text.Replace("\r", "").Replace("\n", " ⏎ ");

        private string Meaning(int index)
        {
            string meaning = _current == TextTableKind.Word ? TextTable.WordMeaning(index) : "";
            if (index >= _originalCount)
                return meaning + (meaning.Length > 0 ? " (new)" : "new");
            bool changed = _tables.Any(t => _baseline.TryGetValue(t.Key, out var game) && index < game.Count && index < t.Value.Entries.Count && game[index] != t.Value.Entries[index]);
            return meaning + (changed ? (meaning.Length > 0 ? " (changed)" : "changed") : "");
        }

        private void FillGrid()
        {
            _syncing = true;
            _grid.Columns.Clear();
            _grid.Rows.Clear();
            _grid.Columns.Add("index", "#");
            _grid.Columns.Add("meaning", "For");
            _grid.Columns[0].Width = 50;
            _grid.Columns[1].Width = _current == TextTableKind.Word ? 140 : 50;
            foreach (char language in _tables.Keys)
            {
                int column = _grid.Columns.Add(language.ToString(), HowToPlayFile.LanguageName(language));
                _grid.Columns[column].Width = _current == TextTableKind.Word ? 170 : 320;
                _grid.Columns[column].Tag = language;
            }
            for (int i = 0; i < RowCount; i++)
                _grid.Rows.Add(RowValues(i));
            _syncing = false;
            ApplyFilter();
            ShowCell();
        }

        private object[] RowValues(int index)
        {
            var values = new List<object> { index, Meaning(index) };
            foreach (var table in _tables.Values)
                values.Add(index < table.Entries.Count ? OneLine(table.Entries[index]) : "");
            return [.. values];
        }

        private void ApplyFilter()
        {
            string find = _search.Text.Trim();
            _grid.CurrentCell = null;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                int index = row.Index;
                row.Visible = find.Length == 0 || index.ToString() == find ||
                    _tables.Values.Any(t => index < t.Entries.Count && t.Entries[index].Contains(find, StringComparison.OrdinalIgnoreCase)) ||
                    Meaning(index).Contains(find, StringComparison.OrdinalIgnoreCase);
            }
        }

        private (int Index, char Language)? CurrentCell() =>
            _grid.CurrentCell is { } cell && _grid.Columns[cell.ColumnIndex].Tag is char language ? (cell.RowIndex, language) : null;

        private void ShowCell()
        {
            if (_syncing)
                return;
            _syncing = true;
            if (CurrentCell() is var (index, language) && _tables.TryGetValue(language, out var table) && index < table.Entries.Count)
            {
                _text.Enabled = true;
                _text.Text = table.Entries[index].Replace("\n", "\r\n");
                _editing.Text = $"{TextTable.Prefix(_current)} {index} ({HowToPlayFile.LanguageName(language)})" + (Meaning(index) is { Length: > 0 } m ? " - " + m : "");
                _preview.Markup = table.Entries[index];
            }
            else
            {
                _text.Enabled = false;
                _text.Text = "";
                _editing.Text = "Pick a cell in a language column to edit it.";
                _preview.Markup = "";
            }
            _syncing = false;
        }

        private void TextEdited()
        {
            if (_syncing || CurrentCell() is not var (index, language) || !_tables.TryGetValue(language, out var table) || index >= table.Entries.Count)
                return;
            table.Entries[index] = _text.Text.Replace("\r\n", "\n");
            _grid.Rows[index].Cells[_grid.CurrentCell!.ColumnIndex].Value = OneLine(table.Entries[index]);
            _grid.Rows[index].Cells[1].Value = Meaning(index);
            _preview.Markup = table.Entries[index];
        }

        // ---- adding (WolfEx) ----

        private void AddEntry()
        {
            if (_tables.Count == 0)
                return;
            int index = RowCount;
            string english = _tables.TryGetValue('E', out var e) && e.Entries.Count > 0 ? "New entry" : "";
            foreach (var table in _tables.Values)
            {
                while (table.Entries.Count < index)
                    table.Entries.Add("");
                table.Entries.Add(english);
            }
            _grid.Rows.Add(RowValues(index));
            var column = _grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(c => c.Tag is char);
            if (column != null)
                _grid.CurrentCell = _grid.Rows[index].Cells[column.Index];
            _text.Focus();
            _text.SelectAll();
            SetStatus($"Added {TextTable.Prefix(_current)} {index}" + (_current == TextTableKind.Word && TextTable.WordMeaning(index) is { Length: > 0 } m ? $" ({m})" : "") +
                ". Type the text for each language; the others keep this text until translated.");
        }

        private void RemoveLastAdded()
        {
            int count = RowCount;
            if (count <= _originalCount)
            {
                SetStatus("Only entries you added can be removed (the game reads its own by number).");
                return;
            }
            foreach (var table in _tables.Values)
                if (table.Entries.Count == count)
                    table.Entries.RemoveAt(count - 1);
            _grid.Rows.RemoveAt(count - 1);
            SetStatus($"Removed {TextTable.Prefix(_current)} {count - 1}.");
        }

        /// <summary>Draws text with the game's @ colours (as HowToText reads them), "%s" as a card name placeholder.</summary>
        private sealed class MarkupPreview : Control
        {
            private string _markup = "";

            public MarkupPreview()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Color.FromArgb(10, 18, 36);
                Font = new Font("Segoe UI", 11f);
            }

            public string Markup
            {
                get => _markup;
                set
                {
                    _markup = value;
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
                int x = 8, y = 6, line = Font.Height + 2;
                foreach (var run in HowToText.Parse(_markup.Replace("%s", "[Card Name]")))
                {
                    if (run.Text == "\n")
                    {
                        x = 8;
                        y += line;
                        continue;
                    }
                    var colour = Color.FromArgb(255, Color.FromArgb((int)HowToText.Colours[Math.Clamp(run.Colour, 0, HowToText.Colours.Length - 1)]));
                    foreach (string word in run.Text.Split(' ').Select((w, i) => i == 0 ? w : " " + w))
                    {
                        int width = TextRenderer.MeasureText(e.Graphics, word, Font, Size.Empty, Flags).Width;
                        if (x + width > Width - 8 && x > 8)
                        {
                            x = 8;
                            y += line;
                        }
                        TextRenderer.DrawText(e.Graphics, word, Font, new Point(x, y), colour, Flags);
                        x += width;
                    }
                }
            }
        }
    }
}
