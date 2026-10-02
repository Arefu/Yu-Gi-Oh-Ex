using Wolf.Editors;
using WolfX;

namespace WolfEx
{
    /// <summary>
    /// The New cards page laid out like the Card Manager: the cards on the left (find, Id / Name / Kind), the picked card's preview and
    /// summary on the right (<see cref="CardSummary"/>) above its tabs (Properties, Text, Art).
    /// </summary>
    internal sealed partial class CardsPanel
    {
        // ---- the list ----
        private readonly ToolStrip _listTools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripTextBox _find = new() { Width = 180, ToolTipText = "Id or part of a name" };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false, MultiSelect = false,
        };
        private List<CardModel> _rows = [];

        // ---- the card ----
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly CardSummary _summary = new();
        private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private readonly NumericUpDown _id = new() { Minimum = MinId, Maximum = MaxId, Value = MinId, Width = 90 };
        // The lists read as on the Card Manager ("13: Spell", "1: Dragon"); the item is the name cards.json uses.
        private readonly ComboBox _kind = Combo(220, Kinds, CardNames.KindName), _type = Combo(150, Types, CardNames.RaceName),
            _attribute = Combo(120, Attributes, CardNames.AttributeName), _icon = Combo(120, Icons, i => CardNames.Icons[Math.Clamp(i, 0, CardNames.Icons.Length - 1)]),
            _limitation = Combo(120, Limitations, null);
        private readonly NumericUpDown _scale = new() { Maximum = 13, Width = 80 };
        private readonly LinkArrowPicker _arrows = new();
        private readonly NumericUpDown _level = new() { Minimum = 1, Maximum = 12, Value = 4, Width = 80 },
            _atk = new() { Maximum = 9990, Increment = 100, Width = 80 }, _def = new() { Maximum = 9990, Increment = 100, Width = 80 },
            _copies = new() { Maximum = 3, Value = 3, Width = 80 };
        private readonly Label _levelLabel = new() { Text = "Level:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
        private readonly Label _archetypes = new() { AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 6, 3, 3) };
        private readonly Button btnArchetypes = new() { Text = "Edit archetypes...", AutoSize = true };

