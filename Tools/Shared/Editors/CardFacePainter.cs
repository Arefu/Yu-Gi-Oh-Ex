using System.Drawing.Drawing2D;

namespace Wolf.Editors
{
    /// <summary>Which of the game's card frames (duel\frame\card_*.png) a card is drawn with.</summary>
    public enum CardFrame { Normal, Effect, Ritual, Fusion, Synchro, Xyz, Link, Token, Spell, Trap, Unknown }

    /// <summary>What a card face shows: the frame, and for monsters the attribute, stars and ATK / DEF (Link: the rating in Level).</summary>
    public sealed record CardFace(CardFrame Frame, string Attribute, int Level, int Atk, int Def)
    {
        public bool IsMonster => Frame is not (CardFrame.Spell or CardFrame.Trap or CardFrame.Unknown);
    }

    /// <summary>Where the painter gets the game's frames and UI icons (pdui\STEAM_icons sprites such as "ICON_ID_LEVEL").</summary>
    public interface ICardFaceArt
    {
        Bitmap? Frame(CardFrame frame);

        Bitmap? Icon(string name);
    }

    /// <summary>The names the game's card numbers stand for (CARD_Prop.bin's kind, attribute and type; File Type Libraries\CARD_Props).</summary>
    public static class CardNames
    {
        public static CardFrame FrameOf(int kind) => kind switch
        {
            0 or 15 or 44 => CardFrame.Normal,
            2 or 3 or 39 or 41 => CardFrame.Fusion,
            4 or 5 or 38 => CardFrame.Ritual,
            10 => CardFrame.Token,
            13 => CardFrame.Spell,
            14 => CardFrame.Trap,
            17 or 18 or 19 or 36 => CardFrame.Synchro,
            22 or 23 or 34 => CardFrame.Xyz,
            42 or 43 => CardFrame.Link,
            _ => CardFrame.Effect,
        };

        /// <summary>The frame's picture: duel\frame\&lt;name&gt;.png.</summary>
        public static string FrameFile(CardFrame frame) => frame switch
        {
            CardFrame.Normal => "card_nomal",
            CardFrame.Ritual => "card_gisiki",
            CardFrame.Fusion => "card_yugo",
            CardFrame.Synchro => "card_sync",
            CardFrame.Xyz => "card_xyz",
            CardFrame.Link => "card_link",
            CardFrame.Token => "card_token",
            CardFrame.Spell => "card_mahou",
            CardFrame.Trap => "card_wana",
            _ => "card_kouka",
        };

        public static string KindName(int kind) => kind switch
        {
            0 => "Normal", 1 => "Effect", 2 => "Fusion", 3 => "Fusion / Effect", 4 => "Ritual", 5 => "Ritual / Effect", 6 => "Toon",
            7 => "Spirit", 8 => "Union", 9 => "Gemini", 10 => "Token", 13 => "Spell", 14 => "Trap", 15 => "Tuner / Normal",
            16 => "Tuner / Effect", 17 => "Synchro", 18 => "Synchro / Effect", 19 => "Synchro / Tuner / Effect", 22 => "Xyz",
            23 => "Xyz / Effect", 24 => "Flip / Effect", 25 => "Pendulum", 26 => "Pendulum / Effect", 27 => "Effect", 28 => "Toon / Effect",
            29 => "Spirit / Effect", 30 => "Tuner", 32 => "Tuner / Flip / Effect", 33 => "Pendulum / Tuner / Effect",
            34 => "Xyz / Pendulum / Effect", 35 => "Pendulum / Flip / Effect", 36 => "Synchro / Pendulum / Effect",
            37 => "Union / Tuner / Effect", 38 => "Ritual / Spirit / Effect", 39 => "Fusion / Tuner", 40 => "Pendulum / Effect",
            41 => "Fusion / Pendulum / Effect", 42 => "Link", 43 => "Link / Effect", 44 => "Pendulum / Tuner / Normal",
            45 => "Pendulum / Spirit / Effect", _ => $"kind {kind}",
        };

