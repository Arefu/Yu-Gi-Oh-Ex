using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// Yu-Gi-Oh-Ex\arenas.json: duel arenas past the game's 14, read by Yu-Gi-Oh-Campaign (Arenas.cpp):
    /// <code>{ "arenas": [ { "id": 100, "folder": "my_arena", "name": { "E": "My Arena" } } ] }</code>
    /// The game loads arenas/&lt;folder&gt;/animlist.txt for a duel's arena id from a table built into the exe (ids 1-8, 13-18); the plugin
    /// adds these. An arena's files are game content and go into the Ex patch (New arena copies a game arena's as a start).
    /// </summary>
    public sealed class ArenaFile
    {
        public const string FileName = "arenas.json";

        public sealed class Arena
        {
            public int Id;
            public string Folder = "";
            public Dictionary<char, string> Names = [];
            public string Name(char language = 'E') =>
                Names.TryGetValue(language, out var n) && n.Length > 0 ? n : Names.TryGetValue('E', out var e) && e.Length > 0 ? e : Folder;
        }

        /// <summary>The game's arenas: id -> folder (g_ArenaFolderTable 0x140A52290).</summary>
        public static readonly (int Id, string Folder)[] GameArenas =
        [
            (1, "classic"), (2, "5ds"), (3, "zexal"), (4, "gx"), (5, "arc5"), (6, "blimp"), (7, "urban"), (8, "barian"),
            (13, "fusion_dimension"), (14, "synchro_dimension"), (15, "xyz_dimension"), (16, "you_show_duel_school"), (17, "vrains_dimension"), (18, "vrains_2019"),
        ];

        public List<Arena> Entries { get; } = [];

        /// <summary>The file of the open game folder (for the arena pickers), or an empty one.</summary>
        public static ArenaFile Current()
        {
            try
            {
                return GameFolderFiles.Current is { } files ? Load(files.ExPath(FileName)) : new ArenaFile();
            }
            catch (Exception)
            {
                return new ArenaFile();
            }
        }

        public static ArenaFile Load(string path)
        {
            var file = new ArenaFile();
            if (!File.Exists(path))
                return file;
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (var node in (root?["arenas"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (node["id"] is not JsonValue id || !id.TryGetValue(out int arenaId))
                    continue;
                var arena = new Arena { Id = arenaId, Folder = node["folder"] is JsonValue f && f.TryGetValue(out string? folder) ? folder : "" };
                if (node["name"] is JsonValue one && one.TryGetValue(out string? name))
                    arena.Names['E'] = name;
                else if (node["name"] is JsonObject names)
                    foreach (var (language, value) in names)
                        if (language.Length > 0 && value is JsonValue v && v.TryGetValue(out string? text))
                            arena.Names[char.ToUpperInvariant(language[0])] = text;
                file.Entries.Add(arena);
            }
            return file;
        }

        public List<string> Problems()
        {
            var problems = new List<string>();
            foreach (var a in Entries)
            {
                if (a.Id <= 0 || GameArenas.Any(g => g.Id == a.Id))
                    problems.Add($"Arena {a.Id}: pick an id above 0 that isn't a game arena's (1-8, 13-18).");
                if (a.Folder.Length == 0 || a.Folder.IndexOfAny(['/', '\\', ':', '.', ' ']) >= 0 || a.Folder.Any(c => c > 127))
                    problems.Add($"Arena {a.Id}: the folder must be a plain ASCII name (no spaces, slashes or dots).");
            }
            foreach (var g in Entries.GroupBy(a => a.Id).Where(g => g.Count() > 1))
                problems.Add($"Arena id {g.Key} is used {g.Count()} times.");
            return problems;
        }

        public void Save(string path)
        {
            if (Entries.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            var array = new JsonArray();
            foreach (var a in Entries)
            {
                var names = new JsonObject();
                foreach (var (language, name) in a.Names.OrderBy(n => n.Key))
                    if (name.Trim().Length > 0)
                        names[language.ToString()] = name;
                array.Add(new JsonObject { ["id"] = a.Id, ["folder"] = a.Folder, ["name"] = names });
            }
            File.WriteAllText(path, new JsonObject { ["arenas"] = array }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        }
    }

    /// <summary>
    /// Campaign > Arenas: the game's 14 arenas (shown, not editable) and new ones (arenas.json, Yu-Gi-Oh-Campaign). New arena makes one from
    /// a copy of a game arena's files (arenas/&lt;folder&gt;/..., into the Ex patch); restyle it on the Animlists and Sprite sheets pages.
    /// Duelists, story duels and music pick arenas by id.
    /// </summary>
    public sealed class ArenaEditor : UserControl, IGameEditor
    {
        private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly NumericUpDown _id = new() { Minimum = 1, Maximum = 999, Width = 70 };
        private readonly TextBox _folder = new() { Width = 220 };
        private readonly DataGridView _names = new()
        {
            Dock = DockStyle.Top, Height = 190, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        private readonly Label _info = new() { Dock = DockStyle.Top, Height = 48, Padding = new Padding(4) };
        private readonly Panel _editor = new() { Dock = DockStyle.Fill, AutoScroll = true };
        private readonly TableLayoutPanel _fields = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private ArenaFile _file = new();
        private GameFolderFiles? _gameFiles;
        private bool _changed, _binding;
        private static readonly char[] Languages = ['E', 'F', 'G', 'I', 'J', 'S'];
        private int Stock => ArenaFile.GameArenas.Length;

        public ArenaEditor()
        {
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(Button("Save", "Save arenas.json (Ctrl+S)", () => Save()));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Button("New arena...", "A new arena copied from the selected one: a still arena (classic, gx...) copies its picture; an animated one (blimp, vrains_dimension, vrains_2019) copies its animlist.txt and pictures. Into the Ex patch", NewArena));
            tools.Items.Add(Button("New from picture...", "A new still arena from any image (arenas\\<folder>.jpg in the Ex patch)", NewFromPicture));
            tools.Items.Add(Button("New animated...", "A new animated arena from your pictures: each PNG is a layer of a new animlist.txt, back to front (arenas\\<folder>\\ in the Ex patch)", NewAnimated));
            tools.Items.Add(Button("Set picture...", "The arena's background picture (arenas\\<folder>.jpg in the Ex patch): any image, saved as a JPEG. For a game arena too", SetPicture));
            tools.Items.Add(Button("Delete...", "Delete the selected new arena (its files stay in the Ex patch)", DeleteArena));

            void Row(string label, Control control)
            {
                _fields.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
                _fields.Controls.Add(control);
            }
            Row("Arena id (not 1-8, 13-18):", _id);
            Row("Folder (arenas\\<folder>\\animlist.txt):", _folder);
            _names.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Language", ReadOnly = true, FillWeight = 20 });
            _names.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Name (WolfX lists; an empty language uses English)", FillWeight = 80 });
            foreach (char language in Languages)
                _names.Rows.Add(language.ToString(), "");
            _editor.Controls.Add(_names);
            _editor.Controls.Add(_fields);
            _editor.Controls.Add(_info);

            _list.SelectedIndexChanged += (_, _) => Bind();
            _id.ValueChanged += (_, _) => Commit();
            _folder.TextChanged += (_, _) => Commit();
            _names.CellValueChanged += (_, _) => Commit();

            var split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(_editor);
            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(tools);
            Controls.Add(status);
            Load += (_, _) => split.SplitterDistance = Math.Clamp(split.Width * 30 / 100, 220, 360);
            _status.Text = "Open the game data (File > Open).";
        }

        public IReadOnlyCollection<string> Files => [];
        public string SavesTo => $"Additional: Yu-Gi-Oh-Ex\\{ArenaFile.FileName}, new arenas (needs Yu-Gi-Oh-Campaign). Standard: their files, arenas\\<folder>\\..., in the Ex patch.";
        public bool Dirty => _changed;
        private string JsonPath => _gameFiles!.ExPath(ArenaFile.FileName);

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            try { _file = ArenaFile.Load(JsonPath); }
            catch (Exception ex) { _file = new ArenaFile(); _status.Text = $"Could not read {ArenaFile.FileName}: {ex.Message}"; }
            _changed = false;
            Fill(0);
            _status.Text = $"{_file.Entries.Count} new arena(s). The game's 14 are listed first.";
        }

        public bool Save()
        {
            if (_gameFiles == null)
                return false;
            // a toolbar click doesn't take the focus: finish what is being typed (a grid cell, a number box) so it is saved too
            _names.EndEdit();
            _ = _id.Value;   // reading Value commits text typed into the box
            Commit();
            var problems = _file.Problems();
            if (problems.Count > 0)
            {
                MessageBox.Show(string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            _file.Save(JsonPath);
            _changed = false;
            _status.Text = $"Saved {ArenaFile.FileName} ({_file.Entries.Count} new arena(s)).";
            return true;
        }

        private ArenaFile.Arena? Selected => _list.SelectedIndex >= Stock && _list.SelectedIndex - Stock < _file.Entries.Count ? _file.Entries[_list.SelectedIndex - Stock] : null;

        private void Fill(int select)
        {
            _binding = true;
            try
            {
                _list.Items.Clear();
                foreach (var (id, folder) in ArenaFile.GameArenas)
                    _list.Items.Add($"{id}: {folder} (game)");
                foreach (var a in _file.Entries)
                    _list.Items.Add($"{a.Id}: {a.Name()}");
            }
            finally { _binding = false; }
            _list.SelectedIndex = Math.Clamp(select, 0, _list.Items.Count - 1);
        }

        private void Bind()
        {
            if (_binding)
                return;
            var a = Selected;
            _fields.Enabled = _names.Enabled = a != null;
            if (a == null)
            {
                int i = _list.SelectedIndex;
                _info.Text = i >= 0 && i < Stock
                    ? $"{ArenaFile.GameArenas[i].Folder}: one of the game's arenas (arenas\\{ArenaFile.GameArenas[i].Folder}\\). Change its look on the Animlists / Sprite sheets pages (Ex patch); New arena copies it."
                    : "";
                return;
            }
            _binding = true;
            try
            {
                _id.Value = Math.Clamp(a.Id, 1, 999);
                _folder.Text = a.Folder;
                for (int i = 0; i < Languages.Length; i++)
                    _names.Rows[i].Cells[1].Value = a.Names.GetValueOrDefault(Languages[i], "");
                _info.Text = $"A new arena: arenas\\{a.Folder}\\animlist.txt in the Ex patch. Duelists and story duels pick it by id {a.Id} (Yu-Gi-Oh-Campaign).";
            }
            finally { _binding = false; }
        }

        private void Commit()
        {
            if (_binding || Selected is not { } a)
                return;
            a.Id = (int)_id.Value;
            a.Folder = _folder.Text.Trim();
            for (int i = 0; i < Languages.Length; i++)
                a.Names[Languages[i]] = _names.Rows[i].Cells[1].Value as string ?? "";
            _changed = true;
            _binding = true;
            try { _list.Items[_list.SelectedIndex] = $"{a.Id}: {a.Name()}"; }
            finally { _binding = false; }
        }

        // A new arena: the selected game arena's files copied to arenas/<new folder>/ (Ex patch), and an arenas.json entry.
        private void NewArena()
        {
            if (_gameFiles == null)
                return;
            int pick = _list.SelectedIndex;
            var source = pick >= 0 && pick < Stock ? ArenaFile.GameArenas[pick].Folder
                : Selected is { } mine ? mine.Folder : ArenaFile.GameArenas[0].Folder;
            int id = 100;
            while (_file.Entries.Any(a => a.Id == id))
                ++id;
            string folder = $"custom_arena_{id}";
            // an arena is arenas\<folder>.jpg (a still picture), and for the animated ones (blimp, vrains...) arenas\<folder>\animlist.txt + pictures
            string from = $@"arenas\{source}\";
            string picture = $@"arenas\{source}.jpg";
            var paths = (_gameFiles.Archive?.Paths ?? []).Concat(_gameFiles.PatchedFiles)
                .Where(p => p.Replace('/', '\\').StartsWith(from, StringComparison.OrdinalIgnoreCase) || p.Replace('/', '\\').Equals(picture, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (paths.Count == 0)
            {
                _status.Text = $"No files under {from} in the game data.";
                return;
            }
            if (GameFolderFiles.GameRunning)
            {
                _status.Text = "Close the game first: the arena's files go into its patch.";
                return;
            }
            var write = new Dictionary<string, byte[]>();
            foreach (string path in paths)
                if (_gameFiles.Read(path) is { } bytes)
                {
                    string clean = path.Replace('/', '\\');
                    string target = clean.Equals(picture, StringComparison.OrdinalIgnoreCase) ? $@"arenas\{folder}.jpg" : $@"arenas\{folder}\" + clean[from.Length..];
                    // the animlist names its pictures by path: point them at the copies
                    if (target.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                        bytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace($"arenas/{source}/", $"arenas/{folder}/").Replace(from, $@"arenas\{folder}\"));
                    write[target] = bytes;
                }
            _gameFiles.Write(write);
            _file.Entries.Add(new ArenaFile.Arena { Id = id, Folder = folder, Names = { ['E'] = $"New arena ({source})" } });
            _changed = true;
            Fill(Stock + _file.Entries.Count - 1);
            _status.Text = $"Arena {id} made from {source}: {write.Count} files copied to arenas\\{folder}\\ in the Ex patch. Save to keep the arena.";
        }

        // ---- from nothing ----

        private ArenaFile.Arena? AddEntry(string what)
        {
            if (_gameFiles == null)
                return null;
            if (GameFolderFiles.GameRunning)
            {
                _status.Text = "Close the game first: the arena's files go into its patch.";
                return null;
            }
            int id = 100;
            while (_file.Entries.Any(a => a.Id == id))
                ++id;
            var arena = new ArenaFile.Arena { Id = id, Folder = $"custom_arena_{id}", Names = { ['E'] = what } };
            return arena;
        }

        private static byte[] AsJpeg(string file)
        {
            if (Path.GetExtension(file).ToLowerInvariant() is ".jpg" or ".jpeg")
                return File.ReadAllBytes(file);
            using var image = Image.FromFile(file);
            using var stream = new MemoryStream();
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
            return stream.ToArray();
        }

        private void NewFromPicture()
        {
            using var dialog = new OpenFileDialog { Title = "The new arena's background picture", Filter = "Pictures (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dialog.ShowDialog(this) != DialogResult.OK || AddEntry(Path.GetFileNameWithoutExtension(dialog.FileName)) is not { } arena)
                return;
            _gameFiles!.Write($@"arenas\{arena.Folder}.jpg", AsJpeg(dialog.FileName));
            _file.Entries.Add(arena);
            _changed = true;
            Fill(Stock + _file.Entries.Count - 1);
            _status.Text = $"Arena {arena.Id}: arenas\\{arena.Folder}.jpg saved into the Ex patch. Save to keep the arena.";
        }

        // An animlist row is "<picture name without extension, lower case>, x, y, speed"; the game finds <name>.png in the folder ignoring case.
        private void NewAnimated()
        {
            using var dialog = new OpenFileDialog { Title = "The new arena's layers, back to front (PNG)", Filter = "PNG pictures (*.png)|*.png", Multiselect = true };
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.FileNames.Length == 0 || AddEntry("New animated arena") is not { } arena)
                return;
            var write = new Dictionary<string, byte[]>();
            var rows = new StringBuilder();
            foreach (string file in dialog.FileNames)
            {
                string name = string.Concat(Path.GetFileNameWithoutExtension(file).Where(c => char.IsAsciiLetterOrDigit(c) || c == '_'));
                if (name.Length == 0)
                    name = $"layer{rows.Length}";
                write[$@"arenas\{arena.Folder}\{name}.png"] = File.ReadAllBytes(file);
                rows.Append($"{name.ToLowerInvariant()}, 0, 0, 0\n");
            }
            write[$@"arenas\{arena.Folder}\animlist.txt"] = Encoding.UTF8.GetBytes(rows.ToString());
            // the still picture too, for anything that shows the arena without its animation
            write[$@"arenas\{arena.Folder}.jpg"] = AsJpeg(dialog.FileNames[0]);
            _gameFiles!.Write(write);
            _file.Entries.Add(arena);
            _changed = true;
            Fill(Stock + _file.Entries.Count - 1);
            _status.Text = $"Arena {arena.Id}: {dialog.FileNames.Length} layer(s) and animlist.txt in arenas\\{arena.Folder}\\ (Ex patch). Position and move them on the Animlists page; Save to keep the arena.";
        }

        private void SetPicture()
        {
            if (_gameFiles == null)
                return;
            int pick = _list.SelectedIndex;
            string? folder = Selected?.Folder ?? (pick >= 0 && pick < Stock ? ArenaFile.GameArenas[pick].Folder : null);
            if (string.IsNullOrEmpty(folder))
                return;
            using var dialog = new OpenFileDialog { Title = $"Background picture for arenas\\{folder}.jpg", Filter = "Pictures (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            if (GameFolderFiles.GameRunning)
            {
                _status.Text = "Close the game first: the picture goes into its patch.";
                return;
            }
            byte[] jpeg;
            if (Path.GetExtension(dialog.FileName).ToLowerInvariant() is ".jpg" or ".jpeg")
                jpeg = File.ReadAllBytes(dialog.FileName);
            else
            {
                using var image = Image.FromFile(dialog.FileName);
                using var stream = new MemoryStream();
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);
                jpeg = stream.ToArray();
            }
            string path = $@"arenas\{folder}.jpg";
            _gameFiles.Write(path, jpeg);
            _status.Text = $"{path} saved into {_gameFiles.Describe(path)}." +
                ((_gameFiles.Archive?.Paths ?? []).Any(p => p.Replace('/', '\\').StartsWith($@"arenas\{folder}\", StringComparison.OrdinalIgnoreCase))
                    ? " This arena is animated (animlist.txt): its look comes from that folder; edit it on the Animlists page." : "");
        }

        private void DeleteArena()
        {
            if (Selected is not { } a)
            {
                _status.Text = "The game's own arenas are built into the exe; change their look instead (Animlists / Sprite sheets).";
                return;
            }
            if (MessageBox.Show($"Delete arena {a.Id} ({a.Name()})? Duelists and duels set to it fall back to the classic arena.", "Delete arena",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            int index = _list.SelectedIndex;
            _file.Entries.Remove(a);
            _changed = true;
            Fill(index - 1);
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
    }
}
