using System.Globalization;
using System.Text;

namespace StartingCollection
{
    /// <summary>
    /// The game's own packed data: YGO_2020.toc lists the files and YGO_2020.dat holds them. Reads single files without unpacking anything,
    /// and saves changed files back in place (<see cref="Write"/>).
    ///
    /// The game (FS::LoadDAT) reads two kinds of toc, picked by its first 3 bytes:
    /// * text, "UT\n" (what the game ships): per file 16 fixed characters "%llx%x" (size in hex right-aligned in 12, length of the path in
    ///   hex in 3, a space), the path, a newline. There are no offsets: each file starts where the one before ends, rounded up to 4.
    /// * binary, "UB" + 1 byte: per file u32 length of the path, the path, u64 size, u64 offset. Files can be anywhere in the .dat.
    /// A first byte of 'E' means the .dat is encrypted (Load_FileContent decrypts it); the game's isn't, and that isn't supported here.
    /// The game reads each file as its size rounded up to 4, so those padding bytes have to exist.
    /// </summary>
    public sealed class TocArchive
    {
        public sealed record Item(string Path, long Offset, long Size)
        {
            public long End => Offset + Padded(Size);
        }

        /// <summary>The first toc, saved once before the archive is first written. Restoring it (and cutting the .dat back) undoes every save.</summary>
        public const string OriginalSuffix = ".original";

        private readonly Dictionary<string, Item> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Item> _order = [];

        private TocArchive(string tocPath, string datPath)
        {
            TocPath = tocPath;
            DatPath = datPath;
        }

        public string TocPath { get; }

        public string DatPath { get; }

        /// <summary>True when the toc is the binary kind (written by <see cref="Write"/>), false for the game's text toc.</summary>
        public bool IsBinary { get; private set; }

        public int Count => _items.Count;

        /// <summary>Every path in the archive, as the toc spells it (backslashes), in toc order.</summary>
        public IEnumerable<string> Paths => _order.Select(item => item.Path);

        public IReadOnlyList<Item> Items => _order;

        /// <summary>True once <see cref="Write"/> has changed this archive (the original toc is kept next to it).</summary>
        public bool IsModified => File.Exists(TocPath + OriginalSuffix);

        private static long Padded(long size) => (size + 3) & ~3L;

        /// <summary>Opens a .toc and the .dat next to it, or returns null when either is missing or the toc can't be read.</summary>
        public static TocArchive? TryOpen(string tocPath)
        {
            string datPath = Path.ChangeExtension(tocPath, ".dat");
            if (!File.Exists(tocPath) || !File.Exists(datPath))
                return null;
            var archive = new TocArchive(tocPath, datPath);
            try
            {
                archive.Parse(File.ReadAllBytes(tocPath));
            }
            catch (InvalidDataException)
            {
                return null;
            }
            return archive;
        }

