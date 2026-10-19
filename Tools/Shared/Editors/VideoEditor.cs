using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Types;
using WolfX.Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The Videos page: the mp4 files in Yu-Gi-Oh-Ex\movies and Yu-Gi-Oh-Ex\videos.json, for Yu-Gi-Oh-Media (docs/MediaPlugin.md "Videos").
    /// A video named after a card id is that card's summon cut-in (replacing the game's for the 23 cards that have one), exodia.mp4 replaces
    /// the Exodia finish; any video can play at startup, before / after a story duel, or from a menu button ({"video": "name"}).
    /// </summary>
    public sealed class VideoEditor : UserControl, IGameEditor
    {
        public const string FileName = "videos.json";

        // The game's own movies (movies\<name>.bik): summon cut-ins by card id, and the Exodia finish.
        private static readonly int[] GameCutIns = [4007, 4021, 4041, 4088, 4737, 4766, 6345, 6653, 7344, 7734, 7735, 9575, 9656, 9708, 10569,
                                                    10658, 11213, 11385, 11721, 12628, 13036, 13258, 13927];

        private static readonly JsonSerializerOptions WriteOptions = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true };
        private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        private sealed class DuelRow
        {
            public int Duel { get; set; }
            public string Before { get; set; } = "";
            public string After { get; set; } = "";
        }

        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ListView _videos = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, MultiSelect = false };
        private readonly CheckedListBox _startup = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        private readonly DataGridView _duels = new()
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.None,
        };
        private readonly System.ComponentModel.BindingList<DuelRow> _duelRows = [];
        private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly Dictionary<int, string> _duelNames = [];
        private GameFolderFiles? _files;
        private string _savedJson = "";

        public IReadOnlyCollection<string> Files => [StoryDuelTable.GamePath('E')];

        public string SavesTo => $"Additional: Yu-Gi-Oh-Ex\\{FileName} and the mp4 files in Yu-Gi-Oh-Ex\\movies (needs Yu-Gi-Oh-Media).";

        public bool Dirty => _files != null && Build().ToJsonString() != _savedJson;

        private string MoviesFolder => _files == null ? "" : Path.Combine(Path.GetDirectoryName(_files.ExPath(FileName))!, "movies");

        public VideoEditor()
        {
            _tools.Items.Add(new ToolStripButton("Save", null, (_, _) => Save()) { ToolTipText = "Save videos.json (Ctrl+S)" });
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripButton("Add video...", null, (_, _) => AddVideo(byCard: false)) { ToolTipText = "Copy an mp4 into the movies folder under a name you choose" });
            _tools.Items.Add(new ToolStripButton("Add card cut-in...", null, (_, _) => AddVideo(byCard: true)) { ToolTipText = "Copy an mp4 in as a card's summon cut-in (named by the card's id)" });
            _tools.Items.Add(new ToolStripButton("Rename...", null, (_, _) => RenameVideo()));
            _tools.Items.Add(new ToolStripButton("Delete", null, (_, _) => DeleteVideo()) { ToolTipText = "Delete the selected mp4 from the movies folder" });
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripButton("Play", null, (_, _) => PlayVideo()) { ToolTipText = "Open the selected video in Windows' player" });
            _tools.Items.Add(new ToolStripButton("Open movies folder", null, (_, _) => { if (_files != null) { Directory.CreateDirectory(MoviesFolder); Process.Start(new ProcessStartInfo(MoviesFolder) { UseShellExecute = true }); } }));

            _videos.Columns.Add("Video", 180);
            _videos.Columns.Add("Plays", 420);
            _videos.Columns.Add("Size", 80, HorizontalAlignment.Right);
            _videos.DoubleClick += (_, _) => PlayVideo();

            var addDuel = new Button { Text = "Add story duel", AutoSize = true, Dock = DockStyle.Top };
            addDuel.Click += (_, _) => { _duelRows.Add(new DuelRow { Duel = _duelNames.Keys.FirstOrDefault() }); };
            var removeDuel = new Button { Text = "Remove story duel", AutoSize = true, Dock = DockStyle.Top };
            removeDuel.Click += (_, _) => { if (_duels.CurrentRow?.DataBoundItem is DuelRow row) _duelRows.Remove(row); };
            _startup.ItemCheck += (_, _) => BeginInvoke(RefreshPlays);
            _duels.CellValueChanged += (_, _) => RefreshPlays();
            _duels.DataError += (_, e) => e.ThrowException = false;

            var startupBox = new GroupBox { Text = "Play when the game starts (in this order)", Dock = DockStyle.Fill };
            startupBox.Controls.Add(_startup);
            var duelBox = new GroupBox { Text = "Story duels: before the duel starts / after it ends", Dock = DockStyle.Fill };
            duelBox.Controls.Add(_duels);
            duelBox.Controls.Add(removeDuel);
            duelBox.Controls.Add(addDuel);
            var bottom = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 260 };
            bottom.Panel1.Controls.Add(startupBox);
            bottom.Panel2.Controls.Add(duelBox);
            var help = new Label
            {
                Dock = DockStyle.Top, Height = 48, Padding = new Padding(4),
                Text = "A video named after a card id (e.g. 4007) is that card's summon cut-in; exodia replaces the Exodia finish. Any video can play at " +
                       "startup, around a story duel, or from a menu button (Menus page: \"play a video\"). H.264 mp4 always works. Esc or a click skips.",
            };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 260 };
            split.Panel1.Controls.Add(_videos);
            split.Panel2.Controls.Add(bottom);
            Controls.Add(split);
            Controls.Add(help);
            Controls.Add(_tools);
            Controls.Add(_status);
            SetEditable(false);
        }

        private void SetEditable(bool editable)
        {
            foreach (ToolStripItem item in _tools.Items)
                item.Enabled = editable;
            _videos.Enabled = _startup.Enabled = _duels.Enabled = editable;
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

        // ---- the videos ----

        private string[] VideoNames() =>
            Directory.Exists(MoviesFolder) ? Directory.GetFiles(MoviesFolder, "*.mp4").Select(f => Path.GetFileNameWithoutExtension(f)!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray() : [];

        private string? SelectedName => _videos.SelectedItems.Count > 0 ? _videos.SelectedItems[0].Text : null;

        /// <summary>What a video does, from its name and videos.json.</summary>
        private string Plays(string name)
        {
            var uses = new List<string>();
            if (string.Equals(name, "exodia", StringComparison.OrdinalIgnoreCase))
                uses.Add("replaces the Exodia finish");
            else if (int.TryParse(name, out int card) && card > 0)
                uses.Add(GameCutIns.Contains(card) ? $"replaces {CardCatalog.NameOf(card)}'s summon cut-in" : $"{CardCatalog.NameOf(card)}'s summon cut-in");
            if (_startup.CheckedItems.Cast<string>().Contains(name))
                uses.Add("at startup");
            foreach (var row in _duelRows)
            {
                if (row.Before == name)
                    uses.Add($"before story duel {row.Duel}");
                if (row.After == name)
                    uses.Add($"after story duel {row.Duel}");
            }
            uses.Add($"menu action {{\"video\": \"{name}\"}}");
            return string.Join("; ", uses);
        }

        private void FillVideos(string? select = null)
        {
            var names = VideoNames();
            _videos.BeginUpdate();
            _videos.Items.Clear();
            foreach (string name in names)
            {
                long size = new FileInfo(Path.Combine(MoviesFolder, name + ".mp4")).Length;
                var item = new ListViewItem([name, Plays(name), $"{size / 1024.0 / 1024.0:0.0} MB"]);
                _videos.Items.Add(item);
                if (name == select)
                    item.Selected = true;
            }
            _videos.EndUpdate();

            var startup = _startup.CheckedItems.Cast<string>().ToList();
            _startup.Items.Clear();
            foreach (string name in startup.Where(names.Contains).Concat(names.Except(startup)))
                _startup.Items.Add(name, startup.Contains(name));

            var choices = new List<string> { "" };
            choices.AddRange(names);
            foreach (var column in _duels.Columns.OfType<DataGridViewComboBoxColumn>().Where(c => c.DataPropertyName != nameof(DuelRow.Duel)))
                column.DataSource = choices.ToList();
        }

        private void RefreshPlays()
        {
            foreach (ListViewItem item in _videos.Items)
                item.SubItems[1].Text = Plays(item.Text);
        }

        private void AddVideo(bool byCard)
        {
            if (_files == null)
                return;
            string name;
            if (byCard)
            {
                using var picker = new CardPickerDialog(CardCatalog.Get(this), "Whose summon cut-in?", askCopies: false, maxCopies: 1);
                if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                    return;
                name = picker.Result[0].Card.Id.ToString();
            }
            else
                name = "";
            using var open = new OpenFileDialog { Filter = "MP4 video (*.mp4)|*.mp4", Title = "Pick the video" };
            if (open.ShowDialog(this) != DialogResult.OK)
                return;
            if (name.Length == 0)
            {
                name = Ask("Video name (what menus and videos.json call it):", Path.GetFileNameWithoutExtension(open.FileName)) ?? "";
                if (name.Length == 0)
                    return;
            }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "That name can't be a file name.", "Add video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Directory.CreateDirectory(MoviesFolder);
            string target = Path.Combine(MoviesFolder, name + ".mp4");
            if (File.Exists(target) && MessageBox.Show(this, $"{name}.mp4 is already there. Replace it?", "Add video", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            File.Copy(open.FileName, target, overwrite: true);
            FillVideos(name);
            _status.Text = $"Copied to {target}.";
        }

        private void RenameVideo()
        {
            if (SelectedName is not { } old || Ask("New name:", old) is not { Length: > 0 } name || name == old)
                return;
            string target = Path.Combine(MoviesFolder, name + ".mp4");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || File.Exists(target))
            {
                MessageBox.Show(this, "That name can't be used (taken, or not a file name).", "Rename video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            File.Move(Path.Combine(MoviesFolder, old + ".mp4"), target);
            foreach (var row in _duelRows)
            {
                if (row.Before == old) row.Before = name;
                if (row.After == old) row.After = name;
            }
            int index = _startup.Items.IndexOf(old);
            bool startup = index >= 0 && _startup.GetItemChecked(index);
            if (index >= 0)
            {
                _startup.Items[index] = name;
                _startup.SetItemChecked(index, startup);
            }
            _duels.Refresh();
            FillVideos(name);
        }

        private void DeleteVideo()
        {
            if (SelectedName is not { } name)
                return;
            if (MessageBox.Show(this, $"Delete {name}.mp4 from the movies folder?", "Delete video", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            File.Delete(Path.Combine(MoviesFolder, name + ".mp4"));
            foreach (var row in _duelRows)
            {
                if (row.Before == name) row.Before = "";
                if (row.After == name) row.After = "";
            }
            _duels.Refresh();
            FillVideos();
            _status.Text = $"Deleted {name}.mp4.";
        }

        private void PlayVideo()
        {
            if (SelectedName is { } name)
                Process.Start(new ProcessStartInfo(Path.Combine(MoviesFolder, name + ".mp4")) { UseShellExecute = true });
        }

        private string? Ask(string question, string value)
        {
            using var form = new Form { Text = "Video", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 96), MaximizeBox = false, MinimizeBox = false };
            var label = new Label { Text = question, Left = 10, Top = 10, Width = 360 };
            var box = new TextBox { Text = value, Left = 10, Top = 32, Width = 360 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 214, Top = 62, Width = 75 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 295, Top = 62, Width = 75 };
            form.Controls.AddRange([label, box, ok, cancel]);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            return form.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
        }

        // ---- videos.json ----

        public void Open(GameFolderFiles files)
        {
            _files = files;
            _duelNames.Clear();
            try
            {
                if (files.Read(StoryDuelTable.GamePath('E')) is byte[] dueldata)
                {
                    var table = StoryDuelTable.Parse(new Dictionary<char, byte[]> { ['E'] = dueldata });
                    StoryDuelJson.Apply(StoryDuelJson.Load(files.ExPath(StoryDuelJson.FileName)), table);
                    foreach (var duel in table.Duels.OrderBy(d => d.Id))
                        _duelNames[duel.Id] = $"{duel.Id}: {duel.Title('E')}";
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException) { }

            if (_duels.Columns.Count == 0)
            {
                _duels.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Story duel", DataPropertyName = nameof(DuelRow.Duel), Width = 260, FlatStyle = FlatStyle.Flat, ValueMember = "Key", DisplayMember = "Value" });
                _duels.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Before", DataPropertyName = nameof(DuelRow.Before), Width = 150, FlatStyle = FlatStyle.Flat });
                _duels.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "After", DataPropertyName = nameof(DuelRow.After), Width = 150, FlatStyle = FlatStyle.Flat });
                _duels.DataSource = _duelRows;
            }
            ((DataGridViewComboBoxColumn)_duels.Columns[0]).DataSource = _duelNames.ToList();

            _duelRows.Clear();
            _startup.Items.Clear();
            string path = files.ExPath(FileName);
            var startup = new List<string>();
            if (File.Exists(path))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject;
                    if (root?["startup"] is JsonValue one && one.TryGetValue<string>(out var single))
                        startup.Add(single);
                    else if (root?["startup"] is JsonArray list)
                        startup.AddRange(list.OfType<JsonValue>().Select(v => v.ToString()));
                    if (root?["storyDuels"] is JsonObject duels)
                        foreach (var (id, node) in duels)
                            if (int.TryParse(id, out int duel) && node is JsonObject entry)
                            {
                                if (!_duelNames.ContainsKey(duel))
                                    _duelNames[duel] = $"{duel}";
                                _duelRows.Add(new DuelRow { Duel = duel, Before = entry["before"]?.ToString() ?? "", After = entry["after"]?.ToString() ?? "" });
                            }
                    ((DataGridViewComboBoxColumn)_duels.Columns[0]).DataSource = _duelNames.ToList();
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    _status.Text = $"{FileName} couldn't be read: {ex.Message}";
                }
            }
            foreach (string name in startup)
                _startup.Items.Add(name, true);
            FillVideos();
            _savedJson = Build().ToJsonString();
            SetEditable(true);
            _status.Text = $"{VideoNames().Length} video(s) in {MoviesFolder}.";
        }

        private JsonObject Build()
        {
            var root = new JsonObject();
            var startup = _startup.CheckedItems.Cast<string>().ToList();
            if (startup.Count > 0)
                root["startup"] = new JsonArray([.. startup.Select(s => JsonValue.Create(s))]);
            var duels = new JsonObject();
            foreach (var row in _duelRows.Where(r => r.Before.Length > 0 || r.After.Length > 0).GroupBy(r => r.Duel).Select(g => g.Last()))
            {
                var entry = new JsonObject();
                if (row.Before.Length > 0) entry["before"] = row.Before;
                if (row.After.Length > 0) entry["after"] = row.After;
                duels[row.Duel.ToString()] = entry;
            }
            if (duels.Count > 0)
                root["storyDuels"] = duels;
            return root;
        }

        public bool Save()
        {
            if (_files == null)
                return false;
            _duels.EndEdit();
            var root = Build();
            string path = _files.ExPath(FileName);
            if (root.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);   // nothing set: no file (the videos still play as cut-ins and menu actions)
            }
            else
                File.WriteAllText(path, root.ToJsonString(WriteOptions) + "\n", new UTF8Encoding(false));
            _savedJson = root.ToJsonString();
            _status.Text = $"Saved {path}. Yu-Gi-Oh-Media reads it when the game starts.";
            return true;
        }
    }
}
