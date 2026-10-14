namespace WolfEx.Designer
{
    /// <summary>Headless pictures of pages (WolfX.exe --render-pages): every widget gallery, and every page in Yu-Gi-Oh-Ex\pages.</summary>
    internal static class PageRenderer
    {
        public static void RenderAll(string gameFolder, string outputFolder, float zoom)
        {
            Directory.CreateDirectory(outputFolder);
            using var art = GameArt.Open(Wolf.Editors.GameFolderFiles.Open(gameFolder));
            using var canvas = new DesignCanvas { Art = art, Zoom = zoom };

            int n = 1;
            foreach (string category in WidgetCatalog.Categories)
            {
                canvas.Document = PagesPanel.BuildGallery(category);
                Save(canvas, Path.Combine(outputFolder, $"gallery-{n++}.png"));
            }

            string pages = Path.Combine(gameFolder, "Yu-Gi-Oh-Ex", "pages");
            if (!Directory.Exists(pages))
                return;
            foreach (string file in Directory.GetFiles(pages, "*.json"))
            {
                canvas.Document = PageDocument.FromJson(File.ReadAllText(file));
                Save(canvas, Path.Combine(outputFolder, Path.GetFileNameWithoutExtension(file) + ".png"));
            }
        }

        /// <summary>
        /// WolfX.exe --sprites &lt;game folder&gt; &lt;output folder&gt; [resource ...]: sprites.txt lists every sheet's sprites with their full size;
        /// for each resource named, every sprite is also saved as &lt;resource&gt;\&lt;sprite&gt;.png (to see which art a control can use).
        /// </summary>
        public static void ListSprites(string gameFolder, string outputFolder, IEnumerable<string> export)
        {
            Directory.CreateDirectory(outputFolder);
            using var art = GameArt.Open(Wolf.Editors.GameFolderFiles.Open(gameFolder));
            var lines = new List<string>();
            foreach (string resource in art.AtlasResources)
            {
                var atlas = art.Atlas(resource);
                if (atlas == null)
                    continue;
                lines.Add($"[{resource}] {atlas.Sprites.Count} sprite(s)");
                lines.AddRange(atlas.Sprites.Select(s => $"  {s.Name} {s.Full.Width}x{s.Full.Height}"));
            }
            File.WriteAllLines(Path.Combine(outputFolder, "sprites.txt"), lines);

            foreach (string resource in export)
            {
                var atlas = art.Atlas(resource);
                if (atlas == null)
                    continue;
                string folder = Path.Combine(outputFolder, resource.Replace('/', '_'));
                Directory.CreateDirectory(folder);
                foreach (var sprite in atlas.Sprites)
                    art.Sprite(resource, sprite.Name)?.Save(Path.Combine(folder, string.Concat(sprite.Name.Split(Path.GetInvalidFileNameChars())) + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void Save(DesignCanvas canvas, string path)
        {
            using var bitmap = canvas.Render();
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
