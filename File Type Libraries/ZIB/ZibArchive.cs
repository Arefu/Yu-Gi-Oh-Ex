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

        /// <summary>The file by its name: exactly, else ignoring case (packdefdata names "bpack_battlepack1.bin", packs.zib holds "bpack_BattlePack1.bin").</summary>
        public Entry? Find(string name) =>
            Entries.FirstOrDefault(entry => entry.Name == name) ?? Entries.FirstOrDefault(entry => entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

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

        // ---- big archives (the card art .zib files are 600 MB): read the list only, rewrite by streaming ----

        /// <summary>One entry of an archive read with <see cref="ReadIndex"/>: where its file is, not the file.</summary>
        public sealed record IndexEntry(string Name, long Start, long Size, int Flags);

        /// <summary>
        /// The entries of a .zib without reading its files. <paramref name="read"/>(start, count) reads part of the archive (null when it
        /// can't). Only the 32 bit layout is read (every .zib the game ships; the game also knows a 64 bit one, marked by bit 0 of the first
        /// start being clear).
        /// </summary>
        public static List<IndexEntry> ReadIndex(Func<long, int, byte[]?> read)
        {
            var result = new List<IndexEntry>();
            if (read(0, EntrySize) is not { Length: EntrySize } head)
                return result;
            long listEnd = BinaryPrimitives.ReadUInt32BigEndian(head) & ~3u;
            if (listEnd < EntrySize || listEnd > 64 * 1024 * 1024 || read(0, (int)listEnd) is not { } list || list.Length < listEnd)
                return result;
            for (int at = 0; at + EntrySize <= listEnd; at += EntrySize)
            {
                uint raw = BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(at));
                uint size = BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(at + 4));
                var nameBytes = list.AsSpan(at + 8, NameBytes);
                int length = nameBytes.IndexOf((byte)0);
                result.Add(new IndexEntry(Encoding.UTF8.GetString(nameBytes[..(length < 0 ? NameBytes : length)]), raw & ~3u, size, (int)(raw & 3)));
            }
            return result;
        }

        /// <summary>
        /// Writes a copy of a big .zib to <paramref name="output"/> with some files replaced (by name, ignoring case) or added, without
        /// holding the archive in memory: the files that stay are copied from <paramref name="read"/> in pieces. The layout is the one
        /// <see cref="ToBytes"/> writes (the game's), the entries keep their order and flags. <paramref name="remove"/>: names taken out.
        /// Returns the bytes written.
        /// </summary>
        public static long Rewrite(Func<long, int, byte[]?> read, IReadOnlyDictionary<string, byte[]> replace, Stream output, IReadOnlyCollection<string>? remove = null)
        {
            var removed = new HashSet<string>(remove ?? [], StringComparer.OrdinalIgnoreCase);
            var entries = ReadIndex(read);
            if (entries.Count == 0)
                throw new InvalidDataException("The .zib's list of files couldn't be read.");
            if (entries.Count == removed.Count(name => entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) && replace.Count == 0)
                throw new InvalidOperationException("A .zib can't be left with no files.");
            var pending = new Dictionary<string, byte[]>(replace, StringComparer.OrdinalIgnoreCase);
            var plan = new List<(string Name, int Flags, long Size, byte[]? Data, long From)>();
            foreach (var entry in entries)
            {
                if (removed.Contains(entry.Name) && !pending.ContainsKey(entry.Name))
                    continue;
                if (pending.Remove(entry.Name, out var data))
                    plan.Add((entry.Name, entry.Flags, data.Length, data, 0));
                else
                    plan.Add((entry.Name, entry.Flags, entry.Size, null, entry.Start));
            }
            foreach (var (name, data) in pending.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                if (Encoding.UTF8.GetByteCount(name) >= NameBytes)
                    throw new ArgumentException($"A name in a .zib is at most {NameBytes - 1} bytes: {name}", nameof(replace));
                plan.Add((name, 0, data.Length, data, 0));
            }

            long start = output.Position;
            long position = (long)plan.Count * EntrySize + Alignment;
            var header = new byte[EntrySize];
            for (int i = 0; i < plan.Count; i++)
            {
                Array.Clear(header);
                if (position + plan[i].Size > uint.MaxValue)
                    throw new InvalidDataException("The .zib would pass 4 GB, which its 32 bit offsets can't hold.");
                BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(position + (i == 0 ? Math.Max(plan[i].Flags, 1) : plan[i].Flags)));
                BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)plan[i].Size);
                Encoding.UTF8.GetBytes(plan[i].Name).CopyTo(header, 8);
                output.Write(header);
                position += Padded(plan[i].Size);
            }
            output.Write(new byte[Alignment]);

            const int Chunk = 1 << 20;
            foreach (var (name, _, size, data, from) in plan)
            {
                if (data != null)
                    output.Write(data);
                else
                {
                    for (long done = 0; done < size;)
                    {
                        int count = (int)Math.Min(Chunk, size - done);
                        if (read(from + done, count) is not { } piece || piece.Length != count)
                            throw new InvalidDataException($"Couldn't read {name} from the .zib.");
                        output.Write(piece);
                        done += count;
                    }
                }
                output.Write(new byte[Padded(size) - size]);
            }
            return output.Position - start;
        }
    }
}
