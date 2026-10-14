using System.Text;

namespace Wolf.Editors
{
    /// <summary>
    /// The sprite names in one of the game's .dfymoo sheets, as the game will read it (the Ex patch's copy when there is one), so pickers
    /// offer sprites added on the Sprite sheets page (Add sprite from PNG): series logos (pdui\SeriesLogo "logo_*"), shop row banners
    /// (pdui\SeriesLogo "wrap_series_*"), pack art (pdui\PackWrappers "wrap_*").
    /// </summary>
    public static class SheetNames
    {
        public const string SeriesLogo = @"pdui\SeriesLogo.dfymoo";
        public const string PackWrappers = @"pdui\PackWrappers.dfymoo";

        /// <summary>The part after <paramref name="prefix"/> of every sprite whose name starts with it, in sheet order.</summary>
        public static List<string> WithPrefix(GameFolderFiles? files, string sheet, string prefix)
        {
            var names = new List<string>();
            if (files?.Read(sheet) is { } data)
                foreach (string raw in Encoding.ASCII.GetString(data).Split('\n'))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("n " + prefix))
                        names.Add(line[(2 + prefix.Length)..]);
                }
            return names;
        }
    }
}
