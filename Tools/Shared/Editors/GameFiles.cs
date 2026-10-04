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
    /// * the game folder (YGO_2020.toc / .dat): files are read straight out of the .dat, a changed one out of WolfX's <b>patch archive</b>
    ///   (YGO_2020-Ex.toc / .dat, <see cref="PatchName"/>), and a loose file in &lt;game&gt;\YGO_2020\&lt;path&gt; wins over both (that is how mods
    ///   such as the Anime Frames replace pictures). Saving writes the patch archive (<see cref="TocArchive.WritePatch"/>), or the loose file
    ///   when there is one; the game's own YGO_2020.dat / .toc are never written. Yu-Gi-Oh-Core (always on) serves the game the patch's files
    ///   whenever they exist ([Yu-Gi-Oh-Core] PatchArchive), and deleting the two patch files undoes everything.
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
        private TocArchive? _patch;
        private readonly string? _extracted;

        /// <summary>The name of the game's own archive (<c>[Yu-Gi-Oh-Core] Archive</c>, YGO_2020 when not set), without .toc.</summary>
        public string ArchiveName { get; } = "YGO_2020";

        /// <summary>The game folder (the one with YuGiOh.exe and YGO_2020.toc).</summary>
        public GameFolderFiles(string gameFolder)
        {
            GameFolder = gameFolder;
            // the archive the game opens: [Yu-Gi-Oh-Core] Archive, YGO_2020 when it isn't set
            string archive = LoadSetting("Archive", "YGO_2020");
            ArchiveName = archive.Length > 0 ? archive : "YGO_2020";
            _archive = TocArchive.TryOpen(Path.Combine(gameFolder, (archive.Length > 0 ? archive : "YGO_2020") + ".toc"))
                       ?? TocArchive.TryOpen(Path.Combine(gameFolder, "YGO_2020.toc"));
            ReloadSettings();
        }

        // ---- how the game picks a file (Yu-Gi-Oh-Core Patch.h): the same settings, so WolfX shows and saves what the game loads ----

        private string _patchName = "YGO_2020-Ex", _looseFolderName = "YGO_2020";
        private bool _looseActive, _patchFirst;

        private string Ini => Path.Combine(GameFolder, "Config.ini");

        private string IniText(string section, string key, string fallback)
        {
            var value = new System.Text.StringBuilder(260);
            GetPrivateProfileString(section, key, fallback, value, value.Capacity, Ini);
            return value.ToString().Trim();
        }

        /// <summary>
        /// Reads the settings again ([Yu-Gi-Oh-Core] PatchArchive / FileOrder / LooseLoading / FolderName) and reopens the patch archive. Called when the data is
        /// opened and when the Game files page changes them.
        /// </summary>
        public void ReloadSettings()
        {
            if (_extracted != null)
                return;
            string name = LoadSetting("PatchArchive", "YGO_2020-Ex");
            _patchName = name.Length > 0 ? name : "YGO_2020-Ex";
            _patchFirst = LoadSetting("FileOrder", "loose").Equals("patch", StringComparison.OrdinalIgnoreCase);
            string folder = LoadSetting("FolderName", "YGO_2020");
            _looseFolderName = folder.Length > 0 ? folder : "YGO_2020";
            _looseActive = LoadSetting("LooseLoading", "0") == "1";
            // the game's own archive is never taken for the patch (PatchArchive=YGO_2020): it would be read as "changed" and written over
            bool isGame = string.Equals(_patchName, "YGO_2020", StringComparison.OrdinalIgnoreCase) || string.Equals(_patchName, ArchiveName, StringComparison.OrdinalIgnoreCase);
            _patch = isGame ? null : TocArchive.TryOpen(PatchToc);
        }

        /// <summary>A [Yu-Gi-Oh-Core] setting, else fallback (as Core's Loading::Setting).</summary>
        public string LoadSetting(string key, string fallback) => IniText("Yu-Gi-Oh-Core", key, fallback);

        /// <summary>True when the game takes loose files from <see cref="OverrideFolder"/> ([Yu-Gi-Oh-Core] LooseLoading=1); always for an extracted folder.</summary>
        public bool LooseLoading => _extracted != null || _looseActive;

        /// <summary>[Yu-Gi-Oh-Core] FileOrder = patch: WolfX's patch wins over loose files (default: loose files win).</summary>
        public bool PatchFirst => _patchFirst;

        /// <summary>
        /// The patch archive's name, without extension: [Yu-Gi-Oh-Core] PatchArchive in the game's Config.ini (Core reads the same key),
        /// "YGO_2020-Ex" when it isn't set.
        /// </summary>
        public string PatchName => _patchName;

        [System.Runtime.InteropServices.DllImport("kernel32", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int GetPrivateProfileString(string section, string key, string fallback, System.Text.StringBuilder value, int size, string file);

        private string PatchToc => Path.Combine(GameFolder, PatchName + ".toc");

        /// <summary>
        /// Throws when the patch would be the game's own archive ([..] PatchArchive set to YGO_2020, or to the Archive the game opens):
        /// writing the patch replaces its .toc / .dat, and the game's own data is never written.
        /// </summary>
        private void GuardPatchIsNotTheGame()
        {
            string patch = Path.GetFullPath(PatchToc);
            foreach (string game in new[] { Path.Combine(GameFolder, "YGO_2020.toc"), Path.Combine(GameFolder, ArchiveName + ".toc"), _archive?.TocPath ?? "" })
                if (game.Length > 0 && string.Equals(patch, Path.GetFullPath(game), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"PatchArchive is \"{PatchName}\", the game's own archive: WolfX never writes that. Set PatchArchive to YGO_2020-Ex (Files > Game files & loading).");
        }

        /// <summary>WolfX's patch archive, when it has any files (game folder only).</summary>
        public TocArchive? Patch => _patch;

        /// <summary>The files the patch archive holds (what WolfX changed in the game's archive).</summary>
        public IReadOnlyList<string> PatchedFiles => _patch?.Paths.ToList() ?? [];

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

        /// <summary>Where loose files are: the extracted folder, or &lt;game&gt;\&lt;FolderName&gt; (default YGO_2020).</summary>
        public string OverrideFolder => _extracted ?? Path.Combine(GameFolder, _looseFolderName);

        public bool Available => _archive != null || _extracted != null;

        public string LoosePath(string path) => Path.Combine(OverrideFolder, path.Replace('/', '\\'));

        /// <summary>
        /// &lt;game&gt;\Yu-Gi-Oh-Ex: additional content, as JSON a person can read (plus PNGs for pictures). The launcher plugins read it
        /// and apply it to the game.
        /// </summary>
        public string ExFolder => Path.Combine(GameFolder, "Yu-Gi-Oh-Ex");

        /// <summary>A file in the Yu-Gi-Oh-Ex folder ("tutorials\steam_tutorial_05_E.json").</summary>
        public string ExPath(string relative) => Path.Combine(ExFolder, relative.Replace('/', '\\'));

        public bool IsOverridden(string path) => _extracted == null && HasLoose(path);

        /// <summary>A loose file the game would use for this path (loose loading on and the file there).</summary>
        private bool HasLoose(string path) => LooseLoading && File.Exists(LoosePath(path));

        /// <summary>Which copy of a file the game loads: "loose", "patch" or "archive" (null: none).</summary>
        public string? SourceOf(string path)
        {
            path = path.Replace('/', '\\');
            if (_extracted != null)
                return File.Exists(LoosePath(path)) ? "loose" : null;
            bool loose = HasLoose(path), patched = _patch?.Contains(path) == true;
            if (_patchFirst && patched)
                return "patch";
            if (loose)
                return "loose";
            if (patched)
                return "patch";
            return _archive?.Contains(path) == true ? "archive" : null;
        }

        /// <summary>The required files this data doesn't have, with what they are for.</summary>
        public List<string> MissingRequired() =>
            RequiredFiles.Where(file => !Exists(file.Path)).Select(file => $"{file.Path} ({file.What})").ToList();

        public bool Exists(string path) => SourceOf(path) != null;

        public byte[]? Read(string path)
        {
            path = path.Replace('/', '\\');
            return SourceOf(path) switch
            {
                "loose" => File.ReadAllBytes(LoosePath(path)),
                "patch" => _patch!.Read(path),
                "archive" => _archive!.Read(path),
                _ => null,
            };
        }

        /// <summary>Part of a file (the art .zib files are hundreds of MB).</summary>
        public byte[]? Read(string path, long start, int count)
        {
            string loose = LoosePath(path);
            string? source = SourceOf(path);
            if (source != "loose")
                return source == "patch" ? _patch!.Read(path, start, count) : _archive?.Read(path, start, count);
            using var stream = new FileStream(loose, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (start < 0 || start >= stream.Length)
                return null;
            stream.Position = start;
            var data = new byte[Math.Min(count, stream.Length - start)];
            stream.ReadExactly(data);
            return data;
        }

        /// <summary>
        /// Reads parts of one file through a single open handle (for walking a whole .zib): read(start, count) clamps to the file and
        /// returns null past its end. Null when the data has no such file. Dispose it when done.
        /// </summary>
        public RangeReader? OpenRange(string path)
        {
            path = path.Replace('/', '\\');
            string? source = SourceOf(path);
            if (source == "loose")
            {
                string loose = LoosePath(path);
                return new RangeReader(loose, 0, new FileInfo(loose).Length);
            }
            var archive = source == "patch" ? _patch : source == "archive" ? _archive : null;
            return archive?.Find(path) is { } item ? new RangeReader(archive.DatPath, item.Offset, item.Size) : null;
        }

        /// <summary>Like <see cref="OpenRange"/>, but the game's own copy in YGO_2020.dat (what the file was before any change).</summary>
        public RangeReader? OpenGameRange(string path) =>
            _archive?.Find(path.Replace('/', '\\')) is { } item ? new RangeReader(_archive.DatPath, item.Offset, item.Size) : null;

        /// <summary>A file inside another (or on its own) read in parts; see <see cref="OpenRange"/>.</summary>
        public sealed class RangeReader : IDisposable
        {
            private readonly FileStream _stream;
            private readonly long _offset;

            internal RangeReader(string file, long offset, long size)
            {
                _stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
                _offset = offset;
                Size = size;
            }

            public long Size { get; }

            public byte[]? Read(long start, int count)
            {
                if (start < 0 || start >= Size)
                    return null;
                var data = new byte[Math.Min(count, Size - start)];
                _stream.Position = _offset + start;
                _stream.ReadExactly(data);
                return data;
            }

            public void Dispose() => _stream.Dispose();
        }

        /// <summary>Where a file is read from, for status lines: "YGO_2020.dat" or the loose file.</summary>
        public string Describe(string path) => SourceOf(path) switch
        {
            "loose" => LoosePath(path),
            "patch" => $"{PatchName}.dat (WolfX's patch)",
            "archive" => "YGO_2020.dat",
            _ => _archive != null ? $"{PatchName}.dat (WolfX's patch)" : LoosePath(path),
        };

        public IEnumerable<string> Paths
        {
            get
            {
                var all = new HashSet<string>(_archive?.Paths ?? [], StringComparer.OrdinalIgnoreCase);
                all.UnionWith(_patch?.Paths ?? []);
                if (LooseLoading && Directory.Exists(OverrideFolder))
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
        /// goes into the patch archive (YGO_2020-Ex.dat / .toc, rewritten with every file it already had plus these). The game's YGO_2020.dat
        /// is not written. Throws <see cref="InvalidOperationException"/> while the game is running (it has the patch open).
        /// </summary>
        public void Write(IReadOnlyDictionary<string, byte[]> files) =>
            Write(files.ToDictionary(file => file.Key, file => TocArchive.PatchSource.Of(file.Value)));

        /// <summary>
        /// <see cref="Write(IReadOnlyDictionary{string, byte[]})"/> for files written by a callback (it writes the file to the stream and
        /// returns its size), for the ones too big to hold in memory: the card art .zib files are 600 MB.
        /// </summary>
        public void Write(IReadOnlyDictionary<string, TocArchive.PatchSource> files)
        {
            var intoArchive = new Dictionary<string, TocArchive.PatchSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var (rawPath, source) in files)
            {
                string path = rawPath.Replace('/', '\\');
                string loose = LoosePath(path);
                // into the copy the game loads: the loose file when it wins, otherwise the patch (an extracted folder is all loose files)
                if (_archive == null || SourceOf(path) == "loose")
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(loose)!);
                    if (File.Exists(loose) && !File.Exists(loose + ".bak"))
                        File.Copy(loose, loose + ".bak");   // the first version, once
                    // written next to it first: a source may still be reading the old file (a .zib rewritten from itself)
                    string temp = loose + ".saving";
                    using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
                        source.WriteTo(stream);
                    File.Move(temp, loose, overwrite: true);
                }
                else
                    intoArchive[path] = source;
            }
            if (intoArchive.Count > 0)
            {
                if (GameRunning)
                    throw new InvalidOperationException($"Close the game first: it has {PatchName}.dat open.");
                GuardPatchIsNotTheGame();
                // the patch keeps every file changed so far (copied across from the old patch, never held in memory); this save's files
                // replace their earlier versions. The new patch is written under a temporary name, so the old one can be read meanwhile.
                var patch = new Dictionary<string, TocArchive.PatchSource>(StringComparer.OrdinalIgnoreCase);
                if (_patch is { } old)
                    foreach (string path in old.Paths)
                        patch[path] = TocArchive.PatchSource.From(old, path);
                foreach (var (path, source) in intoArchive)
                    patch[path] = source;
                TocArchive.WritePatch(PatchToc, patch);
                _patch = TocArchive.TryOpen(PatchToc);
            }
            Written?.Invoke(files.Keys.ToList());
        }

        /// <summary>True when there is something to undo: a patch archive, or the in-place saves older WolfX versions made into YGO_2020.dat.</summary>
        public bool HasChanges => _patch != null || _archive?.IsModified == true;

        /// <summary>
        /// Back to the game's own data: deletes the patch archive, and undoes in-place saves an older WolfX made into YGO_2020.dat (puts the
        /// original .toc back and cuts the .dat to its size).
        /// </summary>
        public void RestoreOriginal()
        {
            if (GameRunning)
                throw new InvalidOperationException("Close the game first: it has the archives open.");
            GuardPatchIsNotTheGame();   // an empty patch is written by deleting its .toc / .dat
            var changed = (_patch?.Paths ?? []).Concat(_archive?.IsModified == true ? _archive.Paths : []).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            TocArchive.WritePatch(PatchToc, new Dictionary<string, byte[]>());
            _patch = null;
            if (_archive?.IsModified == true)
                _archive.RestoreOriginal();
            Written?.Invoke(changed);
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
