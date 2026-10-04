using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The game's bitmap fonts (fontbin\FONT_ID_*.fbin + .png, File Type Libraries/FontBin). Pick a font, see its atlas with every glyph's
    /// rectangle, pick a glyph (in the list or on the atlas) and change its metrics (offsets, advance) or what it draws as. Aliases (a
    /// character drawn with another's image, as Greek Α is drawn with A) can be added and removed, in one size or every size of the face.
    /// The preview draws text with the font as the game would (outline, then glyph) and shows the characters the font lacks; "Check text"
    /// does that for every font (the game draws a missing character as '_'). Only the .fbin files change: the atlas images are not edited here (new glyph images need a repack).
    /// </summary>
    public sealed class FontEditor : UserControl, IGameEditor
    {
        private const string Folder = FontBin.GameFolder;

        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripComboBox _font = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
        private readonly ToolStripComboBox _table = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly ToolStripTextBox _find = new() { Width = 110, ToolTipText = "A character, or its code (U+0041, 0x41)" };
        private readonly ToolStripComboBox _show = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false, MultiSelect = false,
        };
        private readonly FontAtlasView _atlas = new() { Dock = DockStyle.Fill };
        private readonly FontPreview _preview = new() { Dock = DockStyle.Fill };
        private readonly TextBox _previewText = new()
        {
            Dock = DockStyle.Top, Multiline = true, Height = 46, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10f),
            Text = "Dark Magician ATK 2500 / DEF 2100\r\nÀÉÎÕÜ ñ ß Ŋ «quotes» ブラック・マジシャン",
        };
        private readonly ComboBox _scale = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 60 };
        private readonly CheckBox _outline = new() { Text = "Outline", AutoSize = true, Checked = true };
        private readonly Label _missing = new() { AutoSize = true, ForeColor = Color.Firebrick, Margin = new Padding(12, 6, 3, 3) };
        private readonly Label _fontInfo = new() { Dock = DockStyle.Top, AutoSize = false, Height = 64, Padding = new Padding(2, 4, 2, 0), ForeColor = SystemColors.GrayText };
        private readonly Label _glyphTitle = new() { AutoSize = true, Font = new Font("Segoe UI", 14f, FontStyle.Bold), Margin = new Padding(3, 4, 3, 6) };
        private readonly TextBox _source = new() { Width = 90 };
        private readonly NumericUpDown _offsetX = Number(), _offsetY = Number(), _advance = Number();
        private readonly Label _size = Value(), _outlineWidth = Value(), _rect = Value(), _outlineRect = Value();
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private GameFolderFiles? _files;
        private List<string> _paths = [];
        private readonly Dictionary<string, Loaded> _loaded = new(StringComparer.OrdinalIgnoreCase);
        private List<int> _rows = [];
        private bool _binding;

        private sealed class Loaded(FontBin font, byte[] original)
        {
            public FontBin Font { get; } = font;
            public byte[] Original { get; set; } = original;
            public Bitmap? Atlas { get; set; }
            public bool AtlasRead { get; set; }
            public bool Changed => !Font.ToBytes().AsSpan().SequenceEqual(Original);
        }

        public FontEditor()
        {
            _table.Items.AddRange(["Glyphs", "Alternate (@/ ... @|)"]);
            _table.SelectedIndex = 0;
            _show.Items.AddRange(["All", "Images", "Aliases"]);
            _show.SelectedIndex = 0;
            _tools.Items.Add(Button("Save", "Save the changed fonts into the game data (Ctrl+S)", () => Save()));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("Font:"));
            _tools.Items.Add(_font);
            _tools.Items.Add(_table);
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripLabel("Show:"));
            _tools.Items.Add(_show);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("Add alias...", "A new character drawn with an existing glyph's image (e.g. Ŋ drawn as N)", AddAlias));
            _tools.Items.Add(Button("Remove", "Remove the selected glyph from this table", RemoveSelected));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("Check text in every font...", "Which fonts can't draw the preview text, and which characters they lack", CheckEveryFont));

            _list.Columns.Add("Char", 46);
            _list.Columns.Add("Code", 66);
            _list.Columns.Add("Draws as", 72);
            _list.Columns.Add("Size", 58);
            _list.Columns.Add("Offset", 70);
            _list.Columns.Add("Advance", 60);
            _list.RetrieveVirtualItem += (_, e) => e.Item = Row(_rows[e.ItemIndex]);
            _list.SelectedIndexChanged += (_, _) => ShowSelected(fromAtlas: false);

            _font.SelectedIndexChanged += (_, _) => ShowFont();
            _table.SelectedIndexChanged += (_, _) => ShowFont();
            _find.TextChanged += (_, _) => Refill();
            _show.SelectedIndexChanged += (_, _) => Refill();
            _atlas.GlyphClicked += glyph => SelectGlyph(glyph);
            _previewText.TextChanged += (_, _) => UpdatePreview();
            foreach (int scale in new[] { 1, 2, 3, 4 })
                _scale.Items.Add($"{scale}x");
            _scale.SelectedIndex = 0;
            _scale.SelectedIndexChanged += (_, _) => UpdatePreview();
            _outline.CheckedChanged += (_, _) => UpdatePreview();
            _source.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ApplySource();
                    e.SuppressKeyPress = true;
                }
            };
            _source.Leave += (_, _) => ApplySource();
            _offsetX.ValueChanged += (_, _) => EditMetric(g => g.OffsetX = (float)_offsetX.Value);
            _offsetY.ValueChanged += (_, _) => EditMetric(g => g.OffsetY = (float)_offsetY.Value);
            _advance.ValueChanged += (_, _) => EditMetric(g => g.Advance = (float)_advance.Value);

            // the selected glyph's details
            var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(6) };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            details.Controls.Add(_glyphTitle, 0, 0);
            details.SetColumnSpan(_glyphTitle, 2);
            details.RowCount = 1;   // the rows below start under the title
            AddRow(details, "Draws as", _source, "The character whose image this glyph uses (itself, or another for an alias). Type a character or U+XXXX and press Enter.");
            AddRow(details, "Offset X", _offsetX, "From the pen position to the image's left edge, in pixels.");
            AddRow(details, "Offset Y", _offsetY, "From the baseline to the image's top edge (negative = above the baseline).");
            AddRow(details, "Advance", _advance, "How far the pen moves on after this glyph.");
            AddRow(details, "Image size", _size, null);
            AddRow(details, "Outline", _outlineWidth, null);
            AddRow(details, "Atlas rect", _rect, null);
            AddRow(details, "Outline rect", _outlineRect, null);
            var detailsPanel = new Panel { Dock = DockStyle.Fill };
            detailsPanel.Controls.Add(details);
            detailsPanel.Controls.Add(_fontInfo);

            var previewBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            previewBar.Controls.Add(new Label { Text = "Preview, scale:", AutoSize = true, Margin = new Padding(3, 6, 3, 3) });
            previewBar.Controls.Add(_scale);
            previewBar.Controls.Add(_outline);
            previewBar.Controls.Add(_missing);
            var previewPanel = new Panel { Dock = DockStyle.Fill };
            previewPanel.Controls.Add(_preview);
            previewPanel.Controls.Add(_previewText);
            previewPanel.Controls.Add(previewBar);

            var atlasAndDetails = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            atlasAndDetails.Panel1.Controls.Add(_atlas);
            atlasAndDetails.Panel2.Controls.Add(detailsPanel);
            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, FixedPanel = FixedPanel.Panel2 };
            right.Panel1.Controls.Add(atlasAndDetails);
            right.Panel2.Controls.Add(previewPanel);
            var main = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            main.Panel1.Controls.Add(_list);
            main.Panel2.Controls.Add(right);

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(main);
            Controls.Add(_tools);
            Controls.Add(status);

            // splitter positions need the real sizes, which the inner splits only get after the outer ones: each is placed once it fits
            bool mainPlaced = false, rightPlaced = false, detailsPlaced = false;
            main.Layout += (_, _) => mainPlaced = mainPlaced || TrySplit(main, 400);
            right.Layout += (_, _) => rightPlaced = rightPlaced || TrySplit(right, right.Height - 210);
            atlasAndDetails.Layout += (_, _) => detailsPlaced = detailsPlaced || TrySplit(atlasAndDetails, atlasAndDetails.Width - 300);
            ShowGlyph(null);
        }

        /// <summary>Sets the splitter when the split is big enough for it (and some room on both sides); false to try again later.</summary>
        private static bool TrySplit(SplitContainer split, int distance)
        {
            int size = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
            if (size < 300 || distance < split.Panel1MinSize + 80 || distance > size - split.Panel2MinSize - split.SplitterWidth - 80)
                return false;
            split.SplitterDistance = distance;
            return true;
        }

        private static NumericUpDown Number() => new() { Minimum = -4096, Maximum = 4096, DecimalPlaces = 2, Increment = 1, Width = 90 };

        private static Label Value() => new() { AutoSize = true, Margin = new Padding(3, 6, 3, 3) };

        private static void AddRow(TableLayoutPanel table, string caption, Control control, string? tip)
        {
            int row = table.RowCount++;
            var label = new Label { Text = caption + ":", AutoSize = true, Margin = new Padding(3, 6, 8, 3) };
            table.Controls.Add(label, 0, row);
            table.Controls.Add(control, 1, row);
            if (tip != null)
            {
                var tips = new ToolTip();
                tips.SetToolTip(label, tip);
                tips.SetToolTip(control, tip);
            }
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

        // ---- IGameEditor ----

        public bool Dirty => _loaded.Values.Any(l => l.Changed);

        public IReadOnlyCollection<string> Files => _paths;

        public string SavesTo => "Standard: fontbin\\FONT_ID_*.fbin (the glyph metrics and aliases; the .png atlases are not changed here).";

        public void Open(GameFolderFiles files)
        {
            string? was = _font.SelectedItem as string;
            _files = files;
            foreach (var loaded in _loaded.Values)
                loaded.Atlas?.Dispose();
            _loaded.Clear();
            _paths = files.Paths
                .Where(p => p.Replace('/', '\\').StartsWith(Folder + "\\", StringComparison.OrdinalIgnoreCase) && p.EndsWith(".fbin", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Replace('/', '\\'))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _binding = true;
            _font.Items.Clear();
            foreach (string path in _paths)
                _font.Items.Add(Path.GetFileNameWithoutExtension(path));
            _binding = false;
            if (_font.Items.Count == 0)
            {
                _status.Text = "This data has no fontbin\\*.fbin files.";
                ShowFont();
                return;
            }
            int index = was != null ? _font.Items.IndexOf(was) : -1;
            _font.SelectedIndex = index >= 0 ? index : Math.Max(0, _font.Items.IndexOf("FONT_ID_NOTO_20"));
            _status.Text = $"{_paths.Count} fonts.";
        }

        public bool Save()
        {
            if (_files == null)
                return false;
            var write = new Dictionary<string, byte[]>();
            foreach (var (path, loaded) in _loaded)
                if (loaded.Changed)
                    write[path] = loaded.Font.ToBytes();
            try
            {
                if (write.Count > 0)
                    _files.Write(write);
                foreach (var (path, data) in write)
                    _loaded[path].Original = data;
                _status.Text = write.Count == 0 ? "Nothing changed." : $"Saved {write.Count} font(s) into {_files.Describe(write.Keys.First())}.";
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        // ---- fonts ----

        private string? CurrentPath => _font.SelectedIndex >= 0 && _font.SelectedIndex < _paths.Count ? _paths[_font.SelectedIndex] : null;

        private Loaded? Current => CurrentPath is { } path ? LoadFont(path) : null;

        private List<FontGlyph> CurrentTable => Current is { } loaded ? (_table.SelectedIndex == 1 ? loaded.Font.AltGlyphs : loaded.Font.Glyphs) : [];

        private Loaded? LoadFont(string path, bool atlas = true)
        {
            if (!_loaded.TryGetValue(path, out var loaded))
            {
                if (_files?.Read(path) is not { } data)
                    return null;
                try
                {
                    loaded = new Loaded(FontBin.Parse(data), data);
                }
                catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
                {
                    _status.Text = $"{path}: {ex.Message}";
                    return null;
                }
                _loaded[path] = loaded;
            }
            if (atlas && !loaded.AtlasRead)
            {
                loaded.AtlasRead = true;
                loaded.Atlas = Imaging.Decode(_files?.Read(Path.ChangeExtension(path, ".png")));
            }
            return loaded;
        }

        private void ShowFont()
        {
            if (_binding)
                return;
            var loaded = Current;
            _atlas.SetFont(loaded?.Font, loaded?.Atlas, CurrentTable);
            _preview.SetFont(loaded?.Font, loaded?.Atlas);
            if (loaded == null)
                _fontInfo.Text = "";
            else
            {
                var f = loaded.Font;
                var inv = CultureInfo.InvariantCulture;
                _fontInfo.Text = $"{f.Face}  (kind {f.Kind})\n{f.Glyphs.Count} glyphs ({f.Glyphs.Count(g => g.IsAlias)} aliases), {f.AltGlyphs.Count} alternate\n" +
                                 $"Atlas {(loaded.Atlas is { } a ? $"{a.Width}x{a.Height}" : "missing")}\n" +
                                 $"Metrics {string.Join(" ", f.Metrics.Select(m => m.ToString("0.##", inv)))}";
            }
            Refill();
            _atlas.FitToView();
            UpdatePreview();
        }

        // ---- the glyph list ----

        private void Refill()
        {
            var table = CurrentTable;
            string find = _find.Text.Trim();
            char? code = ParseChar(find);
            _rows = Enumerable.Range(0, table.Count).Where(i =>
            {
                var g = table[i];
                if (_show.SelectedIndex == 1 && g.IsAlias || _show.SelectedIndex == 2 && !g.IsAlias)
                    return false;
                if (find.Length == 0)
                    return true;
                return code is { } c ? g.Char == c || g.SourceChar == c : $"{(int)g.Char:X4}".Contains(find, StringComparison.OrdinalIgnoreCase);
            }).ToList();
            _list.VirtualListSize = _rows.Count;
            _list.Invalidate();
            if (_rows.Count > 0 && code != null)
            {
                _list.SelectedIndices.Clear();
                _list.SelectedIndices.Add(0);
                _list.EnsureVisible(0);
            }
            ShowSelected(fromAtlas: false);
        }

        private ListViewItem Row(int index)
        {
            var table = CurrentTable;
            if (index >= table.Count)
                return new ListViewItem(["", "", "", "", "", ""]);
            var g = table[index];
            var inv = CultureInfo.InvariantCulture;
            return new ListViewItem(
            [
                Shown(g.Char),
                $"U+{(int)g.Char:X4}",
                g.IsAlias ? $"{Shown(g.SourceChar)} U+{(int)g.SourceChar:X4}" : "",
                $"{g.PixelWidth}x{g.PixelHeight}",
                string.Create(inv, $"{g.OffsetX:0.##}, {g.OffsetY:0.##}"),
                g.Advance.ToString("0.##", inv),
            ])
            {
                ForeColor = g.IsAlias ? Color.FromArgb(40, 100, 170) : SystemColors.WindowText,
            };
        }

        private static string Shown(char c) => char.IsControl(c) || char.IsWhiteSpace(c) ? "␣" : c.ToString();

        /// <summary>A character typed as itself, or as U+XXXX / 0xXXXX / \uXXXX.</summary>
        private static char? ParseChar(string text)
        {
            text = text.Trim();
            if (text.Length == 1)
                return text[0];
            foreach (string prefix in new[] { "U+", "u+", "0x", "0X", "\\u" })
                if (text.StartsWith(prefix) && ushort.TryParse(text[prefix.Length..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                    return (char)code;
            return null;
        }

        private FontGlyph? SelectedGlyph
        {
            get
            {
                var table = CurrentTable;
                return _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count && _rows[_list.SelectedIndices[0]] < table.Count
                    ? table[_rows[_list.SelectedIndices[0]]]
                    : null;
            }
        }

        private void SelectGlyph(FontGlyph glyph)
        {
            int index = CurrentTable.IndexOf(glyph);
            int row = _rows.IndexOf(index);
            if (row < 0 && index >= 0)
            {
                // not in the filtered list: show everything again
                _binding = true;
                _find.Text = "";
                _show.SelectedIndex = 0;
                _binding = false;
                Refill();
                row = _rows.IndexOf(index);
            }
            if (row < 0)
                return;
            _list.SelectedIndices.Clear();
            _list.SelectedIndices.Add(row);
            _list.EnsureVisible(row);
            ShowSelected(fromAtlas: true);
        }

        private void ShowSelected(bool fromAtlas)
        {
            var glyph = SelectedGlyph;
            ShowGlyph(glyph);
            _atlas.Selected = glyph;
            if (glyph != null && !fromAtlas)
                _atlas.ShowGlyph(glyph);
        }

        private void ShowGlyph(FontGlyph? g)
        {
            _binding = true;
            foreach (Control control in new Control[] { _source, _offsetX, _offsetY, _advance })
                control.Enabled = g != null;
            if (g == null)
            {
                _glyphTitle.Text = "No glyph selected";
                _source.Text = "";
                _size.Text = _outlineWidth.Text = _rect.Text = _outlineRect.Text = "";
            }
            else
            {
                _glyphTitle.Text = $"{Shown(g.Char)}   U+{(int)g.Char:X4}";
                _source.Text = g.IsAlias ? Shown(g.SourceChar) : "(itself)";
                _offsetX.Value = Clamp(g.OffsetX, _offsetX);
                _offsetY.Value = Clamp(g.OffsetY, _offsetY);
                _advance.Value = Clamp(g.Advance, _advance);
                _size.Text = $"{g.PixelWidth} x {g.PixelHeight}";
                _outlineWidth.Text = g.OutlineWidth == 0 ? "none" : $"{g.OutlineWidth} px";
                if (Current?.Atlas is { } atlas)
                {
                    _rect.Text = g.PixelRect(atlas.Width, atlas.Height).ToString();
                    _outlineRect.Text = g.OutlineWidth == 0 ? "-" : g.OutlinePixelRect(atlas.Width, atlas.Height).ToString();
                }
                else
                {
                    _rect.Text = string.Create(CultureInfo.InvariantCulture, $"uv {g.U0:0.####},{g.V0:0.####} - {g.U1:0.####},{g.V1:0.####}");
                    _outlineRect.Text = "";
                }
            }
            _binding = false;
        }

        private static decimal Clamp(float value, NumericUpDown box) => Math.Clamp((decimal)value, box.Minimum, box.Maximum);

        // ---- edits ----

        private void EditMetric(Action<FontGlyph> edit)
        {
            if (_binding || SelectedGlyph is not { } glyph)
                return;
            edit(glyph);
            AfterEdit();
        }

        private void AfterEdit()
        {
            _list.Invalidate();
            _atlas.Invalidate();
            UpdatePreview();
            _status.Text = Dirty ? "Changed (not saved yet)." : "No changes.";
        }

        /// <summary>"Draws as" changed: the glyph takes the other glyph's image (its atlas rects and outline), keeping its own metrics.</summary>
        private void ApplySource()
        {
            if (_binding || SelectedGlyph is not { } glyph)
                return;
            string text = _source.Text.Trim();
            char source = text is "" or "(itself)" ? glyph.SourceChar : ParseChar(text) ?? glyph.SourceChar;
            if (source == glyph.SourceChar)
            {
                ShowGlyph(glyph);
                return;
            }
            var table = CurrentTable;
            var from = table.FirstOrDefault(g => g.Char == source);
            if (from == null || from == glyph)
            {
                _status.Text = $"This table has no U+{(int)source:X4} to draw as.";
                ShowGlyph(glyph);
                return;
            }
            if (!glyph.IsAlias && table.Any(g => g != glyph && g.SourceChar == glyph.Char))
            {
                _status.Text = $"Other glyphs draw as {Shown(glyph.Char)}: change those first (its image would have no glyph of its own).";
                ShowGlyph(glyph);
                return;
            }
            CopyImage(from, glyph);
            ShowGlyph(glyph);
            AfterEdit();
        }

        private static void CopyImage(FontGlyph from, FontGlyph to)
        {
            to.SourceChar = from.SourceChar;
            to.PixelWidth = from.PixelWidth;
            to.PixelHeight = from.PixelHeight;
            to.Width = from.Width;
            to.Height = from.Height;
            to.U0 = from.U0;
            to.V0 = from.V0;
            to.U1 = from.U1;
            to.V1 = from.V1;
            to.OutlineU0 = from.OutlineU0;
            to.OutlineV0 = from.OutlineV0;
            to.OutlineU1 = from.OutlineU1;
            to.OutlineV1 = from.OutlineV1;
            to.OutlineWidth = from.OutlineWidth;
        }

        private static FontGlyph AliasOf(FontGlyph from, char c)
        {
            var glyph = new FontGlyph { Char = c, Reserved = from.Reserved, OffsetX = from.OffsetX, OffsetY = from.OffsetY, Advance = from.Advance };
            CopyImage(from, glyph);
            return glyph;
        }

        /// <summary>Puts a glyph in its place by character (the game's tables are sorted). False when the character is already there.</summary>
        private static bool Insert(List<FontGlyph> table, FontGlyph glyph)
        {
            int index = table.FindIndex(g => g.Char >= glyph.Char);
            if (index >= 0 && table[index].Char == glyph.Char)
                return false;
            table.Insert(index < 0 ? table.Count : index, glyph);
            return true;
        }

        private void AddAlias()
        {
            if (Current is not { } loaded || CurrentPath is not { } path)
                return;
            using var dialog = new AliasDialog(loaded.Font.Face, SelectedGlyph?.SourceChar);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.NewChar is not { } c || dialog.DrawsAs is not { } source)
                return;
            var targets = dialog.EverySize
                ? _paths.Select(p => (Path: p, Loaded: LoadFont(p, atlas: false))).Where(t => t.Loaded?.Font.Face == loaded.Font.Face).ToList()
                : [(path, (Loaded?)loaded)];
            int added = 0;
            var skipped = new List<string>();
            foreach (var (fontPath, target) in targets)
            {
                if (target == null)
                    continue;
                string name = Path.GetFileNameWithoutExtension(fontPath);
                if (target.Font.Find(source) == null)
                {
                    skipped.Add($"{name} has no {Shown(source)}");
                    continue;
                }
                if (target.Font.Find(c) != null)
                {
                    skipped.Add($"{name} already has {Shown(c)}");
                    continue;
                }
                // the main table, and the alternate one when it has the source too (the game's aliases are in both)
                foreach (var table in new[] { target.Font.Glyphs, target.Font.AltGlyphs })
                    if (table.FirstOrDefault(g => g.Char == source) is { } from)
                        Insert(table, AliasOf(from, c));
                added++;
            }
            ShowFont();
            if (Current?.Font.Find(c) is { } made && _table.SelectedIndex == 0)
                SelectGlyph(made);
            _status.Text = $"Added {Shown(c)} (U+{(int)c:X4}) drawn as {Shown(source)} to {added} font(s)" +
                           (skipped.Count > 0 ? $"; skipped: {string.Join(", ", skipped.Distinct())}" : "") + ". Not saved yet.";
        }

        private void RemoveSelected()
        {
            if (SelectedGlyph is not { } glyph)
                return;
            var table = CurrentTable;
            var users = table.Where(g => g != glyph && g.SourceChar == glyph.Char).ToList();
            string question = users.Count > 0
                ? $"{users.Count} other glyph(s) draw as {Shown(glyph.Char)} ({string.Join(" ", users.Take(12).Select(g => Shown(g.Char)))}). They keep its image, but it will have no glyph of its own.\n\nRemove {Shown(glyph.Char)} (U+{(int)glyph.Char:X4}) anyway?"
                : $"Remove {Shown(glyph.Char)} (U+{(int)glyph.Char:X4}) from this table? The game then draws nothing for it in this font.";
            if (MessageBox.Show(this, question, "Remove glyph", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            table.Remove(glyph);
            Refill();
            AfterEdit();
        }

        // ---- preview ----

        private void UpdatePreview()
        {
            _preview.PreviewText = _previewText.Text;
            _preview.PreviewScale = _scale.SelectedIndex + 1;
            _preview.ShowOutline = _outline.Checked;
            _preview.Invalidate();
            var font = Current?.Font;
            var missing = font?.Missing(_previewText.Text) ?? [];
            _missing.Text = font == null ? "" : missing.Length == 0 ? "" : $"Not in this font (the game draws _): {string.Join(" ", missing.Select(c => $"{Shown(c)}"))}";
        }

        private void CheckEveryFont()
        {
            string text = _previewText.Text;
            var list = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
            list.Columns.Add("Font", 230);
            list.Columns.Add("Face", 190);
            list.Columns.Add("Missing", 420);
            int complete = 0;
            foreach (string path in _paths)
            {
                if (LoadFont(path, atlas: false) is not { } loaded)
                    continue;
                var missing = loaded.Font.Missing(text);
                if (missing.Length == 0)
                    complete++;
                list.Items.Add(new ListViewItem([Path.GetFileNameWithoutExtension(path), loaded.Font.Face, missing.Length == 0 ? "-" : string.Join(" ", missing.Select(Shown))])
                {
                    ForeColor = missing.Length == 0 ? SystemColors.GrayText : Color.Firebrick,
                });
            }
            using var form = new Form
            {
                Text = $"The preview text in every font: {complete} of {_paths.Count} can draw all of it",
                Size = new Size(900, 600), StartPosition = FormStartPosition.CenterParent, ShowIcon = false, MinimizeBox = false,
            };
            form.Controls.Add(list);
            form.ShowDialog(this);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                foreach (var loaded in _loaded.Values)
                    loaded.Atlas?.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>New alias: the character, the glyph it draws as, and whether every size of the face gets it.</summary>
        private sealed class AliasDialog : Form
        {
            private readonly TextBox _new = new() { Width = 120 };
            private readonly TextBox _as = new() { Width = 120 };
            private readonly CheckBox _every = new() { AutoSize = true, Checked = true };

            public char? NewChar => ParseChar(_new.Text);

            public char? DrawsAs => ParseChar(_as.Text);

            public bool EverySize => _every.Checked;

            public AliasDialog(string face, char? suggested)
            {
                Text = "Add alias";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                ShowIcon = false;
                StartPosition = FormStartPosition.CenterParent;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                _every.Text = $"In every size of {face}";
                if (suggested is { } s)
                    _as.Text = s.ToString();
                var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Padding = new Padding(10) };
                table.Controls.Add(new Label { Text = "New character:", AutoSize = true, Margin = new Padding(3, 6, 8, 3) }, 0, 0);
                table.Controls.Add(_new, 1, 0);
                table.Controls.Add(new Label { Text = "Draws as:", AutoSize = true, Margin = new Padding(3, 6, 8, 3) }, 0, 1);
                table.Controls.Add(_as, 1, 1);
                table.Controls.Add(_every, 1, 2);
                table.Controls.Add(new Label
                {
                    Text = "A character, or its code (U+014A). The new glyph uses the other's image and metrics;\nchange its offsets and advance afterwards if it needs them.",
                    AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 8, 3, 3),
                }, 0, 3);
                table.SetColumnSpan(table.GetControlFromPosition(0, 3)!, 2);
                var ok = new Button { Text = "Add", DialogResult = DialogResult.OK, AutoSize = true };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
                buttons.Controls.AddRange([cancel, ok]);
                table.Controls.Add(buttons, 0, 4);
                table.SetColumnSpan(buttons, 2);
                Controls.Add(table);
                AcceptButton = ok;
                CancelButton = cancel;
                FormClosing += (_, e) =>
                {
                    if (DialogResult == DialogResult.OK && (NewChar == null || DrawsAs == null))
                    {
                        MessageBox.Show(this, "Give one character (or a U+XXXX code) in each box.", Text);
                        e.Cancel = true;
                    }
                };
            }
        }
    }

    /// <summary>
    /// A font's atlas image with its glyph rectangles. Click a glyph to pick it. Mouse wheel zooms around the cursor; drag (or middle
    /// button) pans.
    /// </summary>
    public sealed class FontAtlasView : Control
    {
        private FontBin? _font;
        private Bitmap? _image;
        private List<FontGlyph> _table = [];
        private float _zoom = 1;
        private PointF _pan = new(10, 10);
        private Point _down;
        private PointF _downPan;
        private bool _dragging, _moved;
        private FontGlyph? _selected;
        private readonly TextureBrush _checker = Imaging.Checkerboard();

        public FontAtlasView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(24, 24, 30);
        }

        public event Action<FontGlyph>? GlyphClicked;

        public FontGlyph? Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                Invalidate();
            }
        }

        public void SetFont(FontBin? font, Bitmap? image, List<FontGlyph> table)
        {
            _font = font;
            _image = image;
            _table = table;
            _selected = null;
            Invalidate();
        }

        public void FitToView()
        {
            if (_image == null || Width < 20 || Height < 20)
                return;
            _zoom = Math.Clamp(Math.Min((Width - 20f) / _image.Width, (Height - 20f) / _image.Height), 0.05f, 16f);
            _pan = new PointF((Width - _image.Width * _zoom) / 2, 10);
            Invalidate();
        }

        /// <summary>Zooms in on a glyph (at least 4x) and centres it.</summary>
        public void ShowGlyph(FontGlyph glyph)
        {
            if (_image == null)
                return;
            var r = glyph.PixelRect(_image.Width, _image.Height);
            _zoom = Math.Max(_zoom, 4);
            _pan = new PointF(Width / 2f - (r.X + r.Width / 2f) * _zoom, Height / 2f - (r.Y + r.Height / 2f) * _zoom);
            Invalidate();
        }

        private RectangleF ToScreen((int X, int Y, int Width, int Height) r) =>
            new(_pan.X + r.X * _zoom, _pan.Y + r.Y * _zoom, r.Width * _zoom, r.Height * _zoom);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_image == null)
            {
                TextRenderer.DrawText(g, _font == null ? "No font" : "The atlas image is missing", Font, ClientRectangle, Color.Gray);
                return;
            }
            var bounds = new RectangleF(_pan.X, _pan.Y, _image.Width * _zoom, _image.Height * _zoom);
            g.FillRectangle(_checker, bounds);
            g.InterpolationMode = _zoom >= 1 ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(_image, bounds);
            g.PixelOffsetMode = PixelOffsetMode.Default;
            using var faint = new Pen(Color.FromArgb(90, 80, 160, 255));
            foreach (var glyph in _table)
            {
                if (glyph.IsAlias || glyph.PixelWidth <= 2)
                    continue;
                var r = ToScreen(glyph.PixelRect(_image.Width, _image.Height));
                if (r.Right >= 0 && r.Bottom >= 0 && r.X <= Width && r.Y <= Height)
                    g.DrawRectangle(faint, r.X, r.Y, r.Width, r.Height);
            }
            if (_selected != null)
            {
                if (_selected.OutlineWidth > 0)
                {
                    using var outline = new Pen(Color.Cyan) { DashStyle = DashStyle.Dash };
                    var o = ToScreen(_selected.OutlinePixelRect(_image.Width, _image.Height));
                    g.DrawRectangle(outline, o.X, o.Y, o.Width, o.Height);
                }
                using var pen = new Pen(Color.Gold, 2);
                var r = ToScreen(_selected.PixelRect(_image.Width, _image.Height));
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
            }
            TextRenderer.DrawText(g, $"{_image.Width}x{_image.Height}  {_zoom * 100:0}%", Font, new Point(6, Height - 20), Color.Gray);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            float old = _zoom;
            _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 1.25f : 0.8f), 0.05f, 32f);
            _pan = new PointF(e.X - (e.X - _pan.X) * _zoom / old, e.Y - (e.Y - _pan.Y) * _zoom / old);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _down = e.Location;
            _downPan = _pan;
            _dragging = true;
            _moved = false;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging)
                return;
            if (Math.Abs(e.X - _down.X) + Math.Abs(e.Y - _down.Y) > 3)
                _moved = true;
            if (_moved)
            {
                _pan = new PointF(_downPan.X + e.X - _down.X, _downPan.Y + e.Y - _down.Y);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            _dragging = false;
            if (_moved || e.Button != MouseButtons.Left || _image == null)
                return;
            float x = (e.X - _pan.X) / _zoom, y = (e.Y - _pan.Y) / _zoom;
            // the glyph with its own image under the cursor (aliases share it)
            var hit = _table.Where(g => !g.IsAlias)
                .Select(g => (Glyph: g, Rect: g.PixelRect(_image.Width, _image.Height)))
                .Where(t => x >= t.Rect.X && x < t.Rect.X + t.Rect.Width && y >= t.Rect.Y && y < t.Rect.Y + t.Rect.Height)
                .OrderBy(t => t.Rect.Width * t.Rect.Height)
                .Select(t => t.Glyph)
                .FirstOrDefault();
            if (hit != null)
                GlyphClicked?.Invoke(hit);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _checker.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Text drawn with a game font: per character its glyph image at pen + offset (the outline image first, darkened), then the pen moves on
    /// by the advance. A character the font lacks is drawn as '_' the way the game does it (Font_FindGlyphOrUnderscore), in red.
    /// </summary>
    public sealed class FontPreview : Control
    {
        private FontBin? _font;
        private Bitmap? _image;
        private float _ascent;

        public FontPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(40, 52, 78);
        }

        public string PreviewText { get; set; } = "";

        public int PreviewScale { get; set; } = 2;

        public bool ShowOutline { get; set; } = true;

        public void SetFont(FontBin? font, Bitmap? image)
        {
            _font = font;
            _image = image;
            // the tallest glyph's top above the baseline: where the first baseline goes
            _ascent = font == null ? 0 : Math.Max(1, font.Glyphs.Where(g => !g.IsAlias && g.PixelHeight > 2).Select(g => -g.OffsetY).DefaultIfEmpty(font.RenderSize).Max());
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_font == null || _image == null)
                return;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float scale = PreviewScale;
            float lineHeight = _font.LineHeight > 0 ? _font.LineHeight : _font.RenderSize;
            float pad = 8;
            float baseline = pad + _ascent * scale;
            using var dark = new ImageAttributes();
            dark.SetColorMatrix(new ColorMatrix([[0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 1, 0], [0, 0, 0, 0, 1]]));
            using var red = new ImageAttributes();
            red.SetColorMatrix(new ColorMatrix([[1, 0, 0, 0, 0], [0, 0.2f, 0, 0, 0], [0, 0, 0.2f, 0, 0], [0, 0, 0, 1, 0], [0, 0, 0, 0, 1]]));
            foreach (string line in PreviewText.Replace("\r\n", "\n").Split('\n'))
            {
                float pen = pad;
                foreach (char c in line)
                {
                    var glyph = _font.Find(c);
                    bool missing = glyph == null;
                    glyph ??= _font.Find('_');
                    if (glyph == null)
                        continue;
                    if (glyph.Height != 0)
                    {
                        if (ShowOutline && glyph.OutlineWidth > 0)
                            Draw(g, glyph.OutlinePixelRect(_image.Width, _image.Height), pen + (glyph.OffsetX - glyph.OutlineWidth) * scale,
                                 baseline + (glyph.OffsetY - glyph.OutlineWidth) * scale, scale, dark);
                        Draw(g, glyph.PixelRect(_image.Width, _image.Height), pen + glyph.OffsetX * scale, baseline + glyph.OffsetY * scale, scale,
                             missing ? red : null);
                    }
                    pen += glyph.Advance * scale;
                }
                baseline += lineHeight * scale;
            }
        }

        private void Draw(Graphics g, (int X, int Y, int Width, int Height) src, float x, float y, float scale, ImageAttributes? attributes)
        {
            if (src.Width <= 0 || src.Height <= 0)
                return;
            var dest = Rectangle.Round(new RectangleF(x, y, src.Width * scale, src.Height * scale));
            if (attributes == null)
                g.DrawImage(_image!, dest, new Rectangle(src.X, src.Y, src.Width, src.Height), GraphicsUnit.Pixel);
            else
                g.DrawImage(_image!, dest, src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attributes);
        }
    }
}
