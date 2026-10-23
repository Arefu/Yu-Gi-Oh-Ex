using System.Text.Json;
using WolfEx;

namespace WolfX
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // Headless check of the EffectScript compiler (no window): WolfX.exe --compile <script file> <result file> [Yu-Gi-Oh-Ex folder]
            if (args.Length >= 3 && args[0] == "--compile")
            {
                if (args.Length > 3)
                    ArchetypeCatalog.Load(args[3]);
                var result = EffectScriptCompiler.Compile(File.ReadAllText(args[1]));
                File.WriteAllText(args[2], result.Ok
                    ? "OK " + result.Compiled!.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = false })
                    : "ERROR " + result.Error);
                return;
            }

            // Summoning data for a whole cards.json (no window): WolfX.exe --attach-summoning <cards.json> <report file> [Yu-Gi-Oh-Ex folder]
            if (args.Length >= 3 && args[0] == "--attach-summoning")
            {
                if (args.Length > 3)
                    ArchetypeCatalog.Load(args[3]);
                File.WriteAllText(args[2], SummoningFromText.Attach(args[1]));
                return;
            }

            // Effects from card text for a whole cards.json (no window): WolfX.exe --attach-effects <cards.json> <report file> <work list csv> [Yu-Gi-Oh-Ex folder]
            if (args.Length >= 4 && args[0] == "--attach-effects")
            {
                if (args.Length > 4)
                    ArchetypeCatalog.Load(args[4]);
                File.WriteAllText(args[2], SummoningFromText.AttachEffects(args[1], args[3]));
                return;
            }

            // The parse trees the block editor gets (no window): WolfX.exe --script-tree <file with one script per line> <result file, one tree per line>
            // Headless check of the page script compiler: WolfX.exe --compile-page <script file> <result file>   (or --decompile-page <page json> <script file>)
            if (args.Length >= 3 && args[0] == "--compile-page")
            {
                try
                {
                    string pageName = Path.GetFileNameWithoutExtension(args[1]);
                    var page = PageScript.Compile(File.ReadAllText(args[1]), pageName);
                    var options = PageScript.Indented;
                    File.WriteAllText(args[2], page.Page.ToJsonString(options));
                    // the page's `edit` / `button on` lines go where the editor puts them: menus\bob-<name>.json next to the pages folder
                    // (they used to be appended to the page file, which the game can't read)
                    if (page.Menus != null)
                    {
                        string menus = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, "..", "menus");
                        Directory.CreateDirectory(menus);
                        File.WriteAllText(Path.Combine(menus, $"bob-{pageName}.json"), page.Menus.ToJsonString(options));
                    }
                }
                catch (PageScript.Error ex)
                {
                    File.WriteAllText(args[2], "ERROR " + ex.Message);
                }
                return;
            }
            if (args.Length >= 3 && args[0] == "--decompile-page")
            {
                File.WriteAllText(args[2], PageScript.Decompile((System.Text.Json.Nodes.JsonObject)System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(args[1]))!));
                return;
            }
            if (args.Length >= 3 && args[0] == "--script-tree")
            {
                File.WriteAllLines(args[2], File.ReadAllLines(args[1]).Select(line => EffectScriptTree.ToJson(line.Replace("\\n", "\n"))));
                return;
            }

            // Pictures of the page designer's widget galleries and pages (no window):
            // WolfX.exe --render-pages <game folder> <output folder> [zoom] -> gallery-<n>.png per widget group, and <page>.png per saved page
            if (args.Length >= 3 && args[0] == "--render-pages")
            {
                WolfEx.Designer.PageRenderer.RenderAll(args[1], args[2], args.Length > 3 ? float.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : 0.5f);
                return;
            }

            // The game's UI sprites (no window): WolfX.exe --sprites <game folder> <output folder> [resource ...] (see PageRenderer.ListSprites)
            if (args.Length >= 3 && args[0] == "--sprites")
            {
                WolfEx.Designer.PageRenderer.ListSprites(args[1], args[2], args.Skip(3));
                return;
            }

            // WolfX.exe --page "Card genres": open on that page
            if (args.Length >= 2 && args[0] == "--page")
                WolfUI.StartPage = args[1];
            // WolfX.exe --card 4007: open the Card Manager on that card
            if (args.Length >= 2 && args[0] == "--card" && int.TryParse(args[1], out int card))
                WolfUI.StartCard = card;

            // Anything that goes wrong is also written to %APPDATA%\WolfX\crash.log (a crash on launch otherwise leaves nothing to read).
            string log = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfX", "crash.log");
            void Record(object? error)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                    File.AppendAllText(log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {error}{Environment.NewLine}{Environment.NewLine}");
                }
                catch { /* nothing more can be done */ }
            }
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Record(e.ExceptionObject);
            Application.ThreadException += (_, e) =>
            {
                Record(e.Exception);
                MessageBox.Show(e.Exception.Message + Environment.NewLine + "Details: " + log, "WolfX", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            try
            {
                Application.Run(new WolfUI());
            }
            catch (Exception e)
            {
                Record(e);
                MessageBox.Show("WolfX could not start: " + e.Message + Environment.NewLine + "Details: " + log, "WolfX", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
