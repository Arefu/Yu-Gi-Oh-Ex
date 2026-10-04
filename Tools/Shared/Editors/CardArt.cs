using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Wolf.Editors
{
    /// <summary>
    /// Card pictures from the open game data (<see cref="GameFolderFiles.Current"/>): the art .zib is hundreds of MB, so only its list and the
    /// one picture asked for are read. A custom card's picture comes from Yu-Gi-Oh-Ex (the "image" file named in cards.json) when given.
    /// .zib: 64-byte entries (big-endian u32 start, u32 size, 56-byte name such as "10011.jpg", the Konami id), then the files; the first
    /// entry's start is where the list ends.
    /// </summary>
    public static class CardArt
    {
        public const string CensoredZib = "2020.full.illust_j.jpg.zib";
        public const string UncensoredZib = "2020.full.illust_a.jpg.zib";

        private static GameFolderFiles? _files;
        private static readonly Dictionary<string, Dictionary<int, (long Start, int Size)>> _indexes = [];
        private static readonly Dictionary<(int, bool), Bitmap?> _cache = [];

        /// <summary>The card's picture, or null. <paramref name="customImage"/>: a file in Yu-Gi-Oh-Ex for a custom card.</summary>
        public static Bitmap? Get(int konamiId, string? customImage = null, bool uncensored = false)
        {
            var files = GameFolderFiles.Current;
            if (files != _files)
            {
                _files = files;
                _indexes.Clear();
                Clear();
            }
            if (!string.IsNullOrEmpty(customImage) && files != null)
            {
                string path = Path.IsPathRooted(customImage) ? customImage : files.ExPath(customImage);
                return File.Exists(path) ? Imaging.Load(path) : null;   // not cached: it may have just been replaced
            }
            if (_cache.TryGetValue((konamiId, uncensored), out var cached))
                return cached;
            if (_cache.Count > 400)
                Clear();
            Bitmap? art = null;
            if (files != null)
            {
                if (uncensored && Index(files, UncensoredZib).TryGetValue(konamiId, out var alt))
                    art = Imaging.Decode(files.Read(UncensoredZib, alt.Start, alt.Size));
                if (art == null && Index(files, CensoredZib).TryGetValue(konamiId, out var entry))
                    art = Imaging.Decode(files.Read(CensoredZib, entry.Start, entry.Size));
            }
            _cache[(konamiId, uncensored)] = art;
            return art;
        }

        /// <summary>Forget the pictures (after the art was saved, or another folder was opened).</summary>
        public static void Clear()
        {
            foreach (var bitmap in _cache.Values)
                bitmap?.Dispose();
            _cache.Clear();
        }

        /// <summary>Forget the pictures and the .zib lists too: after the art .zib files were written.</summary>
        public static void Reset()
        {
            _indexes.Clear();
            Clear();
        }

        /// <summary>True when this art .zib (<see cref="CensoredZib"/> / <see cref="UncensoredZib"/>) of the open data has the card's picture.</summary>
        public static bool Has(string zib, int konamiId)
        {
            if (GameFolderFiles.Current is not { } files)
                return false;
            if (files != _files)
            {
                _files = files;
                _indexes.Clear();
                Clear();
            }
            return Index(files, zib).ContainsKey(konamiId);
        }

        private static Dictionary<int, (long Start, int Size)> Index(GameFolderFiles files, string zib)
        {
            if (_indexes.TryGetValue(zib, out var index))
                return index;
            _indexes[zib] = index = [];
            if (files.Read(zib, 0, 64) is not { Length: 64 } head)
                return index;
            long listEnd = BinaryPrimitives.ReadUInt32BigEndian(head) / 4 * 4;
            if (listEnd < 64 || listEnd > 16 * 1024 * 1024 || files.Read(zib, 0, (int)listEnd) is not { } list)
                return index;
            for (int at = 0; at + 64 <= list.Length; at += 64)
            {
                long start = BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(at)) / 4 * 4;
                int size = (int)BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(at + 4));
                string name = Encoding.ASCII.GetString(list, at + 8, 56).TrimEnd('\0');
                if (size > 0 && int.TryParse(Path.GetFileNameWithoutExtension(name), out int id))
                    index[id] = (start, size);
            }
            return index;
        }
    }

    /// <summary>
    /// Changing a game card's pictures. A card has a censored picture in <see cref="CardArt.CensoredZib"/> (every card) and some also an
    /// uncensored one in <see cref="CardArt.UncensoredZib"/>; each is changed on its own (the Card Manager shows both). They are written
    /// back into those .zib files as standard content. In the game folder that is WolfX's patch archive, YGO_2020-Ex.dat: the game's own
    /// YGO_2020.dat / .toc are never written (GameFolderFiles refuses a patch named like the game's archive). The censored .zib is 600 MB, so
    /// it is rewritten by streaming (<see cref="Types.ZibArchive.Rewrite"/>), never held in memory.
    ///
    /// The game opens the art .zib files as streams (YGO::CARDS::CardArt_OpenZibs 0x14086CD60 -> Zib_OpenStream -> Stream_Open ->
    /// Archive_OpenEntry), not through the whole-file loader, so Yu-Gi-Oh-Core serves the patch's copy by redirecting Archive_OpenEntry
    /// to the patch archive (Core Patch.h). CardArt_OpenZibs lists every "&lt;Konami id&gt;.jpg" in both, so an uncensored picture added
    /// for a card is used too. Pictures are 304 x 304 baseline JPEG.
    /// </summary>
    public static class VanillaArt
    {
        public const int Size = 304;

        /// <summary>"Censored" or "Uncensored", for an art .zib.</summary>
        public static string Describe(string zib) => zib.Equals(CardArt.UncensoredZib, StringComparison.OrdinalIgnoreCase) ? "Uncensored" : "Censored";

        /// <summary>The picture as the game ships it (from YGO_2020.dat, before any change), or null.</summary>
        public static byte[]? Original(GameFolderFiles files, int konamiId, string zib = CardArt.CensoredZib)
        {
            using var reader = files.OpenGameRange(zib);
            if (reader == null)
                return null;
            var entry = Types.ZibArchive.ReadIndex(reader.Read).FirstOrDefault(e => e.Name.Equals(konamiId + ".jpg", StringComparison.OrdinalIgnoreCase));
            return entry == null ? null : reader.Read(entry.Start, (int)entry.Size);
        }

        /// <summary>The picture the game loads now (the patch's or a loose .zib when there is one), as its JPEG bytes, or null.</summary>
        public static byte[]? Current(GameFolderFiles files, int konamiId, string zib = CardArt.CensoredZib)
        {
            using var reader = files.OpenRange(zib);
            if (reader == null)
                return null;
            var entry = Types.ZibArchive.ReadIndex(reader.Read).FirstOrDefault(e => e.Name.Equals(konamiId + ".jpg", StringComparison.OrdinalIgnoreCase));
            return entry == null ? null : reader.Read(entry.Start, (int)entry.Size);
        }

        /// <summary>
        /// True when the open data's copy of this card's picture in that .zib isn't the one in YGO_2020.dat (changed, added or taken out, and saved).
        /// </summary>
        public static bool IsChanged(GameFolderFiles files, int konamiId, string zib = CardArt.CensoredZib)
        {
            if (files.IsExtracted || files.SourceOf(zib) is "archive" or null)
                return false;
            byte[]? current = Current(files, konamiId, zib), original = Original(files, konamiId, zib);
            return current == null || original == null ? current != original : !original.AsSpan().SequenceEqual(current);
        }

        /// <summary>
        /// A picture made into what the game expects: square (the middle of a wide or tall picture), 304 x 304, 24 bit baseline JPEG. A file
        /// that already is that is used as it is, so it isn't compressed twice.
        /// </summary>
        public static byte[] ToGameJpeg(byte[] picture)
        {
            using (var probe = new MemoryStream(picture))
            using (var image = Image.FromStream(probe))
            {
                if (image.RawFormat.Guid == System.Drawing.Imaging.ImageFormat.Jpeg.Guid && image.Width == Size && image.Height == Size &&
                    Image.GetPixelFormatSize(image.PixelFormat) == 24)
                    return picture;
            }
            using var source = new MemoryStream(picture);
            using var input = Image.FromStream(source);
            int side = Math.Min(input.Width, input.Height);
            var crop = new Rectangle((input.Width - side) / 2, (input.Height - side) / 2, side, side);
            using var art = new Bitmap(Size, Size, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(art))
            {
                g.Clear(Color.Black);   // under a transparent picture
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                g.DrawImage(input, new Rectangle(0, 0, Size, Size), crop, GraphicsUnit.Pixel);
            }
            var codec = System.Drawing.Imaging.ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == System.Drawing.Imaging.ImageFormat.Jpeg.Guid);
            using var parameters = new System.Drawing.Imaging.EncoderParameters(1);
            parameters.Param[0] = new System.Drawing.Imaging.EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
            using var output = new MemoryStream();
            art.Save(output, codec, parameters);
            return output.ToArray();
        }

        /// <summary>
        /// Writes the pictures into the art .zib files, each into the one it is for: (.zib, Konami id) -> game JPEG, or null to take the
        /// card's picture out of that .zib (an uncensored picture that was added). Only the .zib files with a change are written, into the
        /// copy the game loads (the patch, never YGO_2020.dat). Throws like
        /// <see cref="GameFolderFiles.Write(IReadOnlyDictionary{string, StartingCollection.TocArchive.PatchSource})"/> (the game must be closed).
        /// Returns the .zib files written.
        /// </summary>
        public static List<string> Save(GameFolderFiles files, IReadOnlyDictionary<(string Zib, int Id), byte[]?> pictures)
        {
            var write = new Dictionary<string, StartingCollection.TocArchive.PatchSource>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in pictures.GroupBy(p => p.Key.Zib, StringComparer.OrdinalIgnoreCase))
            {
                string zib = group.Key;
                if (!files.Exists(zib))
                    throw new InvalidOperationException($"The open data has no {zib}.");
                var replace = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                var remove = new List<string>();
                foreach (var ((_, id), jpeg) in group)
                {
                    if (jpeg != null)
                        replace[id + ".jpg"] = jpeg;
                    else
                        remove.Add(id + ".jpg");
                }
                // the reader is opened and closed inside: the file it reads may be the one being replaced
                write[zib] = StartingCollection.TocArchive.PatchSource.Streamed(stream =>
                {
                    using var reader = files.OpenRange(zib) ?? throw new InvalidOperationException($"{zib} couldn't be opened.");
                    return Types.ZibArchive.Rewrite(reader.Read, replace, stream, remove);
                });
            }
            if (write.Count > 0)
                files.Write(write);
            CardArt.Reset();
            return [.. write.Keys];
        }
    }

    /// <summary>
    /// The card painter's frames (duel\frame\card_*.png) and icons (sprites of pdui\STEAM_icons) from the open game data, so a frame mod
    /// installed as loose files is what's shown, as in the game.
    /// </summary>
    public sealed class WorkspaceCardArt : ICardFaceArt, IGameCardAssets
    {
        private readonly GameFolderFiles _files;
        private readonly Dictionary<string, Bitmap?> _cache = [];
        private readonly Dictionary<string, GameFont?> _fonts = [];
        private Bitmap? _iconSheet;
        private Dictionary<string, Types.DfymooSprite>? _icons;

        public WorkspaceCardArt(GameFolderFiles files) => _files = files;

        public Bitmap? Frame(CardFrame frame) => Frame(CardNames.FrameFile(frame));

        /// <summary>duel\frame\&lt;file&gt;.png (card_nomal, card_pendulum, card_link...).</summary>
        public Bitmap? Frame(string file)
        {
            if (!_cache.TryGetValue(file, out var bitmap))
                _cache[file] = bitmap = Imaging.Decode(_files.Read(Path.Combine("duel", "frame", file + ".png")));
            return bitmap;
        }

        /// <summary>fontbin\&lt;name&gt;.fbin and .png (FONT_ID_MATRIXCAPS_21, FONT_ID_CARD_ATKDEF...).</summary>
        public GameFont? Font(string name)
        {
            if (!_fonts.TryGetValue(name, out var font))
                _fonts[name] = font = GameFont.Parse(_files.Read(Path.Combine("fontbin", name + ".fbin")), Imaging.Decode(_files.Read(Path.Combine("fontbin", name + ".png"))));
            return font;
        }

        public Bitmap? Icon(string name)
        {
            if (_cache.TryGetValue(name, out var cached))
                return cached;
            Bitmap? icon = null;
            _iconSheet ??= Imaging.Decode(_files.Read(Path.Combine("pdui", "STEAM_icons.png")));
            _icons ??= _files.Read(Path.Combine("pdui", "STEAM_icons.dfymoo")) is { } list
                ? Types.DfymooSheet.Parse(Encoding.UTF8.GetString(list)).Sprites.GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
                : [];
            if (_iconSheet != null && _icons.TryGetValue(name, out var sprite))
            {
                var full = sprite.Trimmed ? new Size(sprite.FullWidth, sprite.FullHeight) : sprite.Bounds.Size;
                icon = new Bitmap(full.Width, full.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(icon);
                var offset = sprite.Trimmed ? new Point(sprite.OffsetX, sprite.OffsetY) : Point.Empty;
                g.DrawImage(_iconSheet, new Rectangle(offset, sprite.Bounds.Size), sprite.Bounds, GraphicsUnit.Pixel);
            }
            _cache[name] = icon;
            return icon;
        }
    }
}
