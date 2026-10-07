using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Types
{
    /// <summary>
    /// The game's "Related cards" data, shown by the deck editor's Related cards panel (docs/RelatedCards.md):
    /// bin/tagdata.bin says which cards are related to each card and why (a tag id), and bin/taginfo_&lt;lang&gt;.bin says what each tag
    /// means. Both are loaded by Setup_CardPropTable (0x14076BFC6) and are required: the card data fails to load without them.
    /// </summary>
    public static class RelatedCards
    {
        public const string TagDataPath = @"bin\tagdata.bin";

        /// <summary>The languages with a taginfo file (no R).</summary>
        public static readonly char[] Languages = ['E', 'F', 'G', 'I', 'J', 'S'];

        public static string TagInfoPath(char language) => $@"bin\taginfo_{char.ToUpperInvariant(language)}.bin";

        public static string TagInfoName(char language) => $"taginfo_{char.ToUpperInvariant(language)}.bin";
    }

    /// <summary>One related card: <see cref="KonamiId"/> is related to the owning card because of tag <see cref="TagId"/>.</summary>
    public readonly record struct RelatedCard(int KonamiId, int TagId);

    /// <summary>
    /// bin/tagdata.bin. <see cref="EntryCount"/> (10166) u32 pairs (start, count), one per INTERNAL card id (CARD_INTID.bin maps a Konami
    /// id to it), then a pool of u32 units (u16 related Konami id, u16 tag id). Each internal id's units are written in turn, sorted by
    /// Konami id, followed by one 0 unit; start is the index of its first unit (an empty list's start is its 0 unit). The engine reads it
    /// through Get_RelatedCardCount (0x14076D640) and Get_RelatedCardList (0x14076D690).
    /// A card's list holds the cards whose effect "tag" matches it: Nekroz of Brionac (a Warrior) lists Legendary Sword because the
    /// Sword's tag is {AD}TYPE:SOLDIER.
    /// </summary>
    public sealed class TagDataTable
    {
        public const int EntryCount = 10166;

        /// <summary>Related cards per internal id (index 0..EntryCount-1).</summary>
        public List<RelatedCard>[] Lists { get; } = new List<RelatedCard>[EntryCount];

        public TagDataTable()
        {
            for (int i = 0; i < EntryCount; i++)
                Lists[i] = [];
        }

        public static TagDataTable Load(string path) => Parse(File.ReadAllBytes(path));

        public static TagDataTable Parse(byte[] data)
        {
            if (data.Length < EntryCount * 8 || data.Length % 4 != 0)
                throw new InvalidDataException($"tagdata.bin starts with {EntryCount} (start, count) pairs; this one is only {data.Length} bytes.");
            var table = new TagDataTable();
            int poolStart = EntryCount * 8, poolUnits = (data.Length - poolStart) / 4;
            for (int i = 0; i < EntryCount; i++)
            {
                uint start = BitConverter.ToUInt32(data, i * 8), count = BitConverter.ToUInt32(data, i * 8 + 4);
                if (count == 0)
                    continue;
                if (start + count > poolUnits)
                    throw new InvalidDataException($"tagdata.bin: internal id {i}'s list ({start}+{count}) runs past the pool ({poolUnits} units).");
                for (uint u = 0; u < count; u++)
                {
                    int at = poolStart + (int)(start + u) * 4;
                    table.Lists[i].Add(new RelatedCard(BitConverter.ToUInt16(data, at), BitConverter.ToUInt16(data, at + 2)));
                }
            }
            return table;
        }

        /// <summary>The game's layout: lists in internal id order, each sorted by Konami id and ended by a 0 unit.</summary>
        public byte[] ToBytes()
        {
            int units = Lists.Sum(list => list.Count + 1);
            var data = new byte[EntryCount * 8 + units * 4];
            int pos = 0, poolStart = EntryCount * 8;
            for (int i = 0; i < EntryCount; i++)
            {
                var list = Sorted(Lists[i]);
                BitConverter.TryWriteBytes(data.AsSpan(i * 8), (uint)pos);
                BitConverter.TryWriteBytes(data.AsSpan(i * 8 + 4), (uint)list.Count);
                foreach (var related in list)
                {
                    BitConverter.TryWriteBytes(data.AsSpan(poolStart + pos * 4), (ushort)related.KonamiId);
                    BitConverter.TryWriteBytes(data.AsSpan(poolStart + pos * 4 + 2), (ushort)related.TagId);
                    pos++;
                }
                pos++;   // the 0 unit (already zero)
            }
            return data;
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        /// <summary>By Konami id, the order the game writes (a stable sort keeps two units for one card in their order).</summary>
        public static List<RelatedCard> Sorted(IEnumerable<RelatedCard> list) => [.. list.OrderBy(related => related.KonamiId)];
    }

    /// <summary>A tag condition's kind (TagCondType in the IDB). <see cref="None"/> marks an unused slot.</summary>
    public enum TagCondType : byte
    {
        Atk = 0, Attr = 1, Def = 2, Kind = 3, Level = 4, Icon = 5, Type = 6, Deck = 7, Rank = 8, SpecialSummon = 9, Tribute = 10,
        Tribute2 = 11, Link = 12, None = 255,
    }

    /// <summary>How a condition compares (TagCompareOp in the IDB).</summary>
    public enum TagCompareOp : byte { LessOrEqual = 0, Less = 1, Equal = 2, GreaterOrEqual = 3, Greater = 4, NotEqual = 5, None = 255 }

    /// <summary>The kind of tag: a plain name ("Related to: Nekroz"), what the card affects ({AD}), or what it searches for ({FIND}).</summary>
    public enum TagGroup : byte { Name = 0, Affects = 1, Matches = 2 }

    public readonly record struct TagCondition(TagCondType Type, TagCompareOp Op, ushort Value)
    {
        public static readonly TagCondition Empty = new(TagCondType.None, TagCompareOp.None, 0);

        public bool IsEmpty => Type == TagCondType.None;
    }

    /// <summary>
    /// One taginfo record (0x34 bytes): Group (u8), Flag1 (u8, set on some Xyz/Pendulum tags), HasNumber (u8, 1 when a condition compares
    /// a number), a pad byte, 8 conditions {u8 type, u8 op, u16 value} (unused = FF FF 0000), then u64 file offsets of Key and Text.
    /// The first four bytes as one u32 are the Related cards list's sort key. Key (e.g. "{AD}ATK:&lt;=1000 DECK:MAIN") is never read by
    /// the game. Text is shown only for Group Name; for Affects/Matches the game builds the text from the conditions (TagInfo_BuildText).
    /// </summary>
    public sealed class TagInfo
    {
        public const int Size = 0x34;
        public const int ConditionCount = 8;

        public TagGroup Group { get; set; }
        public byte Flag1 { get; set; }
        public byte HasNumber { get; set; }
        public byte Pad3 { get; set; }
        public TagCondition[] Conditions { get; } = Enumerable.Repeat(TagCondition.Empty, ConditionCount).ToArray();
        public string Key { get; set; } = "";

        /// <summary>The Text per language.</summary>
        public Dictionary<char, string> Texts { get; } = [];

        public TagInfo Clone()
        {
            var copy = new TagInfo { Group = Group, Flag1 = Flag1, HasNumber = HasNumber, Pad3 = Pad3, Key = Key };
            Conditions.CopyTo(copy.Conditions, 0);
            foreach (var (language, text) in Texts)
                copy.Texts[language] = text;
            return copy;
        }

        public bool SameAs(TagInfo other) =>
            Group == other.Group && Flag1 == other.Flag1 && HasNumber == other.HasNumber && Pad3 == other.Pad3 && Key == other.Key &&
            Conditions.SequenceEqual(other.Conditions) && Texts.Count == other.Texts.Count &&
            Texts.All(t => other.Texts.TryGetValue(t.Key, out var text) && text == t.Value);

        public string Text(char language = 'E') =>
            Texts.TryGetValue(language, out var text) ? text : Texts.TryGetValue('E', out var english) ? english : Texts.Values.FirstOrDefault() ?? "";

        public IEnumerable<TagCondition> UsedConditions => Conditions.Where(c => !c.IsEmpty);

        /// <summary>Sets HasNumber the way the game's files do (a number condition: ATK, DEF, LEVEL, RANK or LINK).</summary>
        public void UpdateHasNumber() =>
            HasNumber = (byte)(UsedConditions.Any(c => c.Type is TagCondType.Atk or TagCondType.Def or TagCondType.Level or TagCondType.Rank or TagCondType.Link) ? 1 : 0);

        // ---- names ----

        public static readonly IReadOnlyDictionary<TagCondType, string> TypeKeys = new Dictionary<TagCondType, string>
        {
            [TagCondType.Atk] = "ATK", [TagCondType.Attr] = "ATTR", [TagCondType.Def] = "DEF", [TagCondType.Kind] = "KIND",
            [TagCondType.Level] = "LEVEL", [TagCondType.Icon] = "ICON", [TagCondType.Type] = "TYPE", [TagCondType.Deck] = "DECK",
            [TagCondType.Rank] = "RANK", [TagCondType.SpecialSummon] = "SPECIALSUMMON", [TagCondType.Tribute] = "TRIBUTE",
            [TagCondType.Tribute2] = "TRIBUTE2", [TagCondType.Link] = "LINK",
        };

        /// <summary>Whether a condition type compares a number (the rest pick a named value).</summary>
        public static bool IsNumeric(TagCondType type) => type is TagCondType.Atk or TagCondType.Def or TagCondType.Level or TagCondType.Rank or TagCondType.Link;

        /// <summary>Named values per condition type, as the game's keys spell them.</summary>
        public static readonly IReadOnlyDictionary<TagCondType, IReadOnlyDictionary<int, string>> ValueNames = new Dictionary<TagCondType, IReadOnlyDictionary<int, string>>
        {
            [TagCondType.Attr] = new Dictionary<int, string> { [1] = "LIGHT", [2] = "DARK", [3] = "WATER", [4] = "FIRE", [5] = "EARTH", [6] = "WIND", [7] = "DIVINE" },
            [TagCondType.Kind] = new Dictionary<int, string>
            {
                [0] = "NORMAL", [1] = "EFFECT", [7] = "SPIRIT", [8] = "UNION", [9] = "DUAL", [13] = "MAGIC", [14] = "TRAP", [19] = "SYNCTUNER",
                [27] = "SP_EFFECT", [30] = "SP_TUNER", [33] = "PEND_TUNER", [46] = "NORMAL*", [47] = "SYNC*", [48] = "XYZ*", [49] = "TUNER*",
                [50] = "FUSION*", [51] = "RITUAL*", [52] = "PEND*", [53] = "FLIP*", [54] = "LINK*", [55] = "UNION*",
            },
            [TagCondType.Icon] = new Dictionary<int, string> { [0] = "NULL", [1] = "COUNTER", [2] = "FIELD", [3] = "EQUIP", [4] = "CONTINUOUS", [5] = "QUICKPLAY", [6] = "RITUAL" },
            [TagCondType.Type] = new Dictionary<int, string>
            {
                [1] = "DRAGON", [2] = "UNDEAD", [3] = "DEVIL", [4] = "FLAME", [5] = "POSEIDON", [6] = "SANDROCK", [7] = "MACHINE", [8] = "FISH",
                [9] = "DINOSAURS", [10] = "INSECT", [11] = "BEAST", [12] = "BEASTBTL", [13] = "BOTANICAL", [14] = "AQUARIUS", [15] = "SOLDIER",
                [16] = "BIRD", [17] = "ANGEL", [18] = "WIZARD", [19] = "THUNDER", [20] = "REPTILES", [21] = "PSYCHIC", [22] = "MYSTDRAGON",
                [23] = "CYVERSE",
            },
            [TagCondType.Deck] = new Dictionary<int, string> { [0] = "MAIN", [1] = "EXTRA" },
            [TagCondType.SpecialSummon] = new Dictionary<int, string> { [1] = "YES" },
            [TagCondType.Tribute] = new Dictionary<int, string> { [1] = "YES" },
            [TagCondType.Tribute2] = new Dictionary<int, string> { [1] = "YES" },
        };

        public static readonly IReadOnlyDictionary<TagCompareOp, string> OpText = new Dictionary<TagCompareOp, string>
        {
            [TagCompareOp.LessOrEqual] = "<=", [TagCompareOp.Less] = "<", [TagCompareOp.Equal] = "=", [TagCompareOp.GreaterOrEqual] = ">=",
            [TagCompareOp.Greater] = ">", [TagCompareOp.NotEqual] = "!=",
        };

        public static string ValueName(TagCondType type, int value) =>
            ValueNames.TryGetValue(type, out var names) && names.TryGetValue(value, out var name) ? name : value.ToString();

        public static string GroupPrefix(TagGroup group) => group switch
        {
            TagGroup.Affects => "{AD}",
            TagGroup.Matches => "{FIND}",
            _ => "",
        };

        /// <summary>The key the game's files would have for these conditions ("{AD}ATTR:DARK KIND:XYZ* RANK:=3"). Name tags keep their own key.</summary>
        public string BuildKey()
        {
            if (Group == TagGroup.Name)
                return Key;
            var parts = UsedConditions.Select(c =>
            {
                string name = TypeKeys.TryGetValue(c.Type, out var key) ? key : ((int)c.Type).ToString();
                return IsNumeric(c.Type)
                    ? $"{name}:{(OpText.TryGetValue(c.Op, out var op) ? op : "=")}{(c.Value == ushort.MaxValue ? "-1" : c.Value.ToString())}"   // 0xFFFF = "?" in game
                    : $"{name}:{(c.Op is TagCompareOp.Equal or TagCompareOp.None ? "" : OpText.GetValueOrDefault(c.Op, ""))}{ValueName(c.Type, c.Value)}";
            });
            return GroupPrefix(Group) + string.Join(" ", parts);
        }

        /// <summary>An English description like the game's ("Related card affects: ATK &lt;= 1000, Main Deck"), or the Text for a name tag.</summary>
        public string Describe(char language = 'E')
        {
            if (Group == TagGroup.Name)
                return Text(language);
            string prefix = Group == TagGroup.Matches ? "Related card matches:" : "Related card affects:";
            var parts = UsedConditions.Select(c => IsNumeric(c.Type)
                ? $"{TypeKeys.GetValueOrDefault(c.Type, "?")} {OpText.GetValueOrDefault(c.Op, "=")} {c.Value}"
                : c.Type switch
                {
                    TagCondType.Deck => c.Value == 1 ? "Extra Deck" : "Main Deck",
                    TagCondType.SpecialSummon => "Special Summon",
                    TagCondType.Tribute => "Tribute Summon (Level 5+)",
                    TagCondType.Tribute2 => "Tribute Summon (Level 7+)",
                    _ => $"{TypeKeys.GetValueOrDefault(c.Type, ((int)c.Type).ToString())} {(c.Op is TagCompareOp.Equal ? "" : OpText.GetValueOrDefault(c.Op, "") + " ")}{ValueName(c.Type, c.Value)}",
                });
            return $"{prefix} {string.Join(", ", parts)}";
        }
    }

    /// <summary>
    /// bin/taginfo_&lt;lang&gt;.bin, all languages together: u32 count, count x 0x34-byte <see cref="TagInfo"/> records, then a UTF-16 pool
    /// holding each record's Key and Text (zero-terminated) in record order; the records store their file offsets (the loader turns them
    /// into pointers). Every language has the same records, conditions and keys; only Text differs.
    /// </summary>
    public sealed class TagInfoTable
    {
        public List<TagInfo> Tags { get; } = [];

        /// <summary>The languages this table was read with (and writes).</summary>
        public List<char> Languages { get; } = [];

        /// <summary>Reads every language given. Throws if the languages disagree on the records (the game's never do).</summary>
        public static TagInfoTable Parse(IDictionary<char, byte[]> files)
        {
            var table = new TagInfoTable();
            foreach (var (language, data) in files.OrderBy(f => f.Key == 'E' ? 0 : 1).ThenBy(f => f.Key))
            {
                var records = ParseOne(data);
                char lang = char.ToUpperInvariant(language);
                if (table.Languages.Count == 0)
                {
                    foreach (var (record, text) in records)
                    {
                        record.Texts[lang] = text;
                        table.Tags.Add(record);
                    }
                }
                else
                {
                    if (records.Count != table.Tags.Count)
                        throw new InvalidDataException($"taginfo_{lang}.bin has {records.Count} tags, taginfo_{table.Languages[0]}.bin has {table.Tags.Count}.");
                    for (int i = 0; i < records.Count; i++)
                    {
                        var (record, text) = records[i];
                        var tag = table.Tags[i];
                        if (record.Group != tag.Group || record.Flag1 != tag.Flag1 || record.HasNumber != tag.HasNumber || record.Pad3 != tag.Pad3 ||
                            record.Key != tag.Key || !record.Conditions.SequenceEqual(tag.Conditions))
                            throw new InvalidDataException($"Tag {i} differs between taginfo_{table.Languages[0]}.bin and taginfo_{lang}.bin (only the text may differ).");
                        tag.Texts[lang] = text;
                    }
                }
                table.Languages.Add(lang);
            }
            return table;
        }

        private static List<(TagInfo Record, string Text)> ParseOne(byte[] data)
        {
            if (data.Length < 4)
                throw new InvalidDataException("A taginfo file starts with a u32 count.");
            uint count = BitConverter.ToUInt32(data, 0);
            if (4 + (long)count * TagInfo.Size > data.Length)
                throw new InvalidDataException($"taginfo: {count} tags don't fit in {data.Length} bytes.");
            var list = new List<(TagInfo, string)>();
            for (int i = 0; i < count; i++)
            {
                int at = 4 + i * TagInfo.Size;
                var tag = new TagInfo { Group = (TagGroup)data[at], Flag1 = data[at + 1], HasNumber = data[at + 2], Pad3 = data[at + 3] };
                for (int c = 0; c < TagInfo.ConditionCount; c++)
                {
                    int cond = at + 4 + c * 4;
                    tag.Conditions[c] = new TagCondition((TagCondType)data[cond], (TagCompareOp)data[cond + 1], BitConverter.ToUInt16(data, cond + 2));
                }
                tag.Key = ReadString(data, BitConverter.ToUInt64(data, at + 0x24));
                list.Add((tag, ReadString(data, BitConverter.ToUInt64(data, at + 0x2C))));
            }
            return list;
        }

        private static string ReadString(byte[] data, ulong offset)
        {
            if (offset >= (ulong)data.Length)
                throw new InvalidDataException($"taginfo: a string offset ({offset}) is past the end of the file.");
            int start = (int)offset, end = start;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0))
                end += 2;
            return Encoding.Unicode.GetString(data, start, end - start);
        }

        /// <summary>One language's file, in the game's layout. A tag with no text in this language uses its English text.</summary>
        public byte[] ToBytes(char language)
        {
            language = char.ToUpperInvariant(language);
            using var records = new MemoryStream();
            using var pool = new MemoryStream();
            long poolStart = 4 + (long)Tags.Count * TagInfo.Size;
            var writer = new BinaryWriter(records);
            writer.Write((uint)Tags.Count);
            foreach (var tag in Tags)
            {
                writer.Write((byte)tag.Group);
                writer.Write(tag.Flag1);
                writer.Write(tag.HasNumber);
                writer.Write(tag.Pad3);
                foreach (var condition in tag.Conditions)
                {
                    writer.Write((byte)condition.Type);
                    writer.Write((byte)condition.Op);
                    writer.Write(condition.Value);
                }
                writer.Write((ulong)(poolStart + pool.Length));
                WriteString(pool, tag.Key);
                writer.Write((ulong)(poolStart + pool.Length));
                WriteString(pool, tag.Text(language));
            }
            writer.Flush();
            pool.Position = 0;
            pool.CopyTo(records);
            return records.ToArray();
        }

        private static void WriteString(Stream stream, string text)
        {
            var bytes = Encoding.Unicode.GetBytes(text);
            stream.Write(bytes);
            stream.WriteByte(0);
            stream.WriteByte(0);
        }

        public TagInfoTable Clone()
        {
            var copy = new TagInfoTable();
            copy.Languages.AddRange(Languages);
            copy.Tags.AddRange(Tags.Select(tag => tag.Clone()));
            return copy;
        }
    }

    /// <summary>
    /// WolfEx's form: Yu-Gi-Oh-Ex\relatedcards.json, only what differs from the game's files.
    /// <code>
    /// { "tags":  [ { "id": 1802, "group": "affects", "conditions": [ { "type": "ATK", "op": "&lt;=", "value": 1000 } ],
    ///                "key": "{AD}ATK:&lt;=1000", "text": { "E": "...", "F": "..." } } ],
    ///   "cards": [ { "card": 15300, "name": "My Card",
    ///                "add":    [ { "card": 4007, "tag": 1802, "name": "Blue-Eyes White Dragon" } ],
    ///                "remove": [ { "card": 4041, "tag": 12 } ] } ] }
    /// </code>
    /// "tags" holds new tags (ids after the game's) and changed game tags, whole. "cards" is keyed by Konami id, so custom cards (15300+)
    /// can have related cards too. Only ids, conditions and texts are read; "name" fields are for people.
    /// </summary>
    public static class RelatedCardsJson
    {
        public const string FileName = "relatedcards.json";

        private static readonly JsonSerializerOptions WriteOptions = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public static JsonObject Load(string path)
        {
            if (!File.Exists(path))
                return [];
            try
            {
                return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? [];
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException($"{path} isn't valid JSON: {ex.Message}");
            }
        }

        public static void Save(string path, JsonObject root)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(WriteOptions));
        }

        private static string GroupName(TagGroup group) => group switch { TagGroup.Affects => "affects", TagGroup.Matches => "matches", _ => "name" };

        private static TagGroup ParseGroup(string? text) => text?.ToLowerInvariant() switch
        {
            "affects" or "ad" => TagGroup.Affects,
            "matches" or "find" => TagGroup.Matches,
            _ => TagGroup.Name,
        };

        public static JsonObject TagToJson(int id, TagInfo tag)
        {
            var conditions = new JsonArray();
            foreach (var c in tag.UsedConditions)
            {
                var condition = new JsonObject
                {
                    ["type"] = TagInfo.TypeKeys.TryGetValue(c.Type, out var type) ? type : ((int)c.Type).ToString(),
                    ["op"] = TagInfo.OpText.TryGetValue(c.Op, out var op) ? op : "=",
                    ["value"] = c.Value,
                };
                if (!TagInfo.IsNumeric(c.Type) && TagInfo.ValueNames.TryGetValue(c.Type, out var names) && names.TryGetValue(c.Value, out var name))
                    condition["name"] = name;
                conditions.Add(condition);
            }
            var texts = new JsonObject();
            foreach (var (language, text) in tag.Texts.OrderBy(t => t.Key))
                texts[language.ToString()] = text;
            var json = new JsonObject { ["id"] = id, ["group"] = GroupName(tag.Group) };
            if (tag.Flag1 != 0)
                json["flag1"] = tag.Flag1;
            if (conditions.Count > 0)
                json["conditions"] = conditions;
            json["key"] = tag.Key;
            json["text"] = texts;
            return json;
        }

        public static TagInfo TagFromJson(JsonObject json, TagInfo? start = null)
        {
            var tag = start?.Clone() ?? new TagInfo();
            tag.Group = ParseGroup(json["group"]?.GetValue<string>());
            tag.Flag1 = (byte)(ReadInt(json["flag1"]) ?? 0);
            if (json["conditions"] is JsonArray conditions)
            {
                for (int i = 0; i < TagInfo.ConditionCount; i++)
                    tag.Conditions[i] = TagCondition.Empty;
                int slot = 0;
                foreach (var node in conditions.OfType<JsonObject>())
                {
                    if (slot >= TagInfo.ConditionCount)
                        break;
                    string typeText = node["type"]?.GetValue<string>() ?? "";
                    TagCondType type;
                    var named = TagInfo.TypeKeys.Where(t => t.Value.Equals(typeText, StringComparison.OrdinalIgnoreCase)).Select(t => (TagCondType?)t.Key).FirstOrDefault();
                    if (named is TagCondType known)
                        type = known;
                    else if (byte.TryParse(typeText, out byte raw) && raw != (byte)TagCondType.None)
                        type = (TagCondType)raw;
                    else
                        continue;   // not a condition type we know
                    string opText = node["op"]?.GetValue<string>() ?? "=";
                    var op = TagInfo.OpText.FirstOrDefault(o => o.Value == opText, new(TagCompareOp.Equal, "=")).Key;
                    int value = ReadInt(node["value"]) ?? 0;
                    tag.Conditions[slot++] = new TagCondition(type, op, (ushort)value);
                }
                tag.UpdateHasNumber();
            }
            if (json["key"] is JsonValue key)
                tag.Key = key.GetValue<string>();
            else if (tag.Group != TagGroup.Name)
                tag.Key = tag.BuildKey();
            if (json["text"] is JsonObject texts)
            {
                foreach (var (language, value) in texts)
                    if (language.Length > 0 && value is JsonValue text)
                        tag.Texts[char.ToUpperInvariant(language[0])] = text.GetValue<string>();
            }
            return tag;
        }

        /// <summary>The JSON for what differs: tags past the game's or changed, and per Konami id the related cards added or removed.</summary>
        public static JsonObject Diff(TagInfoTable baseTags, TagInfoTable tags, IDictionary<int, List<RelatedCard>> baseCards,
            IDictionary<int, List<RelatedCard>> cards, Func<int, string?>? cardName = null)
        {
            var tagArray = new JsonArray();
            for (int id = 0; id < tags.Tags.Count; id++)
                if (id >= baseTags.Tags.Count || !tags.Tags[id].SameAs(baseTags.Tags[id]))
                    tagArray.Add(TagToJson(id, tags.Tags[id]));

            var cardArray = new JsonArray();
            foreach (int card in baseCards.Keys.Concat(cards.Keys).Distinct().Order())
            {
                var before = baseCards.TryGetValue(card, out var b) ? b : [];
                var after = cards.TryGetValue(card, out var a) ? a : [];
                var added = after.Where(r => !before.Contains(r)).ToList();
                var removed = before.Where(r => !after.Contains(r)).ToList();
                if (added.Count == 0 && removed.Count == 0)
                    continue;
                var entry = new JsonObject { ["card"] = card };
                if (cardName?.Invoke(card) is string name)
                    entry["name"] = name;
                if (added.Count > 0)
                    entry["add"] = Units(added, cardName);
                if (removed.Count > 0)
                    entry["remove"] = Units(removed, cardName);
                cardArray.Add(entry);
            }
            return new JsonObject
            {
                ["note"] = "Changes to the game's Related cards data (bin/tagdata.bin + bin/taginfo_<lang>.bin), made by WolfEx. \"tags\": new or changed tags " +
                    "(the game's ids are 0-" + (baseTags.Tags.Count - 1) + "). \"cards\": per Konami id, related cards added or removed, each with the tag that explains why.",
                ["tags"] = tagArray,
                ["cards"] = cardArray,
            };
        }

        private static JsonArray Units(IEnumerable<RelatedCard> units, Func<int, string?>? cardName)
        {
            var array = new JsonArray();
            foreach (var unit in units)
            {
                var json = new JsonObject { ["card"] = unit.KonamiId, ["tag"] = unit.TagId };
                if (cardName?.Invoke(unit.KonamiId) is string name)
                    json["name"] = name;
                array.Add(json);
            }
            return array;
        }

        /// <summary>Applies the JSON: tags first (replacing or appending by id), then each card's removes and adds. Returns how many things changed.</summary>
        public static int Apply(JsonObject root, TagInfoTable tags, IDictionary<int, List<RelatedCard>> cards)
        {
            int changed = 0;
            foreach (var node in (root["tags"] as JsonArray ?? []).OfType<JsonObject>().OrderBy(t => ReadInt(t["id"]) ?? int.MaxValue))
            {
                int id = ReadInt(node["id"]) ?? tags.Tags.Count;
                if (id < 0)
                    continue;
                if (id < tags.Tags.Count)
                    tags.Tags[id] = TagFromJson(node, tags.Tags[id]);
                else
                {
                    while (tags.Tags.Count < id)
                        tags.Tags.Add(new TagInfo { Key = "(unused)", Texts = { ['E'] = "" } });
                    tags.Tags.Add(TagFromJson(node));
                }
                changed++;
            }
            foreach (var entry in (root["cards"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if (ReadInt(entry["card"]) is not int card)
                    continue;
                if (!cards.TryGetValue(card, out var list))
                    cards[card] = list = [];
                foreach (var unit in ReadUnits(entry["remove"]))
                    changed += list.Remove(unit) ? 1 : 0;
                foreach (var unit in ReadUnits(entry["add"]))
                {
                    if (list.Contains(unit))
                        continue;
                    list.Add(unit);
                    changed++;
                }
            }
            return changed;
        }

        /// <summary>A custom card's related cards as its cards.json entry keeps them ("related": [ { "card": 4007, "tag": 12, "name": "..." } ]); null when none.</summary>
        public static JsonArray? ToCardJson(IEnumerable<RelatedCard> units, Func<int, string?>? cardName = null)
        {
            var list = TagDataTable.Sorted(units);
            return list.Count == 0 ? null : Units(list, cardName);
        }

        /// <summary>The related cards of a cards.json "related" list.</summary>
        public static List<RelatedCard> FromCardJson(JsonNode? node) => [.. ReadUnits(node)];

        private static IEnumerable<RelatedCard> ReadUnits(JsonNode? node)
        {
            foreach (var unit in (node as JsonArray ?? []).OfType<JsonObject>())
                if (ReadInt(unit["card"]) is int card and >= 1 and <= ushort.MaxValue && ReadInt(unit["tag"]) is int tag and >= 0 and <= ushort.MaxValue)
                    yield return new RelatedCard(card, tag);
        }

        private static int? ReadInt(JsonNode? node) => node is JsonValue value && value.TryGetValue(out int number) ? number : null;
    }
}
