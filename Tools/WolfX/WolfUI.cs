using System.IO;
using Wolf.Editors;
using WolfEx;
using WolfX.Types;

namespace WolfX
{
    /// <summary>
    /// WolfX: one window for everything (WolfEx is part of it now). Open the game folder (its YGO_2020.dat is read and saved in place) or an
    /// extracted YGO_2020 folder; every page works on that (<see cref="GameFolderFiles.Current"/>). Standard content goes back into the
    /// game's own files, additional content into JSON in the Yu-Gi-Oh-Ex folder, which the launcher plugins apply.
    /// The pages are picked from the list on the left; the old designer-made pages scroll when the window is smaller than they were drawn.
    /// </summary>
    public partial class WolfUI : Form
    {
        private sealed record Page(string Group, string Title, Control Content, IGameEditor? Editor, string SavesTo, Control Host);

        /// <summary>The smallest a page is laid out at: a smaller window scrolls the page instead of squeezing it.</summary>
        private static readonly Size PageMinimum = new(760, 440);

        /// <summary>A scrolling page holder that doesn't jump to the focused control (which hid the top toolbar of some pages).</summary>
        private sealed class ScrollHost : Panel
        {
            protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;
        }

        private readonly List<Page> _pages = [];
        private readonly TreeView _nav = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true, ShowLines = false, ItemHeight = 22, BorderStyle = BorderStyle.None };
        private readonly Panel _host = new() { Dock = DockStyle.Fill };
        private readonly Label _pageTitle = new() { Dock = DockStyle.Top, Height = 26, Font = new Font("Segoe UI", 11f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), UseMnemonic = false };
        private readonly Label _pageSaves = new() { Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0), AutoEllipsis = true };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ToolStripStatusLabel _missing = new() { IsLink = true, LinkColor = Color.DarkGoldenrod, Visible = false, ToolTipText = "Which files, and what they are for" };
        private readonly ToolStripStatusLabel _restore = new("Restore the original archive...") { IsLink = true, Visible = false, ToolTipText = "Undo everything WolfX saved into YGO_2020.dat" };
        private readonly ToolStripMenuItem _recent = new("Open &recent");
        private readonly StartScreen _start = new() { Dock = DockStyle.Fill };
        private Page? _shown;
        private GameFolderFiles? _watched;
        private FileSystemWatcher? _exWatcher;
        private readonly System.Windows.Forms.Timer _contentTimer = new() { Interval = 700 };
        private CardsPanel? _cardsPanel;

        /// <summary>The page to show first (WolfX.exe --page "Card genres"), else the Card Manager.</summary>
        public static string? StartPage { get; set; }

        /// <summary>A card to show in the Card Manager at start (WolfX.exe --card 4007).</summary>
        public static int? StartCard { get; set; }

        private CardManager? _cardManager;

        public WolfUI()
        {
            InitializeComponent();
            BuildShell();
            BuildPages();

            GameFolderFiles.CurrentChanged += DataChanged;
            _contentTimer.Tick += (_, _) => { _contentTimer.Stop(); UpdateContentManifest(); };
            _start.OpenArchive = OpenArchive;
            _start.OpenExtracted = OpenExtracted;
            _start.OpenPath = path => OpenPath(path);
            string? remembered = Recent.FirstOrDefault();
            if (remembered != null && GameFolderFiles.Open(remembered) is { } files)
                Use(files, quiet: true);
            else
                ShowStart();
            if (StartCard is int card && _cardManager != null)
            {
                Select("Card Manager");
                Load += (_, _) => _cardManager.ShowCard(card);
            }
        }

        // ---- the window ----

        private void BuildShell()
        {
            Text = "WolfX";
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1280, area.Width), Math.Min(720, area.Height));
            MinimumSize = new Size(Math.Min(900, area.Width), Math.Min(560, area.Height));

            // File menu first; Tools keeps Config, Yami-Yugi and language. What is open is in the title bar.
            var file = new ToolStripMenuItem("&File");
            var openArchive = new ToolStripMenuItem("&Open YGO_2020.dat...", null, (_, _) => OpenArchive())
            {
                ShortcutKeys = Keys.Control | Keys.O, ToolTipText = "The game's archive: everything is read from it and saved back into it",
            };
            var openExtracted = new ToolStripMenuItem("Open &extracted folder...", null, (_, _) => OpenExtracted())
            {
                ShortcutKeys = Keys.Control | Keys.Shift | Keys.O, ToolTipText = "A folder with the files taken out of YGO_2020.dat (any name): saved as loose files there",
            };
            var saveAll = new ToolStripMenuItem("&Save all", null, (_, _) => SaveAll()) { ShortcutKeys = Keys.Control | Keys.Shift | Keys.S };
            var restore = new ToolStripMenuItem("Restore the original &archive...", null, (_, _) => RestoreOriginal());
            var folder = new ToolStripMenuItem("Show the open &folder", null, (_, _) => ShowFolder(GameFolderFiles.Current?.Folder));
            var exFolder = new ToolStripMenuItem("Show the &Yu-Gi-Oh-Ex folder", null, (_, _) => ShowFolder(GameFolderFiles.Current?.ExFolder));
            var exit = new ToolStripMenuItem("E&xit", null, (_, _) => Close());
            file.DropDownItems.AddRange([openArchive, openExtracted, _recent, new ToolStripSeparator(), saveAll, new ToolStripSeparator(), folder, exFolder,
                                         restore, new ToolStripSeparator(), exit]);
            file.DropDownOpening += (_, _) =>
            {
                restore.Enabled = GameFolderFiles.Current?.Archive?.IsModified == true;
                folder.Enabled = exFolder.Enabled = GameFolderFiles.Current != null;
                FillRecent();
            };
            MenuBar.Items.Insert(0, file);
            toolsToolStripMenuItem.DropDownItems.Remove(loadGameToolStripMenuItem);
            _missing.Click += (_, _) => ShowMissing();
            _restore.Click += (_, _) => RestoreOriginal();

            // a folder, YGO_2020.dat or .toc dropped on the window opens it
            AllowDrop = true;
            DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Link : DragDropEffects.None;
            DragDrop += (_, e) =>
            {
                if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } paths)
                    OpenPath(paths[0]);
            };

            _nav.AfterSelect += (_, e) => { if (e.Node?.Tag is Page page) Show(page); };
            _nav.BeforeSelect += (_, e) => { if (e.Node?.Tag is not Page) e.Cancel = true; };

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterWidth = 5 };
            split.Panel1.Controls.Add(_nav);
            split.Panel1.BackColor = _nav.BackColor;
            split.Panel2.Controls.Add(_host);
            split.Panel2.Controls.Add(_pageSaves);
            split.Panel2.Controls.Add(_pageTitle);
            var status = new StatusStrip { SizingGrip = true };
            status.Items.AddRange([_status, _missing, _restore]);

            Controls.Remove(WolfX_TabManager);
            Controls.Add(split);
            Controls.Add(status);
            Controls.SetChildIndex(MenuBar, Controls.Count - 1);   // the menu stays on top
            Load += (_, _) => split.SplitterDistance = 190;
            FormClosing += (_, e) => e.Cancel = !ConfirmDiscard("close WolfX");
        }

        private void BuildPages()
        {
            // Cards
            // The Card Manager shows the same genre / related cards / links editors as their own pages (one instance each).
            var genres = new CardGenreEditor();
            var related = new RelatedCardsEditor();
            var links = new CardLinkEditor();
            var cards = new CardsPanel();
            var manager = new CardManager(genres, related, links, cards);
            manager.OpenCustomCard = id =>
            {
                Select("New cards");
                cards.SelectCard(id);
            };
            Editor("Cards", "Card Manager", manager);
            _cardManager = manager;
            _cardsPanel = cards;
            cards.Warning += SetStatus;
            cards.ShowInCardManager = id =>
            {
                Select("Card Manager");
                manager.ShowCard(id);
            };
            Content("Cards", "New cards", cards, "Additional: Yu-Gi-Oh-Ex\\cards.json + art (needs Yu-Gi-Oh-MoreCards).");
            cards.SaveRequested = () => _pages.First(page => page.Content == cards).Editor!.Save();
            var effects = new EffectsPanel();
            effects.Attach(cards);
            Add("Cards", "Effects", effects, null, "Additional: the cards' effects, saved with cards.json (needs Yu-Gi-Oh-MoreCards and Yu-Gi-Oh-Effects).");
            var library = new EffectLibraryPanel();
            library.UseTemplate = script =>
            {
                if (!effects.ApplyTemplate(script))
                    return false;
                Select("Effects");
                return true;
            };
            Add("Cards", "Effect library", library, null, "The game's cards written in EffectScript, and your own entries: edit them as script or blocks; yours are kept in %APPDATA%\\WolfX\\effect_library.json.");
            Editor("Cards", "Card genres", genres);
            Editor("Cards", "Related cards", related);
            Editor("Cards", "Card links", links);
            Editor("Cards", "Card ids", new CardIdMapEditor());
            Editor("Cards", "Name sort", new CardSortEditor());
            Designed("Cards", Page_PDLimitsManager, "Forbidden & Limited", "Standard: bin\\pd_limits.bin.");

            // Campaign
            Editor("Campaign", "Characters", new CharacterEditor());
            Editor("Campaign", "Decks", new DeckEditor());
            Editor("Campaign", "Story editor", new StoryDuelEditor());
            Editor("Campaign", "Tutorials", new TutorialEditor());

            // Shop
            Add("Shop", "Packs", PackDefinitionsEditor, null, "Standard: main\\packdefdata_<lang>.bin and the packs' cards in packs.zib.");
            Content("Shop", "New packs", new PacksPanel(), "Additional: Yu-Gi-Oh-Ex\\packs.json, cards added to packs and new packs (needs Yu-Gi-Oh-BetterCardShop).");
            Content("Shop", "Unlocks", new UnlocksPanel(), "Additional: Yu-Gi-Oh-Ex\\unlocks.json, cards every profile owns (needs Yu-Gi-Oh-MoreCards).");

            // Text
            Editor("Text", "Text tables", new TextTableEditor());
            Designed("Text", Page_BNDManager, "Strings", "Standard: strings\\Strings_STEAM_<lang>.BND and main\\ui\\credits\\credits.dat.");
            Editor("Text", "How to Play", new HowToPlayEditor());

            // Art and menus
            Editor("Art & menus", "Sprite sheets", new SpriteSheetEditor());
            Editor("Art & menus", "Animlists", new AnimListEditor());
            Content("Art & menus", "Pages", new WolfEx.Designer.PagesPanel(), "Additional: Yu-Gi-Oh-Ex\\pages\\*.json (needs Yu-Gi-Oh-RIX).");
            Content("Art & menus", "Menus", new MenusPanel(), "Additional: Yu-Gi-Oh-Ex\\menus\\menus.json (needs Yu-Gi-Oh-RIX).");

            // Files
            Designed("Files", Page_ZibManager, "Archives (.zib)", "Opens and repacks .zib archives.");
            Designed("Files", Page_YDCManager, "Deck files (.ydc)", "Your own .ydc deck files.");
            Add("Files", "Save editor", SaveEditorFull, null, "Your savegame.dat / savegame-ex.dat.");

            WolfX_TabManager.Dispose();
            foreach (var group in _pages.GroupBy(page => page.Group))
            {
                var node = _nav.Nodes.Add(group.Key);
                node.NodeFont = new Font(_nav.Font, FontStyle.Bold);
                node.ForeColor = SystemColors.GrayText;
                foreach (var page in group)
                    node.Nodes.Add(new TreeNode(page.Title) { Tag = page });
            }
            _nav.ExpandAll();
            Select(StartPage ?? "Card Manager");
            FormLayout.ApplyReadingTabOrder(this);
            PDL_BTN_OpenPDL.Text = STRMAN_BTN_OpenStrings.Text = "Reload";
            PDL_CB_UseCardID.Checked = false;   // names, from the card catalog
        }

        private void Add(string group, string title, Control content, IGameEditor? editor, string savesTo, bool scrolls = false)
        {
            _pages.Add(new Page(group, title, content, editor, editor?.SavesTo ?? savesTo, scrolls ? Fill(content) : Scrolling(content, PageMinimum)));
        }

        private static Control Fill(Control content)
        {
            content.Dock = DockStyle.Fill;
            return content;
        }

        /// <summary>The page fills the space, but never smaller than <paramref name="minimum"/>: below that it scrolls.</summary>
        private static Control Scrolling(Control content, Size minimum)
        {
            var scroll = new ScrollHost { AutoScroll = true, Dock = DockStyle.Fill };
            content.Dock = DockStyle.None;
            content.Location = Point.Empty;
            scroll.Controls.Add(content);
            bool fitting = false;
            scroll.Resize += (_, _) =>
            {
                if (fitting)
                    return;
                fitting = true;
                try
                {
                    var room = scroll.ClientSize;
                    content.Size = new Size(Math.Max(room.Width, minimum.Width), Math.Max(room.Height, minimum.Height));
                    scroll.AutoScrollMinSize = minimum;
                }
                finally { fitting = false; }
            };
            return scroll;
        }

        // a page the Card Manager also shows (ICardFocus) moves between it and its own page, so it is not wrapped in a scroller
        private void Editor<T>(string group, string title, T editor) where T : UserControl, IGameEditor =>
            Add(group, title, editor, editor, editor.SavesTo, scrolls: editor is ICardFocus);

        private void Content(string group, string title, UserControl panel, string savesTo)
        {
            var editor = new ContentPanelEditor((IContentPanel)panel, savesTo);
            Add(group, title, panel, editor, savesTo);
        }

        /// <summary>
        /// A page made in the designer with everything at fixed positions: its controls move to a panel of the size they were drawn for,
        /// which grows with the window (the anchors from FormLayout move them) and scrolls when the window is smaller.
        /// </summary>
        private void Designed(string group, TabPage page, string title, string savesTo)
        {
            FormLayout.AnchorByLayout(page);
            var designed = page.ClientSize;
            var inner = new Panel { Location = Point.Empty, Size = designed };
            foreach (var control in page.Controls.Cast<Control>().Reverse().ToList())
            {
                page.Controls.Remove(control);
                inner.Controls.Add(control);
                inner.Controls.SetChildIndex(control, 0);
            }
            var scroll = new Panel { AutoScroll = true };
            scroll.Controls.Add(inner);
            scroll.Resize += (_, _) => inner.Size = new Size(Math.Max(scroll.ClientSize.Width, designed.Width), Math.Max(scroll.ClientSize.Height, designed.Height));
            Add(group, title, scroll, null, savesTo, scrolls: true);
        }

        private void Select(string title)
        {
            foreach (TreeNode group in _nav.Nodes)
                foreach (TreeNode node in group.Nodes)
                    if (node.Text == title)
                        _nav.SelectedNode = node;
        }

        private void Show(Page page)
        {
            if (_shown == page)
                return;
            if (GameFolderFiles.Current == null)
            {
                // nothing open: the start screen stays, and this page shows once something is
                _shown = null;
                _pending = page;
                return;
            }
            _host.SuspendLayout();
            _host.Controls.Clear();
            if (page.Content is ICardFocus focus)
                focus.CardOnly = false;   // it may have been inside the Card Manager
            _host.Controls.Add(page.Host);
            _host.ResumeLayout();
            _shown = page;
            _pageTitle.Text = page.Title;
            _pageSaves.Text = page.SavesTo;
        }

        private void SetStatus(string text) => _status.Text = text;

        private Page? _pending;

        /// <summary>Nothing open: the start screen (open the .dat, an extracted folder, the Steam install, a recent one).</summary>
        private void ShowStart()
        {
            _host.Controls.Clear();
            var recent = Recent;
            string? steam = GameLocator.FindSteamInstall();
            steam = recent.FirstOrDefault(path => path.Equals(steam, StringComparison.OrdinalIgnoreCase)) ?? steam;   // Steam's registry path is lower case
            _start.Fill(steam, recent);
            _host.Controls.Add(_start);
            _pageTitle.Text = "Open the game data";
            _pageSaves.Text = "WolfX reads and saves the game's own files; your new content goes to JSON in the Yu-Gi-Oh-Ex folder next to them.";
            _pending ??= _nav.SelectedNode?.Tag as Page;
            _shown = null;
            SetStatus("Nothing open yet. Ctrl+O opens YGO_2020.dat, Ctrl+Shift+O an extracted folder; you can also drop either on the window.");
        }

        // ---- the open data ----

        /// <summary>The game's archive: pick YGO_2020.dat (or .toc) in the game folder.</summary>
        private void OpenArchive()
        {
            if (!ConfirmDiscard("open other data"))
                return;
            string? start = GameFolderFiles.Current is { IsExtracted: false } open ? open.Folder : GameLocator.FindSteamInstall();
            using var dialog = new OpenFileDialog
            {
                Title = "Open the game's YGO_2020.dat",
                Filter = "Game archive (YGO_2020.dat, YGO_2020.toc)|YGO_2020.dat;YGO_2020.toc|All files (*.*)|*.*",
                InitialDirectory = start ?? "",
                FileName = "YGO_2020.dat",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                OpenPath(dialog.FileName, confirmed: true);
        }

        /// <summary>An extracted copy of the archive: any folder with the game's bin folder, .zib archives and so on.</summary>
        private void OpenExtracted()
        {
            if (!ConfirmDiscard("open other data"))
                return;
            using var dialog = new FolderBrowserDialog
            {
                Description = "The extracted YGO_2020 folder (any name): the one with bin, main, the .zib archives...",
                UseDescriptionForTitle = true,
                SelectedPath = GameFolderFiles.Current is { IsExtracted: true } open ? open.Folder : "",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                OpenPath(dialog.SelectedPath, confirmed: true);
        }

        /// <summary>Opens a game folder, YGO_2020.dat / .toc, or an extracted folder (a recent entry, a dropped path, a dialog).</summary>
        private void OpenPath(string path, bool confirmed = false)
        {
            if (!confirmed && !ConfirmDiscard("open other data"))
                return;
            if (GameFolderFiles.Open(path) is not { } files)
            {
                bool archive = File.Exists(path) || File.Exists(Path.Combine(path, "YGO_2020.toc"));
                MessageBox.Show(this, archive
                        ? $"{path}\n\nWolfX couldn't read this YGO_2020.toc. Is it from this game (Legacy of the Duelist: Link Evolution)?"
                        : $"{path}\n\nThis has no YGO_2020.toc / YGO_2020.dat and doesn't look like extracted game data (no bin folder or card files).",
                    "Open", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Use(files, quiet: false);
        }

        private static void ShowFolder(string? folder)
        {
            if (folder != null && Directory.Exists(folder))
                System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
        }

        private void FillRecent()
        {
            _recent.DropDownItems.Clear();
            foreach (string path in Recent)
                _recent.DropDownItems.Add(new ToolStripMenuItem(path, null, (_, _) => OpenPath(path)) { Enabled = Directory.Exists(path) });
            _recent.Enabled = _recent.DropDownItems.Count > 0;
        }

        private void Use(GameFolderFiles files, bool quiet)
        {
            Remember(files.Folder);
            State.Path = files.IsExtracted ? files.Folder : null;   // the old designer pages read loose files from here
            StartingCollection.GameFiles.OpenData = files.Read;
            GameFolderFiles.SetCurrent(files);
            var missing = files.MissingRequired();
            if (missing.Count > 0 && !quiet)
                ShowMissing();
        }

        private void DataChanged()
        {
            var files = GameFolderFiles.Current;
            if (files == null)
                return;
            if (_watched != files)
            {
                _watched = files;
                files.Written += written => Reopen(files, written);
            }
            CardCatalog.Reload();
            foreach (var page in _pages)
                page.Editor?.Open(files);

            // the designer-made pages that read the game data open it straight away too
            if (files.Exists(PDLimits.PDLimits.GamePath))
                PDL_BTN_OpenPDL_Click(this, EventArgs.Empty);
            if (files.Exists(global::Types.BND.GamePath((char)State.Language)))
            {
                CREDITS_CheckB_IsCredit.Checked = false;
                STRMAN_BTN_OpenStrings_Click(this, EventArgs.Empty);
            }

            WatchContent(files);
            UpdateContentManifest();

            var missing = files.MissingRequired();
            _missing.Text = $"{missing.Count} required file{(missing.Count == 1 ? "" : "s")} missing";
            _missing.Visible = missing.Count > 0;
            _restore.Visible = files.Archive?.IsModified == true;
            UpdateTitle(files);
            SetStatus(files.IsExtracted
                ? $"Opened the extracted folder {files.Folder}: changes are saved as files there. New content goes to {files.ExFolder}."
                : $"Opened YGO_2020.dat: changes are saved into it (File > Restore undoes them). New content goes to {files.ExFolder}.");
            if (_shown == null)
            {
                // the start screen was up: show the page that was picked
                var page = _pending ?? _nav.SelectedNode?.Tag as Page;
                _pending = null;
                if (page != null)
                    Show(page);
            }
        }

        /// <summary>Any change in the Yu-Gi-Oh-Ex folder (a page saved, or someone edited a file) rewrites content.json a moment later.</summary>
        private void WatchContent(GameFolderFiles files)
        {
            _exWatcher?.Dispose();
            _exWatcher = null;
            Directory.CreateDirectory(files.ExFolder);
            _exWatcher = new FileSystemWatcher(files.ExFolder) { IncludeSubdirectories = true, SynchronizingObject = this, EnableRaisingEvents = true };
            void Changed(object _, FileSystemEventArgs e)
            {
                if (!e.Name?.Equals(ContentManifest.FileName, StringComparison.OrdinalIgnoreCase) ?? true)
                {
                    _contentTimer.Stop();
                    _contentTimer.Start();
                }
            }
            _exWatcher.Created += Changed;
            _exWatcher.Changed += Changed;
            _exWatcher.Deleted += Changed;
            _exWatcher.Renamed += (s, e) => Changed(s, e);
        }

        /// <summary>Rewrites Yu-Gi-Oh-Ex\content.json (the plugins the content needs, which the loader switches on).</summary>
        private void UpdateContentManifest()
        {
            if (GameFolderFiles.Current is { } files)
                ContentManifest.Update(files);
        }

        /// <summary>Another page saved files: the pages that show any of them read them again (unless they have changes of their own).</summary>
        private void Reopen(GameFolderFiles files, IReadOnlyCollection<string> written)
        {
            var set = new HashSet<string>(written.Select(p => p.Replace('/', '\\')), StringComparer.OrdinalIgnoreCase);
            if (set.Any(p => p.StartsWith("bin\\CARD_", StringComparison.OrdinalIgnoreCase)))
                CardCatalog.Reload();
            foreach (var page in _pages)
                if (page.Editor is { Dirty: false } editor && editor.Files.Any(f => set.Contains(f.Replace('/', '\\'))))
                    editor.Open(files);
            _restore.Visible = files.Archive?.IsModified == true;
            UpdateTitle(files);
        }

        /// <summary>The title bar says what is open: "WolfX - YGO_2020.dat (edited) - C:\...\Game" or "WolfX - extracted folder - D:\Data".</summary>
        private void UpdateTitle(GameFolderFiles files)
        {
            string what = files.IsExtracted ? "extracted folder" : files.Archive?.IsModified == true ? "YGO_2020.dat (edited by WolfX)" : "YGO_2020.dat";
            Text = $"WolfX - {what} - {files.Folder}";
        }

        private void ShowMissing()
        {
            if (GameFolderFiles.Current is not { } files)
                return;
            var missing = files.MissingRequired();
            if (missing.Count == 0)
                return;
            MessageBox.Show(this, $"{files.Folder} doesn't have these files, so the pages that use them stay empty:\n\n" + string.Join("\n", missing) +
                "\n\nAn extracted folder needs the game's bin folder and the .zib archives (card art, decks, packs) from YGO_2020.dat.",
                "Required files not found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void SaveAll()
        {
            if (GameFolderFiles.Current is not { } files)
            {
                SetStatus("Nothing is open to save: open YGO_2020.dat or an extracted folder first.");
                return;
            }
            Directory.CreateDirectory(files.ExFolder);
            int saved = 0;
            foreach (var page in _pages.Where(page => page.Editor is { Dirty: true }))
            {
                if (!page.Editor!.Save())
                {
                    Select(page.Title);
                    SetStatus($"Not saved: fix the {page.Title} page first.");
                    return;
                }
                saved++;
            }
            SetStatus(saved == 0 ? "Nothing to save." : $"Saved {saved} page{(saved == 1 ? "" : "s")}. Restart the game to see the changes.");
        }

        private bool ConfirmDiscard(string doing)
        {
            var dirty = _pages.Where(page => page.Editor?.Dirty == true).Select(page => page.Title).ToList();
            if (dirty.Count == 0)
                return true;
            var answer = MessageBox.Show(this, $"These pages have changes that are not saved: {string.Join(", ", dirty)}.\n\nSave them before you {doing}?",
                "WolfX", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel)
                return false;
            if (answer == DialogResult.Yes)
                SaveAll();
            return true;
        }

        private void RestoreOriginal()
        {
            if (GameFolderFiles.Current is not { Archive.IsModified: true } files)
                return;
            if (MessageBox.Show(this, "Put the game's original YGO_2020.toc back and cut YGO_2020.dat back to its original size?\n\n" +
                    "Everything WolfX saved into the archive is undone. Your Yu-Gi-Oh-Ex JSON files are not touched.",
                    "Restore the original archive", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            try
            {
                files.RestoreOriginal();
                GameFolderFiles.SetCurrent(files);
                UpdateTitle(files);
                SetStatus("The archive is the game's original again.");
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Restore the original archive", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string RememberedFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfX", "data.folder");

        /// <summary>What was opened before, newest first (the first one opens at start).</summary>
        private static List<string> Recent
        {
            get
            {
                try
                {
                    return File.Exists(RememberedFile)
                        ? File.ReadAllLines(RememberedFile).Select(line => line.Trim()).Where(line => line.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                        : [];
                }
                catch (IOException) { return []; }
            }
        }

        private static void Remember(string folder)
        {
            try
            {
                var list = Recent.Where(path => !path.Equals(folder, StringComparison.OrdinalIgnoreCase)).Prepend(folder).Take(8);
                Directory.CreateDirectory(Path.GetDirectoryName(RememberedFile)!);
                File.WriteAllLines(RememberedFile, list);
            }
            catch (IOException) { /* remembering the folder is a convenience only */ }
        }

        // ---- designer events ----

        private void WOLFUI_TOOLITEM_LoadGame_Click(object sender, EventArgs e) => OpenArchive();

        private void WOLFUI_TOOLITEM_OpenConfigEditor_Click(object sender, EventArgs e)
        {
            var Config = new WolfX.File_Type_UI.Config();
            Config.ShowDialog();
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
        }

        /// <summary>A WolfEx page (its own JSON file in Yu-Gi-Oh-Ex) as an <see cref="IGameEditor"/>.</summary>
        private sealed class ContentPanelEditor(IContentPanel panel, string savesTo) : IGameEditor
        {
            private GameFolderFiles? _files;

            public void Open(GameFolderFiles files)
            {
                _files = files;
                panel.LoadFrom(files.ExFolder, files.GameFolder);
                panel.MarkSaved();
            }

            public bool Dirty => panel.Dirty;

            public bool Save()
            {
                if (_files == null)
                    return false;
                Directory.CreateDirectory(_files.ExFolder);
                if (!panel.SaveTo(_files.ExFolder))
                    return false;
                panel.MarkSaved();
                return true;
            }

            public IReadOnlyCollection<string> Files => [];

            public string SavesTo => savesTo;
        }
    }
}
