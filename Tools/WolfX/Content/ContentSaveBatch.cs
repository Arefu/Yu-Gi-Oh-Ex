using System.Text;

namespace WolfEx
{
    /// <summary>Stages related writes and removals together, with original-file backups and rollback.</summary>
    internal static class ContentSaveBatch
    {
        private sealed record Entry(string Target, string? Temp, string? Backup);
        internal static byte[] Text(string text) => new UTF8Encoding(false).GetBytes(text);

        internal static void WriteText(string path, string text) => Write(new Dictionary<string, byte[]> { [path] = Text(text) });
        internal static void Remove(string path) => Write(new Dictionary<string, byte[]>(), [path]);

        internal static void Write(IReadOnlyDictionary<string, byte[]> files, IEnumerable<string>? removals = null)
        {
            var entries = new List<Entry>();
            var committed = new List<Entry>();
            var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N");
            try
            {
                void Stage(string path, byte[]? bytes)
                {
                    string target = Path.GetFullPath(path);
                    if (!targets.Add(target)) throw new IOException("Duplicate save target: " + target);
                    if (Directory.Exists(target)) throw new IOException("Save target is a directory: " + target);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    string? temp = bytes == null ? null : target + "." + stamp + ".tmp";
                    string? backup = File.Exists(target) ? target + "." + stamp + ".bak" : null;
                    entries.Add(new Entry(target, temp, backup));
                    if (temp != null)
                        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { stream.Write(bytes!); stream.Flush(true); }
                    if (backup != null)
                    {
                        using (var existing = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                        File.Copy(target, backup, false);
                    }
                }
                foreach (var (path, bytes) in files) Stage(path, bytes);
                foreach (string path in removals ?? []) Stage(path, null);
                foreach (var entry in entries)
                {
                    if (entry.Temp == null) File.Delete(entry.Target);
                    else if (entry.Backup == null) File.Move(entry.Temp, entry.Target);
                    else File.Replace(entry.Temp, entry.Target, null);
                    committed.Add(entry);
                }
            }
            catch (Exception failure)
            {
                var errors = new List<string>();
                foreach (var entry in committed.AsEnumerable().Reverse())
                    try
                    {
                        if (entry.Backup == null) File.Delete(entry.Target);
                        else
                        {
                            string restore = entry.Target + "." + stamp + ".restore.tmp";
                            try
                            {
                                File.Copy(entry.Backup, restore, false);
                                if (File.Exists(entry.Target)) File.Replace(restore, entry.Target, null);
                                else File.Move(restore, entry.Target);
                            }
                            finally
                            {
                                try { File.Delete(restore); }
                                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    { errors.Add(entry.Target + ": " + ex.Message + ". Original backup: " + entry.Backup); }
                if (errors.Count > 0)
                    throw new IOException(failure.Message + "\nSome originals could not be restored:\n" + string.Join("\n", errors), failure);
                throw;
            }
            finally
            {
                foreach (var entry in entries.Where(e => e.Temp != null))
                    try { File.Delete(entry.Temp!); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Preserve the save error. */ }
            }
        }
    }
}
