using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Types
{
    /// <summary>
    /// What a tutorial step does (its first byte), as YGO::TUTORIAL::ExecuteStep (0x1407F5D50) runs it. Values the game doesn't handle
    /// (8, 0x10, 0x16, 0x1A, ...) do nothing.
    /// </summary>
    public enum TutorialOp : byte
    {
        SetupScenario = 0x00,   // P2 = board setup (hardcoded in YGO::TUTORIAL::SetupScenario: 1, 31, 32, 41-46, 51, 52, 61, 62, 71-73, 91, 92, 101-105, 111, 112, 121, 122, 141-143, 161, 162, 171-173, 200, 210)
        Message = 0x01,         // Text in the message box; P3 != 0 puts the box higher. Waits for the player to read it
        Hint = 0x02,            // Text in the hint line (no text = hide it)
        SetTurnPlayer = 0x03,   // P2 = whose turn (0 you, 1 the opponent), plays the turn animation
        Phase = 0x04,           // P2 = phase; P3 != 0 only plays the phase banner
        MoveCursor = 0x05,      // P1 = player, P2 = zone, P3 = index (zone 12 with a card id in P3: that card in the hand)
        Wait = 0x06,            // P2 = frames (60 a second)
        WaitForAction = 0x07,   // P1 = what the player must do (see TutorialFile.ActionName), P2 = its detail; optional hint text
        Flag09 = 0x09,          // P1 = 6 sets a duel UI flag
        Set0A = 0x0A,           // stores P2 and P3 (tutorial state +0x538/+0x53C)
        ShowCard = 0x0B,        // shows card P2 (Konami id) in the card info panel; 0 = clear it, 2 = the card the game has in hand for this (+0x16AD)
        PointerSimple = 0x0C,   // P1 = pointer target, 0 hides the pointer
        Pointer = 0x0D,         // points at player P1's zone P2 (P3 = extra)
        ActivateAt = 0x0E,      // activates player P1's card in zone P2 if it can be
        Set0F = 0x0F,           // stores P2 and P4 (+0x54C/+0x534)
        Label = 0x11,           // a jump target named P1 (Goto)
        RetryStart = 0x12,      // start of a block RetryEnd can go back to
        RetryEnd = 0x13,
        AllowInput = 0x14,      // P2 != 0 lets the player act
        Set15 = 0x15,           // P2 = 1 unlocks every input
        StackDeck = 0x17,       // puts card P4 at index P3 of player P2's deck (swaps it up from below)
        Flag18 = 0x18,
        Set19 = 0x19,           // stores P2 (+0x870)
        Set1B = 0x1B,           // stores P2 (+0x874)
        AllowCards = 0x1C,      // the cards the player may use now: P2, P3, P4 (0 = none)
        Set1D = 0x1D,           // stores P2 (+0x898)
        Goto = 0x1E,            // jumps to Label P2
        End = 0x7F,             // P1: 0 back to the menu, 1 win the duel, 2 leave tutorial mode. Marks the tutorial done in the save
    }

    /// <summary>Tutorial text as the game expands it before layout (YGO::TUTORIAL::ExpandText 0x1407F2E90).</summary>
    public static class TutorialText
    {
        /// <summary>
        /// "$" + digits = the card's name (cardName returns null for an unknown id: the game shows "@8NNNN@0"), "$A" "$B" "$X" "$Y" =
        /// button icons (shown here as [A] etc.), any other "$" stays. Colour markup (@n) is left for the text layout (HowToText.Parse).
        /// </summary>
        public static string Expand(string text, Func<int, string?> cardName)
        {
            var result = new StringBuilder(text.Length + 32);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                char next = i + 1 < text.Length ? text[i + 1] : '\0';
                if (c == '$' && char.IsAsciiDigit(next))
                {
                    int start = ++i;
                    while (i < text.Length && char.IsAsciiDigit(text[i]))
                        i++;
                    int id = int.TryParse(text.AsSpan(start, i - start), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : 0;
                    i--;
                    result.Append(cardName(id) ?? $"@8{id:0000}@0");
                    continue;
                }
                if (c == '$' && next is 'A' or 'B' or 'X' or 'Y')
                {
                    result.Append('[').Append(next).Append(']');
                    i++;
                    continue;
                }
                result.Append(c);
            }
            return result.ToString();
        }
    }

    /// <summary>One 12-byte step: {u8 op, u8 p1, u16 p2, u16 p3, u16 p4, u16 id, i16 text}.</summary>
    public sealed class TutorialStep
    {
        public TutorialOp Op { get; set; }
        public byte P1 { get; set; }
        public ushort P2 { get; set; }
        public ushort P3 { get; set; }
        public ushort P4 { get; set; }

        /// <summary>The step's number in the file (never read by the game; counts up, End has 0xFFFF).</summary>
        public ushort Id { get; set; }

        /// <summary>Index into the text table, or -1 for none.</summary>
        public short Text { get; set; } = -1;

        public TutorialStep Clone() => (TutorialStep)MemberwiseClone();

        public override string ToString() => $"{Id}: {Op} {P1} {P2} {P3} {P4} text {Text}";
    }

    /// <summary>
    /// A Steam tutorial (duel/tutorial/steam_tutorial_NN_&lt;L&gt;.bin, NN = 01-26, one file per language letter). Loaded by
    /// YGO::TUTORIAL::LoadScriptFile (0x1408709D0) through LoadFileFromArchives, so a loose copy in YGO_2020 replaces it. Little-endian:
    ///
    ///   u16 stepCount, u16 textCount, u32 0, u32 0          the game writes the step and text table offsets over the two zeros
    ///   stepCount x 12-byte step                             the last one is End (0x7F), id 0xFFFF
    ///   textCount x { u32 length, u32 0 }                    length in characters; the game fills in each string's offset
    ///   textCount x UTF-16LE string, length chars + a null   in order
    ///
    /// Text markup: "$" + digits = that card's name (Konami id), "$A" "$B" "$X" "$Y" = button icons, "@n" colours as in How to Play,
    /// "\n" = new line (YGO::TUTORIAL::ExpandText 0x1407F2E90).
    /// </summary>
    public sealed class TutorialFile
    {
        public const int HeaderSize = 12;
        public const int StepSize = 12;
        public const string GameFolder = @"duel\tutorial";
        public const int Count = 26;

        public List<TutorialStep> Steps { get; } = [];
        public List<string> Texts { get; } = [];

        public static string FileName(int number, char language) => $"steam_tutorial_{number:00}_{char.ToUpperInvariant(language)}.bin";

        public static string GamePath(int number, char language) => Path.Combine(GameFolder, FileName(number, language));

        /// <summary>The tutorial number and language of a file name ("steam_tutorial_03_G.bin" -> (3, 'G')), or null.</summary>
        public static (int Number, char Language)? NameParts(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            const string Prefix = "steam_tutorial_";
            if (!name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || name.Length != Prefix.Length + 4 || name[Prefix.Length + 2] != '_')
                return null;
            if (!int.TryParse(name.AsSpan(Prefix.Length, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int number))
                return null;
            return (number, char.ToUpperInvariant(name[^1]));
        }

        public static TutorialFile Load(string path) => Parse(File.ReadAllBytes(path));

        /// <summary>A new tutorial to start from: a board setup, a greeting, a wait for the player, and End (back to the menu).</summary>
        public static TutorialFile CreateBlank(int scenario = 1)
        {
            var file = new TutorialFile();
            file.Texts.Add("Welcome to this tutorial!");
            file.Texts.Add("Press to continue.");
            file.Steps.Add(new TutorialStep { Op = TutorialOp.SetupScenario, P2 = (ushort)scenario });
            file.Steps.Add(new TutorialStep { Op = TutorialOp.Message, Text = 0 });
            file.Steps.Add(new TutorialStep { Op = TutorialOp.WaitForAction, P1 = 1, Text = 1 });
            file.Steps.Add(new TutorialStep { Op = TutorialOp.End });
            file.Renumber();
            return file;
        }

        public static TutorialFile Parse(byte[] data)
        {
            if (data.Length < HeaderSize)
                throw new InvalidDataException("Too short for a tutorial file.");
            int steps = BinaryPrimitives.ReadUInt16LittleEndian(data);
            int texts = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(2));
            long tableAt = HeaderSize + (long)StepSize * steps;
            long stringsAt = tableAt + 8L * texts;
            if (stringsAt > data.Length)
                throw new InvalidDataException($"The tables ({steps} steps, {texts} texts) run past the end of the file.");

            var file = new TutorialFile();
            for (int i = 0; i < steps; i++)
            {
                var s = data.AsSpan(HeaderSize + StepSize * i, StepSize);
                file.Steps.Add(new TutorialStep
                {
                    Op = (TutorialOp)s[0],
                    P1 = s[1],
                    P2 = BinaryPrimitives.ReadUInt16LittleEndian(s[2..]),
                    P3 = BinaryPrimitives.ReadUInt16LittleEndian(s[4..]),
                    P4 = BinaryPrimitives.ReadUInt16LittleEndian(s[6..]),
                    Id = BinaryPrimitives.ReadUInt16LittleEndian(s[8..]),
                    Text = BinaryPrimitives.ReadInt16LittleEndian(s[10..]),
                });
            }

            long at = stringsAt;
            for (int i = 0; i < texts; i++)
            {
                long length = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)(tableAt + 8 * i)));
                if (at + 2 * length + 2 > data.Length)
                    throw new InvalidDataException($"Text {i} runs past the end of the file.");
                file.Texts.Add(Encoding.Unicode.GetString(data, (int)at, (int)(2 * length)));
                at += 2 * length + 2;
            }
            return file;
        }

        public byte[] ToBytes()
        {
            if (Steps.Count > ushort.MaxValue || Texts.Count > ushort.MaxValue)
                throw new InvalidDataException("A tutorial holds at most 65535 steps and 65535 texts.");

            using var stream = new MemoryStream();
            var writer = new BinaryWriter(stream);
            writer.Write((ushort)Steps.Count);
            writer.Write((ushort)Texts.Count);
            writer.Write(0u);
            writer.Write(0u);
            foreach (var step in Steps)
            {
                writer.Write((byte)step.Op);
                writer.Write(step.P1);
                writer.Write(step.P2);
                writer.Write(step.P3);
                writer.Write(step.P4);
                writer.Write(step.Id);
                writer.Write(step.Text);
            }
            foreach (string text in Texts)
            {
                writer.Write((uint)text.Length);
                writer.Write(0u);
            }
            foreach (string text in Texts)
            {
                writer.Write(Encoding.Unicode.GetBytes(text));
                writer.Write((ushort)0);
            }
            return stream.ToArray();
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        /// <summary>The text a step shows, or null (no text, or an index past the table).</summary>
        public string? TextOf(TutorialStep step) => step.Text >= 0 && step.Text < Texts.Count ? Texts[step.Text] : null;

        /// <summary>Numbers the steps 0, 1, 2... as the game's files do (End keeps 0xFFFF).</summary>
        public void Renumber()
        {
            ushort id = 0;
            foreach (var step in Steps)
                step.Id = step.Op == TutorialOp.End ? (ushort)0xFFFF : id++;
        }

        /// <summary>Drops texts no step uses and renumbers the rest (keeps the order they're used in).</summary>
        public int RemoveUnusedTexts()
        {
            var map = new Dictionary<short, short>();
            var kept = new List<string>();
            foreach (var step in Steps)
            {
                if (step.Text < 0 || step.Text >= Texts.Count)
                    continue;
                if (!map.TryGetValue(step.Text, out short index))
                {
                    index = (short)kept.Count;
                    kept.Add(Texts[step.Text]);
                    map[step.Text] = index;
                }
                step.Text = index;
            }
            int removed = Texts.Count - kept.Count;
            Texts.Clear();
            Texts.AddRange(kept);
            return removed;
        }

        // ---- descriptions ----

        /// <summary>What WaitForAction's P1 waits for, from the input mask YGO::TUTORIAL::WaitForAction (0x1407F32F0) enables.</summary>
        public static string ActionName(int kind) => kind switch
        {
            1 => "press to continue (input 0x10)",
            2 => "pick a zone (P2 = zone bit)",
            3 => "select a card (input 0x1)",
            4 or 17 => "confirm / next (input 0x1000)",
            5 => "open the card menu (input 0x40)",
            7 => "P2: 1 = 0x200, 2 = 0x400, 3 = 0x20",
            8 or 9 or 14 => "choose in a list (input 0x1010)",
            10 => "input 0x8",
            11 => "input 0xC0",
            15 or 16 => "input 0x1008",
            18 => "input 0x4",
            20 => "input 0x100",
            21 => "input 0xD0",
            23 => "input 0x200",
            24 => "input 0x400",
            25 => "input 0x20",
            26 => "input 0x2",
            27 => "input 0x100C",
            _ => "nothing it can do (the duel plays on)",
        };

        public static string OpName(TutorialOp op) => Enum.IsDefined(op) ? op.ToString() : $"Op{(byte)op:X2} (does nothing)";

        /// <summary>A one-line reading of a step's parameters.</summary>
        public static string Describe(TutorialStep s) => s.Op switch
        {
            TutorialOp.SetupScenario => $"board setup {s.P2}",
            TutorialOp.Message => s.P3 != 0 ? "message (upper box)" : "message",
            TutorialOp.Hint => s.Text < 0 ? "hide the hint" : "hint",
            TutorialOp.SetTurnPlayer => $"turn: {(s.P2 == 0 ? "you" : "opponent")}",
            TutorialOp.Phase => $"phase {PhaseName(s.P2)}" + (s.P3 != 0 ? " (banner only)" : ""),
            TutorialOp.MoveCursor => s.P2 == 12 && s.P3 >= 0xF3C ? $"cursor: player {s.P1} hand card ${s.P3}" : $"cursor: player {s.P1} zone {s.P2} index {s.P3}",
            TutorialOp.Wait => $"wait {s.P2} frames ({s.P2 / 60.0:0.##} s)",
            TutorialOp.WaitForAction => $"wait for: {ActionName(s.P1)}" + (s.P2 != 0 ? $" [{s.P2}]" : ""),
            TutorialOp.Pointer => (IsScreenTarget(s.P2)
                ? s.P2 is >= 20 and <= 25 ? $"point at the {PhaseName(s.P2 - 20)} phase" : $"point at screen element {s.P2}"
                : $"point at player {s.P1} zone {s.P2}") + (s.P3 != 0 ? $" for {s.P3} frames" : ""),
            TutorialOp.PointerSimple => s.P1 == 0 ? "hide the pointer" : IsScreenTarget(s.P1) ? $"point at screen element {s.P1}" : $"point at your zone {s.P1}",
            TutorialOp.ActivateAt => $"activate player {s.P1} zone {s.P2}",
            TutorialOp.Label => $"label {s.P1}",
            TutorialOp.Goto => $"go to label {s.P2}",
            TutorialOp.AllowInput => s.P2 != 0 ? "input on" : "input off",
            TutorialOp.StackDeck => $"player {s.P2} deck[{s.P3}] = ${s.P4}",
            TutorialOp.AllowCards => "allow " + string.Join(", ", new[] { s.P2, s.P3, s.P4 }.Where(c => c != 0).Select(c => "$" + c)),
            TutorialOp.ShowCard => s.P2 switch { 0 => "card info: clear", 2 => "card info: the current card", _ => $"card info: ${s.P2}" },
            TutorialOp.End => s.P1 switch { 1 => "end: win the duel", 2 => "end: leave tutorial mode", _ => "end: back to the menu" },
            _ => $"{s.P1} {s.P2} {s.P3} {s.P4}",
        };

        // ---- what the exe says about the tutorials (fixed tables, not in the files) ----

        /// <summary>Help > Tutorial lists these, in this order (g_TutorialMenuList 0x140A780C0). 16 is in no menu.</summary>
        public static readonly int[] MenuList = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 17, 18, 19, 20];

        /// <summary>Campaign series 0-5 (DM, GX, 5D's, ZEXAL, ARC-V, VRAINS) start with tutorial 21-26 (g_CampaignSeriesTutorial 0x140A77E68).</summary>
        public static readonly int[] CampaignSeriesTutorial = [21, 22, 23, 24, 25, 26];

        /// <summary>The arena each tutorial is played in, by number 0-27 (g_TutorialArenaIds 0x140A57C20).</summary>
        public static readonly int[] ArenaIds = [1, 1, 1, 1, 1, 1, 6, 6, 7, 4, 7, 7, 7, 7, 8, 8, 8, 2, 3, 16, 17, 1, 13, 14, 15, 16, 17];

        /// <summary>The board setups YGO::TUTORIAL::SetupScenario (0x1407F4020) knows; any other number deals the plain decks.</summary>
        public static readonly int[] Scenarios = [1, 31, 32, 41, 42, 43, 44, 45, 46, 51, 52, 61, 62, 71, 72, 73, 91, 92, 101, 102, 103, 104, 105, 111, 112, 121, 122, 141, 142, 143, 161, 162, 171, 172, 173, 200, 210];

        /// <summary>Where the game starts a tutorial number from.</summary>
        public static string WhereUsed(int number)
        {
            if (MenuList.Contains(number))
                return $"Help > Tutorial, item {Array.IndexOf(MenuList, number) + 1}";
            int series = Array.IndexOf(CampaignSeriesTutorial, number);
            if (series >= 0)
                return $"first duel of campaign series {series} ({SeriesName(series)})";
            return "nowhere yet (needs a plugin to add it to the menu)";
        }

        public static string SeriesName(int series) => series switch
        {
            0 => "Duel Monsters", 1 => "GX", 2 => "5D's", 3 => "ZEXAL", 4 => "ARC-V", 5 => "VRAINS", _ => series.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>
        /// A best-guess name for a duel zone number as MoveCursor/Pointer use them (0-4 monsters, 5-9 spells/traps, 10 field, then the
        /// piles, from the engine's zone numbering; the cursor's own numbering isn't fully traced).
        /// </summary>
        public static string ZoneName(int zone) => zone switch
        {
            >= 0 and <= 4 => $"Monster {zone + 1}",
            >= 5 and <= 9 => $"Spell/Trap {zone - 4}",
            10 => "Field",
            11 => "Zone 11",
            12 => "Hand (P3 = card)",
            13 => "Hand",
            14 => "Extra Deck",
            15 => "Deck",
            16 => "Graveyard",
            17 => "Banished",
            _ => $"Zone {zone}",
        };

        /// <summary>
        /// Pointer targets that are parts of the screen, not zones: YGO::TUTORIAL::Pointer_Set (0x1407ED010) sends 16, 20-26, 30-35 and
        /// 40-53 to the screen's element positions (20-25 are the phase buttons, in phase order); anything else is (player, zone).
        /// </summary>
        public static bool IsScreenTarget(int target) => target is 16 or (>= 20 and <= 26) or (>= 30 and <= 35) or (>= 40 and <= 53);

        public static string PhaseName(int phase) => phase switch
        {
            0 => "Draw", 1 => "Standby", 2 => "Main 1", 3 => "Battle", 4 => "Main 2", 5 => "End", _ => phase.ToString(CultureInfo.InvariantCulture),
        };

        // ---- JSON form (what WolfEx saves in Yu-Gi-Oh-Ex\tutorials; a plugin turns it back into the game's form) ----

        public const string JsonFolder = "tutorials";

        public static string JsonName(int number, char language) => $"steam_tutorial_{number:00}_{char.ToUpperInvariant(language)}.json";

        /// <summary>
        /// The tutorial as JSON a person can read and edit:
        ///
        ///   { "tutorial": 5, "language": "E",
        ///     "steps": [ { "id": 3, "op": "Message", "text": "Hello there!\nThis is IN4-M8!" },
        ///                { "id": 5, "op": "ShowCard", "p2": 6310 }, ..., { "op": "End" } ] }
        ///
        /// Ops by name (unknown ones as "0x08"), parameters left out when 0, the text inline (each step with text gets its own entry in
        /// step order, as in the game's files, so it converts back byte for byte). "id" is left out when it's the next number (and on End).
        /// </summary>
        public string ToJson(int number, char language)
        {
            var steps = new System.Text.Json.Nodes.JsonArray();
            int nextId = 0;
            foreach (var step in Steps)
            {
                var node = new System.Text.Json.Nodes.JsonObject();
                bool idImplied = step.Op == TutorialOp.End ? step.Id == 0xFFFF : step.Id == nextId;
                if (!idImplied)
                    node["id"] = step.Id;
                if (step.Id != 0xFFFF)
                    nextId = step.Id + 1;
                node["op"] = OpToken(step.Op);
                if (step.P1 != 0) node["p1"] = step.P1;
                if (step.P2 != 0) node["p2"] = step.P2;
                if (step.P3 != 0) node["p3"] = step.P3;
                if (step.P4 != 0) node["p4"] = step.P4;
                if (TextOf(step) is string text)
                    node["text"] = text;
                else if (step.Text >= 0)
                    node["textIndex"] = step.Text;   // points past the table (not in the game's files)
                steps.Add(node);
            }
            var root = new System.Text.Json.Nodes.JsonObject
            {
                ["tutorial"] = number,
                ["language"] = char.ToUpperInvariant(language).ToString(),
                ["steps"] = steps,
            };
            return root.ToJsonString(JsonOptions);
        }

        private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        /// <summary>Reads <see cref="ToJson"/>'s form back. Problems say which step.</summary>
        public static TutorialFile FromJson(string json, out int number, out char language, List<string>? problems = null)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json, documentOptions: new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true,
            }) as System.Text.Json.Nodes.JsonObject ?? throw new InvalidDataException("A tutorial JSON is an object with \"steps\".");
            number = root["tutorial"]?.GetValue<int>() ?? 0;
            string? letter = root["language"]?.GetValue<string>();
            language = string.IsNullOrEmpty(letter) ? 'E' : char.ToUpperInvariant(letter[0]);

            var file = new TutorialFile();
            int nextId = 0, index = 0;
            foreach (var item in root["steps"] as System.Text.Json.Nodes.JsonArray ?? [])
            {
                index++;
                if (item is not System.Text.Json.Nodes.JsonObject node)
                {
                    problems?.Add($"Step {index} isn't an object.");
                    continue;
                }
                string opName = node["op"]?.GetValue<string>() ?? "";
                if (!TryOp(opName, out var op))
                {
                    problems?.Add($"Step {index}: unknown op \"{opName}\".");
                    continue;
                }
                int P(string key) => node[key]?.GetValue<int>() ?? 0;
                var step = new TutorialStep
                {
                    Op = op, P1 = (byte)P("p1"), P2 = (ushort)P("p2"), P3 = (ushort)P("p3"), P4 = (ushort)P("p4"),
                    Id = (ushort)(node["id"]?.GetValue<int>() ?? (op == TutorialOp.End ? 0xFFFF : nextId)),
                };
                if (step.Id != 0xFFFF)
                    nextId = (step.Id + 1) & 0xFFFF;
                if (node["text"]?.GetValue<string>() is string text)
                {
                    step.Text = (short)file.Texts.Count;
                    file.Texts.Add(text);
                }
                else if (node["textIndex"] != null)
                    step.Text = (short)node["textIndex"]!.GetValue<int>();
                file.Steps.Add(step);
            }
            if (file.Steps.Count == 0 || file.Steps[^1].Op != TutorialOp.End)
                problems?.Add("The last step should be End (the game stops reading there).");
            return file;
        }

        // ---- script form ----

        /// <summary>
        /// The whole tutorial as text, one step a line: "Op p1 p2 p3 p4" then, for a step with text, " | " and the text (a new line as
        /// \n), with the step's id in front ("12:", "-:" for 0xFFFF; optional). Ops can be names or numbers (0x..). Lines starting with #
        /// are comments. Each step with text gets its own entry, in step order, as in the game's files (they round-trip exactly).
        /// </summary>
        public string ToScript()
        {
            var text = new StringBuilder();
            text.Append("# Tutorial script: [id:] Op p1 p2 p3 p4 [| text]. \\n = new line. # = comment. A step without an id gets the one after the previous step's.\n");
            foreach (var step in Steps)
            {
                text.Append(step.Id == 0xFFFF ? "-" : step.Id.ToString(CultureInfo.InvariantCulture)).Append(": ").Append(OpToken(step.Op)).Append(' ').Append(step.P1).Append(' ').Append(step.P2).Append(' ').Append(step.P3).Append(' ').Append(step.P4);
                string? body = TextOf(step);
                if (body != null)
                    text.Append(" | ").Append(body.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"));
                else if (step.Text >= 0)
                    text.Append(" | #missing text ").Append(step.Text);
                text.Append("   # ").Append(Describe(step)).Append('\n');
            }
            return text.ToString();
        }

        private static string OpToken(TutorialOp op) => Enum.IsDefined(op) ? op.ToString() : $"0x{(byte)op:X2}";

        public static TutorialFile ParseScript(string script, List<string>? problems = null)
        {
            var file = new TutorialFile();
            int lineNumber = 0, nextId = 0;
            foreach (string raw in script.Replace("\r\n", "\n").Split('\n'))
            {
                lineNumber++;
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                string? body = null;
                int bar = line.IndexOf(" | ", StringComparison.Ordinal);
                if (bar >= 0)
                {
                    body = line[(bar + 3)..];
                    line = line[..bar];
                    body = Unescape(StripComment(body));
                }
                else
                    line = StripComment(line);

                // "12:" = the step's id, "-:" = 0xFFFF (End); none = the one after the previous step's
                int? id = null;
                int colon = line.IndexOf(':');
                if (colon > 0 && line[..colon].Trim() is string idToken && (idToken == "-" || idToken.All(char.IsDigit)))
                {
                    if (idToken == "-")
                        id = 0xFFFF;
                    else if (!int.TryParse(idToken, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) || parsed > 0xFFFF)
                    {
                        problems?.Add($"Line {lineNumber}: the id must be 0-65535.");
                        continue;
                    }
                    else
                        id = parsed;
                    line = line[(colon + 1)..];
                }

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0)
                    continue;
                if (!TryOp(parts[0], out var op))
                {
                    problems?.Add($"Line {lineNumber}: unknown op \"{parts[0]}\".");
                    continue;
                }
                var numbers = new int[4];
                bool ok = true;
                for (int i = 1; i < parts.Length && i <= 4; i++)
                    if (!TryNumber(parts[i], out numbers[i - 1]))
                    {
                        problems?.Add($"Line {lineNumber}: \"{parts[i]}\" isn't a number.");
                        ok = false;
                    }
                if (parts.Length > 5)
                    problems?.Add($"Line {lineNumber}: more than four numbers (put text after \" | \").");
                if (!ok)
                    continue;
                if (numbers[0] is < 0 or > 255 || numbers[1..].Any(n => n is < 0 or > 0xFFFF))
                {
                    problems?.Add($"Line {lineNumber}: p1 is 0-255, p2-p4 are 0-65535.");
                    continue;
                }

                var step = new TutorialStep
                {
                    Op = op, P1 = (byte)numbers[0], P2 = (ushort)numbers[1], P3 = (ushort)numbers[2], P4 = (ushort)numbers[3],
                    Id = (ushort)(id ?? (op == TutorialOp.End ? 0xFFFF : nextId)),
                };
                if (step.Id != 0xFFFF)
                    nextId = (step.Id + 1) & 0xFFFF;
                if (body != null)
                {
                    step.Text = (short)file.Texts.Count;
                    file.Texts.Add(body);
                }
                file.Steps.Add(step);
            }
            if (file.Steps.Count == 0 || file.Steps[^1].Op != TutorialOp.End)
                problems?.Add("The last step should be End (the game stops reading there).");
            return file;
        }

        /// <summary>"   # comment" after the step (a # inside the text needs a space before it to count as a comment).</summary>
        private static string StripComment(string text)
        {
            int hash = text.IndexOf("   #", StringComparison.Ordinal);
            return (hash >= 0 ? text[..hash] : text).TrimEnd();
        }

        private static string Unescape(string text)
        {
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\\' && i + 1 < text.Length)
                {
                    char next = text[++i];
                    result.Append(next switch { 'n' => '\n', _ => next });
                    continue;
                }
                result.Append(text[i]);
            }
            return result.ToString();
        }

        private static bool TryOp(string token, out TutorialOp op)
        {
            if (Enum.TryParse(token, true, out op) && !char.IsDigit(token[0]))
                return true;
            if (TryNumber(token, out int value) && value is >= 0 and <= 255)
            {
                op = (TutorialOp)value;
                return true;
            }
            return false;
        }

        private static bool TryNumber(string token, out int value) =>
            token.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? int.TryParse(token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
                : int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
