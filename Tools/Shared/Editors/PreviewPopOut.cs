namespace Wolf.Editors
{
    /// <summary>A button shown along the top of a preview's larger window (Play, Prev, Next for a story scene...).</summary>
    public sealed record PopOutAction(string Text, string Tip, Action Click);

    /// <summary>
    /// "Show larger" for any preview that draws itself scaled to its size (card face, How to Play, tutorial, story stage, animlist, sprite
    /// sheet, page designer...). A small button in the preview's top-right corner (and a double-click, where the preview has no other use
    /// for it) moves the preview itself into its own big, resizable window: it keeps updating as you edit, and stays interactive (drag
    /// a character, pick a sprite). The window keeps the preview's shape, as large as fits the screen, and can carry the preview's own
    /// controls (<see cref="PopOutAction"/>: play a scene there). Closing it, Esc or the button again puts the preview back; a note fills
    /// its place meanwhile.
    /// </summary>
    public static class PreviewPopOut
    {
        private sealed class State
        {
            public required Button Button;
            public required string Title;
            public required PopOutAction[] Actions;
            public Size? Shape;
            public Form? Window;
        }

        private const int ButtonSize = 24, Margin = 6;
        private const string Enlarge = "⤢", Restore = "✖";

        private static readonly Dictionary<Control, State> Attached = [];
        private static readonly ToolTip Tips = new();

        /// <param name="doubleClick">Also on double-click (leave off where double-click already does something).</param>
        /// <param name="actions">Buttons for the larger window's toolbar (the preview's own controls, which stay behind on the page).</param>
        public static void Attach(Control preview, string title, bool doubleClick = false, params PopOutAction[] actions)
        {
            if (Attached.ContainsKey(preview))
                return;
            var button = new Button
            {
                Text = Enlarge, Size = new Size(ButtonSize, ButtonSize), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, TabStop = false,
                Font = new Font("Segoe UI Symbol", 9f), Anchor = AnchorStyles.Top | AnchorStyles.Right, Padding = Padding.Empty, Margin = Padding.Empty,
                UseCompatibleTextRendering = true, TextAlign = ContentAlignment.MiddleCenter,
            };
            Style(button, preview.BackColor);
            preview.BackColorChanged += (_, _) => Style(button, preview.BackColor);
            Tips.SetToolTip(button, "Show larger, in its own window (Esc puts it back)");
            var state = new State { Button = button, Title = title, Actions = actions };
            Attached[preview] = state;
            preview.Controls.Add(button);
            Place(preview, button);
            preview.Resize += (_, _) => Place(preview, button);
            button.Click += (_, _) => Toggle(preview);
            if (doubleClick)
                preview.DoubleClick += (_, _) => Toggle(preview);
            preview.Disposed += (_, _) => Attached.Remove(preview);
        }

        /// <summary>The button matches what it sits on: a dark chip on a dark preview, a light one on a light preview.</summary>
        private static void Style(Button button, Color background)
        {
            bool dark = background.GetBrightness() < 0.45f;
            button.BackColor = dark ? Color.FromArgb(58, 60, 72) : Color.FromArgb(244, 244, 247);
            button.ForeColor = dark ? Color.FromArgb(225, 228, 240) : Color.FromArgb(50, 52, 70);
            button.FlatAppearance.BorderColor = dark ? Color.FromArgb(95, 98, 115) : Color.FromArgb(175, 178, 190);
            button.FlatAppearance.MouseOverBackColor = dark ? Color.FromArgb(80, 84, 100) : Color.FromArgb(226, 230, 240);
        }

        private static void Place(Control preview, Button button)
        {
            // inside the client area, clear of any scroll bar
            button.Location = new Point(Math.Max(0, preview.ClientSize.Width - ButtonSize - Margin), Margin);
            button.BringToFront();
        }

        /// <summary>
        /// The shape of what the preview shows (1920 x 1080 for a game screen, 400 x 580 for a card), which its larger window takes; without
        /// it the window takes the preview's own shape, letterboxing included.
        /// </summary>
        public static void SetShape(Control preview, Size shape)
        {
            if (Attached.TryGetValue(preview, out var state))
                state.Shape = shape;
        }

        /// <summary>Pops the preview out, or back in when it is out.</summary>
        public static void Toggle(Control preview)
        {
            if (!Attached.TryGetValue(preview, out var state))
                return;
            if (state.Window != null)
            {
                state.Window.Close();
                return;
            }
            if (preview.Parent is not { } parent)
                return;

            // keep its place: a note where it was, then the preview itself in the window
            int index = parent.Controls.GetChildIndex(preview);
            var (dock, anchor, bounds) = (preview.Dock, preview.Anchor, preview.Bounds);
            var note = new Label
            {
                Text = $"{state.Title} is in its own window.\nClick here or close it to bring it back.", TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SystemColors.GrayText, Dock = dock, Anchor = anchor, Bounds = bounds, Cursor = Cursors.Hand,
            };
            note.Click += (_, _) => state.Window?.Activate();
            parent.SuspendLayout();
            parent.Controls.Remove(preview);
            parent.Controls.Add(note);
            parent.Controls.SetChildIndex(note, index);
            parent.ResumeLayout();

            var owner = parent.FindForm();
            var window = new Form
            {
                Text = state.Title, ShowInTaskbar = false, KeyPreview = true, MinimizeBox = false, StartPosition = FormStartPosition.Manual,
                MinimumSize = new Size(360, 280), Icon = owner?.Icon,
            };
            ToolStrip? bar = null;
            if (state.Actions.Length > 0)
            {
                bar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
                foreach (var action in state.Actions)
                {
                    var item = new ToolStripButton(action.Text) { ToolTipText = action.Tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
                    item.Click += (_, _) => action.Click();
                    bar.Items.Add(item);
                }
            }

            // the preview's shape, as large as fits 85% of the screen it is on
            var area = Screen.FromControl(parent).WorkingArea;
            var shape = state.Shape ?? (bounds.Width > 40 && bounds.Height > 40 ? bounds.Size : new Size(16, 9));
            int extraHeight = (bar?.PreferredSize.Height ?? 0) + SystemInformation.CaptionHeight + SystemInformation.FrameBorderSize.Height * 2;
            double scale = Math.Min(area.Width * 0.85 / shape.Width, (area.Height * 0.85 - extraHeight) / shape.Height);
            var client = new Size(Math.Max(360, (int)(shape.Width * scale)), Math.Max(240, (int)(shape.Height * scale)) + (bar?.PreferredSize.Height ?? 0));
            window.ClientSize = client;
            window.Size = new Size(Math.Min(window.Width, area.Width), Math.Min(window.Height, area.Height));
            window.Location = new Point(area.Left + (area.Width - window.Width) / 2, area.Top + (area.Height - window.Height) / 2);

            preview.Dock = DockStyle.Fill;
            window.Controls.Add(preview);
            if (bar != null)
                window.Controls.Add(bar);
            state.Button.Text = Restore;
            Tips.SetToolTip(state.Button, "Put it back (Esc)");
            window.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    window.Close();
            };
            window.FormClosed += (_, _) =>
            {
                state.Window = null;
                window.Controls.Remove(preview);
                state.Button.Text = Enlarge;
                Tips.SetToolTip(state.Button, "Show larger, in its own window (Esc puts it back)");
                if (note.IsDisposed || note.Parent is not { } home)
                    return;
                home.SuspendLayout();
                int at = home.Controls.GetChildIndex(note);
                home.Controls.Remove(note);
                note.Dispose();
                preview.Dock = dock;
                preview.Anchor = anchor;
                if (dock is DockStyle.None or DockStyle.Left or DockStyle.Right or DockStyle.Top or DockStyle.Bottom)
                    preview.Bounds = bounds;
                home.Controls.Add(preview);
                home.Controls.SetChildIndex(preview, at);
                home.ResumeLayout();
            };
            state.Window = window;
            if (owner != null)
                window.Show(owner);
            else
                window.Show();
        }
    }
}
