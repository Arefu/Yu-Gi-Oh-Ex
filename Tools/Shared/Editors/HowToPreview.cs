using System.Drawing.Drawing2D;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The How to Play screen as the game shows it (RIX::ScreenHelpHowToPlay): the chapters on the left, the chosen chapter's topics
    /// (sub-topics with a "-" in front, as the game adds it), and the article - its heading, picture (main/howto_img/help_duelimg_NNN.png)
    /// and text with the game's markup applied (<see cref="HowToText"/>: @0-@G colours, @/ @| font). Drawn on a 1920 x 1080 frame and
    /// scaled to fit. The text, colours and structure are the game's; the panel positions are approximate (not traced from the screen's
    /// layout yet).
    /// </summary>
    public sealed class HowToPreview : Control
    {
        private List<HowToChapter> _chapters = [];
        private int _chapter = -1, _topic = -1;
        private Bitmap? _frame;

        public HowToPreview()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(18, 18, 24);
        }

        /// <summary>A picture by its number (1-255), or null.</summary>
        public Func<int, Image?> Picture { get; set; } = _ => null;

        public void Show(List<HowToChapter> chapters, int chapter, int topic)
        {
            _chapters = chapters;
            _chapter = chapter;
            _topic = topic;
            Rebuild();
        }

        public void Rebuild()
        {
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

        private static Color Colour(int index) =>
            Color.FromArgb(255, Color.FromArgb((int)HowToText.Colours[Math.Clamp(index, 0, HowToText.Colours.Length - 1)]));

        private Bitmap Render()
        {
            var frame = new Bitmap(1920, 1080);
            using var g = Graphics.FromImage(frame);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var background = new LinearGradientBrush(new Rectangle(0, 0, 1920, 1080), Color.FromArgb(14, 30, 58), Color.FromArgb(4, 8, 20), 90f))
                g.FillRectangle(background, 0, 0, 1920, 1080);

            using var titleFont = new Font("Segoe UI", 44, FontStyle.Bold, GraphicsUnit.Pixel);
            using var listFont = new Font("Segoe UI", 28, FontStyle.Regular, GraphicsUnit.Pixel);
            using var headingFont = new Font("Segoe UI", 36, FontStyle.Bold, GraphicsUnit.Pixel);
            using var bodyFont = new Font("Segoe UI", 27, FontStyle.Regular, GraphicsUnit.Pixel);
            using var altFont = new Font("Segoe UI", 27, FontStyle.Italic, GraphicsUnit.Pixel);

            TextRenderer.DrawText(g, "How to Play", titleFont, new Point(80, 40), Color.White);

            // Chapters
            var chapterPanel = new Rectangle(60, 130, 420, 880);
            Panel(g, chapterPanel);
            int y = chapterPanel.Top + 20;
            for (int i = 0; i < _chapters.Count && y < chapterPanel.Bottom - 40; i++, y += 58)
                ListItem(g, new Rectangle(chapterPanel.Left + 14, y, chapterPanel.Width - 28, 50), _chapters[i].Title, listFont, i == _chapter);

            // Topics of the chosen chapter
            var topicPanel = new Rectangle(500, 130, 500, 880);
            Panel(g, topicPanel);
            if (_chapter >= 0 && _chapter < _chapters.Count)
            {
                y = topicPanel.Top + 20;
                var topics = _chapters[_chapter].Topics;
                for (int i = 0; i < topics.Count && y < topicPanel.Bottom - 40; i++, y += 54)
                {
                    var topic = topics[i];
                    int indent = topic.IsSubTopic ? 28 : 0;
                    ListItem(g, new Rectangle(topicPanel.Left + 14 + indent, y, topicPanel.Width - 28 - indent, 48), (topic.IsSubTopic ? "-" : "") + topic.Title, listFont, i == _topic);
                }
            }

            // Article
            var article = new Rectangle(1020, 130, 840, 880);
            Panel(g, article);
            if (_chapter >= 0 && _chapter < _chapters.Count && _topic >= 0 && _topic < _chapters[_chapter].Topics.Count)
            {
                var topic = _chapters[_chapter].Topics[_topic];
                var inner = Rectangle.Inflate(article, -28, -24);
                TextRenderer.DrawText(g, (topic.IsSubTopic ? "-" : "") + topic.Title, headingFont, new Rectangle(inner.Left, inner.Top, inner.Width, 50), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                y = inner.Top + 62;
                using (var line = new Pen(Color.FromArgb(90, 150, 200, 255), 2))
                    g.DrawLine(line, inner.Left, y - 8, inner.Right, y - 8);

                if (topic.Picture != 0)
                {
                    var picture = Picture(topic.Picture);
                    if (picture != null)
                    {
                        float scale = Math.Min(1f, Math.Min((float)inner.Width / picture.Width, 360f / picture.Height));
                        int w = (int)(picture.Width * scale), h = (int)(picture.Height * scale);
                        g.DrawImage(picture, new Rectangle(inner.Left + (inner.Width - w) / 2, y + 6, w, h));
                        y += h + 18;
                    }
                    else
                    {
                        var missing = new Rectangle(inner.Left, y + 6, inner.Width, 60);
                        using (var hatch = new HatchBrush(HatchStyle.BackwardDiagonal, Color.FromArgb(90, 255, 90, 90), Color.Transparent))
                            g.FillRectangle(hatch, missing);
                        TextRenderer.DrawText(g, $"picture {topic.Picture}: help_duelimg_{topic.Picture:000}.png not found", listFont, missing, Color.White,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                        y += 80;
                    }
                }
                DrawBody(g, topic.Body, new Rectangle(inner.Left, y, inner.Width, inner.Bottom - y), bodyFont, altFont);
            }
            return frame;
        }

        private static void Panel(Graphics g, Rectangle r)
        {
            using var fill = new SolidBrush(Color.FromArgb(150, 6, 14, 30));
            using var edge = new Pen(Color.FromArgb(120, 110, 170, 230), 2);
            g.FillRectangle(fill, r);
            g.DrawRectangle(edge, r);
        }

        private static void ListItem(Graphics g, Rectangle r, string text, Font font, bool selected)
        {
            if (selected)
            {
                using var fill = new LinearGradientBrush(r, Color.FromArgb(200, 40, 120, 220), Color.FromArgb(60, 40, 120, 220), 0f);
                g.FillRectangle(fill, r);
            }
            TextRenderer.DrawText(g, text, font, Rectangle.Inflate(r, -8, 0), selected ? Color.White : Color.FromArgb(215, 225, 240),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        /// <summary>Word-wraps the body's coloured runs into the box (the game wraps on spaces; Japanese text wraps anywhere).</summary>
        private static void DrawBody(Graphics g, string body, Rectangle box, Font font, Font alt)
        {
            const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var runs = HowToText.Parse(body);
            bool hasRuby = runs.Any(r => r.Ruby != null);
            using var rubyFont = new Font(font.FontFamily, font.Size * 0.5f, FontStyle.Regular, GraphicsUnit.Pixel);
            int lineHeight = font.Height + (hasRuby ? rubyFont.Height + 2 : 6);
            int x = box.Left, y = box.Top + (hasRuby ? rubyFont.Height : 0);

            foreach (var run in runs)
            {
                if (run.Text == "\n")
                {
                    x = box.Left;
                    y += lineHeight;
                    continue;
                }
                var f = run.AltFont ? alt : font;
                var colour = Colour(run.Colour);
                if (run.Ruby != null)
                {
                    // ruby: the base stays together, the reading sits small and centred above it
                    int baseWidth = TextRenderer.MeasureText(g, run.Text, f, Size.Empty, Flags).Width;
                    int rubyWidth = TextRenderer.MeasureText(g, run.Ruby, rubyFont, Size.Empty, Flags).Width;
                    int width = Math.Max(baseWidth, rubyWidth);
                    if (x + width > box.Right && x > box.Left)
                    {
                        x = box.Left;
                        y += lineHeight;
                    }
                    if (y > box.Bottom - lineHeight)
                        return;
                    TextRenderer.DrawText(g, run.Ruby, rubyFont, new Point(x + (width - rubyWidth) / 2, y - rubyFont.Height), colour, Flags);
                    TextRenderer.DrawText(g, run.Text, f, new Point(x + (width - baseWidth) / 2, y), colour, Flags);
                    x += width;
                    continue;
                }
                // split into words, keeping the spaces with them; a word too long for a line is broken by characters
                foreach (string word in SplitWords(run.Text))
                {
                    if (y > box.Bottom - lineHeight)
                        return;
                    int width = TextRenderer.MeasureText(g, word, f, Size.Empty, Flags).Width;
                    if (x + width > box.Right && x > box.Left)
                    {
                        x = box.Left;
                        y += lineHeight;
                        if (word.Trim().Length == 0)
                            continue;
                    }
                    if (width > box.Width)
                    {
                        foreach (char c in word)
                        {
                            int cw = TextRenderer.MeasureText(g, c.ToString(), f, Size.Empty, Flags).Width;
                            if (x + cw > box.Right)
                            {
                                x = box.Left;
                                y += lineHeight;
                            }
                            TextRenderer.DrawText(g, c.ToString(), f, new Point(x, y), colour, Flags);
                            x += cw;
                        }
                        continue;
                    }
                    TextRenderer.DrawText(g, word, f, new Point(x, y), colour, Flags);
                    x += width;
                }
            }
        }

        private static IEnumerable<string> SplitWords(string text)
        {
            int start = 0;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == ' ')
                {
                    yield return text[start..(i + 1)];
                    start = i + 1;
                }
                else if (text[i] >= 0x3000)   // CJK: every character can start a new line
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
