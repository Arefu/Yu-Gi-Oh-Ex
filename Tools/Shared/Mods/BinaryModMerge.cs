using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Types;
using DeckData;
using PackDef;

namespace Wolf.Mods;

/// <summary>Three-way merges: incoming is compared with the common original, never with the previous mod.</summary>
public static class BinaryModMerge
{
    private static readonly JsonSerializerOptions Records = new()
    {
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        IncludeFields = true,
    };

    public sealed record Result(byte[] Data, IReadOnlyList<string> Conflicts);

    public static Result Merge(string path, byte[] original, byte[] current, byte[] incoming)
    {
        if (Equal(incoming, original) || Equal(incoming, current)) return new(current, []);
        if (Equal(current, original)) return new(incoming, []);
        var conflicts = new List<string>();
        string name = Path.GetFileName(path.Replace('\\', '/')).ToLowerInvariant();
        try
        {
            if (name.StartsWith("chardata_") && name.EndsWith(".bin"))
            {
                char language = char.ToUpperInvariant(name[9]);
                CharacterTable Read(byte[] data) => CharacterTable.Parse(new Dictionary<char, byte[]> { [language] = data });
                var b = Read(original); var c = Read(current); var n = Read(incoming);
                var output = new CharacterTable { HeaderPad = Pick(b.HeaderPad, c.HeaderPad, n.HeaderPad, "header", conflicts) };
                output.Characters.AddRange(MergeRecords(b.Characters, c.Characters, n.Characters, r => r.Id.ToString(), conflicts));
                if (output.Characters.Any(r => r.Id < 0 || r.Id >= CharacterTable.SlotCount)) throw new InvalidDataException("Character id out of range.");
                return new(output.ToBytes(language), conflicts);
            }
            if (name.StartsWith("deckdata_") && name.EndsWith(".bin"))
            {
                RequireZeroPad(original, current, incoming);
                var b = DeckDataFile.Parse(original); var c = DeckDataFile.Parse(current); var n = DeckDataFile.Parse(incoming);
                var output = new DeckDataFile();
                output.Records.AddRange(MergeRecords(b.Records, c.Records, n.Records, r => r.Id.ToString(), conflicts));
                if (output.Records.Any(r => r.Id >= 700 || r.Slot >= 700)) throw new InvalidDataException("Deck id out of range.");
                return new(output.ToBytes(), conflicts);
            }
            if (name.StartsWith("packdefdata_") && name.EndsWith(".bin"))
            {
                RequireZeroPad(original, current, incoming);
                var b = PackDefFile.Parse(original); var c = PackDefFile.Parse(current); var n = PackDefFile.Parse(incoming);
                var output = new PackDefFile();
                output.Records.AddRange(MergeRecords(b.Records, c.Records, n.Records, r => r.Id.ToString(), conflicts));
                if (output.Records.Any(r => r.Id >= 128)) throw new InvalidDataException("Pack id out of range.");
                return new(output.ToBytes(), conflicts);
            }
            if (name.StartsWith("dueldata_") && name.EndsWith(".bin"))
            {
                char language = char.ToUpperInvariant(name[9]);
                StoryDuelTable Read(byte[] data) => StoryDuelTable.Parse(new Dictionary<char, byte[]> { [language] = data });
                var b = Read(original); var c = Read(current); var n = Read(incoming);
                var output = new StoryDuelTable { HeaderPad = Pick(b.HeaderPad, c.HeaderPad, n.HeaderPad, "header", conflicts) };
                output.Duels.AddRange(MergeRecords(b.Duels, c.Duels, n.Duels, r => r.Id.ToString(), conflicts));
                if (output.Duels.Any(r => r.Id < 0 || r.Id >= StoryDuelTable.SlotCount)) throw new InvalidDataException("Story duel id out of range.");
                return new(output.ToBytes(language), conflicts);
            }
            if (name.StartsWith("scriptdata_") && name.EndsWith(".bin"))
            {
                char language = char.ToUpperInvariant(name[11]);
                StoryScriptTable Read(byte[] data) => StoryScriptTable.Parse(new Dictionary<char, byte[]> { [language] = data });
                var output = new StoryScriptTable();
                output.Scripts.AddRange(MergeRecords(Read(original).Scripts, Read(current).Scripts, Read(incoming).Scripts,
                    r => r.Name.ToUpperInvariant(), conflicts));
                return new(output.ToBytes(language), conflicts);
            }
            if (name.EndsWith(".bnd"))
            {
                List<string> Read(byte[] data)
                {
                    var entries = BND.Parse(data);
                    if (data.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(data) != entries.Count)
                        throw new InvalidDataException("Invalid BND string count.");
                    return entries.Select(e => e.String).ToList();
                }
                var texts = MergeIndexed(Read(original), Read(current), Read(incoming), conflicts);
                return new(BND.ToBytes(texts.Select(t => new BNDString(0, 0, t)).ToList()), conflicts);
            }
            if (name == "credits.dat")
                return new(CRED.ToBytes(MergeIndexed(CRED.Parse(original), CRED.Parse(current), CRED.Parse(incoming), conflicts)), conflicts);
            if (name.StartsWith("packdata_") && name.EndsWith(".bin"))
            {
                var b = PackContents.Parse(original); var c = PackContents.Parse(current); var n = PackContents.Parse(incoming);
                var output = new PackContents();
                List<PackCard> Cards(PackContents pack)
                {
                    var common = pack.Common.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
                    var rare = pack.Rare.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
                    return common.Keys.Concat(rare.Keys).Distinct().Order()
                        .Select(id => new PackCard(id, common.GetValueOrDefault(id), rare.GetValueOrDefault(id))).ToList();
                }
                foreach (var card in MergeRecords(Cards(b), Cards(c), Cards(n), r => r.Id.ToString(), conflicts))
                {
                    output.Common.AddRange(Enumerable.Repeat(card.Id, card.Common));
                    output.Rare.AddRange(Enumerable.Repeat(card.Id, card.Rare));
                }
                if (output.Common.Count > ushort.MaxValue || output.Rare.Count > ushort.MaxValue) throw new InvalidDataException("Pack card count overflow.");
                return new(output.ToBytes(), conflicts);
            }
            if (name.EndsWith(".ydc"))
            {
                var b = YdcDeck.Parse(original); var c = YdcDeck.Parse(current); var n = YdcDeck.Parse(incoming);
                var output = new YdcDeck { Header = Pick(b.Header, c.Header, n.Header, "deck header", conflicts) };
                output.Main.AddRange(MergeCounts(b.Main, c.Main, n.Main, "main", conflicts));
                output.Extra.AddRange(MergeCounts(b.Extra, c.Extra, n.Extra, "extra", conflicts));
                output.Side.AddRange(MergeCounts(b.Side, c.Side, n.Side, "side", conflicts));
                if (output.Main.Count > 60 || output.Extra.Count > 15 || output.Side.Count > 15)
                    throw new InvalidDataException("Combined deck exceeds its section capacity.");
                return new(output.ToBytes(), conflicts);
            }
            if (name.StartsWith("bpack_") && name.EndsWith(".bin"))
            {
                var b = BattlePackContents.Parse(original); var c = BattlePackContents.Parse(current); var n = BattlePackContents.Parse(incoming);
                var output = new BattlePackContents();
                // Slots are keyed by their position. Their duplicate cards encode weights, so don't turn them into sets.
                output.Slots.AddRange(MergeIndexed(b.Slots, c.Slots, n.Slots, conflicts));
                return new(output.ToBytes(), conflicts);
            }
            if (name == "pd_limits.bin") return new(MergeLimits(original, current, incoming, conflicts), conflicts);
            if (name == "tagdata.bin")
            {
                var b = TagDataTable.Parse(original); var c = TagDataTable.Parse(current); var n = TagDataTable.Parse(incoming);
                var output = new TagDataTable();
                for (int i = 0; i < TagDataTable.EntryCount; i++)
                    output.Lists[i].AddRange(Pick(b.Lists[i], c.Lists[i], n.Lists[i], $"related-card list {i}", conflicts));
                return new(output.ToBytes(), conflicts);
            }
            if (name.StartsWith("taginfo_") && name.EndsWith(".bin"))
            {
                char language = char.ToUpperInvariant(name[8]);
                TagInfoTable Read(byte[] data) => TagInfoTable.Parse(new Dictionary<char, byte[]> { [language] = data });
                var output = new TagInfoTable();
                output.Tags.AddRange(MergeIndexed(Read(original).Tags, Read(current).Tags, Read(incoming).Tags, conflicts));
                return new(output.ToBytes(language), conflicts);
            }
            if (name.EndsWith(".zib")) return MergeZib(original, current, incoming);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or OverflowException or IndexOutOfRangeException or JsonException or EndOfStreamException)
        {
            return new(incoming, [$"Cannot reconcile the file's structure ({ex.Message}); later whole file wins."]);
        }

        // Opaque data has no relocatable record model. Keep offsets stable and treat each aligned 8-byte unit atomically.
        // Explicit fixed tables use their full record size instead (so two values of one record never become a hybrid).
        int stride = name switch
        {
            "card_prop.bin" or "card_genre.bin" => 8,
            "card_intid.bin" => 2,
            "card_pass.bin" => 4,
            _ => 8,
        };
        if (!name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            return new(incoming, ["Same media or opaque asset replaced: later whole file wins."]);
        if (original.Length != current.Length || original.Length != incoming.Length)
            return new(incoming, ["Overlapping replacements change the layout; later whole file wins."]);
        byte[] merged = current.ToArray();
        for (int at = 0; at < original.Length; at += stride)
        {
            int count = Math.Min(stride, original.Length - at);
            var b = original.AsSpan(at, count); var c = current.AsSpan(at, count); var n = incoming.AsSpan(at, count);
            if (n.SequenceEqual(b) || n.SequenceEqual(c)) continue;
            if (!c.SequenceEqual(b)) conflicts.Add($"record at byte 0x{at:X}: later change wins");
            n.CopyTo(merged.AsSpan(at));
        }
        return new(merged, conflicts);
    }

    private static Result MergeZib(byte[] original, byte[] current, byte[] incoming)
    {
        var b = ZibArchive.Parse(original); var c = ZibArchive.Parse(current); var n = ZibArchive.Parse(incoming);
        var conflicts = new List<string>();
        var bm = Unique(b.Entries, e => e.Name, StringComparer.OrdinalIgnoreCase);
        var cm = Unique(c.Entries, e => e.Name, StringComparer.OrdinalIgnoreCase);
        var nm = Unique(n.Entries, e => e.Name, StringComparer.OrdinalIgnoreCase);
        var output = new ZibArchive();
        foreach (string key in Keys(bm, cm, nm))
        {
            bm.TryGetValue(key, out var be); cm.TryGetValue(key, out var ce); nm.TryGetValue(key, out var ne);
            if (be != null && ce != null && ne != null)
            {
                // Images/audio/compressed payloads are indivisible entries, not fixed-offset tables.
                bool structured = key.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".zib", StringComparison.OrdinalIgnoreCase)
                    || key.EndsWith(".ydc", StringComparison.OrdinalIgnoreCase) || key.EndsWith(".bnd", StringComparison.OrdinalIgnoreCase);
                var data = structured ? Merge(key, be.Data, ce.Data, ne.Data)
                    : new Result(Pick(be.Data, ce.Data, ne.Data, key, conflicts), []);
                conflicts.AddRange(data.Conflicts.Select(t => key + ": " + t));
                output.Entries.Add(new ZibArchive.Entry { Name = ce.Name, Data = data.Data,
                    Flags = Pick(be.Flags, ce.Flags, ne.Flags, key + " flags", conflicts) });
            }
            else
            {
                var entry = Pick(be, ce, ne, key, conflicts);
                if (entry != null) output.Entries.Add(entry);
            }
        }
        if (output.Entries.Count == 0) throw new InvalidDataException("The merged ZIB has no entries.");
        return new(output.ToBytes(), conflicts);
    }

