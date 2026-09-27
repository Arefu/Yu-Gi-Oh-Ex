using System.IO;
using StartingCollection;

namespace WolfX.Types
{
    /// <summary>Things the pack and deck pages share: backups, and finding the .zib archives near an opened file.</summary>
    internal static class RecordFileHelpers
    {
        /// <summary>Keeps the first version of a file as .bak (never overwrites an existing backup).</summary>
        internal static void Backup(string path)
        {
            if (File.Exists(path) && !File.Exists(path + ".bak"))
                File.Copy(path, path + ".bak");
        }

        /// <summary>Looks for an archive (packs.zib, decks.zib) near the opened file and in the remembered game folder.</summary>
        internal static string? FindArchive(string openedFile, string archiveName)
        {
            for (var directory = new DirectoryInfo(Path.GetDirectoryName(openedFile) ?? "."); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, archiveName);
                if (File.Exists(candidate))
                    return candidate;
            }

            string? folder = CardCatalog.GameFolder;
            return folder != null && Directory.Exists(folder) ? GameFiles.FromGameFolder(folder).Find(archiveName) : null;
        }
    }
}
