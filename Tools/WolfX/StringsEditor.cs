using Wolf.Editors;

namespace WolfX
{
    /// <summary>
    /// The game's UI strings (strings\Strings_STEAM_&lt;L&gt;.BND, 1213 per language, found by their number, so the count never changes) and
    /// the credits (main\ui\credits\credits.dat, one line per entry; lines can be added and removed). Pick the file and language at the top,
    /// find a string by text or number, edit it underneath; changed strings are marked and only the changed files are saved.
    /// </summary>
    internal sealed class StringsEditor : UserControl, IGameEditor
    {
        private static readonly char[] Languages = ['E', 'F', 'G', 'I', 'J', 'S'];
        private const string CreditsChoice = "Credits (credits.dat)";

        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripComboBox _file = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        private readonly ToolStripTextBox _find = new() { Width = 200, ToolTipText = "Part of a string, or its number" };
        private readonly ToolStripComboBox _show = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly ToolStripButton _addLine, _removeLine;
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false, MultiSelect = false,
        };
        private readonly TextBox _edit = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10.5f) };
        private readonly Label _editLabel = new() { Dock = DockStyle.Top, Height = 22, Padding = new Padding(2, 4, 0, 0) };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private GameFolderFiles? _files;
        // every file read so far: the BND strings per language, and the credits; what was read, to mark and save only the changes
        private readonly Dictionary<string, List<string>> _texts = [];
        private readonly Dictionary<string, List<string>> _original = [];
        private readonly HashSet<string> _changedFiles = [];
        private List<int> _rows = [];
        private bool _binding;

        public StringsEditor()
        {
            foreach (char language in Languages)
                _file.Items.Add($"UI strings - {(global::Types.HowToPlayFile.LanguageName(language))} ({language})");
            _file.Items.Add(CreditsChoice);
            _file.SelectedIndex = 0;
            _show.Items.AddRange(["All", "Changed"]);
            _show.SelectedIndex = 0;
            _tools.Items.Add(Button("Save", "Save the changed files into the game data (Ctrl+S)", () => Save()));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("File:"));
            _tools.Items.Add(_file);
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripLabel("Show:"));
            _tools.Items.Add(_show);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(_addLine = Button("Add line", "A new credits line after the selected one", AddLine));
            _tools.Items.Add(_removeLine = Button("Remove line", "Remove the selected credits line", RemoveLine));

            _list.Columns.Add("#", 60);
            _list.Columns.Add("Text", 900);
            _list.RetrieveVirtualItem += (_, e) =>
            {
                if (e.ItemIndex >= _rows.Count) { e.Item = new ListViewItem(new string[_list.Columns.Count]); return; }   // stale index while the list shrinks
                int index = _rows[e.ItemIndex];
                var texts = Current;
                string text = texts != null && index < texts.Count ? texts[index] : "";
                e.Item = new ListViewItem([index.ToString(), text.Replace("\r\n", " / ").Replace("\n", " / ")])
                {
                    ForeColor = IsChanged(index) ? Color.FromArgb(170, 90, 0) : SystemColors.WindowText,
                };
            };
            _list.SelectedIndexChanged += (_, _) => ShowSelected();
            _file.SelectedIndexChanged += (_, _) => { LoadCurrent(); Refill(); };
            _find.TextChanged += (_, _) => Refill();
            _show.SelectedIndexChanged += (_, _) => Refill();
            _edit.TextChanged += (_, _) => Edited();

            var editPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            editPanel.Controls.Add(_edit);
            editPanel.Controls.Add(_editLabel);
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(editPanel);
            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) => split.SplitterDistance = Math.Max(150, split.Height * 62 / 100);
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

        // ---- which file ----

        private bool CreditsShown => _file.SelectedItem as string == CreditsChoice;

        private string CurrentPath => CreditsShown ? global::Types.CRED.GamePath : global::Types.BND.GamePath(Languages[Math.Clamp(_file.SelectedIndex, 0, Languages.Length - 1)]);

        private List<string>? Current => _texts.GetValueOrDefault(CurrentPath);

        private bool IsChanged(int index) =>
            _original.TryGetValue(CurrentPath, out var original) && Current is { } now && (index >= original.Count || index >= now.Count || original[index] != now[index]);

        private void LoadCurrent()
        {
            string path = CurrentPath;
            _addLine.Enabled = _removeLine.Enabled = CreditsShown;
            if (_files == null || _texts.ContainsKey(path))
                return;
            try
            {
                if (_files.Read(path) is not { } data)
                {
                    _status.Text = $"{path} isn't in the open data.";
                    return;
                }
                var texts = CreditsShown ? global::Types.CRED.Parse(data) : global::Types.BND.Parse(data).Select(s => s.String).ToList();
                _texts[path] = texts;
                _original[path] = [.. texts];
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
            {
                _status.Text = $"Couldn't read {path}: {ex.Message}";
            }
        }

        private void Refill(int? select = null)
        {
            var texts = Current ?? [];
            select ??= _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;
            string find = _find.Text.Trim();
            bool changedOnly = _show.SelectedIndex == 1;
            _rows = Enumerable.Range(0, texts.Count)
                .Where(i => !changedOnly || IsChanged(i))
                .Where(i => find.Length == 0 || i.ToString() == find || texts[i].Contains(find, StringComparison.OrdinalIgnoreCase))
                .ToList();
            _binding = true;
            try
            {
                _list.SelectedIndices.Clear();
                _list.VirtualListSize = _rows.Count;
            }
            finally { _binding = false; }
            int row = select is int wanted ? _rows.IndexOf(wanted) : -1;
            if (row < 0 && _rows.Count > 0 && _list.IsHandleCreated)
                row = 0;
            if (row >= 0)
            {
                _list.SelectedIndices.Add(row);
                _list.EnsureVisible(row);
            }
            _list.Invalidate();
            ShowSelected();
            int changed = Enumerable.Range(0, texts.Count).Count(IsChanged);
            _status.Text = $"{CurrentPath}: {texts.Count} {(CreditsShown ? "lines" : "strings")}" + (find.Length > 0 ? $", {_rows.Count} found" : "") +
                           (changed > 0 ? $", {changed} changed (not saved)" : "") + (_files != null ? $" - {_files.Describe(CurrentPath)}" : "");
        }

        private int? SelectedIndex => _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;

        private void ShowSelected()
        {
            if (_binding)
                return;
            _binding = true;
            try
            {
                var texts = Current;
                if (SelectedIndex is not int index || texts == null || index >= texts.Count)
                {
                    _editLabel.Text = "Pick a string.";
                    _edit.Text = "";
                    _edit.Enabled = false;
                    return;
                }
                _edit.Enabled = true;
                _edit.Text = texts[index].Replace("\r\n", "\n").Replace("\n", "\r\n");
                bool changed = IsChanged(index);
                _editLabel.Text = $"{(CreditsShown ? "Line" : "String")} {index}" + (changed ? "  (changed)" : "") +
                                  (changed && _original[CurrentPath].Count > index ? "   was: " + _original[CurrentPath][index].Replace("\n", " / ") : "");
            }
            finally { _binding = false; }
        }

        private void Edited()
        {
            if (_binding || SelectedIndex is not int index || Current is not { } texts || index >= texts.Count)
                return;
            // the game's line breaks are \n
            texts[index] = _edit.Text.Replace("\r\n", "\n");
            _changedFiles.Add(CurrentPath);
            _list.Invalidate();
            bool was = _binding;
            _binding = true;
            try { _editLabel.Text = $"{(CreditsShown ? "Line" : "String")} {index}  (changed)"; }
            finally { _binding = was; }
        }

        private void AddLine()
        {
            if (!CreditsShown || Current is not { } lines)
                return;
            int at = (SelectedIndex ?? lines.Count - 1) + 1;
            lines.Insert(at, "");
            _changedFiles.Add(CurrentPath);
            Refill(at);
            _edit.Focus();
        }

        private void RemoveLine()
        {
            if (!CreditsShown || Current is not { } lines || SelectedIndex is not int index)
                return;
            lines.RemoveAt(index);
            _changedFiles.Add(CurrentPath);
            Refill(Math.Min(index, lines.Count - 1));
        }

        // ---- IGameEditor ----

        public bool Dirty => _changedFiles.Any(path => _texts.TryGetValue(path, out var now) && _original.TryGetValue(path, out var was) && !now.SequenceEqual(was));

        public IReadOnlyCollection<string> Files => [.. Languages.Select(l => global::Types.BND.GamePath(l)), global::Types.CRED.GamePath];

        public string SavesTo => "Standard: strings\\Strings_STEAM_<lang>.BND (the game's UI strings) and main\\ui\\credits\\credits.dat (the credits).";

        public void Open(GameFolderFiles files)
        {
            _files = files;
            _texts.Clear();
            _original.Clear();
            _changedFiles.Clear();
            LoadCurrent();
            Refill();
        }

        public bool Save()
        {
            if (_files == null)
                return false;
            var write = new Dictionary<string, byte[]>();
            foreach (string path in _changedFiles)
            {
                if (!_texts.TryGetValue(path, out var texts) || texts.SequenceEqual(_original[path]))
                    continue;
                write[path] = path == global::Types.CRED.GamePath
                    ? global::Types.CRED.ToBytes(texts)
                    : global::Types.BND.ToBytes(texts.Select(text => new global::Types.BNDString(0, 0, text)).ToList());
            }
            try
            {
                if (write.Count > 0)
                    _files.Write(write);
                foreach (string path in write.Keys)
                    _original[path] = [.. _texts[path]];
                _changedFiles.Clear();
                Refill();
                _status.Text = write.Count == 0 ? "Nothing changed." : $"Saved {string.Join(", ", write.Keys)} into {_files.Describe(write.Keys.First())}.";
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
