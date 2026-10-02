using System.Drawing.Drawing2D;

namespace WolfEx.Designer
{
    internal enum AlignMode { Left, CentreX, Right, Top, CentreY, Bottom }

    /// <summary>
    /// The page drawn at the game's 1920 x 1080, scaled by <see cref="Zoom"/>. Click to select (Ctrl toggles, drag on empty space for a box),
    /// drag to move, drag a handle to resize. Moves and resizes snap to the grid and to guides (the edges and centres of the other elements
    /// and of the screen); hold Alt to place freely. Arrows nudge 1 px (Shift: one grid step).
    /// </summary>
    internal sealed class DesignCanvas : Control
    {
        public const string DragFormat = "WolfEx.WidgetKind";

        private PageDocument _document = new();
        private GameArt _art = GameArt.None;
        private float _zoom = 0.5f;

        private readonly List<PageElement> _selection = [];
        private readonly Stack<string> _undo = new();
        private readonly Stack<string> _redo = new();
        private static string? _clipboard;

        private enum Mode { None, Move, Resize, Box }
        private Mode _mode;
        private PointF _downGame;
        private Dictionary<PageElement, RectangleF> _startBounds = [];
        private int _handle = -1;
        private string? _snapshot;
        private bool _changedDuringDrag;
        private RectangleF _box;
        private readonly List<(bool Vertical, float At)> _guides = [];

        public DesignCanvas()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AllowDrop = true;
            TabStop = true;
            UpdateSize();
        }

        // ---- settings ----

        public int GridSize { get; set; } = 20;
        public bool SnapToGrid { get; set; } = true;
        public bool SnapToGuides { get; set; } = true;
        public bool ShowGrid { get; set; } = true;
        public bool ShowSafeArea { get; set; }

        public float Zoom
        {
            get => _zoom;
            set
            {
                _zoom = Math.Clamp(value, 0.1f, 4f);
                UpdateSize();
                Invalidate();
            }
        }

        public GameArt Art
        {
            get => _art;
            set { _art = value; Invalidate(); }
        }

