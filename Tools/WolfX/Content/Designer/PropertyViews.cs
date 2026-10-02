using System.ComponentModel;
using System.Drawing.Design;
using System.Text.Json.Nodes;
using System.Windows.Forms.Design;

namespace WolfEx.Designer
{
    /// <summary>
    /// What the property grid shows for one element: only the fields its kind uses (a CustomTypeDescriptor filters the rest out).
    /// Values write straight through to the <see cref="PageElement"/>.
    /// </summary>
    internal sealed class ElementView : CustomTypeDescriptor
    {
        public ElementView(PageElement element) => Element = element;

        [Browsable(false)]
        public PageElement Element { get; }

        private WidgetKind? Kind => WidgetCatalog.Find(Element.Kind);

        [Category("1 Element"), Description("A name for the element, unique on the page. Plugins find the element by it.")]
        public string Id { get => Element.Id; set => Element.Id = value.Trim(); }

        [Category("1 Element"), Description("What it is, and whether RIX can build it in the game.")]
        public string Widget => Kind is { } k ? $"{k.Name} ({k.SupportText})" : Element.Kind;

        [Category("1 Element"), Description("The game's class (RTTI name in YuGiOh.exe).")]
        public string GameClass => Kind?.GameClass ?? "";

        [Category("1 Element"), Description("Cannot be dragged or resized while locked.")]
        public bool Locked { get => Element.Locked == true; set => Element.Locked = value ? true : null; }

        [Category("2 Position (game pixels, 1920 x 1080)"), Description("Left edge.")]
        public float X { get => Element.X; set => Element.X = value; }

        [Category("2 Position (game pixels, 1920 x 1080)"), Description("Top edge.")]
        public float Y { get => Element.Y; set => Element.Y = value; }

        [Category("2 Position (game pixels, 1920 x 1080)")]
        public float Width { get => Element.Width; set => Element.Width = Math.Max(1, value); }

        [Category("2 Position (game pixels, 1920 x 1080)")]
        public float Height { get => Element.Height; set => Element.Height = Math.Max(1, value); }

        [Category("2 Position (game pixels, 1920 x 1080)"), Description("The centre, which is what the game uses for its menus (ButtonsX) and most widgets.")]
        public string Centre => $"{Element.X + Element.Width / 2:0.##}, {Element.Y + Element.Height / 2:0.##}";

        [Category("2 Position (game pixels, 1920 x 1080)"), Description("Draw order: higher is in front.")]
        public int Z { get => Element.Z; set => Element.Z = value; }

        [Category("3 Look")]
        public string Text { get => Element.Text ?? ""; set => Element.Text = value; }

        [Category("3 Look"), Description("Text height in game pixels. Empty = the widget's usual size.")]
        public float? TextSize { get => Element.TextSize; set => Element.TextSize = value is > 0 ? value : null; }

        [Category("3 Look"), Description("Where the text sits in the box.")]
        [TypeConverter(typeof(AlignConverter))]
        public string Align { get => Element.Align ?? "left"; set => Element.Align = value == "left" ? null : value; }

        [Category("3 Look"), Description("Text colour: #RRGGBB or #AARRGGBB. Empty = white.")]
        public string Colour { get => Element.Colour ?? ""; set => Element.Colour = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }

        [Category("3 Look"), Description("Show the highlighted (selected) look.")]
        public bool Highlighted { get => Element.Highlighted == true; set => Element.Highlighted = value ? true : null; }

        [Category("3 Look"), Description("The sprite: sheet and name. Click ... to browse every sheet in the game's archive.")]
        [Editor(typeof(SpriteEditor), typeof(UITypeEditor))]
        public string Sprite
        {
            get => Element.Resource != null ? $"{Element.Resource}#{Element.Sprite}" : "";
            set
            {
                int hash = value.IndexOf('#');
                Element.Resource = hash > 0 ? value[..hash].Trim() : null;
                Element.Sprite = hash > 0 ? value[(hash + 1)..].Trim() : null;
            }
        }

        [Category("4 Buttons"), Description("The page's buttons, top to bottom (up to 4 in the game). Each has a label, a description and an action.")]
        public List<PageButton> Buttons
        {
            get => Element.Buttons ??= [];
            set => Element.Buttons = value;
        }

