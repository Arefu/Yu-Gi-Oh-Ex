using StartingCollection;
using Types;
using System.Text.Json.Nodes;

namespace Wolf.Mods;

/// <summary>Reduce installed game-file overlays against the untouched game archive.</summary>
public static class ModDelta
{
    public static void Normalize(string gameFolder, string modFolder)
    {
        var baseline = TocArchive.TryOpen(Path.Combine(gameFolder, "YGO_2020.toc"));
        // An edited archive is not a trustworthy common ancestor.
        if (baseline == null || baseline.IsModified) return;
        string toc = Path.Combine(modFolder, "YGO_2020-Ex.toc");
        var patch = TocArchive.TryOpen(toc);
        string loose = Path.Combine(modFolder, "YGO_2020");
        var remaining = patch?.Paths.ToDictionary(p => p, p => TocArchive.PatchSource.From(patch!, p), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, TocArchive.PatchSource>(StringComparer.OrdinalIgnoreCase);
        var removeLoose = new List<string>();
        var additions = new Dictionary<int, Character>();
        foreach (string path in remaining.Keys.Concat(Directory.Exists(loose)
                     ? Directory.EnumerateFiles(loose, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(loose, f))
                     : []).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (baseline.SizeOf(path) > 32 * 1024 * 1024)
            {
                string largeLoose = Path.Combine(loose, path);
                bool SameLarge(long size, Func<long, int, byte[]?> read)
                {
                    if (size != baseline.SizeOf(path)) return false;
                    for (long at = 0; at < size; at += 1 << 20)
                    {
                        int count = (int)Math.Min(1 << 20, size - at);
                        if (read(at, count) is not byte[] data || data.Length != count
                            || !data.AsSpan().SequenceEqual(baseline.Read(path, at, count))) return false;
                    }
                    return true;
                }
                bool same = patch?.Contains(path) != true || SameLarge(patch.SizeOf(path), (at, count) => patch.Read(path, at, count));
                if (same && File.Exists(largeLoose))
                {
                    using var stream = File.OpenRead(largeLoose);
                    same = SameLarge(stream.Length, (at, count) =>
                    {
                        stream.Position = at; byte[] data = new byte[count]; stream.ReadExactly(data); return data;
                    });
                }
                if (same)
                {
                    remaining.Remove(path);
                    if (File.Exists(largeLoose)) removeLoose.Add(largeLoose);
                }
                continue;
            }
            byte[]? original = baseline.Read(path);
            if (original == null) continue;
            string loosePath = Path.Combine(loose, path);
            // Both sources must be safe to remove regardless of the user's FileOrder setting.
            var sources = new List<byte[]>();
            if (patch?.Read(path) is byte[] packed) sources.Add(packed);
            if (File.Exists(loosePath)) sources.Add(File.ReadAllBytes(loosePath));
            var converted = new List<Character>();
            bool safe = true;
            foreach (byte[] data in sources)
            {
                if (data.AsSpan().SequenceEqual(original)) continue;
                string name = Path.GetFileName(path.Replace('\\', Path.DirectorySeparatorChar));
                char language = name.Length == 14 ? char.ToUpperInvariant(name[9]) : '\0';
                if (!path.Replace('\\', '/').StartsWith("main/", StringComparison.OrdinalIgnoreCase)
                    || !name.Equals(CharacterTable.FileName(language), StringComparison.OrdinalIgnoreCase)
                    || !CharacterTable.AllLanguages.Contains(language)) { safe = false; break; }
                var delta = Additions(original, data, language);
                if (delta == null) { safe = false; break; }
                converted.AddRange(delta);
            }
            if (!safe) continue;
            // A character must have consistent metadata across sources and languages.
            foreach (var c in converted)
            {
                if (additions.TryGetValue(c.Id, out var old))
                {
                    var copy = old.Clone();
                    copy.Names.Clear(); copy.Bios.Clear();
                    foreach (var t in c.Names) copy.Names[t.Key] = t.Value;
                    foreach (var t in c.Bios) copy.Bios[t.Key] = t.Value;
                    if (!copy.SameAs(c)) return; // leave the entire package intact
                    if (c.Names.Any(t => old.Names.TryGetValue(t.Key, out var value) && value != t.Value)
                        || c.Bios.Any(t => old.Bios.TryGetValue(t.Key, out var value) && value != t.Value)) return;
                    foreach (var t in c.Names) old.Names[t.Key] = t.Value;
                    foreach (var t in c.Bios) old.Bios[t.Key] = t.Value;
                }
                else additions[c.Id] = c.Clone();
            }
            remaining.Remove(path);
            if (File.Exists(loosePath)) removeLoose.Add(loosePath);
        }
        string jsonPath = Path.Combine(modFolder, "Yu-Gi-Oh-Ex", "characters.json");
        if (additions.Count > 0)
        {
            var root = CharacterJson.Load(jsonPath);
            var entries = root["characters"] as JsonArray;
            if (root.ContainsKey("characters") && entries == null) return;
            if (entries == null) root["characters"] = entries = new JsonArray();
            var explicitIds = entries.OfType<JsonObject>().Select(e => e["id"]?.GetValue<int>()).ToHashSet();
            foreach (var c in additions.Values.OrderBy(c => c.Id))
                if (!explicitIds.Contains(c.Id)) entries.Add(CharacterJson.ToJson(c, true));
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            CharacterJson.Save(jsonPath, root);
        }
        if (patch != null && remaining.Count != patch.Count)
        {
            // Stream from the old archive into a separate pair before replacing it.
            string reduced = Path.Combine(modFolder, "delta-" + Guid.NewGuid().ToString("N") + ".toc");
            TocArchive.WritePatch(reduced, remaining);
            File.Move(Path.ChangeExtension(reduced, ".dat"), Path.ChangeExtension(toc, ".dat"), true);
            File.Move(reduced, toc, true);
        }
        foreach (string file in removeLoose) File.Delete(file);
    }

    public static List<Character>? Additions(byte[] original, byte[] modified, char language)
    {
        var before = CharacterTable.Parse(new Dictionary<char, byte[]> { [language] = original });
        var after = CharacterTable.Parse(new Dictionary<char, byte[]> { [language] = modified });
        if (before.HeaderPad != after.HeaderPad || after.Characters.Select(c => c.Id).Distinct().Count() != after.Characters.Count
            || before.Characters.Any(c => after.Find(c.Id) is not Character a || !c.SameAs(a))) return null;
        var added = after.Characters.Where(c => before.Find(c.Id) == null).ToList();
        // characters.json cannot represent the reserved Field1C value; preserve such files as binaries.
        return added.All(c => c.Id >= 0 && c.Id < CharacterTable.SlotCount && c.Field1C == 0) ? added : null;
    }
}