    // Stable keys preserve removals as well as additions; a same-id edit is a real conflict, independent of byte offsets.
    private static List<T> MergeRecords<T>(IEnumerable<T> b, IEnumerable<T> c, IEnumerable<T> n, Func<T, string> key, List<string> conflicts)
        where T : class
    {
        var bm = Unique(b, key); var cm = Unique(c, key); var nm = Unique(n, key);
        var output = new List<T>();
        foreach (string id in Keys(bm, cm, nm))
        {
            bm.TryGetValue(id, out var before); cm.TryGetValue(id, out var current); nm.TryGetValue(id, out var incoming);
            var picked = Pick(before, current, incoming, "id " + id, conflicts);
            if (picked != null) output.Add(picked);
        }
        return output;
    }

    public static List<T> MergeIndexed<T>(IReadOnlyList<T> b, IReadOnlyList<T> c, IReadOnlyList<T> n, List<string> conflicts)
    {
        // Each absent tail slot is a deletion, so delete-vs-edit is reported just like an ordinary same-id conflict.
        int size = Math.Max(b.Count, Math.Max(c.Count, n.Count));
        var result = new List<T>();
        bool gap = false;
        for (int i = 0; i < size; i++)
        {
            var picked = Pick(i < b.Count ? new Slot<T>(b[i]) : null, i < c.Count ? new Slot<T>(c[i]) : null,
                i < n.Count ? new Slot<T>(n[i]) : null, "entry " + i, conflicts);
            if (picked == null) { gap = true; continue; }
            if (gap) throw new InvalidDataException("Combining tail removal with another addition would renumber indexed slots.");
            result.Add(picked.Value);
        }
        return result;
    }

