using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;

namespace Wolf.Editors
{
    /// <summary>Everything printed on a card, in the game's own numbers (CARD_Prop.bin's kind, attribute, race; docs/CardRendering.md).</summary>
    public sealed record GameCard
    {
        public string Name { get; init; } = "";
        public int Kind { get; init; }
        public int Attribute { get; init; }
        public string Race { get; init; } = "";
        /// <summary>Level, Rank or Link rating (the kind says which).</summary>
        public int Level { get; init; }
        /// <summary>-1 = "?".</summary>
        public int Atk { get; init; }
        public int Def { get; init; }
        /// <summary>Link arrows: bit 0 top-left, 1 top, 2 top-right, 3 left, 4 right, 5 bottom-left, 6 bottom, 7 bottom-right.</summary>
        public int LinkArrows { get; init; }
        /// <summary>The Pendulum Scale: this game has one per card (CARD_Prop.bin's 4 bits at 23; both sides of the card show it).</summary>
        public int Scale { get; init; }
        /// <summary>Spell/Trap property: 0 Normal, 1 Counter, 2 Field, 3 Equip, 4 Continuous, 5 Quick-Play, 6 Ritual.</summary>
        public int Icon { get; init; }
        /// <summary>The card text as stored: a Pendulum monster's is "monster text\r\n[Pendulum Effect]\r\npendulum text".</summary>
        public string Text { get; init; } = "";
    }

    /// <summary>The pictures and fonts a card face is made of: from the open game data (the archive, or a loose file the game would load instead).</summary>
    public interface IGameCardAssets
    {
        Bitmap? Frame(string file);
        Bitmap? Icon(string name);
        GameFont? Font(string name);
    }

    /// <summary>
    /// One of the game's bitmap fonts (fontbin\&lt;name&gt;.fbin + .png, docs/CardRendering.md "Fonts"): white glyphs with alpha in an atlas,
    /// each with its size, its offset from the pen and the baseline, its advance and its place in the atlas (0..1).
    /// </summary>
    public sealed class GameFont
    {
        private readonly record struct Glyph(float Width, float Height, float Top, float Left, float Advance, RectangleF Source);

        private readonly Bitmap _atlas;
        private readonly Dictionary<char, Glyph> _glyphs = [];

        /// <summary>The pixel size the font was rendered at, and the distance between lines.</summary>
        public float Size { get; }
        public float LineHeight { get; }
        /// <summary>How far capitals reach above the baseline.</summary>
        public float CapHeight { get; private set; }

        private GameFont(Bitmap atlas, float size, float lineHeight)
        {
            _atlas = atlas;
            Size = size;
            LineHeight = lineHeight;
        }