        public static string AttributeName(int attribute) => attribute switch
        {
            1 => "Light", 2 => "Dark", 3 => "Water", 4 => "Fire", 5 => "Earth", 6 => "Wind", 7 => "Divine", 8 => "Spell", 9 => "Trap", _ => "",
        };

        private static readonly string[] Races =
        [
            "", "Dragon", "Zombie", "Fiend", "Pyro", "Sea Serpent", "Rock", "Machine", "Fish", "Dinosaur", "Insect", "Beast", "Beast-Warrior",
            "Plant", "Aqua", "Warrior", "Winged Beast", "Fairy", "Spellcaster", "Thunder", "Reptile", "Psychic", "Wyrm", "Cyberse", "Divine-Beast",
        ];

        public static string RaceName(int race) => race < Races.Length ? Races[race] : race == 30 ? "Spell" : race == 31 ? "Trap" : $"race {race}";

        /// <summary>The Spell / Trap property icons (CARD_Prop.bin's icon field).</summary>
        public static readonly string[] Icons = ["Normal", "Counter", "Field", "Equip", "Continuous", "Quick-Play", "Ritual"];
    }

    /// <summary>
    /// Draws a card at any size, laid out like Yu-Gi-Oh-AnimeCards does in game (docs/CardRendering.md): the game's frame for the card's
    /// kind (the Mods\Anime Frames versions when installed), the illustration filling the art window, the attribute icon in the frame's
    /// circle, level/rank stars centred in the band under the art, ATK and DEF centred in the two boxes. No name or text on the face.
    /// Used by DuelIt and WolfX's Card Manager.
    /// </summary>
    public static class CardFacePainter
    {
        public const float Aspect = 400f / 580f;   // the game's card face is 400 x 580

        // Positions on the 400 x 580 face.
        private static readonly RectangleF ArtWindow = new(11, 11, 378, 413);
        private static readonly PointF AttributeCentre = new(346, 469.5f), LinkAttributeCentre = new(198.5f, 469);
        private const float AttributeSize = 48, StarSize = 28, StarsCentreX = 200, StarsY = 465.5f, StarsMaxWidth = 240;
        private static readonly PointF AtkCentre = new(107.5f, 526.5f), DefCentre = new(291.5f, 526.5f);

        public static Color FrameColour(CardFrame frame) => frame switch
        {
            CardFrame.Normal => Color.FromArgb(214, 180, 100),
            CardFrame.Effect => Color.FromArgb(196, 110, 58),
            CardFrame.Ritual => Color.FromArgb(92, 132, 204),
            CardFrame.Fusion => Color.FromArgb(142, 88, 170),
            CardFrame.Synchro => Color.FromArgb(222, 222, 226),
            CardFrame.Xyz => Color.FromArgb(46, 46, 52),
            CardFrame.Link => Color.FromArgb(40, 96, 170),
            CardFrame.Token => Color.FromArgb(150, 150, 150),
            CardFrame.Spell => Color.FromArgb(32, 148, 128),
            CardFrame.Trap => Color.FromArgb(176, 60, 128),
            _ => Color.FromArgb(62, 74, 104),
        };

        /// <summary>The largest card-shaped rectangle centred in r.</summary>
        public static Rectangle Fit(Rectangle r)
        {
            int width = Math.Min(r.Width, (int)(r.Height * Aspect));
            int height = Math.Min(r.Height, (int)(width / Aspect));
            return new Rectangle(r.X + (r.Width - width) / 2, r.Y + (r.Height - height) / 2, width, height);
        }

        private static string AttributeIcon(string attribute) => attribute.ToUpperInvariant() switch
        {
            "LIGHT" => "ICON_ID_ATTR_LIGHT",
            "DARK" => "ICON_ID_ATTR_DARK",
            "WATER" => "ICON_ID_ATTR_WATER",
            "FIRE" => "ICON_ID_ATTR_FIRE",
            "EARTH" => "ICON_ID_ATTR_EARTH",
            "WIND" => "ICON_ID_ATTR_WIND",
            "DIVINE" => "ICON_ID_ATTR_GOD",
            _ => "",
        };

