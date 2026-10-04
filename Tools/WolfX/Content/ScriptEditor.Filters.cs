using System.Runtime.InteropServices;
using System.Text;

namespace WolfEx
{
    /// <summary>
    /// The filter strip under the completion list (as in Visual Studio): one button per kind of entry. A click shows only that kind (more clicks add kinds; none picked = all); hovering
    /// says what it is. The strip never takes the focus (WS_EX_NOACTIVATE), because Scintilla closes its list when the editor loses it.
    /// </summary>
    internal sealed partial class ScriptEditor
    {
        private static readonly (int Kind, string Letter, Color Colour, string Name, string What)[] KindInfo =
        [
            (IconAction, "A", Color.FromArgb(0x8E, 0x44, 0xAD), "Actions", "What the effect does: draw, destroy, search, special_summon ..."),
            (IconTrigger, "T", Color.FromArgb(0xD3, 0x7A, 0x12), "Triggers", "When a monster effect happens (after on): flip, normal_summoned, sent_to_grave ..."),
            (IconField, "F", Color.FromArgb(0x16, 0x8A, 0x8A), "Filters", "What to narrow a selection by (after where / and): race, attribute, level, atk, archetype, name"),
            (IconValue, "V", Color.FromArgb(0x3C, 0x8D, 0x2F), "Values", "Monster Types and Attributes: Dragon, Spellcaster, Dark, Light ..."),
            (IconKeyword, "K", Color.FromArgb(0x1F, 0x6F, 0xC5), "Keywords", "The rest of the language: effect, on, then, where, all, target, deck, grave, once_per_turn ..."),
            (IconText, "\"", Color.FromArgb(0xA3, 0x4B, 0x2A), "Archetype names", "Names from Archetypes.json, written in quotes"),
            (IconOperator, "=", Color.FromArgb(0x60, 0x60, 0x60), "Comparisons", "=, <=, >=, <, >"),
        ];

