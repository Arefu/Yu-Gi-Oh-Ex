using System.Drawing.Drawing2D;
using StartingCollection;

namespace WolfEx.Designer
{
    /// <summary>One sprite of a .dfymoo sheet: where it is on the sheet, and where that trimmed piece sits inside the untrimmed picture.</summary>
    internal sealed record SpriteInfo(string Name, Rectangle Source, Point Offset, Size Full);

    /// <summary>
    /// A sprite sheet the game draws its UI from: "pdui/doShared" is pdui\doShared.png plus pdui\doShared.dfymoo (a tk2d list of named
    /// rectangles). The game asks for a sheet by that resource name (DFX::TLayerAnimoo::SetResource) and for a sprite in it by name
    /// (DFX::TLayerAnimoo::SelectByName).
    /// </summary>
    internal sealed class Atlas
    {
        public required string Resource { get; init; }
        public required Bitmap Sheet { get; init; }
        public required IReadOnlyList<SpriteInfo> Sprites { get; init; }

        public SpriteInfo? Find(string name) => Sprites.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Pictures from the open game data (Wolf.Editors.GameFolderFiles: the .dat, or an extracted folder), read on demand and kept.
    /// Card art is never loaded: the card widgets draw placeholders, so the designer stays quick.
    /// </summary>
    internal sealed class GameArt : IDisposable
    {
        private readonly Wolf.Editors.IGameFiles? _archive;
        private readonly Dictionary<string, Bitmap?> _images = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Atlas?> _atlases = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Bitmap?> _sprites = new(StringComparer.OrdinalIgnoreCase);

        public static readonly GameArt None = new(null);

        private GameArt(Wolf.Editors.IGameFiles? archive) => _archive = archive;

        public static GameArt Open(Wolf.Editors.GameFolderFiles? files) => files?.Available == true ? new GameArt(files) : None;

        public bool Available => _archive != null;

