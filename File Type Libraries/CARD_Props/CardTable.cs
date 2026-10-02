using System.Buffers.Binary;
using System.Text;

namespace Types
{
    /// <summary>One card's stats: a record of bin\CARD_Prop.bin (the index in the file is the card's internal id).</summary>
    public sealed class CardRecord
    {
        /// <summary>ATK / DEF the game shows as "?".</summary>
        public const int Unknown = -1;

        public int KonamiId { get; set; }
        public int Atk { get; set; }
        public int Def { get; set; }

        /// <summary>
        /// A Link monster's arrows: it has no DEF, so the DEF bits hold the arrow mask (bit 0 top-left, 1 top, 2 top-right, 3 left, 4 right,
        /// 5 bottom-left, 6 bottom, 7 bottom-right; the order Yu-Gi-Oh-MoreCards' "linkmarkers" uses).
        /// </summary>
        public int LinkArrows
        {
            get => Def < 0 ? 0 : Def / 10;
            set => Def = (value & 0xFF) * 10;
        }
        public CARDS_INFO.CARD_Kind Kind { get; set; }
        public CARDS_INFO.CARD_Attribute Attribute { get; set; }
        public int Level { get; set; }
        /// <summary>Spell / Trap icon (0 normal, 1 counter, 2 field, 3 equip, 4 continuous, 5 quick-play, 6 ritual).</summary>
        public int Icon { get; set; }
        public CARDS_INFO.CARD_Type Type { get; set; }
        public int ScaleLeft { get; set; }
        public int ScaleRight { get; set; }
        /// <summary>Bit 0 of the second word: set on most cards; its meaning isn't known, it is kept as it is.</summary>
        public bool Flag { get; set; }
        /// <summary>Bit 31 of the second word, kept as it is.</summary>
        public bool HighBit { get; set; }

        public CardRecord Clone() => (CardRecord)MemberwiseClone();

        public bool SameAs(CardRecord other) => Encode().Equals(other.Encode());

        public (uint First, uint Second) Encode()
        {
            static uint Stat(int value) => value < 0 ? 511u : (uint)Math.Clamp(value / 10, 0, 510);
            uint first = (uint)KonamiId & 0x3FFF | Stat(Atk) << 14 | Stat(Def) << 23;
            uint second = (Flag ? 1u : 0) | ((uint)Kind & 63) << 1 | ((uint)Attribute & 15) << 7 | ((uint)Level & 15) << 11 | ((uint)Icon & 7) << 15 |
                          ((uint)Type & 31) << 18 | ((uint)ScaleLeft & 15) << 23 | ((uint)ScaleRight & 15) << 27 | (HighBit ? 1u << 31 : 0);
            return (first, second);
        }

        public static CardRecord Decode(uint first, uint second)
        {
            static int Stat(uint value) => value == 511 ? Unknown : (int)value * 10;
            return new CardRecord
            {
                KonamiId = (int)(first & 0x3FFF),
                Atk = Stat(first >> 14 & 511),
                Def = Stat(first >> 23 & 511),
                Flag = (second & 1) != 0,
                Kind = (CARDS_INFO.CARD_Kind)(second >> 1 & 63),
                Attribute = (CARDS_INFO.CARD_Attribute)(second >> 7 & 15),
                Level = (int)(second >> 11 & 15),
                Icon = (int)(second >> 15 & 7),
                Type = (CARDS_INFO.CARD_Type)(second >> 18 & 31),
                ScaleLeft = (int)(second >> 23 & 15),
                ScaleRight = (int)(second >> 27 & 15),
                HighBit = (second & 1u << 31) != 0,
            };
        }

        public bool IsSpellOrTrap => Kind is CARDS_INFO.CARD_Kind.Spell or CARDS_INFO.CARD_Kind.Trap;
    }

    /// <summary>bin\CARD_Prop.bin: every card's stats, 8 bytes per internal id (two little-endian words, see <see cref="CardRecord.Encode"/>).</summary>
    public sealed class CardPropTable
    {
        public const string GamePath = @"bin\CARD_Prop.bin";