        [Category("4 Buttons"), Description("What pressing it does, in the menu files' syntax: {\"goto\": \"credits\"}, {\"call\": \"mymod.thing\"}, {\"page\": \"other\"}, {\"quit\": true}.")]
        public string Action
        {
            get => Element.Action?.ToJsonString() ?? "";
            set => Element.Action = string.IsNullOrWhiteSpace(value) ? null : JsonNode.Parse(value);
        }

        private bool Shows(string name) => name switch
        {
            nameof(Text) or nameof(TextSize) => Kind?.HasText == true,
            nameof(Highlighted) => Kind?.HasHighlight == true,
            nameof(Align) or nameof(Colour) => Kind?.HasTextStyle == true,
            nameof(Sprite) => Kind?.HasSprite == true,
            nameof(Buttons) => Kind?.HasButtons == true,
            nameof(Action) => Kind?.HasAction == true,
            _ => true,
        };

        public override PropertyDescriptorCollection GetProperties() => GetProperties(null);

        public override PropertyDescriptorCollection GetProperties(Attribute[]? attributes)
        {
            var all = TypeDescriptor.GetProperties(typeof(ElementView), attributes);
            return new PropertyDescriptorCollection(all.Cast<PropertyDescriptor>().Where(p => p.IsBrowsable && Shows(p.Name)).ToArray());
        }

        public override object? GetPropertyOwner(PropertyDescriptor? pd) => this;

        public override string? GetClassName() => Element.Kind;

        public override string? GetComponentName() => Element.Id;
    }

    /// <summary>The page itself (shown when nothing is selected).</summary>
    internal sealed class PageView
    {
        private readonly PageDocument _document;

        public PageView(PageDocument document) => _document = document;

        [Category("Page"), Description("The file name in Yu-Gi-Oh-Ex\\pages (without .json), and what actions use to open it: {\"page\": \"name\"}.")]
        public string Name { get => _document.Name; set => _document.Name = value.Trim(); }

        [Category("Page"), Description("The title the game shows at the top (RIX_PageDesc::Header).")]
        public string Header { get => _document.Header; set => _document.Header = value; }

        [Category("Page"), Description("Designer only: the picture behind the page. A .jpg path in the archive, or sheet#sprite. The game shows its Battle Pack screen.")]
        [TypeConverter(typeof(BackgroundConverter))]
        public string Background { get => _document.Background; set => _document.Background = value; }

        [Category("Page"), Description("How many elements the game will build from this file / how many are preview only.")]
        public string InGame
        {
            get
            {
                int yes = _document.Elements.Count(e => WidgetCatalog.Find(e.Kind)?.Support == Designer.InGame.Yes);
                return $"{yes} shown in game, {_document.Elements.Count - yes} not shown (designer only for now)";
            }
        }
    }

    internal sealed class AlignConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new(new[] { "left", "center", "right" });
    }

    /// <summary>Offers the archive's backgrounds in a drop-down (the list is filled by the designer panel once the game folder is known).</summary>
    internal sealed class BackgroundConverter : StringConverter
    {
        public static List<string> Choices { get; } = [];

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context) => true;

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context) => false;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context) => new(Choices);
    }

    /// <summary>The ... button on Sprite: opens <see cref="SpritePickerDialog"/>.</summary>
    internal sealed class SpriteEditor : UITypeEditor
    {
        public static GameArt Art { get; set; } = GameArt.None;

        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext? context) => UITypeEditorEditStyle.Modal;

        public override object? EditValue(ITypeDescriptorContext? context, IServiceProvider provider, object? value)
        {
            string current = value as string ?? "";
            int hash = current.IndexOf('#');
            using var dialog = new SpritePickerDialog(Art, hash > 0 ? current[..hash] : null, hash > 0 ? current[(hash + 1)..] : null);
            var service = provider.GetService(typeof(IWindowsFormsEditorService)) as IWindowsFormsEditorService;
            var result = service != null ? service.ShowDialog(dialog) : dialog.ShowDialog();
            return result == DialogResult.OK && dialog.Resource != null ? $"{dialog.Resource}#{dialog.Sprite}" : value;
        }
    }
}
