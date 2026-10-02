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
