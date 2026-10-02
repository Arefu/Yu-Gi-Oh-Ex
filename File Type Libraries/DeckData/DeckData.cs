using System.Buffers.Binary;
using System.Text;

namespace DeckData
{
    /// <summary>
    /// One entry of main/deckdata_#.bin. The game copies these into its deck table (700 slots, indexed by <see cref="Id"/>)
    /// and loads the cards of the deck from decks.zib/&lt;<see cref="FileName"/>&gt;.ydc.
    /// </summary>
    public sealed class DeckRecord
    {
        /// <summary>The deck's slot in the game (0 to 699). The starter decks are 380 to 384.</summary>
        public uint Id { get; set; }

        /// <summary>Always equal to <see cref="Id"/> in the game's files; the game reads this copy.</summary>
        public uint Slot { get; set; }

        /// <summary>0 to 5 for the story decks (looks like the series), 0xFFFFFFFF for the starter decks.</summary>
        public uint Type { get; set; }

        /// <summary>
        /// The deck's signature card (a Konami card id), shown by name on the deck's info panel; 0xFFFF for none (the
        /// starter decks). The game copies only these 16 bits.
        /// </summary>
        public ushort SignatureCard { get; set; }

        /// <summary>Never read by the game: 0, or 0xFFFF on the starter decks (their +12 was written as a 32 bit -1).</summary>
        public ushort Padding14 { get; set; }

        /// <summary>
        /// The character (duelist) the deck belongs to, an index into the game's character table (chardata, below 240).
        /// The deck list sorts by this character's name and the unlock message reads "&lt;character&gt;'s &lt;title&gt;".
        /// </summary>
        public uint CharacterId { get; set; }

        /// <summary>The content pack (SKU) the deck belongs to; the game turns -1 into 1, the base game (skudata "LAUNCH").</summary>
        public int Sku { get; set; }

        /// <summary>The name of the .ydc in decks.zib, without the extension.</summary>
        public string FileName { get; set; } = "";

        public string Title { get; set; } = "";
        public string Text2 { get; set; } = "";
        public string Text3 { get; set; } = "";
    }

    /// <summary>
    /// main/deckdata_#.bin: a count and 4 padding bytes, 56 byte records, then the strings the records point at
    /// (the file name as ASCII, the three texts as UTF-16, all zero terminated).
    /// </summary>
    public sealed class DeckDataFile
    {
        public const int HeaderSize = 8;
        public const int RecordSize = 56;

        public List<DeckRecord> Records { get; } = [];

        public static DeckDataFile Load(string path) => Parse(File.ReadAllBytes(path));

