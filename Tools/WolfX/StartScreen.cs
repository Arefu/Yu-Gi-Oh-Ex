namespace WolfX
{
    /// <summary>
    /// What the window shows while nothing is open: the two ways to open the game data (its YGO_2020.dat, or an extracted copy), the
    /// Steam install when one was found, and the folders opened before.
    /// </summary>
    internal sealed class StartScreen : UserControl
    {
        private readonly FlowLayoutPanel _flow = new()
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(24, 16, 24, 16),
        };

        public Action? OpenArchive { get; set; }
        public Action? OpenExtracted { get; set; }
        public Action<string>? OpenPath { get; set; }

        public StartScreen()
        {
            BackColor = SystemColors.Window;
            Controls.Add(_flow);
        }

        public void Fill(string? steamInstall, IReadOnlyList<string> recent)
        {
            _flow.SuspendLayout();
            _flow.Controls.Clear();
            if (steamInstall != null)
                _flow.Controls.Add(Choice("Open the Steam install", $"Found {Path.Combine(steamInstall, "YGO_2020.dat")}. Changes are saved into it; " +
                                          "File > Restore the original archive undoes them.", () => OpenPath?.Invoke(steamInstall), main: true));
            _flow.Controls.Add(Choice("Open YGO_2020.dat...", "The game's archive, in the game folder. Everything is read from it and saved back into it.",
                () => OpenArchive?.Invoke(), main: steamInstall == null));
            _flow.Controls.Add(Choice("Open an extracted folder...", "A copy of the files taken out of YGO_2020.dat (bin, main, the .zib archives...), any folder " +
                                      "name. Changes are saved as files there.", () => OpenExtracted?.Invoke(), main: false));

            var others = recent.Where(path => !path.Equals(steamInstall, StringComparison.OrdinalIgnoreCase)).ToList();
            if (others.Count > 0)
            {
                _flow.Controls.Add(new Label { Text = "Recent", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 18, 3, 4) });
                foreach (string path in others)
                {
                    var link = new LinkLabel { Text = path, AutoSize = true, Enabled = Directory.Exists(path), Margin = new Padding(6, 2, 3, 2) };
                    link.LinkClicked += (_, _) => OpenPath?.Invoke(path);
                    _flow.Controls.Add(link);
                }
            }
            _flow.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 18, 3, 3),
                Text = "You can also drop the game folder, YGO_2020.dat or an extracted folder on this window. New content (custom cards, packs, " +
                       "pages...) is saved as JSON in the Yu-Gi-Oh-Ex folder next to what you open.",
            });
            _flow.ResumeLayout();
        }

        private static Control Choice(string title, string text, Action click, bool main)
        {
            var panel = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(3, 6, 3, 6) };
            var button = new Button
            {
                Text = title, AutoSize = true, MinimumSize = new Size(260, 36), Font = new Font("Segoe UI", 10f, main ? FontStyle.Bold : FontStyle.Regular),
                TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 8, 0),
            };
            button.Click += (_, _) => click();
            panel.Controls.Add(button);
            panel.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(620, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(4, 3, 3, 3) });
            return panel;
        }
    }
}
