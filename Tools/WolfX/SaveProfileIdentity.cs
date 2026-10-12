namespace WolfX
{
    internal static class SaveProfileIdentity
    {
        public static string Describe(string path, string? gameFolder, string primaryName, string? steamFolder)
        {
            string full = Path.GetFullPath(path);
            if (steamFolder != null)
            {
                string relative = Path.GetRelativePath(Path.Combine(steamFolder, "userdata"), full);
                string[] parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (parts.Length == 4 && ulong.TryParse(parts[0], out _) && parts[1] == "1150640" &&
                    parts[2].Equals("remote", StringComparison.OrdinalIgnoreCase))
                    return $"Steam save — account {parts[0]}. Used by normal Steam launches; Steam Cloud may sync this file.";
            }
            if (gameFolder != null)
            {
                string primary = Path.GetFullPath(Path.Combine(gameFolder, primaryName));
                for (int slot = 1; slot <= 5; slot++)
                {
                    string candidate = SlotPath(primary, slot);
                    if (full.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                        return $"Toolkit profile — slot {slot}, configured by GameSaveName. Used by the toolkit's local save redirect.";
                }
                if (Path.GetDirectoryName(full)!.Equals(Path.GetFullPath(gameFolder).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                    return "Game folder save — not a slot of the currently configured GameSaveName.";
            }
            return "Standalone save file — its use by Steam or the toolkit has not been identified.";
        }

        public static string SlotPath(string primary, int slot) => slot == 1 ? primary :
            Path.Combine(Path.GetDirectoryName(primary)!, Path.GetFileNameWithoutExtension(primary) + "-" + slot + Path.GetExtension(primary));
    }
}