        public static GameFont? Parse(byte[]? fbin, Bitmap? atlas)
        {
            if (fbin == null || atlas == null || fbin.Length < 64)
                return null;
            try
            {
                int at = Array.IndexOf(fbin, (byte)0, 8) + 1;   // two u32, then the source font's name
                float size = BitConverter.ToSingle(fbin, at), lineHeight = BitConverter.ToSingle(fbin, at + 16);
                at += 40;
                int count = BitConverter.ToInt32(fbin, at);
                at += 4;
                var font = new GameFont(atlas, size, lineHeight);
                for (int i = 0; i < count && at + 66 <= fbin.Length; i++, at += 66)
                {
                    // u16 char, then {u16 char, u16 pad, u16 w, u16 h, float w, h, top, left, advance, u0, v0, u1, v1, ...}
                    char ch = (char)BitConverter.ToUInt16(fbin, at);
                    int r = at + 2 + 8;
                    float F(int n) => BitConverter.ToSingle(fbin, r + n * 4);
                    var source = RectangleF.FromLTRB(F(5) * atlas.Width, F(6) * atlas.Height, F(7) * atlas.Width, F(8) * atlas.Height);
                    font._glyphs[ch] = new Glyph(F(0), F(1), F(2), F(3), F(4), source);
                }
                font.CapHeight = font._glyphs.TryGetValue('H', out var h) ? -h.Top : size * 0.7f;
                return font;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public float Measure(string text)
        {
            float width = 0;
            foreach (char ch in text)
                width += _glyphs.TryGetValue(ch, out var g) ? g.Advance : _glyphs.TryGetValue('?', out var q) ? q.Advance : Size / 2;
            return width;
        }

        /// <summary>Draws text with its baseline at y, from x, scaled (sx squeezes a name to its box).</summary>
        public void Draw(Graphics g, string text, float x, float baseline, Color colour, float sx = 1, float sy = 1)
        {
            using var tint = new ImageAttributes();
            tint.SetColorMatrix(new ColorMatrix(
            [
                [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, colour.A / 255f, 0],
                [colour.R / 255f, colour.G / 255f, colour.B / 255f, 0, 1],
            ]));
            foreach (char ch in text)
            {
                if (!_glyphs.TryGetValue(ch, out var glyph) && !_glyphs.TryGetValue('?', out glyph))
                    continue;
                if (glyph.Source.Width >= 1 && glyph.Source.Height >= 1)
                {
                    var dest = new RectangleF(x + glyph.Left * sx, baseline + glyph.Top * sy, glyph.Width * sx, glyph.Height * sy);
                    g.DrawImage(_atlas, [dest.Location, new PointF(dest.Right, dest.Top), new PointF(dest.Left, dest.Bottom)], glyph.Source, GraphicsUnit.Pixel, tint);
                }
                x += glyph.Advance * sx;
            }
        }

        /// <summary>Breaks text into lines no wider than width (on spaces; the stored text's own line breaks are kept).</summary>
        public List<string> Wrap(string text, float width)
        {
            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = new StringBuilder();
                foreach (string word in paragraph.Split(' '))
                {
                    string next = line.Length == 0 ? word : line + " " + word;
                    if (line.Length > 0 && Measure(next) > width)
                    {
                        lines.Add(line.ToString());
                        line.Clear().Append(word);
                    }
                    else
                        line.Clear().Append(next);
                }
                lines.Add(line.ToString());
            }
            return lines;
        }
    }

    /// <summary>
    /// Draws a card the way the game does (CardFace_Build, docs/CardRendering.md): on its 400 x 580 face, the artwork, the frame for the
    /// card's kind (Pendulum, Link and Xyz frames included), the name in the game's font (squeezed to fit), the attribute, level / rank
    /// stars or the Spell/Trap label with its property icon, the type line, the card text in the largest of the game's five text sizes that
    /// fits (the pendulum effect in its own box, with the scales), the line above ATK and "ATK/... DEF/..." or "LINK-n", and Link arrows.
    /// Everything comes from the game data, so the preview is the vanilla card, or whatever frames and fonts the data now holds.
    /// </summary>
    public static class GameCardPainter
    {
        public const float Aspect = 400f / 580f;

        // the game's frame index (KIND_TABLE) and its picture in duel\frame
        private static readonly Dictionary<int, string> FrameFiles = new()
        {
            [0] = "card_nomal", [1] = "card_kouka", [2] = "card_gisiki", [3] = "card_yugo", [7] = "card_mahou", [8] = "card_wana", [9] = "card_token",
            [10] = "card_sync", [11] = "card_sync", [12] = "card_xyz", [13] = "card_pendulum_n", [14] = "card_pendulum", [15] = "card_xyz_pendulum",
            [16] = "card_sync_pendulum", [17] = "card_fusion_pendulum", [18] = "card_link",
        };

        /// <summary>The frame a kind is drawn with (KIND_TABLE's first column, by the kinds' names).</summary>
        public static int FrameOf(int kind) => kind switch
        {
            13 => 7,
            14 => 8,
            10 => 9,
            42 or 43 => 18,
            25 or 44 => 13,                     // Pendulum Normal
            26 or 33 or 35 or 40 or 45 => 14,   // Pendulum Effect
            34 => 15,
            36 => 16,
            41 => 17,
            22 or 23 => 12,
            17 or 18 or 19 => 10,
            2 or 3 or 39 => 3,
            4 or 5 or 38 => 2,
            0 or 15 => 0,
            _ => 1,
        };

        public static bool IsPendulumFrame(int frame) => frame is >= 13 and <= 17;

        private static readonly string[] AttributeIcons = ["", "LIGHT", "DARK", "WATER", "FIRE", "EARTH", "WIND", "GOD"];
        private static readonly string[] PropertyIcons = ["", "", "FIELD", "EQUIP", "CONTINUOUS", "QUICK_PLAY", "RITUAL"];
        private static readonly string[] ArrowIcons = ["UL", "U", "UR", "L", "R", "DL", "D", "DR"];

