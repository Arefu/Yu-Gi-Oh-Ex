using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Text;

namespace Types
{
    /// <summary>
    /// A .dfymoo sprite list (tk2d): the rectangles of one .png sheet, by name. The game asks for a sheet by resource name ("pdui/doShared")
    /// and for a sprite in it by name (DFX::TLayerAnimoo::SelectByName).
    ///
    ///   i tk2d 1          header: format, sheet width and height
    ///   w 1890
    ///   h 848
    ///   ~
    ///   n arrow_1         name
    ///   s 1812 444 73 60  x y width height on the sheet
    ///   o 7 7 85 74       optional: the piece sits at 7, 7 inside an 85 x 74 picture (the sheet stores it with the empty border trimmed)
    ///   ~
    /// </summary>
    public sealed class DfymooSheet
    {
        public List<string> Header { get; } = ["i tk2d 1"];
        public int Width { get; set; }
        public int Height { get; set; }
        public List<DfymooSprite> Sprites { get; } = [];

        public static DfymooSheet Parse(string text)
        {
            var sheet = new DfymooSheet();
            sheet.Header.Clear();
            string[] blocks = text.Replace("\r\n", "\n").Split('~');
            for (int b = 0; b < blocks.Length; b++)
            {
                var lines = blocks[b].Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (b == 0 && !lines.Any(l => l.StartsWith("n ")))
                {
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("w ") && int.TryParse(line[2..], out int w)) sheet.Width = w;
                        else if (line.StartsWith("h ") && int.TryParse(line[2..], out int h)) sheet.Height = h;
                        else sheet.Header.Add(line);
                    }
                    continue;
                }

