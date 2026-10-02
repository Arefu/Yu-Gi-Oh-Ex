using Wolf.Editors;

namespace WolfX
{
    /// <summary>
    /// The top of the Card Manager and New cards pages: the card's face as the game draws it (GameCardPainter: the frames, fonts and icons
    /// of the open game data, scaled to the height it gets) and beside it the name, type line, stats, a line of ids and the card text.
    /// </summary>
    internal sealed class CardSummary : Panel
    {
        private sealed class Canvas : Panel
        {
            public Canvas()
            {
                DoubleBuffered = true;
                ResizeRedraw = true;
            }
        }

        private readonly Canvas _canvas = new() { Dock = DockStyle.Left, Width = 196 };
        private readonly Label _title = new() { Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI", 13f, FontStyle.Bold), AutoEllipsis = true, UseMnemonic = false };
        private readonly Label _typeLine = new() { Dock = DockStyle.Top, Height = 20, ForeColor = Color.FromArgb(70, 70, 90), UseMnemonic = false, AutoEllipsis = true };
        private readonly Label _statLine = new() { Dock = DockStyle.Top, Height = 20, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold), UseMnemonic = false };
        private readonly Label _info = new() { Dock = DockStyle.Bottom, Height = 36, ForeColor = SystemColors.GrayText, UseMnemonic = false };
        private readonly TextBox _text = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None };

        /// <summary>Room beside the card for the "show larger" button (PreviewPopOut), so it never sits on the card.</summary>
        private const int ButtonStrip = 30;

        private GameCard? _card;
        private string _name = "";
        private Bitmap? _picture;
        private bool _ownsPicture;

        /// <summary>Where the frames and icons come from (the open data).</summary>
        public IGameCardAssets? Art { get; set; }

        public CardSummary()
        {
            PreviewPopOut.Attach(_canvas, "Card preview", doubleClick: true);
            PreviewPopOut.SetShape(_canvas, new Size(400, 580));   // the game's card face
            Dock = DockStyle.Fill;
            Padding = new Padding(6);
            var words = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 4, 4) };
            words.Controls.Add(_text);
            words.Controls.Add(_info);
            words.Controls.Add(_statLine);
            words.Controls.Add(_typeLine);
            words.Controls.Add(_title);
            _text.BackColor = SystemColors.Control;
            Controls.Add(words);
            Controls.Add(_canvas);
            _canvas.Paint += (_, e) => DrawCard(e.Graphics);
            // the card as tall as there is room, but the words beside it keep at least 240 px
            Resize += (_, _) => _canvas.Width = Math.Max(60, Math.Min((int)((ClientSize.Height - Padding.Vertical) * GameCardPainter.Aspect) + 8 + ButtonStrip, ClientSize.Width - 240));
            Disposed += (_, _) => DropPicture();
        }

        /// <summary>
        /// Shows a card. <paramref name="ownsPicture"/>: the picture was made for this (a custom card's file) and is disposed when
        /// the next one is shown; game art comes from <see cref="CardArt"/>'s cache and is not.
        /// </summary>
        public void ShowCard(GameCard? card, string info, Bitmap? picture, bool ownsPicture)
        {
            DropPicture();
            _card = card;
            _picture = picture;
            _ownsPicture = ownsPicture;
            _title.Text = card?.Name ?? "";
            _typeLine.Text = card == null ? "" : TypeLine(card);
            _statLine.Text = card == null ? "" : Stats(card);
            _info.Text = info;
            _text.Text = (card?.Text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
            _canvas.Invalidate();
        }

        /// <summary>Only the line of ids changed (the card was edited, say).</summary>
        public string Info
        {
            get => _info.Text;
            set => _info.Text = value;
        }

        public void ShowNothing(string message = "") => ShowCard(null, message, null, false);

        private void DropPicture()
        {
            if (_ownsPicture)
                _picture?.Dispose();
            _picture = null;
            _ownsPicture = false;
        }

        private void DrawCard(Graphics g)
        {
            g.Clear(_canvas.BackColor);
            if (Art == null || _card == null)
                return;
            // the card on the left; the strip on its right keeps the "show larger" button off the card
            int strip = _canvas.Parent == this ? ButtonStrip : 0;
            var r = GameCardPainter.Fit(new Rectangle(4, 4, _canvas.Width - 8 - strip, _canvas.Height - 8));
            GameCardPainter.Draw(g, Art, r, _card, _picture);
        }

        // ---- the words, the same for game and custom cards ----

        private static readonly string[] ArrowNames = ["top-left", "top", "top-right", "left", "right", "bottom-left", "bottom", "bottom-right"];

        public static bool IsSpellOrTrap(GameCard card) => card.Kind is 13 or 14;

        public static string Stats(GameCard card)
        {
            static string Stat(int value) => value < 0 ? "?" : value.ToString();
            int frame = GameCardPainter.FrameOf(card.Kind);
            return IsSpellOrTrap(card) ? "" : frame == 18 ? $"ATK {Stat(card.Atk)}   LINK-{card.Level}" : $"ATK {Stat(card.Atk)}   DEF {Stat(card.Def)}";
        }

        /// <summary>"[Dragon / Normal]   Level 8   LIGHT", "Spell (Quick-Play)", "... Link 3   arrows: top, bottom-left", "... Scales 4 / 4".</summary>
        public static string TypeLine(GameCard card)
        {
            string kind = CardNames.KindName(card.Kind);
            if (IsSpellOrTrap(card))
                return $"{kind} ({CardNames.Icons[Math.Clamp(card.Icon, 0, CardNames.Icons.Length - 1)]})";
            int frame = GameCardPainter.FrameOf(card.Kind);
            string level = frame == 18 ? $"Link {card.Level}" : frame is 12 or 15 ? $"Rank {card.Level}" : $"Level {card.Level}";
            string line = $"[{card.Race} / {kind}]   {level}   {CardNames.AttributeName(card.Attribute).ToUpperInvariant()}";
            if (GameCardPainter.IsPendulumFrame(frame))
                line += $"   Scale {card.Scale}";
            if (frame == 18)
                line += "   Arrows: " + (card.LinkArrows == 0 ? "none" : string.Join(", ", Enumerable.Range(0, 8).Where(b => (card.LinkArrows & 1 << b) != 0).Select(b => ArrowNames[b])));
            return line;
        }
    }
}