        /// <summary>The largest card-shaped rectangle centred in r.</summary>
        public static Rectangle Fit(Rectangle r)
        {
            int width = Math.Min(r.Width, (int)(r.Height * Aspect));
            int height = Math.Min(r.Height, (int)(width / Aspect));
            return new Rectangle(r.X + (r.Width - width) / 2, r.Y + (r.Height - height) / 2, width, height);
        }

        /// <summary>Draws the card into r (card-shaped; see <see cref="Fit"/>).</summary>
        public static void Draw(Graphics g, IGameCardAssets assets, Rectangle r, GameCard card, Bitmap? art)
        {
            var state = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 400f, r.Height / 580f);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                DrawFace(g, assets, card, art);
            }
            finally
            {
                g.Restore(state);
            }
        }

        private static void DrawFace(Graphics g, IGameCardAssets assets, GameCard card, Bitmap? art)
        {
            int frame = FrameOf(card.Kind);
            bool spellTrap = frame is 7 or 8, link = frame == 18, xyz = frame is 12 or 15, pendulum = IsPendulumFrame(frame), monster = !spellTrap;
            using (var grey = new SolidBrush(Color.FromArgb(128, 128, 128)))
                g.FillRectangle(grey, 0, 0, 400, 580);

            // 1. the artwork, under the frame
            var artRect = pendulum ? new RectangleF(26, 104, 347, 444) : new RectangleF(48, 106, 304, 304);
            if (art != null)
                g.DrawImage(art, artRect);

            // 2. the frame
            if (assets.Frame(FrameFiles.GetValueOrDefault(frame, "card_kouka")) is { } frameImage)
                g.DrawImage(frameImage, 0, 0, 400, 580);

            // 3. the name, squeezed into 301 px; white on the dark frames
            Color nameColour = frame is 7 or 8 or 12 or 15 or 18 ? Color.White : Color.Black;
            if (assets.Font("FONT_ID_MATRIXCAPS_21") is { } nameFont)
            {
                float width = nameFont.Measure(card.Name);
                float sx = width > 301 ? 301 / width : 1;
                nameFont.Draw(g, card.Name, 31, 45.5f + nameFont.CapHeight / 2, nameColour, sx);
            }

            // 4. attribute (Spell / Trap: their own circle)
            string attribute = spellTrap ? (frame == 7 ? "MAGIC" : "TRAP") : card.Attribute is > 0 and < 8 ? AttributeIcons[card.Attribute] : "";
            if (attribute.Length > 0 && assets.Icon("ICON_ID_ATTR_L_" + attribute) is { } attributeIcon)
                g.DrawImage(attributeIcon, 353.5f - 18.5f, 45.5f - 18.5f, 37, 37);

            // 5. stars (level right to left from x 346; rank left to right from x 54), or the Spell / Trap label and property icon
            if (spellTrap)
                DrawSpellTrapLabel(g, assets, frame == 7, card.Icon);
            else if (!link && card.Level > 0)
            {
                string starIcon = xyz ? "ICON_ID_RANK" : "ICON_ID_LEVEL";
                if (assets.Icon(starIcon) is { } star)
                {
                    int step = !xyz && card.Level > 11 ? 27 : 28;
                    for (int i = 0; i < Math.Min(card.Level, 13); i++)
                    {
                        float x = xyz ? 54 + i * step : 346 - i * step;
                        g.DrawImage(star, x, 83 - 14, 28, 28);
                    }
                }
            }

            // 6. Link arrows: the ICON_ID_LINK_* sprites are laid out on a whole 400 x 580 card, each over its slot in the frame
            if (link)
                for (int bit = 0; bit < 8; bit++)
                    if ((card.LinkArrows & 1 << bit) != 0 && assets.Icon("ICON_ID_LINK_" + ArrowIcons[bit]) is { } arrow)
                        g.DrawImage(arrow, 0, 0, 400, 580);

            // 7. the text box: type line, card text; a Pendulum's own effect and scales
            string text = card.Text.Replace("\r\n", "\n"), pendulumText = "";
            int split = text.IndexOf("[Pendulum Effect]", StringComparison.OrdinalIgnoreCase);
            if (split >= 0)
            {
                pendulumText = text[(split + "[Pendulum Effect]".Length)..].Trim('\n', ' ');
                text = text[..split].Trim('\n', ' ');
            }
            float textTop = 438;
            if (monster && assets.Font("FONT_ID_STONESERIFBOLD_14") is { } typeFont)
            {
                string typeLine = "[" + string.Join("/", new[] { card.Race }.Concat(CardNames.KindName(card.Kind).Split(" / ")).Where(p => p.Length > 0)).ToUpperInvariant() + "]";
                float width = typeFont.Measure(typeLine);
                typeFont.Draw(g, typeLine, 32, 436 + typeFont.CapHeight + 2, Color.Black, width > 336 ? 336 / width : 1);
                textTop = 436 + typeFont.LineHeight + 2;
            }
            DrawText(g, assets, text, new RectangleF(32, textTop, 336, monster ? 530 - 2 - textTop : 109));
            if (pendulum)
            {
                if (pendulumText.Length > 0)
                    DrawText(g, assets, pendulumText, new RectangleF(62, 365, 278, 66));
                if (assets.Font("FONT_ID_CARD_ATKDEF_SCALE") is { } scaleFont)
                {
                    foreach (float centre in new[] { 42f, 357f })
                    {
                        string s = card.Scale.ToString();
                        scaleFont.Draw(g, s, centre - scaleFont.Measure(s) / 2, 411 + scaleFont.CapHeight / 2, Color.Black);
                    }
                }
            }

            // 8. the line above ATK, and ATK / DEF (or LINK-n), right-aligned at 367
            if (monster)
            {
                using (var line = new SolidBrush(Color.FromArgb(0xD0, 0x23, 0x18, 0x15)))
                    g.FillRectangle(line, 32, 530, 336, 1.5f);
                if (assets.Font("FONT_ID_CARD_ATKDEF") is { } atkFont)
                {
                    static string Stat(int value) => value < 0 ? "?" : value.ToString();
                    string stats = $"ATK/{Stat(card.Atk),4}  " + (link ? $"LINK-{card.Level}" : $"DEF/{Stat(card.Def),4}");
                    atkFont.Draw(g, stats, 367 - atkFont.Measure(stats), 544, Color.Black);
                }
            }
        }

