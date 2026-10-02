using CARD_Named;
using CARD_Pass;
using StartingCollection;
using Types;
using Wolf.Editors;
using WolfEx;
using WolfX.Types;

namespace WolfX
{
    /// <summary>
    /// The Card Manager: every card in one place. Left, the cards (the game's and your custom ones); right, a preview of the picked card
    /// (its face, drawn as Yu-Gi-Oh-AnimeCards draws it in game, with its name, type line, stats and text) and its tabs:
    /// <list type="bullet">
    /// <item>Properties - kind, attribute, type, level / rank / link rating, ATK, DEF, Spell/Trap icon, pendulum scales, password,
    /// archetypes (bin\CARD_Prop.bin, CARD_Pass.bin, CARD_Named.bin, by internal id from CARD_IntID.bin).</item>
    /// <item>Text - name and text in every language (bin\CARD_Indx / CARD_Name / CARD_Desc_&lt;L&gt;.bin).</item>
    /// <item>Genres, Related cards, Card links - the same editors as their own pages (one instance each, so a change shows in both), showing
    /// only this card while they are here.</item>
    /// </list>
    /// Standard content: everything about a game card is saved into the game's files. Custom cards' stats and text are cards.json (the
    /// New cards page, one click away); their genres, related cards and links go to Yu-Gi-Oh-Ex JSON like any other page's.
    /// </summary>
    internal sealed class CardManager : UserControl, IGameEditor
    {
        private sealed record Choice(int Value, string Text)
        {
            public override string ToString() => Text;
        }

        private static readonly string[] ShowChoices = ["All cards", "Game cards", "Custom cards", "Changed"];

        // ---- controls ----
        private readonly ToolStrip _listTools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripTextBox _find = new() { Width = 150, ToolTipText = "Konami id or part of a name" };
        private readonly ToolStripComboBox _show = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false, MultiSelect = false,
        };
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly CardSummary _summary = new();
        private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
        private readonly TabPage _propertiesTab = new("Properties") { UseVisualStyleBackColor = true, AutoScroll = true };
        private readonly TabPage _textTab = new("Text") { UseVisualStyleBackColor = true };

