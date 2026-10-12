using System.Text.Json.Nodes;
using DeckData;
using PackDef;
using StartingCollection;
using Types;
using Wolf.Mods;

static class BinaryMergeTests
{
    static int checks;
    static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    static byte[] Bytes(params long[] values) => values.SelectMany(BitConverter.GetBytes).ToArray();
    static BinaryModMerge.Result Merge(string path, byte[] b, byte[] c, byte[] n) => BinaryModMerge.Merge(path, b, c, n);
    public static void Run(string? fixture)
    {
        var b = Bytes(0, 0, 0, 0); var c = Bytes(1, 0, 0, 0); var n = Bytes(0, 2, 0, 0);
        var merged = Merge("bin/custom.bin", b, c, n);
        Check(merged.Data.SequenceEqual(Bytes(1, 2, 0, 0)) && merged.Conflicts.Count == 0, "Independent opaque records did not merge");
        merged = Merge("bin/custom.bin", b, Bytes(1, 5, 0, 0), Bytes(256, 0, 7, 0));
        Check(merged.Data.SequenceEqual(Bytes(256, 5, 7, 0)) && merged.Conflicts.Count == 1, "Conflicting scalar bytes became a hybrid");
        Check(Merge("bin/custom.bin", b, c, b).Data.SequenceEqual(c), "An unchanged later file reverted an earlier mod");
        Check(Merge("bin/custom.bin", b, c, c).Conflicts.Count == 0, "Identical edits conflicted");
        Check(Merge("bin/custom.bin", b, c, [8]).Conflicts.Count == 1, "Resized opaque replacement lacked a conflict");
        Check(Merge("asset.png", b, c, n).Data.SequenceEqual(n), "Media was spliced as table data");

        CharacterTable Chars(params (int Id, string Key)[] records)
        {
            var table = new CharacterTable();
            foreach (var r in records) table.Characters.Add(new Character { Id = r.Id, Key = r.Key, Field1C = 17 });
            return table;
        }
        byte[] cb = Chars((0, "base")).ToBytes('E');
        var cr = Merge("main/chardata_E.bin", cb, Chars((0, "changed"), (191, "one")).ToBytes('E'),
            Chars((0, "base"), (192, "second character with a longer key")).ToBytes('E'));
        var ct = CharacterTable.Parse(new Dictionary<char, byte[]> { ['E'] = cr.Data });
        Check(ct.Characters.Count == 3 && ct.Find(0)!.Key == "changed" && ct.Find(192)!.Field1C == 17, "Character offsets or unknown fields lost");
        Check(cr.Conflicts.Count == 0, "Distinct characters conflicted");
        cr = Merge("main/chardata_E.bin", cb, Chars((0, "first")).ToBytes('E'), Chars((0, "last")).ToBytes('E'));
        Check(cr.Conflicts.Count == 1 && CharacterTable.Parse(new Dictionary<char, byte[]> { ['E'] = cr.Data }).Find(0)!.Key == "last", "Same-id character conflict failed");

        DeckDataFile Decks(params (uint Id, string Title)[] records)
        {
            var file = new DeckDataFile();
            foreach (var r in records) file.Records.Add(new DeckRecord { Id = r.Id, Slot = r.Id, Title = r.Title, FileName = "deck" + r.Id });
            return file;
        }
        byte[] db = Decks((0, "base")).ToBytes();
        merged = Merge("main/deckdata_E.bin", db, Decks((0, "a different title"), (10, "one")).ToBytes(), Decks((0, "base"), (11, "two")).ToBytes());
        var dt = DeckDataFile.Parse(merged.Data);
        Check(dt.Records.Count == 3 && dt.Records[0].Title == "a different title" && merged.Conflicts.Count == 0, "Deck additions did not preserve string offsets");
        merged = Merge("main/deckdata_E.bin", db, Decks().ToBytes(), Decks((0, "edited after removal")).ToBytes());
        Check(DeckDataFile.Parse(merged.Data).Records.Count == 1 && merged.Conflicts.Count == 1, "Delete-versus-edit was not reported");

        PackDefFile Packs(params (uint Id, string Title)[] records)
        {
            var file = new PackDefFile();
            foreach (var r in records) file.Records.Add(new PackDefRecord { Id = r.Id, Title = r.Title, Name = "pack" + r.Id, Kind = (uint)PackKind.Reward });
            return file;
        }
        var pb = Packs((0, "base")).ToBytes();
        merged = Merge("main/packdefdata_E.bin", pb, Packs((0, "base"), (10, "one")).ToBytes(), Packs((0, "base"), (11, "two")).ToBytes());
        Check(PackDefFile.Parse(merged.Data).Records.Count == 3 && merged.Conflicts.Count == 0, "Pack records did not merge");

        StoryDuelTable Duels(params (int Id, string Key)[] records)
        {
            var table = new StoryDuelTable();
            foreach (var r in records) table.Duels.Add(new StoryDuel { Id = r.Id, Key = r.Key });
            return table;
        }
        var duelBase = Duels((1, "base")).ToBytes('E');
        merged = Merge("main/dueldata_E.bin", duelBase, Duels((1, "base"), (2, "added scene")).ToBytes('E'), Duels((1, "base"), (3, "other scene")).ToBytes('E'));
        Check(StoryDuelTable.Parse(new Dictionary<char, byte[]> { ['E'] = merged.Data }).Duels.Count == 3 && merged.Conflicts.Count == 0, "Story duels did not merge");
        StoryScriptTable Scripts(params (string Name, string Text)[] records)
        {
            var table = new StoryScriptTable();
            foreach (var r in records)
            {
                var script = new StoryScript { Name = r.Name };
                var line = new ScriptLine { Speaker = "infn8" }; line.Texts['E'] = r.Text;
                script.Lines.Add(line); table.Scripts.Add(script);
            }
            return table;
        }
        var scriptBase = Scripts(("BASE_INTRO", "base")).ToBytes('E');
        merged = Merge("main/scriptdata_E.bin", scriptBase, Scripts(("BASE_INTRO", "longer updated text"), ("NEW_INTRO", "one")).ToBytes('E'),
            Scripts(("BASE_INTRO", "base"), ("OTHER_INTRO", "two")).ToBytes('E'));
        var scripts = StoryScriptTable.Parse(new Dictionary<char, byte[]> { ['E'] = merged.Data });
        Check(scripts.Scripts.Count == 3 && scripts.Scripts[0].Lines[0].Text('E') == "longer updated text", "Script offsets or scene additions lost");
        byte[] Bnd(params string[] texts) => BND.ToBytes(texts.Select(t => new BNDString(0, 0, t)).ToList());
        merged = Merge("strings/Strings_STEAM_E.BND", Bnd("zero", "one"), Bnd("long replacement", "one"), Bnd("zero", "second replacement"));
        Check(BND.Parse(merged.Data).Select(t => t.String).SequenceEqual(new[] { "long replacement", "second replacement" }) && merged.Conflicts.Count == 0, "BND offsets did not merge");
        merged = Merge("main/ui/credits/credits.dat", CRED.ToBytes(["zero", "one"]), CRED.ToBytes(["first", "one"]), CRED.ToBytes(["zero", "second"]));
        Check(CRED.Parse(merged.Data).SequenceEqual(new[] { "first", "second" }), "Credit lines did not merge");
        var packBase = new PackContents(); packBase.Common.Add(100);
        var packFirst = new PackContents(); packFirst.Common.AddRange([100, 101, 101]);
        var packSecond = new PackContents(); packSecond.Common.Add(100); packSecond.Rare.Add(102);
        merged = Merge("packdata_test.bin", packBase.ToBytes(), packFirst.ToBytes(), packSecond.ToBytes());
        var pack = PackContents.Parse(merged.Data);
        Check(pack.Common.Count(id => id == 101) == 2 && pack.Rare.Contains(102) && merged.Conflicts.Count == 0, "Pack weights or independent additions lost");
        var battleBase = new BattlePackContents(); battleBase.Slots.AddRange([[100], [101]]);
        var battleFirst = new BattlePackContents(); battleFirst.Slots.AddRange([[100, 102, 102], [101]]);
        var battleSecond = new BattlePackContents(); battleSecond.Slots.AddRange([[100], [101, 103]]);
        merged = Merge("bpack_test.bin", battleBase.ToBytes(), battleFirst.ToBytes(), battleSecond.ToBytes());
        var battle = BattlePackContents.Parse(merged.Data);
        Check(battle.Slots[0].Count(id => id == 102) == 2 && battle.Slots[1].Contains(103) && merged.Conflicts.Count == 0, "Battle-pack slots or weights lost");
        var ydcBase = new YdcDeck(); ydcBase.Main.Add(100);
        var ydcFirst = new YdcDeck(); ydcFirst.Main.AddRange([100, 101, 101]);
        var ydcSecond = new YdcDeck(); ydcSecond.Main.Add(100); ydcSecond.Extra.Add(102);
        merged = Merge("deck.ydc", ydcBase.ToBytes(), ydcFirst.ToBytes(), ydcSecond.ToBytes());
        var ydc = YdcDeck.Parse(merged.Data);
        Check(ydc.Main.Count == 3 && ydc.Extra.Contains(102) && merged.Conflicts.Count == 0, "YDC card counts did not merge");
        var indexedConflicts = new List<string>();
        var indexed = BinaryModMerge.MergeIndexed(new[] { "a", "b" }, new[] { "a" }, new[] { "a", "edited" }, indexedConflicts);
        Check(indexed.Count == 2 && indexed[1] == "edited" && indexedConflicts.Count == 1, "Indexed delete-versus-edit not detected");

        byte[] Limits(ushort[] forbidden, ushort[] limited, ushort[] semi)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            foreach (var list in new[] { forbidden, limited, semi }) { writer.Write((ushort)list.Length); foreach (var id in list) writer.Write(id); }
            return stream.ToArray();
        }
        byte[] lb = Limits([100], [], []);
        merged = Merge("bin/pd_limits.bin", lb, Limits([100, 101], [], []), Limits([100], [102], []));
        Check(merged.Data.SequenceEqual(Limits([100, 101], [102], [])) && merged.Conflicts.Count == 0, "Ban-list categories did not merge");
        merged = Merge("bin/pd_limits.bin", lb, Limits([], [100], []), Limits([], [], [100]));
        Check(merged.Data.SequenceEqual(Limits([], [], [100])) && merged.Conflicts.Count == 1, "Same ban-list card conflict failed");
        var tb = new TagDataTable(); var tc = new TagDataTable(); var tn = new TagDataTable();
        tc.Lists[0].Add(new RelatedCard(100, 1)); tn.Lists[1].Add(new RelatedCard(101, 2));
        merged = Merge("bin/tagdata.bin", tb.ToBytes(), tc.ToBytes(), tn.ToBytes());
        var tags = TagDataTable.Parse(merged.Data);
        Check(tags.Lists[0].Count == 1 && tags.Lists[1].Count == 1 && merged.Conflicts.Count == 0, "Related-card pools did not merge");

