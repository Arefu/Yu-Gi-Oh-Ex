using System.Diagnostics;
using System.IO;
using System.Drawing.Imaging;
using StartingCollection;

namespace Wolf.Editors
{
    /// <summary>
    /// Where an editor reads the game's files from. Paths are archive paths with backslashes ("pdui\doShared.png").
    /// </summary>
    public interface IGameFiles
    {
        byte[]? Read(string path);
        IEnumerable<string> Paths { get; }
    }

    /// <summary>
    /// The game data WolfX works on, one for the whole app (<see cref="Current"/>): every editor reads and saves through it.
    /// Two kinds of folder open:
    /// * the game folder (YGO_2020.toc / .dat): files are read straight out of the .dat, and a loose file in &lt;game&gt;\YGO_2020\&lt;path&gt;
    ///   wins (that is how mods such as the Anime Frames replace pictures). Saving writes into the .dat in place (<see cref="TocArchive.Write"/>),
    ///   or into the loose file when there is one, since that is the one the game uses.
    /// * an extracted YGO_2020 folder (any name): files are read and saved there.
    /// Standard content (changes to things the game already has) is saved here in the game's own formats. Additional content (new cards,
    /// packs, ...) goes to the Yu-Gi-Oh-Ex folder (<see cref="ExFolder"/>) as JSON, which the launcher plugins apply.
    /// </summary>
    public sealed class GameFolderFiles : IGameFiles
    {
        /// <summary>The files a full set of game data has; missing ones are reported when a folder is opened.</summary>
        public static readonly (string Path, string What)[] RequiredFiles =
        [
            (@"bin\CARD_Prop.bin", "card stats"),
            (@"bin\CARD_IntID.bin", "Konami id to internal id"),
            (@"bin\CARD_Indx_E.bin", "card text index"),
            (@"bin\CARD_Name_E.bin", "card names"),
            (@"bin\CARD_Desc_E.bin", "card texts"),
            (@"bin\CARD_Genre.bin", "card genres"),
            (@"bin\tagdata.bin", "related cards"),
            (@"2020.full.illust_j.jpg.zib", "card art"),
            ("decks.zib", "deck cards"),
            ("packs.zib", "pack cards"),
        ];

        private readonly TocArchive? _archive;
        private readonly string? _extracted;

        /// <summary>The game folder (the one with YuGiOh.exe and YGO_2020.toc).</summary>
        public GameFolderFiles(string gameFolder)
        {
            GameFolder = gameFolder;
            _archive = TocArchive.TryOpen(Path.Combine(gameFolder, "YGO_2020.toc"));
        }

        private GameFolderFiles(string extractedFolder, string exParent)
        {
            _extracted = extractedFolder;
            GameFolder = exParent;
        }

        /// <summary>The data every editor uses, set by the main window. <see cref="CurrentChanged"/> tells the editors to reopen.</summary>
        public static GameFolderFiles? Current { get; private set; }

        public static event Action? CurrentChanged;

        public static void SetCurrent(GameFolderFiles? files)
        {
            Current = files;
            CurrentChanged?.Invoke();
        }

        /// <summary>
        /// Opens a game folder or an extracted data folder. Returns null when the folder is neither (no YGO_2020.toc and none of the
        /// <see cref="RequiredFiles"/>).
        /// </summary>
        public static GameFolderFiles? Open(string folder)
        {
            folder = Path.GetFullPath(folder);
            if (File.Exists(folder))
                folder = Path.GetDirectoryName(folder) ?? folder;   // YGO_2020.dat or .toc itself was picked
            if (File.Exists(Path.Combine(folder, "YGO_2020.toc")))
            {
                var game = new GameFolderFiles(folder);
                return game._archive != null ? game : null;
            }
            if (!RequiredFiles.Any(file => File.Exists(Path.Combine(folder, file.Path))) && !Directory.Exists(Path.Combine(folder, "bin")))
                return null;
            // An extracted folder: Yu-Gi-Oh-Ex goes next to it (inside the game folder, when it is one of its folders).
            return new GameFolderFiles(folder, Path.GetDirectoryName(folder) ?? folder);
        }

