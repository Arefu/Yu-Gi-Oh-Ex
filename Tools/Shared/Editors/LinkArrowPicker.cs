namespace Wolf.Editors
{
    /// <summary>
    /// A Link monster's arrows as a 3 x 3 grid of toggles around the card (the middle is the card). <see cref="Value"/> is the game's mask:
    /// bit 0 top-left, 1 top, 2 top-right, 3 left, 4 right, 5 bottom-left, 6 bottom, 7 bottom-right (CARD_Prop.bin's DEF bits for a Link,
    /// cards.json's "linkmarkers").
    /// </summary>
    public sealed class LinkArrowPicker : TableLayoutPanel
    {
        private static readonly (int Bit, int Column, int Row, string Glyph, string Name)[] Arrows =
        [
            (0, 0, 0, "↖", "top-left"), (1, 1, 0, "↑", "top"), (2, 2, 0, "↗", "top-right"),
            (3, 0, 1, "←", "left"), (4, 2, 1, "→", "right"),
            (5, 0, 2, "↙", "bottom-left"), (6, 1, 2, "↓", "bottom"), (7, 2, 2, "↘", "bottom-right"),
        ];

        private readonly CheckBox[] _boxes = new CheckBox[8];
        private bool _setting;

        /// <summary>An arrow was turned on or off (not raised when <see cref="Value"/> is set).</summary>
        public event Action? Changed;

        public LinkArrowPicker()
        {
            ColumnCount = 3;
            RowCount = 3;
            AutoSize = true;
            Margin = new Padding(0, 2, 0, 2);
            var tips = new ToolTip();
            foreach (var (bit, column, row, glyph, name) in Arrows)
            {
                var box = new CheckBox
                {
                    Appearance = Appearance.Button, Text = glyph, Size = new Size(30, 26), TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(1),
                    Font = new Font("Segoe UI Symbol", 10f), FlatStyle = FlatStyle.Flat,
                };
                box.FlatAppearance.CheckedBackColor = Color.FromArgb(230, 80, 60);
                tips.SetToolTip(box, $"Points {name}");
                box.CheckedChanged += (_, _) =>
                {
                    box.ForeColor = box.Checked ? Color.White : SystemColors.ControlText;
                    if (!_setting)
                        Changed?.Invoke();
                };
                _boxes[bit] = box;
                Controls.Add(box, column, row);
            }
            Controls.Add(new Label { Text = "LINK", AutoSize = false, Size = new Size(30, 26), TextAlign = ContentAlignment.MiddleCenter, ForeColor = SystemColors.GrayText, Font = new Font("Segoe UI", 7f) }, 1, 1);
        }

        public int Value
        {
            get
            {
                int value = 0;
                for (int bit = 0; bit < 8; bit++)
                    if (_boxes[bit].Checked)
                        value |= 1 << bit;
                return value;
            }
            set
            {
                _setting = true;
                try
                {
                    for (int bit = 0; bit < 8; bit++)
                        _boxes[bit].Checked = (value & 1 << bit) != 0;
                }
                finally
                {
                    _setting = false;
                }
            }
        }

        /// <summary>How many arrows are on (a Link monster's rating is meant to match it).</summary>
        public int Count => Enumerable.Range(0, 8).Count(bit => _boxes[bit].Checked);
    }
}
