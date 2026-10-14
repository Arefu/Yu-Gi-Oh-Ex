using StartingCollection;
using Types;
using Wolf.Mods;

void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
var before = new CharacterTable();
before.Characters.Add(new Character { Id = 0, Key = "yugi" });
var after = CharacterTable.Parse(new Dictionary<char, byte[]> { ['E'] = before.ToBytes('E') });
after.Characters.Add(new Character { Id = 191, Key = "new" });
Check(ModDelta.Additions(before.ToBytes('E'), after.ToBytes('E'), 'E')?.Count == 1, "Addition missing");
after.Characters[0].Deck = 12;
Check(ModDelta.Additions(before.ToBytes('E'), after.ToBytes('E'), 'E') == null, "Edit treated as addition");
after.Characters.RemoveAt(0);
Check(ModDelta.Additions(before.ToBytes('E'), after.ToBytes('E'), 'E') == null, "Removal treated as addition");
string root = Path.Combine(Directory.GetCurrentDirectory(), "mod-delta-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    TocArchive.WritePatch(Path.Combine(root, "YGO_2020.toc"), new Dictionary<string, byte[]>
    { ["main\\chardata_E.bin"] = before.ToBytes('E'), ["bin\\same.bin"] = [1, 2, 3] });
    var combined = CharacterTable.Parse(new Dictionary<char, byte[]> { ['E'] = before.ToBytes('E') });
    foreach (int id in new[] { 191, 192 })
    {
        string folder = Path.Combine(root, "mod" + id);
        Directory.CreateDirectory(folder);
        var table = CharacterTable.Parse(new Dictionary<char, byte[]> { ['E'] = before.ToBytes('E') });
        table.Characters.Add(new Character { Id = id, Key = "new" + id });
        TocArchive.WritePatch(Path.Combine(folder, "YGO_2020-Ex.toc"), new Dictionary<string, byte[]>
        { ["main\\chardata_E.bin"] = table.ToBytes('E'), ["bin\\same.bin"] = [1, 2, 3], ["bin\\custom.bin"] = [9] });
        ModDelta.Normalize(root, folder);
        var patch = TocArchive.TryOpen(Path.Combine(folder, "YGO_2020-Ex.toc"))!;
        Check(patch.Count == 1 && patch.Read("bin\\custom.bin")!.SequenceEqual(new byte[] { 9 }), "Archive reduction lost content");
        CharacterJson.Apply(CharacterJson.Load(Path.Combine(folder, "Yu-Gi-Oh-Ex", "characters.json")), combined);
        ModDelta.Normalize(root, folder);
    }
    Check(combined.Find(191) != null && combined.Find(192) != null, "Independent mods did not combine");
}
finally { Directory.Delete(root, true); }
Console.WriteLine("Mod delta regression checks passed.");
BinaryMergeTests.Run(args.Length == 2 && args[0] == "--fixture" ? Path.GetFullPath(args[1]) : null);
