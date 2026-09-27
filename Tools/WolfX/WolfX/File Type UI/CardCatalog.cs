using System.ComponentModel;
using System.IO;
using StartingCollection;

namespace WolfX.Types
{
    /// <summary>
    /// The card names every editor uses. The first time a card is needed it asks for a folder and reads the names from the game's own
    /// files (bin/CARD_IntID.bin, CARD_Indx_E.bin, CARD_Name_E.bin). Any folder that has a bin folder with those in it works: the game
    /// folder (YuGiOh.exe), an extracted YGO_2020 folder, or MODS/OVERRIDES/REQ. The folder is remembered between runs.
    /// </summary>
    public static class CardCatalog
    {
        private static CardDatabase? _database;
        private static string? _folder;
        private static bool _declined;
        private static CardDatabase? _idsOnly;

        /// <summary>A folder to try before asking (WolfX gives its extracted YGO_2020 folder here).</summary>
        public static Func<string?>? DefaultFolder { get; set; }

        public static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfX", "gamefolder.txt");

        /// <summary>The game folder in use, or null until one has been picked.</summary>
        public static string? GameFolder => _folder ??= ReadRemembered();

        /// <summary>
        /// The loaded names. If no folder with the game's data can be found (or the user cancels the folder prompt) this still
        /// returns a database, one that knows every card only by its Konami id, so the editors keep working.
        /// </summary>
        public static CardDatabase Get(IWin32Window? owner)
        {
            if (_database != null)
                return _database;

            if (_declined)
                return _idsOnly ??= CardDatabase.IdsOnly();

            var candidates = new List<string?> { GameFolder, DefaultFolder?.Invoke() };
            foreach (string? candidate in candidates)
            {
                if (candidate == null || !Directory.Exists(candidate))
                    continue;

                var found = CardDatabase.Load(candidate);
                if (found.Cards.Count > 0)
                    return Use(candidate);
            }

            while (true)
            {
                string? folder = Ask(owner);
                if (folder == null)
                {
                    _declined = true; // don't ask again this run; use ids
                    return _idsOnly ??= CardDatabase.IdsOnly();
                }

                var found = CardDatabase.Load(folder);
                if (found.Cards.Count > 0)
                    return Use(folder);

                MessageBox.Show(owner, "That folder has no bin/CARD_Indx_E.bin, CARD_Name_E.bin and CARD_IntID.bin (looked in the folder, its YGO_2020, MODS/OVERRIDES/REQ and Remaining Files).", "Card names", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>Uses this game folder from now on (also reloads, so new custom cards show up).</summary>
        public static CardDatabase Use(string folder)
        {
            _folder = folder;
            _declined = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                File.WriteAllText(SettingsFile, folder);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }

            _database = CardDatabase.Load(folder);
            return _database;
        }

        public static void Reload()
        {
            if (_folder != null)
                _database = CardDatabase.Load(_folder);
        }

        public static string NameOf(int id) => _database?.NameOf(id) ?? $"#{id}";

        private static string? ReadRemembered()
        {
            try
            {
                return File.Exists(SettingsFile) ? File.ReadAllText(SettingsFile).Trim() : null;
            }
            catch (IOException) { return null; }
        }

        private static string? Ask(IWin32Window? owner)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the game folder (it reads YGO_2020.dat directly) or your extracted YGO_2020 folder so card names can be shown. Cancel to work with Konami ids only.",
                UseDescriptionForTitle = true,
            };
            return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
        }
    }
}