    private sealed record Slot<T>(T Value);
    private sealed record PackCard(ushort Id, int Common, int Rare);

    private static byte[] MergeLimits(byte[] b, byte[] c, byte[] n, List<string> conflicts)
    {
        Dictionary<ushort, int> Read(byte[] bytes)
        {
            using var reader = new BinaryReader(new MemoryStream(bytes));
            var map = new Dictionary<ushort, int>();
            for (int category = 0; category < 3; category++)
            {
                int count = reader.ReadUInt16();
                for (int i = 0; i < count; i++)
                    if (!map.TryAdd(reader.ReadUInt16(), category + 1)) throw new InvalidDataException("Duplicate ban-list card.");
            }
            if (reader.BaseStream.Position != bytes.Length) throw new InvalidDataException("Unexpected ban-list data.");
            return map;
        }
        var bm = Read(b); var cm = Read(c); var nm = Read(n);
        var output = new Dictionary<ushort, int>();
        foreach (ushort id in bm.Keys.Concat(cm.Keys).Concat(nm.Keys).Distinct())
        {
            int value = Pick(bm.GetValueOrDefault(id), cm.GetValueOrDefault(id), nm.GetValueOrDefault(id), "card " + id, conflicts);
            if (value != 0) output[id] = value;
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        for (int category = 1; category <= 3; category++)
        {
            var ids = output.Where(kv => kv.Value == category).Select(kv => kv.Key).Order().ToList();
            writer.Write(checked((ushort)ids.Count));
            foreach (ushort id in ids) writer.Write(id);
        }
        return stream.ToArray();
    }

    private static void RequireZeroPad(params byte[][] files)
    {
        if (files.Any(f => f.Length < 8 || BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(4)) != 0))
            throw new InvalidDataException("Nonzero or missing record-table padding.");
    }