                var sprite = new DfymooSprite();
                bool named = false;
                foreach (string line in lines)
                {
                    if (line.StartsWith("n "))
                    {
                        sprite.Name = line[2..];
                        named = true;
                    }
                    else if (line.StartsWith("s ") && Numbers(line, out var s))
                    {
                        sprite.X = s[0]; sprite.Y = s[1]; sprite.Width = s[2]; sprite.Height = s[3];
                    }
                    else if (line.StartsWith("o ") && Numbers(line, out var o))
                    {
                        sprite.Trimmed = true;
                        sprite.OffsetX = o[0]; sprite.OffsetY = o[1]; sprite.FullWidth = o[2]; sprite.FullHeight = o[3];
                    }
                    else
                        sprite.Extra.Add(line);
                }
                if (named)
                    sheet.Sprites.Add(sprite);
            }
            if (sheet.Header.Count == 0)
                sheet.Header.Add("i tk2d 1");
            return sheet;
        }

        public string ToText()
        {
            var text = new StringBuilder();
            foreach (string line in Header)
                text.Append(line).Append("\r\n");
            text.Append("w ").Append(Width.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            text.Append("h ").Append(Height.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            foreach (var sprite in Sprites)
            {
                text.Append("~\r\n");
                text.Append("n ").Append(sprite.Name).Append("\r\n");
                text.Append($"s {sprite.X} {sprite.Y} {sprite.Width} {sprite.Height}\r\n");
                if (sprite.Trimmed)
                    text.Append($"o {sprite.OffsetX} {sprite.OffsetY} {sprite.FullWidth} {sprite.FullHeight}\r\n");
                foreach (string line in sprite.Extra)
                    text.Append(line).Append("\r\n");
            }
            text.Append('~');
            return text.ToString();
        }

        public DfymooSheet Clone() => Parse(ToText());

        // ---- JSON form (what WolfEx saves in Yu-Gi-Oh-Ex\sprites\<resource>.json, the .png next to it) ----

        public const string JsonFolder = "sprites";

        /// <summary>
        ///   { "width": 1890, "height": 848,
        ///     "sprites": [ { "name": "arrow_1", "x": 1812, "y": 444, "width": 73, "height": 60,
        ///                    "trim": { "x": 7, "y": 7, "width": 85, "height": 74 } } ] }
        /// "header" is only written when it isn't the usual ["i tk2d 1"], "extra" only for lines the format doesn't name. Converts back
        /// to the same .dfymoo text.
        /// </summary>
        public string ToJson()
        {
            var root = new System.Text.Json.Nodes.JsonObject();
            if (!(Header.Count == 1 && Header[0] == "i tk2d 1"))
                root["header"] = new System.Text.Json.Nodes.JsonArray([.. Header.Select(h => (System.Text.Json.Nodes.JsonNode?)h)]);
            root["width"] = Width;
            root["height"] = Height;
            var sprites = new System.Text.Json.Nodes.JsonArray();
            foreach (var s in Sprites)
            {
                var node = new System.Text.Json.Nodes.JsonObject { ["name"] = s.Name, ["x"] = s.X, ["y"] = s.Y, ["width"] = s.Width, ["height"] = s.Height };
                if (s.Trimmed)
                    node["trim"] = new System.Text.Json.Nodes.JsonObject { ["x"] = s.OffsetX, ["y"] = s.OffsetY, ["width"] = s.FullWidth, ["height"] = s.FullHeight };
                if (s.Extra.Count > 0)
                    node["extra"] = new System.Text.Json.Nodes.JsonArray([.. s.Extra.Select(e => (System.Text.Json.Nodes.JsonNode?)e)]);
                sprites.Add(node);
            }
            root["sprites"] = sprites;
            return root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        public static DfymooSheet FromJson(string json)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json, documentOptions: new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true,
            }) as System.Text.Json.Nodes.JsonObject ?? throw new InvalidDataException("A sprite sheet JSON is an object with \"sprites\".");
            var sheet = new DfymooSheet { Width = root["width"]?.GetValue<int>() ?? 0, Height = root["height"]?.GetValue<int>() ?? 0 };
            if (root["header"] is System.Text.Json.Nodes.JsonArray header)
            {
                sheet.Header.Clear();
                sheet.Header.AddRange(header.Select(h => h?.GetValue<string>() ?? "").Where(h => h.Length > 0));
            }
            foreach (var item in root["sprites"] as System.Text.Json.Nodes.JsonArray ?? [])
            {
                if (item is not System.Text.Json.Nodes.JsonObject node)
                    continue;
                int I(System.Text.Json.Nodes.JsonNode? n, string key) => n?[key]?.GetValue<int>() ?? 0;
                var sprite = new DfymooSprite
                {
                    Name = node["name"]?.GetValue<string>() ?? "",
                    X = I(node, "x"), Y = I(node, "y"), Width = I(node, "width"), Height = I(node, "height"),
                };
                if (node["trim"] is System.Text.Json.Nodes.JsonObject trim)
                {
                    sprite.Trimmed = true;
                    sprite.OffsetX = I(trim, "x"); sprite.OffsetY = I(trim, "y"); sprite.FullWidth = I(trim, "width"); sprite.FullHeight = I(trim, "height");
                }
                if (node["extra"] is System.Text.Json.Nodes.JsonArray extra)
                    sprite.Extra.AddRange(extra.Select(e => e?.GetValue<string>() ?? ""));
                sheet.Sprites.Add(sprite);
            }
            return sheet;
        }

        /// <summary>Reads a .dfymoo file.</summary>
        public static DfymooSheet Load(string path) => Parse(File.ReadAllText(path));

        /// <summary>Writes the .dfymoo file (its .png is not touched).</summary>
        public void Save(string path) => File.WriteAllText(path, ToText());

        public string NewName(string stem)
        {
            for (int n = 1; ; n++)
            {
                string name = $"{stem}{n}";
                if (!Sprites.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
                    return name;
            }
        }

        private static bool Numbers(string line, out int[] values)
        {
            values = new int[4];
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5)
                return false;
            for (int i = 0; i < 4; i++)
            {
                if (!int.TryParse(parts[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
                    return false;
            }
            return true;
        }
    }

    public sealed class DfymooSprite
    {
        [Category("Sprite"), Description("The name the game asks for.")]
        public string Name { get; set; } = "";

        [Category("On the sheet"), Description("Left edge on the sheet, in pixels.")]
        public int X { get; set; }

        [Category("On the sheet"), Description("Top edge on the sheet, in pixels.")]
        public int Y { get; set; }

        [Category("On the sheet")]
        public int Width { get; set; }

        [Category("On the sheet")]
        public int Height { get; set; }

        [Category("Trimmed border"), Description("The sheet stores the sprite without its empty border; the game puts it back (the 'o' line).")]
        public bool Trimmed { get; set; }

        [Category("Trimmed border"), Description("Where the stored piece sits inside the full picture.")]
        public int OffsetX { get; set; }

        [Category("Trimmed border")]
        public int OffsetY { get; set; }

        [Category("Trimmed border"), Description("The full picture's size, border included.")]
        public int FullWidth { get; set; }

        [Category("Trimmed border")]
        public int FullHeight { get; set; }

        [Browsable(false)]
        public List<string> Extra { get; } = [];

        [Browsable(false)]
        public Rectangle Bounds
        {
            get => new(X, Y, Width, Height);
            set { X = value.X; Y = value.Y; Width = value.Width; Height = value.Height; }
        }

        public override string ToString() => Name;
    }
}
