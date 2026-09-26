namespace WolfEx
{
    /// <summary>A page of WolfEx that edits files in the game's Yu-Gi-Oh-Ex folder.</summary>
    internal interface IContentPanel
    {
        string Title { get; }

        /// <summary>Reads the page's file from the Yu-Gi-Oh-Ex folder (it may not exist yet).</summary>
        void LoadFrom(string extraCardsFolder, string gameFolder);

        /// <summary>Writes the page's file. Returns false when something has to be fixed first.</summary>
        bool SaveTo(string extraCardsFolder);
    }

    /// <summary>
    /// Edits the content the Yu-Gi-Oh-MoreCards plugin loads from the game folder: Yu-Gi-Oh-Ex/cards.json (new cards
    /// and their art) Yu-Gi-Oh-Ex/unlocks.json (cards the player owns from the start) and Yu-Gi-Oh-Ex/packs.json (cards added to shop packs). Only JSON and image files.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private readonly TextBox _gameFolder = new() { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly Label _status = new() { AutoSize = true, Padding = new Padding(4, 6, 0, 0), Dock = DockStyle.Bottom };
        private readonly List<IContentPanel> _panels = [];
        private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

        public MainForm()
        {
            Text = "WolfEx - new content for Legacy of the Duelist";
            Width = 1100;
            Height = 760;

            var top = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 5, Padding = new Padding(4) };
            top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++)
                top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            top.Controls.Add(new Label { Text = "Game folder:", AutoSize = true, Padding = new Padding(0, 6, 4, 0) }, 0, 0);
            top.Controls.Add(_gameFolder, 1, 0);
            top.Controls.Add(MakeButton("Browse...", (_, _) => Browse()), 2, 0);
            top.Controls.Add(MakeButton("Reload", (_, _) => Reload()), 3, 0);
            top.Controls.Add(MakeButton("Save all", (_, _) => SaveAll()), 4, 0);

            AddPanel(new CardsPanel());
            AddPanel(new UnlocksPanel());
            AddPanel(new PacksPanel());

            Controls.Add(_tabs);
            Controls.Add(_status);
            Controls.Add(top);

            string? saved = ReadRemembered();
            if (saved != null && Directory.Exists(saved))
            {
                _gameFolder.Text = saved;
                Reload();
            }
            else
                SetStatus("Pick the game folder (the one with YuGiOh.exe). Files go in its Yu-Gi-Oh-Ex folder.");
        }

        private string ExtraCardsFolder => Path.Combine(_gameFolder.Text, "Yu-Gi-Oh-Ex");

        private void AddPanel(IContentPanel panel)
        {
            _panels.Add(panel);
            var page = new TabPage(panel.Title);
            page.Controls.Add((Control)panel);
            ((Control)panel).Dock = DockStyle.Fill;
            _tabs.TabPages.Add(page);
        }

        private void Browse()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the game folder (the one containing YuGiOh.exe)",
                UseDescriptionForTitle = true,
                SelectedPath = _gameFolder.Text,
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            if (!File.Exists(Path.Combine(dialog.SelectedPath, "YuGiOh.exe")))
            {
                MessageBox.Show("YuGiOh.exe is not in that folder.", "WolfEx", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _gameFolder.Text = dialog.SelectedPath;
            Remember(dialog.SelectedPath);
            Reload();
        }

        private void Reload()
        {
            if (string.IsNullOrEmpty(_gameFolder.Text))
                return;

            foreach (var panel in _panels)
                panel.LoadFrom(ExtraCardsFolder, _gameFolder.Text);

            SetStatus($"Loaded from {ExtraCardsFolder}");
        }

        private void SaveAll()
        {
            if (string.IsNullOrEmpty(_gameFolder.Text))
            {
                Browse();
                if (string.IsNullOrEmpty(_gameFolder.Text))
                    return;
            }

            Directory.CreateDirectory(ExtraCardsFolder);
            foreach (var panel in _panels)
            {
                if (!panel.SaveTo(ExtraCardsFolder))
                {
                    _tabs.SelectedIndex = _panels.IndexOf(panel);
                    SetStatus($"Not saved: fix the {panel.Title} page first.");
                    return;
                }
            }

            SetStatus($"Saved to {ExtraCardsFolder}. Restart the game to load it.");
        }

        private void SetStatus(string text) => _status.Text = text;

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += onClick;
            return button;
        }

        private static string RememberedFile =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfEx", "game.folder");

        private static string? ReadRemembered()
        {
            try { return File.Exists(RememberedFile) ? File.ReadAllText(RememberedFile).Trim() : null; }
            catch { return null; }
        }

        private static void Remember(string folder)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(RememberedFile)!);
                File.WriteAllText(RememberedFile, folder);
            }
            catch { /* remembering the folder is a convenience only */ }
        }
    }
}