        private static Bitmap IconBitmap(int kind)
        {
            var info = KindInfo.First(k => k.Kind == kind);
            var bitmap = new Bitmap(14, 14);
            using var g = Graphics.FromImage(bitmap);
            using var font = new Font("Segoe UI", 7F, FontStyle.Bold);
            using var fill = new SolidBrush(info.Colour);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.FillRectangle(fill, 1, 1, 12, 12);
            TextRenderer.DrawText(g, info.Letter, font, new Rectangle(0, 0, 14, 14), Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return bitmap;
        }

        // ---- the list's own window (Scintilla's ListBoxX popup), so the strip and the hint sit right against it ----

        private delegate bool EnumThreadProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32")] private static extern bool EnumThreadWindows(uint threadId, EnumThreadProc callback, IntPtr lParam);
        [DllImport("kernel32")] private static extern uint GetCurrentThreadId();
        [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
        [DllImport("user32")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>Where the completion list is on screen, or null when it can't be found (then the caret position is used).</summary>
        private static Rectangle? ListBounds()
        {
            Rectangle? found = null;
            EnumThreadWindows(GetCurrentThreadId(), (hwnd, _) =>
            {
                var name = new StringBuilder(64);
                GetClassName(hwnd, name, name.Capacity);
                if (name.ToString() == "ListBoxX" && IsWindowVisible(hwnd) && GetWindowRect(hwnd, out var r))
                {
                    found = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>The list on screen: the real window when found, else worked out from the caret.</summary>
        private Rectangle ListScreenBounds()
        {
            if (ListBounds() is { } real)
                return real;
            int lineBottom = PointYFromPosition(CurrentPosition) + Lines[LineFromPosition(CurrentPosition)].Height;
            return RectangleToScreen(new Rectangle(_listLeft, lineBottom, _listWidth, Lines[0].Height * 10));
        }

        // ---- the strip ----

        /// <summary>Kinds picked on the strip: the list shows only these (none picked = everything); cleared when the list closes.</summary>
        private readonly HashSet<int> _onlyKinds = [];
        private FilterStrip? _strip;
        private bool _reshowing, _lastAutomatic;

        private void ShowStrip(IReadOnlyCollection<int> present)
        {
            _strip ??= new FilterStrip(this);
            _strip.SetKinds(present, _onlyKinds);
            var list = ListScreenBounds();
            var screen = Screen.FromRectangle(list).WorkingArea;
            int y = list.Bottom + _strip.Height <= screen.Bottom ? list.Bottom : list.Top - _strip.Height;
            _strip.Location = new Point(list.Left, y);
            if (!_strip.Visible)
                _strip.Show(FindForm());
        }

        private void HideStrip()
        {
            if (_reshowing)
                return;
            _strip?.Hide();
            _onlyKinds.Clear();
        }

        /// <summary>A strip button was clicked: pick or unpick that kind and list again.</summary>
        private void ToggleKind(int kind)
        {
            if (!_onlyKinds.Remove(kind))
                _onlyKinds.Add(kind);
            _reshowing = true;
            try { ShowCompletion(_lastAutomatic); }
            finally { _reshowing = false; }
        }

        private sealed class FilterStrip : Form
        {
            private readonly ScriptEditor _editor;
            private readonly ToolTip _tips = new() { ShowAlways = true, InitialDelay = 300, UseAnimation = false, UseFading = false };
            private readonly FlowLayoutPanel _buttons = new() { Dock = DockStyle.Fill, Padding = new Padding(2), WrapContents = false };

            public FilterStrip(ScriptEditor editor)
            {
                _editor = editor;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = SystemColors.Control;
                Padding = new Padding(1);
                Controls.Add(_buttons);
                Paint += (_, e) => ControlPaint.DrawBorder(e.Graphics, ClientRectangle, SystemColors.ControlDark, ButtonBorderStyle.Solid);
                foreach (var info in KindInfo)
                {
                    var button = new KindButton(info.Kind, IconBitmap(info.Kind)) { Margin = new Padding(1) };
                    button.Click += (_, _) => _editor.ToggleKind(button.Kind);
                    _buttons.Controls.Add(button);
                }
                ClientSize = new Size(KindInfo.Length * 24 + 6, 28);
            }

            public void SetKinds(IReadOnlyCollection<int> present, HashSet<int> picked)
            {
                foreach (KindButton button in _buttons.Controls)
                {
                    var info = KindInfo.First(k => k.Kind == button.Kind);
                    button.Present = present.Contains(button.Kind) || picked.Contains(button.Kind);
                    button.On = picked.Contains(button.Kind);
                    string state = !button.Present ? "None in this list."
                        : button.On ? "Picked: the list shows only the picked kinds. Click to unpick."
                        : "Click to show only these (add more kinds with more clicks).";
                    _tips.SetToolTip(button, $"{info.Name}\r\n{info.What}\r\n\r\n{state}");
                    button.Invalidate();
                }
            }

            // never activated: a click must leave the focus (and so Scintilla's list) in the editor
            protected override bool ShowWithoutActivation => true;

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000 /* WS_EX_NOACTIVATE */ | 0x80 /* WS_EX_TOOLWINDOW */;
                    return cp;
                }
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == 0x21 /* WM_MOUSEACTIVATE */)
                {
                    m.Result = 3; // MA_NOACTIVATE
                    return;
                }
                base.WndProc(ref m);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _tips.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>One kind's button: its icon, pressed in while the kind is shown. It can't take the focus.</summary>
        private sealed class KindButton : Control
        {
            private readonly Bitmap _icon;
            private bool _hover;

            public KindButton(int kind, Bitmap icon)
            {
                Kind = kind;
                _icon = icon;
                Size = new Size(22, 22);
                Cursor = Cursors.Hand;
                SetStyle(ControlStyles.Selectable, false);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            }

            public int Kind { get; }
            public bool On { get; set; }
            public bool Present { get; set; } = true;

            protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnClick(EventArgs e)
            {
                if (Present)
                    base.OnClick(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(Parent?.BackColor ?? SystemColors.Control);
                var box = new Rectangle(0, 0, Width - 1, Height - 1);
                if (On && Present)
                {
                    // picked: pressed in, like a checked toolbar button
                    using var back = new SolidBrush(Color.FromArgb(0xCC, 0xE4, 0xF7));
                    g.FillRectangle(back, box);
                    using var edge = new Pen(SystemColors.Highlight);
                    g.DrawRectangle(edge, box);
                }
                else if (_hover && Present)
                {
                    using var edge = new Pen(SystemColors.Highlight);
                    g.DrawRectangle(edge, box);
                }
                var at = new Point((Width - _icon.Width) / 2, (Height - _icon.Height) / 2);
                if (Present)
                    g.DrawImage(_icon, at);
                else
                    ControlPaint.DrawImageDisabled(g, _icon, at.X, at.Y, Parent?.BackColor ?? SystemColors.Control);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) _icon.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