        private readonly TextBox _name = new() { Dock = DockStyle.Top };
        private readonly TextBox _desc = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10f) };

        private readonly TextBox _artPath = new() { ReadOnly = true, Width = 420 };
        private readonly Button btnChooseArt = new() { Text = "Choose art...", AutoSize = true };
        private readonly Label _artInfo = new() { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 8, 3, 3) };
        private readonly PictureBox _preview = new() { Size = new Size(228, 228), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(3, 8, 3, 3) };

        private ToolStripButton _duplicate = null!, _delete = null!, _inManager = null!;

        /// <summary>Set by the window: the page's Save (the same as File > Save all for this page).</summary>
        public Func<bool>? SaveRequested { get; set; }

        /// <summary>Set by the window: shows this card in the Card Manager (its genres, related cards, links).</summary>
        public Action<int>? ShowInCardManager { get; set; }

        /// <summary>A list entry: <see cref="Name"/> is what cards.json says, the text is how the Card Manager shows the game's value.</summary>
        private sealed record Named(string Name, string Text)
        {
            public override string ToString() => Text;
        }

        private static ComboBox Combo(int width, (string Name, int Value)[] items, Func<int, string>? text)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
            combo.Items.AddRange(items.Select(item => (object)new Named(item.Name, text == null ? item.Name : $"{item.Value}: {text(item.Value)}")).ToArray());
            combo.SelectedIndex = 0;
            return combo;
        }

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        private void BuildLayout()
        {
            // left: the cards
            _listTools.Items.Add(new ToolStripLabel("Find:"));
            _find.TextChanged += (_, _) => Refill(Selected);
            _listTools.Items.Add(_find);
            _list.Columns.Add("Id", 56);
            _list.Columns.Add("Name", 170);
            _list.Columns.Add("Kind", 90);
            _list.RetrieveVirtualItem += (_, e) =>
            {
                var card = _rows[e.ItemIndex];
                e.Item = new ListViewItem([card.Id.ToString(), card.Name, card.Kind]);
            };
            _list.SelectedIndexChanged += (_, _) => BindSelected();
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_list);
            left.Controls.Add(_listTools);

            // right: tools, preview + summary, tabs
            _tools.Items.Add(Button("Save", "Save cards.json and the art (Ctrl+S)", () => SaveRequested?.Invoke()));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("New card", "Add a card with the next free id", () => AddCard(null)));
            _tools.Items.Add(_duplicate = Button("Duplicate", "Add a copy of this card with the next free id", DuplicateCard));
            _tools.Items.Add(_delete = Button("Delete...", "Remove this card from cards.json (its art file stays)", DeleteCard));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(_inManager = Button("Genres, related cards, links...", "Show this card in the Card Manager, which has those tabs", () =>
            {
                if (Selected is { } card)
                    ShowInCardManager?.Invoke(card.Id);
            }));

            var upright = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            upright.Panel1.Controls.Add(_summary);
            _tabs.TabPages.Add(PropertiesTab());
            _tabs.TabPages.Add(TextTab());
            _tabs.TabPages.Add(ArtTab());
            upright.Panel2.Controls.Add(_tabs);
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(upright);
            right.Controls.Add(_tools);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);
            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(status);
            Load += (_, _) =>
            {
                split.SplitterDistance = Math.Clamp(split.Width * 36 / 100, 240, 360);   // the list: a third of the page
                upright.SplitterDistance = Math.Clamp(upright.Height * 45 / 100, 160, 300);
                upright.Panel1MinSize = 120;   // only once it has a size: WinForms throws otherwise
                upright.Panel2MinSize = 160;
                if (Selected == null && _rows.Count > 0)
                    Refill(_rows[0]);   // the list exists now: a selection made before it did is lost
            };

            foreach (var combo in new[] { _kind, _type, _attribute, _icon, _limitation })
                combo.SelectedIndexChanged += Editor_Changed;
            foreach (var number in new[] { _id, _level, _atk, _def, _scale, _copies })
                number.ValueChanged += Editor_Changed;
            _name.TextChanged += Editor_Changed;
            _arrows.Changed += () => Editor_Changed(_arrows, EventArgs.Empty);
            _desc.TextChanged += Editor_Changed;
            btnArchetypes.Click += btnArchetypes_Click;
            btnChooseArt.Click += btnChooseArt_Click;
        }

        private static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            row.Controls.AddRange(controls);
            return row;
        }

        private static TabPage Tab(string title, Control top, Control? note)
        {
            var tab = new TabPage(title) { UseVisualStyleBackColor = true, AutoScroll = true };
            tab.Controls.Add(top);
            if (note != null)
                tab.Controls.Add(note);
            return tab;
        }

        private static Label Note(string text) => new()
        {
            Dock = DockStyle.Top, AutoSize = false, Height = 40, ForeColor = SystemColors.GrayText, Padding = new Padding(9, 6, 6, 0), Text = text,
        };

        private TabPage PropertiesTab()
        {
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(6) };
            var rows = new (Control Label, Control Field)[]
            {
                // the Card Manager's fields in its order, then what only a new card has
                (Caption("Card id:"), _id), (Caption("Kind:"), _kind), (Caption("Attribute:"), _attribute), (Caption("Type:"), _type),
                (_levelLabel, _level), (Caption("ATK:"), _atk), (Caption("DEF:"), _def), (Caption("Spell / Trap icon:"), _icon),
                (Caption("Link arrows:"), _arrows), (Caption("Pendulum scale:"), _scale), (Caption("Archetypes:"), Row(_archetypes, btnArchetypes)),
                (Caption("Limitation:"), _limitation), (Caption("Owned from the start:"), _copies),
            };
            for (int row = 0; row < rows.Length; row++)
            {
                grid.Controls.Add(rows[row].Label, 0, row);
                grid.Controls.Add(rows[row].Field, 1, row);
            }
            var tab = Tab("Properties", grid, Note($"Saved to Yu-Gi-Oh-Ex\\cards.json (Yu-Gi-Oh-MoreCards). Ids {MinId}-{MaxId}; ATK and DEF in steps of 10. " +
                                                    "\"Owned from the start\": copies every profile has."));
            return tab;
        }

        private TabPage TextTab()
        {
            var fields = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            fields.Controls.Add(_desc);
            fields.Controls.Add(new Label { Text = "Text:", Dock = DockStyle.Top, Height = 20 });
            fields.Controls.Add(_name);
            fields.Controls.Add(new Label { Text = "Name:", Dock = DockStyle.Top, Height = 20 });
            var tab = new TabPage("Text") { UseVisualStyleBackColor = true };
            tab.Controls.Add(fields);
            return tab;
        }

        private TabPage ArtTab()
        {
            var flow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(6) };
            flow.Controls.Add(Row(_artPath, btnChooseArt));
            flow.Controls.Add(_artInfo);
            flow.Controls.Add(_preview);
            return Tab("Art", flow, Note("A 304 x 304 JPG, 24 bit, is what the game expects. The picture is copied next to cards.json as <id>.jpg / .png when you save."));
        }
    }
}