        public static DeckDataFile Parse(byte[] data)
        {
            if (data.Length < HeaderSize)
                throw new InvalidDataException("deckdata is too short.");

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(data);
            if (HeaderSize + (long)count * RecordSize > data.Length)
                throw new InvalidDataException($"deckdata says {count} records but is only {data.Length} bytes.");

            var file = new DeckDataFile();
            for (int i = 0; i < count; i++)
            {
                var record = data.AsSpan(HeaderSize + i * RecordSize, RecordSize);
                file.Records.Add(new DeckRecord
                {
                    Id = BinaryPrimitives.ReadUInt32LittleEndian(record),
                    Slot = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]),
                    Type = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]),
                    SignatureCard = BinaryPrimitives.ReadUInt16LittleEndian(record[12..]),
                    Padding14 = BinaryPrimitives.ReadUInt16LittleEndian(record[14..]),
                    CharacterId = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]),
                    Sku = BinaryPrimitives.ReadInt32LittleEndian(record[20..]),
                    FileName = ReadAscii(data, BinaryPrimitives.ReadUInt64LittleEndian(record[24..])),
                    Title = ReadUtf16(data, BinaryPrimitives.ReadUInt64LittleEndian(record[32..])),
                    Text2 = ReadUtf16(data, BinaryPrimitives.ReadUInt64LittleEndian(record[40..])),
                    Text3 = ReadUtf16(data, BinaryPrimitives.ReadUInt64LittleEndian(record[48..])),
                });
            }
            return file;
        }

        public DeckRecord? Find(uint id) => Records.FirstOrDefault(record => record.Id == id);

        public byte[] ToBytes()
        {
            var strings = new MemoryStream();
            long stringsStart = HeaderSize + (long)Records.Count * RecordSize;

            var output = new MemoryStream();
            var writer = new BinaryWriter(output);
            writer.Write((uint)Records.Count);
            writer.Write(0u);

            // The strings follow the records, in the order the game's own file has them.
            var pointers = new List<ulong[]>();
            foreach (var record in Records)
            {
                ulong name = (ulong)(stringsStart + strings.Length);
                AddString(strings, Encoding.ASCII.GetBytes(record.FileName), 1);
                ulong title = (ulong)(stringsStart + strings.Length);
                AddString(strings, Encoding.Unicode.GetBytes(record.Title), 2);
                ulong text2 = (ulong)(stringsStart + strings.Length);
                AddString(strings, Encoding.Unicode.GetBytes(record.Text2), 2);
                ulong text3 = (ulong)(stringsStart + strings.Length);
                AddString(strings, Encoding.Unicode.GetBytes(record.Text3), 2);
                pointers.Add([name, title, text2, text3]);
            }

            for (int i = 0; i < Records.Count; i++)
            {
                var record = Records[i];
                writer.Write(record.Id);
                writer.Write(record.Slot);
                writer.Write(record.Type);
                writer.Write(record.SignatureCard);
                writer.Write(record.Padding14);
                writer.Write(record.CharacterId);
                writer.Write(record.Sku);
                foreach (ulong pointer in pointers[i])
                    writer.Write(pointer);
            }

            strings.WriteTo(output);
            return output.ToArray();
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        private static long AddString(MemoryStream stream, byte[] bytes, int terminatorBytes)
        {
            stream.Write(bytes);
            stream.Write(new byte[terminatorBytes]);
            return stream.Length;
        }

        private static string ReadAscii(byte[] data, ulong offset)
        {
            int start = checked((int)offset);
            int end = Array.IndexOf(data, (byte)0, start);
            return end < 0 ? "" : Encoding.ASCII.GetString(data, start, end - start);
        }

        private static string ReadUtf16(byte[] data, ulong offset)
        {
            int start = checked((int)offset);
            int end = start;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0))
                end += 2;

            return Encoding.Unicode.GetString(data, start, end - start);
        }
    }

    /// <summary>
    /// A .ydc deck file from decks.zib: 8 header bytes, then the main, extra and side deck as a 16 bit count followed
    /// by that many 16 bit Konami card ids.
    /// </summary>
    public sealed class YdcDeck
    {
        public byte[] Header { get; set; } = new byte[8];
        public List<ushort> Main { get; } = [];
        public List<ushort> Extra { get; } = [];
        public List<ushort> Side { get; } = [];

        public IEnumerable<ushort> AllCards => Main.Concat(Extra).Concat(Side);

        public static YdcDeck Parse(byte[] data)
        {
            if (data.Length < 8)
                throw new InvalidDataException("A .ydc file is at least 8 bytes.");

            var deck = new YdcDeck { Header = data[..8] };
            int position = 8;

            foreach (var section in new[] { deck.Main, deck.Extra, deck.Side })
            {
                if (position + 2 > data.Length)
                    break;

                int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position));
                position += 2;

                if (position + 2 * count > data.Length)
                    throw new InvalidDataException("A .ydc section runs past the end of the file.");

                for (int i = 0; i < count; i++, position += 2)
                    section.Add(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(position)));
            }
            return deck;
        }

        public byte[] ToBytes()
        {
            var output = new MemoryStream();
            var writer = new BinaryWriter(output);
            writer.Write(Header);
            foreach (var section in new[] { Main, Extra, Side })
            {
                writer.Write((ushort)section.Count);
                foreach (ushort card in section)
                    writer.Write(card);
            }
            return output.ToArray();
        }
    }
}