        private void Parse(byte[] toc)
        {
            _items.Clear();
            _order.Clear();
            if (toc.Length < 3)
                throw new InvalidDataException("The toc is too short.");
            if (toc[0] == 'E')
                throw new InvalidDataException("The archive is encrypted.");
            IsBinary = toc[1] == 'B';
            int at = 3;
            if (IsBinary)
            {
                while (at + 4 <= toc.Length)
                {
                    int length = BitConverter.ToInt32(toc, at);
                    if (length < 0 || at + 4 + length + 16 > toc.Length)
                        throw new InvalidDataException("The binary toc is cut short.");
                    string path = Encoding.ASCII.GetString(toc, at + 4, length);
                    long size = BitConverter.ToInt64(toc, at + 4 + length);
                    long offset = BitConverter.ToInt64(toc, at + 12 + length);
                    Add(new Item(Normalize(path), offset, size));
                    at += 20 + length;
                }
                return;
            }

            // Text: parsed the way the game does (16 fixed characters, the path, one byte), but lenient about the header and spacing so
            // a hand-made toc (Yami-Yugi's packer writes "size length path" without padding) reads too.
            long next = 0;
            foreach (string line in Encoding.ASCII.GetString(toc).Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed == "UT")
                    continue;
                string[] parts = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 || !long.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long size))
                    continue;
                Add(new Item(Normalize(parts[2]), next, size));
                next += Padded(size);
            }
        }

        private void Add(Item item)
        {
            if (_items.TryGetValue(item.Path, out var old))
                _order.Remove(old);   // the game keeps the last one of a name too (it overwrites the map entry)
            _items[item.Path] = item;
            _order.Add(item);
        }

        private static string Normalize(string path) => path.Trim().Replace('/', '\\');

        public bool Contains(string path) => _items.ContainsKey(Normalize(path));

        public Item? Find(string path) => _items.GetValueOrDefault(Normalize(path));

        /// <summary>The file's bytes, or null when the archive doesn't have it.</summary>
        public byte[]? Read(string path)
        {
            if (!_items.TryGetValue(Normalize(path), out var item))
                return null;

            using var stream = new FileStream(DatPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = item.Offset;
            var data = new byte[item.Size];
            stream.ReadExactly(data);
            return data;
        }

        /// <summary>The file's size in bytes, or -1 when the archive doesn't have it.</summary>
        public long SizeOf(string path) => _items.TryGetValue(Normalize(path), out var item) ? item.Size : -1;

        /// <summary>
        /// Part of a file: count bytes from start (clamped to the file), or null when the archive doesn't have it. For big files
        /// that are archives themselves (the card art .zib files are hundreds of MB), so only the piece needed is read.
        /// </summary>
        public byte[]? Read(string path, long start, int count)
        {
            if (!_items.TryGetValue(Normalize(path), out var item) || start < 0 || start >= item.Size)
                return null;

            count = (int)Math.Min(count, item.Size - start);
            using var stream = new FileStream(DatPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = item.Offset + start;
            var data = new byte[count];
            stream.ReadExactly(data);
            return data;
        }

        // ---- writing ----

        /// <summary>
        /// Saves files into the archive (changed ones, or new paths) without moving anything else, so it takes as long as the files are big:
        /// * The first time, the game's toc is copied to YGO_2020.toc.original and the toc becomes the binary kind, which gives every file its
        ///   own offset. The .dat isn't rewritten.
        /// * Files are written to free space after the original end of the .dat, never over anything the original toc points at, and never
        ///   over a file the current toc still points at. Then the new toc replaces the old one in one step. So a save that fails part way
        ///   leaves the archive as it was, and <see cref="RestoreOriginal"/> can always undo everything.
        /// * A file's previous copy is free again after the save, so saving the same file again reuses that space: the .dat only grows by
        ///   about twice the size of the files you edit, and is cut back when the end is free.
        /// </summary>
        public void Write(IReadOnlyDictionary<string, byte[]> files)
        {
            if (files.Count == 0)
                return;

            string original = TocPath + OriginalSuffix;
            if (!File.Exists(original))
                File.Copy(TocPath, original);
            long protectedEnd = OriginalEnd();

            // Space after the original end that a current file uses. The replaced files' old copies stay used until the new toc is in.
            var used = _order.Where(item => item.End > protectedEnd).Select(item => (Start: Math.Max(item.Offset, protectedEnd), item.End)).ToList();
            var placed = new Dictionary<string, Item>(StringComparer.OrdinalIgnoreCase);
            using (var dat = new FileStream(DatPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                foreach (var (rawPath, data) in files)
                {
                    string path = Normalize(rawPath);
                    long need = Padded(data.Length);
                    long offset = FindSpace(used, protectedEnd, Math.Max(need, 4));
                    used.Add((offset, offset + Math.Max(need, 4)));
                    dat.Position = offset;
                    dat.Write(data);
                    dat.Write(new byte[need - data.Length]);   // the game reads the padding too
                    placed[path] = new Item(_items.TryGetValue(path, out var old) ? old.Path : path, offset, data.Length);
                }
                dat.Flush(flushToDisk: true);
            }

            foreach (var item in placed.Values)
            {
                int index = _items.TryGetValue(item.Path, out var old) ? _order.IndexOf(old) : -1;
                if (index >= 0)
                    _order[index] = item;
                else
                    _order.Add(item);
                _items[item.Path] = item;
            }
            SaveBinaryToc();
            TrimDat(protectedEnd);
        }

        /// <summary>The first gap at or after start (ends of used ranges, sorted) that holds size bytes, else the end of everything.</summary>
        private static long FindSpace(List<(long Start, long End)> used, long start, long size)
        {
            long at = start;
            foreach (var (s, e) in used.OrderBy(range => range.Start))
            {
                if (s - at >= size)
                    return at;
                at = Math.Max(at, e);
            }
            return at;
        }

        private void SaveBinaryToc()
        {
            using var buffer = new MemoryStream();
            buffer.Write("UB\n"u8);
            using (var writer = new BinaryWriter(buffer, Encoding.ASCII, leaveOpen: true))
            {
                foreach (var item in _order)
                {
                    byte[] name = Encoding.ASCII.GetBytes(item.Path);
                    writer.Write(name.Length);
                    writer.Write(name);
                    writer.Write(item.Size);
                    writer.Write(item.Offset);
                }
            }
            string temp = TocPath + ".saving";
            File.WriteAllBytes(temp, buffer.ToArray());
            File.Move(temp, TocPath, overwrite: true);
            IsBinary = true;
        }

        /// <summary>Cuts free space off the end of the .dat (never below the original end).</summary>
        private void TrimDat(long protectedEnd)
        {
            long end = Math.Max(protectedEnd, _order.Count > 0 ? _order.Max(item => item.End) : 0);
            using var dat = new FileStream(DatPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            if (dat.Length > end)
                dat.SetLength(end);
        }

        /// <summary>Where the game's own files end in the .dat: everything before this is never written.</summary>
        public long OriginalEnd()
        {
            string original = TocPath + OriginalSuffix;
            if (!File.Exists(original))
                return new FileInfo(DatPath).Length;
            var first = new TocArchive(original, DatPath);
            first.Parse(File.ReadAllBytes(original));
            return first._order.Count > 0 ? first._order.Max(item => item.End) : 0;
        }

        /// <summary>Undoes every <see cref="Write"/>: puts the original toc back and cuts the .dat back to its original size.</summary>
        public void RestoreOriginal()
        {
            string original = TocPath + OriginalSuffix;
            if (!File.Exists(original))
                return;
            long end = OriginalEnd();
            File.Copy(original, TocPath, overwrite: true);
            using (var dat = new FileStream(DatPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                if (dat.Length > end)
                    dat.SetLength(end);
            }
            File.Delete(original);
            Parse(File.ReadAllBytes(TocPath));
        }
    }
}
