using System.Text;

namespace Wolf.Editors
{
    /// <summary>Stages a group of content files before replacing any target. Keeps the originals as backups.</summary>
    public static class ContentFileSave
    {
        private sealed record Entry(string Target, string Temp, string? Backup);

        public static byte[] Text(string value) => new UTF8Encoding(false).GetBytes(value);

        public static void WriteText(string path, string value) => Write(new Dictionary<string, byte[]> { [path] = Text(value) });
        public static void WriteBytes(string path, byte[] value) => Write(new Dictionary<string, byte[]> { [path] = value });

        public static bool TryWrite(IReadOnlyDictionary<string, byte[]> files, out string error)
        {
            try { Write(files); error = ""; return true; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { error = "Not saved: " + ex.Message; return false; }
        }

        public static void Write(IReadOnlyDictionary<string, byte[]> files)
        {
            var entries = new List<Entry>();
            var committed = new List<Entry>();
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N");
            try
            {
                foreach (var (path, bytes) in files)
                {
                    string target = Path.GetFullPath(path);
                    if (!targets.Add(target)) throw new IOException("Duplicate save target: " + target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    string temp = target + "." + stamp + ".tmp";
                    string? backup = File.Exists(target) ? target + "." + stamp + ".bak" : null;
                    var entry = new Entry(target, temp, backup);
                    entries.Add(entry);
                    using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    { stream.Write(bytes); stream.Flush(true); }
                    if (backup != null)
                    {
                        // Detect locked/read-only files before replacing any member of the batch.
                        using (var existing = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                        File.Copy(target, backup, false);
                    }
                }
                foreach (var entry in entries)
                {
                    if (entry.Backup != null) File.Replace(entry.Temp, entry.Target, null);
                    else File.Move(entry.Temp, entry.Target);
                    committed.Add(entry);
                }
            }
            catch (Exception failure)
            {
                var rollbackErrors = new List<string>();
                foreach (var entry in committed.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (entry.Backup == null) File.Delete(entry.Target);
                        else
                        {
                            File.Copy(entry.Backup, entry.Temp, true);
                            File.Replace(entry.Temp, entry.Target, null);
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { rollbackErrors.Add(entry.Target + ": " + ex.Message + ". Original backup: " + entry.Backup); }
                }
                if (rollbackErrors.Count > 0)
                    throw new IOException(failure.Message + "\nSome files could not be restored:\n" + string.Join("\n", rollbackErrors), failure);
                throw;
            }
            finally
            {
                foreach (var entry in entries)
                    try { File.Delete(entry.Temp); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Keep the original error. */ }
            }
        }
    }
}
