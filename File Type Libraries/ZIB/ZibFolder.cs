using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Types
{
    /// <summary>
    /// An extracted .zib: a folder holding the archive's files (people unpack decks.zib, packs.zib or the card art archives to look at or
    /// change them). It reads exactly like the .zib it stands for, in the layout <see cref="ZibArchive.ToBytes"/> writes, without holding
    /// the files in memory: only the parts asked for are read, so a 600 MB art folder works too.
    ///
    /// The folder is "&lt;name&gt;.zib" (what WolfX's own extract writes) or "&lt;name&gt;" next to where the .zib would be. Entries are the files
    /// directly in it (sub folders become "sub/file" names), ordered by name; the game's archives are looked up by name everywhere the tools
    /// read them, so the original order doesn't matter there.
    /// </summary>
    public sealed class ZibFolder
    {
        private readonly List<(string Name, string File, long Start, long Size)> _entries = [];

        public string Folder { get; }

        /// <summary>The whole archive's size: what the .zib file would be.</summary>
        public long Size { get; }

        /// <summary>Files skipped because their name doesn't fit in a .zib entry (55 bytes at most).</summary>
        public IReadOnlyList<string> Skipped { get; }

        private readonly byte[] _header;

        /// <summary>
        /// The folder that stands for <paramref name="zibPath"/> when the .zib itself isn't there: "&lt;path&gt;" as a folder, else the same
        /// without ".zib". Null when the .zib exists as a file or there is no such folder.
        /// </summary>
        public static string? Find(string zibPath)
        {
            if (!zibPath.EndsWith(".zib", StringComparison.OrdinalIgnoreCase) || File.Exists(zibPath))
                return null;
            if (Directory.Exists(zibPath))
                return zibPath;
            string bare = zibPath[..^4];
            return Directory.Exists(bare) && !File.Exists(bare) ? bare : null;
        }

        /// <summary>The folder for <paramref name="zibPath"/> (see <see cref="Find"/>), or null.</summary>
        public static ZibFolder? Open(string zibPath) => Find(zibPath) is { } folder ? new ZibFolder(folder) : null;

        public ZibFolder(string folder)
        {
            Folder = folder;
            var skipped = new List<string>();
            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)
                .Select(file => (Name: Path.GetRelativePath(folder, file).Replace('\\', '/'), File: file))
                .Where(file => !file.Name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) && !file.Name.EndsWith(".saving", StringComparison.OrdinalIgnoreCase))
                .OrderBy(file => file.Name, StringComparer.Ordinal)
                .ToList();
            foreach (var file in files.Where(file => Encoding.UTF8.GetByteCount(file.Name) >= ZibArchive.NameBytes).ToList())
            {
                skipped.Add(file.Name);
                files.Remove(file);
            }
            Skipped = skipped;

            long position = (long)files.Count * ZibArchive.EntrySize + ZibArchive.Alignment;
            _header = new byte[position];
            for (int i = 0; i < files.Count; i++)
            {
                long size = new FileInfo(files[i].File).Length;
                var entry = _header.AsSpan(i * ZibArchive.EntrySize, ZibArchive.EntrySize);
                BinaryPrimitives.WriteUInt32BigEndian(entry, (uint)(position + (i == 0 ? 1 : 0)));   // the first start carries flag 1, as the game's do
                BinaryPrimitives.WriteUInt32BigEndian(entry[4..], (uint)size);
                Encoding.UTF8.GetBytes(files[i].Name).CopyTo(entry[8..]);
                _entries.Add((files[i].Name, files[i].File, position, size));
                position += Padded(size);
            }
            Size = position;
        }

        public int Count => _entries.Count;

        /// <summary>Part of the archive, as if read from the .zib file: null past its end, shorter at the end.</summary>
        public byte[]? Read(long start, int count)
        {
            if (start < 0 || start >= Size || count <= 0)
                return null;
            var data = new byte[Math.Min(count, Size - start)];
            long end = start + data.Length;

            if (start < _header.Length)
                _header.AsSpan((int)start, (int)(Math.Min(end, _header.Length) - start)).CopyTo(data);

            // the files the range covers (padding stays zero)
            int first = _entries.FindLastIndex(entry => entry.Start <= start);
            for (int i = Math.Max(first, 0); i < _entries.Count && _entries[i].Start < end; i++)
            {
                var (_, file, fileStart, size) = _entries[i];
                long from = Math.Max(start, fileStart), to = Math.Min(end, fileStart + size);
                if (from >= to)
                    continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                stream.Position = from - fileStart;
                stream.ReadExactly(data.AsSpan((int)(from - start), (int)(to - from)));
            }
            return data;
        }

        /// <summary>The whole archive in memory (the small ones: decks.zib, packs.zib).</summary>
        public byte[] ToBytes() => Size > int.MaxValue
            ? throw new InvalidDataException($"{Folder} makes a {Size / (1024 * 1024)} MB archive, too big to read whole.")
            : Read(0, (int)Size) ?? [];

        /// <summary>
        /// Writes a .zib's files into <paramref name="folder"/> (the other way round): each entry becomes a file, and files of the folder the
        /// archive no longer has are deleted. <paramref name="read"/> reads parts of the archive, as for <see cref="ZibArchive.ReadIndex"/>.
        /// </summary>
        public static void Unpack(Func<long, int, byte[]?> read, string folder)
        {
            var entries = ZibArchive.ReadIndex(read);
            if (entries.Count == 0)
                throw new InvalidDataException("The .zib's list of files couldn't be read.");
            Directory.CreateDirectory(folder);
            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int Chunk = 1 << 20;
            foreach (var entry in entries)
            {
                string target = Path.Combine(folder, entry.Name.Replace('/', '\\'));
                keep.Add(Path.GetFullPath(target));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, Chunk);
                for (long done = 0; done < entry.Size;)
                {
                    int count = (int)Math.Min(Chunk, entry.Size - done);
                    if (read(entry.Start + done, count) is not { } piece || piece.Length != count)
                        throw new InvalidDataException($"Couldn't read {entry.Name} from the .zib.");
                    output.Write(piece);
                    done += count;
                }
            }
            foreach (string file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).ToList())
            {
                if (!keep.Contains(Path.GetFullPath(file)) && !file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);
            }
        }

        private static long Padded(long length) => (length + ZibArchive.Alignment - 1) / ZibArchive.Alignment * ZibArchive.Alignment;
    }
}
