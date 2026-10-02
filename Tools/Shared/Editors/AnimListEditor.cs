using System.IO;
using Types;
using System.Drawing.Drawing2D;

namespace Wolf.Editors
{
    /// <summary>
    /// Edits an animlist (title screen or arena layers) on the game's 1920 x 1080 screen: drag layers into place with grid and guide snapping,
    /// reorder, add pictures, pick the menu side. "Open from game" / "New for game" work on the game's own title animations and arenas, and
    /// Save puts the animlist and the pictures you added back into the game data (the game only plays the animations it lists, so they are
    /// always the game's). Open... / Save as... work on animlist files anywhere on disk. An animlist saved as JSON in Yu-Gi-Oh-Ex\animlists
    /// by the old WolfEx still opens, and moves into the game data when saved.
    /// </summary>
    public sealed class AnimListEditor : UserControl, IGameEditor
    {
        private readonly SceneCanvas _canvas = new() { Dock = DockStyle.Fill };
        private readonly CheckedListBox _layers = new() { Dock = DockStyle.Fill, IntegralHeight = false, CheckOnClick = false };
        private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.NoSort };
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStrip _tools2 = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel _cursor = new() { AutoSize = false, Width = 140 };
        private readonly ToolStripComboBox _side = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        private readonly ToolStripComboBox _grid = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 55 };
        private readonly ToolStripComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly ToolStripItem[] _titleOnly;
        private readonly ToolStripButton _play;
        private readonly ToolStripButton _openFromGame, _newForGame;
        private readonly Stack<string> _undo = new();
        private readonly Stack<string> _redo = new();
        private readonly Dictionary<string, byte[]> _imported = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Bitmap?> _pictures = new(StringComparer.OrdinalIgnoreCase);
        private string _savedText = "";
        private string _beforeEdit = "";
        private bool _syncing;

        private string? _diskFile;        // animlist on disk
        private string? _gameFolder;      // or the animation's folder in the game ("title\anims\Utopia39")
        private string _fileName = "animlist.combined.txt";
        private GameFolderFiles? _gameFiles;

        private static readonly int[] GridSizes = [1, 5, 10, 20, 40, 60];

        public AnimListEditor()
        {
            _savedText = _canvas.List.ToText();   // the empty list it starts with is not a change
            PreviewPopOut.Attach(_canvas, "Animlist preview");
            _tools.Items.Add(Button("New...", "Start an empty animlist in a folder of your pictures", NewOnDisk));
            _tools.Items.Add(Button("Open...", "Open an animlist file (its pictures must be in the same folder)", OpenFromDisk));
            _openFromGame = Button("Open from game...", "Open one of the game's title animations or arenas", OpenFromGame);
            _newForGame = Button("New for game...", "Replace one of the game's title animations with your own pictures", NewForGame);
            _openFromGame.Visible = _newForGame.Visible = false;
            _tools.Items.Add(_openFromGame);
            _tools.Items.Add(_newForGame);
            _tools.Items.Add(Button("Save", "Ctrl+S", () => Save(false)));
            _tools.Items.Add(Button("Save as...", "Save somewhere else", () => Save(true)));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("Undo", "Ctrl+Z", Undo));
            _tools.Items.Add(Button("Redo", "Ctrl+Y", Redo));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("Add picture...", "Add a layer (a PNG you pick is copied into the folder when you save)", AddPicture));
            _tools.Items.Add(Button("Remove", "Remove the selected layer", RemoveLayer));
            _tools.Items.Add(Button("Forward", "Move the selected layer one step to the front", () => MoveLayer(1)));
            _tools.Items.Add(Button("Back", "Move the selected layer one step to the back", () => MoveLayer(-1)));

            // Title screen or arena: the same file format, but only the title screen has a menu (and reads "side"). Set from the
            // file's path when it's opened (see AnimlistKinds.FromPath); pick it by hand for files outside the game.
            _tools2.Items.Add(new ToolStripLabel("Screen:"));
            _kind.Items.AddRange(["Title screen", "Arena"]);
            _kind.SelectedIndex = 0;   // before the handler: _titleOnly isn't built yet
            _kind.SelectedIndexChanged += (_, _) => ApplyKind();
            _tools2.Items.Add(_kind);
            _play = Toggle("Play slides", false, on => _canvas.Playing = on);
            _play.ToolTipText = "Animate the layers' slide as the game does (YGO::ANIM::Animlist_UpdateSlide): y = Y + slide x cos(t), one cycle every 2 pi seconds (about 6.3 s), all layers together";
            _tools2.Items.Add(_play);
            _tools2.Items.Add(new ToolStripSeparator());
            var menuLabel = new ToolStripLabel("Title menu:");
            _tools2.Items.Add(menuLabel);
            _side.Items.AddRange(["on the left (side 0)", "on the right (side 1)"]);
            _side.SelectedIndex = 0;
            _side.SelectedIndexChanged += (_, _) =>
            {
                if (_syncing || _canvas.List.Side == _side.SelectedIndex)
                    return;
                string before = _canvas.List.ToText();
                _canvas.List.Side = _side.SelectedIndex;
                Changed(before);
            };
            _tools2.Items.Add(_side);
            _tools2.Items.Add(new ToolStripSeparator());
            _tools2.Items.Add(new ToolStripLabel("Grid:"));
            foreach (int size in GridSizes)
                _grid.Items.Add(size == 1 ? "off" : size.ToString());
            _grid.SelectedIndex = 3;
            _grid.SelectedIndexChanged += (_, _) => { _canvas.GridSize = GridSizes[_grid.SelectedIndex]; _canvas.Invalidate(); };
            _canvas.GridSize = 20;
            _tools2.Items.Add(_grid);
            _tools2.Items.Add(Toggle("Snap to grid", true, on => _canvas.SnapToGrid = on));
            _tools2.Items.Add(Toggle("Snap to guides", true, on => _canvas.SnapToGuides = on));
            _tools2.Items.Add(Toggle("Show grid", false, on => _canvas.ShowGrid = on));
            var showMenu = Toggle("Show title menu", true, on => _canvas.ShowMenu = on);
            _tools2.Items.Add(showMenu);
            _titleOnly = [menuLabel, _side, showMenu];
            _tools2.Items.Add(new ToolStripLabel("  Click picks the top visible pixel. Arrows nudge (Shift: grid step). Alt: no snapping. Untick a layer to hide it here.")
            {
                ForeColor = SystemColors.GrayText,
            });

            var outer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            var inner = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            inner.Panel1.Controls.Add(_layers);
            inner.Panel1.Controls.Add(new Label { Text = "Layers (front at the top)", Dock = DockStyle.Top, Height = 20 });
            inner.Panel2.Controls.Add(_canvas);
            outer.Panel1.Controls.Add(inner);
            outer.Panel2.Controls.Add(_properties);

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_cursor);
            status.Items.Add(_status);
            Controls.Add(outer);
            Controls.Add(_tools2);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) =>
            {
                inner.SplitterDistance = 230;
                outer.SplitterDistance = Math.Max(300, outer.Width - 280);
            };

            _canvas.Picture = Picture;
            _canvas.SelectionChanged += (_, _) => ShowSelected();
            _canvas.Changed += Changed;
            _canvas.CursorMoved += p => _cursor.Text = $"x {p.X}, y {p.Y}";
            _layers.SelectedIndexChanged += (_, _) =>
            {
                if (!_syncing && _layers.SelectedItem is AnimlistLayer layer)
                    _canvas.Selected = layer;
            };
            _layers.ItemCheck += (_, e) =>
            {
                if (_syncing || _layers.Items[e.Index] is not AnimlistLayer layer)
                    return;
                if (e.NewValue == CheckState.Checked) _canvas.Hidden.Remove(layer);
                else _canvas.Hidden.Add(layer);
                _canvas.Invalidate();
            };
            _properties.PropertyValueChanged += (_, e) =>
            {
                if (e.ChangedItem?.Label == nameof(AnimlistLayer.Name))
                    _pictures.Clear();
                Changed(_beforeEdit);
            };
        }

        /// <summary>The open data: "Open from game", "New for game" and Save use it. The animlist being edited stays open.</summary>
        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            _openFromGame.Visible = _newForGame.Visible = files.Available;
        }

        bool IGameEditor.Save() => Save(false);

        public IReadOnlyCollection<string> Files => _gameFolder != null ? [Path.Combine(_gameFolder, _fileName)] : [];

        public string SavesTo => "Standard: the game's animlist and the pictures you add, into the game data (the game only plays the animations it lists).";

        public bool Dirty => _canvas.List.ToText() != _savedText || _imported.Count > 0;

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        private static ToolStripButton Toggle(string text, bool on, Action<bool> set)
        {
            var button = new ToolStripButton(text) { CheckOnClick = true, Checked = on, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.CheckedChanged += (_, _) => set(button.Checked);
            return button;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Control | Keys.S: Save(false); return true;
                case Keys.Control | Keys.Z: Undo(); return true;
                case Keys.Control | Keys.Y: Redo(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- pictures ----

        /// <summary>A layer's picture: an imported file first, then the folder's name.png / name.jpg / name as it is.</summary>
        private Bitmap? Picture(string name)
        {
            if (_pictures.TryGetValue(name, out var cached))
                return cached;
            Bitmap? picture = null;
            if (_imported.TryGetValue(name, out var bytes))
                picture = Imaging.Decode(bytes);
            else
            {
                foreach (string candidate in new[] { name + ".png", name + ".jpg", name })
                {
                    byte[]? data = null;
                    if (_diskFile != null)
                    {
                        string path = Path.Combine(Path.GetDirectoryName(_diskFile)!, candidate);
                        data = File.Exists(path) ? File.ReadAllBytes(path) : null;
                    }
                    else if (_gameFolder != null && _gameFiles != null)
                    {
                        // a picture the old WolfEx saved next to its JSON first, then the game's
                        string mine = Path.Combine(ExFolderFor(_gameFolder), candidate);
                        data = File.Exists(mine) ? File.ReadAllBytes(mine) : _gameFiles.Read(Path.Combine(_gameFolder, candidate));
                    }
                    if (data != null && (picture = Imaging.Decode(data)) != null)
                        break;
                }
            }
            _pictures[name] = picture;
            return picture;
        }

        private void ClearPictures()
        {
            foreach (var picture in _pictures.Values)
                picture?.Dispose();
            _pictures.Clear();
        }

        // ---- documents ----

        /// <summary>
        /// Title screen or arena, from where the file is (the game's own naming): arenas\&lt;X&gt;\animlist.txt is an arena (read by
        /// YGO::DUEL::Arena_LoadBackground), title\anims\&lt;X&gt;\animlist.combined.txt / .cropped.txt a title screen (RIX::ScreenTitle::LoadTitleAnim).
        /// Anything else (a file outside the game) goes by its name: animlist.txt = arena, otherwise title.
        /// </summary>
        public static bool IsArenaPath(string path)
        {
            string normal = path.Replace('/', '\\');
            if (normal.Contains(@"\arenas\", StringComparison.OrdinalIgnoreCase) || normal.StartsWith(@"arenas\", StringComparison.OrdinalIgnoreCase))
                return true;
            if (normal.Contains(@"\title\", StringComparison.OrdinalIgnoreCase) || normal.StartsWith(@"title\", StringComparison.OrdinalIgnoreCase))
                return false;
            return string.Equals(Path.GetFileName(normal), "animlist.txt", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsArena => _kind.SelectedIndex == 1;

        /// <summary>Arenas have no title menu and ignore "side": hide those controls and the menu overlay for them.</summary>
        private void ApplyKind()
        {
            foreach (var item in _titleOnly)
                item.Visible = !IsArena;
            _canvas.IsArena = IsArena;
            _canvas.Invalidate();
        }

        private void SetDocument(Animlist list, string? diskFile, string? gameFolder, string fileName, bool isNew)
        {
            ClearPictures();
            _imported.Clear();
            _diskFile = diskFile;
            _gameFolder = gameFolder;
            _fileName = fileName;
            _kind.SelectedIndex = IsArenaPath(diskFile ?? Path.Combine(gameFolder ?? "", fileName)) ? 1 : 0;
            ApplyKind();
            _canvas.SetList(list);
            _undo.Clear();
            _redo.Clear();
            _savedText = list.ToText();   // a new list is not "changed" until it is edited (nothing would be lost)
            Refill();
            ShowSelected();
            SetStatus($"{Describe()}: {list.Layers.Count} layers");
        }

        private string Describe() => _diskFile ?? (_gameFolder != null ? Path.Combine(_gameFolder, _fileName) : "new animlist");

        private bool ConfirmDiscard() =>
            !Dirty || MessageBox.Show(this, "The animlist has changes that are not saved. Discard them?", "Animlist",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        public void OpenFile(string path)
        {
            SetDocument(Animlist.Parse(File.ReadAllText(path)), path, null, Path.GetFileName(path), false);
        }

        private void OpenFromDisk()
        {
            if (!ConfirmDiscard())
                return;
            using var dialog = new OpenFileDialog { Title = "Open an animlist", Filter = "Animlist (animlist*.txt)|animlist*.txt|Text files (*.txt)|*.txt" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                OpenFile(dialog.FileName);
        }

        private void NewOnDisk()
        {
            if (!ConfirmDiscard())
                return;
            using var dialog = new SaveFileDialog
            {
                Title = "Where the new animlist goes (put its pictures in the same folder)",
                Filter = "Title screen animation|animlist.combined.txt|Arena|animlist.txt",
                FileName = "animlist.combined.txt",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            // The game names them differently, so the file name says which it is: make sure it matches the type picked.
            bool arena = dialog.FilterIndex == 2;
            string file = Path.Combine(Path.GetDirectoryName(dialog.FileName)!, arena ? "animlist.txt" : "animlist.combined.txt");
            SetDocument(new Animlist(), file, null, Path.GetFileName(file), true);
            _kind.SelectedIndex = arena ? 1 : 0;
        }

        private IReadOnlyList<string> GameAnimlists() =>
            _gameFiles == null ? [] : _gameFiles.Paths
                .Where(p => Path.GetFileName(p).StartsWith("animlist", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();

        /// <summary>Where the old WolfEx put a game animation's JSON and pictures (Yu-Gi-Oh-Ex\animlists\title\anims\Utopia39).</summary>
        private string ExFolderFor(string gameFolder) => _gameFiles!.ExPath(Path.Combine(Animlist.JsonFolder, gameFolder));

        /// <summary>The old WolfEx's JSON for an animlist (Yu-Gi-Oh-Ex\animlists\title\anims\Utopia39\animlist.combined.json).</summary>
        private string ExJson(string gameFolder, string fileName) => Path.Combine(ExFolderFor(gameFolder), Path.ChangeExtension(fileName, ".json"));

        private void OpenFromGame()
        {
            if (_gameFiles == null || !ConfirmDiscard())
                return;
            string? choice = ListPicker.Pick(this, "Open a title animation or arena", GameAnimlists(),
                p => File.Exists(ExJson(Path.GetDirectoryName(p)!, Path.GetFileName(p))) ? p + "   (your Yu-Gi-Oh-Ex JSON)" : p);
            if (choice == null)
                return;
            string folder = Path.GetDirectoryName(choice)!, file = Path.GetFileName(choice), json = ExJson(folder, file);
            try
            {
                Animlist list;
                if (File.Exists(json))
                    list = Animlist.FromJson(File.ReadAllText(json));
                else if (_gameFiles.Read(choice) is byte[] text)
                    list = Animlist.Parse(System.Text.Encoding.UTF8.GetString(text));
                else
                    return;
                SetDocument(list, null, folder, file, false);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
            {
                MessageBox.Show(this, $"Couldn't read {choice}:\n{ex.Message}", "Animlist", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// The game only plays the title animations and arenas it knows (fixed lists in the exe: the title folders in RIX::ScreenTitle's
        /// table, the arenas in YGO::DUEL::Arena_LoadBackground's), so a new one replaces one of them: it is saved into that folder in the
        /// game data, with your pictures.
        /// </summary>
        private void NewForGame()
        {
            if (_gameFiles == null || !ConfirmDiscard())
                return;
            var folders = GameAnimlists().Select(p => Path.GetDirectoryName(p)!)
                .Where(p => p.StartsWith("title", StringComparison.OrdinalIgnoreCase) || p.StartsWith("arenas", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            string? folder = ListPicker.Pick(this, "Which title animation or arena does yours replace?", folders,
                p => (IsArenaPath(p + "\\") ? "Arena:  " : "Title:  ") + p);
            if (folder == null)
                return;
            bool arena = IsArenaPath(folder + "\\");
            SetDocument(new Animlist(), null, folder, arena ? "animlist.txt" : "animlist.combined.txt", true);
            SetStatus($"New {(arena ? "arena" : "title animation")} for {folder}: add your pictures with Add picture. Saving puts it and your pictures into the game data; the game's pictures stay usable too.");
        }

        private bool Save(bool saveAs)
        {
            if (_diskFile == null && _gameFolder == null)
                saveAs = true;
            var list = _canvas.List;
            if (list.Layers.Count > Animlist.MaxLayers &&
                MessageBox.Show(this, $"The game reads only the first {Animlist.MaxLayers} layers. Save anyway?", "Animlist", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;

            // one of the game's animations: the animlist and your pictures into the game data
            if (!saveAs && _diskFile == null && _gameFolder != null && _gameFiles != null)
            {
                try
                {
                    var write = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
                    {
                        [Path.Combine(_gameFolder, _fileName)] = System.Text.Encoding.UTF8.GetBytes(list.ToText()),
                    };
                    foreach (var (name, bytes) in _imported)
                        write[Path.Combine(_gameFolder, name + ".png")] = bytes;
                    // pictures the old WolfEx saved next to its JSON move into the game data too
                    string old = ExFolderFor(_gameFolder);
                    foreach (var layer in list.Layers.Where(l => !_imported.ContainsKey(l.Name)))
                        if (Path.Combine(old, layer.Name + ".png") is var mine && File.Exists(mine) && !_gameFiles.Exists(Path.Combine(_gameFolder, layer.Name + ".png")))
                            write[Path.Combine(_gameFolder, layer.Name + ".png")] = File.ReadAllBytes(mine);
                    _gameFiles.Write(write);
                    string json = ExJson(_gameFolder, _fileName);
                    if (File.Exists(json))
                        File.Delete(json);
                    _imported.Clear();   // Picture() now finds them in the game data
                    _savedText = list.ToText();
                    SetStatus($"Saved {Path.Combine(_gameFolder, _fileName)}{(write.Count > 1 ? $" and {write.Count - 1} pictures" : "")} into {_gameFiles.Describe(Path.Combine(_gameFolder, _fileName))}.");
                    return true;
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    SetStatus("Not saved: " + ex.Message);
                    return false;
                }
            }

            string? target = saveAs ? null : _diskFile;
            if (target == null)
            {
                using var dialog = new SaveFileDialog
                {
                    Title = "Save the animlist",
                    Filter = "Animlist|animlist*.txt|Text files (*.txt)|*.txt",
                    FileName = _fileName,
                    InitialDirectory = _diskFile != null ? Path.GetDirectoryName(_diskFile) : null,
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return false;
                target = dialog.FileName;
            }

            string folder = Path.GetDirectoryName(target)!;
            Directory.CreateDirectory(folder);
            File.WriteAllText(target, list.ToText());
            foreach (var (name, bytes) in _imported)
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), bytes);

            // pictures now come from the saved folder
            _diskFile = target;
            _gameFolder = null;
            _imported.Clear();
            _savedText = list.ToText();
            SetStatus($"Saved {target}");
            return true;
        }

        // ---- layers ----

        private void AddPicture()
        {
            using var dialog = new OpenFileDialog { Title = "Add pictures as layers", Filter = "Pictures (*.png;*.jpg)|*.png;*.jpg", Multiselect = true };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            string before = _canvas.List.ToText();
            AnimlistLayer? last = null;
            foreach (string file in dialog.FileNames)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                bool inFolder = _diskFile != null && string.Equals(Path.GetDirectoryName(file), Path.GetDirectoryName(_diskFile), StringComparison.OrdinalIgnoreCase);
                if (!inFolder)
                {
                    byte[] bytes = File.ReadAllBytes(file);
                    if (file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        // saved as .png next to the animlist
                        using var image = Imaging.Decode(bytes);
                        if (image == null) continue;
                        using var png = new MemoryStream();
                        image.Save(png, System.Drawing.Imaging.ImageFormat.Png);
                        bytes = png.ToArray();
                    }
                    _imported[name] = bytes;
                    _pictures.Remove(name);
                }
                last = new AnimlistLayer { Name = name };
                _canvas.List.Layers.Add(last);
            }
            Changed(before);
            if (last != null)
                _canvas.Selected = last;
        }

        private void RemoveLayer()
        {
            if (_canvas.Selected is not { } layer)
                return;
            string before = _canvas.List.ToText();
            _canvas.List.Layers.Remove(layer);
            _canvas.Selected = null;
            Changed(before);
        }

        private void MoveLayer(int direction)
        {
            if (_canvas.Selected is not { } layer)
                return;
            var layers = _canvas.List.Layers;
            int index = layers.IndexOf(layer), to = index + direction;
            if (to < 0 || to >= layers.Count)
                return;
            string before = _canvas.List.ToText();
            layers.RemoveAt(index);
            layers.Insert(to, layer);
            Changed(before);
        }

        // ---- undo / views ----

        private void Changed(string before)
        {
            if (before != _canvas.List.ToText())
            {
                _undo.Push(before);
                _redo.Clear();
            }
            _beforeEdit = _canvas.List.ToText();
            _canvas.Invalidate();
            Refill();
            _properties.Refresh();
            SetStatus(Dirty ? Describe() + " *" : Describe());
        }

        private void Undo() => Swap(_undo, _redo);

        private void Redo() => Swap(_redo, _undo);

        private void Swap(Stack<string> from, Stack<string> to)
        {
            if (from.Count == 0)
                return;
            to.Push(_canvas.List.ToText());
            int selected = _canvas.Selected != null ? _canvas.List.Layers.IndexOf(_canvas.Selected) : -1;
            var list = Animlist.Parse(from.Pop());
            _canvas.ReplaceList(list, selected >= 0 && selected < list.Layers.Count ? list.Layers[selected] : null);
            _beforeEdit = list.ToText();
            Refill();
            ShowSelected();
        }

        private void Refill()
        {
            _syncing = true;
            _side.SelectedIndex = _canvas.List.Side == 0 ? 0 : 1;
            _layers.BeginUpdate();
            _layers.Items.Clear();
            // front first, like a layer panel
            foreach (var layer in Enumerable.Reverse(_canvas.List.Layers))
                _layers.Items.Add(layer, !_canvas.Hidden.Contains(layer));
            _layers.SelectedItem = _canvas.Selected;
            _layers.EndUpdate();
            _syncing = false;
        }

        private void ShowSelected()
        {
            _properties.SelectedObject = _canvas.Selected;
            _beforeEdit = _canvas.List.ToText();
            _syncing = true;
            _layers.SelectedItem = _canvas.Selected;
            _syncing = false;
        }

        private void SetStatus(string text) => _status.Text = text;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ClearPictures();
            base.Dispose(disposing);
        }
    }

    /// <summary>The 1920 x 1080 title screen with an animlist's layers on it.</summary>
    public sealed class SceneCanvas : Control
    {
        public const int ScreenWidth = 1920, ScreenHeight = 1080;

        private Animlist _list = new();
        private float _zoom = 0.5f;
        private PointF _origin;
        private AnimlistLayer? _selected;
        private bool _dragging, _changed;
        private Point _downScreen;
        private Point _startPos;
        private string? _snapshot;
        private readonly List<(bool Vertical, int At)> _guides = [];
        private readonly TextureBrush _checker = Imaging.Checkerboard(16);

        public SceneCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Color.FromArgb(24, 24, 30);
            _timer.Tick += (_, _) =>
            {
                _phase += _clock.Elapsed.TotalSeconds;
                _clock.Restart();
                if (_phase > 2 * Math.PI)
                    _phase -= 2 * Math.PI;
                Invalidate();
            };
        }

        public Func<string, Bitmap?> Picture { get; set; } = _ => null;
        public HashSet<AnimlistLayer> Hidden { get; } = [];
        public int GridSize { get; set; } = 20;
        public bool SnapToGrid { get; set; } = true;
        public bool SnapToGuides { get; set; } = true;
        public bool ShowGrid { get; set; }
        public bool ShowMenu { get; set; } = true;
        public bool IsArena { get; set; }   // arenas have no title menu
        public Animlist List => _list;

        // ---- slide playback (YGO::ANIM::Animlist_UpdateSlide) ----
        // Every frame the game adds the frame time to one phase, wraps it at 2 pi, and puts each layer with a non-zero slide at
        // y + slide * cos(phase): a vertical bob of +-slide pixels, one cycle every 2 pi seconds, all layers together.
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
        private readonly System.Diagnostics.Stopwatch _clock = new();
        private double _phase;

        public bool Playing
        {
            get => _timer.Enabled;
            set
            {
                if (value == _timer.Enabled)
                    return;
                if (value)
                {
                    _phase = 0;   // the game starts the phase at 0 when the screen loads
                    _clock.Restart();
                    _timer.Start();
                }
                else
                {
                    _timer.Stop();
                    _phase = 0;
                }
                Invalidate();
            }
        }

        /// <summary>How far the slide has moved the layer right now (0 when not playing: the editor shows the file's positions).</summary>
        private int SlideOffset(AnimlistLayer layer) =>
            Playing && layer.Slide != 0 ? (int)Math.Round(layer.Slide * Math.Cos(_phase)) : 0;

        public AnimlistLayer? Selected
        {
            get => _selected;
            set { _selected = value; Invalidate(); SelectionChanged?.Invoke(this, EventArgs.Empty); }
        }

        public event EventHandler? SelectionChanged;
        public event Action<string>? Changed;
        public event Action<Point>? CursorMoved;

        public void SetList(Animlist list)
        {
            _list = list;
            _selected = null;
            Hidden.Clear();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ReplaceList(Animlist list, AnimlistLayer? selected)
        {
            _list = list;
            _selected = selected;
            Hidden.Clear();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            _zoom = Math.Max(0.05f, Math.Min((Width - 20f) / ScreenWidth, (Height - 20f) / ScreenHeight));
            _origin = new PointF((Width - ScreenWidth * _zoom) / 2, (Height - ScreenHeight * _zoom) / 2);
        }

        private Point ToScreenPixel(Point p) => new((int)Math.Floor((p.X - _origin.X) / _zoom), (int)Math.Floor((p.Y - _origin.Y) / _zoom));

        /// <summary>Where the layer is drawn now (its file position, moved by the slide while playing).</summary>
        private Rectangle Bounds(AnimlistLayer layer)
        {
            var picture = Picture(layer.Name);
            return new Rectangle(layer.X, layer.Y + SlideOffset(layer), picture?.Width ?? 200, picture?.Height ?? 120);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var screen = new RectangleF(_origin.X, _origin.Y, ScreenWidth * _zoom, ScreenHeight * _zoom);
            g.FillRectangle(_checker, screen);

            var state = g.Save();
            g.SetClip(screen);
            g.TranslateTransform(_origin.X, _origin.Y);
            g.ScaleTransform(_zoom, _zoom);
            g.InterpolationMode = InterpolationMode.Bilinear;
            foreach (var layer in _list.Layers)
            {
                if (Hidden.Contains(layer))
                    continue;
                var picture = Picture(layer.Name);
                int y = layer.Y + SlideOffset(layer);
                if (picture != null)
                    g.DrawImage(picture, layer.X, y, picture.Width, picture.Height);
                else
                {
                    using var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(120, 255, 80, 80), Color.FromArgb(60, 0, 0, 0));
                    g.FillRectangle(hatch, layer.X, y, 200, 120);
                    g.DrawString("missing: " + layer.Name, SystemFonts.DefaultFont, Brushes.White, layer.X + 4, y + 4);
                }
            }

            if (ShowMenu && !IsArena)
            {
                // RIX::ScreenTitle::LoadTitleAnim: the logo and menu are centred on x 568 (side 0) or 1367 (other sides)
                float centre = _list.Side == 0 ? 568 : 1367;
                var menu = new RectangleF(centre - 330, 140, 660, 800);
                using var fill = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
                using var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 3) { DashStyle = DashStyle.Dash };
                g.FillRectangle(fill, menu);
                g.DrawRectangle(pen, menu.X, menu.Y, menu.Width, menu.Height);
                using var font = new Font("Segoe UI", 36, FontStyle.Bold);
                using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("title logo\nand menu", font, Brushes.White, menu, format);
            }
            g.Restore(state);

            if (ShowGrid && GridSize > 1 && GridSize * _zoom >= 5)
            {
                using var gridPen = new Pen(Color.FromArgb(35, 255, 255, 255));
                for (int x = 0; x <= ScreenWidth; x += GridSize)
                    g.DrawLine(gridPen, _origin.X + x * _zoom, screen.Top, _origin.X + x * _zoom, screen.Bottom);
                for (int y = 0; y <= ScreenHeight; y += GridSize)
                    g.DrawLine(gridPen, screen.Left, _origin.Y + y * _zoom, screen.Right, _origin.Y + y * _zoom);
            }
            using (var edge = new Pen(Color.FromArgb(120, 255, 255, 255)))
                g.DrawRectangle(edge, screen.X, screen.Y, screen.Width, screen.Height);

            foreach (var (vertical, at) in _guides)
            {
                using var guide = new Pen(Color.FromArgb(230, 255, 60, 160));
                if (vertical) g.DrawLine(guide, _origin.X + at * _zoom, 0, _origin.X + at * _zoom, Height);
                else g.DrawLine(guide, 0, _origin.Y + at * _zoom, Width, _origin.Y + at * _zoom);
            }

            if (_selected != null)
            {
                var r = Bounds(_selected);
                using var pen = new Pen(Color.DeepSkyBlue, 2);
                g.DrawRectangle(pen, _origin.X + r.X * _zoom, _origin.Y + r.Y * _zoom, r.Width * _zoom, r.Height * _zoom);
                string text = $"{_selected.Name}  {_selected.X}, {_selected.Y}";
                var size = g.MeasureString(text, Font);
                float tx = Math.Max(0, _origin.X + r.X * _zoom), ty = Math.Max(0, _origin.Y + r.Y * _zoom - size.Height);
                g.FillRectangle(Brushes.DeepSkyBlue, tx, ty, size.Width, size.Height);
                g.DrawString(text, Font, Brushes.Black, tx, ty);
            }
        }

        /// <summary>The front-most layer with a visible pixel under the point (so a full-screen background does not hide the rest).</summary>
        private AnimlistLayer? HitTest(Point p)
        {
            for (int i = _list.Layers.Count - 1; i >= 0; i--)
            {
                var layer = _list.Layers[i];
                if (Hidden.Contains(layer))
                    continue;
                var r = Bounds(layer);
                if (!r.Contains(p))
                    continue;
                var picture = Picture(layer.Name);
                if (picture == null || picture.GetPixel(p.X - r.X, p.Y - r.Y).A > 20)
                    return layer;
            }
            return null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left)
                return;
            var hit = HitTest(ToScreenPixel(e.Location));
            Selected = hit;
            if (hit == null)
                return;
            _dragging = true;
            _changed = false;
            _downScreen = e.Location;
            _startPos = new Point(hit.X, hit.Y);
            _snapshot = _list.ToText();
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            CursorMoved?.Invoke(ToScreenPixel(e.Location));
            if (!_dragging || _selected == null)
            {
                Cursor = HitTest(ToScreenPixel(e.Location)) != null ? Cursors.SizeAll : Cursors.Default;
                return;
            }
            int x = _startPos.X + (int)Math.Round((e.X - _downScreen.X) / _zoom);
            int y = _startPos.Y + (int)Math.Round((e.Y - _downScreen.Y) / _zoom);
            var r = Bounds(_selected);
            _guides.Clear();
            if ((ModifierKeys & Keys.Alt) == 0)
            {
                x += SnapAxis(x, r.Width, true);
                y += SnapAxis(y, r.Height, false);
            }
            _selected.X = x;
            _selected.Y = y;
            _changed = true;
            Invalidate();
        }

        private int SnapAxis(int start, int size, bool vertical)
        {
            if (SnapToGuides)
            {
                int threshold = Math.Max(2, (int)(8 / _zoom));
                var targets = new List<int> { 0, (vertical ? ScreenWidth : ScreenHeight) / 2, vertical ? ScreenWidth : ScreenHeight };
                foreach (var other in _list.Layers)
                {
                    if (other == _selected || Hidden.Contains(other)) continue;
                    var r = Bounds(other);
                    if (vertical) targets.AddRange([r.Left, r.Left + r.Width / 2, r.Right]);
                    else targets.AddRange([r.Top, r.Top + r.Height / 2, r.Bottom]);
                }
                int best = int.MaxValue, bestTarget = 0;
                foreach (int edge in new[] { start, start + size / 2, start + size })
                    foreach (int target in targets)
                        if (Math.Abs(target - edge) < Math.Abs(best) && Math.Abs(target - edge) <= threshold)
                        {
                            best = target - edge;
                            bestTarget = target;
                        }
                if (best != int.MaxValue)
                {
                    _guides.Add((vertical, bestTarget));
                    return best;
                }
            }
            if (SnapToGrid && GridSize > 1)
                return (int)Math.Round(start / (double)GridSize) * GridSize - start;
            return 0;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Capture = false;
            if (_dragging && _changed && _snapshot != null)
                Changed?.Invoke(_snapshot);
            _dragging = false;
            _snapshot = null;
            _guides.Clear();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (_selected != null && e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
            {
                int step = e.Shift ? Math.Max(2, GridSize) : 1;
                string before = _list.ToText();
                _selected.X += e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
                _selected.Y += e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
                Changed?.Invoke(before);
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timer.Dispose();
                _checker.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
