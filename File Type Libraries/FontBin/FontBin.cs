using System.Globalization;
using System.Text;

namespace Types
{
    /// <summary>
    /// A bitmap font: fontbin/FONT_ID_*.fbin describes the glyphs of the atlas image next to it (fontbin/FONT_ID_*.png, 8-bit palette).
    /// YGO::TEXT::Font_Load (0x140761A80) reads both ("fontbin/%s.fbin", "fontbin/%s") for font ids 0-37, named by
    /// YGO::TEXT::Font_GetFileName (0x1408727D0) in the order of <see cref="GameFontNames"/>.
    /// Each table goes into a std::map keyed by character, so order doesn't matter to the game (a duplicate character overwrites the
    /// earlier one); the shipped files are sorted and this library keeps them that way. A character the font lacks is drawn as '_'
    /// (Font_FindGlyphOrUnderscore, 0x140761900). After loading, the game nudges a few glyphs itself: fonts 8-13 'f' OffsetY +1,
    /// 28/29 '[' ']' OffsetY -1, 34 't' OffsetY +1, 36 '1' Advance -2, and U+2007 gets the advance of '0'.
    ///
    /// Little-endian and packed (no alignment):
    ///
    ///   u32        Kind          font+0; 0 Latin, 1 YCJ (Japanese card text), 2 NOTO_J, 4 PD_88, 6 USERNAME (not seen read back)
    ///   u32        Reserved      font+4; always 0
    ///   char[]     Face          null-terminated ASCII, e.g. "font/Alps Normal"
    ///   f32[10]    Metrics       see <see cref="Metrics"/>
    ///   u32 + n*66 Glyphs        the font
    ///   u32 + n*66 AltGlyphs     the alternate font, switched on and off in text by the markup "@/" and "@|" (empty in most files), see <see cref="AltGlyphs"/>
    ///
    /// All 38 shipped files parse to the last byte, and <see cref="ToBytes"/> writes them back byte-for-byte.
    /// </summary>
    public sealed class FontBin
    {
        public const string GameFolder = "fontbin";

        /// <summary>The font ids 0-37 as YGO::TEXT::Font_GetFileName names them (the FONT_ID enum: 0 = PD_88, 37 = USERNAME).</summary>
        public static readonly string[] GameFontNames =
        [
            "FONT_ID_PD_88", "FONT_ID_PD_44", "FONT_ID_PD_32", "FONT_ID_PD_23", "FONT_ID_PD_20", "FONT_ID_PD_16", "FONT_ID_PD_14", "FONT_ID_PD_12",
            "FONT_ID_NOTO_23", "FONT_ID_NOTO_20", "FONT_ID_NOTO_17", "FONT_ID_NOTO_15", "FONT_ID_NOTO_13", "FONT_ID_NOTO_9",
            "FONT_ID_NOTO_J_44", "FONT_ID_NOTO_J_23", "FONT_ID_NOTO_J_20", "FONT_ID_NOTO_J_17", "FONT_ID_NOTO_J_15", "FONT_ID_NOTO_J_13", "FONT_ID_NOTO_J_9",
            "FONT_ID_YCJ_NAME", "FONT_ID_YCJ_KIND_MAGIC", "FONT_ID_YCJ_KIND_MONSTER", "FONT_ID_YCJ_BODY", "FONT_ID_YCJ_BODY_M1", "FONT_ID_YCJ_BODY_M2",
            "FONT_ID_MATRIXCAPS_21", "FONT_ID_STONESERIFBOLD_16", "FONT_ID_STONESERIFBOLD_14",
            "FONT_ID_MATRIXBOOK_18", "FONT_ID_MATRIXBOOK_16", "FONT_ID_MATRIXBOOK_14", "FONT_ID_MATRIXBOOK_12", "FONT_ID_MATRIXBOOK_10",
            "FONT_ID_CARD_ATKDEF", "FONT_ID_CARD_ATKDEF_SCALE", "FONT_ID_USERNAME",
        ];

        public const int GlyphSize = 66;

        public uint Kind { get; set; }

        public uint Reserved { get; set; }

        /// <summary>The source typeface the atlas was rendered from, e.g. "font/MatrixBook" (not loaded by the game as a file).</summary>
        public string Face { get; set; } = "";

        /// <summary>
        /// Ten floats, where Font_Load puts them in the font object and what reads them:
        ///   [0] +40 render size (NOTO_13 = 18, NOTO_J_44 = 60)   [1] +44   [2] +48   [3] +52 (the point size in the file name; not seen read)
        ///   [4] +4200 line height (times the style's line scale)   [5] +4204 descent: taken off the last line for the block's bottom
        ///   [6] +4208 ascent: the first baseline below the top (+4212 is a copy)   [7] +4216 height used to centre a block vertically
        ///   [8] +4256 ruby raise: how far ruby text ($R(...)) sits above its line   [9] +4260 ruby scale (0.45; 0.4 for NOTO_J)
        /// </summary>
        public float[] Metrics { get; set; } = new float[10];

