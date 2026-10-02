namespace DuelIt
{
    /// <summary>What the mouse is over: one card (with where it is), or a pile (graveyard, deck, ...) and the cards in it, top first.</summary>
    public sealed record HoverTarget(int CardId, string Context, string? PileTitle = null, IReadOnlyList<int>? PileCards = null)
    {
        public bool IsPile => PileCards != null;
    }

    /// <summary>
    /// The hover card: a big picture of the card with its name, type, stats and text, or a pile's contents. A tool window that never
    /// takes focus, so the board and the event list keep the keyboard.
    /// </summary>
    public sealed class CardPopup : Form
    {
        private readonly GameData _data;
        private HoverTarget? _target;

        private const int Pad = 5, CardWidth = 100, TextWidth = 200;
        private static readonly Color Back = Color.FromArgb(22, 25, 32);

        public CardPopup(GameData data)
        {
            _data = data;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Back;
            DoubleBuffered = true;
            // No TopMost property: setting it activates the window, which greyed out DuelIt's title bar on every hover. The popup
            // is kept on top by WS_EX_TOPMOST and shown/moved only with no-activate calls, like a tooltip.
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                const int WS_POPUP = unchecked((int)0x80000000);
                const int WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 0x8;
                var cp = base.CreateParams;
                cp.Style = WS_POPUP;
                cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
                return cp;
            }
        }

        // Clicks (it shouldn't get any: it sits beside the cursor) must not activate it either.
        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

        private void PlaceWithoutActivating(Rectangle bounds, bool show)
        {
            const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
            var HWND_TOPMOST = new IntPtr(-1);
            SetWindowPos(Handle, HWND_TOPMOST, bounds.X, bounds.Y, bounds.Width, bounds.Height, SWP_NOACTIVATE | (show ? SWP_SHOWWINDOW : 0));
        }

        /// <summary>Shows target next to the cursor (screen point), or hides the popup for null.</summary>
        public void ShowFor(HoverTarget? target, Point screen)
        {
            if (target == null)
            {
                _target = null;
                Hide();
                return;
            }
            bool changed = target != _target;
            _target = target;
            var size = Measure(target);

            var work = Screen.FromPoint(screen).WorkingArea;
            int x = screen.X + 18, y = screen.Y + 18;
            if (x + size.Width > work.Right)
                x = screen.X - size.Width - 12;
            if (y + size.Height > work.Bottom)
                y = Math.Max(work.Top, work.Bottom - size.Height);
            bool show = !Visible;
            PlaceWithoutActivating(new Rectangle(x, y, size.Width, size.Height), show);
            if (show)
                Visible = true;   // sync WinForms' state with the already-shown window (ShowWithoutActivation keeps this from activating)
            if (changed || show)
                Invalidate();
        }

        private string Body(HoverTarget target)
        {
            if (target.IsPile)
            {
                var lines = target.PileCards!.Take(10).Select((id, i) => $"{i + 1}. {_data.NameOf(id)}");
                string more = target.PileCards!.Count > 10 ? $"\n... {target.PileCards.Count - 10} more" : "";
                return target.PileCards.Count == 0 ? "(empty)" : string.Join("\n", lines) + more;
            }
            var info = _data.Card(target.CardId);
            return info?.Description ?? "";
        }

        private Size Measure(HoverTarget target)
        {
            using var font = new Font("Segoe UI", 7.5f);
            var text = TextRenderer.MeasureText(Body(target), font, new Size(TextWidth, 2000), TextFormatFlags.WordBreak);
            int cardHeight = (int)(CardWidth / CardPainter.Aspect);
            int height = Math.Max(cardHeight, 70 + text.Height + 8) + Pad * 2;
            return new Size(Pad * 3 + CardWidth + TextWidth, height);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var pen = new Pen(Color.FromArgb(90, 100, 124)))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            if (_target == null)
                return;

            int shownId = _target.IsPile ? (_target.PileCards!.Count > 0 ? _target.PileCards[0] : 0) : _target.CardId;
            var cardRect = new Rectangle(Pad, Pad, CardWidth, (int)(CardWidth / CardPainter.Aspect));
            if (shownId > 0)
                CardPainter.Draw(g, _data, cardRect, shownId);
            else if (_target.IsPile)
                CardPainter.DrawBack(g, _data, cardRect);

            int x = Pad * 2 + CardWidth, y = Pad;
            var info = _data.Card(_target.CardId);
            using var titleFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
            using var font = new Font("Segoe UI", 7.5f);
            string title = _target.IsPile ? $"{_target.PileTitle} ({_target.PileCards!.Count})" : info?.Name ?? $"#{_target.CardId}";
            TextRenderer.DrawText(g, title, titleFont, new Rectangle(x, y, TextWidth, 17), Color.White, TextFormatFlags.EndEllipsis);
            y += 17;

            if (!_target.IsPile && info != null)
            {
                TextRenderer.DrawText(g, info.TypeLine, font, new Rectangle(x, y, TextWidth, 13), Color.FromArgb(200, 190, 150), TextFormatFlags.EndEllipsis);
                y += 13;
                if (info.StatLine.Length > 0)
                {
                    using var bold = new Font(font, FontStyle.Bold);
                    TextRenderer.DrawText(g, info.StatLine, bold, new Rectangle(x, y, TextWidth, 13), Color.White, TextFormatFlags.EndEllipsis);
                    y += 13;
                }
            }
            string context = _target.Context + (info?.IsCustom == true ? "  (custom card)" : "") + (_target.CardId > 0 && !_target.IsPile ? $"  id {_target.CardId}" : "");
            TextRenderer.DrawText(g, context, font, new Rectangle(x, y, TextWidth, 13), Color.FromArgb(130, 140, 160), TextFormatFlags.EndEllipsis);
            y += 16;

            TextRenderer.DrawText(g, Body(_target), font, new Rectangle(x, y, TextWidth, Height - y - Pad), Color.Gainsboro, TextFormatFlags.WordBreak);
        }
    }
}
