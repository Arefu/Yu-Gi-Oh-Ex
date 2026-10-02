namespace WolfEx.Designer
{
    /// <summary>Pick one sprite of any sprite sheet (.dfymoo) in the game's archive, with thumbnails.</summary>
    internal sealed class SpritePickerDialog : Form
    {
        private readonly GameArt _art;
        private readonly ListBox _sheets = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "Filter sprites" };
        private readonly SpriteGrid _grid = new() { Dock = DockStyle.Fill };
        private readonly Label _info = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft };

        public string? Resource { get; private set; }
        public string? Sprite { get; private set; }

        public SpritePickerDialog(GameArt art, string? resource, string? sprite)
        {
            _art = art;
            Text = "Pick a sprite";
            Size = new Size(1000, 680);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            ShowIcon = false;

            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 260 };
            split.Panel1.Controls.Add(_sheets);
            split.Panel1.Controls.Add(new Label { Text = "Sprite sheets", Dock = DockStyle.Top, Height = 20 });
            split.Panel2.Controls.Add(_grid);
            split.Panel2.Controls.Add(_filter);
            split.Panel2.Controls.Add(_info);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var ok = new Button { Text = "Use sprite", DialogResult = DialogResult.OK, AutoSize = true };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(split);
            Controls.Add(buttons);

            foreach (string sheet in art.AtlasResources)
                _sheets.Items.Add(sheet);
            _sheets.SelectedIndexChanged += (_, _) => Fill();
            _filter.TextChanged += (_, _) => Fill();
            _grid.SelectionChanged += (_, _) => Choose();
            _grid.Activated += (_, _) => { if (Sprite != null) { DialogResult = DialogResult.OK; Close(); } };

            if (resource != null && _sheets.Items.IndexOf(resource) is int index and >= 0)
                _sheets.SelectedIndex = index;
            else if (_sheets.Items.Count > 0)
                _sheets.SelectedIndex = 0;
            if (sprite != null)
                _grid.Select(sprite);
        }

        private void Fill()
        {
            var atlas = _sheets.SelectedItem is string sheet ? _art.Atlas(sheet) : null;
            string filter = _filter.Text.Trim();
            _grid.SetItems(atlas, atlas?.Sprites.Where(s => filter.Length == 0 || s.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList() ?? []);
            Choose();
        }

        private void Choose()
        {
            if (_grid.SelectedSprite is { } info && _sheets.SelectedItem is string sheet)
            {
                Resource = sheet;
                Sprite = info.Name;
                _info.Text = $"{sheet}  {info.Name}   {info.Full.Width} x {info.Full.Height}";
            }
            else
            {
                Resource = null;
                Sprite = null;
                _info.Text = "";
            }
        }
    }

    /// <summary>
    /// A scrolling grid of sprite thumbnails that only draws the rows on screen, straight from the sheet (no per-sprite bitmaps or
    /// ImageList). A 738-sprite sheet like STEAM_icons opens instantly.
    /// </summary>
    internal sealed class SpriteGrid : ScrollableControl
    {
        private const int Cell = 112, Thumb = 96;
        private Atlas? _atlas;
        private List<SpriteInfo> _items = [];
        private int _selected = -1;

        public SpriteGrid()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
            BackColor = Color.FromArgb(36, 38, 48);
        }

        public event EventHandler? SelectionChanged;
        public event EventHandler? Activated;

        public SpriteInfo? SelectedSprite => _selected >= 0 && _selected < _items.Count ? _items[_selected] : null;

        private int Columns => Math.Max(1, ClientSize.Width / Cell);

        public void SetItems(Atlas? atlas, List<SpriteInfo> items)
        {
            _atlas = atlas;
            _items = items;
            _selected = items.Count > 0 ? 0 : -1;
            AutoScrollPosition = Point.Empty;
            UpdateScroll();
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void Select(string name)
        {
            int index = _items.FindIndex(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return;
            _selected = index;
            int row = index / Columns;
            AutoScrollPosition = new Point(0, Math.Max(0, row * (Cell + 18) - ClientSize.Height / 2));
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateScroll()
        {
            int rows = (_items.Count + Columns - 1) / Columns;
            AutoScrollMinSize = new Size(0, rows * (Cell + 18));
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScroll();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_atlas == null)
                return;
            int columns = Columns, rowHeight = Cell + 18;
            int scroll = -AutoScrollPosition.Y;
            int firstRow = scroll / rowHeight, lastRow = (scroll + ClientSize.Height) / rowHeight;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            using var cellBrush = new SolidBrush(Color.FromArgb(52, 55, 70));
            using var selectedPen = new Pen(Color.DeepSkyBlue, 2);
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int index = row * columns + column;
                    if (index >= _items.Count)
                        return;
                    var info = _items[index];
                    var cell = new Rectangle(column * Cell + 4, row * rowHeight - scroll + 4, Cell - 8, Cell - 8);
                    g.FillRectangle(cellBrush, cell);
                    float scale = Math.Min(1f, Math.Min((float)Thumb / info.Full.Width, (float)Thumb / info.Full.Height));
                    // draw the piece where it sits in the untrimmed picture, scaled into the cell
                    float w = info.Full.Width * scale, h = info.Full.Height * scale;
                    float ox = cell.X + (cell.Width - w) / 2, oy = cell.Y + (cell.Height - h) / 2;
                    var dest = new RectangleF(ox + info.Offset.X * scale, oy + info.Offset.Y * scale, info.Source.Width * scale, info.Source.Height * scale);
                    g.DrawImage(_atlas.Sheet, dest, info.Source, GraphicsUnit.Pixel);
                    if (index == _selected)
                        g.DrawRectangle(selectedPen, cell);
                    TextRenderer.DrawText(g, info.Name, Font, new Rectangle(column * Cell, cell.Bottom + 2, Cell, 16), Color.Gainsboro,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.EndEllipsis);
                }
            }
        }

        private int IndexAt(Point p)
        {
            int column = p.X / Cell, row = (p.Y - AutoScrollPosition.Y) / (Cell + 18);
            if (column >= Columns)
                return -1;
            int index = row * Columns + column;
            return index < _items.Count ? index : -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            int index = IndexAt(e.Location);
            if (index < 0)
                return;
            _selected = index;
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            if (IndexAt(e.Location) >= 0)
                Activated?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            Invalidate();
        }

        protected override void OnScroll(ScrollEventArgs se)
        {
            base.OnScroll(se);
            Invalidate();
        }
    }
}
