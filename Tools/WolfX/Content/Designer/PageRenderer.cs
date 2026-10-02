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

        private static void Save(DesignCanvas canvas, string path)
        {
            using var bitmap = canvas.Render();
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
