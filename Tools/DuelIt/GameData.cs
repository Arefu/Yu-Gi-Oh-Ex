using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json.Nodes;
using StartingCollection;
using Wolf.Editors;

namespace DuelIt
{
    /// <summary>What a card is, for drawing it and for the hover card: from the game's bin\CARD_* files, or Yu-Gi-Oh-Ex\cards.json for custom cards.</summary>
    public sealed record CardInfo(int Id, string Name, string Description, string KindText, CardFrame Frame, string Attribute, string Race,
                                  int Level, int Atk, int Def, bool IsCustom, string? ImageFile)
    {
        public bool IsMonster => Frame is not (CardFrame.Spell or CardFrame.Trap or CardFrame.Unknown);

        /// <summary>"[Warrior / Effect]  Level 4  WATER", "Spell", ...</summary>
        public string TypeLine
        {
            get
            {
                if (!IsMonster)
                    return KindText;
                string level = Frame == CardFrame.Link ? $"LINK-{Level}" : Frame == CardFrame.Xyz ? $"Rank {Level}" : $"Level {Level}";
                return $"[{Race} / {KindText}]   {level}   {Attribute.ToUpperInvariant()}";
            }
        }

        public string StatLine => !IsMonster ? "" : Frame == CardFrame.Link ? $"ATK {Stat(Atk)}" : $"ATK {Stat(Atk)} / DEF {Stat(Def)}";

        private static string Stat(int value) => value < 0 ? "?" : value.ToString();
    }

    /// <summary>
    /// Card data and art straight from the game: bin\CARD_Prop.bin / CARD_Indx / CARD_Name / CARD_Desc (loose or inside YGO_2020.dat),
    /// card illustrations from the two .zib archives inside YGO_2020.dat (read one picture at a time, nothing is unpacked), the card
    /// back (card\0000.png), card frames and UI icons, and custom cards from Yu-Gi-Oh-Ex\cards.json with their own art (square
    /// illustrations, like the game's).
    ///
    /// The .zib files: 2020.full.illust_j.jpg.zib has every card (the censored art the game shows); 2020.full.illust_a.jpg.zib only
    /// has the cards whose art differs (uncensored). <see cref="Uncensored"/> prefers _a and falls back to _j.
    /// </summary>
    public sealed class GameData : ICardFaceArt
    {
        private const string CensoredZib = "2020.full.illust_j.jpg.zib";
        private const string UncensoredZib = "2020.full.illust_a.jpg.zib";

        private readonly Dictionary<int, CardInfo> _cards = [];
        private readonly Dictionary<int, (long Start, int Size)> _censored = [];
        private readonly Dictionary<int, (long Start, int Size)> _uncensored = [];
        private readonly Dictionary<(int, bool), Bitmap?> _art = [];
        private readonly object _artLock = new();
        private readonly Dictionary<string, Bitmap?> _frames = [];
        private readonly Dictionary<string, Bitmap?> _icons = [];
        private Bitmap? _iconSheet;
        private Dictionary<string, (Rectangle Source, Point Offset, Size Full)>? _iconList;
        private GameFiles? _files;
        private TocArchive? _toc;
        private string _customFolder = "";

        public static readonly GameData Empty = new();

        public List<string> Warnings { get; } = [];
        public Bitmap? CardBack { get; private set; }
        public bool HasArt => _censored.Count > 0 || _uncensored.Count > 0;

        /// <summary>Show the uncensored illustrations where the game has them.</summary>
        public bool Uncensored { get; set; }

        public CardInfo? Card(int id) => _cards.TryGetValue(id, out var card) ? card : null;

        public string NameOf(int id) => _cards.TryGetValue(id, out var card) ? card.Name : $"#{id}";

        public static GameData Load(string gameFolder, string language = "E")
        {
            var data = new GameData();
            var files = GameFiles.FromGameFolder(gameFolder);
            data._files = files;
            data._toc = files.Toc;
            data._customFolder = Path.Combine(gameFolder, "Yu-Gi-Oh-Ex");

            byte[]? props = files.ReadBytes(Path.Combine("bin", "CARD_Prop.bin"));
            byte[]? index = files.ReadBytes(Path.Combine("bin", $"CARD_Indx_{language}.bin"));
            byte[]? names = files.ReadBytes(Path.Combine("bin", $"CARD_Name_{language}.bin"));
            byte[]? descs = files.ReadBytes(Path.Combine("bin", $"CARD_Desc_{language}.bin"));
            if (props == null || index == null || names == null || descs == null)
                data.Warnings.Add($"The card files (bin\\CARD_Prop/Indx/Name/Desc_{language}) were not found in {gameFolder}.");
            else
                data.ReadGameCards(props, index, names, descs);

            data.ReadCustomCards(Path.Combine(data._customFolder, "cards.json"));

            if (data._toc != null)
            {
                data.ReadZibIndex(CensoredZib, data._censored);
                data.ReadZibIndex(UncensoredZib, data._uncensored);
                data.CardBack = Decode(data._toc.Read(@"card\0000.png"));
            }
            if (!data.HasArt)
                data.Warnings.Add("No card art found (YGO_2020.dat's .zib archives), cards are drawn without pictures.");
            return data;
        }

