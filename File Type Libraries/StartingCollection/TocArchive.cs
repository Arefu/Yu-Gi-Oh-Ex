using System.Globalization;

namespace StartingCollection
{
    /// <summary>
    /// The game's own packed data: YGO_2020.toc lists the files and YGO_2020.dat holds them one after another, each padded to a multiple
    /// of 4 bytes. This reads single files out of it without unpacking anything (the Yami-Yugi tool unpacks the whole thing).
    ///
    /// A toc line is: size in hex, length of the path in hex, path. The first line is "UT".
    /// </summary>
    public sealed class TocArchive
    {
        private sealed record Item(long Offset, long Size);

        private readonly Dictionary<string, Item> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _datPath;

        private TocArchive(string datPath) => _datPath = datPath;

        public int Count => _items.Count;

        /// <summary>Opens a .toc and the .dat next to it, or returns null when either is missing.</summary>
        public static TocArchive? TryOpen(string tocPath)
        {
            string datPath = Path.ChangeExtension(tocPath, ".dat");
            if (!File.Exists(tocPath) || !File.Exists(datPath))
                return null;

            var archive = new TocArchive(datPath);
            long offset = 0;
            foreach (string line in File.ReadLines(tocPath))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed == "UT")
                    continue;

                string[] parts = trimmed.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 3 || !long.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long size))
                    continue;

                archive._items[Normalize(parts[2])] = new Item(offset, size);
                offset += (size + 3) / 4 * 4;
            }
            return archive;
        }

        private static string Normalize(string path) => path.Trim().Replace('/', '\\');

        public bool Contains(string path) => _items.ContainsKey(Normalize(path));

        /// <summary>The file's bytes, or null when the archive doesn't have it.</summary>
        public byte[]? Read(string path)
        {
            if (!_items.TryGetValue(Normalize(path), out var item))
                return null;

            using var stream = new FileStream(_datPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = item.Offset;
            var data = new byte[item.Size];
            stream.ReadExactly(data);
            return data;
        }
    }
}
