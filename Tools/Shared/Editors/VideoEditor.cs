using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Types;
using WolfX.Types;
using static Wolf.Editors.TriggerChoices;
using Uses = Wolf.Editors.TriggerUses;

namespace Wolf.Editors
{
    /// <summary>
    /// The Videos page: the mp4 files in Yu-Gi-Oh-Ex\movies and Yu-Gi-Oh-Ex\videos.json, for Yu-Gi-Oh-Media (docs/MediaPlugin.md "Videos").
    /// A video named after a card id is that card's summon cut-in (replacing the game's for the 23 cards that have one), exodia.mp4 replaces
    /// the Exodia finish; any video can play on a duel event ("In duels": the same conditions as Voice Over lines - when I summon these
    /// cards, when my opponent activates a Trap...), at startup, before / after a story duel, or from a menu button ({"video": "name"}).
    /// </summary>
    public sealed class VideoEditor : UserControl, IGameEditor
    {
        public const string FileName = "videos.json";

        // The game's own movies (movies\<name>.bik): summon cut-ins by card id, and the Exodia finish.
        private static readonly int[] GameCutIns = [4007, 4021, 4041, 4088, 4737, 4766, 6345, 6653, 7344, 7734, 7735, 9575, 9656, 9708, 10569,
                                                    10658, 11213, 11385, 11721, 12628, 13036, 13258, 13927];

        /// <summary>The duel events a video can play on (videos.json "duel" "when"), as the editor says them; "who" is its own column.</summary>
        private static readonly (string Key, string Text, Uses Uses, bool Sided)[] WhenChoices =
        [
            ("duelStart", "the duel starts", Uses.None, false),
            ("turnStart", "a turn starts (LP at or below Amount; 0 = any)", Uses.Amount | Uses.Standing, true),
            ("battlePhase", "the Battle Phase starts", Uses.None, true),
            ("summon", "a monster is summoned", Uses.Cards | Uses.How, true),
            ("attack", "a monster attacks", Uses.Cards, true),
            ("directAttack", "a monster attacks directly", Uses.Cards, true),
            ("lethalAttack", "an attack will win the duel if it lands", Uses.Cards, true),
            ("activate", "a card or effect is activated", Uses.Cards | Uses.Kind | Uses.Chain, true),
            ("set", "a card is Set", Uses.Cards | Uses.Kind, true),
            ("destroyed", "a card is destroyed", Uses.Cards | Uses.Kind, true),
            ("damage", "damage is taken (at least Amount)", Uses.Amount, true),
            ("lowLP", "LP fall to Amount or below", Uses.Amount, true),
            ("win", "the duel is won", Uses.Reason, true),
            ("lose", "the duel is lost", Uses.Reason, true),
            ("draw", "the duel is a draw", Uses.None, false),
        ];

        /// <summary>videos.json "who": about the player at the keyboard, their opponent, or either.</summary>
        private static readonly (string Key, string Text)[] WhoChoices = [("me", "me (the player)"), ("opponent", "my opponent"), ("any", "either duelist")];

        private static readonly JsonSerializerOptions WriteOptions = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true };
        private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        private sealed class DuelRow
        {
            public int Duel { get; set; }
            public string Before { get; set; } = "";
            public string After { get; set; } = "";
        }

