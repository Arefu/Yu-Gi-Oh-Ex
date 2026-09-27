using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Types
{
    /// <summary>
    /// A .zib archive held in memory: read it, change or add files, write it back. Unlike <see cref="ZIB"/> it keeps the file names
    /// exactly as they are (some contain capitals) and the order of the entries, and rewrites the archive byte for byte when nothing changed.
    ///
    /// Layout (verified byte exact on packs.zib, decks.zib and busts.zib): one 64 byte entry per file, then 16 zero bytes, then the files,
    /// each padded with zeros to a multiple of 16. An entry is a big endian u32 start offset (the low 2 bits are flags; the first file has 1),
    /// a big endian u32 size and the name in 56 bytes, zero padded.
    /// </summary>
    public sealed class ZibArchive
    {
        public const int EntrySize = 64;
        public const int NameBytes = 56;
        public const int Alignment = 16;

        public sealed class Entry
        {
            public string Name { get; set; } = "";
            public byte[] Data { get; set; } = [];
            public int Flags { get; set; }
        }

        public List<Entry> Entries { get; } = [];

        public static ZibArchive Load(string path) => Parse(File.ReadAllBytes(path));

        public static ZibArchive Parse(byte[] data)
        {
            var archive = new ZibArchive();
            long dataStart = long.MaxValue;
            for (long offset = 0; offset + EntrySize <= dataStart; offset += EntrySize)
            {
                uint raw = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)offset));
                uint size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)offset + 4));
                long start = raw & ~3u;
                if (start + size > data.Length)
                    throw new InvalidDataException($"Entry {archive.Entries.Count} runs past the end of the file.");

                dataStart = Math.Min(dataStart, start);
                var nameBytes = data.AsSpan((int)offset + 8, NameBytes);
                int length = nameBytes.IndexOf((byte)0);
                archive.Entries.Add(new Entry
                {
                    Name = Encoding.UTF8.GetString(nameBytes[..(length < 0 ? NameBytes : length)]),
                    Data = data.AsSpan((int)start, (int)size).ToArray(),
                    Flags = (int)(raw & 3),
                });
            }
            return archive;
        }

        public Entry? Find(string name) => Entries.FirstOrDefault(entry => entry.Name == name);

        public byte[]? Get(string name) => Find(name)?.Data;

        /// <summary>Replaces the file, or appends it when the archive doesn't have it yet.</summary>
        public void Set(string name, byte[] data)
        {
            if (Encoding.UTF8.GetByteCount(name) >= NameBytes)
                throw new ArgumentException($"A name in a .zib is at most {NameBytes - 1} bytes.", nameof(name));

            var entry = Find(name);
            if (entry == null)
                Entries.Add(new Entry { Name = name, Data = data });
            else
                entry.Data = data;
        }

        public byte[] ToBytes()
        {
            var output = new MemoryStream();
            long position = (long)Entries.Count * EntrySize + Alignment;
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                var header = new byte[EntrySize];
                BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(position + (i == 0 ? Math.Max(entry.Flags, 1) : entry.Flags)));
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)entry.Data.Length);
                Encoding.UTF8.GetBytes(entry.Name).CopyTo(header, 8);
                output.Write(header);
                position += Padded(entry.Data.Length);
            }

            output.Write(new byte[Alignment]);
            foreach (var entry in Entries)
            {
                output.Write(entry.Data);
                output.Write(new byte[Padded(entry.Data.Length) - entry.Data.Length]);
            }
            return output.ToArray();
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        private static long Padded(long length) => (length + Alignment - 1) / Alignment * Alignment;
    }
}