        // CARD_Prop.bin: 8 bytes per card, in the same order as CARD_Indx (u32 name offset, u32 description offset).
        //   word 0: id 14 bits, ATK/10 9 bits, DEF/10 9 bits (511 = "?")
        //   word 1: bit 0 unknown, kind 6 bits, attribute 4, level 4, spell/trap icon 3, race 5, pendulum scales 4 + 4
        // (File Type Libraries\CARD_Props has the same layout.)
        private void ReadGameCards(byte[] props, byte[] index, byte[] names, byte[] descs)
        {
            int count = Math.Min(props.Length / 8, index.Length / 8);
            for (int i = 0; i < count; i++)
            {
                uint first = BinaryPrimitives.ReadUInt32LittleEndian(props.AsSpan(i * 8));
                uint second = BinaryPrimitives.ReadUInt32LittleEndian(props.AsSpan(i * 8 + 4));
                int id = (int)(first & 0x3FFF);
                if (id == 0)
                    continue;
                int atk = (int)((first >> 14) & 0x1FF), def = (int)((first >> 23) & 0x1FF);
                int kind = (int)((second >> 1) & 0x3F);
                int attribute = (int)((second >> 7) & 0xF);
                int level = (int)((second >> 11) & 0xF);
                int race = (int)((second >> 18) & 0x1F);

                string name = ReadString(names, BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i * 8)));
                string desc = ReadString(descs, BinaryPrimitives.ReadUInt32LittleEndian(index.AsSpan(i * 8 + 4)));
                if (name.Length == 0)
                    continue;
                _cards[id] = new CardInfo(id, name, desc, CardNames.KindName(kind), CardNames.FrameOf(kind), CardNames.AttributeName(attribute), CardNames.RaceName(race), level,
                    atk == 511 ? -1 : atk * 10, def == 511 ? -1 : def * 10, false, null);
            }
        }

        private static string ReadString(byte[] data, uint offset)
        {
            if (offset >= data.Length)
                return "";
            int end = (int)offset;
            while (end + 1 < data.Length && (data[end] != 0 || data[end + 1] != 0))
                end += 2;
            return Encoding.Unicode.GetString(data, (int)offset, end - (int)offset);
        }

        private void ReadCustomCards(string path)
        {
            if (!File.Exists(path))
                return;
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
                var array = root as JsonArray ?? root?["cards"] as JsonArray;
                foreach (var node in array?.OfType<JsonObject>() ?? [])
                {
                    if (node["id"] is not JsonValue idValue || !idValue.TryGetValue<int>(out int id))
                        continue;
                    string Text(string key) => node[key] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
                    int Number(string key) => node[key] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;

                    string kind = Text("kind");
                    var frame = kind.ToLowerInvariant() switch
                    {
                        "normal" => CardFrame.Normal,
                        "spell" => CardFrame.Spell,
                        "trap" => CardFrame.Trap,
                        var k when k.Contains("fusion") => CardFrame.Fusion,
                        var k when k.Contains("synchro") => CardFrame.Synchro,
                        var k when k.Contains("xyz") => CardFrame.Xyz,
                        var k when k.Contains("link") => CardFrame.Link,
                        var k when k.Contains("ritual") => CardFrame.Ritual,
                        "" => CardFrame.Unknown,
                        _ => CardFrame.Effect,
                    };
                    string image = Text("image");
                    _cards[id] = new CardInfo(id, Text("name") is { Length: > 0 } n ? n : $"Custom card {id}", Text("description"),
                        kind.Length > 0 ? kind : "Custom", frame, Text("attribute"), Text("type"), Number("level"), Number("atk"), Number("def"),
                        true, image.Length > 0 ? Path.Combine(_customFolder, image) : null);
                }
            }
            catch (Exception ex)
            {
                Warnings.Add($"Could not read {path}: {ex.Message}");
            }
        }

        // .zib: a list of 64-byte entries (big-endian u32 start, u32 size, 56-byte name such as "10011.jpg"), then the files. The
        // first entry's start is where the list ends.
        private void ReadZibIndex(string zib, Dictionary<int, (long, int)> into)
        {
            if (_toc == null || _toc.Read(zib, 0, 64) is not { Length: 64 } head)
                return;
            long dataStart = BinaryPrimitives.ReadUInt32BigEndian(head) / 4 * 4;
            if (dataStart < 64 || dataStart > 16 * 1024 * 1024 || _toc.Read(zib, 0, (int)dataStart) is not { } list)
                return;
            for (int at = 0; at + 64 <= list.Length; at += 64)
            {
                long start = BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(at)) / 4 * 4;
                int size = (int)BinaryPrimitives.ReadUInt32BigEndian(list.AsSpan(at + 4));
                string name = Encoding.ASCII.GetString(list, at + 8, 56).TrimEnd('\0');
                if (size > 0 && int.TryParse(Path.GetFileNameWithoutExtension(name), out int id))
                    into[id] = (start, size);
            }
        }

        /// <summary>
        /// The card frame the game draws for this kind of card (duelrame\card_*.png, 400 x 580). Loose files win over the archive,
        /// so installed frame mods (Mods\Anime Frames) are what's shown, as in the game.
        /// </summary>
        public Bitmap? Frame(CardFrame frame)
        {
            string name = CardNames.FrameFile(frame);
            lock (_artLock)
            {
                if (!_frames.TryGetValue(name, out var bitmap))
                    _frames[name] = bitmap = _files == null ? null : Decode(_files.ReadBytes(Path.Combine("duel", "frame", name + ".png")));
                return bitmap;
            }
        }

        /// <summary>A sprite from the game's UI atlas pdui\STEAM_icons (e.g. "ICON_ID_ATTR_FIRE", "ICON_ID_LEVEL"), untrimmed, or null.</summary>
        public Bitmap? Icon(string name)
        {
            lock (_artLock)
            {
                if (_icons.TryGetValue(name, out var cached))
                    return cached;
                Bitmap? icon = null;
                if (_files != null)
                {
                    _iconSheet ??= Decode(_files.ReadBytes(Path.Combine("pdui", "STEAM_icons.png")));
                    _iconList ??= _files.ReadBytes(Path.Combine("pdui", "STEAM_icons.dfymoo")) is { } list ? ParseSprites(Encoding.UTF8.GetString(list)) : [];
                    if (_iconSheet != null && _iconList.TryGetValue(name, out var sprite))
                    {
                        icon = new Bitmap(sprite.Full.Width, sprite.Full.Height, PixelFormat.Format32bppPArgb);
                        using var g = Graphics.FromImage(icon);
                        g.DrawImage(_iconSheet, new Rectangle(sprite.Offset, sprite.Source.Size), sprite.Source, GraphicsUnit.Pixel);
                    }
                }
                _icons[name] = icon;
                return icon;
            }
        }

        /// <summary>tk2d sprite list: "n name", "s x y w h" (on the sheet), optional "o x y w h" (placement in the untrimmed w x h), "~" between items.</summary>
        private static Dictionary<string, (Rectangle Source, Point Offset, Size Full)> ParseSprites(string text)
        {
            var sprites = new Dictionary<string, (Rectangle, Point, Size)>(StringComparer.OrdinalIgnoreCase);
            foreach (string block in text.Split('~', StringSplitOptions.RemoveEmptyEntries))
            {
                string? name = null;
                Rectangle source = Rectangle.Empty;
                Point offset = Point.Empty;
                Size full = Size.Empty;
                foreach (string raw in block.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2 && parts[0] == "n")
                        name = parts[1];
                    else if (parts.Length == 5 && (parts[0] == "s" || parts[0] == "o") && parts.Skip(1).All(p => int.TryParse(p, out _)))
                    {
                        int[] v = parts.Skip(1).Select(int.Parse).ToArray();
                        if (parts[0] == "s")
                            source = new Rectangle(v[0], v[1], v[2], v[3]);
                        else
                        {
                            offset = new Point(v[0], v[1]);
                            full = new Size(v[2], v[3]);
                        }
                    }
                }
                if (name != null && !source.IsEmpty)
                    sprites[name] = (source, offset, full.IsEmpty ? source.Size : full);
            }
            return sprites;
        }

        /// <summary>The card's picture (cached; safe to call from paint), or null.</summary>
        public Bitmap? Art(int id)
        {
            bool uncensored = Uncensored;
            lock (_artLock)
            {
                if (_art.TryGetValue((id, uncensored), out var cached))
                    return cached;
                if (_art.Count > 800)
                    _art.Clear();   // simple cap; a duel shows a few hundred at most

                Bitmap? art = null;
                if (Card(id) is { ImageFile: { } file } && File.Exists(file))
                {
                    try { art = Decode(File.ReadAllBytes(file)); } catch (IOException) { }
                }
                else if (_toc != null)
                {
                    if (uncensored && _uncensored.TryGetValue(id, out var alt))
                        art = Decode(_toc.Read(UncensoredZib, alt.Start, alt.Size));
                    if (art == null && _censored.TryGetValue(id, out var entry))
                        art = Decode(_toc.Read(CensoredZib, entry.Start, entry.Size));
                }
                _art[(id, uncensored)] = art;
                return art;
            }
        }

        /// <summary>Decodes into 32 bpp premultiplied ARGB, which GDI+ draws without converting every time.</summary>
        private static Bitmap? Decode(byte[]? data)
        {
            if (data == null)
                return null;
            try
            {
                using var stream = new MemoryStream(data);
                using var image = Image.FromStream(stream);
                var fast = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppPArgb);
                fast.SetResolution(96, 96);
                using var g = Graphics.FromImage(fast);
                g.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
                return fast;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