        public PageDocument Document
        {
            get => _document;
            set
            {
                _document = value;
                _selection.Clear();
                _undo.Clear();
                _redo.Clear();
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public IReadOnlyList<PageElement> Selection => _selection;

        /// <summary>The selection changed.</summary>
        public event EventHandler? SelectionChanged;

        /// <summary>Something on the page changed (moved, added, undone...).</summary>
        public event EventHandler? DocumentChanged;

        /// <summary>The mouse is over this point of the page (game pixels).</summary>
        public event Action<PointF>? CursorMoved;

        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;

        private void UpdateSize() => Size = new Size((int)Math.Ceiling(PageDocument.ScreenWidth * _zoom), (int)Math.Ceiling(PageDocument.ScreenHeight * _zoom));

        // ---- undo ----

        /// <summary>Call before changing the document from outside (property grid...); the change can then be undone.</summary>
        public void BeginChange()
        {
            _undo.Push(_document.ToJson());
            _redo.Clear();
        }

        /// <summary>Call after an outside change.</summary>
        public void EndChange()
        {
            WidgetCatalog.Normalise(_document);
            Invalidate();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// An outside change already happened (the property grid changes values straight away): <paramref name="before"/> is the document as
        /// it was, taken with <see cref="PageDocument.ToJson"/> before the change.
        /// </summary>
        public void CommitChange(string before)
        {
            if (before == _document.ToJson())
                return;
            _undo.Push(before);
            _redo.Clear();
            EndChange();
        }

        public void Undo() => Swap(_undo, _redo);

        public void Redo() => Swap(_redo, _undo);

        private void Swap(Stack<string> from, Stack<string> to)
        {
            if (from.Count == 0)
                return;
            var selected = _selection.Select(e => e.Id).ToHashSet();
            to.Push(_document.ToJson());
            _document = PageDocument.FromJson(from.Pop());
            _selection.Clear();
            _selection.AddRange(_document.Elements.Where(e => selected.Contains(e.Id)));
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        // ---- editing (also used by the panel's toolbar) ----

        public PageElement Add(WidgetKind kind, PointF centre)
        {
            BeginChange();
            var element = new PageElement
            {
                Id = _document.NewId(kind.Id),
                Kind = kind.Id,
                Width = kind.Size.Width,
                Height = kind.Size.Height,
                Z = _document.Elements.Count == 0 ? 0 : _document.Elements.Max(e => e.Z) + 1,
                Text = kind.DefaultText,
            };
            if (kind.HasButtons)
            {
                element.Buttons = [new PageButton { Label = "Button 1" }, new PageButton { Label = "Button 2" }];
                element.Height = element.Buttons.Count * WidgetCatalog.ButtonSpacing;
            }
            if (kind.HasSprite)
            {
                element.Resource = "pdui/doShared";
                element.Sprite = "optionbox";
            }
            element.X = SnapValue(centre.X - element.Width / 2);
            element.Y = SnapValue(centre.Y - element.Height / 2);
            _document.Elements.Add(element);
            Select(element);
            EndChange();
            return element;
        }

        public void Select(PageElement? element, bool add = false)
        {
            if (!add)
                _selection.Clear();
            if (element != null && !_selection.Contains(element))
                _selection.Add(element);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void SelectAll()
        {
            _selection.Clear();
            _selection.AddRange(_document.Elements);
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        public void DeleteSelection()
        {
            if (_selection.Count == 0)
                return;
            BeginChange();
            foreach (var element in _selection)
                _document.Elements.Remove(element);
            _selection.Clear();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            EndChange();
        }

        public void Copy()
        {
            if (_selection.Count > 0)
                _clipboard = new PageDocument { Elements = [.. _selection] }.ToJson();
        }

        public void Paste(float offset = 20)
        {
            if (_clipboard == null)
                return;
            var copied = PageDocument.FromJson(_clipboard).Elements;
            if (copied.Count == 0)
                return;
            BeginChange();
            _selection.Clear();
            int z = _document.Elements.Count == 0 ? 0 : _document.Elements.Max(e => e.Z) + 1;
            foreach (var element in copied)
            {
                element.Id = _document.NewId(element.Kind);
                element.X += offset;
                element.Y += offset;
                element.Z = z++;
                _document.Elements.Add(element);
                _selection.Add(element);
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            EndChange();
        }

        public void Duplicate()
        {
            Copy();
            Paste();
        }

        public void Nudge(float dx, float dy)
        {
            var movable = _selection.Where(e => e.Locked != true).ToList();
            if (movable.Count == 0)
                return;
            BeginChange();
            foreach (var element in movable)
            {
                element.X += dx;
                element.Y += dy;
            }
            EndChange();
        }

        public void Align(AlignMode mode)
        {
            if (_selection.Count == 0)
                return;
            // one element aligns to the screen, several to the box around them
            RectangleF area = _selection.Count == 1 ? new RectangleF(0, 0, PageDocument.ScreenWidth, PageDocument.ScreenHeight) : Union(_selection);
            BeginChange();
            foreach (var e in _selection.Where(e => e.Locked != true))
            {
                switch (mode)
                {
                    case AlignMode.Left: e.X = area.Left; break;
                    case AlignMode.CentreX: e.X = area.Left + (area.Width - e.Width) / 2; break;
                    case AlignMode.Right: e.X = area.Right - e.Width; break;
                    case AlignMode.Top: e.Y = area.Top; break;
                    case AlignMode.CentreY: e.Y = area.Top + (area.Height - e.Height) / 2; break;
                    case AlignMode.Bottom: e.Y = area.Bottom - e.Height; break;
                }
            }
            EndChange();
        }

        /// <summary>Equal gaps between three or more selected elements.</summary>
        public void Distribute(bool horizontal)
        {
            var items = _selection.Where(e => e.Locked != true).OrderBy(e => horizontal ? e.X : e.Y).ToList();
            if (items.Count < 3)
                return;
            float start = horizontal ? items[0].X : items[0].Y;
            float end = horizontal ? items[^1].X + items[^1].Width : items[^1].Y + items[^1].Height;
            float total = items.Sum(e => horizontal ? e.Width : e.Height);
            float gap = (end - start - total) / (items.Count - 1);
            BeginChange();
            float at = start;
            foreach (var e in items)
            {
                if (horizontal) { e.X = at; at += e.Width + gap; }
                else { e.Y = at; at += e.Height + gap; }
            }
            EndChange();
        }

        public void MatchSize(bool width, bool height)
        {
            if (_selection.Count < 2)
                return;
            var first = _selection[0];
            BeginChange();
            foreach (var e in _selection.Skip(1).Where(e => e.Locked != true))
            {
                if (width) e.Width = first.Width;
                if (height) e.Height = first.Height;
            }
            EndChange();
        }

        public void ChangeOrder(bool toFront)
        {
            if (_selection.Count == 0 || _document.Elements.Count == 0)
                return;
            BeginChange();
            int edge = toFront ? _document.Elements.Max(e => e.Z) : _document.Elements.Min(e => e.Z);
            foreach (var e in _selection)
                e.Z = toFront ? ++edge : --edge;
            EndChange();
        }

        public void ResetSize()
        {
            BeginChange();
            foreach (var e in _selection.Where(e => e.Locked != true))
            {
                if (WidgetCatalog.Find(e.Kind) is not { } kind)
                    continue;
                SizeF size = kind.Size;
                if (kind.HasSprite && e.Resource != null && e.Sprite != null)
                    size = _art.SpriteSize(e.Resource, e.Sprite, Size.Round(size));
                e.Width = size.Width;
                e.Height = size.Height;
            }
            EndChange();
        }

        private static RectangleF Union(IEnumerable<PageElement> elements) =>
            elements.Select(e => e.Bounds).Aggregate(RectangleF.Union);

        // ---- painting ----

        protected override void OnPaint(PaintEventArgs pe) => PaintPage(pe.Graphics, overlay: true);

        /// <summary>Draws the page at the current zoom (with the grid, guides and selection when <paramref name="overlay"/> is set).</summary>
        public void PaintPage(Graphics g, bool overlay)
        {
            g.Clear(Color.FromArgb(18, 20, 28));
            g.InterpolationMode = _zoom < 1 ? InterpolationMode.HighQualityBilinear : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var state = g.Save();
            g.ScaleTransform(_zoom, _zoom);
            DrawBackground(g);
            foreach (var element in _document.Elements.OrderBy(e => e.Z))
            {
                var kind = WidgetCatalog.Find(element.Kind);
                var clip = g.Save();
                try
                {
                    if (kind != null)
                        kind.Paint(g, element, _art);
                    else
                        WidgetCatalog.Box(g, element.Bounds, "unknown: " + element.Kind);
                }
                catch (Exception ex)
                {
                    WidgetCatalog.Box(g, element.Bounds, ex.Message);
                }
                g.Restore(clip);
            }
            g.Restore(state);

            if (!overlay)
                return;
            DrawGrid(g);
            DrawOverlay(g);
        }

        /// <summary>The page as a picture (no grid or selection).</summary>
        public Bitmap Render()
        {
            var bitmap = new Bitmap(Width, Height);
            using var g = Graphics.FromImage(bitmap);
            PaintPage(g, overlay: false);
            return bitmap;
        }

        private void DrawBackground(Graphics g)
        {
            var screen = new RectangleF(0, 0, PageDocument.ScreenWidth, PageDocument.ScreenHeight);
            string background = _document.Background ?? "";
            int hash = background.IndexOf('#');
            bool drawn = hash > 0
                ? g.DrawSprite(_art, background[..hash], background[(hash + 1)..], screen)
                : _art.Image(background) is { } image && Draw(g, image, screen);
            if (!drawn)
            {
                using var brush = new LinearGradientBrush(screen, Color.FromArgb(10, 30, 60), Color.FromArgb(5, 10, 25), LinearGradientMode.Vertical);
                g.FillRectangle(brush, screen);
            }
        }

        private static bool Draw(Graphics g, Image image, RectangleF rect)
        {
            g.DrawImage(image, rect);
            return true;
        }

        private void DrawGrid(Graphics g)
        {
            if (!ShowGrid || GridSize <= 0)
                return;
            float step = GridSize * _zoom;
            int every = 1;
            while (step * every < 6)
                every *= 2;
            using var minor = new Pen(Color.FromArgb(28, 255, 255, 255));
            using var major = new Pen(Color.FromArgb(60, 255, 255, 255));
            int count = 0;
            for (float x = 0; x <= Width; x += step * every, count++)
                g.DrawLine(count % 5 == 0 ? major : minor, x, 0, x, Height);
            count = 0;
            for (float y = 0; y <= Height; y += step * every, count++)
                g.DrawLine(count % 5 == 0 ? major : minor, 0, y, Width, y);
        }

        private void DrawOverlay(Graphics g)
        {
            // screen centre lines
            using (var centre = new Pen(Color.FromArgb(70, 255, 120, 200)) { DashStyle = DashStyle.Dash })
            {
                g.DrawLine(centre, Width / 2f, 0, Width / 2f, Height);
                g.DrawLine(centre, 0, Height / 2f, Width, Height / 2f);
            }
            if (ShowSafeArea)
            {
                using var safe = new Pen(Color.FromArgb(140, 255, 200, 0)) { DashStyle = DashStyle.Dot };
                g.DrawRectangle(safe, Width * 0.05f, Height * 0.05f, Width * 0.9f, Height * 0.9f);
            }

            foreach (var (vertical, at) in _guides)
            {
                using var guide = new Pen(Color.FromArgb(230, 255, 60, 160));
                float p = at * _zoom;
                if (vertical) g.DrawLine(guide, p, 0, p, Height);
                else g.DrawLine(guide, 0, p, Width, p);
            }

            foreach (var element in _selection)
            {
                var r = ToScreen(element.Bounds);
                using var pen = new Pen(element.Locked == true ? Color.OrangeRed : Color.DeepSkyBlue, 2);
                g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                if (_selection.Count == 1 && element.Locked != true)
                {
                    foreach (var handle in Handles(r))
                    {
                        g.FillRectangle(Brushes.White, handle);
                        g.DrawRectangle(Pens.DodgerBlue, handle.X, handle.Y, handle.Width, handle.Height);
                    }
                }
            }

            if (_mode == Mode.Box)
            {
                var r = ToScreen(Normalise(_box));
                using var brush = new SolidBrush(Color.FromArgb(40, 30, 144, 255));
                g.FillRectangle(brush, r);
                g.DrawRectangle(Pens.DodgerBlue, r.X, r.Y, r.Width, r.Height);
            }
        }

        private RectangleF ToScreen(RectangleF r) => new(r.X * _zoom, r.Y * _zoom, r.Width * _zoom, r.Height * _zoom);

        private PointF ToGame(Point p) => new(p.X / _zoom, p.Y / _zoom);

        private static RectangleF Normalise(RectangleF r) =>
            RectangleF.FromLTRB(Math.Min(r.Left, r.Right), Math.Min(r.Top, r.Bottom), Math.Max(r.Left, r.Right), Math.Max(r.Top, r.Bottom));

        // handles 0..7: top-left, top, top-right, right, bottom-right, bottom, bottom-left, left
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

        private PageElement? HitTest(PointF game) =>
            _document.Elements.OrderByDescending(e => e.Z).FirstOrDefault(e => e.Bounds.Contains(game));

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            if (e.Button != MouseButtons.Left)
                return;

            var game = ToGame(e.Location);
            _downGame = game;
            _changedDuringDrag = false;

            if (_selection.Count == 1 && _selection[0].Locked != true)
            {
                var handles = Handles(ToScreen(_selection[0].Bounds));
                int index = Array.FindIndex(handles, h => RectangleF.Inflate(h, 3, 3).Contains(e.Location));
                if (index >= 0)
                {
                    _mode = Mode.Resize;
                    _handle = index;
                    StartDrag();
                    return;
                }
            }

            var hit = HitTest(game);
            bool toggle = (ModifierKeys & (Keys.Control | Keys.Shift)) != 0;
            if (hit == null)
            {
                if (!toggle)
                    Select(null);
                _mode = Mode.Box;
                _box = new RectangleF(game, SizeF.Empty);
                return;
            }

            if (toggle && _selection.Contains(hit))
            {
                _selection.Remove(hit);
                Invalidate();
                SelectionChanged?.Invoke(this, EventArgs.Empty);
                return;
            }
            if (!_selection.Contains(hit))
                Select(hit, toggle);

            _mode = Mode.Move;
            StartDrag();
        }

        private void StartDrag()
        {
            _snapshot = _document.ToJson();
            _startBounds = _selection.ToDictionary(s => s, s => s.Bounds);
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var game = ToGame(e.Location);
            CursorMoved?.Invoke(game);

            switch (_mode)
            {
                case Mode.Move:
                    DragMove(game);
                    break;
                case Mode.Resize:
                    DragResize(game);
                    break;
                case Mode.Box:
                    _box = new RectangleF(_box.Location, new SizeF(game.X - _box.X, game.Y - _box.Y));
                    Invalidate();
                    break;
                default:
                    Cursor = CursorFor(e.Location);
                    break;
            }
        }

        private Cursor CursorFor(Point location)
        {
            if (_selection.Count == 1 && _selection[0].Locked != true)
            {
                var handles = Handles(ToScreen(_selection[0].Bounds));
                int index = Array.FindIndex(handles, h => RectangleF.Inflate(h, 3, 3).Contains(location));
                if (index >= 0)
                    return (index % 4) switch { 0 => Cursors.SizeNWSE, 1 => Cursors.SizeNS, 2 => Cursors.SizeNESW, _ => Cursors.SizeWE };
            }
            return HitTest(ToGame(location)) != null ? Cursors.SizeAll : Cursors.Default;
        }

        private bool Free => (ModifierKeys & Keys.Alt) != 0;

        private void DragMove(PointF game)
        {
            var movable = _startBounds.Keys.Where(k => k.Locked != true).ToList();
            if (movable.Count == 0)
                return;
            float dx = game.X - _downGame.X, dy = game.Y - _downGame.Y;

            // snap the box around everything that moves, then move each by the same amount
            RectangleF moving = movable.Select(k => _startBounds[k]).Aggregate(RectangleF.Union);
            moving.Offset(dx, dy);
            _guides.Clear();
            if (!Free)
            {
                float sx = SnapAxis(moving.Left, moving.Left + moving.Width / 2, moving.Right, vertical: true, movable);
                float sy = SnapAxis(moving.Top, moving.Top + moving.Height / 2, moving.Bottom, vertical: false, movable);
                dx += sx;
                dy += sy;
            }

            foreach (var element in movable)
            {
                var start = _startBounds[element];
                element.X = (float)Math.Round(start.X + dx, 2);
                element.Y = (float)Math.Round(start.Y + dy, 2);
            }
            _changedDuringDrag = true;
            Invalidate();
        }

        /// <summary>
        /// How far to shift a moving box along one axis so one of its edges (or its centre) lands on a guide, else its first edge on the grid.
        /// Guides are the screen's edges and centre and the other elements' edges and centres, within 8 screen pixels.
        /// </summary>
        private float SnapAxis(float low, float mid, float high, bool vertical, ICollection<PageElement> moving)
        {
            float threshold = 8 / _zoom;
            if (SnapToGuides)
            {
                var targets = new List<float> { 0, vertical ? PageDocument.ScreenWidth / 2f : PageDocument.ScreenHeight / 2f, vertical ? PageDocument.ScreenWidth : PageDocument.ScreenHeight };
                foreach (var other in _document.Elements)
                {
                    if (moving.Contains(other))
                        continue;
                    var r = other.Bounds;
                    if (vertical) targets.AddRange([r.Left, r.Left + r.Width / 2, r.Right]);
                    else targets.AddRange([r.Top, r.Top + r.Height / 2, r.Bottom]);
                }

                float best = float.MaxValue, bestTarget = 0;
                foreach (float edge in new[] { low, mid, high })
                {
                    foreach (float target in targets)
                    {
                        float d = target - edge;
                        if (Math.Abs(d) < Math.Abs(best) && Math.Abs(d) <= threshold)
                        {
                            best = d;
                            bestTarget = target;
                        }
                    }
                }
                if (best != float.MaxValue)
                {
                    _guides.Add((vertical, bestTarget));
                    return best;
                }
            }
            return SnapToGrid && GridSize > 0 ? SnapValue(low) - low : 0;
        }

        private float SnapValue(float value) =>
            SnapToGrid && GridSize > 0 && !Free ? (float)Math.Round(value / GridSize) * GridSize : (float)Math.Round(value);

        private float SnapEdge(float value, bool vertical, PageElement self)
        {
            if (Free)
                return value;
            float threshold = 8 / _zoom;
            if (SnapToGuides)
            {
                var targets = new List<float> { 0, vertical ? PageDocument.ScreenWidth / 2f : PageDocument.ScreenHeight / 2f, vertical ? PageDocument.ScreenWidth : PageDocument.ScreenHeight };
                foreach (var other in _document.Elements.Where(o => o != self))
                {
                    var r = other.Bounds;
                    if (vertical) targets.AddRange([r.Left, r.Left + r.Width / 2, r.Right]);
                    else targets.AddRange([r.Top, r.Top + r.Height / 2, r.Bottom]);
                }
                var near = targets.Where(t => Math.Abs(t - value) <= threshold).OrderBy(t => Math.Abs(t - value)).ToList();
                if (near.Count > 0)
                {
                    _guides.Add((vertical, near[0]));
                    return near[0];
                }
            }
            return SnapValue(value);
        }

        private void DragResize(PointF game)
        {
            var element = _selection[0];
            var start = _startBounds[element];
            float left = start.Left, top = start.Top, right = start.Right, bottom = start.Bottom;
            float dx = game.X - _downGame.X, dy = game.Y - _downGame.Y;
            _guides.Clear();

            bool moveLeft = _handle is 0 or 6 or 7, moveRight = _handle is 2 or 3 or 4;
            bool moveTop = _handle is 0 or 1 or 2, moveBottom = _handle is 4 or 5 or 6;
            if (moveLeft) left = SnapEdge(start.Left + dx, true, element);
            if (moveRight) right = SnapEdge(start.Right + dx, true, element);
            if (moveTop) top = SnapEdge(start.Top + dy, false, element);
            if (moveBottom) bottom = SnapEdge(start.Bottom + dy, false, element);

            const float min = 8;
            if (right - left < min) { if (moveLeft) left = right - min; else right = left + min; }
            if (bottom - top < min) { if (moveTop) top = bottom - min; else bottom = top + min; }

            // corners keep the shape for pictures (or with Shift)
            bool keepAspect = (WidgetCatalog.Find(element.Kind)?.KeepAspect == true) ^ ((ModifierKeys & Keys.Shift) != 0);
            if (keepAspect && _handle % 2 == 0 && start.Height > 0)
            {
                float aspect = start.Width / start.Height;
                float width = right - left;
                float height = width / aspect;
                if (moveTop) top = bottom - height; else bottom = top + height;
            }

            element.Bounds = RectangleF.FromLTRB(left, top, right, bottom);
            if (WidgetCatalog.Find(element.Kind)?.HasButtons == true)
            {
                element.Y = start.Y;   // the game sets the height: 100 px per button
                WidgetCatalog.Normalise(_document);
            }
            _changedDuringDrag = true;
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            Capture = false;
            if (_mode == Mode.Box)
            {
                var box = Normalise(_box);
                if (box.Width > 2 || box.Height > 2)
                {
                    if ((ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
                        _selection.Clear();
                    foreach (var element in _document.Elements.Where(el => box.IntersectsWith(el.Bounds) && !_selection.Contains(el)))
                        _selection.Add(element);
                    SelectionChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else if ((_mode == Mode.Move || _mode == Mode.Resize) && _changedDuringDrag && _snapshot != null)
            {
                _undo.Push(_snapshot);
                _redo.Clear();
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
            _mode = Mode.None;
            _snapshot = null;
            _guides.Clear();
            Invalidate();
        }

        // ---- keyboard ----

        protected override bool IsInputKey(Keys keyData) =>
            (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            float step = e.Shift ? Math.Max(1, GridSize) : 1;
            switch (e.KeyCode)
            {
                case Keys.Left: Nudge(-step, 0); break;
                case Keys.Right: Nudge(step, 0); break;
                case Keys.Up: Nudge(0, -step); break;
                case Keys.Down: Nudge(0, step); break;
                case Keys.Delete: DeleteSelection(); break;
                case Keys.Escape: Select(null); break;
                case Keys.Z when e.Control && e.Shift: Redo(); break;
                case Keys.Z when e.Control: Undo(); break;
                case Keys.Y when e.Control: Redo(); break;
                case Keys.C when e.Control: Copy(); break;
                case Keys.V when e.Control: Paste(); break;
                case Keys.D when e.Control: Duplicate(); break;
                case Keys.A when e.Control: SelectAll(); break;
                case Keys.OemCloseBrackets: ChangeOrder(true); break;
                case Keys.OemOpenBrackets: ChangeOrder(false); break;
                default: base.OnKeyDown(e); return;
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        // ---- drag in from the palette ----

        protected override void OnDragOver(DragEventArgs e)
        {
            e.Effect = e.Data?.GetDataPresent(DragFormat) == true ? DragDropEffects.Copy : DragDropEffects.None;
        }

        protected override void OnDragDrop(DragEventArgs e)
        {
            if (e.Data?.GetData(DragFormat) is not string id || WidgetCatalog.Find(id) is not { } kind)
                return;
            var point = ToGame(PointToClient(new Point(e.X, e.Y)));
            Add(kind, point);
            Focus();
        }
    }
}
