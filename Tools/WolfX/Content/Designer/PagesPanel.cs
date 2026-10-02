using Wolf.Editors;
using System.Text.RegularExpressions;

namespace WolfEx.Designer
{
    /// <summary>
    /// The page designer: lay out a page on the game's 1920 x 1080 screen with the game's own art, and save it as Yu-Gi-Oh-Ex\pages\&lt;name&gt;.json.
    /// Yu-Gi-Oh-RIX opens a page from a menu action ({"page": "name"}) and builds the elements it supports (header and menu buttons today);
    /// everything else is a preview, marked as such in the palette. See docs/PageDesigner.md.
    /// </summary>
    internal sealed class PagesPanel : UserControl, IContentPanel
    {
        private static readonly int[] GridSizes = [0, 4, 5, 8, 10, 16, 20, 24, 25, 30, 32, 40, 48, 50, 60, 64, 80, 100];
        private static readonly (string Text, float Zoom)[] Zooms = [("Fit", 0), ("25%", .25f), ("33%", 1 / 3f), ("50%", .5f), ("67%", 2 / 3f), ("75%", .75f), ("100%", 1), ("150%", 1.5f), ("200%", 2)];

        private readonly DesignCanvas _canvas = new();
        private readonly Panel _viewport = new() { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.FromArgb(30, 32, 40) };
        private readonly TreeView _palette = new() { Dock = DockStyle.Fill, HideSelection = false, ShowNodeToolTips = true, ItemHeight = 20 };
        private readonly Label _kindInfo = new() { Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(4) };
        private readonly PropertyGrid _properties = new() { Dock = DockStyle.Fill, ToolbarVisible = false, HelpVisible = true, PropertySort = PropertySort.Categorized };
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStrip _tools2 = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripComboBox _pages = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, ToolTipText = "Pages in Yu-Gi-Oh-Ex\\pages" };
        private readonly ToolStripComboBox _grid = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60, ToolTipText = "Grid size (game pixels)" };
        private readonly ToolStripComboBox _zoom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        private readonly ToolStripStatusLabel _cursor = new() { Width = 160, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private ToolStripButton _undo = null!, _redo = null!;

        private GameArt _art = GameArt.None;
        private string _folder = "";
        private string? _loadedName;     // the file the document came from (null = not saved yet)
        private string _savedJson = "";
        private string _beforeEdit = "";
        private bool _loadingList;

        public string Title => "Pages";

        public PagesPanel()
        {
            PreviewPopOut.Attach(_viewport, "Page preview");
            BuildToolbars();
            BuildPalette();

            _viewport.Controls.Add(_canvas);
            _viewport.Resize += (_, _) => { if (_zoom.SelectedIndex == 0) FitZoom(); CentreCanvas(); };
            _canvas.SelectionChanged += (_, _) => ShowProperties();
            _canvas.DocumentChanged += (_, _) => { RefreshProperties(); UpdateState(); };
            _canvas.CursorMoved += p => _cursor.Text = $"x {p.X:0}, y {p.Y:0}";
            _canvas.MouseWheel += Canvas_MouseWheel;

            _properties.PropertyValueChanged += (_, _) => { _canvas.CommitChange(_beforeEdit); _beforeEdit = _canvas.Document.ToJson(); };
            _properties.SelectedGridItemChanged += (_, _) => _beforeEdit = _canvas.Document.ToJson();

            var right = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2, Orientation = Orientation.Vertical };
            var left = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Orientation = Orientation.Vertical };
            left.Panel1.Controls.Add(_palette);
            left.Panel1.Controls.Add(_kindInfo);
            left.Panel1.Controls.Add(new Label
            {
                Text = "Drag onto the page (double-click: middle).\nGreen: shows in game.\nOrange: the game has it, a page can't place it yet.\nGrey: designer only.",
                Dock = DockStyle.Top, Height = 68,
            });
            left.Panel2.Controls.Add(_viewport);
            right.Panel1.Controls.Add(left);
            right.Panel2.Controls.Add(_properties);

            var statusStrip = new StatusStrip { SizingGrip = false };
            statusStrip.Items.Add(_cursor);
            statusStrip.Items.Add(_status);

            Controls.Add(right);
            Controls.Add(_tools2);
            Controls.Add(_tools);
            Controls.Add(statusStrip);

            Load += (_, _) =>
            {
                left.SplitterDistance = 260;
                right.SplitterDistance = Math.Max(300, right.Width - 320);
                FitZoom();
            };

            New();
        }

        // ---- IContentPanel ----

        public void LoadFrom(string extraCardsFolder, string gameFolder)
        {
            _art.Dispose();
            _art = GameArt.Open(Wolf.Editors.GameFolderFiles.Current);
            _canvas.Art = _art;
            SpriteEditor.Art = _art;
            BackgroundConverter.Choices.Clear();
            BackgroundConverter.Choices.AddRange(_art.Backgrounds);
            foreach (string sheet in _art.AtlasResources.Where(s => s.Contains("noise_", StringComparison.OrdinalIgnoreCase) && !s.EndsWith("_small")))
            {
                if (_art.Atlas(sheet)?.Sprites.FirstOrDefault(s => s.Full == new Size(1920, 1080)) is { } full)
                    BackgroundConverter.Choices.Add($"{sheet}#{full.Name}");
            }

            _folder = Path.Combine(extraCardsFolder, "pages");
            RefreshPageList(_loadedName);
            SetStatus(_art.Available ? $"Art from {Wolf.Editors.GameFolderFiles.Current!.Folder}. Pages in {_folder}." : "No game data open: widgets are drawn as boxes.");
            _canvas.Invalidate();
        }

        public bool SaveTo(string extraCardsFolder)
        {
            if (!Dirty)
                return true;
            if (string.IsNullOrWhiteSpace(_canvas.Document.Name))
                return true;   // an unnamed page is not saved by Save all; the page's own Save asks for a name
            return Save();
        }

        // ---- toolbars ----

        private void BuildToolbars()
        {
            _tools.Items.Add(new ToolStripLabel("Page:"));
            _tools.Items.Add(_pages);
            _pages.SelectedIndexChanged += (_, _) => { if (!_loadingList && _pages.SelectedItem is string name) Open(name); };
            _tools.Items.Add(Button("New", "Start an empty page", () => { if (ConfirmDiscard()) New(); }));
            _tools.Items.Add(Button("Save", "Save to Yu-Gi-Oh-Ex\\pages (Ctrl+S)", () => Save()));
            _tools.Items.Add(Button("Save as...", "Save under a new name", () => SaveAs()));
            _tools.Items.Add(Button("Delete", "Delete this page's file", DeletePage));
            _tools.Items.Add(new ToolStripSeparator());

            var gallery = new ToolStripDropDownButton("Widget gallery") { ToolTipText = "A page showing every widget of a group (not saved unless you save it)" };
            foreach (string category in WidgetCatalog.Categories)
                gallery.DropDownItems.Add(category, null, (_, _) => { if (ConfirmDiscard()) ShowGallery(category); });
            _tools.Items.Add(gallery);
            _tools.Items.Add(new ToolStripSeparator());

            _undo = Button("Undo", "Ctrl+Z", () => _canvas.Undo());
            _redo = Button("Redo", "Ctrl+Y", () => _canvas.Redo());
            _tools.Items.Add(_undo);
            _tools.Items.Add(_redo);
            _tools.Items.Add(new ToolStripSeparator());

            _tools.Items.Add(new ToolStripLabel("Zoom:"));
            foreach (var (text, _) in Zooms)
                _zoom.Items.Add(text);
            _zoom.SelectedIndex = 0;
            _zoom.SelectedIndexChanged += (_, _) =>
            {
                if (_zoom.SelectedIndex == 0) FitZoom();
                else _canvas.Zoom = Zooms[_zoom.SelectedIndex].Zoom;
                CentreCanvas();
            };
            _tools.Items.Add(_zoom);

            // second row: grid, snapping, arranging
            _tools2.Items.Add(new ToolStripLabel("Grid:"));
            foreach (int size in GridSizes)
                _grid.Items.Add(size == 0 ? "off" : size.ToString());
            _grid.SelectedItem = "20";
            _grid.SelectedIndexChanged += (_, _) =>
            {
                _canvas.GridSize = GridSizes[_grid.SelectedIndex];
                _canvas.Invalidate();
            };
            _tools2.Items.Add(_grid);
            _tools2.Items.Add(Toggle("Snap to grid", true, on => _canvas.SnapToGrid = on, "Moves and resizes land on the grid"));
            _tools2.Items.Add(Toggle("Snap to guides", true, on => _canvas.SnapToGuides = on, "Snap to other elements' edges and centres, and to the screen's (hold Alt to place freely)"));
            _tools2.Items.Add(Toggle("Show grid", true, on => _canvas.ShowGrid = on));
            _tools2.Items.Add(Toggle("Safe area", false, on => _canvas.ShowSafeArea = on, "The middle 90% of the screen: keep text inside it"));
            _tools2.Items.Add(new ToolStripSeparator());

            var align = new ToolStripDropDownButton("Arrange") { ToolTipText = "One element: against the screen. Several: against the box around them." };
            align.DropDownItems.Add("Align left", null, (_, _) => _canvas.Align(AlignMode.Left));
            align.DropDownItems.Add("Centre horizontally", null, (_, _) => _canvas.Align(AlignMode.CentreX));
            align.DropDownItems.Add("Align right", null, (_, _) => _canvas.Align(AlignMode.Right));
            align.DropDownItems.Add(new ToolStripSeparator());
            align.DropDownItems.Add("Align top", null, (_, _) => _canvas.Align(AlignMode.Top));
            align.DropDownItems.Add("Centre vertically", null, (_, _) => _canvas.Align(AlignMode.CentreY));
            align.DropDownItems.Add("Align bottom", null, (_, _) => _canvas.Align(AlignMode.Bottom));
            align.DropDownItems.Add(new ToolStripSeparator());
            align.DropDownItems.Add("Space evenly across", null, (_, _) => _canvas.Distribute(true));
            align.DropDownItems.Add("Space evenly down", null, (_, _) => _canvas.Distribute(false));
            align.DropDownItems.Add("Same width as first", null, (_, _) => _canvas.MatchSize(true, false));
            align.DropDownItems.Add("Same height as first", null, (_, _) => _canvas.MatchSize(false, true));
            align.DropDownItems.Add("Usual size", null, (_, _) => _canvas.ResetSize());
            align.DropDownItems.Add(new ToolStripSeparator());
            align.DropDownItems.Add("Bring to front  ]", null, (_, _) => _canvas.ChangeOrder(true));
            align.DropDownItems.Add("Send to back  [", null, (_, _) => _canvas.ChangeOrder(false));
            _tools2.Items.Add(align);

            var edit = new ToolStripDropDownButton("Edit");
            edit.DropDownItems.Add("Copy  Ctrl+C", null, (_, _) => _canvas.Copy());
            edit.DropDownItems.Add("Paste  Ctrl+V", null, (_, _) => _canvas.Paste());
            edit.DropDownItems.Add("Duplicate  Ctrl+D", null, (_, _) => _canvas.Duplicate());
            edit.DropDownItems.Add("Delete  Del", null, (_, _) => _canvas.DeleteSelection());
            edit.DropDownItems.Add("Select all  Ctrl+A", null, (_, _) => _canvas.SelectAll());
            _tools2.Items.Add(edit);
            _tools2.Items.Add(new ToolStripLabel("   Arrows nudge 1 px, Shift+arrows one grid step, Alt while dragging = no snapping")
            {
                ForeColor = SystemColors.GrayText,
            });
        }

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        private ToolStripButton Toggle(string text, bool on, Action<bool> set, string? tip = null)
        {
            var button = new ToolStripButton(text) { CheckOnClick = true, Checked = on, ToolTipText = tip ?? text, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.CheckedChanged += (_, _) =>
            {
                set(button.Checked);
                _canvas.Invalidate();
            };
            return button;
        }

        // ---- palette ----

        private void BuildPalette()
        {
            foreach (string category in WidgetCatalog.Categories)
            {
                var group = _palette.Nodes.Add(category);
                group.NodeFont = new Font(_palette.Font, FontStyle.Bold);
                foreach (var kind in WidgetCatalog.All.Where(k => k.Category == category))
                {
                    var node = group.Nodes.Add(kind.Id, $"{kind.Name}  ({kind.SupportText})");
                    node.Tag = kind;
                    node.ToolTipText = kind.SupportExplained + "\n" + kind.GameClass;
                    node.ForeColor = kind.Support switch
                    {
                        InGame.Yes => Color.ForestGreen,
                        InGame.Code => Color.DarkOrange,
                        _ => SystemColors.GrayText,
                    };
                }
            }
            _palette.Nodes[0].Expand();

            _palette.AfterSelect += (_, e) => ShowKind(e.Node?.Tag as WidgetKind);
            _palette.NodeMouseDoubleClick += (_, e) =>
            {
                if (e.Node.Tag is WidgetKind kind)
                    _canvas.Add(kind, new PointF(PageDocument.ScreenWidth / 2f, PageDocument.ScreenHeight / 2f));
            };
            _palette.ItemDrag += (_, e) =>
            {
                if (e.Item is TreeNode { Tag: WidgetKind kind })
                    DoDragDrop(new DataObject(DesignCanvas.DragFormat, kind.Id), DragDropEffects.Copy);
            };
        }

        private void ShowKind(WidgetKind? kind)
        {
            _kindInfo.Text = kind == null ? "" :
                $"{kind.Name}\n{kind.SupportExplained}\n{kind.GameClass}\n{kind.Notes}";
        }

        // ---- properties ----

        private void ShowProperties()
        {
            var selection = _canvas.Selection;
            if (selection.Count == 0)
                _properties.SelectedObject = new PageView(_canvas.Document);
            else if (selection.Count == 1)
                _properties.SelectedObject = new ElementView(selection[0]);
            else
                _properties.SelectedObjects = selection.Select(e => (object)new ElementView(e)).ToArray();
            _beforeEdit = _canvas.Document.ToJson();

            if (selection.Count == 1 && WidgetCatalog.Find(selection[0].Kind) is { } kind)
                ShowKind(kind);
            UpdateState();
        }

        private void RefreshProperties()
        {
            // after an undo the elements are new objects: re-bind
            if (_canvas.Selection.Count == 0 || _properties.SelectedObject is ElementView view && !_canvas.Document.Elements.Contains(view.Element))
                ShowProperties();
            else
                _properties.Refresh();
            _beforeEdit = _canvas.Document.ToJson();
        }

        // ---- zoom ----

        private void FitZoom()
        {
            if (_viewport.ClientSize.Width < 50 || _viewport.ClientSize.Height < 50)
                return;
            float zoom = Math.Min((_viewport.ClientSize.Width - 20f) / PageDocument.ScreenWidth, (_viewport.ClientSize.Height - 20f) / PageDocument.ScreenHeight);
            _canvas.Zoom = Math.Max(0.1f, zoom);
        }

        private void CentreCanvas()
        {
            int x = Math.Max(0, (_viewport.ClientSize.Width - _canvas.Width) / 2);
            int y = Math.Max(0, (_viewport.ClientSize.Height - _canvas.Height) / 2);
            _canvas.Location = new Point(x + _viewport.AutoScrollPosition.X, y + _viewport.AutoScrollPosition.Y);
        }

        private void Canvas_MouseWheel(object? sender, MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) == 0)
                return;
            float zoom = _canvas.Zoom * (e.Delta > 0 ? 1.15f : 1 / 1.15f);
            _canvas.Zoom = zoom;
            CentreCanvas();
            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;
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

        // ---- files ----

        public bool Dirty => _canvas.Document.ToJson() != _savedJson;

        public void MarkSaved() { }   // the page keeps its own saved copy (_savedJson)

        private void New()
        {
            var document = new PageDocument { Name = "", Header = "My Page" };
            SetDocument(document, null);
        }

        private void SetDocument(PageDocument document, string? loadedName)
        {
            _canvas.Document = document;
            _loadedName = loadedName;
            _savedJson = document.ToJson();   // a new page is not "changed" until it is edited (nothing would be lost)
            ShowProperties();
            UpdateState();
        }

        private void RefreshPageList(string? select)
        {
            _loadingList = true;
            _pages.Items.Clear();
            if (Directory.Exists(_folder))
            {
                foreach (string file in Directory.GetFiles(_folder, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                    _pages.Items.Add(Path.GetFileNameWithoutExtension(file));
            }
            _pages.SelectedItem = select;
            _loadingList = false;
        }

        private void Open(string name)
        {
            if (name == _loadedName || !ConfirmDiscard())
            {
                RefreshPageList(_loadedName);
                return;
            }
            try
            {
                var document = PageDocument.FromJson(File.ReadAllText(Path.Combine(_folder, name + ".json")));
                if (string.IsNullOrWhiteSpace(document.Name))
                    document.Name = name;
                SetDocument(document, name);
                SetStatus($"Opened {name}.json");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"{name}.json could not be read: {ex.Message}", "Pages", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshPageList(_loadedName);
            }
        }

        private static bool ValidName(string name) => Regex.IsMatch(name, @"^[A-Za-z0-9_.\-]+$");

        private bool Save()
        {
            var document = _canvas.Document;
            if (string.IsNullOrWhiteSpace(document.Name))
                return SaveAs();
            if (!ValidName(document.Name))
            {
                MessageBox.Show(this, "A page name can only use letters, digits, '.', '_' and '-'.", "Pages", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (string.IsNullOrEmpty(_folder))
            {
                MessageBox.Show(this, "Pick the game folder first.", "Pages", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }
            var duplicate = document.Elements.GroupBy(e => e.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
            {
                MessageBox.Show(this, $"Two elements are called \"{duplicate.Key}\". Ids must be unique.", "Pages", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var missing = document.Elements.Where(e => WidgetCatalog.Find(e.Kind)?.Support != InGame.Yes).Select(e => e.Id).ToList();
            if (missing.Count > 0 && _warnedMissing.Add(document.Name))
            {
                MessageBox.Show(this,
                    $"Saved, but these won't appear in the game yet (only the page header, menu buttons, images and text do): {string.Join(", ", missing)}.\n\n" +
                    "They stay in the file, so they will appear once the game can build them.",
                    "Pages", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }

            Directory.CreateDirectory(_folder);
            string json = document.ToJson();
            File.WriteAllText(Path.Combine(_folder, document.Name + ".json"), json);
            if (_loadedName != null && !string.Equals(_loadedName, document.Name, StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this, $"The page was renamed. Delete the old file {_loadedName}.json?", "Pages", MessageBoxButtons.YesNo) == DialogResult.Yes)
                File.Delete(Path.Combine(_folder, _loadedName + ".json"));
            _loadedName = document.Name;
            _savedJson = json;
            RefreshPageList(_loadedName);
            UpdateState();
            SetStatus($"Saved {document.Name}.json. Open it from a menu button with the action {{\"page\": \"{document.Name}\"}}.");
            return true;
        }

        private bool SaveAs()
        {
            string? name = Prompt("Save page as", "Page name (letters, digits, . _ -):", _canvas.Document.Name);
            if (name == null)
                return false;
            if (!ValidName(name))
            {
                MessageBox.Show(this, "A page name can only use letters, digits, '.', '_' and '-'.", "Pages", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (File.Exists(Path.Combine(_folder, name + ".json")) && !string.Equals(name, _loadedName, StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this, $"{name}.json exists. Replace it?", "Pages", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return false;
            _canvas.Document.Name = name;
            _loadedName = null;   // a new file: nothing to rename
            return Save();
        }

        private void DeletePage()
        {
            if (_loadedName == null)
                return;
            if (MessageBox.Show(this, $"Delete {_loadedName}.json?", "Pages", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            File.Delete(Path.Combine(_folder, _loadedName + ".json"));
            _loadedName = null;
            _savedJson = "";
            RefreshPageList(null);
            New();
        }

        private bool ConfirmDiscard()
        {
            if (!Dirty || _canvas.Document.Elements.Count == 0 && _loadedName == null)
                return true;
            return MessageBox.Show(this, "This page has changes that are not saved. Discard them?", "Pages", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        private string? Prompt(string title, string label, string value)
        {
            using var form = new Form
            {
                Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false, MaximizeBox = false, ShowIcon = false, ClientSize = new Size(360, 110),
            };
            var text = new TextBox { Text = value, Left = 12, Top = 36, Width = 336 };
            var ok = new System.Windows.Forms.Button { Text = "OK", DialogResult = DialogResult.OK, Left = 192, Top = 72, Width = 75 };
            var cancel = new System.Windows.Forms.Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 273, Top = 72, Width = 75 };
            form.Controls.AddRange([new Label { Text = label, Left = 12, Top = 12, AutoSize = true }, text, ok, cancel]);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            return form.ShowDialog(this) == DialogResult.OK && text.Text.Trim().Length > 0 ? text.Text.Trim() : null;
        }

        private void ShowGallery(string category)
        {
            var document = BuildGallery(category);
            SetDocument(document, null);
            _savedJson = document.ToJson();   // a gallery is not "changed" until you change it
            RefreshPageList(null);
            SetStatus($"Gallery: {document.Elements.Count} widgets in \"{category}\". Green = shows in game, orange = the game has it but a page cannot place it yet, grey = designer only.");
        }

        /// <summary>Every widget of one group laid out on a page, shrunk where needed so they all fit on the screen.</summary>
        public static PageDocument BuildGallery(string category)
        {
            var kinds = WidgetCatalog.All.Where(k => k.Category == category).ToList();
            var document = new PageDocument { Name = "", Header = category };
            const float margin = 30, gap = 24, top = 150;

            for (float scale = 1f; scale > 0.05f; scale *= 0.9f)
            {
                document.Elements.Clear();
                float x = margin, y = top, rowHeight = 0;
                bool fits = true;
                foreach (var kind in kinds)
                {
                    float w = kind.Size.Width * scale, h = kind.Size.Height * scale;
                    if (x + w > PageDocument.ScreenWidth - margin && x > margin)
                    {
                        x = margin;
                        y += rowHeight + gap;
                        rowHeight = 0;
                    }
                    if (y + h > PageDocument.ScreenHeight - margin || w > PageDocument.ScreenWidth - 2 * margin)
                    {
                        fits = false;
                        break;
                    }
                    var element = new PageElement
                    {
                        Id = document.NewId(kind.Id), Kind = kind.Id, X = (float)Math.Round(x), Y = (float)Math.Round(y),
                        Width = (float)Math.Round(w), Height = (float)Math.Round(h), Z = document.Elements.Count, Text = kind.DefaultText,
                        TextSize = kind.HasText && scale < 1 ? kind.DefaultTextSize * scale : null,
                    };
                    if (kind.HasButtons)
                        element.Buttons = [new PageButton { Label = "Booster Packs" }, new PageButton { Label = "Card Shop" }];
                    if (kind.HasSprite)
                    {
                        element.Resource = "pdui/doShared";
                        element.Sprite = "optionbox";
                    }
                    document.Elements.Add(element);
                    x += w + gap;
                    rowHeight = Math.Max(rowHeight, h);
                }
                if (fits)
                    break;
            }
            return document;
        }

        private void UpdateState()
        {
            _undo.Enabled = _canvas.CanUndo;
            _redo.Enabled = _canvas.CanRedo;
            var document = _canvas.Document;
            int preview = document.Elements.Count(e => WidgetCatalog.Find(e.Kind)?.Support != InGame.Yes);
            string name = string.IsNullOrEmpty(document.Name) ? "(unsaved page)" : document.Name + ".json";
            _status.Text = $"{name}{(Dirty ? " *" : "")}   {document.Elements.Count} elements, {preview} not shown in game   {_statusMessage}";
        }

        private string _statusMessage = "";
        private readonly HashSet<string> _warnedMissing = new(StringComparer.OrdinalIgnoreCase);

        private void SetStatus(string text)
        {
            _statusMessage = text;
            UpdateState();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _art.Dispose();
            base.Dispose(disposing);
        }
    }
}