        /// <summary>True for an extracted folder, false for the game folder (the .dat).</summary>
        public bool IsExtracted => _extracted != null;

        /// <summary>The game folder, or for an extracted folder the folder it is in (Yu-Gi-Oh-Ex goes there).</summary>
        public string GameFolder { get; }

        /// <summary>The archive, for the game folder.</summary>
        public TocArchive? Archive => _archive;

        /// <summary>What was opened: the extracted folder or the game folder.</summary>
        public string Folder => _extracted ?? GameFolder;

        /// <summary>Where loose files go: the extracted folder, or &lt;game&gt;\YGO_2020.</summary>
        public string OverrideFolder => _extracted ?? Path.Combine(GameFolder, "YGO_2020");

        public bool Available => _archive != null || _extracted != null;

        public string LoosePath(string path) => Path.Combine(OverrideFolder, path.Replace('/', '\\'));

        /// <summary>
        /// &lt;game&gt;\Yu-Gi-Oh-Ex: additional content, as JSON a person can read (plus PNGs for pictures). The launcher plugins read it
        /// and apply it to the game.
        /// </summary>
        public string ExFolder => Path.Combine(GameFolder, "Yu-Gi-Oh-Ex");

        /// <summary>A file in the Yu-Gi-Oh-Ex folder ("tutorials\steam_tutorial_05_E.json").</summary>
        public string ExPath(string relative) => Path.Combine(ExFolder, relative.Replace('/', '\\'));

        public bool IsOverridden(string path) => _extracted == null && File.Exists(LoosePath(path));

        /// <summary>The required files this data doesn't have, with what they are for.</summary>
        public List<string> MissingRequired() =>
            RequiredFiles.Where(file => !Exists(file.Path)).Select(file => $"{file.Path} ({file.What})").ToList();

        public bool Exists(string path) => File.Exists(LoosePath(path)) || (_archive?.Contains(path) ?? false);

        public byte[]? Read(string path)
        {
            path = path.Replace('/', '\\');
            string loose = LoosePath(path);
            if (File.Exists(loose))
                return File.ReadAllBytes(loose);
            return _archive?.Read(path);
        }

        /// <summary>Part of a file (the art .zib files are hundreds of MB).</summary>
        public byte[]? Read(string path, long start, int count)
        {
            string loose = LoosePath(path);
            if (!File.Exists(loose))
                return _archive?.Read(path, start, count);
            using var stream = new FileStream(loose, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (start < 0 || start >= stream.Length)
                return null;
            stream.Position = start;
            var data = new byte[Math.Min(count, stream.Length - start)];
            stream.ReadExactly(data);
            return data;
        }

        /// <summary>Where a file is read from, for status lines: "YGO_2020.dat" or the loose file.</summary>
        public string Describe(string path) => File.Exists(LoosePath(path)) ? LoosePath(path) : _archive != null ? "YGO_2020.dat" : LoosePath(path);

        public IEnumerable<string> Paths
        {
            get
            {
                var all = new HashSet<string>(_archive?.Paths ?? [], StringComparer.OrdinalIgnoreCase);
                if (Directory.Exists(OverrideFolder))
                {
                    foreach (string file in Directory.EnumerateFiles(OverrideFolder, "*", SearchOption.AllDirectories))
                        all.Add(Path.GetRelativePath(OverrideFolder, file));
                }
                return all;
            }
        }

        /// <summary>True while the game is running: it has the .dat open, so the archive isn't written then.</summary>
        public static bool GameRunning => Process.GetProcessesByName("YuGiOh").Length > 0;

        /// <summary>Raised after <see cref="Write"/> with the paths written, so other editors showing those files can reload.</summary>
        public event Action<IReadOnlyCollection<string>>? Written;

        public void Write(string path, byte[] data) => Write(new Dictionary<string, byte[]> { [path] = data });

        /// <summary>
        /// Saves files in the game's own formats: a loose file stays a loose file (the extracted folder, or &lt;game&gt;\YGO_2020\...), the rest
        /// goes into YGO_2020.dat in place. Throws <see cref="InvalidOperationException"/> while the game is running.
        /// </summary>
        public void Write(IReadOnlyDictionary<string, byte[]> files)
        {
            var intoArchive = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var (rawPath, data) in files)
            {
                string path = rawPath.Replace('/', '\\');
                string loose = LoosePath(path);
                if (_archive == null || File.Exists(loose))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
                    if (File.Exists(loose) && !File.Exists(loose + ".bak"))
                        File.Copy(loose, loose + ".bak");   // the first version, once
                    File.WriteAllBytes(loose, data);
                }
                else
                    intoArchive[path] = data;
            }
            if (intoArchive.Count > 0)
            {
                if (GameRunning)
                    throw new InvalidOperationException("Close the game first: it has YGO_2020.dat open.");
                _archive!.Write(intoArchive);
            }
            Written?.Invoke(files.Keys.ToList());
        }

