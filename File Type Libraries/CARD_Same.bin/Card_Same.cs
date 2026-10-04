namespace Types
{
    /// <summary>How a CARD_Same row treats the card's name (the row's third u16).</summary>
    public enum SameNameMode : ushort
    {
        /// <summary>
        /// 0: the name is always the target's ("This card's name is always treated as ..."): Harpie Lady 1/2/3 -> Harpie Lady, A Legendary
        /// Ocean -> Umi, Fusion Substitute -> Polymerization, the Sheep/Ojama Token variants. The game gives the card the target's
        /// identity id, so every "same name" test in a duel sees the target.
        /// </summary>
        Always = 0,
        /// <summary>
        /// 0x100: the name becomes the target's only while an effect says so (Cyber Dragon Zwei, Harpie Channeler, Blue-Eyes Alternative:
        /// "while on the field / in the GY"). The card keeps its own identity id; the target goes in the second id only.
        /// </summary>
        WhileEffect = 0x100,
    }

    /// <summary>One row: <see cref="Card"/>'s name is treated as <see cref="Target"/>'s.</summary>
    public readonly record struct SameNameRow(int Card, int Target, SameNameMode Mode);

    /// <summary>
    /// bin/CARD_Same.bin: cards whose name is treated as another card's. No header: rows of three u16 (card Konami id, target Konami id,
    /// mode 0 or 0x100), 6 bytes each, sorted by card. The game's file has 72 rows.
    ///
    /// The exe (Setup_CardPropTable 0x14076BFC6) loads it before CARD_Prop.bin and, for each card id below 14969, binary searches it
    /// (List_BinarySearchRowByKonamiId, so the rows MUST stay sorted by card) and fills the card props' +0x2C / +0x2E words:
    /// +0x2E (the "same" id) = target; +0x2C (the identity id) = target for <see cref="SameNameMode.Always"/>, the card itself for
    /// <see cref="SameNameMode.WhileEffect"/>. A card with no row gets its own id in both. Card_IsSameName (0x14076D6E0, called 300+
    /// times by the duel engine: fusion materials, "a card with the same name", Xyz...) compares the +0x2C ids. Setup_FullCardProps copies
    /// them to FULL_CARD_PROPS +0x6C / +0x70. Rows for custom cards (14969+) are never read; their same-name card is "sameName" in
    /// cards.json, applied by Yu-Gi-Oh-MoreCards. See docs/CardSame.md.
    /// </summary>
    public sealed class CardSameTable
    {
        public const string GamePath = @"bin\CARD_Same.bin";
        public const int EntrySize = 6;
        /// <summary>The exe only reads rows for card ids below this (its card tables end at 14968).</summary>
        public const int CardLimit = 14969;

        /// <summary>Every row, sorted by card.</summary>
        public List<SameNameRow> Rows { get; } = [];

        public static string ModeName(SameNameMode mode) => mode switch
        {
            SameNameMode.Always => "always",
            SameNameMode.WhileEffect => "while an effect says so",
            _ => $"0x{(ushort)mode:X}",
        };

        public static CardSameTable Load(string path) => Parse(File.ReadAllBytes(path));

        public static CardSameTable Parse(byte[] data)
        {
            if (data.Length % EntrySize != 0)
                throw new InvalidDataException($"CARD_Same.bin is a list of 6-byte rows; this one is {data.Length} bytes.");
            var table = new CardSameTable();
            for (int offset = 0; offset < data.Length; offset += EntrySize)
                table.Rows.Add(new SameNameRow(BitConverter.ToUInt16(data, offset), BitConverter.ToUInt16(data, offset + 2),
                    (SameNameMode)BitConverter.ToUInt16(data, offset + 4)));
            return table;
        }

        public byte[] ToBytes()
        {
            var data = new byte[Rows.Count * EntrySize];
            for (int i = 0; i < Rows.Count; i++)
            {
                BitConverter.TryWriteBytes(data.AsSpan(i * EntrySize), (ushort)Rows[i].Card);
                BitConverter.TryWriteBytes(data.AsSpan(i * EntrySize + 2), (ushort)Rows[i].Target);
                BitConverter.TryWriteBytes(data.AsSpan(i * EntrySize + 4), (ushort)Rows[i].Mode);
            }
            return data;
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        public CardSameTable Clone()
        {
            var copy = new CardSameTable();
            copy.Rows.AddRange(Rows);
            return copy;
        }

        /// <summary>The card's row, or null when its name is its own.</summary>
        public SameNameRow? Find(int card)
        {
            int index = Rows.FindIndex(row => row.Card == card);
            return index >= 0 ? Rows[index] : null;
        }

        /// <summary>The cards whose name is treated as <paramref name="target"/>'s.</summary>
        public List<SameNameRow> TreatedAs(int target) => Rows.Where(row => row.Target == target).ToList();

        /// <summary>Gives the card this row (replacing its old one), keeping the rows sorted by card.</summary>
        public void Set(int card, int target, SameNameMode mode)
        {
            if (card is < 1 or > ushort.MaxValue || target is < 1 or > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(card), "Card ids are 1-65535 (u16).");
            Remove(card);
            int at = Rows.FindIndex(row => row.Card > card);
            Rows.Insert(at < 0 ? Rows.Count : at, new SameNameRow(card, target, mode));
        }

        /// <summary>The card's name is its own again. Returns false if it had no row.</summary>
        public bool Remove(int card) => Rows.RemoveAll(row => row.Card == card) > 0;

        /// <summary>Things that look wrong: not sorted (the game binary searches), a card twice, itself as target, a chain, an unknown mode.</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            var cards = Rows.Select(row => row.Card).ToHashSet();
            for (int i = 0; i < Rows.Count; i++)
            {
                var row = Rows[i];
                if (i > 0 && row.Card <= Rows[i - 1].Card)
                    problems.Add(row.Card == Rows[i - 1].Card ? $"{row.Card} has two rows (the game finds only one)." :
                        $"Row {i} ({row.Card}) is out of order: the game binary searches the file, so it would miss cards.");
                if (row.Card == row.Target)
                    problems.Add($"{row.Card} is treated as itself.");
                if (cards.Contains(row.Target))
                    problems.Add($"{row.Card} -> {row.Target}, which has its own row: the game doesn't follow chains.");
                if (row.Card >= CardLimit)
                    problems.Add($"{row.Card} is a custom id: the game never reads its row (use \"sameName\" in cards.json).");
                if (row.Mode is not (SameNameMode.Always or SameNameMode.WhileEffect))
                    problems.Add($"{row.Card}: mode 0x{(ushort)row.Mode:X} is unknown (the game only tests bit 0x100).");
            }
            return problems;
        }
    }
}
