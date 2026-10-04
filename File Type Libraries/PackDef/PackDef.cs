using System.Buffers.Binary;
using System.Text;

namespace PackDef
{
    /// <summary>The two kinds of pack the game has (the value is the character the game tests for).</summary>
    public enum PackKind : uint
    {
        /// <summary>A reward pack: its cards are packs.zib/packdata_&lt;name&gt;.bin.</summary>
        Reward = 'R',

        /// <summary>A battle pack: its cards are packs.zib/bpack_&lt;name&gt;.bin.</summary>
        Battle = 'B',
    }

    /// <summary>One pack of main/packdefdata_#.bin (up to 128 packs, ids below 128).</summary>
    public sealed class PackDefRecord
    {
        public uint Id { get; set; }

        /// <summary>0 to 5 for the reward packs (the series), 0xFFFFFFFF for the battle packs.</summary>
        public uint Series { get; set; }

        /// <summary>What the pack costs in the shop.</summary>
        public uint Cost { get; set; }

        public uint Kind { get; set; }

        /// <summary>The name the pack's card list file is built from, e.g. "1_1" or "battlepack1".</summary>
        public string Name { get; set; } = "";

        public string Title { get; set; } = "";
        public string Text { get; set; } = "";

        public bool IsReward => Kind == (uint)PackKind.Reward;

        /// <summary>The file inside packs.zib that holds the card list.</summary>
        public string ContentsFile => IsReward ? $"packdata_{Name}.bin" : $"bpack_{Name}.bin";
    }

    /// <summary>
    /// main/packdefdata_#.bin: a count and 4 padding bytes, 40 byte records, then the strings the records point at
    /// (the name as ASCII, the two texts as UTF-16, all zero terminated).
    /// </summary>
    public sealed class PackDefFile
    {
        public const int HeaderSize = 8;
        public const int RecordSize = 40;

        public List<PackDefRecord> Records { get; } = [];

        public static PackDefFile Load(string path) => Parse(File.ReadAllBytes(path));