        /// <summary>Puts the game's original YGO_2020.toc back and cuts the .dat back: undoes every save into the archive.</summary>
        public void RestoreOriginal()
        {
            if (GameRunning)
                throw new InvalidOperationException("Close the game first: it has YGO_2020.dat open.");
            _archive?.RestoreOriginal();
            Written?.Invoke(_archive?.Paths.ToList() ?? []);
        }
    }

    /// <summary>Files in a folder on disk (an unpacked YGO_2020, or anywhere).</summary>
    public sealed class DiskFiles(string root) : IGameFiles
    {
        public string Root { get; } = root;

        public byte[]? Read(string path)
        {
            string full = Path.Combine(Root, path.Replace('/', '\\'));
            return File.Exists(full) ? File.ReadAllBytes(full) : null;
        }

        public IEnumerable<string> Paths =>
            Directory.Exists(Root) ? Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(Root, f)) : [];
    }

    public static class Imaging
    {
        /// <summary>
        /// Decodes a picture into 32 bpp premultiplied ARGB, the format GDI+ draws fastest (a PNG decodes to plain ARGB, which GDI+ converts
        /// on every DrawImage: that is what made big sheets such as STEAM_icons crawl).
        /// </summary>
        public static Bitmap? Decode(byte[]? data)
        {
            if (data == null)
                return null;
            try
            {
                using var stream = new MemoryStream(data);
                using var decoded = Image.FromStream(stream);
                return ToFast(decoded);
            }
            catch
            {
                return null;
            }
        }

        public static Bitmap? Load(string path) => File.Exists(path) ? Decode(File.ReadAllBytes(path)) : null;

        public static Bitmap ToFast(Image image)
        {
            var fast = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
            fast.SetResolution(96, 96);
            using var g = Graphics.FromImage(fast);
            g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
            return fast;
        }

        /// <summary>The alpha of every pixel (row by row), for "fit to pixels" and "find sprites".</summary>
        public static byte[] AlphaMap(Bitmap bitmap)
        {
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var alpha = new byte[bitmap.Width * bitmap.Height];
                var row = new byte[data.Stride];
                for (int y = 0; y < bitmap.Height; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, data.Stride);
                    for (int x = 0; x < bitmap.Width; x++)
                        alpha[y * bitmap.Width + x] = row[x * 4 + 3];
                }
                return alpha;
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        /// <summary>A grey checkerboard, so transparent parts of a picture show as such.</summary>
        public static TextureBrush Checkerboard(int cell = 8)
        {
            var tile = new Bitmap(cell * 2, cell * 2);
            using (var g = Graphics.FromImage(tile))
            {
                g.Clear(Color.FromArgb(58, 58, 64));
                using var dark = new SolidBrush(Color.FromArgb(44, 44, 50));
                g.FillRectangle(dark, 0, 0, cell, cell);
                g.FillRectangle(dark, cell, cell, cell, cell);
            }
            return new TextureBrush(tile);
        }
    }
}
