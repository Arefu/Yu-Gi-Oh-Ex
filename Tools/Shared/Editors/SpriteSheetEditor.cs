using System.IO;
using Types;
namespace Wolf.Editors
{
    /// <summary>
    /// Edits a .dfymoo sprite list over its .png sheet: draw, move and resize sprite rectangles with the mouse, name them, trim them to their
    /// pixels, or find every sprite on a new sheet automatically. "Open from game" opens one of the game's sheets (or one of yours in
    /// Yu-Gi-Oh-Ex\sprites); Save puts a game sheet back into the game data (its .dfymoo, and its .png when you gave it a new picture) and a
    /// new sheet into Yu-Gi-Oh-Ex\sprites as JSON for a plugin to apply. Open... / Save as... work on .dfymoo files anywhere on disk.
    /// </summary>
    public sealed class SpriteSheetEditor : UserControl, IGameEditor
    {
        private readonly SheetCanvas _canvas = new() { Dock = DockStyle.Fill };
        private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "Filter sprites" };
        private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, ToolbarVisible = false, PropertySort = PropertySort.Categorized };
        private readonly PictureBox _preview = new() { Dock = DockStyle.Bottom, Height = 150, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(44, 44, 50) };
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel _cursor = new() { AutoSize = false, Width = 220, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripComboBox _grid = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 55 };
        private readonly ToolStripButton _openFromGame;
        private readonly Stack<string> _undo = new();
        private readonly Stack<string> _redo = new();
        private string _savedText = "";
        private string _beforeEdit = "";
        private bool _syncing;

        private string? _diskPath;       // the .dfymoo on disk this came from / goes to
        private string? _resourcePath;   // or its archive path without extension ("pdui\doShared")
        private byte[]? _newPng;         // a new sheet's picture, written next to the .dfymoo on save

        public SpriteSheetEditor()
        {
            PreviewPopOut.Attach(_canvas, "Sprite sheet");

            PreviewPopOut.Attach(_preview, "Sprite", doubleClick: true);
            _tools.Items.Add(Button("New from PNG...", "Start a sprite list for a picture", NewFromPng));
            _tools.Items.Add(Button("Open...", "Open a .dfymoo file (its .png must be next to it)", OpenFromDisk));
            _openFromGame = Button("Open from game...", "Open one of the game's sprite sheets, or one you saved as JSON in Yu-Gi-Oh-Ex", OpenFromGame);
            _openFromGame.Visible = false;
            _tools.Items.Add(_openFromGame);
            _tools.Items.Add(Button("Save", "Ctrl+S", () => Save(false)));
            _tools.Items.Add(Button("Save as...", "Save somewhere else", () => Save(true)));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("Undo", "Ctrl+Z", Undo));
            _tools.Items.Add(Button("Redo", "Ctrl+Y", Redo));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("Fit to pixels", "Shrink the selected sprite to its visible pixels", () => _canvas.FitSelectedToPixels()));
            _tools.Items.Add(Button("Find sprites", "Add a sprite for every island of pixels no sprite covers yet", FindSprites));
            _tools.Items.Add(Button("Duplicate", "Ctrl+D", () => _canvas.DuplicateSelected()));
            _tools.Items.Add(Button("Delete", "Del", () => _canvas.DeleteSelected()));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("Grid:"));
            foreach (int size in new[] { 1, 2, 4, 8, 16, 32, 64 })
                _grid.Items.Add(size == 1 ? "px" : size.ToString());
            _grid.SelectedIndex = 0;
            _grid.SelectedIndexChanged += (_, _) => { _canvas.GridSize = new[] { 1, 2, 4, 8, 16, 32, 64 }[_grid.SelectedIndex]; _canvas.Invalidate(); };
            _tools.Items.Add(_grid);
            _tools.Items.Add(Toggle("Snap to edges", true, on => _canvas.SnapToEdges = on));
            _tools.Items.Add(Toggle("Names", true, on => _canvas.ShowNames = on));
            _tools.Items.Add(Button("Fit view", "Show the whole sheet", () => _canvas.FitToView()));
            _tools.Items.Add(new ToolStripLabel("  Drag on empty space: new sprite (Ctrl: even over another). Wheel: zoom. Middle drag / Space: pan. Alt: no snapping.")
            {
                ForeColor = SystemColors.GrayText,
            });

            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_list);
            left.Controls.Add(_filter);
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_properties);
            right.Controls.Add(_preview);

            var outer = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            var inner = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            inner.Panel1.Controls.Add(left);
            inner.Panel2.Controls.Add(_canvas);
            outer.Panel1.Controls.Add(inner);
            outer.Panel2.Controls.Add(right);

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_cursor);
            status.Items.Add(_status);

            Controls.Add(outer);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) =>
            {
                inner.SplitterDistance = 220;
                outer.SplitterDistance = Math.Max(300, outer.Width - 300);
            };

            _filter.TextChanged += (_, _) => FillList();
            _list.SelectedIndexChanged += (_, _) =>
            {
                if (_syncing || _list.SelectedItem is not DfymooSprite sprite)
                    return;
                _canvas.Selected = sprite;
                _canvas.ShowSprite(sprite);
            };
            _canvas.SelectionChanged += (_, _) => ShowSelected();
            _canvas.Changed += before => { PushUndo(before); FillList(); };
            _canvas.CursorMoved += (p, c) => _cursor.Text = $"{p.X}, {p.Y}   rgba {c.R} {c.G} {c.B} {c.A}";
            _properties.PropertyValueChanged += (_, _) =>
            {
                PushUndo(_beforeEdit);
                _beforeEdit = _canvas.Sheet.ToText();
                _canvas.Invalidate();
                FillList();
                UpdatePreview();
            };
        }

        private GameFolderFiles? _gameFiles;

        /// <summary>The open data: "Open from game" and Save use it. The sheet being edited stays open.</summary>
        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            _openFromGame.Visible = files.Available;
        }

        bool IGameEditor.Save() => Save(false);

        public IReadOnlyCollection<string> Files => _resourcePath != null ? [_resourcePath + ".dfymoo", _resourcePath + ".png"] : [];

        public string SavesTo => "Standard: the game's .dfymoo (and its .png when you give it a new picture). Additional (new sheets): Yu-Gi-Oh-Ex\\sprites\\*.json (no plugin reads it yet).";

        /// <summary>True when the sheet is one of the game's (its .dfymoo is in the game data), so it is saved there.</summary>
        private bool IsGameSheet => _resourcePath != null && _gameFiles?.Exists(_resourcePath + ".dfymoo") == true;

        public bool Dirty => _canvas.Image != null && _canvas.Sheet.ToText() != _savedText;

        // ---- toolbar helpers ----

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
                case Keys.Control | Keys.D: _canvas.DuplicateSelected(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- loading ----

        private void SetDocument(DfymooSheet sheet, Bitmap image, string? diskPath, string? resourcePath, byte[]? newPng)
        {
            _canvas.Image?.Dispose();
            _diskPath = diskPath;
            _resourcePath = resourcePath;
            _newPng = newPng;
            if (sheet.Width == 0 || sheet.Height == 0)
            {
                sheet.Width = image.Width;
                sheet.Height = image.Height;
            }
            _canvas.SetSheet(sheet, image);
            _undo.Clear();
            _redo.Clear();
            _savedText = newPng != null ? "" : sheet.ToText();
            _beforeEdit = sheet.ToText();
            FillList();
            ShowSelected();
            SetStatus($"{Describe()}: {sheet.Sprites.Count} sprites, sheet {image.Width} x {image.Height}");
        }

        private string Describe() => _diskPath ?? (_resourcePath != null ? _resourcePath + ".dfymoo" : "new sheet");

        private bool ConfirmDiscard() =>
            !Dirty || MessageBox.Show(this, "The sprite list has changes that are not saved. Discard them?", "Sprite sheet",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        /// <summary>Opens a .dfymoo on disk (its .png next to it).</summary>
        public void OpenFile(string dfymooPath)
        {
            string png = Path.ChangeExtension(dfymooPath, ".png");
            var image = Imaging.Load(png);
            if (image == null)
            {
                MessageBox.Show(this, $"{Path.GetFileName(png)} was not found next to the .dfymoo (or could not be read).", "Sprite sheet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetDocument(DfymooSheet.Parse(File.ReadAllText(dfymooPath)), image, dfymooPath, null, null);
        }

        private void OpenFromDisk()
        {
            if (!ConfirmDiscard())
                return;
            using var dialog = new OpenFileDialog { Title = "Open a sprite list", Filter = "Sprite list (*.dfymoo)|*.dfymoo" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                OpenFile(dialog.FileName);
        }

        /// <summary>Where a new sheet's JSON goes (Yu-Gi-Oh-Ex\sprites\pdui\doShared.json; its .png next to it when it's a new picture).</summary>
        private string ExJson(string resource) => _gameFiles!.ExPath(Path.Combine(DfymooSheet.JsonFolder, resource + ".json"));

        /// <summary>The sheets you've saved as JSON in Yu-Gi-Oh-Ex\sprites (resource paths, like the game's).</summary>
        private IEnumerable<string> ExSheets()
        {
            string folder = _gameFiles!.ExPath(DfymooSheet.JsonFolder);
            return Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f)[..^".json".Length])
                : [];
        }

        private void OpenFromGame()
        {
            if (_gameFiles == null || !ConfirmDiscard())
                return;
            var mine = ExSheets().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var sheets = _gameFiles.Paths.Where(p => p.EndsWith(".dfymoo", StringComparison.OrdinalIgnoreCase))
                .Select(p => p[..^".dfymoo".Length]).Concat(mine).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            string? choice = ListPicker.Pick(this, "Open a sprite sheet from the game", sheets,
                p => mine.Contains(p) ? p + "   (your Yu-Gi-Oh-Ex JSON)" : p);
            if (choice == null)
                return;
            try
            {
                DfymooSheet sheet;
                Bitmap? image;
                string json = ExJson(choice);
                if (File.Exists(json))
                {
                    sheet = DfymooSheet.FromJson(File.ReadAllText(json));
                    string png = Path.ChangeExtension(json, ".png");
                    image = File.Exists(png) ? Imaging.Load(png) : Imaging.Decode(_gameFiles.Read(choice + ".png"));
                }
                else
                {
                    var text = _gameFiles.Read(choice + ".dfymoo");
                    sheet = DfymooSheet.Parse(System.Text.Encoding.UTF8.GetString(text ?? []));
                    image = Imaging.Decode(_gameFiles.Read(choice + ".png"));
                }
                if (image == null)
                {
                    MessageBox.Show(this, $"{choice}.png could not be read.", "Sprite sheet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                SetDocument(sheet, image, null, choice, null);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
            {
                MessageBox.Show(this, $"Couldn't read {choice}:\n{ex.Message}", "Sprite sheet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Asks for a new sheet's resource name ("pdui/mySheet"): the name the game (and the plugin) will know it by.</summary>
        private string? AskResourceName()
        {
            using var form = new Form
            {
                Text = "New sprite sheet", Size = new Size(460, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false,
            };
            var box = new TextBox { Location = new Point(12, 36), Width = 420, Text = @"custom\newSheet" };
            form.Controls.Add(new Label { Text = "Resource name (folder\\name, no extension):", Location = new Point(12, 12), AutoSize = true });
            form.Controls.Add(box);
            var ok = new System.Windows.Forms.Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(357, 72) };
            form.Controls.Add(ok);
            form.AcceptButton = ok;
            if (form.ShowDialog(this) != DialogResult.OK)
                return null;
            string name = box.Text.Trim().Replace('/', '\\').Trim('\\');
            return name.Length > 0 && name.IndexOfAny(Path.GetInvalidPathChars()) < 0 ? name : null;
        }

        private void NewFromPng()
        {
            if (!ConfirmDiscard())
                return;
            using var dialog = new OpenFileDialog { Title = "Pick the sheet picture", Filter = "PNG picture (*.png)|*.png" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            byte[] png = File.ReadAllBytes(dialog.FileName);
            var image = Imaging.Decode(png);
            if (image == null)
            {
                MessageBox.Show(this, "That picture could not be read.", "Sprite sheet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetDocument(new DfymooSheet(), image, null, null, png);
            if (MessageBox.Show(this, "Find the sprites on it automatically? (each island of visible pixels becomes a sprite; you can rename and adjust them)",
                    "New sprite sheet", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                FindSprites();
        }

        // ---- saving ----

        private bool Save(bool saveAs)
        {
            if (_canvas.Image == null)
                return false;
            var sheet = _canvas.Sheet;
            var duplicate = sheet.Sprites.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1 || string.IsNullOrWhiteSpace(g.Key));
            if (duplicate != null)
            {
                MessageBox.Show(this, string.IsNullOrWhiteSpace(duplicate.Key) ? "A sprite has no name." : $"Two sprites are called \"{duplicate.Key}\".",
                    "Sprite sheet", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            // one of the game's sheets: back into the game data (no JSON for it any more)
            if (!saveAs && _diskPath == null && IsGameSheet)
            {
                try
                {
                    var write = new Dictionary<string, byte[]> { [_resourcePath + ".dfymoo"] = System.Text.Encoding.UTF8.GetBytes(sheet.ToText()) };
                    if (_newPng != null)
                        write[_resourcePath + ".png"] = _newPng;
                    _gameFiles!.Write(write);
                    string old = ExJson(_resourcePath!);
                    foreach (string mine in new[] { old, Path.ChangeExtension(old, ".png") })
                        if (File.Exists(mine))
                            File.Delete(mine);
                    _newPng = null;
                    _savedText = sheet.ToText();
                    SetStatus($"Saved {_resourcePath}.dfymoo{(write.Count > 1 ? " and .png" : "")} into {_gameFiles.Describe(_resourcePath + ".dfymoo")}.");
                    return true;
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    SetStatus("Not saved: " + ex.Message);
                    return false;
                }
            }

            // a new sheet: JSON in Yu-Gi-Oh-Ex\sprites (its picture as a PNG next to it)
            if (!saveAs && _diskPath == null && _gameFiles?.Available == true)
            {
                _resourcePath ??= AskResourceName();
                if (_resourcePath == null)
                    return false;
                string json = ExJson(_resourcePath);
                Directory.CreateDirectory(Path.GetDirectoryName(json)!);
                File.WriteAllText(json, sheet.ToJson());
                string picture = Path.ChangeExtension(json, ".png");
                if (_newPng != null)
                    File.WriteAllBytes(picture, _newPng);
                else if (!File.Exists(picture) && _gameFiles.Read(_resourcePath + ".png") == null && _canvas.Image != null)
                    _canvas.Image.Save(picture, System.Drawing.Imaging.ImageFormat.Png);   // no picture anywhere else: keep the one being edited
                _newPng = null;
                _savedText = sheet.ToText();
                SetStatus($"Saved {json} (a new sheet: no plugin applies it yet).");
                return true;
            }

            string? target = saveAs ? null : _diskPath;
            if (target == null)
            {
                using var dialog = new SaveFileDialog
                {
                    Title = "Save the sprite list",
                    Filter = "Sprite list (*.dfymoo)|*.dfymoo",
                    FileName = Path.GetFileName(_diskPath ?? _resourcePath ?? "sheet") + ".dfymoo",
                    InitialDirectory = _diskPath != null ? Path.GetDirectoryName(_diskPath) : null,
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return false;
                target = dialog.FileName;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, sheet.ToText());

            // the picture goes next to it when it is not there yet (a new sheet, or the game's sheet saved somewhere new)
            string png = Path.ChangeExtension(target, ".png");
            if (!File.Exists(png))
            {
                byte[]? bytes = _newPng ?? (_resourcePath != null ? _gameFiles?.Read(_resourcePath + ".png") : null)
                                ?? (_diskPath != null && File.Exists(Path.ChangeExtension(_diskPath, ".png")) ? File.ReadAllBytes(Path.ChangeExtension(_diskPath, ".png")) : null);
                if (bytes != null)
                    File.WriteAllBytes(png, bytes);
            }

            _diskPath = target;
            _newPng = null;
            _savedText = sheet.ToText();
            SetStatus($"Saved {target}");
            return true;
        }

        // ---- editing ----

        private void FindSprites()
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                int added = _canvas.FindSprites();
                SetStatus(added == 0 ? "No uncovered pixels found." : $"Added {added} sprites (sprite_1, sprite_2, ...). Rename them in the list.");
            }
            finally { Cursor = Cursors.Default; }
        }

        private void PushUndo(string before)
        {
            if (before == _canvas.Sheet.ToText())
                return;
            _undo.Push(before);
            _redo.Clear();
            SetStatus(Dirty ? $"{Describe()} *" : Describe());
        }

        private void Undo() => Swap(_undo, _redo);

        private void Redo() => Swap(_redo, _undo);

        private void Swap(Stack<string> from, Stack<string> to)
        {
            if (from.Count == 0)
                return;
            to.Push(_canvas.Sheet.ToText());
            _canvas.ReplaceSheet(DfymooSheet.Parse(from.Pop()));
            _beforeEdit = _canvas.Sheet.ToText();
            FillList();
            ShowSelected();
        }

        private void FillList()
        {
            _syncing = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            string filter = _filter.Text.Trim();
            foreach (var sprite in _canvas.Sheet.Sprites.Where(s => filter.Length == 0 || s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                _list.Items.Add(sprite);
            _list.SelectedItem = _canvas.Selected;
            _list.EndUpdate();
            _syncing = false;
        }

        private void ShowSelected()
        {
            var sprite = _canvas.Selected;
            _properties.SelectedObject = sprite;
            _beforeEdit = _canvas.Sheet.ToText();
            _syncing = true;
            _list.SelectedItem = sprite;
            _syncing = false;
            UpdatePreview();
        }

        /// <summary>The selected sprite as the game draws it: the piece put back into its full-size picture.</summary>
        private void UpdatePreview()
        {
            _preview.Image?.Dispose();
            _preview.Image = null;
            var sprite = _canvas.Selected;
            var image = _canvas.Image;
            if (sprite == null || image == null || sprite.Width <= 0 || sprite.Height <= 0)
                return;
            int w = sprite.Trimmed && sprite.FullWidth > 0 ? sprite.FullWidth : sprite.Width;
            int h = sprite.Trimmed && sprite.FullHeight > 0 ? sprite.FullHeight : sprite.Height;
            if (w > 4096 || h > 4096)
                return;
            var preview = new Bitmap(w, h);
            using (var g = Graphics.FromImage(preview))
                g.DrawImage(image, new Rectangle(sprite.Trimmed ? sprite.OffsetX : 0, sprite.Trimmed ? sprite.OffsetY : 0, sprite.Width, sprite.Height), sprite.Bounds, GraphicsUnit.Pixel);
            _preview.Image = preview;
        }

        private void SetStatus(string text) => _status.Text = text;
    }

    /// <summary>A small searchable list to pick one item from.</summary>
    public static class ListPicker
    {
        public static string? Pick(IWin32Window owner, string title, IReadOnlyList<string> items, Func<string, string>? display = null)
        {
            using var form = new Form
            {
                Text = title, Size = new Size(520, 600), StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, ShowIcon = false, ShowInTaskbar = false,
            };
            var filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Filter" };
            var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            var ok = new System.Windows.Forms.Button { Text = "Open", DialogResult = DialogResult.OK, Dock = DockStyle.Bottom, Height = 32 };
            form.Controls.Add(list);
            form.Controls.Add(filter);
            form.Controls.Add(ok);
            form.AcceptButton = ok;

            var shown = new List<string>();
            void Fill()
            {
                list.BeginUpdate();
                list.Items.Clear();
                shown.Clear();
                foreach (string item in items.Where(i => i.Contains(filter.Text.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    shown.Add(item);
                    list.Items.Add(display?.Invoke(item) ?? item);
                }
                list.EndUpdate();
                if (list.Items.Count > 0)
                    list.SelectedIndex = 0;
            }
            filter.TextChanged += (_, _) => Fill();
            list.DoubleClick += (_, _) => { form.DialogResult = DialogResult.OK; form.Close(); };
            Fill();
            return form.ShowDialog(owner) == DialogResult.OK && list.SelectedIndex >= 0 ? shown[list.SelectedIndex] : null;
        }
    }
}
