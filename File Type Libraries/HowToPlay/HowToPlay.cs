using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Types
{
    /// <summary>What a howtoplay record is (the kind byte).</summary>
    public enum HowToKind : byte
    {
        Chapter = 0,     // a chapter in the list on the left ("Duel Basics")
        Topic = 1,       // a topic heading ("●What is a Deck?")
        SubTopic = 2,    // a sub-topic heading, shown with a "-" in front
        End = 4,         // the last record (empty text)
        Body = 0xFF,     // the article text for the heading before it
    }

    /// <summary>One record: its kind, its picture (body records only: main/howto_img/help_duelimg_NNN.png, 0 = none) and its text.</summary>
    public sealed class HowToRecord
    {
        public HowToKind Kind { get; set; }
        public byte Picture { get; set; }
        public string Text { get; set; } = "";

        public override string ToString() => $"{Kind} {Picture}: {Text}";
    }

    /// <summary>
    /// The How to Play help (main/howto_db/howtoplay_&lt;L&gt;.bin, one file per language letter; read by
    /// RIX::ScreenHelpHowToPlay::LoadDatabase, 0x1408485E0). Big-endian:
    ///
    ///   u32 count, u32 0
    ///   count x { u8 kind, u8 picture, u16 length, u32 0 }      the last one is kind 4 (end)
    ///   count x UTF-16BE string, length characters + a null     in record order (the end record's is empty)
    ///
    /// The game builds its screen from it: each chapter takes (heading, body) pairs until the next chapter, a heading being a topic or
    /// a sub-topic. There is no fixed limit on chapters or topics (the game keeps them in vectors).
    ///
    /// Text markup (YGO::TEXT::Layout_BreakLines): "@" + 0-9 / A-G switches colour (<see cref="HowToText.Colours"/>; "@71 [Name]" is
    /// colour 7 then "1 [Name]"), "@/" and "@|" the alternate font on and off, "$R(...)" ruby text, "{{" a tag, U+E000-U+F8FF icons.
    /// </summary>
    public sealed class HowToPlayFile
    {
        public List<HowToRecord> Records { get; } = [];

        /// <summary>The language letters the game ships (English, French, German, Italian, Japanese, Spanish). Others load the same way.</summary>
        public static readonly IReadOnlyDictionary<char, string> KnownLanguages = new Dictionary<char, string>
        {
            ['E'] = "English", ['F'] = "French", ['G'] = "German", ['I'] = "Italian", ['J'] = "Japanese", ['S'] = "Spanish",
            // R is not Russian: its files are the Japanese text with ruby readings ($R漢字(かんじ)); g_LanguageLetterTable maps language id 6 to 'r'
            ['R'] = "Japanese (ruby)", ['P'] = "Portuguese", ['K'] = "Korean", ['C'] = "Chinese",
        };

        public const string GameFolder = @"main\howto_db";
        public const string PictureFolder = @"main\howto_img";

        public static string FileName(char language) => $"howtoplay_{char.ToUpperInvariant(language)}.bin";

        public static string GamePath(char language) => Path.Combine(GameFolder, FileName(language));

        public static string PicturePath(int picture) => Path.Combine(PictureFolder, $"help_duelimg_{picture:000}.png");

        /// <summary>The language letter in a file name ("howtoplay_G.bin" -> 'G'), or null.</summary>
        public static char? LanguageOf(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name.StartsWith("howtoplay_", StringComparison.OrdinalIgnoreCase) && name.Length == "howtoplay_".Length + 1
                ? char.ToUpperInvariant(name[^1]) : null;
        }

        public static string LanguageName(char language) =>
            KnownLanguages.TryGetValue(char.ToUpperInvariant(language), out var name) ? name : $"Language {char.ToUpperInvariant(language)}";

        public static HowToPlayFile Load(string path) => Parse(File.ReadAllBytes(path));

        public static HowToPlayFile Parse(byte[] data)
        {
            if (data.Length < 8)
                throw new InvalidDataException("Too short for a howtoplay file.");
            int count = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));
            if (count < 0 || 8 + 8L * count > data.Length)
                throw new InvalidDataException($"The record table ({count} records) runs past the end of the file.");

            var file = new HowToPlayFile();
            int text = 8 + 8 * count;
            for (int i = 0; i < count; i++)
            {
                int at = 8 + 8 * i;
                var record = new HowToRecord { Kind = (HowToKind)data[at], Picture = data[at + 1] };
                int length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(at + 2));
                if (length != 0xFFFF)
                {
                    if (text + 2 * length + 2 > data.Length)
                        throw new InvalidDataException($"Record {i}'s text runs past the end of the file.");
                    record.Text = Encoding.BigEndianUnicode.GetString(data, text, 2 * length);
                    text += 2 * length + 2;
                }
                file.Records.Add(record);
            }
            return file;
        }

        public byte[] ToBytes()
        {
            var records = Records.ToList();
            if (records.Count == 0 || records[^1].Kind != HowToKind.End)
                records.Add(new HowToRecord { Kind = HowToKind.End });

            using var stream = new MemoryStream();
            Span<byte> word = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(word, (uint)records.Count);
            stream.Write(word);
            stream.Write(new byte[4]);
            foreach (var record in records)
            {
                if (record.Text.Length >= 0xFFFF)
                    throw new InvalidDataException($"A text is too long ({record.Text.Length} characters, the most is 65534).");
                stream.WriteByte((byte)record.Kind);
                stream.WriteByte(record.Picture);
                BinaryPrimitives.WriteUInt16BigEndian(word, (ushort)record.Text.Length);
                stream.Write(word[..2]);
                stream.Write(new byte[4]);
            }
            foreach (var record in records)
            {
                stream.Write(Encoding.BigEndianUnicode.GetBytes(record.Text));
                stream.Write(new byte[2]);
            }
            return stream.ToArray();
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        // ---- the structure the game shows ----

        /// <summary>Chapters and their topics, as the game groups them. Records that don't fit the pattern are reported in problems.</summary>
        public List<HowToChapter> ToChapters(List<string>? problems = null)
        {
            var chapters = new List<HowToChapter>();
            HowToChapter? chapter = null;
            for (int i = 0; i < Records.Count; i++)
            {
                var record = Records[i];
                switch (record.Kind)
                {
                    case HowToKind.Chapter:
                        chapter = new HowToChapter { Title = record.Text };
                        chapters.Add(chapter);
                        break;
                    case HowToKind.Topic:
                    case HowToKind.SubTopic:
                    {
                        var topic = new HowToTopic { Title = record.Text, IsSubTopic = record.Kind == HowToKind.SubTopic };
                        if (i + 1 < Records.Count && Records[i + 1].Kind == HowToKind.Body)
                        {
                            topic.Body = Records[i + 1].Text;
                            topic.Picture = Records[i + 1].Picture;
                            i++;
                        }
                        else
                            problems?.Add($"Record {i} (\"{record.Text}\") has no body after it.");
                        if (chapter == null)
                        {
                            problems?.Add($"Record {i} (\"{record.Text}\") comes before the first chapter.");
                            chapter = new HowToChapter { Title = "(no chapter)" };
                            chapters.Add(chapter);
                        }
                        chapter.Topics.Add(topic);
                        break;
                    }
                    case HowToKind.Body:
                        problems?.Add($"Record {i} is a body with no heading before it.");
                        break;
                    case HowToKind.End:
                        break;
                    default:
                        problems?.Add($"Record {i} has an unknown kind {(byte)record.Kind}.");
                        break;
                }
            }
            return chapters;
        }

        public static HowToPlayFile FromChapters(IEnumerable<HowToChapter> chapters)
        {
            var file = new HowToPlayFile();
            foreach (var chapter in chapters)
            {
                file.Records.Add(new HowToRecord { Kind = HowToKind.Chapter, Text = chapter.Title });
                foreach (var topic in chapter.Topics)
                {
                    file.Records.Add(new HowToRecord { Kind = topic.IsSubTopic ? HowToKind.SubTopic : HowToKind.Topic, Text = topic.Title });
                    file.Records.Add(new HowToRecord { Kind = HowToKind.Body, Picture = topic.Picture, Text = topic.Body });
                }
            }
            file.Records.Add(new HowToRecord { Kind = HowToKind.End });
            return file;
        }

        // ---- JSON form (what WolfEx saves in Yu-Gi-Oh-Ex\howtoplay; a plugin turns it back into the game's form) ----

        public const string JsonFolder = "howtoplay";

        public static string JsonName(char language) => $"howtoplay_{char.ToUpperInvariant(language)}.json";

        /// <summary>
        /// The help as JSON a person can read and edit (pictures are PNGs next to it, help_duelimg_NNN.png):
        ///
        ///   { "language": "E",
        ///     "chapters": [ { "title": "Duel Basics",
        ///                     "topics": [ { "title": "●What is a Deck?", "picture": 16, "body": "..." },
        ///                                 { "title": "...", "subTopic": true, "body": "..." } ] } ] }
        ///
        /// It converts back byte for byte (same as the script form).
        /// </summary>
        public string ToJson(char language)
        {
            var chapters = new System.Text.Json.Nodes.JsonArray();
            foreach (var chapter in ToChapters())
            {
                var topics = new System.Text.Json.Nodes.JsonArray();
                foreach (var topic in chapter.Topics)
                {
                    var node = new System.Text.Json.Nodes.JsonObject { ["title"] = topic.Title };
                    if (topic.IsSubTopic)
                        node["subTopic"] = true;
                    if (topic.Picture != 0)
                        node["picture"] = topic.Picture;
                    node["body"] = topic.Body;
                    topics.Add(node);
                }
                chapters.Add(new System.Text.Json.Nodes.JsonObject { ["title"] = chapter.Title, ["topics"] = topics });
            }
            var root = new System.Text.Json.Nodes.JsonObject { ["language"] = char.ToUpperInvariant(language).ToString(), ["chapters"] = chapters };
            return root.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
        }

        public static HowToPlayFile FromJson(string json, List<string>? problems = null)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json, documentOptions: new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true,
            }) as System.Text.Json.Nodes.JsonObject ?? throw new InvalidDataException("A How to Play JSON is an object with \"chapters\".");
            var chapters = new List<HowToChapter>();
            foreach (var item in root["chapters"] as System.Text.Json.Nodes.JsonArray ?? [])
            {
                if (item is not System.Text.Json.Nodes.JsonObject c)
                    continue;
                var chapter = new HowToChapter { Title = c["title"]?.GetValue<string>() ?? "" };
                foreach (var t in c["topics"] as System.Text.Json.Nodes.JsonArray ?? [])
                {
                    if (t is not System.Text.Json.Nodes.JsonObject topic)
                        continue;
                    int picture = topic["picture"]?.GetValue<int>() ?? 0;
                    if (picture is < 0 or > 255)
                    {
                        problems?.Add($"\"{topic["title"]}\": picture {picture} isn't 0-255.");
                        picture = 0;
                    }
                    chapter.Topics.Add(new HowToTopic
                    {
                        Title = topic["title"]?.GetValue<string>() ?? "",
                        IsSubTopic = topic["subTopic"]?.GetValue<bool>() ?? false,
                        Picture = (byte)picture,
                        Body = topic["body"]?.GetValue<string>() ?? "",
                    });
                }
                chapters.Add(chapter);
            }
            return FromChapters(chapters);
        }

        // ---- script form ----

        /// <summary>
        /// The whole file as editable text:
        ///
        ///   # Chapter title
        ///   ## Topic title
        ///   ### Sub-topic title          (the game puts a "-" in front)
        ///   #! picture 16                (optional, straight after a heading)
        ///   the body text, as many lines as it takes, markup (@7, @0, ...) as is
        ///
        /// A body line that starts with # or \ gets a \ in front. Newlines in the file are \n; the text is written back exactly.
        /// </summary>
        public string ToScript()
        {
            var text = new StringBuilder();
            text.Append("# How to Play script - # chapter, ## topic, ### sub-topic, #! picture N; body text follows its heading. Lines starting with #: comments are ignored.\n");
            foreach (var chapter in ToChapters())
            {
                text.Append("\n# ").Append(chapter.Title).Append('\n');
                foreach (var topic in chapter.Topics)
                {
                    text.Append(topic.IsSubTopic ? "### " : "## ").Append(topic.Title).Append('\n');
                    if (topic.Picture != 0)
                        text.Append("#! picture ").Append(topic.Picture.ToString(CultureInfo.InvariantCulture)).Append('\n');
                    foreach (string line in topic.Body.Split('\n'))
                        text.Append(line.StartsWith('#') || line.StartsWith('\\') ? "\\" + line : line).Append('\n');
                }
            }
            return text.ToString();
        }

        /// <summary>Reads the script form back. Errors say which line.</summary>
        public static HowToPlayFile ParseScript(string script, List<string>? problems = null)
        {
            var chapters = new List<HowToChapter>();
            HowToChapter? chapter = null;
            HowToTopic? topic = null;
            var body = new List<string>();
            int lineNumber = 0;

            // The body is every line up to the next heading. ToScript writes a blank line before each chapter heading and ends the
            // file with a newline, so those (and only those) are dropped: the body comes back exactly as it was.
            void EndTopic(bool dropBlankLine)
            {
                if (topic == null)
                    return;
                if (dropBlankLine && body.Count > 0 && body[^1].Length == 0)
                    body.RemoveAt(body.Count - 1);
                topic.Body = string.Join("\n", body);
                body.Clear();
                topic = null;
            }

            foreach (string raw in script.Replace("\r\n", "\n").Split('\n'))
            {
                lineNumber++;
                string line = raw;
                if (line.StartsWith("#: ") || (line.StartsWith("# How to Play script") && chapter == null))
                    continue;
                if (line.StartsWith("### ") || line.StartsWith("## "))
                {
                    EndTopic(false);
                    if (chapter == null)
                    {
                        problems?.Add($"Line {lineNumber}: a topic before the first chapter (# ...).");
                        chapter = new HowToChapter { Title = "(no chapter)" };
                        chapters.Add(chapter);
                    }
                    bool sub = line.StartsWith("### ");
                    topic = new HowToTopic { IsSubTopic = sub, Title = line[(sub ? 4 : 3)..] };
                    chapter.Topics.Add(topic);
                    continue;
                }
                if (line.StartsWith("# "))
                {
                    EndTopic(true);
                    chapter = new HowToChapter { Title = line[2..] };
                    chapters.Add(chapter);
                    continue;
                }
                if (line.StartsWith("#! picture"))
                {
                    if (topic == null || body.Count > 0)
                        problems?.Add($"Line {lineNumber}: '#! picture' goes straight after a topic heading.");
                    else if (!byte.TryParse(line["#! picture".Length..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out byte picture))
                        problems?.Add($"Line {lineNumber}: the picture number must be 0-255.");
                    else
                        topic.Picture = picture;
                    continue;
                }
                if (topic == null)
                {
                    if (line.Trim().Length > 0)
                        problems?.Add($"Line {lineNumber}: text outside a topic (put it after a ## or ### heading).");
                    continue;
                }
                body.Add(line.StartsWith('\\') ? line[1..] : line);
            }
            EndTopic(true);
            return FromChapters(chapters);
        }
    }

    public sealed class HowToChapter
    {
        public string Title { get; set; } = "";
        public List<HowToTopic> Topics { get; } = [];

        public override string ToString() => Title;
    }

    public sealed class HowToTopic
    {
        public string Title { get; set; } = "";
        public bool IsSubTopic { get; set; }
        public string Body { get; set; } = "";
        public byte Picture { get; set; }

        public override string ToString() => (IsSubTopic ? "- " : "") + Title;
    }

    /// <summary>The game's text markup, for previews (YGO::TEXT::Layout_BreakLines, g_TextColorTable 0x140C8D170).</summary>
    public static class HowToText
    {
        /// <summary>Colours "@0".."@9", "@A".."@G" select (RGBA in the exe).</summary>
        public static readonly uint[] Colours =
        [
            0xF0F0F0, 0x002945, 0x37DD97, 0x0EC9FF, 0x00EAFF, 0x6600FF, 0xFF8400, 0x0084FF, 0xFF00FF,
            0x004D0B, 0x0000BB, 0xB00C00, 0x00D957, 0x808080, 0xFFFFFF, 0xFFFFFF, 0x0000FF,
        ];

        /// <summary>A run of text in one colour (Colour = index into <see cref="Colours"/>), or a line break (Text == "\n").</summary>
        /// <summary>Ruby is the reading shown small above Text ("$R名(めい)": Text "名", Ruby "めい"), or null.</summary>
        public readonly record struct Run(string Text, int Colour, bool AltFont, string? Ruby = null);

        /// <summary>Splits text into coloured runs the way the game reads its markup.</summary>
        public static List<Run> Parse(string text)
        {
            var runs = new List<Run>();
            var current = new StringBuilder();
            int colour = 0;
            bool alt = false;

            void Flush()
            {
                if (current.Length > 0)
                    runs.Add(new Run(current.ToString(), colour, alt));
                current.Clear();
            }

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '@' && next != '\0' && !(next >= 'H' && next <= 'Z'))
                {
                    int index = next >= '0' && next <= '9' ? next - '0' : next >= 'A' && next <= 'G' ? next - 'A' + 10 : -1;
                    if (index >= 0)
                    {
                        Flush();
                        colour = index;
                        i++;
                        continue;
                    }
                    if (next == '/' || next == '|')
                    {
                        Flush();
                        alt = next == '/';
                        i++;
                        continue;
                    }
                }
                // "$R" base "(" reading ")": ruby (furigana), as Layout_BreakLines reads it
                if (c == '$' && next == 'R')
                {
                    int open = text.IndexOf('(', i + 2), close = open < 0 ? -1 : text.IndexOf(')', open + 1);
                    if (open > i + 2 && close > open)
                    {
                        Flush();
                        runs.Add(new Run(text[(i + 2)..open], colour, alt, text[(open + 1)..close]));
                        i = close;
                        continue;
                    }
                }
                if (c == '\r')
                    continue;
                if (c == '\n')
                {
                    Flush();
                    runs.Add(new Run("\n", colour, alt));
                    continue;
                }
                current.Append(c);
            }
            Flush();
            return runs;
        }
    }
}
