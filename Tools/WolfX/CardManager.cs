using System.Runtime.InteropServices;
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
    /// <item>Art - the card's picture, written back into the art .zib files (2020.full.illust_j / _a.jpg.zib, <see cref="VanillaArt"/>).</item>
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
        private readonly TabPage _artTab = new("Art") { UseVisualStyleBackColor = true, AutoScroll = true };

        private readonly ComboBox _kind = Combo(220), _attribute = Combo(120), _type = Combo(150), _icon = Combo(120), _language = Combo(140);
        private readonly NumericUpDown _level = Number(0, 13, 1), _atk = Number(0, 5100, 50), _def = Number(0, 5100, 50),
            _scaleLeft = Number(0, 13, 1), _scaleRight = Number(0, 13, 1), _password = Number(0, 99999999, 1);
        private readonly CheckBox _atkUnknown = new() { Text = "?", AutoSize = true }, _defUnknown = new() { Text = "?", AutoSize = true };
        private readonly LinkArrowPicker _arrows = new();
        private readonly Label _arrowCount = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 30, 3, 3) };
        private readonly Label _levelLabel = new() { Text = "Level:", AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly Label _archetypes = new() { AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 6, 3, 3) };
        private readonly Button _editArchetypes = new() { Text = "Edit archetypes...", AutoSize = true };
        private readonly Label _sameName = new() { AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(3, 7, 3, 3) };
        private readonly ComboBox _sameMode = Combo(170);
        private readonly Button _pickSame = new() { Text = "Pick card...", AutoSize = true }, _clearSame = new() { Text = "Own name", AutoSize = true };
        private readonly Panel _gameFields = new() { Dock = DockStyle.Fill };
        private readonly Panel _customNote = new() { Dock = DockStyle.Fill, Visible = false };
        private readonly Panel _customTextNote = new() { Dock = DockStyle.Fill, Visible = false };
        private readonly Panel _gameText = new() { Dock = DockStyle.Fill };
        private readonly TextBox _name = new() { Dock = DockStyle.Top };
        private readonly TextBox _desc = new() { Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10f) };
        // the card's first three index letters in this language (bin\CARD_Kana1/2/3_<lang>.bin): kana of the reading in Japanese, else the name's
        private readonly TextBox _kana = new() { Width = 70, MaxLength = 3, Font = new Font("Segoe UI", 10f) };
        private readonly Button _kanaFromName = new() { Text = "From name", AutoSize = true };
        // the card's two pictures: censored (every card) and uncensored (only some cards have one); each is changed on its own
        private readonly ArtSlot _censoredArt = new(CardArt.CensoredZib), _uncensoredArt = new(CardArt.UncensoredZib);
        private readonly Button _addUncensored = new() { Text = "Add an uncensored picture...", AutoSize = true };
        private readonly Panel _artFields = new() { Dock = DockStyle.Fill };
        private readonly Panel _customArtNote = new() { Dock = DockStyle.Fill, Visible = false };
        // Fusion / Ritual / Synchro / Xyz requirements: the same editor as the New cards page; game cards' changes go to summoning.json
        private readonly TabPage _summonTab = new("Summoning") { UseVisualStyleBackColor = true };
        private readonly SummonEditor _summon = new();
        private SummoningFile? _summoning;

        // ---- the pages shown inside (shared with their own pages) and the New cards page ----
        private readonly (TabPage Tab, Control Editor)[] _embedded;
        private readonly CardsPanel _customCards;

        /// <summary>Set by the window: shows the New cards page with this custom card (0: a new one).</summary>
        public Action<int>? OpenCustomCard { get; set; }

        // ---- data ----
        private GameFolderFiles? _files;
        private CardIdMap? _idMap;
        private CardPropTable? _props;
        private CardSameTable? _same;
        private readonly Dictionary<char, CardTextTable> _texts = [];
        private readonly Dictionary<char, CardKanaTable> _kanas = [];
        private readonly Dictionary<char, CardNameSort> _sorts = [];   // the deck editor's name order per language (bin\CARD_Sort / Sort2_<lang>.bin)
        private List<int> _passwords = [];
        private readonly HashSet<string> _changedFiles = [];   // what Save has to write
        private readonly HashSet<int> _changedCards = [];
        // (.zib, Konami id) -> new picture (game JPEG), or null = take it out of that .zib; written into the art .zib files on Save
        private readonly Dictionary<(string Zib, int Id), byte[]?> _pendingArt = [];
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
            BuildArt();
            _tabs.TabPages.Add(_propertiesTab);
            _tabs.TabPages.Add(_textTab);
            _tabs.TabPages.Add(_artTab);
            BuildSummon();
            _tabs.TabPages.Add(_summonTab);
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

        // ---- layout of the own tabs ----

        private void BuildSummon()
        {
            _summon.CardName = NameOf;
            _summon.CardIdByName = name => _customCards.Cards.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
                                           ?? EffectScriptCompiler.TryCardIdByName(name);
            _summon.CustomCards = () => _customCards.Cards.Select(c => (c.Name, c.Archetypes)).ToList();
            _summon.DataChanged += target =>
            {
                if (!target.GameCard)
                    return;   // a custom card's Extra: saved with cards.json
                if (_summoning == null)
                    return;
                if (!_summoning.Has(target.Id))
                {
                    _summoning.Set(target.Id, target.Name, target.Data);
                    ShowSummon();   // "Changed" now
                }
                else
                    _summoning.Touch();
                _changedCards.Add(target.Id);
                _list.Invalidate();
            };
            _summon.ResetToGame += target =>
            {
                _summoning?.Remove(target.Id);
                ShowSummon();
            };
            _summonTab.Controls.Add(_summon);
        }

        /// <summary>The picked card's requirements: a custom card's Extra, a game card's summoning.json entry or a copy of the game's.</summary>
        private void ShowSummon()
        {
            if (_shown is not int id)
            {
                _summon.Bind(null);
                return;
            }
            if (Custom(id) is { } custom)
            {
                custom.Extra ??= [];
                bool ritualSpell = custom.Kind == "Spell" && custom.Icon == "Ritual";
                _summon.Bind(new SummonTarget
                {
                    Id = id, Name = custom.Name, Kind = custom.Kind, RitualSpell = ritualSpell, Description = custom.Description, Level = custom.Level,
                    Data = custom.Extra,
                });
                return;
            }
            var record = Record(id);
            if (record == null)
            {
                _summon.Bind(null);
                return;
            }
            int i = InternalOf(id);
            string description = i > 0 && _texts.TryGetValue('E', out var english) && i < english.Descs.Count ? english.Descs[i] : "";
            var changed = _summoning?.Get(id);
            _summon.Bind(new SummonTarget
            {
                Id = id,
                Name = NameOf(id),
                Kind = CardNames.KindName((int)record.Kind),
                RitualSpell = record.Kind == CARDS_INFO.CARD_Kind.Spell && record.Icon < CardNames.Icons.Length && CardNames.Icons[record.Icon] == "Ritual",
                Description = description,
                Level = record.Level,
                Data = changed ?? GameSummonTables.For(id),
                GameCard = true,
                Changed = changed != null,
            });
        }

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
                (Caption("Same name as:"), Row(_sameName, _sameMode, _pickSame, _clearSame)),
            };
            for (int row = 0; row < rows.Length; row++)
            {
                grid.Controls.Add(rows[row].Label, 0, row);
                grid.Controls.Add(rows[row].Field, 1, row);
            }
            _password.Width = 110;

            var note = new Label
            {
                Dock = DockStyle.Top, AutoSize = false, Height = 70, ForeColor = SystemColors.GrayText, Padding = new Padding(9, 6, 6, 0),
                Text = "Saved into the game data: bin\\CARD_Prop.bin (stats), bin\\CARD_Pass.bin (password), bin\\CARD_Named.bin (archetypes), " +
                       "bin\\CARD_Same.bin (same name). ATK and DEF are kept in steps of 10; \"?\" is the game's unknown value. " +
                       "Same name \"always\": duels treat the card as the other one everywhere (Harpie Lady 1); " +
                       "\"while an effect says so\": it keeps its own name unless its text changes it (Cyber Dragon Zwei).",
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
            _sameMode.Items.Add(new Choice((int)SameNameMode.Always, "always"));
            _sameMode.Items.Add(new Choice((int)SameNameMode.WhileEffect, "while an effect says so"));
            _sameMode.SelectedIndex = 0;
            _sameMode.SelectedIndexChanged += (_, _) => SameModeEdited();
            _pickSame.Click += (_, _) => PickSameName();
            _clearSame.Click += (_, _) => SetSameName(null);

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
            var kanaRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
            kanaRow.Controls.Add(new Label { Text = "Index letters:", AutoSize = true, Margin = new Padding(0, 8, 3, 3) });
            kanaRow.Controls.Add(_kana);
            kanaRow.Controls.Add(_kanaFromName);
            kanaRow.Controls.Add(new Label
            {
                Text = "First 3 letters of the name (Japanese: of its reading, in hiragana). bin\\CARD_Kana1/2/3_<lang>.bin; only the Japanese build of the game reads it.",
                AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 8, 3, 3),
            });
            fields.Controls.Add(kanaRow);
            fields.Controls.Add(_name);
            fields.Controls.Add(new Label { Text = "Name:", Dock = DockStyle.Top, Height = 20 });
            _name.TextChanged += (_, _) => TextEdited();
            _kana.TextChanged += (_, _) => KanaEdited();
            _kanaFromName.Click += (_, _) => _kana.Text = CardKanaTable.FromName(_name.Text);
            _desc.TextChanged += (_, _) => TextEdited();
            _gameText.Controls.Add(fields);
            _gameText.Controls.Add(bar);
            _customTextNote.Controls.Add(CustomNote("A custom card: its name and text are in cards.json, edited on the New cards page."));
            _textTab.Controls.Add(_gameText);
            _textTab.Controls.Add(_customTextNote);
        }

        /// <summary>One of a card's two pictures on the Art tab: the picture, what it is, and its buttons.</summary>
        private sealed class ArtSlot : GroupBox
        {
            public readonly string Zib;
            public readonly PictureBox Picture = new()
            {
                Size = new Size(VanillaArt.Size, VanillaArt.Size), SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, AllowDrop = true,
                BackColor = Color.FromArgb(32, 32, 32), Margin = new Padding(3),
            };
            public readonly Label Info = new() { AutoSize = true, MaximumSize = new Size(VanillaArt.Size, 0), Margin = new Padding(3, 6, 3, 3) };
            public readonly Button Choose = new() { Text = "Choose...", AutoSize = true };
            public readonly Button Paste = new() { Text = "Paste", AutoSize = true };
            public readonly Button Export = new() { Text = "Export...", AutoSize = true };
            public readonly Button Game = new() { Text = "Use the game's", AutoSize = true };

            public ArtSlot(string zib)
            {
                Zib = zib;
                Text = $"{VanillaArt.Describe(zib)}  ({zib})";
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Padding = new Padding(6);
                Margin = new Padding(3, 3, 12, 3);
                var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                buttons.Controls.AddRange([Choose, Paste, Export, Game]);
                var column = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
                column.Controls.AddRange([Picture, buttons, Info]);
                Controls.Add(column);
            }

            public void SetPicture(Bitmap? picture)
            {
                var old = Picture.Image;
                Picture.Image = picture;
                old?.Dispose();
            }
        }

        private void BuildArt()
        {
            var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true, Padding = new Padding(6) };
            row.Controls.Add(_censoredArt);
            row.Controls.Add(_uncensoredArt);
            var side = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            side.Controls.Add(_addUncensored);
            side.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(380, 0), ForeColor = SystemColors.GrayText, Margin = new Padding(3, 12, 3, 3),
                Text = "A card has a censored picture (2020.full.illust_j.jpg.zib, what the game shows normally) and some cards also an uncensored one " +
                       "(2020.full.illust_a.jpg.zib); each is changed on its own. Choose, paste or drop a picture: it is made square (the middle of it), " +
                       "304 x 304, JPEG, like the game's. Save writes it into that .zib in WolfX's patch archive, YGO_2020-Ex.dat; the game's own " +
                       "YGO_2020.dat is never written (Yu-Gi-Oh-Core gives the game the patch's copy). The censored .zib is 600 MB, so saving it " +
                       "takes a few seconds.",
            });
            row.Controls.Add(side);
            _artFields.Controls.Add(row);

            foreach (var slot in new[] { _censoredArt, _uncensoredArt })
            {
                var s = slot;
                s.Choose.Click += (_, _) => ChooseArt(s.Zib);
                s.Paste.Click += (_, _) => PasteArt(s.Zib);
                s.Export.Click += (_, _) => ExportArt(s.Zib);
                s.Game.Click += (_, _) => UseGameArt(s.Zib);
                s.Picture.DragEnter += (_, e) =>
                    e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true || e.Data?.GetDataPresent(DataFormats.Bitmap) == true ? DragDropEffects.Copy : DragDropEffects.None;
                s.Picture.DragDrop += (_, e) =>
                {
                    if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } dropped)
                        SetArt(s.Zib, () => File.ReadAllBytes(dropped[0]), Path.GetFileName(dropped[0]));
                    else if (e.Data?.GetData(DataFormats.Bitmap) is Image image)
                        SetArt(s.Zib, () => ImageBytes(image), "the dropped picture");
                };
                PreviewPopOut.Attach(s.Picture, $"Card art ({VanillaArt.Describe(s.Zib).ToLowerInvariant()})", doubleClick: true);
            }
            _addUncensored.Click += (_, _) => ChooseArt(CardArt.UncensoredZib);

            _customArtNote.Controls.Add(CustomNote("A custom card: its art is a file next to cards.json, chosen on the New cards page."));
            _artTab.Controls.Add(_artFields);
            _artTab.Controls.Add(_customArtNote);
        }

        // ---- IGameEditor ----

        public bool Dirty => _changedFiles.Count > 0 || _pendingArt.Count > 0;

        public IReadOnlyCollection<string> Files =>
            [CardPropTable.GamePath, CardIdMap.GamePath, Card_Pass.GamePath, Card_Named.GamePath, CardSameTable.GamePath, CardArt.CensoredZib, CardArt.UncensoredZib,
             .. CardTextTable.Languages.SelectMany(l => new[] { CardTextTable.IndxPath(l), CardTextTable.NamePath(l), CardTextTable.DescPath(l) }),
             .. CardKanaTable.Languages.SelectMany(l => new[] { CardKanaTable.KanaPath(1, l), CardKanaTable.KanaPath(2, l), CardKanaTable.KanaPath(3, l) }),
             .. CardNameSort.Languages.SelectMany(l => new[] { SortPath(l, second: false), SortPath(l, second: true) })];

        private static string SortPath(char language, bool second) =>
            $@"{CardNameSort.GameFolder}\{(second ? CardNameSort.Sort2Name(language) : CardNameSort.SortName(language))}";

        public string SavesTo => "Standard: a game card's stats, text, password, archetypes and same-name card into bin\\CARD_*, its art into the art .zib files. Additional: custom cards are cards.json (New cards); " +
                                 "a game card's changed summoning requirements are Yu-Gi-Oh-Ex\\summoning.json (Yu-Gi-Oh-Effects); genres, related cards and links follow their own pages' rules.";

        public void Open(GameFolderFiles files)
        {
            _files = files;
            _summary.Art = new WorkspaceCardArt(files);
            _texts.Clear();
            _kanas.Clear();
            _sorts.Clear();
            _changedFiles.Clear();
            _changedCards.Clear();
            _pendingArt.Clear();
            _summoning = SummoningFile.Load(files.ExPath(SummoningFile.FileName));
            try
            {
                _idMap = files.Read(CardIdMap.GamePath) is { } ids ? CardIdMap.Parse(ids) : null;
                _props = files.Read(CardPropTable.GamePath) is { } prop ? CardPropTable.Parse(prop) : null;
                _same = files.Read(CardSameTable.GamePath) is { } same ? CardSameTable.Parse(same) : null;
                foreach (char language in CardTextTable.Languages)
                {
                    if (files.Read(CardTextTable.IndxPath(language)) is { } indx && files.Read(CardTextTable.NamePath(language)) is { } names &&
                        files.Read(CardTextTable.DescPath(language)) is { } descs)
                        _texts[language] = CardTextTable.Parse(indx, names, descs);
                }
                foreach (char language in CardKanaTable.Languages)
                {
                    if (files.Read(CardKanaTable.KanaPath(1, language)) is { } kana1 && files.Read(CardKanaTable.KanaPath(2, language)) is { } kana2 &&
                        files.Read(CardKanaTable.KanaPath(3, language)) is { } kana3)
                        _kanas[language] = CardKanaTable.Parse(kana1, kana2, kana3);
                }
                foreach (char language in CardNameSort.Languages)
                {
                    if (files.Read(SortPath(language, second: false)) is { } sort && files.Read(SortPath(language, second: true)) is { } sort2)
                        _sorts[language] = CardNameSort.Parse(sort, sort2);
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
                foreach (var (language, kana) in _kanas)
                {
                    if (!_changedFiles.Contains(CardKanaTable.KanaPath(1, language)))
                        continue;
                    var (kana1, kana2, kana3) = kana.ToBytes();
                    write[CardKanaTable.KanaPath(1, language)] = kana1;
                    write[CardKanaTable.KanaPath(2, language)] = kana2;
                    write[CardKanaTable.KanaPath(3, language)] = kana3;
                }
                if (_changedFiles.Contains(Card_Pass.GamePath))
                    write[Card_Pass.GamePath] = Card_Pass.ToBytes(_passwords);
                if (_changedFiles.Contains(Card_Named.GamePath))
                    write[Card_Named.GamePath] = Card_Named.ToBytes();
                if (_same != null && _changedFiles.Contains(CardSameTable.GamePath))
                    write[CardSameTable.GamePath] = _same.ToBytes();
                if (write.Count > 0)
                    _files.Write(write);
                int art = _pendingArt.Count;
                if (art > 0)
                {
                    UseWaitCursor = true;
                    try
                    {
                        VanillaArt.Save(_files, _pendingArt);
                    }
                    finally
                    {
                        UseWaitCursor = false;
                    }
                    _pendingArt.Clear();
                }
                _changedFiles.Clear();
                _changedCards.Clear();
                _list.Invalidate();

                int pages = 0;
                if (_summoning?.Dirty == true)
                {
                    _summoning.Save();
                    pages++;
                }
                if (_customCards.Dirty && _customCards.SaveRequested?.Invoke() == true)
                    pages++;   // a custom card's requirements changed here: cards.json
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
                               (art > 0 ? $"; {art} card picture{(art == 1 ? "" : "s")} into {_files.Describe(CardArt.CensoredZib)}" : "") +
                               (pages > 0 ? $"; saved {pages} of summoning.json, cards.json and the genres / related cards / links pages." : ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or InvalidDataException)
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
            _customArtNote.Visible = custom;
            _artFields.Visible = !custom;
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
                ShowSameName();
                ShowText();
            }
            finally
            {
                _binding = false;
            }
            ShowSummary();
            ShowArt();
            ShowSummon();
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
                bool hasKana = i > 0 && _kanas.TryGetValue(language, out var kana) && i < kana.Readings.Count;
                _kana.Text = hasKana ? _kanas[language].Readings[i] : "";
                _kana.Enabled = _kanaFromName.Enabled = hasKana;
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

        /// <summary>The card whose name this one is treated as (bin\CARD_Same.bin), and how.</summary>
        private void ShowSameName()
        {
            var row = _shown is int id ? _same?.Find(id) : null;
            _sameName.Text = row is { } r ? $"{NameOf(r.Target)} ({r.Target})" : _same == null ? "(no bin\\CARD_Same.bin)" : "(its own)";
            SelectChoice(_sameMode, (int)(row?.Mode ?? SameNameMode.Always));
            _clearSame.Enabled = row != null;
            _pickSame.Enabled = _sameMode.Enabled = _same != null && _shown is int card && Record(card) != null;
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
            // index letters that came from the old name follow the new one (a typed reading is left alone)
            if (_kanas.TryGetValue(language, out var kana) && i < kana.Readings.Count && kana.Readings[i] == CardKanaTable.FromName(table.Names[i]) &&
                CardKanaTable.FromName(_name.Text) is var letters && letters != kana.Readings[i])
            {
                kana.Readings[i] = letters;
                Changed(id, CardKanaTable.KanaPath(1, language));
                bool was = _binding;
                _binding = true;
                _kana.Text = letters;
                _binding = was;
            }
            table.Names[i] = _name.Text;
            table.Descs[i] = _desc.Text.Replace("\r\n", "\n");
            Changed(id, CardTextTable.NamePath(language));
            if (language == 'E')
                _list.Invalidate();
        }

        private void PickSameName()
        {
            if (_shown is not int id)
                return;
            using var picker = new CardPickerDialog(CardCatalog.Get(this), $"{NameOf(id)}'s name is treated as", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) == DialogResult.OK && picker.Result.Count > 0)
                SetSameName(picker.Result[0].Card.Id);
        }

        private void SameModeEdited()
        {
            if (!_binding && _shown is int id && _same?.Find(id) is { } row)
                SetSameName(row.Target);
        }

        /// <summary>Treats the shown card's name as <paramref name="target"/>'s (null: its own name again). Mode: the combo's.</summary>
        private void SetSameName(int? target)
        {
            if (_shown is not int id || _same == null || Record(id) == null)
                return;
            if (target == id)
            {
                _status.Text = "A card can't be treated as itself: use Own name.";
                return;
            }
            if (target is int t)
            {
                _same.Set(id, t, (SameNameMode)ChoiceOf(_sameMode));
                _status.Text = _same.Find(t) != null
                    ? $"{NameOf(t)} is treated as another card itself; the game doesn't follow chains, so {NameOf(id)} only counts as {NameOf(t)}."
                    : $"{NameOf(id)}'s name is treated as {NameOf(t)} ({CardSameTable.ModeName((SameNameMode)ChoiceOf(_sameMode))}).";
            }
            else if (!_same.Remove(id))
                return;
            bool was = _binding;
            _binding = true;
            try
            {
                ShowSameName();
            }
            finally
            {
                _binding = was;
            }
            Changed(id, CardSameTable.GamePath);
        }

        private void KanaEdited()
        {
            char language = (char)ChoiceOf(_language);
            if (_binding || _shown is not int id || InternalOf(id) is not (int i and > 0) || !_kanas.TryGetValue(language, out var kana) || i >= kana.Readings.Count ||
                kana.Readings[i] == _kana.Text)
                return;
            kana.Readings[i] = _kana.Text;
            Changed(id, CardKanaTable.KanaPath(1, language));
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
                          NameOrderText(i) + (_changedCards.Contains(id) ? "   changed, not saved" : "");
            // new art not saved yet is shown on the face too
            Bitmap? pending = _pendingArt.TryGetValue((CardArt.CensoredZib, id), out var jpeg) && jpeg != null ? Imaging.Decode(jpeg) : null;
            Bitmap? picture = pending ?? CardArt.Get(id);
            if (Record(id) is not { } record)
            {
                _summary.ShowCard(new GameCard { Name = CardCatalog.NameOf(id), Kind = 1 }, info, picture, ownsPicture: pending != null);
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
            }, info, picture, ownsPicture: pending != null);
        }

        /// <summary>
        /// "name order 1234 of 10165" in the Text tab's language: the card's place in the deck editor's name order (bin\CARD_Sort_<lang>.bin), and
        /// a warning when its name no longer fits there (a rename: rebuild on the Name sort page).
        /// </summary>
        private string NameOrderText(int internalId)
        {
            char language = (char)ChoiceOf(_language);
            if (internalId <= 0 || !_sorts.TryGetValue(language, out var sort) || internalId >= sort.Rank.Length)
                return "";
            int rank = sort.Rank[internalId];
            string text = $"   name order {rank} of {sort.Count - 1}";
            if (!CardNameSort.RebuildableLanguages.Contains(language) || !_texts.TryGetValue(language, out var names))
                return text;
            string Name(int position) => position > 0 && position < sort.Order.Length && sort.Order[position] < names.Names.Count ? names.Names[sort.Order[position]] : "";
            string own = Name(rank);
            bool afterPrevious = rank <= 1 || string.Compare(Name(rank - 1), own, StringComparison.OrdinalIgnoreCase) <= 0;
            bool beforeNext = rank + 1 >= sort.Order.Length || string.Compare(own, Name(rank + 1), StringComparison.OrdinalIgnoreCase) <= 0;
            return afterPrevious && beforeNext ? text : text + " (out of place after a rename: rebuild it on Name sort)";
        }

        // ---- art ----

        private void ShowArt()
        {
            bool game = _shown is int shown && _files != null && Custom(shown) == null;
            if (!game || _shown is not int id || _files == null)
            {
                _censoredArt.SetPicture(null);
                _uncensoredArt.SetPicture(null);
                _uncensoredArt.Visible = _addUncensored.Visible = false;
                return;
            }
            // the uncensored slot is shown for the cards that have an uncensored picture (or are getting one)
            bool hasUncensored = CardArt.Has(CardArt.UncensoredZib, id);
            bool pendingUncensored = _pendingArt.TryGetValue((CardArt.UncensoredZib, id), out var newUncensored);
            _uncensoredArt.Visible = pendingUncensored ? newUncensored != null || hasUncensored : hasUncensored;
            _addUncensored.Visible = !_uncensoredArt.Visible;
            ShowSlot(_censoredArt, id);
            if (_uncensoredArt.Visible)
                ShowSlot(_uncensoredArt, id);
        }

        private void ShowSlot(ArtSlot slot, int id)
        {
            bool uncensored = slot.Zib == CardArt.UncensoredZib;
            bool pending = _pendingArt.TryGetValue((slot.Zib, id), out var jpeg);
            Bitmap? saved = !uncensored ? CardArt.Get(id) : CardArt.Has(slot.Zib, id) ? CardArt.Get(id, uncensored: true) : null;
            slot.SetPicture(pending ? (jpeg != null ? Imaging.Decode(jpeg) : null) : saved != null ? new Bitmap(saved) : null);   // a copy: CardArt's are cached
            bool changed = VanillaArt.IsChanged(_files!, id, slot.Zib);
            bool original = VanillaArt.Original(_files!, id, slot.Zib) != null;
            slot.Info.Text =
                pending ? (jpeg == null ? "Taken out when you save: the game shows the censored picture." : "New picture: written into the .zib when you save (Ctrl+S).") :
                changed ? (original ? "Changed, saved in " : "Added, saved in ") + _files!.Describe(slot.Zib) + "." :
                slot.Picture.Image == null ? "No picture in the .zib for this card." : "The game's own picture.";
            slot.Game.Enabled = pending || changed;
            slot.Game.Text = !original && (changed || pending) ? "Take it out" : "Use the game's";
            slot.Export.Enabled = slot.Picture.Image != null;
        }

        private static byte[] ImageBytes(Image image)
        {
            using var stream = new MemoryStream();
            image.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }

        private void ArtChanged(int id)
        {
            _changedCards.Add(id);
            _list.Invalidate();
            ShowArt();
            ShowSummary();
        }

        /// <summary>The picture becomes the card's new picture in that .zib (made into the game's JPEG), saved with the rest on Save.</summary>
        private void SetArt(string zib, Func<byte[]> picture, string from)
        {
            if (_shown is not int id || Custom(id) != null)
                return;
            try
            {
                _pendingArt[(zib, id)] = VanillaArt.ToGameJpeg(picture());
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or ExternalException or UnauthorizedAccessException)
            {
                _status.Text = $"Couldn't use {from} as art: {ex.Message}";
                return;
            }
            _status.Text = $"New {VanillaArt.Describe(zib).ToLowerInvariant()} picture for {NameOf(id)} from {from}: written into {zib} when you save.";
            ArtChanged(id);
        }

        private void ChooseArt(string zib)
        {
            using var dialog = new OpenFileDialog
            {
                Title = $"Choose the {VanillaArt.Describe(zib).ToLowerInvariant()} picture",
                Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                SetArt(zib, () => File.ReadAllBytes(dialog.FileName), Path.GetFileName(dialog.FileName));
        }

        private void PasteArt(string zib)
        {
            if (Clipboard.ContainsFileDropList() && Clipboard.GetFileDropList() is { Count: > 0 } list && list[0] is string file)
                SetArt(zib, () => File.ReadAllBytes(file), Path.GetFileName(file));
            else if (Clipboard.GetImage() is { } image)
                using (image)
                    SetArt(zib, () => ImageBytes(image), "the clipboard");
            else
                _status.Text = "The clipboard has no picture.";
        }

        private void ExportArt(string zib)
        {
            if (_shown is not int id || _files == null)
                return;
            byte[]? jpeg = _pendingArt.TryGetValue((zib, id), out var pending) ? pending : VanillaArt.Current(_files, id, zib);
            if (jpeg == null)
                return;
            string suffix = zib == CardArt.UncensoredZib ? "_uncensored" : "";
            using var dialog = new SaveFileDialog { Title = "Export card art", FileName = $"{id}{suffix}.jpg", Filter = "JPEG (*.jpg)|*.jpg" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            File.WriteAllBytes(dialog.FileName, jpeg);
            _status.Text = $"Exported to {dialog.FileName}.";
        }

        /// <summary>
        /// Back to the game's picture in that .zib: drops a new picture not saved yet, or (when one was saved) puts the game's back on the
        /// next save. An uncensored picture the game doesn't have is taken out again.
        /// </summary>
        private void UseGameArt(string zib)
        {
            if (_shown is not int id || _files == null)
                return;
            bool pending = _pendingArt.Remove((zib, id));
            if (VanillaArt.IsChanged(_files, id, zib))
            {
                _pendingArt[(zib, id)] = VanillaArt.Original(_files, id, zib);   // null = take the added picture out
                _status.Text = $"The game's {VanillaArt.Describe(zib).ToLowerInvariant()} picture comes back when you save.";
            }
            else if (pending)
                _status.Text = "New picture dropped: the card keeps the game's.";
            ArtChanged(id);
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