        public List<CardRecord> Records { get; } = [];

        public static CardPropTable Parse(byte[] data)
        {
            var table = new CardPropTable();
            for (int at = 0; at + 8 <= data.Length; at += 8)
                table.Records.Add(CardRecord.Decode(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at)), BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 4))));
            return table;
        }

        public byte[] ToBytes()
        {
            var data = new byte[Records.Count * 8];
            for (int i = 0; i < Records.Count; i++)
            {
                var (first, second) = Records[i].Encode();
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 8), first);
                BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 8 + 4), second);
            }
            return data;
        }

        public CardPropTable Clone()
        {
            var table = new CardPropTable();
            table.Records.AddRange(Records.Select(r => r.Clone()));
            return table;
        }
    }

    /// <summary>
    /// A language's card names and texts: bin\CARD_Indx_&lt;L&gt;.bin (per internal id a u32 offset into the names and one into the texts, then
    /// one more pair: the ends of the two files) and bin\CARD_Name_&lt;L&gt;.bin / CARD_Desc_&lt;L&gt;.bin (4 zero bytes, then UTF-16LE strings, each
    /// with its 0 and padded with zeros to a multiple of 4 bytes).
    /// </summary>
    public sealed class CardTextTable
    {
        public static readonly char[] Languages = ['E', 'F', 'G', 'I', 'J', 'R', 'S'];

        public static string IndxPath(char language) => $@"bin\CARD_Indx_{char.ToUpperInvariant(language)}.bin";
        public static string NamePath(char language) => $@"bin\CARD_Name_{char.ToUpperInvariant(language)}.bin";
        public static string DescPath(char language) => $@"bin\CARD_Desc_{char.ToUpperInvariant(language)}.bin";

        /// <summary>Per internal id.</summary>
        public List<string> Names { get; } = [];

        /// <summary>Per internal id.</summary>
        public List<string> Descs { get; } = [];

        private static string ReadString(byte[] data, int offset)
        {
            if (offset < 0 || offset >= data.Length)
                return "";
            int end = offset;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0))
                end += 2;
            return Encoding.Unicode.GetString(data, offset, end - offset);
        }

        public static CardTextTable Parse(byte[] indx, byte[] names, byte[] descs)
        {
            var table = new CardTextTable();
            int count = indx.Length / 8 - 1;
            for (int i = 0; i < count; i++)
            {
                table.Names.Add(ReadString(names, (int)BinaryPrimitives.ReadUInt32LittleEndian(indx.AsSpan(i * 8))));
                table.Descs.Add(ReadString(descs, (int)BinaryPrimitives.ReadUInt32LittleEndian(indx.AsSpan(i * 8 + 4))));
            }
            return table;
        }

        private static byte[] Strings(List<string> strings, List<uint> offsets)
        {
            using var stream = new MemoryStream();
            stream.Write(new byte[4]);
            foreach (string text in strings)
            {
                offsets.Add((uint)stream.Position);
                byte[] bytes = Encoding.Unicode.GetBytes(text + "\0");
                stream.Write(bytes);
                stream.Write(new byte[(4 - bytes.Length % 4) % 4]);
            }
            offsets.Add((uint)stream.Length);
            return stream.ToArray();
        }

        public (byte[] Indx, byte[] Names, byte[] Descs) ToBytes()
        {
            var nameOffsets = new List<uint>();
            var descOffsets = new List<uint>();
            byte[] names = Strings(Names, nameOffsets), descs = Strings(Descs, descOffsets);
            var indx = new byte[nameOffsets.Count * 8];
            for (int i = 0; i < nameOffsets.Count; i++)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(indx.AsSpan(i * 8), nameOffsets[i]);
                BinaryPrimitives.WriteUInt32LittleEndian(indx.AsSpan(i * 8 + 4), descOffsets[i]);
            }
            return (indx, names, descs);
        }

        public CardTextTable Clone()
        {
            var table = new CardTextTable();
            table.Names.AddRange(Names);
            table.Descs.AddRange(Descs);
            return table;
        }
    }
}
