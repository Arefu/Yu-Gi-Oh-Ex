using Wolf.Editors;

string root = Path.Combine(Path.GetTempPath(), "WolfX-ContentSafety-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name); passed++;
}
try
{
    string json = Path.Combine(root, "definition.json"), png = Path.Combine(root, "image.png");
    File.WriteAllText(json, "old definition"); File.WriteAllBytes(png, [1, 2, 3]);
    var files = new Dictionary<string, byte[]> { [json] = ContentFileSave.Text("new definition"), [png] = [4, 5, 6] };
    using (var locked = new FileStream(png, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        Check(!ContentFileSave.TryWrite(files, out string error) && error.StartsWith("Not saved:"), "locked image reports failed save");
        Check(File.ReadAllText(json) == "old definition", "locked second target preserves definition");
    }
    Check(ContentFileSave.TryWrite(files, out _), "retry succeeds");
    Check(File.ReadAllText(json) == "new definition" && File.ReadAllBytes(png).SequenceEqual(new byte[] { 4, 5, 6 }), "definition and replacement image saved together");
    Check(Directory.GetFiles(root, "definition.json.*.bak").Any(p => File.ReadAllText(p) == "old definition") &&
        Directory.GetFiles(root, "image.png.*.bak").Any(p => File.ReadAllBytes(p).SequenceEqual(new byte[] { 1, 2, 3 })), "backups preserve exact original contents");
    string blocked = Path.Combine(root, "directory-target"); Directory.CreateDirectory(blocked);
    Check(!ContentFileSave.TryWrite(new Dictionary<string, byte[]> { [json] = [7], [blocked] = [8] }, out _), "replacement failure reported");
    Check(File.ReadAllText(json) == "new definition", "earlier replacement rolled back");
    string added = Path.Combine(root, "new-file");
    Check(!ContentFileSave.TryWrite(new Dictionary<string, byte[]> { [added] = [7], [blocked] = [8] }, out _) && !File.Exists(added), "newly created member rolled back");
    File.SetAttributes(png, FileAttributes.ReadOnly);
    Check(!ContentFileSave.TryWrite(files, out _) && File.ReadAllText(json) == "new definition", "read-only image preserves definition");
    File.SetAttributes(png, FileAttributes.Normal);
    Check(!Directory.GetFiles(root, "*.tmp").Any(), "temporary files cleaned after success and failure");
    ContentFileSave.WriteText(json, "é日本語");
    Check(File.ReadAllBytes(json).SequenceEqual(ContentFileSave.Text("é日本語")), "UTF-8 text remains BOM-free");
    Check(Directory.GetFiles(root, "definition.json.*.bak").Select(File.ReadAllText).Contains("new definition"), "successive saves retain separate backups");
    Console.WriteLine($"{passed} content-save checks passed.");
}
finally
{
    foreach (string file in Directory.GetFiles(root)) File.SetAttributes(file, FileAttributes.Normal);
    Directory.Delete(root, true);
}
