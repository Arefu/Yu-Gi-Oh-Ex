using System.Text;

namespace Types
{
    /// <summary>
    /// A font character set: font/charset_#.bin, a sorted list of distinct UTF-16 code units stored big-endian (2 bytes each, no header,
    /// no count). Each file lists the characters one font atlas was built to hold:
    ///
    ///   charset_L   188 chars   Latin: ASCII, Latin-1, a few Greek/Cyrillic/punctuation (the E/F/G/I/S languages)
    ///   charset_R   155 chars   kana only (hiragana, katakana, ・ ー): ruby / furigana text above Japanese
    ///   charset_T  2550 chars   Japanese text: L's set plus kana, ~2080 kanji and fullwidth forms
    ///   charset_U   665 chars   wide symbol set: Latin Extended-A, Greek, Cyrillic, kana, arrows, shapes
    ///
    /// The letter meanings are read off the contents, not the exe: YuGiOh.exe has no "charset" string (ASCII or UTF-16) and never loads
    /// these, so they are leftovers from the font build. Still useful to check which characters of new text the shipped fonts cover.
    /// </summary>
    public sealed class FontCharset
    {
        public static readonly char[] Names = ['L', 'R', 'T', 'U'];

        public const string GameFolder = "font";

        /// <summary>The characters, sorted ascending, no duplicates.</summary>
        public char[] Chars { get; private set; } = [];

        public int Count => Chars.Length;

        public static string FileName(char name) => $"charset_{char.ToUpperInvariant(name)}.bin";

        public static FontCharset Load(string fontFolder, char name) => Load(Path.Combine(fontFolder, FileName(name)));

        public static FontCharset Load(string path) => Parse(File.ReadAllBytes(path));

        public static FontCharset Parse(byte[] bytes)
        {
            if (bytes.Length % 2 != 0)
                throw new InvalidDataException("charset_#.bin should be a list of big-endian u16 code units.");
            var chars = new char[bytes.Length / 2];
            for (int i = 0; i < chars.Length; i++)
                chars[i] = (char)(bytes[2 * i] << 8 | bytes[2 * i + 1]);
            return new FontCharset { Chars = chars };
        }

        public static FontCharset FromChars(IEnumerable<char> chars) => new() { Chars = [.. chars.Distinct().Order()] };

        public byte[] ToBytes()
        {
            var bytes = new byte[Chars.Length * 2];
            for (int i = 0; i < Chars.Length; i++)
            {
                bytes[2 * i] = (byte)(Chars[i] >> 8);
                bytes[2 * i + 1] = (byte)Chars[i];
            }
            return bytes;
        }

        public void Save(string fontFolder, char name) => File.WriteAllBytes(Path.Combine(fontFolder, FileName(name)), ToBytes());

        /// <summary>Whether the list is sorted ascending with no duplicates (as all four shipped files are).</summary>
        public bool IsSorted()
        {
            for (int i = 1; i < Chars.Length; i++)
                if (Chars[i - 1] >= Chars[i])
                    return false;
            return true;
        }

        public bool Contains(char c) => Array.BinarySearch(Chars, c) >= 0;

        /// <summary>The distinct characters of <paramref name="text"/> this set lacks (line breaks ignored), in code order.</summary>
        public char[] Missing(string text) => [.. text.Where(c => c != '\r' && c != '\n' && !Contains(c)).Distinct().Order()];

        /// <summary>Adds characters, keeping the list sorted and distinct. Returns how many were new.</summary>
        public int Add(IEnumerable<char> chars)
        {
            int before = Chars.Length;
            Chars = [.. Chars.Concat(chars).Distinct().Order()];
            return Chars.Length - before;
        }

        public override string ToString() => new(Chars);

        /// <summary>One line per character: code, glyph, Unicode category (for dumps and diffs).</summary>
        public string Dump()
        {
            var sb = new StringBuilder();
            foreach (char c in Chars)
                sb.Append($"U+{(int)c:X4}\t{(char.IsControl(c) || char.IsWhiteSpace(c) ? ' ' : c)}\t{char.GetUnicodeCategory(c)}\n");
            return sb.ToString();
        }
    }
}
