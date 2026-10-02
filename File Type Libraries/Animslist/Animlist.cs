using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Types
{
    /// <summary>
    /// An animlist: the picture layers of a title screen animation (title\anims\&lt;X&gt;\animlist.combined.txt, else animlist.cropped.txt) or of a
    /// duel arena (arenas\&lt;X&gt;\animlist.txt). Read by YGO::ANIM::Animlist_Load (0x140745D30):
    ///
    ///   side, 0                            which side the title menu goes: 0 = menu on the left (x 568), anything else = on the right (x 1367)
    ///   Utopia39_BG, 0, 0, 0               layer: picture name (no extension; the folder's .png or .jpg), x, y, slide
    ///   Utopia39_Character.cropped, 770, -10, 30
    ///
    /// Up to 10 layers, first = furthest back. Lines starting with # or ; are comments. "slide" is a vertical bob in pixels, the same on the
    /// title screen and in arenas (YGO::ANIM::Animlist_UpdateSlide, 0x140746510): each frame the phase advances by the frame time (wrapped at
    /// 2 pi) and the layer is drawn at y + slide * cos(phase) - one cycle every ~6.3 s, all layers in step. The game's files use 30 on the
    /// characters and 0 on everything else (0 = still). "side" only matters on the title screen.
    /// </summary>
    public sealed class Animlist
    {
        public const int MaxLayers = 10;

        public int Side { get; set; }
        public List<AnimlistLayer> Layers { get; } = [];

        public static Animlist Parse(string text)
        {
            var list = new Animlist();
            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw;
                int comment = line.IndexOfAny(['#', ';']);
                if (comment >= 0)
                    line = line[..comment];
                string[] parts = line.Split(',').Select(p => p.Trim()).ToArray();
                if (parts.Length == 0 || parts[0].Length == 0)
                    continue;
                if (string.Equals(parts[0], "side", StringComparison.OrdinalIgnoreCase))
                {
                    if (parts.Length > 1 && int.TryParse(parts[1], out int side))
                        list.Side = side;
                    continue;
                }
                var layer = new AnimlistLayer { Name = parts[0] };
                if (parts.Length > 1) layer.X = Int(parts[1]);
                if (parts.Length > 2) layer.Y = Int(parts[2]);
                if (parts.Length > 3) layer.Slide = Int(parts[3]);
                list.Layers.Add(layer);
            }
            return list;
        }

        private static int Int(string text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;

        public string ToText()
        {
            var text = new StringBuilder();
            text.Append("side, ").Append(Side.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
            foreach (var layer in Layers)
                text.Append($"{layer.Name}, {layer.X}, {layer.Y}, {layer.Slide}\r\n");
            return text.ToString();
        }

        public Animlist Clone() => Parse(ToText());

        // ---- JSON form (what WolfEx saves in Yu-Gi-Oh-Ex\animlists\<folder>\<file>.json, the pictures next to it) ----

        public const string JsonFolder = "animlists";

        /// <summary>{ "side": 0, "layers": [ { "name": "Utopia39_BG", "x": 0, "y": 0, "slide": 0 }, ... ] } (first = furthest back).</summary>
        public string ToJson()
        {
            var layers = new System.Text.Json.Nodes.JsonArray();
            foreach (var layer in Layers)
                layers.Add(new System.Text.Json.Nodes.JsonObject { ["name"] = layer.Name, ["x"] = layer.X, ["y"] = layer.Y, ["slide"] = layer.Slide });
            return new System.Text.Json.Nodes.JsonObject { ["side"] = Side, ["layers"] = layers }
                .ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        public static Animlist FromJson(string json)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(json, documentOptions: new System.Text.Json.JsonDocumentOptions
            {
                CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true,
            }) as System.Text.Json.Nodes.JsonObject ?? throw new InvalidDataException("An animlist JSON is an object with \"layers\".");
            var list = new Animlist { Side = root["side"]?.GetValue<int>() ?? 0 };
            foreach (var item in root["layers"] as System.Text.Json.Nodes.JsonArray ?? [])
                if (item is System.Text.Json.Nodes.JsonObject layer)
                    list.Layers.Add(new AnimlistLayer
                    {
                        Name = layer["name"]?.GetValue<string>() ?? "",
                        X = layer["x"]?.GetValue<int>() ?? 0,
                        Y = layer["y"]?.GetValue<int>() ?? 0,
                        Slide = layer["slide"]?.GetValue<int>() ?? 0,
                    });
            return list;
        }

        /// <summary>Reads an animlist file.</summary>
        public static Animlist Load(string path) => Parse(File.ReadAllText(path));

        public void Save(string path) => File.WriteAllText(path, ToText());
    }

    public sealed class AnimlistLayer
    {
        [Description("The picture: its file name in the animation's folder, without .png / .jpg.")]
        public string Name { get; set; } = "";

        [Description("Left edge on the 1920 x 1080 screen.")]
        public int X { get; set; }

        [Description("Top edge on the 1920 x 1080 screen.")]
        public int Y { get; set; }

        [Description("Vertical bob in pixels: the layer moves between y - slide and y + slide (y + slide x cos(t), one cycle every ~6.3 s). 0 = still. The game uses 30 on characters. Press Play slides to see it.")]
        public int Slide { get; set; }

        public override string ToString() => $"{Name}  ({X}, {Y})";
    }
}
