using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WolfEx.Designer
{
    /// <summary>
    /// One page made in the designer: Yu-Gi-Oh-Ex\pages\&lt;name&gt;.json. Positions are the game's screen pixels (1920 x 1080, top left is 0, 0,
    /// y goes down), the same numbers RIX and the game's widgets use (RIX_PageDesc::ButtonsX / ButtonsY, CreateFromLayout x / y).
    /// See docs/PageDesigner.md.
    /// </summary>
    internal sealed class PageDocument
    {
        public const int ScreenWidth = 1920;
        public const int ScreenHeight = 1080;
        public const int CurrentVersion = 1;

        public int Version { get; set; } = CurrentVersion;
        public string Name { get; set; } = "";
        public string Header { get; set; } = "";

        /// <summary>A picture from the game's archive shown behind the page in the designer only (the game shows its Battle Pack screen).</summary>
        public string Background { get; set; } = "pdui\\bg_vrains.jpg";

        public List<PageElement> Elements { get; set; } = [];

        private static readonly JsonSerializerOptions Options = new()
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static PageDocument FromJson(string json)
        {
            var document = JsonSerializer.Deserialize<PageDocument>(json, Options) ?? new PageDocument();
            document.Elements ??= [];
            return document;
        }

        public PageDocument Clone() => FromJson(ToJson());

        public string NewId(string kind)
        {
            for (int n = 1; ; n++)
            {
                string id = $"{kind}{n}";
                if (!Elements.Any(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase)))
                    return id;
            }
        }
    }

    /// <summary>One thing on a page. Kind is a <see cref="WidgetKind.Id"/>; the other fields are used by the kinds that need them.</summary>
    internal sealed class PageElement
    {
        public string Id { get; set; } = "";
        public string Kind { get; set; } = "";
        public float X { get; set; }
        public float Y { get; set; }
        public float Width { get; set; }
        public float Height { get; set; }

        /// <summary>Draw order: higher is in front. The game's widgets take the same number when they are created.</summary>
        public int Z { get; set; }

        public string? Text { get; set; }
        public float? TextSize { get; set; }

        /// <summary>Text elements: "left" (default), "center" or "right" inside the box.</summary>
        public string? Align { get; set; }

        /// <summary>Text elements: "#RRGGBB" or "#AARRGGBB" (default white).</summary>
        public string? Colour { get; set; }

        /// <summary>Image elements: a sprite sheet ("pdui/doShared") and the sprite in it ("menuheader").</summary>
        public string? Resource { get; set; }
        public string? Sprite { get; set; }

        /// <summary>Show the widget's highlighted look (menu buttons, digits...).</summary>
        public bool? Highlighted { get; set; }

        /// <summary>Button lists: the buttons, top to bottom (RIX pages take up to 4).</summary>
        public List<PageButton>? Buttons { get; set; }

        /// <summary>What pressing it does, in the menu files' action syntax: {"goto": "credits"}, {"call": "mymod.thing"}, {"page": "other"}...</summary>
        public JsonNode? Action { get; set; }

        /// <summary>Locked elements cannot be dragged in the designer.</summary>
        public bool? Locked { get; set; }

        [JsonIgnore]
        public RectangleF Bounds
        {
            get => new(X, Y, Width, Height);
            set { X = value.X; Y = value.Y; Width = value.Width; Height = value.Height; }
        }
    }

    internal sealed class PageButton
    {
        [Description("The text on the button.")]
        public string Label { get; set; } = "Button";

        [Description("Shown under the menu while the button is highlighted.")]
        public string? Description { get; set; }

        [Description("What it does, in the menu files' syntax, e.g. {\"goto\": \"credits\"} or {\"page\": \"myOtherPage\"}. Empty = nothing.")]
        [JsonIgnore]
        public string ActionText
        {
            get => Action?.ToJsonString() ?? "";
            set => Action = string.IsNullOrWhiteSpace(value) ? null : JsonNode.Parse(value);
        }

        [Browsable(false)]
        public JsonNode? Action { get; set; }

        public override string ToString() => Label;
    }
}
