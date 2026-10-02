namespace Wolf.Editors
{
    /// <summary>
    /// Makes designer-laid-out windows (WolfX's pages are all absolute positions) behave when resized, and gives every container a
    /// sensible tab order. Run once after InitializeComponent, while the controls are still at their designed sizes.
    /// </summary>
    public static class FormLayout
    {
        /// <summary>Sizable, maximizable, no smaller than it was designed; then anchors and tab order for everything in it.</summary>
        public static void MakeResizable(Form form)
        {
            form.MinimumSize = form.Size;
            form.FormBorderStyle = FormBorderStyle.Sizable;
            form.MaximizeBox = true;
            AnchorByLayout(form);
            ApplyReadingTabOrder(form);
        }

        /// <summary>
        /// Anchors each absolutely placed control from where it sits in its container:
        ///   spans most of the width  -> stretches left-right; spans most of the height -> stretches top-bottom;
        ///   sits against the right / bottom edge -> stays against it; anything else keeps its place.
        /// Docked controls, and children of flow / table layouts and split containers, are left to their layout.
        /// </summary>
        public static void AnchorByLayout(Control root)
        {
            foreach (Control child in root.Controls)
                AnchorByLayout(child);

            if (root is FlowLayoutPanel or TableLayoutPanel or SplitContainer or ToolStrip or DataGridView or ListView or TreeView ||
                root.Controls.Count == 0 || root.ClientSize.Width <= 0 || root.ClientSize.Height <= 0)
                return;

            var client = root.ClientSize;
            const int Edge = 30;
            foreach (Control child in root.Controls)
            {
                if (child.Dock != DockStyle.None || child is ToolStrip or StatusStrip or MenuStrip)
                    continue;
                if (child.Anchor != (AnchorStyles.Top | AnchorStyles.Left))
                    continue;   // someone already chose an anchor: keep it

                var r = child.Bounds;
                var anchor = AnchorStyles.None;
                bool wide = r.Width >= client.Width * 0.55;
                bool tall = r.Height >= client.Height * 0.55;
                bool atRight = client.Width - r.Right <= Edge && r.Left > client.Width * 0.4;
                bool atBottom = client.Height - r.Bottom <= Edge && r.Top > client.Height * 0.4;

                anchor |= wide ? AnchorStyles.Left | AnchorStyles.Right : atRight ? AnchorStyles.Right : AnchorStyles.Left;
                anchor |= tall ? AnchorStyles.Top | AnchorStyles.Bottom : atBottom ? AnchorStyles.Bottom : AnchorStyles.Top;

                // a single-line control can't grow in height
                if (child is TextBox { Multiline: false } or ComboBox or NumericUpDown or DateTimePicker or Label { AutoSize: true } or CheckBox or RadioButton or Button)
                    anchor &= ~(tall ? AnchorStyles.Bottom : AnchorStyles.None);

                // A control that shows content (a tab control, grid, list, tree, text, picture, an editor) grows into free space: to the
                // right edge if nothing sits to its right, down to the bottom if nothing sits below it.
                if (IsGrowable(child))
                {
                    var siblings = root.Controls.Cast<Control>().Where(s => s != child && s.Visible && s.Dock == DockStyle.None).ToList();
                    bool freeRight = !siblings.Any(s => s.Left >= r.Right - 2 && s.Top < r.Bottom && s.Bottom > r.Top);
                    bool freeBelow = !siblings.Any(s => s.Top >= r.Bottom - 2 && s.Left < r.Right && s.Right > r.Left);
                    if (freeRight && (anchor & AnchorStyles.Left) != 0)
                        anchor |= AnchorStyles.Right;
                    if (freeBelow && (anchor & AnchorStyles.Top) != 0)
                        anchor |= AnchorStyles.Bottom;
                }
                child.Anchor = anchor;
            }
        }

        private static bool IsGrowable(Control control) =>
            control is TabControl or DataGridView or ListBox or ListView or TreeView or RichTextBox or TextBox { Multiline: true } or PictureBox or UserControl;

        /// <summary>Tab order in reading order (rows top to bottom, left to right within a row), in every container.</summary>
        public static void ApplyReadingTabOrder(Control root)
        {
            const int RowTolerance = 12;
            var children = root.Controls.Cast<Control>().ToList();
            if (children.Count > 1 && root is not (FlowLayoutPanel or TableLayoutPanel or ToolStrip))
            {
                // docked controls keep the order that places them; the rest go by position
                var placed = children.Where(c => c.Dock == DockStyle.None).OrderBy(c => c.Top).ToList();
                var rows = new List<List<Control>>();
                foreach (var control in placed)
                {
                    var row = rows.LastOrDefault();
                    if (row == null || control.Top - row[0].Top > RowTolerance)
                        rows.Add(row = []);
                    row.Add(control);
                }
                int index = 0;
                foreach (var docked in children.Where(c => c.Dock != DockStyle.None).Reverse())   // docking order is reverse z-order
                    docked.TabIndex = index++;
                foreach (var control in rows.SelectMany(row => row.OrderBy(c => c.Left)))
                    control.TabIndex = index++;
            }
            foreach (Control child in root.Controls)
                ApplyReadingTabOrder(child);
        }
    }
}
