namespace ModManager
{
    /// <summary>
    /// Yu-Gi-Oh-Ex Mod Manager: installs mod .zips into &lt;game&gt;\Mods, switches them on and off, sets the load order, says what each needs and
    /// where mods clash, and packs new mods (docs/Mods.md).
    ///   ModManager.exe [--game &lt;game folder&gt;] [--create] [mod.zip ...]
    /// A .zip given on the command line (or opened with the Mod Manager) is installed.
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            string? game = null;
            bool create = false;
            var zips = new List<string>();
            for (int i = 0; i < args.Length; ++i)
            {
                if (args[i].Equals("--game", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    game = args[++i];
                else if (args[i].Equals("--create", StringComparison.OrdinalIgnoreCase))
                    create = true;
                else if (args[i].EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    zips.Add(args[i]);
            }
            Application.Run(new MainForm(game, create, zips));
        }
    }
}
