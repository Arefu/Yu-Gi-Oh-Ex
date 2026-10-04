namespace Types
{
    /// <summary>
    /// bin/CARD_Kana1_#.bin, CARD_Kana2_#.bin and CARD_Kana3_#.bin (one set per language, E F G I J R S): the first, second and third
    /// character of each card's reading, one UTF-16 char per INTERNAL id (10166 x 2 bytes, internal id 0 = no card). Together they are the
    /// card's first three "index" letters: kana of the reading in Japanese, letters of the name elsewhere ("IKS" ...).
    ///
    /// The exe loads only CARD_Kana1_# (Setup_CardPropTable, g_CardDataFiles +0xF0; for languages 5 and 6 it maps voiced kana to plain ones
    /// through word_140A51A20). Get_CardIndexInitialFromKonamiId (0x14076D480) gives a card's index initial (FULL_CARD_PROPS +0x1A) from it,
    /// but ONLY in the Japanese build (g_bIsJpVersion); the other builds use the first character of the card's name. Kana2 and Kana3 are
    /// never read. See docs/CardKana.md.
    /// </summary>
    public sealed class CardKanaTable
    {
        public static readonly char[] Languages = ['E', 'F', 'G', 'I', 'J', 'R', 'S'];

        /// <summary>The archive path of one of the three files (n = 1, 2 or 3).</summary>
        public static string KanaPath(int n, char language) => $@"bin\CARD_Kana{n}_{language}.bin";

        /// <summary>Per internal id: up to three characters (Kana1, Kana2, Kana3; a 0 char ends it).</summary>
        public List<string> Readings { get; } = [];

        public static CardKanaTable Parse(byte[] kana1, byte[] kana2, byte[] kana3)
        {
            if (kana1.Length % 2 != 0 || kana1.Length != kana2.Length || kana1.Length != kana3.Length)
                throw new InvalidDataException($"CARD_Kana1/2/3 must be the same even size (one char per card); they are {kana1.Length}, {kana2.Length} and {kana3.Length} bytes.");
            var table = new CardKanaTable();
            for (int offset = 0; offset < kana1.Length; offset += 2)
            {
                // kept exactly (a 0 in the middle too) so ToBytes gives the same bytes back
                char a = (char)BitConverter.ToUInt16(kana1, offset), b = (char)BitConverter.ToUInt16(kana2, offset), c = (char)BitConverter.ToUInt16(kana3, offset);
                table.Readings.Add(new string([a, b, c]).TrimEnd('\0'));
            }
            return table;
        }

        public (byte[] Kana1, byte[] Kana2, byte[] Kana3) ToBytes()
        {
            var files = new[] { new byte[Readings.Count * 2], new byte[Readings.Count * 2], new byte[Readings.Count * 2] };
            for (int i = 0; i < Readings.Count; i++)
            {
                string reading = Readings[i];
                for (int n = 0; n < 3; n++)
                    BitConverter.TryWriteBytes(files[n].AsSpan(i * 2), (ushort)(n < reading.Length ? reading[n] : '\0'));
            }
            return (files[0], files[1], files[2]);
        }

        /// <summary>
        /// The three letters a name gives: its first three characters as written, which is what the game's non-Japanese files hold (all but a
        /// few dozen cards; Japanese readings have to be typed).
        /// </summary>
        public static string FromName(string name)
        {
            // a Japanese name may start with ruby markup: $R<kanji>(<reading>) - the game skips to after the '('
            if (name.StartsWith("$R") && name.IndexOf('(') is int open and > 0)
                name = name[(open + 1)..];
            return name.Length <= 3 ? name : name[..3];
        }
    }
}
