using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// Puts one picture into a game sheet (a .dfymoo sprite list over its .png) and saves both into the game data (the Ex patch): the same
    /// as the Sprite sheets page's Replace sprite image / Add sprite from PNG, for pages that set one sprite (a duelist's portrait).
    /// </summary>
    public static class SheetEdit
    {
        /// <summary>
        /// Sets sprite <paramref name="sprite"/> of sheet <paramref name="resource"/> ("pdui\chars", no extension) to the picture: an existing
        /// sprite is painted over (stretched to its place), a new one is added under the sheet's picture. Returns what it did, for a status line.
        /// </summary>
        public static string SetSprite(GameFolderFiles files, string resource, string sprite, byte[] png)
        {
            if (files.Read(resource + ".dfymoo") is not { } list || files.Read(resource + ".png") is not { } sheetPng)
                throw new InvalidDataException($"{resource} isn't in the game data.");
            var sheet = DfymooSheet.Parse(System.Text.Encoding.UTF8.GetString(list));
            using var picture = Imaging.Decode(png) ?? throw new InvalidDataException("The picture couldn't be read.");
            using var old = Imaging.Decode(sheetPng) ?? throw new InvalidDataException($"{resource}.png couldn't be read.");

            Bitmap result;
            string what;
            if (sheet.Sprites.FirstOrDefault(s => s.Name.Equals(sprite, StringComparison.OrdinalIgnoreCase)) is { } existing)
            {
                result = new Bitmap(old);
                using var g = Graphics.FromImage(result);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(picture, new Rectangle(existing.X, existing.Y, existing.Width, existing.Height));
                what = $"{sprite} replaced in {resource} ({picture.Width} x {picture.Height} stretched to {existing.Width} x {existing.Height})";
            }
            else
            {
                int top = old.Height + 2;
                result = new Bitmap(Math.Max(old.Width, picture.Width), top + picture.Height, PixelFormat.Format32bppArgb);
                using var g = Graphics.FromImage(result);
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(old, 0, 0, old.Width, old.Height);
                g.DrawImage(picture, 0, top, picture.Width, picture.Height);
                sheet.Width = result.Width;
                sheet.Height = result.Height;
                sheet.Sprites.Add(new DfymooSprite { Name = sprite, X = 0, Y = top, Width = picture.Width, Height = picture.Height, FullWidth = picture.Width, FullHeight = picture.Height });
                what = $"{sprite} added to {resource}";
            }
            using (result)
            using (var stream = new MemoryStream())
            {
                result.Save(stream, ImageFormat.Png);
                files.Write(new Dictionary<string, byte[]>
                {
                    [resource + ".dfymoo"] = System.Text.Encoding.UTF8.GetBytes(sheet.ToText()),
                    [resource + ".png"] = stream.ToArray(),
                });
            }
            return what + $", saved into {files.Describe(resource + ".dfymoo")}.";
        }
    }
}