        /// <summary>One videos.json "duel" entry.</summary>
        private sealed class EventRow
        {
            public string When { get; set; } = "summon";
            public string Who { get; set; } = "me";
            public int Amount { get; set; }
            public List<int> CardList { get; set; } = [];
            public string Cards => CardList.Count == 0 ? "" : string.Join(", ", CardList.Select(CardCatalog.NameOf));
            public string How { get; set; } = "any";
            public string Reason { get; set; } = "any";
            public string Kind { get; set; } = "any";
            public string Chain { get; set; } = "either";
            public string Standing { get; set; } = "any";
            public int Duelist { get; set; } = -1;
            public int Against { get; set; } = -1;
            public List<string> VideoList { get; set; } = [];
            public string Videos => VideoList.Count == 0 ? "(double-click to pick videos)" : string.Join(", ", VideoList);
            public int Chance { get; set; } = 100;
            public string Once { get; set; } = "duel";
        }

        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ListView _videos = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, MultiSelect = false };
        private readonly CheckedListBox _startup = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        private readonly ListBox _sections = new() { Dock = DockStyle.Fill, IntegralHeight = false, ItemHeight = 28, DrawMode = DrawMode.OwnerDrawFixed, Font = new Font("Segoe UI", 10f) };
        private readonly Panel _content = new() { Dock = DockStyle.Fill };
        private readonly List<Control> _pages = [];
        private readonly ListView _storyList = new() { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, MultiSelect = false };
        private readonly TextBox _storyFilter = new() { Dock = DockStyle.Top, PlaceholderText = "Find a story duel..." };
        private readonly CheckBox _storyOnlyUsed = new() { Dock = DockStyle.Top, Text = "Only duels with videos", AutoSize = true, Padding = new Padding(2, 4, 0, 4) };
        private readonly Label _storyHeading = new() { Dock = DockStyle.Top, Height = 30, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 11f, FontStyle.Bold), AutoEllipsis = true };
        private readonly ComboBox _storyBefore = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly ComboBox _storyAfter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private bool _storyFilling;
        private readonly DataGridView _events = new()
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, EditMode = DataGridViewEditMode.EditOnEnter,
        };
        private readonly Label _sentence = new() { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(4), Font = new Font("Segoe UI", 9.5f, FontStyle.Italic) };
        private readonly BindingList<DuelRow> _duelRows = [];
        private readonly BindingList<EventRow> _eventRows = [];
        private readonly BindingList<Choice<int>> _characterItems = [];
        private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly Dictionary<int, string> _duelNames = [];
        private readonly Dictionary<int, string> _characterNames = [];
        private GameFolderFiles? _files;
        private string _savedJson = "";

        public IReadOnlyCollection<string> Files => [StoryDuelTable.GamePath('E'), CharacterTable.GamePath('E')];

        public string SavesTo => $"Additional: Yu-Gi-Oh-Ex\\{FileName} and the mp4 files in Yu-Gi-Oh-Ex\\movies (needs Yu-Gi-Oh-Media).";

        public bool Dirty => _files != null && Build().ToJsonString() != _savedJson;

        private string MoviesFolder => _files == null ? "" : Path.Combine(Path.GetDirectoryName(_files.ExPath(FileName))!, "movies");

        public VideoEditor()
        {
            _tools.Items.Add(new ToolStripButton("Save", null, (_, _) => Save()) { ToolTipText = "Save videos.json (Ctrl+S)" });
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripButton("Add video...", null, (_, _) => AddVideo(byCard: false)) { ToolTipText = "Copy an mp4 into the movies folder under a name you choose" });
            _tools.Items.Add(new ToolStripButton("Add card cut-in...", null, (_, _) => AddVideo(byCard: true)) { ToolTipText = "Copy an mp4 in as a card's summon cut-in (named by the card's id)" });
            _tools.Items.Add(new ToolStripButton("Rename...", null, (_, _) => RenameVideo()));
            _tools.Items.Add(new ToolStripButton("Delete", null, (_, _) => DeleteVideo()) { ToolTipText = "Delete the selected mp4 from the movies folder" });
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripButton("Play", null, (_, _) => PlayVideo()) { ToolTipText = "Open the selected video in Windows' player" });
            _tools.Items.Add(new ToolStripButton("Open movies folder", null, (_, _) => { if (_files != null) { Directory.CreateDirectory(MoviesFolder); Process.Start(new ProcessStartInfo(MoviesFolder) { UseShellExecute = true }); } }));

            _videos.Columns.Add("Video", 180);
            _videos.Columns.Add("Plays", 520);
            _videos.Columns.Add("Size", 80, HorizontalAlignment.Right);
            _videos.DoubleClick += (_, _) => PlayVideo();

            // ---- In duels ----
            BuildEventColumns();
            var addEvent = new Button { Text = "Add duel video", AutoSize = true };
            addEvent.Click += (_, _) => AddEvent();
            var removeEvent = new Button { Text = "Remove", AutoSize = true };
            removeEvent.Click += (_, _) =>
            {
                _events.EndEdit();
                foreach (var row in _events.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<EventRow>().ToList())
                    _eventRows.Remove(row);
                RefreshPlays();
            };
            var eventButtons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2) };
            eventButtons.Controls.AddRange([addEvent, removeEvent]);
            var eventsPage = Page("In duels", "Plays a video full screen when something happens in a duel - the same choices as Voice Over lines. " +
                                              "The duel waits while it plays; Esc or a click skips it.", _events, _sentence, eventButtons);

            // ---- Game start ----
            _startup.ItemCheck += (_, _) => BeginInvoke(() => { RefreshPlays(); _sections.Invalidate(); });
            var up = new Button { Text = "Move up", AutoSize = true };
            up.Click += (_, _) => MoveStartup(-1);
            var down = new Button { Text = "Move down", AutoSize = true };
            down.Click += (_, _) => MoveStartup(1);
            var order = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(2) };
            order.Controls.AddRange([up, down]);
            var startupPage = Page("Game start", "Ticked videos play full screen as the game starts, top to bottom.", _startup, order);

            // ---- Story duels: every duel on the left, its two videos on the right ----
            _storyList.Columns.Add("Story duel", 260);
            _storyList.Columns.Add("Before", 130);
            _storyList.Columns.Add("After", 130);
            _storyFilter.TextChanged += (_, _) => FillStoryList();
            _storyOnlyUsed.CheckedChanged += (_, _) => FillStoryList();
            _storyList.SelectedIndexChanged += (_, _) => ShowStoryDuel();
            _storyBefore.SelectedIndexChanged += (_, _) => SetStoryVideo(before: true);
            _storyAfter.SelectedIndexChanged += (_, _) => SetStoryVideo(before: false);
            var storyLeft = new Panel { Dock = DockStyle.Fill };
            storyLeft.Controls.Add(_storyList);
            storyLeft.Controls.Add(_storyOnlyUsed);
            storyLeft.Controls.Add(_storyFilter);
            var details = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(4) };
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            details.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Button PlayButton(ComboBox box) { var b = new Button { Text = "Play", AutoSize = true }; b.Click += (_, _) => PlayVideo(box.SelectedItem as string); return b; }
            Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 8, 8, 3) };
            details.Controls.Add(Caption("Before the duel starts:"), 0, 0);
            details.Controls.Add(_storyBefore, 1, 0);
            details.Controls.Add(PlayButton(_storyBefore), 2, 0);
            details.Controls.Add(Caption("After it ends:"), 0, 1);
            details.Controls.Add(_storyAfter, 1, 1);
            details.Controls.Add(PlayButton(_storyAfter), 2, 1);
            var clear = new Button { Text = "Clear both", AutoSize = true };
            clear.Click += (_, _) => { _storyBefore.SelectedIndex = _storyAfter.SelectedIndex = 0; };
            details.Controls.Add(clear, 1, 2);
            var storyRight = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 0, 0, 0) };
            storyRight.Controls.Add(new Label { Dock = DockStyle.Top, Height = 44, Text = "Both play full screen: \"before\" as the duel's engine starts, \"after\" at its result. Esc or a click skips." });
            storyRight.Controls.Add(details);
            storyRight.Controls.Add(_storyHeading);
            var storySplit = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel2 };
            storySplit.Panel1.Controls.Add(storyLeft);
            storySplit.Panel2.Controls.Add(storyRight);
            storySplit.SizeChanged += (_, _) => { if (storySplit.Width > 700) storySplit.SplitterDistance = storySplit.Width - 380; };
            var storyPage = Page("Story duels", "Pick a story duel, then the videos around it.", storySplit);

            var videosPage = Page("Videos", "The mp4s in Yu-Gi-Oh-Ex\\movies. A video named after a card id (e.g. 4007) is that card's summon cut-in; exodia replaces the " +
                                            "Exodia finish. Any video can also play from a menu button (Menus page: \"play a video\"). H.264 mp4 always works.", _videos);

            _pages.AddRange([videosPage, eventsPage, startupPage, storyPage]);
            foreach (var page in _pages)
                _content.Controls.Add(page);
            _sections.Items.AddRange(["Videos", "In duels", "Game start", "Story duels"]);
            _sections.DrawItem += DrawSection;
            _sections.SelectedIndexChanged += (_, _) =>
            {
                for (int i = 0; i < _pages.Count; i++)
                    _pages[i].Visible = i == _sections.SelectedIndex;
            };
            _sections.SelectedIndex = 0;

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 170 };
            split.Panel1.Controls.Add(_sections);
            split.Panel2.Controls.Add(_content);
            Controls.Add(split);
            Controls.Add(_tools);
            Controls.Add(_status);
            SetEditable(false);
        }

        private void SetEditable(bool editable)
        {
            foreach (ToolStripItem item in _tools.Items)
                item.Enabled = editable;
            _videos.Enabled = _startup.Enabled = _storyList.Enabled = _storyBefore.Enabled = _storyAfter.Enabled = _events.Enabled = _sections.Enabled = editable;
        }

        /// <summary>A sidebar page: its title and help line over the controls (the first fills the rest).</summary>
        private static Control Page(string title, string help, params Control[] controls)
        {
            var page = new Panel { Dock = DockStyle.Fill, Visible = false, Padding = new Padding(6, 4, 4, 4) };
            foreach (var control in controls)
                page.Controls.Add(control);
            page.Controls.Add(new Label { Dock = DockStyle.Top, Height = 36, Text = help, ForeColor = SystemColors.GrayText });
            page.Controls.Add(new Label { Dock = DockStyle.Top, Height = 30, Text = title, Font = new Font("Segoe UI", 13f, FontStyle.Bold) });
            return page;
        }

        /// <summary>The sidebar: each section with how much it holds.</summary>
        private void DrawSection(object? sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            e.DrawBackground();
            string count = e.Index switch
            {
                0 => _videos.Items.Count.ToString(),
                1 => _eventRows.Count.ToString(),
                2 => _startup.CheckedItems.Count.ToString(),
                _ => _duelRows.Count(r => r.Before.Length > 0 || r.After.Length > 0).ToString(),
            };
            var bounds = e.Bounds;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            var color = selected ? SystemColors.HighlightText : SystemColors.ControlText;
            TextRenderer.DrawText(e.Graphics, _sections.Items[e.Index].ToString(), e.Font, new Rectangle(bounds.X + 8, bounds.Y, bounds.Width - 50, bounds.Height), color, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            TextRenderer.DrawText(e.Graphics, count, e.Font, new Rectangle(bounds.Right - 46, bounds.Y, 40, bounds.Height), selected ? color : SystemColors.GrayText, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }

        private void MoveStartup(int by)
        {
            int index = _startup.SelectedIndex, to = index + by;
            if (index < 0 || to < 0 || to >= _startup.Items.Count)
                return;
            object item = _startup.Items[index];
            bool on = _startup.GetItemChecked(index);
            _startup.Items.RemoveAt(index);
            _startup.Items.Insert(to, item);
            _startup.SetItemChecked(to, on);
            _startup.SelectedIndex = to;
        }

        // ---- story duels ----

        private DuelRow? StoryRow(int duel) => _duelRows.FirstOrDefault(r => r.Duel == duel);

        private int? SelectedDuel => _storyList.SelectedItems.Count > 0 ? (int)_storyList.SelectedItems[0].Tag! : null;

        private void FillStoryList()
        {
            int? keep = SelectedDuel;
            string filter = _storyFilter.Text.Trim();
            _storyList.BeginUpdate();
            _storyList.Items.Clear();
            foreach (var (id, name) in _duelNames.OrderBy(p => p.Key))
            {
                var row = StoryRow(id);
                bool used = row != null && (row.Before.Length > 0 || row.After.Length > 0);
                if (_storyOnlyUsed.Checked && !used)
                    continue;
                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                var item = new ListViewItem([name, row?.Before ?? "", row?.After ?? ""]) { Tag = id };
                if (used)
                    item.Font = new Font(_storyList.Font, FontStyle.Bold);
                _storyList.Items.Add(item);
                if (id == keep)
                    item.Selected = true;
            }
            _storyList.EndUpdate();
            ShowStoryDuel();
        }

        private void ShowStoryDuel()
        {
            _storyFilling = true;
            var choices = new List<string> { "" };
            choices.AddRange(VideoNames());
            foreach (var box in new[] { _storyBefore, _storyAfter })
            {
                box.Items.Clear();
                box.Items.AddRange([.. choices]);
            }
            if (SelectedDuel is int duel)
            {
                var row = StoryRow(duel);
                _storyHeading.Text = _duelNames.GetValueOrDefault(duel, duel.ToString());
                _storyBefore.SelectedItem = choices.Contains(row?.Before ?? "") ? row?.Before ?? "" : "";
                _storyAfter.SelectedItem = choices.Contains(row?.After ?? "") ? row?.After ?? "" : "";
                _storyBefore.Enabled = _storyAfter.Enabled = true;
            }
            else
            {
                _storyHeading.Text = "Pick a story duel";
                _storyBefore.SelectedIndex = _storyAfter.SelectedIndex = 0;
                _storyBefore.Enabled = _storyAfter.Enabled = false;
            }
            _storyFilling = false;
        }

        private void SetStoryVideo(bool before)
        {
            if (_storyFilling || SelectedDuel is not int duel)
                return;
            string value = ((before ? _storyBefore : _storyAfter).SelectedItem as string) ?? "";
            var row = StoryRow(duel);
            if (row == null)
                _duelRows.Add(row = new DuelRow { Duel = duel });
            if (before) row.Before = value; else row.After = value;
            if (row.Before.Length == 0 && row.After.Length == 0)
                _duelRows.Remove(row);
            if (_storyList.SelectedItems.Count > 0)
            {
                var item = _storyList.SelectedItems[0];
                item.SubItems[1].Text = row.Before;
                item.SubItems[2].Text = row.After;
                item.Font = new Font(_storyList.Font, row.Before.Length > 0 || row.After.Length > 0 ? FontStyle.Bold : FontStyle.Regular);
            }
            RefreshPlays();
            _sections.Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                Save();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---- the "In duels" grid ----

        private static Uses UsesOf(string when) => WhenChoices.FirstOrDefault(e => e.Key == when).Uses;

        private static bool Sided(string when) => WhenChoices.FirstOrDefault(e => e.Key == when).Sided;

        /// <summary>The event setting a grid column belongs to, or null for the columns every entry has.</summary>
        private static Uses? ColumnUse(string property) => property switch
        {
            nameof(EventRow.Amount) => Uses.Amount,
            nameof(EventRow.Cards) => Uses.Cards,
            nameof(EventRow.How) => Uses.How,
            nameof(EventRow.Reason) => Uses.Reason,
            nameof(EventRow.Kind) => Uses.Kind,
            nameof(EventRow.Chain) => Uses.Chain,
            nameof(EventRow.Standing) => Uses.Standing,
            _ => null,
        };

        private void BuildEventColumns()
        {
            DataGridViewComboBoxColumn Combo(string header, string property, (string Key, string Text)[] items, int width, string tip) => new()
            {
                HeaderText = header, DataPropertyName = property, DataSource = items.Select(k => new Choice<string>(k.Key, k.Text)).ToList(),
                ValueMember = nameof(Choice<string>.Key), DisplayMember = nameof(Choice<string>.Text), Width = width, FlatStyle = FlatStyle.Flat, ToolTipText = tip,
            };
            DataGridViewComboBoxColumn Characters(string header, string property, string tip) => new()
            {
                HeaderText = header, DataPropertyName = property, DataSource = _characterItems,
                ValueMember = nameof(Choice<int>.Key), DisplayMember = nameof(Choice<int>.Text), Width = 150, FlatStyle = FlatStyle.Flat, ToolTipText = tip,
            };
            _events.Columns.AddRange(
            [
                Combo("When", nameof(EventRow.When), [.. WhenChoices.Select(e => (e.Key, e.Text))], 250, "What happens in the duel"),
                Combo("Who", nameof(EventRow.Who), WhoChoices, 120, "Whose summon / attack / turn...: the player's, their opponent's, or either"),
                new DataGridViewTextBoxColumn { HeaderText = "Amount", DataPropertyName = nameof(EventRow.Amount), Width = 60, ToolTipText = "Damage: at least this much. LP: this many or fewer. Turn start: LP at or below (0 = any)." },
                new DataGridViewTextBoxColumn { HeaderText = "Cards (double-click)", DataPropertyName = nameof(EventRow.Cards), ReadOnly = true, Width = 180, ToolTipText = "Only these cards (none = any)" },
                Combo("Summoned", nameof(EventRow.How), SummonKinds, 150, "Summons: only when the monster is summoned this way"),
                Combo("Card kind", nameof(EventRow.Kind), CardKinds, 95, "Activate / Set / destroyed: only this kind of card"),
                Combo("Chain", nameof(EventRow.Chain), ChainChoices, 105, "Activate: only when it starts a chain, or only in response to another card"),
                Combo("LP standing", nameof(EventRow.Standing), Standings, 105, "Turn start: only when that side is winning / losing / even on LP"),
                Combo("Win by", nameof(EventRow.Reason), WinReasons, 170, "Win / lose: only when the duel is won this way"),
                Characters("Duelist", nameof(EventRow.Duelist), "Only when this character is the one doing it"),
                Characters("Against", nameof(EventRow.Against), "Only when the other duelist is this character"),
                new DataGridViewTextBoxColumn { HeaderText = "Videos (double-click)", DataPropertyName = nameof(EventRow.Videos), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 160, ToolTipText = "One is picked each time; none repeats until all have played" },
                new DataGridViewTextBoxColumn { HeaderText = "Chance %", DataPropertyName = nameof(EventRow.Chance), Width = 65 },
                Combo("Once", nameof(EventRow.Once), OnceChoices, 105, "Once per duel, once per turn, or every time"),
            ]);
            _events.DataSource = _eventRows;
            _events.DataError += (_, e) =>
            {
                e.ThrowException = false;
                _status.Text = $"Row {e.RowIndex + 1}: {e.Exception?.Message}";
            };
            _events.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex < 0 || _events.Rows[e.RowIndex].DataBoundItem is not EventRow row)
                    return;
                string column = _events.Columns[e.ColumnIndex].DataPropertyName;
                if (column == nameof(EventRow.Videos))
                    PickVideos(row);
                else if (column == nameof(EventRow.Cards) && UsesOf(row.When).HasFlag(Uses.Cards))
                    PickCards(row);
            };
            bool Greyed(EventRow row, string property) =>
                (ColumnUse(property) is Uses use && !UsesOf(row.When).HasFlag(use)) || (property == nameof(EventRow.Who) && !Sided(row.When));
            _events.CellBeginEdit += (_, e) =>
            {
                if (_events.Rows[e.RowIndex].DataBoundItem is EventRow row && Greyed(row, _events.Columns[e.ColumnIndex].DataPropertyName))
                    e.Cancel = true;
            };
            _events.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0 || _events.Rows[e.RowIndex].DataBoundItem is not EventRow row)
                    return;
                string property = _events.Columns[e.ColumnIndex].DataPropertyName;
                if (Greyed(row, property))
                {
                    if (e.CellStyle != null)
                    {
                        e.CellStyle.BackColor = SystemColors.Control;
                        e.CellStyle.ForeColor = SystemColors.GrayText;
                    }
                    if (property is nameof(EventRow.Amount) or nameof(EventRow.Cards))
                    {
                        e.Value = "";
                        e.FormattingApplied = true;
                    }
                }
                else if (property == nameof(EventRow.Cards) && row.CardList.Count == 0)
                {
                    e.Value = "(any card; double-click to pick)";
                    e.FormattingApplied = true;
                }
            };
            _events.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (_events.IsCurrentCellDirty && _events.CurrentCell is DataGridViewComboBoxCell or DataGridViewCheckBoxCell)
                    _events.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _events.CellValueChanged += (_, _) => { ShowSentence(); _events.Invalidate(); RefreshPlays(); };
            _events.SelectionChanged += (_, _) => ShowSentence();
        }

        private EventRow? SelectedEvent => _events.CurrentRow?.DataBoundItem as EventRow;

        private string CharacterName(int id) => _characterNames.GetValueOrDefault(id, $"character {id}");

        /// <summary>When an entry plays, as words ("I summon Blue-Eyes White Dragon (Special Summon)").</summary>
        private string WhenText(EventRow row)
        {
            string cards = CardsText(row.CardList);
            bool sided = Sided(row.When);
            string subject = row.Who switch { "opponent" => "my opponent", "any" => "either duelist", _ => "I" };
            string verb(string mine, string theirs) => row.Who == "me" ? mine : theirs;
            string owner = row.Who switch { "opponent" => "my opponent's", "any" => "a", _ => "my" };
            string text = row.When switch
            {
                "duelStart" => "the duel starts",
                "draw" => "the duel is a draw",
                "turnStart" => $"{owner} turn starts" + (row.Amount > 0 ? $" with LP at {row.Amount} or below" : ""),
                "battlePhase" => $"{subject} {verb("enter", "enters")} the Battle Phase",
                "summon" => $"{subject} {verb("summon", "summons")} {(row.CardList.Count == 0 ? "a monster" : cards)}" + (row.How != "any" ? $" ({TextOf(SummonKinds, row.How)})" : ""),
                "attack" => $"{subject} {verb("attack", "attacks")}" + (row.CardList.Count > 0 ? $" with {cards}" : ""),
                "directAttack" => $"{subject} {verb("attack", "attacks")} directly" + (row.CardList.Count > 0 ? $" with {cards}" : ""),
                "lethalAttack" => $"{owner} attack will win the duel if it lands" + (row.CardList.Count > 0 ? $" ({cards})" : ""),
                "activate" => $"{subject} {verb("activate", "activates")} {(row.CardList.Count > 0 ? cards : row.Kind != "any" ? TextOf(CardKinds, row.Kind) : "a card or effect")}" +
                              (row.Chain == "response" ? " in response to another card" : row.Chain == "start" ? " to start a chain" : ""),
                "set" => $"{subject} {verb("Set", "Sets")} {(row.CardList.Count > 0 ? cards : row.Kind != "any" ? TextOf(CardKinds, row.Kind) : "a card")}",
                "destroyed" => $"{owner} {(row.CardList.Count > 0 ? cards : row.Kind != "any" ? TextOf(CardKinds, row.Kind).Replace("a ", "") : "card")} is destroyed",
                "damage" => $"{subject} {verb("take", "takes")} at least {row.Amount} damage",
                "lowLP" => $"{owner} LP fall to {row.Amount} or below",
                "win" => $"{subject} {verb("win", "wins")}" + (row.Reason != "any" ? $" by {TextOf(WinReasons, row.Reason)}" : ""),
                "lose" => $"{subject} {verb("lose", "loses")}" + (row.Reason != "any" ? $" ({TextOf(WinReasons, row.Reason)})" : ""),
                _ => row.When,
            };
            if (sided && row.When == "turnStart" && row.Standing != "any")
                text += $" while {(row.Who == "me" ? "I am" : "they are")} {TextOf(Standings, row.Standing)}";
            return text;
        }

        /// <summary>The selected entry as a sentence, so its meaning is never a guess.</summary>
        private void ShowSentence()
        {
            if (SelectedEvent is not { } row)
            {
                _sentence.Text = "Add a duel video: pick when it plays, then double-click Videos.";
                return;
            }
            string videos = row.VideoList.Count switch { 0 => "nothing yet (no videos)", 1 => row.VideoList[0], _ => $"one of {row.VideoList.Count} videos" };
            var parts = new List<string> { $"Plays {videos} full screen when {WhenText(row)}" };
            if (row.Duelist >= 0)
                parts.Add($"only when {CharacterName(row.Duelist)} does it");
            if (row.Against >= 0)
                parts.Add($"only against {CharacterName(row.Against)}");
            if (row.Chance < 100)
                parts.Add($"{row.Chance}% of the time");
            parts.Add(row.Once switch { "turn" => "at most once per turn", "never" => "every time", _ => "at most once per duel" });
            _sentence.Text = string.Join(", ", parts) + ".";
        }

        private static string CardsText(List<int> cards) => cards.Count switch
        {
            0 => "",
            1 => CardCatalog.NameOf(cards[0]),
            _ => string.Join(", ", cards.Take(cards.Count - 1).Select(CardCatalog.NameOf)) + " or " + CardCatalog.NameOf(cards[^1]),
        };

        private void AddEvent()
        {
            _events.EndEdit();
            var row = new EventRow { When = SelectedEvent?.When ?? "summon" };
            if (SelectedName is { } name)
                row.VideoList.Add(name);
            _eventRows.Add(row);
            _events.CurrentCell = _events.Rows[_eventRows.Count - 1].Cells[0];
            RefreshPlays();
            _status.Text = row.VideoList.Count > 0 ? $"Added: plays {row.VideoList[0]}. Now choose when." : "Added. Double-click Videos to pick what plays.";
        }

        private void PickCards(EventRow row)
        {
            _events.EndEdit();
            using var picker = new CardPickerDialog(CardCatalog.Get(this), "Which cards (none = any)", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;
            row.CardList = picker.Result.Select(pick => pick.Card.Id).Distinct().ToList();
            _eventRows.ResetItem(_eventRows.IndexOf(row));
            ShowSentence();
        }

        /// <summary>Ticks the movies folder's videos the entry picks from.</summary>
        private void PickVideos(EventRow row)
        {
            _events.EndEdit();
            var names = VideoNames();
            if (names.Length == 0)
            {
                _status.Text = "No videos yet: use Add video... first.";
                return;
            }
            using var form = new Form { Text = "Videos to play", FormBorderStyle = FormBorderStyle.SizableToolWindow, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(320, 360) };
            var list = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
            foreach (string name in names)
                list.Items.Add(name, row.VideoList.Contains(name, StringComparer.OrdinalIgnoreCase));
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            buttons.Controls.AddRange([cancel, ok]);
            form.Controls.Add(list);
            form.Controls.Add(buttons);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            if (form.ShowDialog(this) != DialogResult.OK)
                return;
            row.VideoList = list.CheckedItems.Cast<string>().ToList();
            _eventRows.ResetItem(_eventRows.IndexOf(row));
            ShowSentence();
            RefreshPlays();
        }

        // ---- the videos ----

        private string[] VideoNames() =>
            Directory.Exists(MoviesFolder) ? Directory.GetFiles(MoviesFolder, "*.mp4").Select(f => Path.GetFileNameWithoutExtension(f)!).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray() : [];

        private string? SelectedName => _videos.SelectedItems.Count > 0 ? _videos.SelectedItems[0].Text : null;

        /// <summary>What a video does, from its name and videos.json.</summary>
        private string Plays(string name)
        {
            var uses = new List<string>();
            if (string.Equals(name, "exodia", StringComparison.OrdinalIgnoreCase))
                uses.Add("replaces the Exodia finish");
            else if (int.TryParse(name, out int card) && card > 0)
                uses.Add(GameCutIns.Contains(card) ? $"replaces {CardCatalog.NameOf(card)}'s summon cut-in" : $"{CardCatalog.NameOf(card)}'s summon cut-in");
            foreach (var row in _eventRows.Where(r => r.VideoList.Contains(name)))
                uses.Add($"when {WhenText(row)}");
            if (_startup.CheckedItems.Cast<string>().Contains(name))
                uses.Add("at startup");
            foreach (var row in _duelRows)
            {
                if (row.Before == name)
                    uses.Add($"before story duel {row.Duel}");
                if (row.After == name)
                    uses.Add($"after story duel {row.Duel}");
            }
            uses.Add($"menu action {{\"video\": \"{name}\"}}");
            return string.Join("; ", uses);
        }

        private void FillVideos(string? select = null)
        {
            var names = VideoNames();
            _videos.BeginUpdate();
            _videos.Items.Clear();
            foreach (string name in names)
            {
                long size = new FileInfo(Path.Combine(MoviesFolder, name + ".mp4")).Length;
                var item = new ListViewItem([name, Plays(name), $"{size / 1024.0 / 1024.0:0.0} MB"]);
                _videos.Items.Add(item);
                if (name == select)
                    item.Selected = true;
            }
            _videos.EndUpdate();

            var startup = _startup.CheckedItems.Cast<string>().ToList();
            _startup.Items.Clear();
            foreach (string name in startup.Where(names.Contains).Concat(names.Except(startup)))
                _startup.Items.Add(name, startup.Contains(name));

            FillStoryList();
            _sections.Invalidate();
        }

        private void RefreshPlays()
        {
            foreach (ListViewItem item in _videos.Items)
                item.SubItems[1].Text = Plays(item.Text);
        }

        private void AddVideo(bool byCard)
        {
            if (_files == null)
                return;
            string name;
            if (byCard)
            {
                using var picker = new CardPickerDialog(CardCatalog.Get(this), "Whose summon cut-in?", askCopies: false, maxCopies: 1);
                if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                    return;
                name = picker.Result[0].Card.Id.ToString();
            }
            else
                name = "";
            using var open = new OpenFileDialog { Filter = "MP4 video (*.mp4)|*.mp4", Title = "Pick the video" };
            if (open.ShowDialog(this) != DialogResult.OK)
                return;
            if (name.Length == 0)
            {
                name = Ask("Video name (what menus and videos.json call it):", Path.GetFileNameWithoutExtension(open.FileName)) ?? "";
                if (name.Length == 0)
                    return;
            }
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "That name can't be a file name.", "Add video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Directory.CreateDirectory(MoviesFolder);
            string target = Path.Combine(MoviesFolder, name + ".mp4");
            if (File.Exists(target) && MessageBox.Show(this, $"{name}.mp4 is already there. Replace it?", "Add video", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            File.Copy(open.FileName, target, overwrite: true);
            FillVideos(name);
            _status.Text = $"Copied to {target}.";
        }

        /// <summary>Points every use of a video at its new name ("" = removed).</summary>
        private void Rename(string old, string name)
        {
            foreach (var row in _duelRows)
            {
                if (row.Before == old) row.Before = name;
                if (row.After == old) row.After = name;
            }
            foreach (var row in _eventRows)
            {
                int index = row.VideoList.IndexOf(old);
                if (index < 0)
                    continue;
                if (name.Length > 0)
                    row.VideoList[index] = name;
                else
                    row.VideoList.RemoveAt(index);
            }
            _eventRows.ResetBindings();
            FillStoryList();
        }

        private void RenameVideo()
        {
            if (SelectedName is not { } old || Ask("New name:", old) is not { Length: > 0 } name || name == old)
                return;
            string target = Path.Combine(MoviesFolder, name + ".mp4");
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || File.Exists(target))
            {
                MessageBox.Show(this, "That name can't be used (taken, or not a file name).", "Rename video", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            File.Move(Path.Combine(MoviesFolder, old + ".mp4"), target);
            Rename(old, name);
            int index = _startup.Items.IndexOf(old);
            bool startup = index >= 0 && _startup.GetItemChecked(index);
            if (index >= 0)
            {
                _startup.Items[index] = name;
                _startup.SetItemChecked(index, startup);
            }
            FillVideos(name);
        }

        private void DeleteVideo()
        {
            if (SelectedName is not { } name)
                return;
            if (MessageBox.Show(this, $"Delete {name}.mp4 from the movies folder?", "Delete video", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            File.Delete(Path.Combine(MoviesFolder, name + ".mp4"));
            Rename(name, "");
            FillVideos();
            _status.Text = $"Deleted {name}.mp4.";
        }

        private void PlayVideo(string? name = null)
        {
            name ??= SelectedName;
            if (!string.IsNullOrEmpty(name) && File.Exists(Path.Combine(MoviesFolder, name + ".mp4")))
                Process.Start(new ProcessStartInfo(Path.Combine(MoviesFolder, name + ".mp4")) { UseShellExecute = true });
        }

        private string? Ask(string question, string value)
        {
            using var form = new Form { Text = "Video", FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(380, 96), MaximizeBox = false, MinimizeBox = false };
            var label = new Label { Text = question, Left = 10, Top = 10, Width = 360 };
            var box = new TextBox { Text = value, Left = 10, Top = 32, Width = 360 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 214, Top = 62, Width = 75 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 295, Top = 62, Width = 75 };
            form.Controls.AddRange([label, box, ok, cancel]);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            return form.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
        }

        // ---- videos.json ----

        private void ReadNames(GameFolderFiles files)
        {
            _duelNames.Clear();
            _characterNames.Clear();
            try
            {
                if (files.Read(StoryDuelTable.GamePath('E')) is byte[] dueldata)
                {
                    var table = StoryDuelTable.Parse(new Dictionary<char, byte[]> { ['E'] = dueldata });
                    StoryDuelJson.Apply(StoryDuelJson.Load(files.ExPath(StoryDuelJson.FileName)), table);
                    foreach (var duel in table.Duels.OrderBy(d => d.Id))
                        _duelNames[duel.Id] = $"{duel.Id}: {duel.Title('E')}";
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException) { }
            try
            {
                if (files.Read(CharacterTable.GamePath('E')) is byte[] chardata)
                {
                    var table = CharacterTable.Parse(new Dictionary<char, byte[]> { ['E'] = chardata });
                    CharacterJson.Apply(CharacterJson.Load(files.ExPath(CharacterJson.FileName)), table);
                    foreach (var (id, name) in GameNames.Characters(table, 'E'))
                        _characterNames[id] = name;
                }
            }
            catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException) { }
        }

        public void Open(GameFolderFiles files)
        {
            _files = files;
            ReadNames(files);

            _duelRows.Clear();
            _eventRows.Clear();
            _startup.Items.Clear();
            string path = files.ExPath(FileName);
            var startup = new List<string>();
            if (File.Exists(path))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject;
                    if (root?["startup"] is JsonValue one && one.TryGetValue<string>(out var single))
                        startup.Add(single);
                    else if (root?["startup"] is JsonArray list)
                        startup.AddRange(list.OfType<JsonValue>().Select(v => v.ToString()));
                    if (root?["storyDuels"] is JsonObject duels)
                        foreach (var (id, node) in duels)
                            if (int.TryParse(id, out int duel) && node is JsonObject entry)
                            {
                                if (!_duelNames.ContainsKey(duel))
                                    _duelNames[duel] = $"{duel}";
                                _duelRows.Add(new DuelRow { Duel = duel, Before = entry["before"]?.ToString() ?? "", After = entry["after"]?.ToString() ?? "" });
                            }
                    if (root?["duel"] is JsonArray events)
                        foreach (var node in events.OfType<JsonObject>())
                            if (ReadEvent(node) is { } row)
                                _eventRows.Add(row);
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    _status.Text = $"{FileName} couldn't be read: {ex.Message}";
                }
            }
            _characterItems.Clear();
            _characterItems.Add(new Choice<int>(-1, "(anyone)"));
            foreach (var (id, name) in _characterNames.OrderBy(p => p.Key))
                _characterItems.Add(new Choice<int>(id, $"{id}: {name}"));
            foreach (string name in startup)
                _startup.Items.Add(name, true);
            FillVideos();
            ShowSentence();
            _savedJson = Build().ToJsonString();
            SetEditable(true);
            _status.Text = $"{VideoNames().Length} video(s) in {MoviesFolder}, {_eventRows.Count} duel video(s).";
        }

        private EventRow? ReadEvent(JsonObject node)
        {
            string when = node["when"]?.ToString() ?? "";
            if (!WhenChoices.Any(e => e.Key == when))
                return null;
            var row = new EventRow
            {
                When = when,
                Who = KeyOf(WhoChoices, node["who"]),
                Amount = (int)(Num(node["amount"]) ?? 0),
                CardList = Cards(node),
                How = KeyOf(SummonKinds, node["how"]),
                Reason = ReasonKey(node["reason"]),
                Kind = KeyOf(CardKinds, node["kind"]),
                Chain = node["response"] is JsonValue response && response.TryGetValue<bool>(out bool r) ? (r ? "response" : "start") : "either",
                Standing = KeyOf(Standings, node["standing"]),
                Duelist = (int)(Num(node["duelist"]) ?? -1),
                Against = (int)(Num(node["against"]) ?? -1),
                Chance = (int)Math.Clamp(Num(node["chance"]) ?? 100, 0, 100),
                Once = OnceKey(node["once"]),
            };
            if (node["videos"] is JsonArray videos)
                row.VideoList.AddRange(videos.OfType<JsonValue>().Select(v => v.ToString()));
            if (node["video"] is JsonValue video)
                row.VideoList.Add(video.ToString());
            foreach (int id in new[] { row.Duelist, row.Against }.Where(id => id >= 0 && !_characterNames.ContainsKey(id)))
                _characterNames[id] = $"character {id}";
            return row;
        }

        private static JsonObject WriteEvent(EventRow row)
        {
            var uses = UsesOf(row.When);
            var node = new JsonObject { ["when"] = row.When };
            if (Sided(row.When) && row.Who != "me")
                node["who"] = row.Who;
            if (uses.HasFlag(Uses.Amount) && (row.Amount != 0 || row.When != "turnStart"))
                node["amount"] = row.Amount;
            if (uses.HasFlag(Uses.Cards) && row.CardList.Count > 0)
                node["cards"] = new JsonArray([.. row.CardList.Select(c => JsonValue.Create(c))]);
            if (uses.HasFlag(Uses.How) && row.How != "any")
                node["how"] = row.How;
            if (uses.HasFlag(Uses.Reason) && row.Reason != "any")
                node["reason"] = row.Reason;
            if (uses.HasFlag(Uses.Kind) && row.Kind != "any")
                node["kind"] = row.Kind;
            if (uses.HasFlag(Uses.Chain) && row.Chain != "either")
                node["response"] = row.Chain == "response";
            if (uses.HasFlag(Uses.Standing) && row.Standing != "any")
                node["standing"] = row.Standing;
            if (row.Duelist >= 0)
                node["duelist"] = row.Duelist;
            if (row.Against >= 0)
                node["against"] = row.Against;
            node["videos"] = new JsonArray([.. row.VideoList.Select(v => JsonValue.Create(v))]);
            if (row.Chance != 100)
                node["chance"] = row.Chance;
            node["once"] = OnceNode(row.Once);
            return node;
        }

        private JsonObject Build()
        {
            var root = new JsonObject();
            var startup = _startup.CheckedItems.Cast<string>().ToList();
            if (startup.Count > 0)
                root["startup"] = new JsonArray([.. startup.Select(s => JsonValue.Create(s))]);
            var duels = new JsonObject();
            foreach (var row in _duelRows.Where(r => r.Before.Length > 0 || r.After.Length > 0).GroupBy(r => r.Duel).Select(g => g.Last()))
            {
                var entry = new JsonObject();
                if (row.Before.Length > 0) entry["before"] = row.Before;
                if (row.After.Length > 0) entry["after"] = row.After;
                duels[row.Duel.ToString()] = entry;
            }
            if (duels.Count > 0)
                root["storyDuels"] = duels;
            if (_eventRows.Count > 0)
                root["duel"] = new JsonArray([.. _eventRows.Select(WriteEvent)]);
            return root;
        }

        public bool Save()
        {
            if (_files == null)
                return false;
            _events.EndEdit();
            var names = VideoNames();
            var problems = new List<string>();
            for (int i = 0; i < _eventRows.Count; i++)
            {
                var row = _eventRows[i];
                if (row.VideoList.Count == 0)
                    problems.Add($"Duel video {i + 1} ({WhenText(row)}) has no videos (double-click its Videos cell).");
                if (row.Chance is < 1 or > 100)
                    problems.Add($"Duel video {i + 1}: chance must be 1-100.");
                foreach (string missing in row.VideoList.Where(v => !names.Contains(v, StringComparer.OrdinalIgnoreCase)))
                    problems.Add($"Duel video {i + 1}: movies\\{missing}.mp4 doesn't exist.");
            }
            if (problems.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var root = Build();
            string path = _files.ExPath(FileName);
            if (root.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);   // nothing set: no file (the videos still play as cut-ins and menu actions)
            }
            else
                File.WriteAllText(path, root.ToJsonString(WriteOptions) + "\n", new UTF8Encoding(false));
            _savedJson = root.ToJsonString();
            _status.Text = $"Saved {path}. Yu-Gi-Oh-Media reads it when the game starts.";
            return true;
        }
    }
}