        private readonly ComboBox _kind = Combo(220), _attribute = Combo(120), _type = Combo(150), _icon = Combo(120), _language = Combo(140);
        private readonly NumericUpDown _level = Number(0, 13, 1), _atk = Number(0, 5100, 50), _def = Number(0, 5100, 50),
            _scaleLeft = Number(0, 13, 1), _scaleRight = Number(0, 13, 1), _password = Number(0, 99999999, 1);
        private readonly CheckBox _atkUnknown = new() { Text = "?", AutoSize = true }, _defUnknown = new() { Text = "?", AutoSize = true };
        private readonly LinkArrowPicker _arrows = new();
        private readonly Label _arrowCount = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 30, 3, 3) };
        private readonly Label _levelLabel = new() { Text = "Level:", AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly Label _archetypes = new() { AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 6, 3, 3) };
        private readonly Button _editArchetypes = new() { Text = "Edit archetypes...", AutoSize = true };
        private readonly Panel _gameFields = new() { Dock = DockStyle.Fill };
        private readonly Panel _customNote = new() { Dock = DockStyle.Fill, Visible = false };
        private readonly Panel _customTextNote = new() { Dock = DockStyle.Fill, Visible = false };
        private readonly Panel _gameText = new() { Dock = DockStyle.Fill };
        private readonly TextBox _name = new() { Dock = DockStyle.Top };
        private readonly TextBox _desc = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10f) };

        // ---- the pages shown inside (shared with their own pages) and the New cards page ----
        private readonly (TabPage Tab, Control Editor)[] _embedded;
        private readonly CardsPanel _customCards;

        /// <summary>Set by the window: shows the New cards page with this custom card (0: a new one).</summary>
        public Action<int>? OpenCustomCard { get; set; }

        // ---- data ----
        private GameFolderFiles? _files;
        private CardIdMap? _idMap;
        private CardPropTable? _props;
        private readonly Dictionary<char, CardTextTable> _texts = [];
        private List<int> _passwords = [];
        private readonly HashSet<string> _changedFiles = [];   // what Save has to write
        private readonly HashSet<int> _changedCards = [];
        private List<CardEntry> _rows = [];
        private int? _shown;
        private bool _binding;

        public CardManager(CardGenreEditor genres, RelatedCardsEditor related, CardLinkEditor links, CardsPanel customCards)
        {
            _customCards = customCards;
            _customCards.CardsChanged += () => { _list.Invalidate(); ShowSelected(); };
            _embedded = [(new TabPage("Genres"), genres), (new TabPage("Related cards"), related), (new TabPage("Links"), links)];

            // left: the cards
            _listTools.Items.Add(new ToolStripLabel("Find:"));
            _find.TextChanged += (_, _) => Refill();
            _listTools.Items.Add(_find);
            _show.Items.AddRange(ShowChoices);
            _show.SelectedIndex = 0;
            _show.SelectedIndexChanged += (_, _) => Refill();
            _listTools.Items.Add(_show);
            _list.Columns.Add("Id", 56);
            _list.Columns.Add("Name", 170);
            _list.Columns.Add("Kind", 80);
            _list.RetrieveVirtualItem += (_, e) =>
            {
                var card = _rows[e.ItemIndex];
                e.Item = new ListViewItem([card.Id.ToString(), NameOf(card.Id), KindText(card.Id)])
                {
                    ForeColor = card.IsCustom ? Color.FromArgb(40, 90, 160) : _changedCards.Contains(card.Id) ? Color.FromArgb(170, 90, 0) : SystemColors.WindowText,
                };
            };
            _list.SelectedIndexChanged += (_, _) => ShowSelected();
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_list);
            left.Controls.Add(_listTools);

            // right: tools, preview + summary, tabs
            _tools.Items.Add(Button("Save", "Save this page: game cards into the game's files (Ctrl+S)", () => Save()));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("New custom card...", "Make a new card on the New cards page (saved to cards.json)", () => OpenCustomCard?.Invoke(0)));

            // preview + summary above the tabs, with a splitter between them; the card scales to the height it gets
            var upright = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            upright.Panel1.Controls.Add(_summary);

            BuildProperties();
            BuildText();
            _tabs.TabPages.Add(_propertiesTab);
            _tabs.TabPages.Add(_textTab);
            foreach (var (tab, _) in _embedded)
            {
                tab.UseVisualStyleBackColor = true;
                _tabs.TabPages.Add(tab);
            }
            _tabs.SelectedIndexChanged += (_, _) => AttachEmbedded();
            VisibleChanged += (_, _) => AttachEmbedded();

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
                if (_shown == null && _rows.Count > 0)
                    Refill(_rows[0].Id);   // the list exists now: pick the first card
            };
            _status.Text = "Open the game data (File > Open).";
        }

        private static ComboBox Combo(int width) => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };

        private static NumericUpDown Number(int min, int max, int step) => new() { Minimum = min, Maximum = max, Increment = step, Width = 80 };

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
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

        // ---- layout of the two own tabs ----

        private void BuildProperties()
        {
            foreach (var kind in Enum.GetValues<CARDS_INFO.CARD_Kind>().Where(k => (int)k >= 0))
                _kind.Items.Add(new Choice((int)kind, $"{(int)kind}: {CardNames.KindName((int)kind)}"));
            for (int a = 0; a <= 9; a++)
                _attribute.Items.Add(new Choice(a, a == 0 ? "0: (none)" : $"{a}: {CardNames.AttributeName(a)}"));
            foreach (var type in Enum.GetValues<CARDS_INFO.CARD_Type>())
                _type.Items.Add(new Choice((int)type, $"{(int)type}: {((int)type == 0 ? "(none)" : CardNames.RaceName((int)type))}"));
            for (int i = 0; i < CardNames.Icons.Length; i++)
                _icon.Items.Add(new Choice(i, CardNames.Icons[i]));

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(6) };
            static Label Caption(string text) => new() { Text = text, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 3, 3) };
            static FlowLayoutPanel Row(params Control[] controls)
            {
                var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                row.Controls.AddRange(controls);
                return row;
            }
            _levelLabel.Margin = new Padding(3, 7, 3, 3);
            var rows = new (Control Label, Control Field)[]
            {
                (Caption("Kind:"), _kind), (Caption("Attribute:"), _attribute), (Caption("Type:"), _type), (_levelLabel, _level),
                (Caption("ATK:"), Row(_atk, _atkUnknown)), (Caption("DEF:"), Row(_def, _defUnknown)), (Caption("Spell / Trap icon:"), _icon),
                (Caption("Link arrows:"), Row(_arrows, _arrowCount)),
                // the game has one Pendulum Scale per card; the 4 bits after it are something else (0-3 on the game's cards, meaning not known yet)
                (Caption("Pendulum scale:"), _scaleLeft),
                (Caption("Other 4 bits:"), Row(_scaleRight, new Label { Text = "(not a scale; use not known yet)", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(6, 7, 3, 3) })),
                (Caption("Password:"), _password),
                (Caption("Archetypes:"), Row(_archetypes, _editArchetypes)),
            };
            for (int row = 0; row < rows.Length; row++)
            {
                grid.Controls.Add(rows[row].Label, 0, row);
                grid.Controls.Add(rows[row].Field, 1, row);
            }
            _password.Width = 110;

            var note = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 40, ForeColor = SystemColors.GrayText, Padding = new Padding(9, 6, 6, 0),
                Text = "Saved into the game data: bin\\CARD_Prop.bin (stats), bin\\CARD_Pass.bin (password), bin\\CARD_Named.bin (archetypes). " +
                       "ATK and DEF are kept in steps of 10; \"?\" is the game's unknown value.",
            };
            _gameFields.Controls.Add(note);
            _gameFields.Controls.Add(grid);

            foreach (var combo in new[] { _kind, _attribute, _type, _icon })
                combo.SelectedIndexChanged += (_, _) => PropertyEdited();
            foreach (var number in new[] { _level, _atk, _def, _scaleLeft, _scaleRight })
                number.ValueChanged += (_, _) => PropertyEdited();
            _atkUnknown.CheckedChanged += (_, _) => PropertyEdited();
            _defUnknown.CheckedChanged += (_, _) => PropertyEdited();
            _arrows.Changed += PropertyEdited;
            _password.ValueChanged += (_, _) => PasswordEdited();
            _editArchetypes.Click += (_, _) => EditArchetypes();

            _customNote.Controls.Add(CustomNote("A custom card: its stats, art and text are in cards.json, edited on the New cards page."));
            _gameFields.AutoScroll = true;
            _propertiesTab.Controls.Add(_gameFields);
            _propertiesTab.Controls.Add(_customNote);
        }

        private Panel CustomNote(string text)
        {
            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(10) };
            panel.Controls.Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(520, 0) });
            var open = new System.Windows.Forms.Button { Text = "Edit on New cards", AutoSize = true };
            open.Click += (_, _) => { if (_shown is int id) OpenCustomCard?.Invoke(id); };
            panel.Controls.Add(open);
            return panel;
        }

        private void BuildText()
        {
            foreach (char language in CardTextTable.Languages)
                _language.Items.Add(new Choice(language, $"{HowToPlayFile.LanguageName(language)} ({language})"));
            _language.SelectedIndex = 0;
            _language.SelectedIndexChanged += (_, _) =>
            {
                ShowText();
                ShowSummary();   // the face shows the language being edited
            };
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false };
            bar.Controls.Add(new Label { Text = "Language:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
            bar.Controls.Add(_language);
            bar.Controls.Add(new Label
            {
                Text = "Saved into bin\\CARD_Name / CARD_Desc / CARD_Indx_<lang>.bin.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(12, 7, 3, 3),
            });
            var fields = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };
            fields.Controls.Add(_desc);
            fields.Controls.Add(new Label { Text = "Text:", Dock = DockStyle.Top, Height = 20 });
            fields.Controls.Add(_name);
            fields.Controls.Add(new Label { Text = "Name:", Dock = DockStyle.Top, Height = 20 });
            _name.TextChanged += (_, _) => TextEdited();
            _desc.TextChanged += (_, _) => TextEdited();
            _gameText.Controls.Add(fields);
            _gameText.Controls.Add(bar);
            _customTextNote.Controls.Add(CustomNote("A custom card: its name and text are in cards.json, edited on the New cards page."));
            _textTab.Controls.Add(_gameText);
            _textTab.Controls.Add(_customTextNote);
        }

        // ---- IGameEditor ----

        public bool Dirty => _changedFiles.Count > 0;

        public IReadOnlyCollection<string> Files =>
            [CardPropTable.GamePath, CardIdMap.GamePath, Card_Pass.GamePath, Card_Named.GamePath,
             .. CardTextTable.Languages.SelectMany(l => new[] { CardTextTable.IndxPath(l), CardTextTable.NamePath(l), CardTextTable.DescPath(l) })];

        public string SavesTo => "Standard: a game card's stats, text, password and archetypes into bin\\CARD_*. Additional: custom cards are cards.json (New cards); " +
                                 "genres, related cards and links follow their own pages' rules.";

        public void Open(GameFolderFiles files)
        {
            _files = files;
            _summary.Art = new WorkspaceCardArt(files);
            _texts.Clear();
            _changedFiles.Clear();
            _changedCards.Clear();
            try
            {
                _idMap = files.Read(CardIdMap.GamePath) is { } ids ? CardIdMap.Parse(ids) : null;
                _props = files.Read(CardPropTable.GamePath) is { } prop ? CardPropTable.Parse(prop) : null;
                foreach (char language in CardTextTable.Languages)
                {
                    if (files.Read(CardTextTable.IndxPath(language)) is { } indx && files.Read(CardTextTable.NamePath(language)) is { } names &&
                        files.Read(CardTextTable.DescPath(language)) is { } descs)
                        _texts[language] = CardTextTable.Parse(indx, names, descs);
                }
                _passwords = files.Read(Card_Pass.GamePath) is { } pass ? Card_Pass.Parse(pass) : [];
                if (files.Read(Card_Named.GamePath) is { } named)
                    Card_Named.Parse(named);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException)
            {
                _status.Text = "Couldn't read the card files: " + ex.Message;
            }
            Refill(_shown);
            _status.Text = _props == null || _idMap == null
                ? "The open data has no bin\\CARD_Prop.bin or bin\\CARD_IntID.bin: only custom cards can be shown."
                : $"{_rows.Count(r => !r.IsCustom)} game cards, {_rows.Count(r => r.IsCustom)} custom cards. Text in {string.Join(" ", _texts.Keys)}.";
        }

        /// <summary>Writes the files that changed, then the pages shown inside (genres, related cards, links) when they have changes.</summary>
        public bool Save()
        {
            if (_files == null)
                return false;
            try
            {
                var write = new Dictionary<string, byte[]>();
                if (_props != null && _changedFiles.Contains(CardPropTable.GamePath))
                    write[CardPropTable.GamePath] = _props.ToBytes();
                foreach (var (language, table) in _texts)
                {
                    if (!_changedFiles.Contains(CardTextTable.NamePath(language)))
                        continue;
                    var (indx, names, descs) = table.ToBytes();
                    write[CardTextTable.IndxPath(language)] = indx;
                    write[CardTextTable.NamePath(language)] = names;
                    write[CardTextTable.DescPath(language)] = descs;
                }
                if (_changedFiles.Contains(Card_Pass.GamePath))
                    write[Card_Pass.GamePath] = Card_Pass.ToBytes(_passwords);
                if (_changedFiles.Contains(Card_Named.GamePath))
                    write[Card_Named.GamePath] = Card_Named.ToBytes();
                if (write.Count > 0)
                    _files.Write(write);
                _changedFiles.Clear();
                _changedCards.Clear();
                _list.Invalidate();

                int pages = 0;
                foreach (var (_, editor) in _embedded)
                {
                    if (editor is IGameEditor { Dirty: true } page)
                    {
                        if (!page.Save())
                            return false;
                        pages++;
                    }
                }
                _status.Text = (write.Count > 0 ? $"Saved {write.Count} files into {_files.Describe(CardPropTable.GamePath)}" : "No card files changed") +
                               (pages > 0 ? $"; saved {pages} of the genres / related cards / links pages." : ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        // ---- the list ----

        private CardsPanel.CardModel? Custom(int id) => _customCards.Cards.FirstOrDefault(c => c.Id == id);

        private int InternalOf(int id) => _idMap?.InternalOf(id) ?? 0;

        private CardRecord? Record(int id) => InternalOf(id) is int i and > 0 && _props != null && i < _props.Records.Count ? _props.Records[i] : null;

        private string NameOf(int id)
        {
            if (Custom(id) is { } custom)
                return custom.Name;
            int i = InternalOf(id);
            return i > 0 && _texts.TryGetValue('E', out var english) && i < english.Names.Count ? english.Names[i] : CardCatalog.NameOf(id);
        }

        private string KindText(int id) =>
            Custom(id) is { } custom ? custom.Kind : Record(id) is { } record ? CardNames.KindName((int)record.Kind) : "";

        private void Refill(int? select = null)
        {
            select ??= Selected()?.Id;
            string find = _find.Text.Trim();
            var cards = CardCatalog.Get(this).Cards.Where(c => c.Id > 0).ToList();
            foreach (var custom in _customCards.Cards.Where(c => cards.All(e => e.Id != c.Id)))
                cards.Add(new CardEntry(custom.Id, custom.Name, true));   // added on New cards, not saved yet
            int show = _show.SelectedIndex;
            _rows = cards
                .Where(c => show switch { 1 => !c.IsCustom, 2 => c.IsCustom || Custom(c.Id) != null, 3 => _changedCards.Contains(c.Id), _ => true })
                .Where(c => find.Length == 0 || c.Id.ToString() == find || NameOf(c.Id).Contains(find, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Id).ToList();
            _list.VirtualListSize = _rows.Count;
            _list.SelectedIndices.Clear();
            int index = select is int wanted ? _rows.FindIndex(c => c.Id == wanted) : -1;
            if (index < 0 && _rows.Count > 0 && select == null)
                index = 0;
            if (index >= 0)
            {
                _list.SelectedIndices.Add(index);
                _list.EnsureVisible(index);
            }
            _list.Invalidate();
            ShowSelected();
        }

        private CardEntry? Selected() =>
            _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;

        /// <summary>Shows this card (from the window: "find in Card Manager").</summary>
        public void ShowCard(int konamiId)
        {
            _find.Text = "";
            _show.SelectedIndex = 0;
            Refill(konamiId);
        }

        // ---- the picked card ----

        private void ShowSelected()
        {
            var card = Selected();
            _shown = card?.Id;
            bool custom = card != null && (card.IsCustom || Custom(card.Id) != null);
            _customNote.Visible = _customTextNote.Visible = custom;
            _gameFields.Visible = !custom;
            _gameText.Visible = !custom;
            var record = card == null ? null : Record(card.Id);
            _gameFields.Enabled = record != null;

            _binding = true;
            try
            {
                if (record != null)
                {
                    SelectChoice(_kind, (int)record.Kind);
                    SelectChoice(_attribute, (int)record.Attribute);
                    SelectChoice(_type, (int)record.Type);
                    SelectChoice(_icon, record.Icon);
                    _level.Value = Math.Clamp(record.Level, 0, 13);
                    _atkUnknown.Checked = record.Atk < 0;
                    _defUnknown.Checked = record.Def < 0;
                    _atk.Value = Math.Clamp(Math.Max(0, record.Atk), 0, (int)_atk.Maximum);
                    bool isLink = CardNames.FrameOf((int)record.Kind) == CardFrame.Link;
                    _def.Value = isLink ? 0 : Math.Clamp(Math.Max(0, record.Def), 0, (int)_def.Maximum);   // a Link's DEF bits are its arrows (shown below)
                    _def.Text = isLink ? "" : _def.Value.ToString();
                    _scaleLeft.Value = Math.Clamp(record.ScaleLeft, 0, 13);
                    _scaleRight.Value = Math.Clamp(record.ScaleRight, 0, 13);
                    _arrows.Value = CardNames.FrameOf((int)record.Kind) == CardFrame.Link ? record.LinkArrows : 0;
                    int i = InternalOf(card!.Id);
                    _password.Value = i < _passwords.Count ? Math.Clamp(_passwords[i], 0, (int)_password.Maximum) : 0;
                    _password.Enabled = i < _passwords.Count;
                    UpdateFieldStates(record);
                }
                ShowArchetypes();
                ShowText();
            }
            finally
            {
                _binding = false;
            }
            ShowSummary();
            AttachEmbedded();
        }

        private static void SelectChoice(ComboBox combo, int value)
        {
            int index = combo.Items.Cast<Choice>().ToList().FindIndex(c => c.Value == value);
            if (index < 0)
            {
                combo.Items.Add(new Choice(value, $"{value}: (unknown)"));
                index = combo.Items.Count - 1;
            }
            combo.SelectedIndex = index;
        }

        private static int ChoiceOf(ComboBox combo) => combo.SelectedItem is Choice choice ? choice.Value : 0;

        /// <summary>The fields that mean something for this kind: Spell/Trap icon, pendulum scales, DEF (not for Link), what Level is called.</summary>
        private void UpdateFieldStates(CardRecord record)
        {
            var frame = CardNames.FrameOf((int)record.Kind);
            bool monster = !record.IsSpellOrTrap;
            _levelLabel.Text = frame == CardFrame.Xyz ? "Rank:" : frame == CardFrame.Link ? "Link rating:" : "Level:";
            _icon.Enabled = !monster;
            _level.Enabled = _atk.Enabled = _atkUnknown.Enabled = _attribute.Enabled = monster;
            _def.Enabled = _defUnknown.Enabled = monster && frame != CardFrame.Link;
            _arrows.Enabled = frame == CardFrame.Link;   // a Link's DEF bits are its arrows
            _arrowCount.Text = frame != CardFrame.Link ? "" : _arrows.Count == record.Level ? $"{_arrows.Count} arrows" :
                $"{_arrows.Count} arrows - Link {record.Level} cards normally have {record.Level}";
            _scaleLeft.Enabled = CardNames.KindName((int)record.Kind).Contains("Pendulum");
            _atk.Enabled &= !_atkUnknown.Checked;
            _def.Enabled &= !_defUnknown.Checked;
        }

        private void ShowText()
        {
            bool was = _binding;
            _binding = true;
            try
            {
                char language = (char)ChoiceOf(_language);
                int i = _shown is int id ? InternalOf(id) : 0;
                bool has = i > 0 && _texts.TryGetValue(language, out var table) && i < table.Names.Count;
                _name.Text = has ? _texts[language].Names[i] : "";
                _desc.Text = has ? _texts[language].Descs[i].Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
                _name.Enabled = _desc.Enabled = has;
            }
            finally
            {
                _binding = was;
            }
        }

        private void ShowArchetypes()
        {
            var codes = _shown is int id ? Card_Named.ArchetypesOf(id) : [];
            _archetypes.Text = codes.Count == 0 ? "(none)" : string.Join(", ", codes.Select(c => $"{Card_Named.NameOf(c)} ({c})"));
            _editArchetypes.Enabled = _shown is int card && Record(card) != null && Card_Named.CardsInArchetype.Count > 0;
        }

        // ---- edits ----

        private void Changed(int id, string file)
        {
            _changedFiles.Add(file);
            _changedCards.Add(id);
            _list.Invalidate();
            ShowSummary();
        }

        private void PropertyEdited()
        {
            if (_binding || _shown is not int id || Record(id) is not { } record)
                return;
            record.Kind = (CARDS_INFO.CARD_Kind)ChoiceOf(_kind);
            record.Attribute = (CARDS_INFO.CARD_Attribute)ChoiceOf(_attribute);
            record.Type = (CARDS_INFO.CARD_Type)ChoiceOf(_type);
            record.Icon = ChoiceOf(_icon);
            record.Level = (int)_level.Value;
            record.Atk = _atkUnknown.Checked ? CardRecord.Unknown : (int)_atk.Value / 10 * 10;
            record.Def = _defUnknown.Checked ? CardRecord.Unknown : (int)_def.Value / 10 * 10;
            if (CardNames.FrameOf((int)record.Kind) == CardFrame.Link)
                record.LinkArrows = _arrows.Value;   // in place of DEF
            record.ScaleLeft = (int)_scaleLeft.Value;
            record.ScaleRight = (int)_scaleRight.Value;
            UpdateFieldStates(record);
            Changed(id, CardPropTable.GamePath);
        }

        private void PasswordEdited()
        {
            if (_binding || _shown is not int id || InternalOf(id) is not (int i and > 0) || i >= _passwords.Count)
                return;
            _passwords[i] = (int)_password.Value;
            Changed(id, Card_Pass.GamePath);
        }

        private void TextEdited()
        {
            char language = (char)ChoiceOf(_language);
            if (_binding || _shown is not int id || InternalOf(id) is not (int i and > 0) || !_texts.TryGetValue(language, out var table) || i >= table.Names.Count)
                return;
            table.Names[i] = _name.Text;
            table.Descs[i] = _desc.Text.Replace("\r\n", "\n");
            Changed(id, CardTextTable.NamePath(language));
            if (language == 'E')
                _list.Invalidate();
        }

        private void EditArchetypes()
        {
            if (_shown is not int id)
                return;
            static IEnumerable<(int Code, string Text)> Choices() =>
                Card_Named.CardsInArchetype.Keys.Where(code => code > 0).OrderBy(code => code).Select(code => (code, $"{code} - {Card_Named.NameOf(code)}"));
            using var dialog = new ArchetypePickerDialog(NameOf(id), Card_Named.ArchetypesOf(id), Choices);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            var wanted = dialog.Result.ToHashSet();
            foreach (var (code, list) in Card_Named.CardsInArchetype)
            {
                bool has = list.BinarySearch(id) >= 0;
                if (wanted.Contains(code) && !has)
                {
                    list.Add(id);
                    list.Sort();
                }
                else if (!wanted.Contains(code) && has)
                    list.Remove(id);
            }
            ShowArchetypes();
            Changed(id, Card_Named.GamePath);
        }

        // ---- preview ----

        private void ShowSummary()
        {
            if (_shown is not int id)
            {
                _summary.ShowNothing();
                return;
            }
            if (Custom(id) is { } custom)
            {
                _summary.ShowCard(custom.ToGameCard(), $"Konami id {id}   custom card (cards.json)", CardArt.Get(id, custom.PendingArt ?? custom.Image), ownsPicture: true);
                return;
            }
            int i = InternalOf(id);
            string info = $"Konami id {id}   internal id {i}" + (i > 0 && i < _passwords.Count && _passwords[i] != 0 ? $"   password {_passwords[i]:00000000}" : "") +
                          (_changedCards.Contains(id) ? "   changed, not saved" : "");
            if (Record(id) is not { } record)
            {
                _summary.ShowCard(new GameCard { Name = CardCatalog.NameOf(id), Kind = 1 }, info, CardArt.Get(id), ownsPicture: false);
                return;
            }
            // the card in the language picked on the Text tab, so its edits show on the face too
            char language = (char)ChoiceOf(_language);
            var texts = _texts.TryGetValue(language, out var table) ? table : _texts.GetValueOrDefault('E');
            bool link = GameCardPainter.FrameOf((int)record.Kind) == 18;
            _summary.ShowCard(new GameCard
            {
                Name = texts != null && i > 0 && i < texts.Names.Count ? texts.Names[i] : NameOf(id),
                Kind = (int)record.Kind, Attribute = (int)record.Attribute, Race = CardNames.RaceName((int)record.Type), Level = record.Level,
                Atk = record.Atk, Def = link ? 0 : record.Def, LinkArrows = link ? record.LinkArrows : 0, Scale = record.ScaleLeft,
                Icon = record.Icon, Text = texts != null && i > 0 && i < texts.Descs.Count ? texts.Descs[i] : "",
            }, info, CardArt.Get(id), ownsPicture: false);
        }

        // ---- the shared pages ----

        /// <summary>Puts the open tab's page (genres, related cards, links) in it, showing only this card.</summary>
        private void AttachEmbedded()
        {
            if (!Visible || _shown is not int id)
                return;
            foreach (var (tab, editor) in _embedded)
            {
                if (tab != _tabs.SelectedTab)
                    continue;
                if (editor.Parent != tab)
                {
                    editor.Dock = DockStyle.Fill;
                    tab.Controls.Add(editor);
                }
                if (editor is ICardFocus focus)
                {
                    focus.CardOnly = true;
                    focus.ShowCard(id);
                }
            }
        }
    }
}
