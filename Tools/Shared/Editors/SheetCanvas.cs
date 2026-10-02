using System.IO;
using Types;
using System.Drawing.Drawing2D;

namespace Wolf.Editors
{
    /// <summary>
    /// A sprite sheet with its rectangles on top. Click a rectangle to select it, drag to move, drag a handle to resize, drag on empty space
    /// to draw a new sprite. Everything lands on whole pixels, and snaps to the grid and to other sprites' edges (Alt: no snapping).
    /// Mouse wheel zooms around the cursor; middle button (or Space + drag) pans.
    /// </summary>
    public sealed class SheetCanvas : Control
    {
        private DfymooSheet _sheet = new();
        private Bitmap? _image;
        private byte[]? _alpha;
        private float _zoom = 1;
        private PointF _pan = new(20, 20);
        private readonly TextureBrush _checker = Imaging.Checkerboard();

        private DfymooSprite? _selected;
        private enum Mode { None, Move, Resize, Draw, Pan }
        private Mode _mode;
        private Point _downMouse;
        private PointF _downPan;
        private Rectangle _startRect;
        private int _handle;
        private Point _drawStart;
        private Rectangle _drawRect;
        private bool _changed;
        private string? _snapshot;
        private bool _space;

        public SheetCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Color.FromArgb(24, 24, 30);
        }

        public int GridSize { get; set; } = 1;
        public bool SnapToEdges { get; set; } = true;
        public bool ShowNames { get; set; } = true;

        public DfymooSheet Sheet => _sheet;
        public Bitmap? Image => _image;

        public DfymooSprite? Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler? SelectionChanged;

        /// <summary>The sheet changed. The argument is the sheet as it was before (for undo).</summary>
        public event Action<string>? Changed;

        public event Action<Point, Color>? CursorMoved;

        public float Zoom
        {
            get => _zoom;
            set { ZoomAround(new PointF(Width / 2f, Height / 2f), value); }
        }