    private static List<ushort> MergeCounts(IEnumerable<ushort> b, IEnumerable<ushort> c, IEnumerable<ushort> n, string label, List<string> conflicts)
    {
        Dictionary<ushort, int> Count(IEnumerable<ushort> list) => list.GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var bm = Count(b); var cm = Count(c); var nm = Count(n);
        var output = new List<ushort>();
        foreach (ushort id in bm.Keys.Concat(cm.Keys).Concat(nm.Keys).Distinct().Order())
        {
            int count = Pick(bm.GetValueOrDefault(id), cm.GetValueOrDefault(id), nm.GetValueOrDefault(id), label + " card " + id, conflicts);
            output.AddRange(Enumerable.Repeat(id, count));
        }
        return output;
    }

    private static Dictionary<string, T> Unique<T>(IEnumerable<T> records, Func<T, string> key, IEqualityComparer<string>? comparer = null)
    {
        var map = new Dictionary<string, T>(comparer ?? StringComparer.Ordinal);
        foreach (T item in records)
            if (!map.TryAdd(key(item), item)) throw new InvalidDataException("Duplicate record key: " + key(item));
        return map;
    }

    private static IEnumerable<string> Keys<T>(params Dictionary<string, T>[] maps) =>
        maps.SelectMany(m => m.Keys).Distinct(maps[0].Comparer);

    private static T Pick<T>(T before, T current, T incoming, string label, List<string> conflicts)
    {
        if (Same(before, incoming) || Same(current, incoming)) return current;
        if (!Same(before, current)) conflicts.Add(label + ": later change wins");
        return incoming;
    }

    private static bool Same<T>(T a, T b) => a is byte[] ab && b is byte[] bb ? Equal(ab, bb)
        : JsonSerializer.Serialize(a, Records) == JsonSerializer.Serialize(b, Records);
    private static bool Equal(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
}