        public float RenderSize => Metrics[0];

        public float LineHeight => Metrics[4];

        public float Descent => Metrics[5];

        public float Ascent => Metrics[6];

        public float CenterHeight => Metrics[7];

        public float RubyRaise => Metrics[8];

        public float RubyScale => Metrics[9];

        /// <summary>The glyphs, sorted by <see cref="FontGlyph.Char"/>.</summary>
        public List<FontGlyph> Glyphs { get; set; } = [];

        /// <summary>
        /// The alternate font (font+4184): text between "@/" and "@|" is drawn with these glyphs (Layout_BreakLines). A subset of
        /// <see cref="Glyphs"/> (MATRIXBOOK 350, NOTO 344, NOTO_J and YCJ the 155 kana of font/charset_R; empty in the others), drawn a few
        /// px wider. A character missing here is drawn as '_' even when <see cref="Glyphs"/> has it.
        /// </summary>
        public List<FontGlyph> AltGlyphs { get; set; } = [];

        public static string FileName(string fontId) => fontId + ".fbin";

        public static string AtlasName(string fontId) => fontId + ".png";

        public static FontBin Load(string fontbinFolder, string fontId) => Load(Path.Combine(fontbinFolder, FileName(fontId)));

        public static FontBin Load(string path) => Parse(File.ReadAllBytes(path));

        public static FontBin Parse(byte[] bytes)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            var font = new FontBin { Kind = reader.ReadUInt32(), Reserved = reader.ReadUInt32() };
            var face = new List<byte>();
            for (byte b; (b = reader.ReadByte()) != 0;)
                face.Add(b);
            font.Face = Encoding.ASCII.GetString([.. face]);
            for (int i = 0; i < font.Metrics.Length; i++)
                font.Metrics[i] = reader.ReadSingle();
            font.Glyphs = ReadGlyphs(reader);
            font.AltGlyphs = ReadGlyphs(reader);
            if (reader.BaseStream.Position != bytes.Length)
                throw new InvalidDataException($"fbin has {bytes.Length - reader.BaseStream.Position} bytes after the glyph tables.");
            return font;
        }

