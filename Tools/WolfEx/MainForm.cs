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
    internal sealed partial class MainForm : Form
    {
        private readonly List<IContentPanel> _panels;

        public MainForm()
        {
            InitializeComponent();

            // The pages are the tabs in the designer, in the same order. Effects is not in this list: it
            // has no file of its own - it edits the same CardModel instances cardsPanel owns, which is
            // the only one of these that actually writes cards.json.
            _panels = [cardsPanel, unlocksPanel, packsPanel, menusPanel];
            cardsPanel.Warning += SetStatus;
            effectsPanel.Attach(cardsPanel);

            // The Effect library is a read-only reference (the game's cards written in EffectScript); "Use as template" hands a script to the Effects tab.
            var libraryPanel = new EffectLibraryPanel { Dock = DockStyle.Fill };
            var tabLibrary = new TabPage("Effect library") { UseVisualStyleBackColor = true };
            tabLibrary.Controls.Add(libraryPanel);
            _tabs.TabPages.Add(tabLibrary);
            libraryPanel.UseTemplate = script =>
            {
                if (!effectsPanel.ApplyTemplate(script))
                    return false;
                _tabs.SelectedTab = tabEffects;
                return true;
            };

            string? saved = ReadRemembered();
            if (saved != null && Directory.Exists(saved))
            {
                _gameFolder.Text = saved;
                Reload();
            }
            else
                SetStatus("Pick the game folder (the one with YuGiOh.exe). Files go in its Yu-Gi-Oh-Ex folder.");
        }

        private void btnBrowse_Click(object? sender, EventArgs e) => Browse();

        private void btnReload_Click(object? sender, EventArgs e) => Reload();

        private void btnSaveAll_Click(object? sender, EventArgs e) => SaveAll();

        private string ExtraCardsFolder => Path.Combine(_gameFolder.Text, "Yu-Gi-Oh-Ex");

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

            WolfX.Types.CardCatalog.Use(_gameFolder.Text);
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
                    if (((Control)panel).Parent is TabPage page)
                        _tabs.SelectedTab = page;
                    SetStatus($"Not saved: fix the {panel.Title} page first.");
                    return;
                }
            }

            SetStatus($"Saved to {ExtraCardsFolder}. Restart the game to load it.");
        }

        private void SetStatus(string text) => _status.Text = text;

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