        ZibArchive Zib(params (string Name, byte[] Data)[] entries)
        {
            var archive = new ZibArchive(); foreach (var e in entries) archive.Set(e.Name, e.Data); return archive;
        }
        var zb = Zib(("base.jpg", [0]), ("remove.jpg", [0])).ToBytes();
        merged = Merge("art.zib", zb, Zib(("base.jpg", [1]), ("one.jpg", [1, 2])).ToBytes(),
            Zib(("base.jpg", [0]), ("remove.jpg", [0]), ("two.jpg", [3, 4, 5])).ToBytes());
        var z = ZibArchive.Parse(merged.Data);
        Check(z.Entries.Count == 3 && z.Find("remove.jpg") == null && z.Find("one.jpg") != null && z.Find("two.jpg") != null
            && z.Get("base.jpg")!.SequenceEqual(new byte[] { 1 }), "ZIB additions, deletion, or edits lost");

        // Exercise the actual archive preparation pipeline, source precedence, and cache invalidation.
        string root = fixture ?? Path.Combine(Directory.GetCurrentDirectory(), "binary-merge-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var word = new TextTable(); word.Entries.AddRange(["base zero", "base one"]);
            var wt = word.ToBytes();
            byte[] largeArt = Enumerable.Range(0, (1 << 20) + 17).Select(i => (byte)(i % 251)).ToArray();
            var baseFiles = new Dictionary<string, byte[]>
            {
                [@"bin\custom.bin"] = b, [@"art.zib"] = Zib(("base.jpg", [0]), ("remove.jpg", [0]), ("large.jpg", largeArt)).ToBytes(),
                [@"bin\WORD_Indx_E.bin"] = wt.Index, [@"bin\WORD_Text_E.bin"] = wt.Text,
                [@"bin\CARD_Indx_E.bin"] = CardText("zero", "one")[0],
                [@"bin\CARD_Name_E.bin"] = CardText("zero", "one")[1],
                [@"bin\CARD_Desc_E.bin"] = CardText("zero", "one")[2],
            };
            TocArchive.WritePatch(Path.Combine(root, "YGO_2020.toc"), baseFiles);
            var mods = new List<Mod>();
            for (int i = 0; i < 2; i++)
            {
                string id = "mod" + i, folder = Path.Combine(root, "Mods", id);
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "mod.json"), "{\"name\":\"" + id + "\"}");
                var words = new TextTable(); words.Entries.AddRange(i == 0 ? ["long replacement for zero", "base one"] : ["base zero", "second replacement"]);
                var bytes = words.ToBytes();
                var cards = i == 0 ? CardText("a longer card name", "one") : CardText("zero", "another card name");
                TocArchive.WritePatch(Path.Combine(folder, "YGO_2020-Ex.toc"), new Dictionary<string, byte[]>
                {
                    [@"bin\custom.bin"] = i == 0 ? c : n,
                    [@"art.zib"] = i == 0 ? Zib(("base.jpg", [1]), ("one.jpg", [1, 2]), ("large.jpg", largeArt)).ToBytes()
                        : Zib(("base.jpg", [0]), ("remove.jpg", [0]), ("two.jpg", [3, 4, 5]), ("large.jpg", largeArt)).ToBytes(),
                    [@"bin\WORD_Indx_E.bin"] = bytes.Index, [@"bin\WORD_Text_E.bin"] = bytes.Text,
                    [@"bin\CARD_Indx_E.bin"] = cards[0], [@"bin\CARD_Name_E.bin"] = cards[1], [@"bin\CARD_Desc_E.bin"] = cards[2],
                });
                mods.Add(new Mod { Id = id, Folder = folder, Info = new ModInfo { Name = id } });
            }
            string Manifest() => File.ReadAllText(Path.Combine(root, "Mods", "merged.json"));
            TocArchive Archive() => TocArchive.TryOpen(Path.Combine(root, JsonNode.Parse(Manifest())!["patch"]!.GetValue<string>() + ".toc"))!;
            var problems = ModMergeArchive.Prepare(root, mods);
            Check(problems.Count == 0, "Independent pipeline changes produced conflicts: " + string.Join(";", problems.Select(p => p.Text)));
            Check(Archive().Read(@"bin\custom.bin")!.SequenceEqual(Bytes(1, 2, 0, 0)), "Generated binary overlay did not combine mods");
            z = ZibArchive.Parse(Archive().Read("art.zib")!);
            Check(z.Find("one.jpg") != null && z.Find("two.jpg") != null && z.Find("remove.jpg") == null, "Streamed ZIB did not combine entries");
            Check(z.Get("large.jpg")!.SequenceEqual(largeArt), "Multi-chunk streamed ZIB payload corrupted");
            var wordsOut = TextTable.Parse(Archive().Read(@"bin\WORD_Indx_E.bin")!, Archive().Read(@"bin\WORD_Text_E.bin")!);
            Check(wordsOut.Entries.SequenceEqual(new[] { "long replacement for zero", "second replacement" }), "Paired text offsets were not rebuilt");
            Check(System.Text.Encoding.Unicode.GetString(Archive().Read(@"bin\CARD_Name_E.bin")!).Contains("another card name")
                && System.Text.Encoding.Unicode.GetString(Archive().Read(@"bin\CARD_Name_E.bin")!).Contains("a longer card name"), "Card text triplet did not merge");
            string first = Manifest();
            ModMergeArchive.Prepare(root, mods);
            Check(Manifest() == first, "Unchanged inputs unnecessarily rebuilt the cache");
            File.AppendAllText(Path.Combine(root, JsonNode.Parse(Manifest())!["patch"]!.GetValue<string>() + ".dat"), "broken cache");
            ModMergeArchive.Prepare(root, mods);
            Check(Manifest() != first && Archive().Read(@"bin\custom.bin")!.SequenceEqual(Bytes(1, 2, 0, 0)), "Damaged output was reused");
            string looseFile = Path.Combine(mods[1].LooseFolder, "bin", "custom.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(looseFile)!);
            File.WriteAllBytes(looseFile, Bytes(0, 3, 0, 0));
            ModMergeArchive.Prepare(root, mods);
            Check(Manifest() != first && Archive().Read(@"bin\custom.bin")!.SequenceEqual(Bytes(1, 3, 0, 0)), "New loose file did not invalidate cache");
            ModMergeArchive.Prepare(root, mods, patchFirst: true);
            Check(Archive().Read(@"bin\custom.bin")!.SequenceEqual(Bytes(1, 2, 0, 0)), "FileOrder=patch was ignored");
            File.WriteAllBytes(looseFile, Bytes(4, 0, 0, 0));
            problems = ModMergeArchive.Prepare(root, mods);
            Check(problems.Any(p => p.Text.Contains("later change wins")) && Archive().Read(@"bin\custom.bin")!.SequenceEqual(Bytes(4, 0, 0, 0)), "Conflict warning or later precedence failed");
            mods.Reverse(); ModMergeArchive.Prepare(root, mods);
            Check(Archive().Read(@"bin\custom.bin")!.SequenceEqual(Bytes(1, 0, 0, 0)), "Reordering did not update conflict precedence");
            mods[0].Enabled = false; ModMergeArchive.Prepare(root, mods);
            Check(JsonNode.Parse(Manifest())!["patch"]!.GetValue<string>() == "", "Disabled mod left stale merged output");
            mods[0].Enabled = true; mods.Reverse(); File.Delete(looseFile);
            ModLibrary.SaveList(root, mods); // also creates the native validation fixture
            Check(Archive().Read(@"bin\custom.bin")!.SequenceEqual(Bytes(1, 2, 0, 0)), "SaveList did not prepare merges");
            File.WriteAllText(Path.Combine(root, "YGO_2020.toc.original"), "edited baseline marker");
            problems = ModMergeArchive.Prepare(root, mods);
            Check(problems.Count > 0 && JsonNode.Parse(Manifest())!["patch"]!.GetValue<string>() == "", "Modified baseline used as common ancestor");
            File.Delete(Path.Combine(root, "YGO_2020.toc.original"));
            ModMergeArchive.Prepare(root, mods);
        }
        finally { if (fixture == null) Directory.Delete(root, true); }
        Console.WriteLine($"Binary merge checks passed ({checks}).");
    }

    static byte[][] CardText(params string[] names)
    {
        using var index = new MemoryStream(); using var pool = new MemoryStream(); using var desc = new MemoryStream();
        using var writer = new BinaryWriter(index);
        foreach (string name in names)
        {
            writer.Write((uint)pool.Length); writer.Write((uint)desc.Length);
            pool.Write(System.Text.Encoding.Unicode.GetBytes(name)); pool.Write([0, 0]);
            desc.Write(System.Text.Encoding.Unicode.GetBytes("description")); desc.Write([0, 0]);
        }
        return [index.ToArray(), pool.ToArray(), desc.ToArray()];
    }
}