        private static void DrawSpellTrapLabel(Graphics g, IGameCardAssets assets, bool spell, int icon)
        {
            if (assets.Font("FONT_ID_STONESERIFBOLD_16") is not { } font)
                return;
            string label = spell ? "[SPELL CARD" : "[TRAP CARD";
            string property = icon >= 0 && icon < PropertyIcons.Length ? PropertyIcons[icon] : "";
            float baseline = 83 + font.CapHeight / 2;
            if (property.Length > 0 && assets.Icon("ICON_ID_ICON_CARD_L_" + property) is { } propertyIcon)
            {
                font.Draw(g, label, 330 - font.Measure(label), baseline, Color.Black);
                g.DrawImage(propertyIcon, 343 - 10, 83 - 10, 20, 20);
                font.Draw(g, "]", 357, baseline, Color.Black);
            }
            else
            {
                string whole = label + "]";
                font.Draw(g, whole, 367 - font.Measure(whole), baseline, Color.Black);
            }
        }

        /// <summary>Card text in the largest of the game's five sizes (Matrix Book 18 .. 10) that fits; squeezed upright if even 10 doesn't.</summary>
        private static void DrawText(Graphics g, IGameCardAssets assets, string text, RectangleF box)
        {
            if (text.Length == 0 || box.Height <= 4)
                return;
            foreach (int size in new[] { 18, 16, 14, 12, 10 })
            {
                if (assets.Font("FONT_ID_MATRIXBOOK_" + size) is not { } font)
                    continue;
                var lines = font.Wrap(text, box.Width);
                float height = lines.Count * font.LineHeight;
                if (height <= box.Height || size == 10)
                {
                    float sy = height > box.Height ? box.Height / height : 1;
                    float baseline = box.Top + font.CapHeight * sy + 1;
                    foreach (string line in lines)
                    {
                        font.Draw(g, line, box.Left, baseline, Color.Black, 1, sy);
                        baseline += font.LineHeight * sy;
                    }
                    return;
                }
            }
        }
    }
}
