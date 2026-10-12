using System.Text.Json;
using System.Text.Json.Nodes;
using StartingCollection;
using Types;

namespace Wolf.Mods;

/// <summary>Produces a separate merged overlay. Originals and mod packages are never rewritten.</summary>
public static class ModMergeArchive
{
    public const string ManifestName = "merged.json";
    private const int Version = 1;
    private sealed record Source(Mod Mod, TocArchive? Patch, string Loose, bool PatchFirst)
    {
        public byte[]? Read(string path) => Read(path, 0, checked((int)Size(path)));
        public long Size(string path)
        {
            string file = Path.Combine(Loose, path);
            if (PatchFirst && Patch?.Contains(path) == true) return Patch.SizeOf(path);
            if (File.Exists(file)) return new FileInfo(file).Length;
            return Patch?.SizeOf(path) ?? -1;
        }
        public byte[]? Read(string path, long start, int count)
        {
            string file = Path.Combine(Loose, path);
            if (PatchFirst && Patch?.Contains(path) == true) return Patch.Read(path, start, count);
            if (!File.Exists(file)) return Patch?.Read(path, start, count);
            using var stream = File.OpenRead(file);
            stream.Position = start;
            byte[] bytes = new byte[count];
            stream.ReadExactly(bytes);
            return bytes;
        }
        public IEnumerable<string> Paths => (Patch?.Paths ?? []).Concat(Directory.Exists(Loose)
            ? Directory.EnumerateFiles(Loose, "*", SearchOption.AllDirectories).Select(p => Path.GetRelativePath(Loose, p)) : [])
            .Select(ArchivePath).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<ModIssue> Prepare(string gameFolder, IReadOnlyList<Mod> mods, string archiveName = "YGO_2020", bool patchFirst = false)
    {
        var active = mods.Where(m => m.Enabled && !m.IsLocal).ToList();
        string manifestPath = Path.Combine(gameFolder, "Mods", ManifestName);
        var inputs = Snapshot(gameFolder, active, archiveName);
        var ids = new JsonArray(active.Select(m => (JsonNode?)JsonValue.Create(m.Id)).ToArray());
        JsonObject? old = null;
        try { old = File.Exists(manifestPath) ? JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject : null; }
        catch (JsonException) { /* A broken cache is rebuilt from source mods. */ }
        if (CacheMatches(gameFolder, old, archiveName, patchFirst, ids, inputs))
            return ReadIssues(old!);

        var sources = active.Select(m => new Source(m, OpenPatch(m.PatchToc), m.LooseFolder, patchFirst)).ToList();
        var owners = new Dictionary<string, List<Source>>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
            foreach (string path in source.Paths)
            {
                if (!owners.TryGetValue(path, out var list)) owners[path] = list = [];
                list.Add(source);
            }
        var baseline = TocArchive.TryOpen(Path.Combine(gameFolder, archiveName + ".toc"));
        var issues = new List<ModIssue>();
        var output = new Dictionary<string, TocArchive.PatchSource>(StringComparer.OrdinalIgnoreCase);
        var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (baseline == null || baseline.IsModified)
        {
            foreach (var pair in owners.Where(p => p.Value.Count > 1))
                issues.Add(new(IssueLevel.Warning, pair.Value[^1].Mod.Id,
                    $"{pair.Key}: no untouched game archive is available; later whole file wins."));
        }
        else
        {
            foreach (var group in TextGroups(owners.Keys))
            {
                var providers = sources.Where(s => group.Paths.Any(p => owners.TryGetValue(p, out var list) && list.Contains(s))).ToList();
                if (providers.Count < 2) continue;
                foreach (string path in group.Paths) processed.Add(path);
                try
                {
                    var original = group.Paths.Select(p => baseline.Read(p) ?? throw new InvalidDataException("Missing original text-table component.")).ToArray();
                    var current = original;
                    foreach (var source in providers)
                    {
                        var incoming = group.Paths.Select((p, i) => source.Size(p) < 0 ? original[i] : source.Read(p)!).ToArray();
                        var merged = MergeText(group.CardText, original, current, incoming);
                        current = merged.Files;
                        Report(issues, source.Mod, string.Join(" + ", group.Paths), merged.Conflicts);
                    }
                    for (int i = 0; i < group.Paths.Length; i++) output[group.Paths[i]] = TocArchive.PatchSource.Of(current[i]);
                }
                catch (Exception ex) when (IsFormatError(ex))
                {
                    issues.Add(new(IssueLevel.Warning, providers[^1].Mod.Id,
                        $"{string.Join(" + ", group.Paths)}: cannot merge text components ({ex.Message}); original load order applies."));
                }
            }
            foreach (var pair in owners.Where(p => p.Value.Count > 1 && !processed.Contains(p.Key)))
            {
                string path = pair.Key;
                if (path.EndsWith(".zib", StringComparison.OrdinalIgnoreCase) && baseline.Contains(path))
                {
                    try { output[path] = MergeZib(baseline, path, pair.Value, issues); }
                    catch (Exception ex) when (IsFormatError(ex))
                    {
                        issues.Add(new(IssueLevel.Warning, pair.Value[^1].Mod.Id,
                            $"{path}: cannot reconcile archive entries ({ex.Message}); later whole file wins."));
                    }
                    continue;
                }
                byte[]? original = baseline.Read(path);
                byte[]? current = original;
                foreach (var source in pair.Value)
                {
                    byte[] incoming = source.Read(path)!;
                    if (original == null)
                    {
                        if (current != null && !current.AsSpan().SequenceEqual(incoming))
                            Report(issues, source.Mod, path, ["new file supplied by more than one mod: later whole file wins"]);
                        current = incoming;
                    }
                    else
                    {
                        var merged = BinaryModMerge.Merge(path, original, current!, incoming);
                        current = merged.Data;
                        Report(issues, source.Mod, path, merged.Conflicts);
                    }
                }
                output[path] = TocArchive.PatchSource.Of(current!);
            }
        }

        // A unique archive stays immutable while the running game may still have its previous version mounted.
        string patchName = "";
        if (output.Count > 0)
        {
            patchName = @"Mods\.merged\" + Guid.NewGuid().ToString("N") + @"\overlay";
            string toc = Path.Combine(gameFolder, patchName + ".toc");
            Directory.CreateDirectory(Path.GetDirectoryName(toc)!);
            TocArchive.WritePatch(toc, output);
        }
        if (!JsonNode.DeepEquals(inputs, Snapshot(gameFolder, active, archiveName)))
            throw new IOException("Mod files changed while merging; refresh to rebuild the merged archive.");
        var manifest = new JsonObject
        {
            ["version"] = Version, ["archive"] = archiveName, ["patchFirst"] = patchFirst,
            ["mods"] = ids, ["inputs"] = inputs, ["patch"] = patchName,
            ["outputs"] = patchName.Length == 0 ? new JsonArray() : Stamps(gameFolder, [patchName + ".toc", patchName + ".dat"]),
            ["issues"] = JsonSerializer.SerializeToNode(issues),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        string temp = manifestPath + ".tmp";
        File.WriteAllText(temp, manifest.ToJsonString());
        File.Move(temp, manifestPath, true);
        CleanCache(gameFolder, patchName);
        return issues;
    }

    private static void CleanCache(string gameFolder, string keep)
    {
        if (ModLibrary.IsGameRunning()) return;
        string root = Path.GetFullPath(Path.Combine(gameFolder, "Mods", ".merged"));
        if (!Directory.Exists(root) || (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) return;
        string kept = keep.Length == 0 ? "" : Path.GetFullPath(Path.GetDirectoryName(Path.Combine(gameFolder, keep))!);
        foreach (string folder in Directory.EnumerateDirectories(root))
        {
            string full = Path.GetFullPath(folder), name = Path.GetFileName(folder);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || full.Equals(kept, StringComparison.OrdinalIgnoreCase) || name.Length != 32 || !name.All(char.IsAsciiHexDigit)
                || (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateDirectories(folder).Any()
                || Directory.EnumerateFiles(folder).Any(p => Path.GetFileName(p) is not ("overlay.toc" or "overlay.dat" or "overlay.toc.tmp" or "overlay.dat.tmp"))) continue;
            try { Directory.Delete(full, true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* An open archive can be collected after the game exits. */ }
        }
    }

    private static TocArchive? OpenPatch(string path)
    {
        var patch = TocArchive.TryOpen(path);
        if (patch == null && (File.Exists(path) || File.Exists(Path.ChangeExtension(path, ".dat"))))
            throw new InvalidDataException("Incomplete or unreadable mod archive: " + path);
        return patch;
    }

    // Mirrors Core's MergeArchive.h. Inventory includes absent files so deletion or a newly added loose file invalidates the cache.
    public static JsonArray Snapshot(string gameFolder, IReadOnlyList<Mod> active, string archiveName)
    {
        var files = new List<string> { archiveName + ".toc", archiveName + ".dat", archiveName + ".toc.original" };
        foreach (var mod in active)
        {
            string prefix = @"Mods\" + mod.Id + @"\";
            files.AddRange(new[] { prefix + "mod.json", prefix + "YGO_2020-Ex.toc", prefix + "YGO_2020-Ex.dat" });
            if (Directory.Exists(mod.LooseFolder))
                files.AddRange(Directory.EnumerateFiles(mod.LooseFolder, "*", SearchOption.AllDirectories)
                    .Select(p => prefix + @"YGO_2020\" + Path.GetRelativePath(mod.LooseFolder, p)));
        }
        return Stamps(gameFolder, files);
    }

    private static JsonArray Stamps(string gameFolder, IEnumerable<string> files)
    {
        var array = new JsonArray();
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            var info = new FileInfo(Path.Combine(gameFolder, file));
            array.Add(new JsonObject { ["path"] = file, ["size"] = info.Exists ? info.Length : -1,
                ["time"] = info.Exists ? info.LastWriteTimeUtc.ToFileTimeUtc() : 0 });
        }
        return array;
    }

    private static bool CacheMatches(string gameFolder, JsonObject? manifest, string archiveName, bool patchFirst, JsonArray ids, JsonArray inputs)
    {
        try
        {
            if (manifest == null || manifest["version"]?.GetValue<int>() != Version || manifest["archive"]?.GetValue<string>() != archiveName
                || manifest["patchFirst"]?.GetValue<bool>() != patchFirst || !JsonNode.DeepEquals(manifest["mods"], ids)
                || !JsonNode.DeepEquals(manifest["inputs"], inputs)) return false;
            string patch = manifest["patch"]?.GetValue<string>() ?? "";
            _ = ReadIssues(manifest);
            var parts = patch.Split('\\');
            if (patch.Length > 0 && (parts.Length != 4 || parts[0] != "Mods" || parts[1] != ".merged" || parts[3] != "overlay"
                || parts[2].Length != 32 || !parts[2].All(c => char.IsAsciiHexDigit(c)))) return false;
            return patch.Length == 0 || (File.Exists(Path.Combine(gameFolder, patch + ".toc")) && File.Exists(Path.Combine(gameFolder, patch + ".dat"))
                && JsonNode.DeepEquals(manifest["outputs"], Stamps(gameFolder, [patch + ".toc", patch + ".dat"])));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { return false; }
    }
    private static IReadOnlyList<ModIssue> ReadIssues(JsonObject manifest) =>
        manifest["issues"]?.Deserialize<List<ModIssue>>() ?? [];
    private static void Report(List<ModIssue> issues, Mod mod, string path, IReadOnlyList<string> conflicts)
    {
        if (conflicts.Count == 0) return;
        issues.Add(new(IssueLevel.Warning, mod.Id, path + ": " + string.Join("; ", conflicts.Take(3))
            + (conflicts.Count > 3 ? $"; and {conflicts.Count - 3} more conflicts" : "") + "."));
    }
    private static bool IsFormatError(Exception ex) => ex is InvalidDataException or ArgumentException or OverflowException
        or IndexOutOfRangeException or JsonException or EndOfStreamException;
    private static string ArchivePath(string path)
    {
        string result = path.Replace('/', '\\');
        if (Path.IsPathRooted(result) || result.Split('\\').Any(p => p is ".." or "." or "") || result.Contains(':'))
            throw new InvalidDataException("Invalid archive path: " + path);
        return result;
    }

    private sealed record TextGroup(string[] Paths, bool CardText);
    private static IEnumerable<TextGroup> TextGroups(IEnumerable<string> paths)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            string name = Path.GetFileName(path).ToUpperInvariant();
            foreach (string prefix in new[] { "WORD", "DLG", "CARD" })
            {
                string[] kinds = prefix == "CARD" ? ["Indx", "Name", "Desc"] : ["Indx", "Text"];
                if (!kinds.Any(k => name.StartsWith(prefix + "_" + k.ToUpperInvariant() + "_")) || !name.EndsWith(".BIN")) continue;
                char language = name[^5];
                string group = prefix + "_" + language;
                if (seen.Add(group)) yield return new(kinds.Select(k => $@"bin\{prefix}_{k}_{language}.bin").ToArray(), prefix == "CARD");
            }
        }
    }
    private sealed record TextResult(byte[][] Files, IReadOnlyList<string> Conflicts);
    private static TextResult MergeText(bool cards, byte[][] b, byte[][] c, byte[][] n)
    {
        var conflicts = new List<string>();
        if (!cards)
        {
            var table = new TextTable();
            table.Entries.AddRange(BinaryModMerge.MergeIndexed(TextTable.Parse(b[0], b[1]).Entries,
                TextTable.Parse(c[0], c[1]).Entries, TextTable.Parse(n[0], n[1]).Entries, conflicts));
            var bytes = table.ToBytes();
            return new([bytes.Index, bytes.Text], conflicts);
        }
        List<(string Name, string Desc)> Read(byte[][] files)
        {
            if (files[0].Length % 8 != 0) throw new InvalidDataException("Card text index is not a sequence of offset pairs.");
            string Text(byte[] data, uint offset)
            {
                int at = checked((int)offset), end = at;
                if (at < 0 || at >= data.Length || at % 2 != 0) throw new InvalidDataException("Invalid card text offset.");
                while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0)) end += 2;
                if (end + 1 >= data.Length) throw new InvalidDataException("Unterminated card text.");
                return System.Text.Encoding.Unicode.GetString(data, at, end - at);
            }
            var result = new List<(string, string)>();
            for (int at = 0; at < files[0].Length; at += 8)
                result.Add((Text(files[1], BitConverter.ToUInt32(files[0], at)), Text(files[2], BitConverter.ToUInt32(files[0], at + 4))));
            return result;
        }
        var entries = BinaryModMerge.MergeIndexed(Read(b), Read(c), Read(n), conflicts);
        using var index = new MemoryStream(); using var names = new MemoryStream(); using var descriptions = new MemoryStream();
        using var writer = new BinaryWriter(index);
        foreach (var entry in entries)
        {
            writer.Write(checked((uint)names.Length)); writer.Write(checked((uint)descriptions.Length));
            names.Write(System.Text.Encoding.Unicode.GetBytes(entry.Name)); names.Write([0, 0]);
            descriptions.Write(System.Text.Encoding.Unicode.GetBytes(entry.Desc)); descriptions.Write([0, 0]);
        }
        return new([index.ToArray(), names.ToArray(), descriptions.ToArray()], conflicts);
    }

    // Large art archives are compared one entry at a time and copied in chunks, rather than allocating multiple 600 MB arrays.
    private sealed record ZibPiece(string Name, int Flags, long Size, Func<long, int, byte[]?> Read);
    private static TocArchive.PatchSource MergeZib(TocArchive baseline, string path, IReadOnlyList<Source> sources, List<ModIssue> issues)
    {
        Dictionary<string, ZibPiece> Index(long size, Func<long, int, byte[]?> read)
        {
            var entries = ZibArchive.ReadIndex(read);
            if (entries.Count == 0) throw new InvalidDataException("Empty or unreadable ZIB index.");
            var result = new Dictionary<string, ZibPiece>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
            {
                if (e.Start < entries.Count * 64L || e.Size < 0 || e.Start + e.Size > size || string.IsNullOrEmpty(e.Name))
                    throw new InvalidDataException("Invalid ZIB entry: " + e.Name);
                if (!result.TryAdd(e.Name, new(e.Name, e.Flags, e.Size, (start, count) => read(e.Start + start, count))))
                    throw new InvalidDataException("Duplicate ZIB entry: " + e.Name);
            }
            return result;
        }
        bool Equal(ZibPiece? a, ZibPiece? b)
        {
            if (a == null || b == null) return a == b;
            if (a.Size != b.Size || a.Flags != b.Flags) return false;
            for (long at = 0; at < a.Size; at += 1 << 20)
            {
                int count = (int)Math.Min(1 << 20, a.Size - at);
                if (!a.Read(at, count)!.AsSpan().SequenceEqual(b.Read(at, count))) return false;
            }
            return true;
        }
        var original = Index(baseline.SizeOf(path), (at, count) => baseline.Read(path, at, count));
        var current = new Dictionary<string, ZibPiece>(original, StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            var incoming = Index(source.Size(path), (at, count) => source.Read(path, at, count));
            foreach (string key in original.Keys.Concat(current.Keys).Concat(incoming.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
            {
                original.TryGetValue(key, out var b); current.TryGetValue(key, out var c); incoming.TryGetValue(key, out var n);
                if (Equal(b, n) || Equal(c, n)) continue;
                if (!Equal(b, c))
                {
                    if (b != null && c != null && n != null && (key.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                        || key.EndsWith(".ydc", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".bnd", StringComparison.OrdinalIgnoreCase))
                        && Math.Max(b.Size, Math.Max(c.Size, n.Size)) <= 32 * 1024 * 1024)
                    {
                        var merged = BinaryModMerge.Merge(key, b.Read(0, (int)b.Size)!, c.Read(0, (int)c.Size)!, n.Read(0, (int)n.Size)!);
                        Report(issues, source.Mod, path + " / " + key, merged.Conflicts);
                        byte[] data = merged.Data;
                        if (c.Flags != b.Flags && n.Flags != b.Flags && c.Flags != n.Flags)
                            Report(issues, source.Mod, path + " / " + key, ["entry flags: later change wins"]);
                        current[key] = new(key, n.Flags == b.Flags ? c.Flags : n.Flags, data.Length, (at, count) => data.AsSpan((int)at, count).ToArray());
                        continue;
                    }
                    Report(issues, source.Mod, path + " / " + key, ["same archive entry: later change wins"]);
                }
                if (n == null) current.Remove(key); else current[key] = n;
            }
        }
        if (current.Count == 0) throw new InvalidDataException("Merged ZIB has no files.");
        return TocArchive.PatchSource.Streamed(stream =>
        {
            long start = stream.Position, position = current.Count * 64L + 16;
            var entries = current.Values.ToList();
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (position + e.Size > uint.MaxValue) throw new InvalidDataException("Merged ZIB exceeds 4 GB.");
                byte[] header = new byte[64];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)position) | (uint)(i == 0 ? e.Flags | 1 : e.Flags));
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), checked((uint)e.Size));
                byte[] name = System.Text.Encoding.UTF8.GetBytes(e.Name);
                if (name.Length >= 56) throw new InvalidDataException("ZIB entry name is too long.");
                name.CopyTo(header, 8); stream.Write(header);
                position += (e.Size + 15) & ~15L;
            }
            stream.Write(new byte[16]);
            foreach (var e in entries)
            {
                for (long at = 0; at < e.Size; at += 1 << 20)
                {
                    int count = (int)Math.Min(1 << 20, e.Size - at);
                    byte[] data = e.Read(at, count) ?? throw new InvalidDataException("Unreadable ZIB entry.");
                    if (data.Length != count) throw new InvalidDataException("Truncated ZIB entry.");
                    stream.Write(data);
                }
                stream.Write(new byte[(int)(((e.Size + 15) & ~15L) - e.Size)]);
            }
            return stream.Position - start;
        });
    }
}
