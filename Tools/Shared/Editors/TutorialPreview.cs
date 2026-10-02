using System.Drawing.Drawing2D;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// One frame of a Steam tutorial (<see cref="TutorialState"/> from <see cref="TutorialPlayback"/>): the duel field with the cursor and
    /// pointer on their zones, the phase and turn, the hint line, the message box (lower, or upper when the step says so) with the
    /// game's text expansion ($card names, $A button icons) and @ colours, and what the tutorial is waiting for. Drawn on a 1920 x 1080
    /// frame and scaled to fit. The layout is schematic: it shows what the script does, not the game's exact screen.
    /// </summary>
    public sealed class TutorialPreview : Control
    {
        private TutorialState? _state;
        private int _stepCount;
        private Bitmap? _frame;

        public TutorialPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(18, 18, 24);
        }

        /// <summary>A card's name by Konami id, or null if there is no such card.</summary>
        public Func<int, string?> CardName { get; set; } = _ => null;

        public void Show(TutorialState? state, int stepCount)
        {
            _state = state;
            _stepCount = stepCount;
            _frame?.Dispose();
            _frame = Render();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (_frame == null)
                return;
            float scale = Math.Min((float)Width / _frame.Width, (float)Height / _frame.Height);
            var target = new RectangleF((Width - _frame.Width * scale) / 2, (Height - _frame.Height * scale) / 2, _frame.Width * scale, _frame.Height * scale);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            e.Graphics.DrawImage(_frame, target);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _frame?.Dispose();
            base.Dispose(disposing);
        }

        // ---- the field: 5 monster + 5 spell/trap zones a side, the field zone and the piles; zones are the engine's numbers ----

        // Layout (1920 x 1080): top bar, hint band, the opponent's hand, their back and front rows, a shared middle row (zone 11 and
        // the banished piles), your front and back rows, your hand, then the message box (the upper box covers the opponent's side).
        private const int Cell = 96, Gap = 10, Columns = 7;
        private const int Left = (1920 - Columns * Cell - (Columns - 1) * Gap) / 2;
        private static readonly Rectangle HintBand = new(360, 48, 1200, 58);
        private static readonly Rectangle LowerBox = new(260, 770, 1400, 270);
        private static readonly Rectangle UpperBox = new(260, 110, 1400, 270);

        private static int RowY(int player, int row) => row switch
        {
            -1 => 382,                                       // the middle row both players share
            0 => player == 0 ? 488 : 276,                    // front: monsters, field spell, graveyard
            _ => player == 0 ? 594 : 170,                    // back: spells/traps, extra deck, deck
        };

        /// <summary>Where a zone is drawn for a player (0 = you, bottom; 1 = the opponent, top, mirrored), or null (hand, or unknown).</summary>
        private static Rectangle? ZoneRect(int player, int zone)
        {
            (int column, int row) = zone switch
            {
                >= 0 and <= 4 => (zone + 1, 0),
                >= 5 and <= 9 => (zone - 4, 1),
                10 => (0, 0),
                11 => (2, -1),
                14 => (0, 1),
                15 => (6, 1),
                16 => (6, 0),
                17 => (6, -1),
                _ => (-1, 0),
            };
            if (column < 0)
                return null;
            if (player != 0)
                column = Columns - 1 - column;
            return new Rectangle(Left + column * (Cell + Gap), RowY(player, row), Cell, Cell);
        }

        private static Rectangle HandRect(int player) => player == 0 ? new Rectangle(Left, 700, Columns * (Cell + Gap) - Gap, 56) : new Rectangle(Left, 110, Columns * (Cell + Gap) - Gap, 50);

        // Screen-element pointer targets (TutorialFile.IsScreenTarget): 20-25 are drawn as the phase bar; the rest get one labelled
        // slot, as what they are isn't traced yet.
        private static Rectangle PhaseRect(int phase) => new(1500, 300 + phase * 64, 150, 54);

        // the card info panel (ShowCard); screen elements 16, 26, 30-35 and 40-53 (40-53 look like parts of this panel) point at it
        private static readonly Rectangle CardPanel = new(24, 560, 380, 190);
        private static readonly Rectangle OtherTarget = new(44, 650, 340, 56);

        private static Rectangle TargetRect(int player, int target) =>
            TutorialFile.IsScreenTarget(target)
                ? target is >= 20 and <= 26 ? PhaseRect(target - 20) : OtherTarget
                : ZoneRect(player & 1, target) ?? HandRect(player & 1);

        private Bitmap Render()
        {
            var frame = new Bitmap(1920, 1080);
            using var g = Graphics.FromImage(frame);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new LinearGradientBrush(new Rectangle(0, 0, 1920, 1080), Color.FromArgb(22, 34, 58), Color.FromArgb(6, 10, 22), 90f))
                g.FillRectangle(background, 0, 0, 1920, 1080);

            using var small = new Font("Segoe UI", 20, FontStyle.Regular, GraphicsUnit.Pixel);
            using var label = new Font("Segoe UI", 17, FontStyle.Regular, GraphicsUnit.Pixel);
            using var big = new Font("Segoe UI", 30, FontStyle.Bold, GraphicsUnit.Pixel);
            using var body = new Font("Segoe UI", 30, FontStyle.Regular, GraphicsUnit.Pixel);

            if (_state == null)
            {
                TextRenderer.DrawText(g, "Open a tutorial to preview it.", big, new Rectangle(0, 0, 1920, 1080), Color.Gainsboro,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return frame;
            }
            var s = _state;

            // zones
            for (int player = 0; player < 2; player++)
            {
                for (int zone = 0; zone <= 17; zone++)
                {
                    if (ZoneRect(player, zone) is not Rectangle r)
                        continue;
                    using var fill = new SolidBrush(player == 0 ? Color.FromArgb(70, 40, 90, 160) : Color.FromArgb(70, 160, 50, 50));
                    using var edge = new Pen(Color.FromArgb(120, 170, 200, 240), 2);
                    g.FillRectangle(fill, r);
                    g.DrawRectangle(edge, r);
                    TextRenderer.DrawText(g, $"{zone}\n{TutorialFile.ZoneName(zone)}", label, r, Color.FromArgb(170, 200, 220, 240),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                var hand = HandRect(player);
                using (var handFill = new SolidBrush(Color.FromArgb(50, 255, 255, 255)))
                    g.FillRectangle(handFill, hand);
                TextRenderer.DrawText(g, player == 0 ? "Your hand" : "Opponent's hand", small, hand, Color.FromArgb(160, 220, 220, 220),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // the phase bar (pointer targets 20-25)
            string phase = s.Phase >= 0 ? TutorialFile.PhaseName(s.Phase) : "-";
            for (int p = 0; p < 6; p++)
            {
                var r = PhaseRect(p);
                using var fill = new SolidBrush(p == s.Phase ? Color.FromArgb(200, 30, 110, 200) : Color.FromArgb(90, 30, 50, 80));
                g.FillRectangle(fill, r);
                TextRenderer.DrawText(g, TutorialFile.PhaseName(p), small, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // card info panel
            if (s.CardInfo is int shown)
            {
                using var fill = new SolidBrush(Color.FromArgb(200, 12, 24, 44));
                using var edge = new Pen(Color.FromArgb(160, 110, 190, 255), 2);
                g.FillRectangle(fill, CardPanel);
                g.DrawRectangle(edge, CardPanel);
                string name = shown == 2 ? "(the current card)" : CardName(shown) ?? $"card #{shown}";
                TextRenderer.DrawText(g, "Card info", label, new Point(CardPanel.Left + 12, CardPanel.Top + 8), Color.FromArgb(160, 200, 230, 255));
                TextRenderer.DrawText(g, name, small, new Rectangle(CardPanel.Left + 12, CardPanel.Top + 34, CardPanel.Width - 24, 56), Color.White,
                    TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
            }

            // cursor (yellow frame) and pointer (pulsing arrow above its target)
            if (s.Cursor is var (cp, cz, ci))
            {
                Rectangle r = ZoneRect(cp & 1, cz) ?? HandRect(cp & 1);
                using var pen = new Pen(Color.Gold, 5);
                g.DrawRectangle(pen, Rectangle.Inflate(r, 5, 5));
                string what = cz == 12 && ci >= 0xF3C ? CardName(ci) ?? $"card {ci}" : $"player {cp} zone {cz} [{ci}]";
                TextRenderer.DrawText(g, "cursor: " + what, label, new Point(r.Left, r.Bottom + 6), Color.Gold);
            }
            if (s.Pointer is var (pp, pz))
            {
                Rectangle r = TargetRect(pp, pz);
                if (r == OtherTarget)
                {
                    using var fill = new SolidBrush(Color.FromArgb(160, 90, 60, 20));
                    g.FillRectangle(fill, r);
                    TextRenderer.DrawText(g, $"screen element {pz}", small, r, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                float pulse = (float)(Math.Sin(s.Frame / 60.0 * Math.PI * 2) * 8);
                int tipY = (int)(r.Top - 6 - pulse);
                int centreX = r.Left + r.Width / 2;
                var arrow = new[] { new Point(centreX, tipY), new Point(centreX - 22, tipY - 38), new Point(centreX + 22, tipY - 38) };
                using var brush = new SolidBrush(Color.FromArgb(240, 255, 120, 40));
                g.FillPolygon(brush, arrow);
            }

            // top bar: turn, phase, step, time
            string top = $"Board setup {(s.Scenario >= 0 ? s.Scenario.ToString() : "-")}   Turn: {(s.TurnPlayer == 0 ? "you" : "opponent")}   Phase: {phase}   " +
                         $"Step {s.Step + 1}/{_stepCount}   Frame {s.Frame} ({s.Frame / 60.0:0.00} s)   Input {(s.InputOn ? "on" : "off")}";
            TextRenderer.DrawText(g, top, small, new Point(24, 16), Color.White);
            if (s.PhaseBanner && s.StepFrame < 60)
            {
                var banner = new Rectangle(0, 390, 1920, 80);
                using var fill = new SolidBrush(Color.FromArgb(180, 20, 60, 140));
                g.FillRectangle(fill, banner);
                TextRenderer.DrawText(g, phase + " Phase", big, banner, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            // side panel: allowed cards and deck stacking
            int y = 120;
            if (s.AllowedCards.Count > 0)
            {
                TextRenderer.DrawText(g, "Cards the player may use:", small, new Point(24, y), Color.LightGreen);
                foreach (int card in s.AllowedCards)
                    TextRenderer.DrawText(g, "  " + (CardName(card) ?? $"#{card}"), small, new Point(24, y += 28), Color.LightGreen);
                y += 40;
            }
            foreach (string stack in s.DeckStacks.TakeLast(6))
                TextRenderer.DrawText(g, stack.Contains('$') ? ExpandCards(stack) : stack, label, new Point(24, y += 24), Color.FromArgb(200, 200, 200));

            // hint line
            if (s.Hint != null)
            {
                using var fill = new SolidBrush(Color.FromArgb(220, 10, 40, 90));
                g.FillRectangle(fill, HintBand);
                DrawMarkup(g, TutorialText.Expand(s.Hint, CardName), Rectangle.Inflate(HintBand, -20, -12), small);
            }

            // message box
            if (s.Message != null)
            {
                var box = s.MessageUpper ? UpperBox : LowerBox;
                using var fill = new SolidBrush(Color.FromArgb(235, 8, 16, 34));
                using var edge = new Pen(Color.FromArgb(200, 110, 190, 255), 3);
                g.FillRectangle(fill, box);
                g.DrawRectangle(edge, box);
                DrawMarkup(g, TutorialText.Expand(s.Message, CardName), Rectangle.Inflate(box, -30, -24), body);
                TextRenderer.DrawText(g, "▼", small, new Point(box.Right - 44, box.Bottom - 40), Color.FromArgb(200, 110, 190, 255));
            }

            if (s.Waiting != null)
            {
                var wait = s.Message != null && !s.MessageUpper ? new Rectangle(560, 710, 800, 46) : new Rectangle(560, 990, 800, 46);
                using var fill = new SolidBrush(Color.FromArgb(200, 120, 70, 0));
                g.FillRectangle(fill, wait);
                TextRenderer.DrawText(g, "Waiting for the player: " + s.Waiting, small, wait, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            if (s.Ended != null)
            {
                using var dim = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
                g.FillRectangle(dim, 0, 0, 1920, 1080);
                TextRenderer.DrawText(g, s.Ended, big, new Rectangle(0, 0, 1920, 1080), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            return frame;
        }

        private string ExpandCards(string text) => TutorialText.Expand(text, CardName).Replace("@8", "#").Replace("@0", "");

        private static Color Colour(int index) =>
            Color.FromArgb(255, Color.FromArgb((int)HowToText.Colours[Math.Clamp(index, 0, HowToText.Colours.Length - 1)]));

        /// <summary>Word-wraps the text's coloured runs into the box.</summary>
        private static void DrawMarkup(Graphics g, string text, Rectangle box, Font font)
        {
            const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            int lineHeight = font.Height + 6;
            int x = box.Left, y = box.Top;
            foreach (var run in HowToText.Parse(text))
            {
                if (run.Text == "\n")
                {
                    x = box.Left;
                    y += lineHeight;
                    continue;
                }
                var colour = Colour(run.Colour);
                foreach (string word in Words(run.Text))
                {
                    if (y > box.Top && y > box.Bottom - lineHeight + 4)
                        return;
                    int width = TextRenderer.MeasureText(g, word, font, Size.Empty, Flags).Width;
                    if (x + width > box.Right && x > box.Left)
                    {
                        x = box.Left;
                        y += lineHeight;
                        if (word.Trim().Length == 0)
                            continue;
                    }
                    TextRenderer.DrawText(g, word, font, new Point(x, y), colour, Flags);
                    x += width;
                }
            }
        }

        private static IEnumerable<string> Words(string text)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ')
                {
                    yield return text[start..(i + 1)];
                    start = i + 1;
                }
                else if (text[i] >= 0x3000)
                {
                    if (i > start)
                        yield return text[start..i];
                    yield return text[i].ToString();
                    start = i + 1;
                }
            }
            if (start < text.Length)
                yield return text[start..];
        }
    }
}
