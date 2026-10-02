using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WolfX
{
    /// <summary>Finds the Steam install of the game (Steam's own folder and every library listed in its libraryfolders.vdf).</summary>
    internal static partial class GameLocator
    {
        private const string GameFolderName = "Yu-Gi-Oh! Legacy of the Duelist Link Evolution";

        /// <summary>The game folder (with YGO_2020.toc), or null.</summary>
        public static string? FindSteamInstall()
        {
            try
            {
                string? steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrEmpty(steam))
                    return null;
                var libraries = new List<string> { steam.Replace('/', '\\') };
                string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                    foreach (Match match in LibraryPath().Matches(File.ReadAllText(vdf)))
                        libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
                foreach (string library in libraries)
                {
                    string folder = Path.Combine(library, "steamapps", "common", GameFolderName);
                    if (File.Exists(Path.Combine(folder, "YGO_2020.toc")))
                        return folder;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // not found is fine: the user picks the folder
            }
            return null;
        }

        [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
        private static partial Regex LibraryPath();
    }
}