        /// <summary>Draws a card into r (card-shaped). label is shown in the art window when there is no picture.</summary>
        public static void Draw(Graphics g, ICardFaceArt source, Rectangle r, CardFace? face, Bitmap? art, string label)
        {
            var frame = face?.Frame ?? CardFrame.Unknown;
            float s = r.Width / 400f;
            RectangleF Face(RectangleF f) => new(r.X + f.X * s, r.Y + f.Y * s, f.Width * s, f.Height * s);
            RectangleF Centred(PointF c, float w, float h) => Face(new RectangleF(c.X - w / 2, c.Y - h / 2, w, h));

            g.InterpolationMode = r.Width > 150 ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Frame, then the illustration in the art window (square art, cropped to the window's shape).
            if (source.Frame(frame) is { } frameImage)
                g.DrawImage(frameImage, r);
            else
                using (var fill = new SolidBrush(FrameColour(frame)))
                    g.FillRectangle(fill, r);

            var window = Face(ArtWindow);
            if (art != null)
            {
                float scale = Math.Max(window.Width / art.Width, window.Height / art.Height);
                float cropW = window.Width / scale, cropH = window.Height / scale;
                g.DrawImage(art, window, new RectangleF((art.Width - cropW) / 2, (art.Height - cropH) / 2, cropW, cropH), GraphicsUnit.Pixel);
            }
            else
            {
                using (var dark = new SolidBrush(Color.FromArgb(40, 44, 56)))
                    g.FillRectangle(dark, window);
                using var labelFont = new Font("Segoe UI", Math.Max(6f, r.Width / 11f), GraphicsUnit.Pixel);
                TextRenderer.DrawText(g, label, labelFont, Rectangle.Round(window), Color.Gainsboro,
                    TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }

            if (face == null || !face.IsMonster)
                return;   // Spell/Trap frames carry their symbol themselves

            // Attribute in the frame's circle.
            if (source.Icon(AttributeIcon(face.Attribute)) is { } attribute)
                g.DrawImage(attribute, Centred(frame == CardFrame.Link ? LinkAttributeCentre : AttributeCentre, AttributeSize, AttributeSize));

            // Stars: one row centred in the band, shrunk to fit. Xyz shows rank stars, Link none.
            if (frame != CardFrame.Link && face.Level > 0 && r.Width >= 90 && source.Icon(frame == CardFrame.Xyz ? "ICON_ID_RANK" : "ICON_ID_LEVEL") is { } star)
            {
                float step = face.Level > 11 ? 27 : 28;
                float width = StarSize + step * (face.Level - 1);
                float shrink = Math.Min(1f, StarsMaxWidth / width);
                float size = StarSize * shrink, x0 = StarsCentreX - width * shrink / 2 + size / 2;
                for (int i = 0; i < face.Level; i++)
                    g.DrawImage(star, Centred(new PointF(x0 + i * step * shrink, StarsY), size, size));
            }

            // ATK / DEF (Link: ATK and the Link rating). At board sizes the numbers get a readable minimum size and a dark backing.
            string atk = Stat(face.Atk), def = frame == CardFrame.Link ? $"LINK-{face.Level}" : Stat(face.Def);
            float fontPixels = 34 * s;
            bool small = fontPixels < 9;
            using var font = new Font("Segoe UI", Math.Max(small ? 8f : 7f, fontPixels), FontStyle.Bold, GraphicsUnit.Pixel);
            if (small)
            {
                var strip = new Rectangle(r.X, r.Bottom - font.Height - 2, r.Width, font.Height + 2);
                using (var back = new SolidBrush(Color.FromArgb(200, 10, 10, 14)))
                    g.FillRectangle(back, strip);
                TextRenderer.DrawText(g, $"{atk}/{def}", font, strip, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                return;
            }
            var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(g, atk, font, Rectangle.Round(Centred(AtkCentre, 150, 50)), Color.Black, flags);
            TextRenderer.DrawText(g, def, font, Rectangle.Round(Centred(DefCentre, 150, 50)), Color.Black, flags);
        }

        private static string Stat(int value) => value < 0 ? "?" : value.ToString();
    }
}
