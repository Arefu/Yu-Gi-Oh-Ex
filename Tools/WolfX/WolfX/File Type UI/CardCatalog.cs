using StartingCollection;
using Wolf.Editors;

namespace WolfX.Types
{
    /// <summary>
    /// The card names every editor uses, read from the open game data (<see cref="GameFolderFiles.Current"/>: bin/CARD_IntID.bin,
    /// CARD_Indx_E.bin, CARD_Name_E.bin, out of the .dat or an extracted folder) plus the custom cards in Yu-Gi-Oh-Ex/cards.json.
    /// With nothing open, every card is known by its Konami id only, so the editors keep working.
    /// </summary>
    public static class CardCatalog
    {
        private static CardDatabase? _database;
        private static CardDatabase? _idsOnly;

        static CardCatalog() => GameFolderFiles.CurrentChanged += () => _database = null;

        /// <summary>The game folder of the open data (Yu-Gi-Oh-Ex is in it), or null when nothing is open.</summary>
        public static string? GameFolder => GameFolderFiles.Current?.GameFolder;

        /// <summary>The loaded names (the owner argument is kept for the callers; nothing is asked any more).</summary>
        public static CardDatabase Get(IWin32Window? owner = null)
        {
            if (_database != null)
                return _database;
            var files = GameFolderFiles.Current;
            if (files?.Available != true)
                return _idsOnly ??= CardDatabase.IdsOnly();
            var found = CardDatabase.Load(files.GameFolder);   // reads through GameFiles.OpenData, which is the open data
            return found.Cards.Count > 0 ? _database = found : _idsOnly ??= CardDatabase.IdsOnly();
        }

        /// <summary>Reads the names again (after cards.json or the name files changed).</summary>
        public static void Reload() => _database = null;

        public static string NameOf(int id) => Get().NameOf(id);
    }
}