        private static List<FontGlyph> ReadGlyphs(BinaryReader reader)
        {
            uint count = reader.ReadUInt32();
            if (count * (long)GlyphSize > reader.BaseStream.Length - reader.BaseStream.Position)
                throw new InvalidDataException($"fbin glyph count {count} runs past the end of the file.");
            var glyphs = new List<FontGlyph>((int)count);
            for (int i = 0; i < count; i++)
                glyphs.Add(FontGlyph.Read(reader));
            return glyphs;
        }

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Kind);
            writer.Write(Reserved);
            writer.Write(Encoding.ASCII.GetBytes(Face));
            writer.Write((byte)0);
            foreach (float f in Metrics)
                writer.Write(f);
            foreach (var table in new[] { Glyphs, AltGlyphs })
            {
                writer.Write((uint)table.Count);
                foreach (var glyph in table)
                    glyph.Write(writer);
            }
            return stream.ToArray();
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        /// <summary>The glyph for a character, or null. Binary search, so <see cref="Glyphs"/> must stay sorted (the game's are).</summary>
        public FontGlyph? Find(char c) => Find(Glyphs, c);

        public FontGlyph? FindAlt(char c) => Find(AltGlyphs, c);

        private static FontGlyph? Find(List<FontGlyph> table, char c)
        {
            int lo = 0, hi = table.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (table[mid].Char == c)
                    return table[mid];
                if (table[mid].Char < c)
                    lo = mid + 1;
                else
                    hi = mid - 1;
            }
            return null;
        }

        /// <summary>The distinct characters of <paramref name="text"/> this font has no glyph for (line breaks ignored).</summary>
        public char[] Missing(string text) => [.. text.Where(c => c != '\r' && c != '\n' && Find(c) == null).Distinct().Order()];

        /// <summary>How wide <paramref name="text"/> draws on one line: the sum of the advances (missing characters count 0).</summary>
        public float MeasureWidth(string text) => text.Sum(c => Find(c)?.Advance ?? 0);

        /// <summary>One line per glyph (for dumps and diffs). Pass the atlas size to get pixel rectangles instead of UVs.</summary>
        public string Dump(int atlasWidth = 0, int atlasHeight = 0)
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;
            sb.Append(inv, $"Kind={Kind} Face={Face} Metrics=[{string.Join(", ", Metrics.Select(m => m.ToString(inv)))}]\n");
            foreach (var (name, table) in new[] { ("Glyphs", Glyphs), ("AltGlyphs", AltGlyphs) })
            {
                sb.Append(inv, $"{name} {table.Count}\n");
                foreach (var g in table)
                {
                    string glyph = char.IsControl(g.Char) || char.IsWhiteSpace(g.Char) ? " " : g.Char.ToString();
                    string alias = g.IsAlias ? $" -> U+{(int)g.SourceChar:X4}" : "";
                    string rect = atlasWidth > 0 && atlasHeight > 0
                        ? $"px {g.PixelRect(atlasWidth, atlasHeight)} outline px {g.OutlinePixelRect(atlasWidth, atlasHeight)}"
                        : $"uv ({g.U0:0.####},{g.V0:0.####})-({g.U1:0.####},{g.V1:0.####})";
                    sb.Append(inv, $"U+{(int)g.Char:X4} {glyph}{alias} size {g.Width}x{g.Height} offset ({g.OffsetX},{g.OffsetY}) advance {g.Advance} outline {g.OutlineWidth} {rect}\n");
                }
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// One 66-byte glyph record:
    ///
    ///   u16 Char, u16 SourceChar, u16 Reserved (0)       SourceChar != Char = an alias drawn with another letter's image (Greek Α -> A)
    ///   u16 PixelWidth, u16 PixelHeight                  the same numbers as Width/Height below
    ///   f32 Width, Height, OffsetY, OffsetX, Advance     pen-relative: OffsetY is the top edge from the baseline (negative = up)
    ///   f32 U0, V0, U1, V1                               the glyph in the atlas (0..1)
    ///   f32 OU0, OV0, OU1, OV1                           the outlined glyph, OutlineWidth px bigger on every side;
    ///                                                    with OutlineWidth 0 it is a dummy (-1, -1)..(+1, +1) texel
    ///   u32 OutlineWidth                                 0 (no outline: whole fonts, or blank glyphs in some), 1, or 3 (PD_44, PD_88)
    ///
    /// In memory (the IDB's FontGlyph) the record is the 64 bytes after Char. Text styles with flag 0x400 draw the outline first: the glyph
    /// rect grown by OutlineWidth px on every side, textured from the Outline UVs (Layout_Build, 0x140763E40). Height 0 = no quad.
    /// </summary>
    public sealed class FontGlyph
    {
        public char Char { get; set; }

        public char SourceChar { get; set; }

        public ushort Reserved { get; set; }

        public ushort PixelWidth { get; set; }

        public ushort PixelHeight { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }

        public float OffsetY { get; set; }

        public float OffsetX { get; set; }

        public float Advance { get; set; }

        public float U0 { get; set; }

        public float V0 { get; set; }

        public float U1 { get; set; }

        public float V1 { get; set; }

        public float OutlineU0 { get; set; }

        public float OutlineV0 { get; set; }

        public float OutlineU1 { get; set; }

        public float OutlineV1 { get; set; }

        public uint OutlineWidth { get; set; }

        public bool IsAlias => Char != SourceChar;

        internal static FontGlyph Read(BinaryReader r) => new()
        {
            Char = (char)r.ReadUInt16(),
            SourceChar = (char)r.ReadUInt16(),
            Reserved = r.ReadUInt16(),
            PixelWidth = r.ReadUInt16(),
            PixelHeight = r.ReadUInt16(),
            Width = r.ReadSingle(),
            Height = r.ReadSingle(),
            OffsetY = r.ReadSingle(),
            OffsetX = r.ReadSingle(),
            Advance = r.ReadSingle(),
            U0 = r.ReadSingle(),
            V0 = r.ReadSingle(),
            U1 = r.ReadSingle(),
            V1 = r.ReadSingle(),
            OutlineU0 = r.ReadSingle(),
            OutlineV0 = r.ReadSingle(),
            OutlineU1 = r.ReadSingle(),
            OutlineV1 = r.ReadSingle(),
            OutlineWidth = r.ReadUInt32(),
        };

        internal void Write(BinaryWriter w)
        {
            w.Write((ushort)Char);
            w.Write((ushort)SourceChar);
            w.Write(Reserved);
            w.Write(PixelWidth);
            w.Write(PixelHeight);
            foreach (float f in new[] { Width, Height, OffsetY, OffsetX, Advance, U0, V0, U1, V1, OutlineU0, OutlineV0, OutlineU1, OutlineV1 })
                w.Write(f);
            w.Write(OutlineWidth);
        }

        /// <summary>The glyph's rectangle in an atlas of the given size, as (x, y, width, height) in pixels.</summary>
        public (int X, int Y, int Width, int Height) PixelRect(int atlasWidth, int atlasHeight) => ToPixels(U0, V0, U1, V1, atlasWidth, atlasHeight);

        public (int X, int Y, int Width, int Height) OutlinePixelRect(int atlasWidth, int atlasHeight) =>
            ToPixels(OutlineU0, OutlineV0, OutlineU1, OutlineV1, atlasWidth, atlasHeight);

        private static (int, int, int, int) ToPixels(float u0, float v0, float u1, float v1, int w, int h)
        {
            int x = (int)MathF.Round(u0 * w), y = (int)MathF.Round(v0 * h);
            return (x, y, (int)MathF.Round(u1 * w) - x, (int)MathF.Round(v1 * h) - y);
        }
    }
}