        /// <summary>The sprite sheets in the archive, as resource names ("pdui/doShared").</summary>
        public IReadOnlyList<string> AtlasResources =>
            _archive == null ? [] : _archive.Paths
                .Where(p => p.EndsWith(".dfymoo", StringComparison.OrdinalIgnoreCase))
                .Select(p => p[..^".dfymoo".Length].Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Full-screen pictures that make sensible page backgrounds.</summary>
        public IReadOnlyList<string> Backgrounds =>
            _archive == null ? [] : _archive.Paths
                .Where(p => p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) &&
                            (p.StartsWith("pdui\\", StringComparison.OrdinalIgnoreCase) || p.StartsWith("arenas\\", StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>A picture by its archive path ("pdui\bg_vrains.jpg"), or null.</summary>
        public Bitmap? Image(string path)
        {
            if (_archive == null || string.IsNullOrEmpty(path))
                return null;
            path = path.Replace('/', '\\');
            if (_images.TryGetValue(path, out var cached))
                return cached;

            Bitmap? image = null;
            try
            {
                // premultiplied ARGB: GDI+ draws it without converting every time (plain ARGB PNGs made STEAM_icons crawl)
                image = Wolf.Editors.Imaging.Decode(_archive.Read(path));
            }
            catch { image = null; }
            _images[path] = image;
            return image;
        }

        /// <summary>A sprite sheet by resource name ("pdui/doShared"), or null.</summary>
        public Atlas? Atlas(string resource)
        {
            if (_archive == null || string.IsNullOrEmpty(resource))
                return null;
            if (_atlases.TryGetValue(resource, out var cached))
                return cached;

            Atlas? atlas = null;
            string basePath = resource.Replace('/', '\\');
            byte[]? list = _archive.Read(basePath + ".dfymoo");
            Bitmap? sheet = Image(basePath + ".png");
            if (list != null && sheet != null)
                atlas = new Atlas { Resource = resource, Sheet = sheet, Sprites = ParseDfymoo(System.Text.Encoding.UTF8.GetString(list)) };
            _atlases[resource] = atlas;
            return atlas;
        }

        /// <summary>One sprite, untrimmed to its full size (transparent where the sheet trimmed it), or null.</summary>
        public Bitmap? Sprite(string resource, string name)
        {
            string key = resource + "|" + name;
            if (_sprites.TryGetValue(key, out var cached))
                return cached;

            Bitmap? sprite = null;
            var atlas = Atlas(resource);
            var info = atlas?.Find(name);
            if (atlas != null && info != null && info.Full.Width > 0 && info.Full.Height > 0)
            {
                sprite = new Bitmap(info.Full.Width, info.Full.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                using var g = Graphics.FromImage(sprite);
                g.DrawImage(atlas.Sheet, new Rectangle(info.Offset, info.Source.Size), info.Source, GraphicsUnit.Pixel);
            }
            _sprites[key] = sprite;
            return sprite;
        }

        /// <summary>
        /// tk2d sprite list: "n name", "s x y w h" (the rectangle on the sheet), optional "o x y w h" (where it sits in the untrimmed
        /// w x h picture), items separated by "~". The header ("i tk2d 1", "w", "h") comes before the first "~".
        /// </summary>
        internal static List<SpriteInfo> ParseDfymoo(string text)
        {
            var sprites = new List<SpriteInfo>();
            foreach (string block in text.Split('~', StringSplitOptions.RemoveEmptyEntries))
            {
                string? name = null;
                Rectangle source = Rectangle.Empty;
                Point offset = Point.Empty;
                Size full = Size.Empty;
                foreach (string raw in block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("n "))
                        name = line[2..].Trim();
                    else if (line.StartsWith("s ") && Numbers(line, out int[] s))
                        source = new Rectangle(s[0], s[1], s[2], s[3]);
                    else if (line.StartsWith("o ") && Numbers(line, out int[] o))
                    {
                        offset = new Point(o[0], o[1]);
                        full = new Size(o[2], o[3]);
                    }
                }
                if (name == null || source.IsEmpty)
                    continue;
                if (full.IsEmpty)
                    full = source.Size;
                sprites.Add(new SpriteInfo(name, source, offset, full));
            }
            return sprites;
        }

        private static bool Numbers(string line, out int[] values)
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            values = new int[4];
            if (parts.Length < 5)
                return false;
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i + 1], out values[i]))
                    return false;
            }
            return true;
        }

        public void Dispose()
        {
            foreach (var image in _images.Values)
                image?.Dispose();
            foreach (var sprite in _sprites.Values)
                sprite?.Dispose();
            _images.Clear();
            _sprites.Clear();
            _atlases.Clear();
        }
    }

    internal static class ArtDrawing
    {
        /// <summary>Draws a sprite stretched to the rectangle; false when the game's art is not available.</summary>
        public static bool DrawSprite(this Graphics g, GameArt art, string resource, string name, RectangleF rect)
        {
            var sprite = art.Sprite(resource, name);
            if (sprite == null)
                return false;
            g.DrawImage(sprite, rect);
            return true;
        }

        public static Size SpriteSize(this GameArt art, string resource, string name, Size fallback) =>
            art.Atlas(resource)?.Find(name)?.Full ?? fallback;

        /// <summary>Text the way the game's menus look: white with a dark outline, centred (or left aligned) in the rectangle.</summary>
        public static void DrawGameText(this Graphics g, string text, RectangleF rect, float pixelSize, Color colour, bool centred = true, bool bold = true)
        {
            if (string.IsNullOrEmpty(text) || pixelSize <= 0.5f)
                return;
            using var path = new GraphicsPath();
            using var family = new FontFamily("Segoe UI");
            using var format = new StringFormat
            {
                Alignment = centred ? StringAlignment.Center : StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
            };
            path.AddString(text, family, (int)(bold ? FontStyle.Bold : FontStyle.Regular), pixelSize, rect, format);
            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var outline = new Pen(Color.FromArgb(200, 0, 0, 0), Math.Max(1f, pixelSize / 7f)) { LineJoin = LineJoin.Round })
                g.DrawPath(outline, path);
            using (var fill = new SolidBrush(colour))
                g.FillPath(fill, path);
            g.SmoothingMode = smoothing;
        }
    }
}