        public void SetSheet(DfymooSheet sheet, Bitmap? image)
        {
            _sheet = sheet;
            _image = image;
            _alpha = null;
            _selected = null;
            FitToView();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Swaps in another copy of the same sheet (undo), keeping the view.</summary>
        public void ReplaceSheet(DfymooSheet sheet)
        {
            string? selectedName = _selected?.Name;
            _sheet = sheet;
            _selected = sheet.Sprites.FirstOrDefault(s => s.Name == selectedName);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void FitToView()
        {
            if (_image == null || Width < 20 || Height < 20)
            {
                Invalidate();
                return;
            }
            _zoom = Math.Min((Width - 40f) / _image.Width, (Height - 40f) / _image.Height);
            _zoom = Math.Clamp(_zoom, 0.05f, 16f);
            _pan = new PointF((Width - _image.Width * _zoom) / 2, (Height - _image.Height * _zoom) / 2);
            Invalidate();
        }

        public void ShowSprite(DfymooSprite sprite)
        {
            _pan = new PointF(Width / 2f - (sprite.X + sprite.Width / 2f) * _zoom, Height / 2f - (sprite.Y + sprite.Height / 2f) * _zoom);
            Invalidate();
        }

        private void ZoomAround(PointF screen, float zoom)
        {
            zoom = Math.Clamp(zoom, 0.05f, 32f);
            var sheetPoint = ToSheetF(screen);
            _zoom = zoom;
            _pan = new PointF(screen.X - sheetPoint.X * _zoom, screen.Y - sheetPoint.Y * _zoom);
            Invalidate();
        }

        private PointF ToSheetF(PointF screen) => new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

        private Point ToSheet(Point screen)
        {
            var p = ToSheetF(screen);
            return new Point((int)Math.Floor(p.X), (int)Math.Floor(p.Y));
        }

        private RectangleF ToScreen(Rectangle r) => new(_pan.X + r.X * _zoom, _pan.Y + r.Y * _zoom, r.Width * _zoom, r.Height * _zoom);

        // ---- editing helpers ----

        private void Begin() => _snapshot = _sheet.ToText();

        private void Commit()
        {
            if (_snapshot != null && _snapshot != _sheet.ToText())
                Changed?.Invoke(_snapshot);
            _snapshot = null;
        }

        /// <summary>Shrinks the selected sprite to the pixels that are not transparent inside it.</summary>
        public void FitSelectedToPixels()
        {
            if (_selected == null || _image == null)
                return;
            _alpha ??= Imaging.AlphaMap(_image);
            var r = Rectangle.Intersect(_selected.Bounds, new Rectangle(0, 0, _image.Width, _image.Height));
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = r.Top; y < r.Bottom; y++)
                for (int x = r.Left; x < r.Right; x++)
                    if (_alpha[y * _image.Width + x] > 8)
                    {
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
            if (maxX < 0)
                return;
            Begin();
            _selected.Bounds = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
            Invalidate();
            Commit();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Adds a sprite for every island of visible pixels that no sprite covers yet (islands closer than <paramref name="gap"/> pixels join).
        /// Returns how many were added.
        /// </summary>
        public int FindSprites(int gap = 2)
        {
            if (_image == null)
                return 0;
            _alpha ??= Imaging.AlphaMap(_image);
            int w = _image.Width, h = _image.Height;
            var covered = new bool[w * h];
            foreach (var sprite in _sheet.Sprites)
            {
                var r = Rectangle.Intersect(sprite.Bounds, new Rectangle(0, 0, w, h));
                for (int y = r.Top; y < r.Bottom; y++)
                    Array.Fill(covered, true, y * w + r.Left, r.Width);
            }

            var seen = new bool[w * h];
            var found = new List<Rectangle>();
            var stack = new Stack<int>();
            for (int start = 0; start < w * h; start++)
            {
                if (seen[start] || covered[start] || _alpha[start] <= 8)
                    continue;
                int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
                stack.Push(start);
                seen[start] = true;
                while (stack.Count > 0)
                {
                    int i = stack.Pop();
                    int x = i % w, y = i / w;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                    for (int dy = -gap; dy <= gap; dy++)
                    {
                        int ny = y + dy;
                        if (ny < 0 || ny >= h)
                            continue;
                        for (int dx = -gap; dx <= gap; dx++)
                        {
                            int nx = x + dx;
                            if (nx < 0 || nx >= w)
                                continue;
                            int n = ny * w + nx;
                            if (!seen[n] && !covered[n] && _alpha[n] > 8)
                            {
                                seen[n] = true;
                                stack.Push(n);
                            }
                        }
                    }
                }
                var rect = Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
                if (rect.Width * rect.Height >= 16)
                    found.Add(rect);
            }

            if (found.Count == 0)
                return 0;
            Begin();
            foreach (var rect in found.OrderBy(r => r.Y / 16).ThenBy(r => r.X))
                _sheet.Sprites.Add(new DfymooSprite { Name = _sheet.NewName("sprite_"), Bounds = rect });
            Invalidate();
            Commit();
            return found.Count;
        }

        public void AddSprite(Rectangle rect)
        {
            Begin();
            var sprite = new DfymooSprite { Name = _sheet.NewName("sprite_"), Bounds = rect };
            _sheet.Sprites.Add(sprite);
            Commit();
            Selected = sprite;
        }

        public void DeleteSelected()
        {
            if (_selected == null)
                return;
            Begin();
            _sheet.Sprites.Remove(_selected);
            Commit();
            Selected = null;
        }

        public void DuplicateSelected()
        {
            if (_selected == null)
                return;
            Begin();
            var copy = new DfymooSprite
            {
                Name = _sheet.NewName(_selected.Name + "_copy"),
                Bounds = new Rectangle(_selected.X + 8, _selected.Y + 8, _selected.Width, _selected.Height),
                Trimmed = _selected.Trimmed, OffsetX = _selected.OffsetX, OffsetY = _selected.OffsetY,
                FullWidth = _selected.FullWidth, FullHeight = _selected.FullHeight,
            };
            _sheet.Sprites.Add(copy);
            Commit();
            Selected = copy;
        }

        // ---- painting ----

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_image == null)
            {
                TextRenderer.DrawText(g, "Open a sheet, or start a new one from a PNG.", Font, ClientRectangle, Color.Gray,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            var sheetRect = ToScreen(new Rectangle(0, 0, _image.Width, _image.Height));
            _checker.ResetTransform();
            _checker.TranslateTransform(_pan.X, _pan.Y);
            g.FillRectangle(_checker, sheetRect);
            g.InterpolationMode = _zoom >= 2 ? InterpolationMode.NearestNeighbor : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // only the visible part of the sheet is drawn, which is what keeps big sheets quick
            var visible = RectangleF.Intersect(sheetRect, ClientRectangle);
            if (!visible.IsEmpty)
            {
                var source = RectangleF.FromLTRB((visible.Left - _pan.X) / _zoom, (visible.Top - _pan.Y) / _zoom,
                                                 (visible.Right - _pan.X) / _zoom, (visible.Bottom - _pan.Y) / _zoom);
                g.DrawImage(_image, visible, source, GraphicsUnit.Pixel);
            }
            g.PixelOffsetMode = PixelOffsetMode.Default;

            if (GridSize > 1 && GridSize * _zoom >= 6)
            {
                using var grid = new Pen(Color.FromArgb(40, 255, 255, 255));
                for (int x = 0; x <= _image.Width; x += GridSize)
                {
                    float sx = _pan.X + x * _zoom;
                    if (sx >= 0 && sx <= Width) g.DrawLine(grid, sx, Math.Max(0, sheetRect.Top), sx, Math.Min(Height, sheetRect.Bottom));
                }
                for (int y = 0; y <= _image.Height; y += GridSize)
                {
                    float sy = _pan.Y + y * _zoom;
                    if (sy >= 0 && sy <= Height) g.DrawLine(grid, Math.Max(0, sheetRect.Left), sy, Math.Min(Width, sheetRect.Right), sy);
                }
            }

            using var outline = new Pen(Color.FromArgb(170, 80, 200, 255));
            using var label = new SolidBrush(Color.FromArgb(200, 0, 0, 0));
            var font = Font;
            foreach (var sprite in _sheet.Sprites)
            {
                var r = ToScreen(sprite.Bounds);
                if (!r.IntersectsWith(ClientRectangle))
                    continue;
                g.DrawRectangle(outline, r.X, r.Y, r.Width, r.Height);
                if (ShowNames && r.Width > 40 && sprite != _selected)
                {
                    var size = g.MeasureString(sprite.Name, font);
                    if (size.Width < r.Width)
                    {
                        g.FillRectangle(label, r.X, r.Y, size.Width, size.Height);
                        g.DrawString(sprite.Name, font, Brushes.White, r.X, r.Y);
                    }
                }
            }

            RectangleF? highlight = _mode == Mode.Draw ? ToScreen(_drawRect) : _selected != null ? ToScreen(_selected.Bounds) : null;
            if (highlight is { } h)
            {
                using var pen = new Pen(Color.HotPink, 2);
                g.DrawRectangle(pen, h.X, h.Y, h.Width, h.Height);
                if (_selected != null && _mode != Mode.Draw)
                {
                    string text = $"{_selected.Name}  {_selected.X}, {_selected.Y}  {_selected.Width} x {_selected.Height}";
                    var size = g.MeasureString(text, font);
                    float ty = h.Y - size.Height - 2 < 0 ? h.Bottom + 2 : h.Y - size.Height - 2;
                    g.FillRectangle(Brushes.HotPink, h.X, ty, size.Width, size.Height);
                    g.DrawString(text, font, Brushes.Black, h.X, ty);
                    foreach (var handle in Handles(h))
                    {
                        g.FillRectangle(Brushes.White, handle);
                        g.DrawRectangle(Pens.HotPink, handle.X, handle.Y, handle.Width, handle.Height);
                    }
                }
            }
        }

        private static RectangleF[] Handles(RectangleF r)
        {
            const float s = 8;
            float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
            PointF[] points =
            [
                new(r.Left, r.Top), new(cx, r.Top), new(r.Right, r.Top), new(r.Right, cy),
                new(r.Right, r.Bottom), new(cx, r.Bottom), new(r.Left, r.Bottom), new(r.Left, cy),
            ];
            return points.Select(p => new RectangleF(p.X - s / 2, p.Y - s / 2, s, s)).ToArray();
        }

        // ---- mouse ----

        private DfymooSprite? HitTest(Point sheet)
        {
            // smallest first, so a sprite inside a bigger one can still be picked
            return _sheet.Sprites.Where(s => s.Bounds.Contains(sheet)).OrderBy(s => s.Width * s.Height).FirstOrDefault();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            _downMouse = e.Location;
            _changed = false;
            if (e.Button == MouseButtons.Middle || e.Button == MouseButtons.Left && _space)
            {
                _mode = Mode.Pan;
                _downPan = _pan;
                return;
            }
            if (e.Button != MouseButtons.Left || _image == null)
                return;

            if (_selected != null)
            {
                int handle = Array.FindIndex(Handles(ToScreen(_selected.Bounds)), h => RectangleF.Inflate(h, 3, 3).Contains(e.Location));
                if (handle >= 0)
                {
                    _mode = Mode.Resize;
                    _handle = handle;
                    _startRect = _selected.Bounds;
                    Begin();
                    Capture = true;
                    return;
                }
            }

            var at = ToSheet(e.Location);
            var hit = (ModifierKeys & Keys.Control) != 0 ? null : HitTest(at);
            if (hit != null)
            {
                Selected = hit;
                _mode = Mode.Move;
                _startRect = hit.Bounds;
                Begin();
            }
            else
            {
                _mode = Mode.Draw;
                _drawStart = Snap(at, null);
                _drawRect = new Rectangle(_drawStart, Size.Empty);
            }
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var at = ToSheet(e.Location);
            if (_image != null && at.X >= 0 && at.Y >= 0 && at.X < _image.Width && at.Y < _image.Height)
                CursorMoved?.Invoke(at, _image.GetPixel(at.X, at.Y));

            switch (_mode)
            {
                case Mode.Pan:
                    _pan = new PointF(_downPan.X + e.X - _downMouse.X, _downPan.Y + e.Y - _downMouse.Y);
                    Invalidate();
                    break;
                case Mode.Move when _selected != null:
                {
                    var down = ToSheet(_downMouse);
                    var moved = new Rectangle(_startRect.X + at.X - down.X, _startRect.Y + at.Y - down.Y, _startRect.Width, _startRect.Height);
                    var snapped = Snap(moved.Location, _selected);
                    var snappedFar = Snap(new Point(moved.Right, moved.Bottom), _selected);
                    // snap whichever edge is closer to something
                    int x = Math.Abs(snapped.X - moved.X) <= Math.Abs(snappedFar.X - moved.Right) ? snapped.X : snappedFar.X - moved.Width;
                    int y = Math.Abs(snapped.Y - moved.Y) <= Math.Abs(snappedFar.Y - moved.Bottom) ? snapped.Y : snappedFar.Y - moved.Height;
                    _selected.Bounds = new Rectangle(x, y, moved.Width, moved.Height);
                    _changed = true;
                    Invalidate();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    break;
                }
                case Mode.Resize when _selected != null:
                {
                    var p = Snap(at, _selected);
                    int l = _startRect.Left, t = _startRect.Top, r = _startRect.Right, b = _startRect.Bottom;
                    if (_handle is 0 or 6 or 7) l = Math.Min(p.X, r - 1);
                    if (_handle is 2 or 3 or 4) r = Math.Max(p.X, l + 1);
                    if (_handle is 0 or 1 or 2) t = Math.Min(p.Y, b - 1);
                    if (_handle is 4 or 5 or 6) b = Math.Max(p.Y, t + 1);
                    _selected.Bounds = Rectangle.FromLTRB(l, t, r, b);
                    _changed = true;
                    Invalidate();
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                    break;
                }
                case Mode.Draw:
                {
                    var p = Snap(at, null);
                    _drawRect = Rectangle.FromLTRB(Math.Min(p.X, _drawStart.X), Math.Min(p.Y, _drawStart.Y), Math.Max(p.X, _drawStart.X), Math.Max(p.Y, _drawStart.Y));
                    Invalidate();
                    break;
                }
                default:
                    Cursor = CursorAt(e.Location);
                    break;
            }
        }

        private Cursor CursorAt(Point location)
        {
            if (_space)
                return Cursors.Hand;
            if (_selected != null)
            {
                int handle = Array.FindIndex(Handles(ToScreen(_selected.Bounds)), h => RectangleF.Inflate(h, 3, 3).Contains(location));
                if (handle >= 0)
                    return (handle % 4) switch { 0 => Cursors.SizeNWSE, 1 => Cursors.SizeNS, 2 => Cursors.SizeNESW, _ => Cursors.SizeWE };
            }
            return HitTest(ToSheet(location)) != null ? Cursors.SizeAll : Cursors.Cross;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Capture = false;
            if (_mode == Mode.Draw)
            {
                if (_drawRect.Width >= 2 && _drawRect.Height >= 2)
                    AddSprite(_drawRect);
                else
                    Selected = null;
            }
            else if ((_mode == Mode.Move || _mode == Mode.Resize) && _changed)
                Commit();
            else
                _snapshot = null;
            _mode = Mode.None;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ZoomAround(e.Location, _zoom * (e.Delta > 0 ? 1.25f : 0.8f));
        }

        /// <summary>Whole pixels; then the grid; then (within 6 screen pixels) another sprite's edge or the sheet's edge.</summary>
        private Point Snap(Point p, DfymooSprite? self)
        {
            if ((ModifierKeys & Keys.Alt) != 0)
                return p;
            int x = p.X, y = p.Y;
            if (GridSize > 1)
            {
                x = (int)Math.Round(x / (double)GridSize) * GridSize;
                y = (int)Math.Round(y / (double)GridSize) * GridSize;
            }
            if (SnapToEdges)
            {
                int threshold = Math.Max(1, (int)(6 / _zoom));
                var xs = new List<int> { 0 };
                var ys = new List<int> { 0 };
                if (_image != null) { xs.Add(_image.Width); ys.Add(_image.Height); }
                foreach (var other in _sheet.Sprites)
                {
                    if (other == self) continue;
                    xs.Add(other.X); xs.Add(other.X + other.Width);
                    ys.Add(other.Y); ys.Add(other.Y + other.Height);
                }
                int bx = xs.OrderBy(v => Math.Abs(v - p.X)).First(), by = ys.OrderBy(v => Math.Abs(v - p.Y)).First();
                if (Math.Abs(bx - p.X) <= threshold) x = bx;
                if (Math.Abs(by - p.Y) <= threshold) y = by;
            }
            return new Point(x, y);
        }

        // ---- keyboard ----

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                _space = true;
                Cursor = Cursors.Hand;
                e.Handled = true;
                return;
            }
            if (_selected != null && e.KeyCode is Keys.Left or Keys.Right or Keys.Up or Keys.Down)
            {
                int step = e.Shift ? Math.Max(8, GridSize) : 1;
                int dx = e.KeyCode == Keys.Left ? -step : e.KeyCode == Keys.Right ? step : 0;
                int dy = e.KeyCode == Keys.Up ? -step : e.KeyCode == Keys.Down ? step : 0;
                Begin();
                if (e.Control)   // Ctrl + arrows: resize from the right / bottom
                    _selected.Bounds = new Rectangle(_selected.X, _selected.Y, Math.Max(1, _selected.Width + dx), Math.Max(1, _selected.Height + dy));
                else
                    _selected.Bounds = new Rectangle(_selected.X + dx, _selected.Y + dy, _selected.Width, _selected.Height);
                Commit();
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                return;
            }
            if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Escape) { Selected = null; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                _space = false;
                Cursor = Cursors.Default;
            }
            base.OnKeyUp(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _checker.Dispose();
            base.Dispose(disposing);
        }
    }
}