        public static PackDefFile Parse(byte[] data)
        {
            if (data.Length < HeaderSize)
                throw new InvalidDataException("packdefdata is too short.");

            uint count = BinaryPrimitives.ReadUInt32LittleEndian(data);
            if (HeaderSize + (long)count * RecordSize > data.Length)
                throw new InvalidDataException($"packdefdata says {count} packs but is only {data.Length} bytes.");

            var file = new PackDefFile();
            for (int i = 0; i < count; i++)
            {
                var record = data.AsSpan(HeaderSize + i * RecordSize, RecordSize);
                file.Records.Add(new PackDefRecord
                {
                    Id = BinaryPrimitives.ReadUInt32LittleEndian(record),
                    Series = BinaryPrimitives.ReadUInt32LittleEndian(record[4..]),
                    Cost = BinaryPrimitives.ReadUInt32LittleEndian(record[8..]),
                    Kind = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]),
                    Name = ReadAscii(data, BinaryPrimitives.ReadUInt64LittleEndian(record[16..])),
                    Title = ReadUtf16(data, BinaryPrimitives.ReadUInt64LittleEndian(record[24..])),
                    Text = ReadUtf16(data, BinaryPrimitives.ReadUInt64LittleEndian(record[32..])),
                });
            }
            return file;
        }

        public PackDefRecord? Find(string name) => Records.FirstOrDefault(record => record.Name == name);

        public byte[] ToBytes()
        {
            long stringsStart = HeaderSize + (long)Records.Count * RecordSize;
            var strings = new MemoryStream();
            var pointers = new List<ulong[]>();

            foreach (var record in Records)
            {
                ulong name = (ulong)(stringsStart + strings.Length);
                Add(strings, Encoding.ASCII.GetBytes(record.Name), 1);
                ulong title = (ulong)(stringsStart + strings.Length);
                Add(strings, Encoding.Unicode.GetBytes(record.Title), 2);
                ulong text = (ulong)(stringsStart + strings.Length);
                Add(strings, Encoding.Unicode.GetBytes(record.Text), 2);
                pointers.Add([name, title, text]);
            }

            var output = new MemoryStream();
            var writer = new BinaryWriter(output);
            writer.Write((uint)Records.Count);
            writer.Write(0u);

            for (int i = 0; i < Records.Count; i++)
            {
                var record = Records[i];
                writer.Write(record.Id);
                writer.Write(record.Series);
                writer.Write(record.Cost);
                writer.Write(record.Kind);
                foreach (ulong pointer in pointers[i])
                    writer.Write(pointer);
            }

            strings.WriteTo(output);
            return output.ToArray();
        }

        private static void Add(MemoryStream stream, byte[] bytes, int terminatorBytes)
        {
            stream.Write(bytes);
            stream.Write(new byte[terminatorBytes]);
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
    /// The card list of a reward pack (packs.zib/packdata_&lt;name&gt;.bin): a 16 bit count of common cards, a 16 bit
    /// count of rare cards, then the Konami ids of the common cards and of the rare cards. The game refuses the
    /// file unless its size is exactly 2 * (common + rare) + 4.
    /// </summary>
    public sealed class PackContents
    {
        public List<ushort> Common { get; } = [];
        public List<ushort> Rare { get; } = [];

        public static PackContents Parse(byte[] data)
        {
            if (data.Length < 4)
                throw new InvalidDataException("A pack list is at least 4 bytes.");

            int common = BinaryPrimitives.ReadUInt16LittleEndian(data);
            int rare = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
            if (2 * (common + rare) + 4 != data.Length)
                throw new InvalidDataException($"A pack list with {common} common and {rare} rare cards is {2 * (common + rare) + 4} bytes, not {data.Length}.");

            var contents = new PackContents();
            for (int i = 0; i < common; i++)
                contents.Common.Add(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4 + 2 * i)));
            for (int i = 0; i < rare; i++)
                contents.Rare.Add(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(4 + 2 * (common + i))));
            return contents;
        }

        public byte[] ToBytes()
        {
            var output = new MemoryStream();
            var writer = new BinaryWriter(output);
            writer.Write((ushort)Common.Count);
            writer.Write((ushort)Rare.Count);
            foreach (ushort card in Common)
                writer.Write(card);
            foreach (ushort card in Rare)
                writer.Write(card);
            return output.ToArray();
        }
    }

    /// <summary>
    /// The card list of a battle pack (packs.zib/bpack_&lt;name&gt;.bin): one pool per <b>slot</b> of an opened pack, each card of the pack
    /// drawn from its slot's pool. A card listed several times in a pool comes up that much more often (the game's weights). Layout
    /// (little endian): u64 slot count, a u64 offset per slot, then each slot: u16 count and that many u16 Konami ids.
    /// </summary>
    public sealed class BattlePackContents
    {
        public List<List<ushort>> Slots { get; } = [];

        public static BattlePackContents Parse(byte[] data)
        {
            if (data.Length < 8)
                throw new InvalidDataException("A battle pack list is at least 8 bytes.");
            long count = BinaryPrimitives.ReadInt64LittleEndian(data);
            if (count < 0 || count > 64 || 8 + count * 8 > data.Length)
                throw new InvalidDataException($"A battle pack list with {count} slots doesn't fit in {data.Length} bytes.");

            var contents = new BattlePackContents();
            for (int slot = 0; slot < count; slot++)
            {
                long offset = BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(8 + slot * 8));
                if (offset < 0 || offset + 2 > data.Length)
                    throw new InvalidDataException($"Slot {slot + 1} starts past the end of the file.");
                int cards = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)offset));
                if (offset + 2 + cards * 2 > data.Length)
                    throw new InvalidDataException($"Slot {slot + 1} ({cards} cards) runs past the end of the file.");
                var ids = new List<ushort>(cards);
                for (int i = 0; i < cards; i++)
                    ids.Add(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan((int)offset + 2 + i * 2)));
                contents.Slots.Add(ids);
            }
            return contents;
        }

        public byte[] ToBytes()
        {
            var output = new MemoryStream();
            var writer = new BinaryWriter(output);
            writer.Write((long)Slots.Count);
            long offset = 8 + Slots.Count * 8L;
            foreach (var slot in Slots)
            {
                writer.Write(offset);
                offset += 2 + slot.Count * 2;
            }
            foreach (var slot in Slots)
            {
                writer.Write((ushort)slot.Count);
                foreach (ushort card in slot)
                    writer.Write(card);
            }
            return output.ToArray();
        }
    }
}
