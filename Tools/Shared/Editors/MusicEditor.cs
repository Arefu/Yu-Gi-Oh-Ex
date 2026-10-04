using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The Music page: Yu-Gi-Oh-Ex\music.json for Yu-Gi-Oh-Music (docs/MusicPlugin.md). Which file (or which of the game's own tracks) plays
    /// for each of the game's music slots: everywhere, or only in an arena, against an opponent, or in one story duel (the most specific wins).
    /// Several rows for the same slot = one of them is picked at random each time the slot starts. Added files are copied into the music folder.
    /// </summary>
    public sealed class MusicEditor : UserControl, IGameEditor
    {
        public const string FileName = "music.json";

        private enum ScopeKind { Everywhere, Arena, Opponent, StoryDuel }

        private sealed record ScopeKey(ScopeKind Kind, int Id);

        public sealed class SlotItem(string key, string text)
        {
            public string Key { get; } = key;
            public string Text { get; } = text;
        }

        private sealed class TrackRow
        {
            public string Slot { get; set; } = "duel";
            public string File { get; set; } = "";
            public string GameTrack { get; set; } = "";
            public double? Volume { get; set; }
            public bool Loop { get; set; } = true;
            public double LoopStart { get; set; }
            public double LoopEnd { get; set; }
            public int? FadeIn { get; set; }
            public int? FadeOut { get; set; }

            public bool IsPlainFile => GameTrack.Length == 0 && Volume == null && Loop && LoopStart == 0 && LoopEnd == 0 && FadeIn == null && FadeOut == null;
        }

        // The game's music slots (docs/AudioSystem.md); keys are what music.json uses.
        private static readonly (string Key, string What)[] Slots =
        [
            ("duel_1_r", "LP pinch pair A (r)"), ("duel_1_y", "LP pinch pair A (y)"), ("duel_2_r", "LP pinch pair B (r)"), ("duel_2_y", "LP pinch pair B (y)"),
            ("duel_normal_2_t", "duel music"), ("mus_arc_v", "duel music in the ARC-V arena"), ("mus_duel_01", "duel music"), ("mus_duel_03", "duel music"),
            ("mus_title", "title and menus (Everywhere only)"), ("mus_tutorial", "tutorial duels"), ("mus_vrains", "duel music in the VRAINS arena"),
            ("mus_vs01", "VS intro before a duel"), ("system_deck", "a sound effect slot"), ("system_result", "duel end and results"),
        ];

        private static readonly JsonSerializerOptions WriteOptions = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true };
        private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _save, _addFiles, _addGame, _remove, _play, _stop, _openFolder;
        private readonly TextBox _folder = new() { Width = 160, Text = "music" };
        private readonly NumericUpDown _volume = new() { Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.05m, Value = 1, Width = 60 };
        private readonly NumericUpDown _fadeIn = new() { Minimum = 0, Maximum = 60000, Increment = 100, Width = 70 };
        private readonly NumericUpDown _fadeOut = new() { Minimum = 0, Maximum = 60000, Increment = 100, Width = 70 };
        private readonly TreeView _scopes = new() { Dock = DockStyle.Fill, HideSelection = false };
        private readonly ComboBox _addKind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 95 };
        private readonly IdCombo _addId = new(200);
        private readonly Button _addScope = new() { Text = "Add", AutoSize = true };
        private readonly Button _removeScope = new() { Text = "Remove", AutoSize = true };
        private readonly Label _heading = new() { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        private readonly DataGridView _grid = new()
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, EditMode = DataGridViewEditMode.EditOnEnter,
        };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private readonly BindingList<SlotItem> _slotItems = [];
        private readonly Dictionary<ScopeKey, BindingList<TrackRow>> _rows = [];
        private readonly Dictionary<int, string> _arenaNames = [], _characterNames = [], _duelNames = [];
        private GameFolderFiles? _files;
        private ScopeKey _shown = new(ScopeKind.Everywhere, 0);
        private string _savedJson = "";
        private bool _previewOpen;

        public IReadOnlyCollection<string> Files => [@"main\arenadata_E.bin", CharacterTable.GamePath('E'), StoryDuelTable.GamePath('E')];

        public string SavesTo => $"Additional: Yu-Gi-Oh-Ex\\{FileName} and the files in its music folder (needs Yu-Gi-Oh-Music).";

        public bool Dirty => _files != null && Build().ToJsonString() != _savedJson;

        public MusicEditor()
        {
            _save = Button("Save", "Save music.json (Ctrl+S)", () => Save());
            _addFiles = Button("Add files...", "Add mp3 / wav / flac files to the selected slot (copied into the music folder)", AddFiles);
            _addGame = Button("Add game track", "Play one of the game's own tracks for this slot instead", AddGameTrack);
            _remove = Button("Remove", "Remove the selected rows", RemoveRows);
            _play = Button("Play", "Listen to the selected row's file", Preview);
            _stop = Button("Stop", "Stop listening", StopPreview);
            _openFolder = Button("Open music folder", "Open the folder music.json's files are read from", OpenFolder);
            _tools.Items.AddRange([_save, new ToolStripSeparator(), _addFiles, _addGame, _remove, new ToolStripSeparator(), _play, _stop, new ToolStripSeparator(), _openFolder]);

            var defaults = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4, 2, 4, 2) };
            defaults.Controls.AddRange([Caption("Music folder (in Yu-Gi-Oh-Ex):"), _folder, Caption("Volume:"), _volume, Caption("Fade in (ms):"), _fadeIn,
                Caption("Fade out (ms):"), _fadeOut, Caption("(defaults for rows that leave them blank)")]);

            _addKind.Items.AddRange(["Arena", "Opponent", "Story duel"]);
            _addKind.SelectedIndex = 0;
            _addKind.SelectedIndexChanged += (_, _) => FillAddIds();
            _addScope.Click += (_, _) => AddScope();
            _removeScope.Click += (_, _) => RemoveScope();
            var scopeTools = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, WrapContents = true };
            scopeTools.Controls.AddRange([_addKind, _addId, _addScope, _removeScope]);
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_scopes);
            left.Controls.Add(scopeTools);
            left.Controls.Add(new Label { Dock = DockStyle.Top, Height = 34, Text = "Where it plays (the most specific wins: story duel, opponent, arena, everywhere)." });
            _scopes.AfterSelect += (_, e) =>
            {
                if (e.Node?.Tag is ScopeKey key)
                    ShowScope(key);
            };

            BuildColumns();
            _grid.DataError += (_, e) =>
            {
                e.ThrowException = false;
                _status.Text = $"Row {e.RowIndex + 1}: {e.Exception?.Message}";
            };
            _grid.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].DataPropertyName == nameof(TrackRow.File))
                    BrowseFile(e.RowIndex);
            };
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_grid);
            right.Controls.Add(new Label
            {
                Dock = DockStyle.Bottom, Height = 36,
                Text = "Several rows for one slot: one is picked at random each time it starts. Blank volume / fades use the defaults above. " +
                       "Loop start / end are seconds (0 = the whole file). Double-click a file to pick another one.",
            });
            right.Controls.Add(_heading);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);
            Load += (_, _) => split.SplitterDistance = Math.Clamp(split.Width * 28 / 100, 240, 380);

            var status = new StatusStrip();
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(defaults);
            Controls.Add(_tools);
            Controls.Add(status);

            _status.Text = "Open the game data (File > Open).";
            SetEditable(false);
        }

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 7, 2, 3) };

        private void SetEditable(bool editable)
        {
            foreach (ToolStripItem item in _tools.Items)
                item.Enabled = editable;
            _grid.Enabled = _scopes.Enabled = _addScope.Enabled = _removeScope.Enabled = editable;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                StopPreview();
            base.Dispose(disposing);
        }

        // ---- grid ----

        private void BuildColumns()
        {
            ResetSlotItems();
            var slot = new DataGridViewComboBoxColumn
            {
                HeaderText = "Slot", DataPropertyName = nameof(TrackRow.Slot), DataSource = _slotItems, ValueMember = nameof(SlotItem.Key),
                DisplayMember = nameof(SlotItem.Text), Width = 260, FlatStyle = FlatStyle.Flat,
            };
            var gameItems = new List<SlotItem> { new("", "(the file)") };
            gameItems.AddRange(Slots.Select(s => new SlotItem(s.Key, s.Key)));
            var game = new DataGridViewComboBoxColumn
            {
                HeaderText = "Or a game track", DataPropertyName = nameof(TrackRow.GameTrack), DataSource = gameItems, ValueMember = nameof(SlotItem.Key),
                DisplayMember = nameof(SlotItem.Text), Width = 130, FlatStyle = FlatStyle.Flat,
            };
            _grid.Columns.AddRange(
            [
                slot,
                new DataGridViewTextBoxColumn { HeaderText = "File", DataPropertyName = nameof(TrackRow.File), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 140 },
                game,
                Number("Volume", nameof(TrackRow.Volume), 60),
                new DataGridViewCheckBoxColumn { HeaderText = "Loop", DataPropertyName = nameof(TrackRow.Loop), Width = 45 },
                Number("Loop start", nameof(TrackRow.LoopStart), 70),
                Number("Loop end", nameof(TrackRow.LoopEnd), 70),
                Number("Fade in", nameof(TrackRow.FadeIn), 60),
                Number("Fade out", nameof(TrackRow.FadeOut), 60),
            ]);
        }

        private static DataGridViewTextBoxColumn Number(string header, string property, int width) => new()
        {
            HeaderText = header, DataPropertyName = property, Width = width,
            DefaultCellStyle = new DataGridViewCellStyle { NullValue = "", DataSourceNullValue = null },
        };

        private void ResetSlotItems()
        {
            _slotItems.Clear();
            _slotItems.Add(new SlotItem("duel", "duel: every duel track (4 5 6 7 10)"));
            _slotItems.Add(new SlotItem("pinch", "pinch: every LP pinch track (0-3)"));
            for (int i = 0; i < Slots.Length; i++)
                _slotItems.Add(new SlotItem(Slots[i].Key, $"{Slots[i].Key} ({i}): {Slots[i].What}"));
        }

        /// <summary>A key music.json may use that isn't in the list (another slot number): listed as it is.</summary>
        private string SlotKey(string key)
        {
            if (int.TryParse(key, out int number) && number >= 0 && number < Slots.Length)
                return Slots[number].Key;
            var known = _slotItems.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (known != null)
                return known.Key;
            _slotItems.Add(new SlotItem(key, $"{key}: (slot number)"));
            return key;
        }

        private BindingList<TrackRow> RowsOf(ScopeKey key)
        {
            if (!_rows.TryGetValue(key, out var rows))
                _rows[key] = rows = [];
            return rows;
        }

        private void ShowScope(ScopeKey key)
        {
            _grid.EndEdit();
            _shown = key;
            _grid.DataSource = RowsOf(key);
            _heading.Text = ScopeTitle(key);
        }

        private TrackRow? Selected => _grid.CurrentRow?.DataBoundItem as TrackRow;

        // ---- scopes ----

        private string ScopeTitle(ScopeKey key) => key.Kind switch
        {
            ScopeKind.Arena => $"Arena {key.Id}: {_arenaNames.GetValueOrDefault(key.Id, "?")}",
            ScopeKind.Opponent => $"Opponent {key.Id}: {_characterNames.GetValueOrDefault(key.Id, "?")}",
            ScopeKind.StoryDuel => $"Story duel {key.Id}: {_duelNames.GetValueOrDefault(key.Id, "?")}",
            _ => "Everywhere (menus and every duel)",
        };

        private void RebuildTree()
        {
            _scopes.BeginUpdate();
            _scopes.Nodes.Clear();
            var everywhere = _scopes.Nodes.Add("Everywhere");
            everywhere.Tag = new ScopeKey(ScopeKind.Everywhere, 0);
            foreach (var (kind, title) in new[] { (ScopeKind.Arena, "Arenas"), (ScopeKind.Opponent, "Opponents"), (ScopeKind.StoryDuel, "Story duels") })
            {
                var group = _scopes.Nodes.Add(title);
                group.ForeColor = SystemColors.GrayText;
                foreach (var key in _rows.Keys.Where(k => k.Kind == kind).OrderBy(k => k.Id))
                {
                    var node = group.Nodes.Add(ScopeTitle(key));
                    node.Tag = key;
                    if (key == _shown)
                        _scopes.SelectedNode = node;
                }
            }
            _scopes.ExpandAll();
            _scopes.EndUpdate();
            if (_scopes.SelectedNode == null)
                _scopes.SelectedNode = everywhere;
        }

        private void FillAddIds()
        {
            _addId.SetItems(_addKind.SelectedIndex switch
            {
                0 => _arenaNames.Select(p => (p.Key, p.Value)),
                1 => _characterNames.Select(p => (p.Key, p.Value)),
                _ => _duelNames.Select(p => (p.Key, p.Value)),
            });
            _addId.Value = _addKind.SelectedIndex == 0 ? _arenaNames.Keys.DefaultIfEmpty(1).First() : _addKind.SelectedIndex == 1 ? _characterNames.Keys.DefaultIfEmpty(1).First() : _duelNames.Keys.DefaultIfEmpty(1).First();
        }

        private void AddScope()
        {
            var key = new ScopeKey(_addKind.SelectedIndex switch { 0 => ScopeKind.Arena, 1 => ScopeKind.Opponent, _ => ScopeKind.StoryDuel }, _addId.Value);
            RowsOf(key);
            _shown = key;
            RebuildTree();
        }

        private void RemoveScope()
        {
            if (_scopes.SelectedNode?.Tag is not ScopeKey key || key.Kind == ScopeKind.Everywhere)
                return;
            if (RowsOf(key).Count > 0 && MessageBox.Show(this, $"Remove {ScopeTitle(key)} and its {RowsOf(key).Count} row(s)?", "Music",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            _rows.Remove(key);
            _shown = new ScopeKey(ScopeKind.Everywhere, 0);
            RebuildTree();
        }

        // ---- rows ----

        private string MusicFolder => _files == null ? "" : Path.Combine(_files.ExFolder, _folder.Text.Trim().Length > 0 ? _folder.Text.Trim() : "music");

        private string FullPath(string file) => Path.IsPathRooted(file) ? file : Path.Combine(MusicFolder, file);

        /// <summary>Copies a file into the music folder (unless it's there already) and gives the name music.json uses for it.</summary>
        private string Bring(string path)
        {
            string folder = MusicFolder;
            Directory.CreateDirectory(folder);
            string full = Path.GetFullPath(path);
            if (full.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(folder, full);
            string target = Path.Combine(folder, Path.GetFileName(full));
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(full).Length)
                File.Copy(full, target, overwrite: true);
            return Path.GetFileName(full);
        }

        private static OpenFileDialog AudioDialog(bool multiple) => new() { Filter = "Music (*.mp3;*.wav;*.flac)|*.mp3;*.wav;*.flac|All files (*.*)|*.*", Multiselect = multiple };

        private void AddFiles()
        {
            using var dialog = AudioDialog(true);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            _grid.EndEdit();
            string slot = Selected?.Slot ?? "duel";
            var rows = RowsOf(_shown);
            try
            {
                foreach (string path in dialog.FileNames)
                    rows.Add(new TrackRow { Slot = slot, File = Bring(path) });
                _status.Text = $"Added {dialog.FileNames.Length} file(s) to {slot}; they're in {MusicFolder}.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Music", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AddGameTrack()
        {
            _grid.EndEdit();
            RowsOf(_shown).Add(new TrackRow { Slot = Selected?.Slot ?? "duel", GameTrack = "mus_duel_01" });
        }

        private void BrowseFile(int row)
        {
            if (_grid.Rows[row].DataBoundItem is not TrackRow track)
                return;
            using var dialog = AudioDialog(false);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                track.File = Bring(dialog.FileName);
                track.GameTrack = "";
                RowsOf(_shown).ResetItem(row);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Music", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RemoveRows()
        {
            _grid.EndEdit();
            var rows = RowsOf(_shown);
            foreach (var track in _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<TrackRow>().ToList())
                rows.Remove(track);
        }

        private void OpenFolder()
        {
            if (_files == null)
                return;
            Directory.CreateDirectory(MusicFolder);
            System.Diagnostics.Process.Start("explorer.exe", MusicFolder);
        }

        // ---- preview (MCI: mp3 and wav; flac only with a codec installed) ----

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder? result, int length, IntPtr callback);

        private void Preview()
        {
            if (Selected is not { } track || track.File.Length == 0)
            {
                _status.Text = "Pick a row with a file to listen to it (the game's own tracks can't be played here).";
                return;
            }
            string path = FullPath(track.File);
            if (!File.Exists(path))
            {
                _status.Text = $"{path} doesn't exist.";
                return;
            }
            StopPreview();
            int error = mciSendString($"open \"{path}\" type mpegvideo alias wolfxmusic", null, 0, IntPtr.Zero);
            if (error == 0)
                error = mciSendString("play wolfxmusic", null, 0, IntPtr.Zero);
            _previewOpen = error == 0;
            _status.Text = error == 0 ? $"Playing {Path.GetFileName(path)}." : $"Windows can't play {Path.GetFileName(path)} here (MCI error {error}); the game still can.";
        }

        private void StopPreview()
        {
            if (!_previewOpen)
                return;
            mciSendString("close wolfxmusic", null, 0, IntPtr.Zero);
            _previewOpen = false;
        }

        // ---- opening ----

        public void Open(GameFolderFiles files)
        {
            _files = files;
            ReadNames(files);
            FillAddIds();
            _rows.Clear();
            ResetSlotItems();
            _folder.Text = "music";
            _volume.Value = 1;
            _fadeIn.Value = _fadeOut.Value = 0;
            string path = files.ExPath(FileName);
            string note = "no music.json yet";
            if (File.Exists(path))
            {
                try
                {
                    Read(JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject ?? throw new InvalidDataException("not a JSON object"));
                    note = $"{_rows.Values.Sum(r => r.Count)} row(s) from {FileName}";
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
                {
                    note = $"{FileName} couldn't be read: {ex.Message}";
                }
            }
            _shown = new ScopeKey(ScopeKind.Everywhere, 0);
            RebuildTree();
            ShowScope(_shown);
            _savedJson = Build().ToJsonString();
            SetEditable(true);
            _status.Text = $"{path}: {note}. The music folder is {MusicFolder}.";
        }

        private void ReadNames(GameFolderFiles files)
        {
            _arenaNames.Clear();
            _characterNames.Clear();
            _duelNames.Clear();
            foreach (var (id, name) in GameNames.Arenas(files, null))
                _arenaNames[id] = name;
            try
            {
                if (files.Read(CharacterTable.GamePath('E')) is byte[] chardata)
                {
                    var table = CharacterTable.Parse(new Dictionary<char, byte[]> { ['E'] = chardata });
                    CharacterJson.Apply(CharacterJson.Load(files.ExPath(CharacterJson.FileName)), table);
                    foreach (var (id, name) in GameNames.Characters(table, 'E'))
                        _characterNames[id] = name;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException) { }
            try
            {
                if (files.Read(StoryDuelTable.GamePath('E')) is byte[] dueldata)
                {
                    var table = StoryDuelTable.Parse(new Dictionary<char, byte[]> { ['E'] = dueldata });
                    StoryDuelJson.Apply(StoryDuelJson.Load(files.ExPath(StoryDuelJson.FileName)), table);
                    foreach (var duel in table.Duels.OrderBy(d => d.Id))
                    {
                        string opponent = _characterNames.GetValueOrDefault(duel.Characters[1], $"character {duel.Characters[1]}");
                        _duelNames[duel.Id] = duel.Title('E') is { Length: > 0 } title ? $"{title} (vs {opponent})" : $"vs {opponent}";
                    }
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException) { }
        }

        private void Read(JsonObject root)
        {
            if (root["folder"] is JsonValue folder && folder.TryGetValue<string>(out var f))
                _folder.Text = f;
            _volume.Value = (decimal)Math.Clamp(Num(root["volume"]) ?? 1, 0, 2);
            _fadeIn.Value = (decimal)Math.Clamp(Num(root["fadeIn"]) ?? 0, 0, 60000);
            _fadeOut.Value = (decimal)Math.Clamp(Num(root["fadeOut"]) ?? 0, 0, 60000);
            if (root["slots"] is JsonObject slots)
                ReadScope(slots, new ScopeKey(ScopeKind.Everywhere, 0));
            foreach (var (name, kind) in new[] { ("arenas", ScopeKind.Arena), ("opponents", ScopeKind.Opponent), ("storyDuels", ScopeKind.StoryDuel) })
                if (root[name] is JsonObject scopes)
                    foreach (var (id, scope) in scopes)
                        if (int.TryParse(id, out int number) && scope is JsonObject entries)
                            ReadScope(entries, new ScopeKey(kind, number));
        }

        private static double? Num(JsonNode? node) => node is JsonValue value && value.TryGetValue<double>(out double d) ? d : null;

        private void ReadScope(JsonObject entries, ScopeKey key)
        {
            var rows = RowsOf(key);
            foreach (var (slotKey, value) in entries)
            {
                string slot = SlotKey(slotKey);
                if (value is JsonArray list)
                {
                    foreach (var entry in list)
                        if (ReadRow(entry, slot, null) is { } row)
                            rows.Add(row);
                }
                else if (value is JsonObject obj && obj["files"] is JsonArray files)
                {
                    foreach (var entry in files)
                        if (ReadRow(entry, slot, obj) is { } row)
                            rows.Add(row);
                }
                else if (ReadRow(value, slot, null) is { } row)
                    rows.Add(row);
            }
        }

        private static TrackRow? ReadRow(JsonNode? node, string slot, JsonObject? shared)
        {
            var row = new TrackRow { Slot = slot };
            void Options(JsonObject o)
            {
                if (Num(o["volume"]) is double v) row.Volume = v;
                if (o["loop"] is JsonValue loop && loop.TryGetValue<bool>(out bool l)) row.Loop = l;
                if (Num(o["loopStart"]) is double s) row.LoopStart = s;
                if (Num(o["loopEnd"]) is double e) row.LoopEnd = e;
                if (Num(o["fadeIn"]) is double fi) row.FadeIn = (int)fi;
                if (Num(o["fadeOut"]) is double fo) row.FadeOut = (int)fo;
            }
            if (shared != null)
                Options(shared);
            switch (node)
            {
                case JsonValue value when value.TryGetValue<string>(out var file):
                    row.File = file;
                    return row;
                case JsonObject obj:
                    Options(obj);
                    if (obj["slot"] is JsonValue game)
                    {
                        string text = game.ToString();
                        row.GameTrack = int.TryParse(text, out int n) && n >= 0 && n < Slots.Length ? Slots[n].Key : text;
                        if (!Slots.Any(s => s.Key == row.GameTrack))
                            return null;
                        return row;
                    }
                    if (obj["file"] is JsonValue f && f.TryGetValue<string>(out var path))
                    {
                        row.File = path;
                        return row;
                    }
                    return null;
                default:
                    return null;
            }
        }

        // ---- saving ----

        private JsonObject Build()
        {
            var root = new JsonObject { ["folder"] = _folder.Text.Trim().Length > 0 ? _folder.Text.Trim() : "music" };
            if (_volume.Value != 1)
                root["volume"] = (double)_volume.Value;
            if (_fadeIn.Value != 0)
                root["fadeIn"] = (int)_fadeIn.Value;
            if (_fadeOut.Value != 0)
                root["fadeOut"] = (int)_fadeOut.Value;
            root["slots"] = BuildScope(RowsOf(new ScopeKey(ScopeKind.Everywhere, 0)));
            foreach (var (name, kind) in new[] { ("arenas", ScopeKind.Arena), ("opponents", ScopeKind.Opponent), ("storyDuels", ScopeKind.StoryDuel) })
            {
                var scopes = new JsonObject();
                foreach (var key in _rows.Keys.Where(k => k.Kind == kind).OrderBy(k => k.Id))
                    scopes[key.Id.ToString()] = BuildScope(_rows[key]);
                if (scopes.Count > 0)
                    root[name] = scopes;
            }
            return root;
        }

        private static JsonObject BuildScope(IEnumerable<TrackRow> rows)
        {
            var scope = new JsonObject();
            foreach (var group in rows.GroupBy(r => r.Slot))
            {
                var choices = group.Select(BuildChoice).ToList();
                scope[group.Key] = choices.Count == 1 ? choices[0] : new JsonArray([.. choices]);
            }
            return scope;
        }

        private static JsonNode BuildChoice(TrackRow row)
        {
            if (row.GameTrack.Length > 0)
                return new JsonObject { ["slot"] = row.GameTrack };
            if (row.IsPlainFile)
                return JsonValue.Create(row.File)!;
            var obj = new JsonObject { ["file"] = row.File };
            if (row.Volume is double volume) obj["volume"] = volume;
            if (!row.Loop) obj["loop"] = false;
            if (row.LoopStart != 0) obj["loopStart"] = row.LoopStart;
            if (row.LoopEnd != 0) obj["loopEnd"] = row.LoopEnd;
            if (row.FadeIn is int fadeIn) obj["fadeIn"] = fadeIn;
            if (row.FadeOut is int fadeOut) obj["fadeOut"] = fadeOut;
            return obj;
        }

        public bool Save()
        {
            if (_files == null)
                return false;
            _grid.EndEdit();
            var problems = new List<string>();
            foreach (var (key, rows) in _rows)
                foreach (var row in rows)
                {
                    if (row.GameTrack.Length == 0 && row.File.Trim().Length == 0)
                        problems.Add($"{ScopeTitle(key)}, {row.Slot}: a row has no file and no game track.");
                    else if (row.GameTrack.Length == 0 && !File.Exists(FullPath(row.File)))
                        problems.Add($"{ScopeTitle(key)}, {row.Slot}: {FullPath(row.File)} doesn't exist (the game would play its own track).");
                    if (row.LoopEnd != 0 && row.LoopEnd <= row.LoopStart)
                        problems.Add($"{ScopeTitle(key)}, {row.Slot}: loop end must be after loop start.");
                }
            if (problems.Any(p => !p.Contains("doesn't exist")))
            {
                MessageBox.Show(this, string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (problems.Count > 0 && MessageBox.Show(this, string.Join("\n", problems) + "\n\nSave anyway?", "Music", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;

            var root = Build();
            Directory.CreateDirectory(_files.ExFolder);
            File.WriteAllText(_files.ExPath(FileName), root.ToJsonString(WriteOptions) + "\n", new UTF8Encoding(false));
            _savedJson = root.ToJsonString();
            _status.Text = $"Saved {_files.ExPath(FileName)}. Yu-Gi-Oh-Music reads it when the game starts.";
            return true;
        }
    }
}
