using System.Drawing.Drawing2D;

namespace DuelIt
{
    /// <summary>
    /// Draws a <see cref="DuelState"/> as a duel field: the other side on top (mirrored), the local side at the bottom, cards with their
    /// art. Raises <see cref="HoverChanged"/> with the card or pile under the mouse (for the hover card).
    /// </summary>
    public sealed class BoardView : Control
    {
        private DuelState? _state;
        private readonly List<(Rectangle Area, HoverTarget Target)> _hits = [];
        private HoverTarget? _hover;

        public BoardView()
        {
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Color.FromArgb(24, 28, 36);
        }

        public GameData Data { get; set; } = GameData.Empty;

        public DuelState? State
        {
            get => _state;
            set { _state = value; Invalidate(); }
        }

        /// <summary>The card or pile under the mouse changed (null = nothing); the point is in screen coordinates.</summary>
        public event Action<HoverTarget?, Point>? HoverChanged;

        private static readonly Color ZoneFill = Color.FromArgb(34, 40, 52);
        private static readonly Color ZoneBorder = Color.FromArgb(70, 80, 100);
        private static readonly Color MovedBorder = Color.Gold;
        private static readonly Color LabelColour = Color.FromArgb(110, 120, 140);

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            _hits.Clear();
            if (_state == null)
            {
                TextRenderer.DrawText(g, "Open Duels.log and pick a duel.", Font, ClientRectangle, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            int local = _state.LocalSide;
            int half = ClientSize.Height / 2;
            DrawSide(g, _state, 1 - local, new Rectangle(0, 0, ClientSize.Width, half - 12), top: true);
            DrawSide(g, _state, local, new Rectangle(0, half + 12, ClientSize.Width, ClientSize.Height - half - 12), top: false);

            // Middle bar: turn / phase / result
            string middle = $"Turn {_state.Turn}   {(_state.TurnSide >= 0 ? DuelText.Player(_state.TurnSide, local) : "")}   {_state.Phase}";
            if (_state.Result != null)
                middle += $"   |   {_state.Result}";
            if (_state.Mismatches > 0)
                middle += $"   |   {_state.Mismatches} move(s) didn't match the tracked position";
            using var bold = new Font(Font.FontFamily, Font.Size + 1, FontStyle.Bold);
            var bar = new Rectangle(0, half - 12, ClientSize.Width, 24);
            using (var brush = new SolidBrush(Color.FromArgb(12, 14, 20)))
                g.FillRectangle(brush, bar);
            TextRenderer.DrawText(g, middle, bold, bar, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawSide(Graphics g, DuelState state, int sideIndex, Rectangle area, bool top)
        {
            var side = state.Sides[sideIndex];
            string who = DuelText.Player(sideIndex, state.LocalSide);
            const int pad = 6;
            const int columns = 7;   // left pile column + 5 zones + right pile column

            // Four card rows per side (extra monster, monster, spell/trap, hand), card-shaped cells.
            int cellHeight = Math.Max(30, (area.Height - pad * 5) / 4);
            int cellWidth = (int)(cellHeight * CardPainter.Aspect);
            if (cellWidth * columns + pad * (columns + 1) > area.Width)
            {
                cellWidth = Math.Max(20, (area.Width - pad * (columns + 1)) / columns);
                cellHeight = (int)(cellWidth / CardPainter.Aspect);
            }
            int left = area.Left + (area.Width - (cellWidth * columns + pad * (columns - 1))) / 2;

            int[] rowY = new int[4];
            for (int row = 0; row < 4; row++)
            {
                // local side reads top-down EMZ, monsters, spells/traps, hand; the other side is mirrored
                int slotFromTop = top ? 3 - row : row;
                rowY[row] = area.Top + pad + slotFromTop * (cellHeight + pad);
            }

            int X(int column) => left + column * (cellWidth + pad);
            Rectangle Cell(int column, int row) => new(X(column), rowY[row], cellWidth, cellHeight);

            // Extra monster zones sit over monster zones 2 and 4.
            DrawZone(g, state, Cell(2, 0), side.Field[5], "EMZ", $"{who} Extra Monster Zone 1");
            DrawZone(g, state, Cell(4, 0), side.Field[6], "EMZ", $"{who} Extra Monster Zone 2");
            for (int i = 0; i < 5; i++)
            {
                int column = top ? 5 - i : 1 + i;   // the opponent's zones read right to left, as seen across the table
                DrawZone(g, state, Cell(column, 1), side.Field[i], $"M{i + 1}", $"{who} Monster Zone {i + 1}");
                DrawZone(g, state, Cell(column, 2), side.Field[7 + i], $"S/T{i + 1}", $"{who} Spell & Trap Zone {i + 1}");
            }
            DrawZone(g, state, Cell(top ? 6 : 0, 1), side.Field[12], "Field", $"{who} Field Zone");
            DrawPile(g, state, Cell(top ? 0 : 6, 1), "Graveyard", who, side.Grave, faceUp: true);
            DrawPile(g, state, Cell(top ? 0 : 6, 2), "Deck", who, side.Deck, faceUp: false);
            DrawPile(g, state, Cell(top ? 6 : 0, 2), "Extra Deck", who, side.Extra, faceUp: false);
            DrawPile(g, state, Cell(top ? 0 : 6, 0), "Banished", who, side.Banished, faceUp: true);

            // Player label + LP in the free corner of the extra monster row
            var labelArea = top ? new Rectangle(X(5), rowY[0], cellWidth * 2 + pad, cellHeight) : new Rectangle(X(0), rowY[0], cellWidth * 2 + pad, cellHeight);
            using var lpFont = new Font(Font.FontFamily, Font.Size + 8, FontStyle.Bold);
            string label = who + (state.TurnSide == sideIndex ? "   - turn" : "");
            TextRenderer.DrawText(g, label, Font, new Rectangle(labelArea.X, labelArea.Y + 4, labelArea.Width, 18), Color.LightGray, TextFormatFlags.Left);
            TextRenderer.DrawText(g, $"{side.LifePoints} LP", lpFont, new Rectangle(labelArea.X, labelArea.Y + 22, labelArea.Width, 34),
                side.LifePoints <= 1000 ? Color.OrangeRed : Color.White, TextFormatFlags.Left);

            // Hand: a row of cards, overlapping when there are many
            var handArea = new Rectangle(X(0), rowY[3], X(6) + cellWidth - X(0), cellHeight);
            DrawHand(g, state, handArea, side.Hand, cellWidth, who);
        }

        private void DrawCard(Graphics g, DuelState state, Rectangle r, int slot, string context)
        {
            int id = state.CardId(slot);
            CardPainter.Draw(g, Data, r, id, DuelText.Card(state, slot));
            bool moved = slot == state.LastMovedSlot;
            using (var pen = new Pen(moved ? MovedBorder : Color.FromArgb(160, 0, 0, 0), moved ? 3 : 1))
                g.DrawRectangle(pen, r);
            _hits.Add((r, new HoverTarget(id, context)));
        }

        private void DrawZone(Graphics g, DuelState state, Rectangle r, int? slot, string label, string context)
        {
            using (var fill = new SolidBrush(ZoneFill))
                g.FillRectangle(fill, r);
            using (var pen = new Pen(ZoneBorder))
                g.DrawRectangle(pen, r);
            if (slot != null)
                DrawCard(g, state, Rectangle.Inflate(r, -1, -1), slot.Value, context);
            else
                TextRenderer.DrawText(g, label, Font, r, LabelColour, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private void DrawPile(Graphics g, DuelState state, Rectangle r, string label, string who, List<int> pile, bool faceUp)
        {
            using (var fill = new SolidBrush(ZoneFill))
                g.FillRectangle(fill, r);
            bool moved = state.LastMovedSlot is int slot && pile.Contains(slot);
            if (pile.Count > 0)
            {
                var card = Rectangle.Inflate(r, -1, -1);
                if (faceUp)
                    CardPainter.Draw(g, Data, card, state.CardId(pile[^1]));
                else
                    CardPainter.DrawBack(g, Data, card);
            }
            using (var pen = new Pen(moved ? MovedBorder : ZoneBorder, moved ? 3 : 1))
                g.DrawRectangle(pen, r);

            // count badge
            string text = pile.Count > 0 ? $"{label}  {pile.Count}" : label;
            var badge = new Rectangle(r.X, r.Bottom - 18, r.Width, 18);
            if (pile.Count > 0)
                using (var brush = new SolidBrush(Color.FromArgb(190, 0, 0, 0)))
                    g.FillRectangle(brush, badge);
            TextRenderer.DrawText(g, text, Font, pile.Count > 0 ? badge : r, pile.Count > 0 ? Color.White : LabelColour,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // top first: the last card added is on top of graveyard/banished; the deck's top is its first card
            var ids = (faceUp ? Enumerable.Reverse(pile) : pile).Select(state.CardId).ToList();
            _hits.Add((r, new HoverTarget(ids.Count > 0 ? ids[0] : 0, $"{who} {label}", $"{who} {label}", ids)));
        }

        private void DrawHand(Graphics g, DuelState state, Rectangle area, List<int> hand, int cardWidth, string who)
        {
            using (var fill = new SolidBrush(Color.FromArgb(28, 32, 42)))
                g.FillRectangle(fill, area);
            if (hand.Count == 0)
            {
                TextRenderer.DrawText(g, "(empty hand)", Font, area, Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            int step = hand.Count == 1 ? 0 : Math.Min(cardWidth + 6, (area.Width - cardWidth) / (hand.Count - 1));
            int total = cardWidth + step * (hand.Count - 1);
            int x = area.X + (area.Width - total) / 2;
            // drawn left to right, so later cards overlap earlier ones; hit-test the visible strip of each
            for (int i = 0; i < hand.Count; i++)
            {
                var r = new Rectangle(x + i * step, area.Y, cardWidth, area.Height);
                DrawCard(g, state, r, hand[i], $"{who} hand, card {i + 1} of {hand.Count}");
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            HoverTarget? found = null;
            for (int i = _hits.Count - 1; i >= 0; i--)   // last drawn is on top
                if (_hits[i].Area.Contains(e.Location))
                {
                    found = _hits[i].Target;
                    break;
                }
            if (found != _hover || found != null)
            {
                _hover = found;
                HoverChanged?.Invoke(found, PointToScreen(e.Location));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = null;
            HoverChanged?.Invoke(null, Point.Empty);
        }
    }
}
