using System.IO;
using Types;
using WolfX.Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The game's Related cards data (File Type Libraries/RelatedCards, docs/RelatedCards.md): bin/tagdata.bin (which cards are related
    /// to each card, and the tag that explains why) and bin/taginfo_&lt;lang&gt;.bin (what each tag means), shown in the deck editor's
    /// Related cards panel.
    /// "Cards": each card's related cards; add some (picking the tag), change their tag, remove them, or start a list for a new card.
    /// "Tags": every tag; edit its group, conditions, key and text, or add new ones.
    /// Opens the game's data with Yu-Gi-Oh-Ex\relatedcards.json on top. Saving puts the game's tags and the game's cards' related cards
    /// into tagdata.bin and taginfo_*.bin (tagdata.bin is indexed by internal id); new tags, custom cards (15300+) and related cards that
    /// are custom cards or use a new tag go to relatedcards.json, which Yu-Gi-Oh-MoreCards applies.
    /// </summary>
    public sealed class RelatedCardsEditor : UserControl, IGameEditor, ICardFocus
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _save;
        private readonly ToolStripComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 45 };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly TabControl _pages = new() { Dock = DockStyle.Fill };

        // cards page
        private readonly ToolStripComboBox _cardFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
        private readonly ToolStripTextBox _cardFind = new() { Width = 170, ToolTipText = "Konami id or part of a card name" };
        private readonly ListView _cardList = NewList(virtualMode: true, multi: false);
        private readonly ListView _relatedList = NewList(virtualMode: false, multi: true);
        private readonly Label _cardHeading = Heading();
        private readonly FlowLayoutPanel _cardButtons = new() { Dock = DockStyle.Bottom, Height = 34, WrapContents = false };

        // tags page
        private readonly ToolStripTextBox _tagFind = new() { Width = 200, ToolTipText = "Tag id, or part of its key or text" };
        private readonly ListView _tagList = NewList(virtualMode: true, multi: false);
        private readonly ComboBox _group = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
        private readonly NumericUpDown _flag1 = new() { Minimum = 0, Maximum = 255, Width = 55 };
        private readonly ComboBox[] _condType = new ComboBox[TagInfo.ConditionCount];
        private readonly ComboBox[] _condOp = new ComboBox[TagInfo.ConditionCount];
        private readonly ComboBox[] _condValue = new ComboBox[TagInfo.ConditionCount];
        private readonly TextBox _key = new() { Width = 420 };
        private readonly DataGridView _texts = new()
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.CellSelect,
        };
        private readonly Label _tagHeading = Heading();
        private readonly Label _tagPreview = new() { Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText };
        private readonly Panel _tagEditor = new() { Dock = DockStyle.Fill, Enabled = false };

        private sealed record CardRow(int Card, int Count, string Note);

        private CardIdMap? _idMap;
        private TagDataTable? _tagData;                          // as read (keeps lists of internal ids no Konami id reaches)
        private TagInfoTable? _tags, _baseTags;
        private Dictionary<int, List<RelatedCard>> _cards = [], _baseCards = [];
        private readonly HashSet<int> _newCards = [];
        private HashSet<int> _customIds = [];                    // the cards.json cards whose related cards were read from there
        private ICustomCardStore? _store;
        private List<CardRow> _cardRows = [];
        private List<int> _tagRows = [];
        private Dictionary<int, int> _tagUse = [];
        private GameFolderFiles? _gameFiles;
        private readonly SplitContainer _cardSplit = new() { Dock = DockStyle.Fill };
        private readonly ToolStrip _cardTools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private bool _changed, _loadingTag;

        private string JsonPath => _gameFiles!.ExPath(RelatedCardsJson.FileName);

        public IReadOnlyCollection<string> Files =>
            [RelatedCards.TagDataPath, CardIdMap.GamePath, .. RelatedCards.Languages.Select(RelatedCards.TagInfoPath)];

        public string SavesTo => $"Standard: {RelatedCards.TagDataPath} and taginfo_<lang>.bin. Additional: a custom card's related cards in its cards.json entry, new tags and other cards in Yu-Gi-Oh-Ex\\{RelatedCardsJson.FileName} (needs Yu-Gi-Oh-MoreCards).";

        private char Language => _language.SelectedItem is string text && text.Length > 0 ? text[0] : 'E';

        public bool Dirty => _changed;

        public RelatedCardsEditor()
        {

            ListPick.FirstWhenShown(_cardList);   // open on the first entry, not an empty "Pick a ..." panel
            _save = Button("Save", "The game's tags and cards into tagdata.bin / taginfo, new tags and custom cards into relatedcards.json (Ctrl+S)", () => Save());
            _tools.Items.AddRange([_save, new ToolStripSeparator(), new ToolStripLabel("Text language:")]);
            foreach (char language in RelatedCards.Languages)
                _language.Items.Add(language.ToString());
            _language.SelectedIndex = 0;
            _language.SelectedIndexChanged += (_, _) => { RefillCards(); RefillTags(); };
            _tools.Items.Add(_language);

            _pages.TabPages.Add(BuildCardsPage());
            _pages.TabPages.Add(BuildTagsPage());

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(_pages);
            Controls.Add(_tools);
            Controls.Add(status);
            SetEditable(false);
            _status.Text = "Open the game data (File > Open).";
        }

        public bool CardOnly
        {
            get => _cardSplit.Panel1Collapsed;
            set
            {
                _cardSplit.Panel1Collapsed = value;
                _tools.Visible = _cardTools.Visible = !value;
                if (value)
                    _pages.SelectedIndex = 0;
            }
        }

        public void ShowCard(int konamiId)
        {
            if (_tags == null)
                return;
            if (!_cardRows.Any(row => row.Card == konamiId))
                _newCards.Add(konamiId);
            if (_cardFilter.SelectedIndex == 1)
                _cardFilter.SelectedIndex = 0;
            _cardFind.Text = "";
            RefillCards(konamiId);
        }

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            if (!Open(files.Read(RelatedCards.TagDataPath), language => files.Read(RelatedCards.TagInfoPath(language)), files.Read(CardIdMap.GamePath),
                    files.Describe(RelatedCards.TagDataPath)))
            {
                _tags = null;
                SetEditable(false);
            }
        }

        // ---- layout ----

        private static ListView NewList(bool virtualMode, bool multi) => new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, HideSelection = false, VirtualMode = virtualMode,
            MultiSelect = multi,
        };

        private static Label Heading() => new() { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        private static Button SmallButton(string text, Action click)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += (_, _) => click();
            return button;
        }

        private TabPage BuildCardsPage()
        {
            var page = new TabPage("Cards") { UseVisualStyleBackColor = true };
            var tools = _cardTools;
            tools.Items.Add(new ToolStripLabel("Show:"));
            _cardFilter.Items.AddRange(["Cards with related cards", "Changed", "All cards"]);
            _cardFilter.SelectedIndex = 0;
            _cardFilter.SelectedIndexChanged += (_, _) => RefillCards();
            tools.Items.Add(_cardFilter);
            tools.Items.Add(new ToolStripLabel("Find:"));
            _cardFind.TextChanged += (_, _) => RefillCards();
            tools.Items.Add(_cardFind);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Button("New card...", "Give related cards to a card that has none (a custom card, for example)", NewCard));

            _cardList.Columns.Add("Konami id", 75);
            _cardList.Columns.Add("Card", 250);
            _cardList.Columns.Add("Related", 60);
            _cardList.Columns.Add("", 110);
            _cardList.RetrieveVirtualItem += (_, e) =>
            {
                if (e.ItemIndex >= _cardRows.Count) { e.Item = new ListViewItem(new string[_cardList.Columns.Count]); return; }   // stale index while the list shrinks
                var row = _cardRows[e.ItemIndex];
                e.Item = new ListViewItem([row.Card.ToString(), CardName(row.Card), row.Count.ToString(), row.Note]);
            };
            _cardList.SelectedIndexChanged += (_, _) => ShowRelated();

            _relatedList.Columns.Add("Konami id", 75);
            _relatedList.Columns.Add("Related card", 230);
            _relatedList.Columns.Add("Tag", 50);
            _relatedList.Columns.Add("Why", 380);
            _relatedList.Columns.Add("", 70);
            _relatedList.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Delete)
                    RemoveRelated();
            };
            _relatedList.DoubleClick += (_, _) => ChangeTag();
            _cardButtons.Controls.AddRange([SmallButton("Add related cards...", AddRelated), SmallButton("Change tag...", ChangeTag),
                SmallButton("Remove", RemoveRelated), SmallButton("Show tag", ShowSelectedTag)]);

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_relatedList);
            right.Controls.Add(_cardHeading);
            right.Controls.Add(_cardButtons);
            _cardSplit.Panel1.Controls.Add(_cardList);
            _cardSplit.Panel2.Controls.Add(right);
            page.Controls.Add(_cardSplit);
            page.Controls.Add(tools);
            page.Layout += (_, _) =>
            {
                if (!_cardSplit.Panel1Collapsed && _cardSplit.Width > 700 && _cardSplit.SplitterDistance < 250)
                    _cardSplit.SplitterDistance = _cardSplit.Width * 38 / 100;
            };
            return page;
        }

        private TabPage BuildTagsPage()
        {
            var page = new TabPage("Tags") { UseVisualStyleBackColor = true };
            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(new ToolStripLabel("Find:"));
            _tagFind.TextChanged += (_, _) => RefillTags();
            tools.Items.Add(_tagFind);
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Button("New tag", "Add a tag after the game's (edit it on the right, then Apply)", () => NewTag(null)));
            tools.Items.Add(Button("Duplicate", "Add a copy of the selected tag", () => NewTag(SelectedTagId())));

            _tagList.Columns.Add("Id", 50);
            _tagList.Columns.Add("Group", 70);
            _tagList.Columns.Add("Key", 260);
            _tagList.Columns.Add("Text", 260);
            _tagList.Columns.Add("Cards", 55);
            _tagList.RetrieveVirtualItem += (_, e) =>
            {
                if (e.ItemIndex >= _tagRows.Count) { e.Item = new ListViewItem(new string[_tagList.Columns.Count]); return; }   // stale index while the list shrinks
                int id = _tagRows[e.ItemIndex];
                var tag = _tags!.Tags[id];
                e.Item = new ListViewItem([id.ToString(), tag.Group.ToString(), tag.Key, tag.Describe(Language), _tagUse.GetValueOrDefault(id).ToString()]);
            };
            _tagList.SelectedIndexChanged += (_, _) => ShowTag();

            // the tag editor
            _group.Items.AddRange(["Name (\"Related to: ...\", shows Text)", "Affects ({AD}: related card affects...)", "Matches ({FIND}: related card matches...)"]);
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false };
            top.Controls.AddRange([new Label { Text = "Group:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, _group,
                new Label { Text = "Flag1:", AutoSize = true, Margin = new Padding(12, 7, 3, 3) }, _flag1]);

            var conditions = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(3) };
            conditions.Controls.Add(new Label { Text = "Condition", AutoSize = true });
            conditions.Controls.Add(new Label { Text = "Type", AutoSize = true });
            conditions.Controls.Add(new Label { Text = "Op", AutoSize = true });
            conditions.Controls.Add(new Label { Text = "Value", AutoSize = true });
            for (int i = 0; i < TagInfo.ConditionCount; i++)
            {
                int slot = i;
                _condType[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
                _condType[i].Items.Add("(none)");
                foreach (var name in TagInfo.TypeKeys.Values)
                    _condType[i].Items.Add(name);
                _condOp[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 55 };
                _condOp[i].Items.AddRange(TagInfo.OpText.Values.Cast<object>().ToArray());
                _condValue[i] = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 150 };
                _condType[i].SelectedIndexChanged += (_, _) => ConditionTypeChanged(slot);
                conditions.Controls.Add(new Label { Text = $"{i + 1}", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
                conditions.Controls.Add(_condType[i]);
                conditions.Controls.Add(_condOp[i]);
                conditions.Controls.Add(_condValue[i]);
            }

            var keyRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false };
            keyRow.Controls.AddRange([new Label { Text = "Key:", AutoSize = true, Margin = new Padding(3, 7, 3, 3) }, _key,
                SmallButton("From conditions", () => _key.Text = ReadEditor(new TagInfo()).BuildKey())]);

            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Language", ReadOnly = true, FillWeight = 12 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Text (shown for Name tags; the game builds the others from the conditions)", FillWeight = 88 });

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, WrapContents = false };
            buttons.Controls.AddRange([SmallButton("Apply", ApplyTag), SmallButton("Revert", ShowTag), SmallButton("Cards using it", CardsUsingTag)]);

            var textsPanel = new Panel { Dock = DockStyle.Fill };
            textsPanel.Controls.Add(_texts);
            _tagEditor.Controls.Add(textsPanel);
            _tagEditor.Controls.Add(keyRow);
            _tagEditor.Controls.Add(conditions);
            _tagEditor.Controls.Add(top);
            _tagEditor.Controls.Add(buttons);

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_tagEditor);
            right.Controls.Add(_tagPreview);
            right.Controls.Add(_tagHeading);
            var split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(_tagList);
            split.Panel2.Controls.Add(right);
            page.Controls.Add(split);
            page.Controls.Add(tools);
            page.Layout += (_, _) =>
            {
                if (split.Width > 700 && split.SplitterDistance < 250)
                    split.SplitterDistance = split.Width * 45 / 100;
            };
            return page;
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

        private void SetEditable(bool editable)
        {
            _save.Enabled = editable;
            _cardButtons.Enabled = editable;
        }

        // ---- names ----

        private static string CardName(int id) => CardCatalog.NameOf(id);

        private string TagText(int tagId) =>
            _tags != null && tagId >= 0 && tagId < _tags.Tags.Count ? _tags.Tags[tagId].Describe(Language) : $"(no tag {tagId})";

        // ---- opening / saving ----

        private bool Open(byte[]? tagData, Func<char, byte[]?> tagInfo, byte[]? intIds, string from)
        {
            if (tagData == null || intIds == null)
            {
                _status.Text = $"{from}: tagdata.bin or CARD_IntID.bin is missing.";
                return false;
            }
            var infoFiles = new Dictionary<char, byte[]>();
            foreach (char language in RelatedCards.Languages)
                if (tagInfo(language) is byte[] bytes)
                    infoFiles[language] = bytes;
            if (infoFiles.Count == 0)
            {
                _status.Text = $"{from}: no taginfo_*.bin.";
                return false;
            }
            try
            {
                CardCatalog.Get(this);
                _idMap = CardIdMap.Parse(intIds);
                _tagData = TagDataTable.Parse(tagData);
                _tags = TagInfoTable.Parse(infoFiles);
                _cards = [];
                foreach (var (internalId, konami) in _idMap.KonamiByInternal())
                    if (internalId < TagDataTable.EntryCount && _tagData.Lists[internalId].Count > 0)
                        _cards[konami] = [.. _tagData.Lists[internalId]];
                _baseTags = _tags.Clone();
                _baseCards = _cards.ToDictionary(c => c.Key, c => c.Value.ToList());
                int fromJson = RelatedCardsJson.Apply(RelatedCardsJson.Load(JsonPath), _tags, _cards);
                _customIds = [];
                ReadCustomCards();
                _newCards.Clear();
                _changed = false;
                SetEditable(true);
                RefillCards();
                RefillTags();
                _status.Text = $"{from}: {_cards.Count} cards with related cards ({_cards.Values.Sum(l => l.Count)} links), {_tags.Tags.Count} tags, " +
                    $"languages {string.Join("", _tags.Languages)}" + (fromJson > 0 ? $"; {fromJson} changes from {RelatedCardsJson.FileName}" : "") + ".";
                return true;
            }
            catch (InvalidDataException ex)
            {
                _status.Text = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Standard content: the game's tags (edited) go into taginfo_*.bin, and every game card's related cards into tagdata.bin, leaving out
        /// related cards that are custom cards or use a new tag. Additional content: new tags, custom cards' lists and the left-out related
        /// cards go to relatedcards.json; it is deleted when there is nothing in it.
        /// </summary>
        public bool Save()
        {
            if (_tags == null || _tagData == null || _idMap == null || _baseTags == null || _gameFiles == null)
                return false;
            ApplyTagIfEdited();
            try
            {
                int gameTags = _baseTags.Tags.Count;
                var standardTags = _tags.Clone();
                if (standardTags.Tags.Count > gameTags)
                    standardTags.Tags.RemoveRange(gameTags, standardTags.Tags.Count - gameTags);
                bool IsStandard(RelatedCard related) => GameContent.IsGameCard(related.KonamiId) && related.TagId < gameTags;

                var data = new TagDataTable();
                for (int i = 0; i < TagDataTable.EntryCount; i++)
                    data.Lists[i].AddRange(_tagData.Lists[i]);
                var inBin = new Dictionary<int, List<RelatedCard>>();
                foreach (var (internalId, konami) in _idMap.KonamiByInternal())
                {
                    if (internalId >= TagDataTable.EntryCount)
                        continue;
                    data.Lists[internalId].Clear();
                    if (_cards.TryGetValue(konami, out var list))
                        data.Lists[internalId].AddRange(list.Where(IsStandard));
                    if (data.Lists[internalId].Count > 0)
                        inBin[konami] = [.. data.Lists[internalId]];
                }

                var write = new Dictionary<string, byte[]>();
                byte[] tagData = data.ToBytes();
                if (!tagData.AsSpan().SequenceEqual(_tagData.ToBytes()))
                    write[RelatedCards.TagDataPath] = tagData;
                foreach (char language in standardTags.Languages)
                {
                    byte[] info = standardTags.ToBytes(language);
                    if (!info.AsSpan().SequenceEqual(_baseTags.ToBytes(language)))
                        write[RelatedCards.TagInfoPath(language)] = info;
                }
                if (write.Count > 0)
                    _gameFiles.Write(write);
                _tagData = data;

                // cards.json's cards keep theirs in their own entry; relatedcards.json gets the tags and the rest
                var root = RelatedCardsJson.Diff(standardTags, _tags, inBin.Where(c => !CustomCards.Has(c.Key)).ToDictionary(c => c.Key, c => c.Value),
                    _cards.Where(c => !CustomCards.Has(c.Key)).ToDictionary(c => c.Key, c => c.Value), id => CardName(id));
                int tags = ((System.Text.Json.Nodes.JsonArray)root["tags"]!).Count, cards = ((System.Text.Json.Nodes.JsonArray)root["cards"]!).Count;
                if (tags + cards > 0)
                    RelatedCardsJson.Save(JsonPath, root);
                else if (File.Exists(JsonPath))
                    File.Delete(JsonPath);
                foreach (int id in _cards.Keys.Where(CustomCards.Has).ToList())
                    PushCustom(id);
                bool cardsSaved = CustomCards.Store?.Save() ?? true;
                _baseTags = _tags.Clone();
                _baseCards = _cards.ToDictionary(c => c.Key, c => c.Value.ToList());
                _changed = false;
                RefillCards();
                _status.Text = (write.Count > 0 ? $"Saved {string.Join(", ", write.Keys.Select(Path.GetFileName))} into {_gameFiles.Describe(RelatedCards.TagDataPath)}" : "The game's files are unchanged") +
                    (tags + cards > 0 ? $"; {tags} new tags and {cards} cards to {JsonPath} (Yu-Gi-Oh-MoreCards applies it)" : "") +
                    (cardsSaved ? "; custom cards' related cards are in cards.json." : "; cards.json NOT saved (see the New cards page).");
                return cardsSaved;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        private void Changed(string message)
        {
            _changed = true;
            _status.Text = message;
        }

        /// <summary>A card's related cards changed: a custom card's go straight into its cards.json entry, a game card's wait for Save.</summary>
        private void CardChanged(int card, string message)
        {
            if (CustomCards.Has(card))
            {
                PushCustom(card);
                _baseCards[card] = [.. ListOf(card)];   // cards.json is saved on its own: not a change of this page
                _status.Text = message + " (cards.json)";
            }
            else
                Changed(message);
            RefillCards(card);
        }

        // ---- custom cards (cards.json "related") ----

        /// <summary>
        /// The cards.json cards' related cards from their entries (a card without "related" keeps what relatedcards.json gave it, which moves
        /// into cards.json on the next save). Runs on open and whenever cards.json's card list changes.
        /// </summary>
        private void ReadCustomCards()
        {
            if (!ReferenceEquals(_store, CustomCards.Store))
            {
                if (_store != null)
                    _store.Changed -= CustomCardsChanged;
                _store = CustomCards.Store;
                if (_store != null)
                    _store.Changed += CustomCardsChanged;
            }
            var now = _store?.Ids.ToHashSet() ?? [];
            foreach (int gone in _customIds.Where(id => !now.Contains(id)))
            {
                _cards.Remove(gone);   // deleted or renumbered on New cards: its related cards went with its entry
                _baseCards.Remove(gone);
            }
            foreach (int id in now)
            {
                if (_store!.Get(id, "related") is { } related)
                    _cards[id] = RelatedCardsJson.FromCardJson(related);
                else if (_customIds.Contains(id))
                    _cards.Remove(id);
                if (_cards.TryGetValue(id, out var list))
                    _baseCards[id] = [.. list];
            }
            _customIds = now;
        }

        private void CustomCardsChanged()
        {
            if (_tags == null)
                return;
            ReadCustomCards();
            RefillCards();
        }

        /// <summary>Puts a custom card's related cards into its cards.json entry.</summary>
        private void PushCustom(int card) =>
            CustomCards.SetIfChanged(card, "related", RelatedCardsJson.ToCardJson(_cards.GetValueOrDefault(card) ?? [], id => CardName(id)));

        // ---- cards page ----

        private HashSet<int> ChangedCards()
        {
            var changed = new HashSet<int>();
            foreach (int card in _cards.Keys.Concat(_baseCards.Keys))
            {
                var now = _cards.GetValueOrDefault(card) ?? [];
                var before = _baseCards.GetValueOrDefault(card) ?? [];
                if (now.Count != before.Count || now.Except(before).Any())
                    changed.Add(card);
            }
            return changed;
        }

        private void RefillCards(int? select = null)
        {
            if (_tags == null)
                return;
            select ??= SelectedCard();
            var changed = ChangedCards();
            string find = _cardFind.Text.Trim();
            IEnumerable<int> cards = _cardFilter.SelectedIndex switch
            {
                1 => changed.Concat(_newCards).Distinct(),
                2 => CardCatalog.Get(this).Cards.Select(c => c.Id).Concat(_cards.Keys).Concat(_newCards).Distinct(),
                _ => _cards.Where(c => c.Value.Count > 0).Select(c => c.Key).Concat(_newCards).Distinct(),
            };
            _cardRows = cards.Order()
                .Where(card => find.Length == 0 || card.ToString() == find || CardName(card).Contains(find, StringComparison.OrdinalIgnoreCase))
                .Select(card =>
                {
                    int count = _cards.GetValueOrDefault(card)?.Count ?? 0;
                    string note = _newCards.Contains(card) && count == 0 ? "new (none yet)"
                        : !_baseCards.ContainsKey(card) && count > 0 ? "new"
                        : changed.Contains(card) ? "changed" : "";
                    return new CardRow(card, count, note);
                }).ToList();
            _cardList.VirtualListSize = _cardRows.Count;
            _cardList.SelectedIndices.Clear();
            int index = select is int wanted ? _cardRows.FindIndex(row => row.Card == wanted) : -1;
            if (index >= 0)
            {
                _cardList.SelectedIndices.Add(index);
                _cardList.EnsureVisible(index);
            }
            _cardList.Invalidate();
            ShowRelated();
        }

        private int? SelectedCard() =>
            _cardList.SelectedIndices.Count > 0 && _cardList.SelectedIndices[0] < _cardRows.Count ? _cardRows[_cardList.SelectedIndices[0]].Card : null;

        private void ShowRelated()
        {
            _relatedList.BeginUpdate();
            _relatedList.Items.Clear();
            if (SelectedCard() is int card)
            {
                _cardHeading.Text = $"Related to {CardName(card)} ({card}):";
                var before = _baseCards.GetValueOrDefault(card) ?? [];
                var list = _cards.GetValueOrDefault(card) ?? [];
                foreach (var related in TagDataTable.Sorted(list))
                    _relatedList.Items.Add(new ListViewItem([related.KonamiId.ToString(), CardName(related.KonamiId), related.TagId.ToString(),
                        TagText(related.TagId), before != null && !before.Contains(related) ? "added" : ""]) { Tag = related });
                if (before != null)
                    foreach (var gone in before.Where(r => !list.Contains(r)))
                        _relatedList.Items.Add(new ListViewItem([gone.KonamiId.ToString(), CardName(gone.KonamiId), gone.TagId.ToString(), TagText(gone.TagId), "removed"])
                        {
                            ForeColor = SystemColors.GrayText,
                        });
            }
            else
                _cardHeading.Text = _tags == null ? "" : "Pick a card.";
            _relatedList.EndUpdate();
            _cardButtons.Enabled = SelectedCard() != null;
        }

        private List<RelatedCard> SelectedRelated() => _relatedList.SelectedItems.Cast<ListViewItem>().Select(i => i.Tag).OfType<RelatedCard>().ToList();

        private List<RelatedCard> ListOf(int card)
        {
            if (!_cards.TryGetValue(card, out var list))
                _cards[card] = list = [];
            return list;
        }

        private void NewCard()
        {
            if (_tags == null)
                return;
            using var picker = new CardPickerDialog(CardCatalog.Get(this), "Give related cards to a card", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                return;
            int card = picker.Result[0].Card.Id;
            if ((_cards.GetValueOrDefault(card)?.Count ?? 0) == 0)
                _newCards.Add(card);
            _cardFind.Text = "";
            if (_cardFilter.SelectedIndex == 1 && !_newCards.Contains(card))
                _cardFilter.SelectedIndex = 0;
            RefillCards(card);
            _status.Text = $"{CardName(card)} ({card}): add its related cards." +
                (_idMap!.InternalOf(card) == 0 ? $" It isn't in CARD_IntID.bin, so it is saved to {RelatedCardsJson.FileName}." : "");
        }

        private void AddRelated()
        {
            if (SelectedCard() is not int card || _tags == null)
                return;
            using var picker = new CardPickerDialog(CardCatalog.Get(this), $"Cards related to {CardName(card)}", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                return;
            if (PickTag($"Why are they related to {CardName(card)}?", null) is not int tag)
                return;
            var list = ListOf(card);
            int added = 0;
            foreach (var (entry, _) in picker.Result)
            {
                var related = new RelatedCard(entry.Id, tag);
                if (list.Contains(related))
                    continue;
                list.Add(related);
                added++;
            }
            CardChanged(card, $"Added {added} related cards to {CardName(card)} with tag {tag}.");
        }

        private void ChangeTag()
        {
            if (SelectedCard() is not int card || SelectedRelated() is not { Count: > 0 } selected)
                return;
            if (PickTag($"New tag for {selected.Count} related cards", selected[0].TagId) is not int tag)
                return;
            var list = ListOf(card);
            foreach (var related in selected)
            {
                int at = list.IndexOf(related);
                if (at >= 0)
                    list[at] = related with { TagId = tag };
            }
            CardChanged(card, $"{selected.Count} related cards of {CardName(card)} now use tag {tag}.");
        }

        private void RemoveRelated()
        {
            if (SelectedCard() is not int card || SelectedRelated() is not { Count: > 0 } selected)
                return;
            var list = ListOf(card);
            int removed = selected.Count(related => list.Remove(related));
            CardChanged(card, $"Removed {removed} related cards from {CardName(card)}.");
        }

        private void ShowSelectedTag()
        {
            if (SelectedRelated() is { Count: > 0 } selected)
                SelectTag(selected[0].TagId);
        }

        /// <summary>A tag picker: every tag with its meaning, filterable.</summary>
        private int? PickTag(string title, int? current)
        {
            using var form = new Form
            {
                Text = title, Size = new Size(760, 560), StartPosition = FormStartPosition.CenterParent, ShowInTaskbar = false, MinimizeBox = false,
            };
            var filter = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Filter: tag id, key or text..." };
            var list = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
            var all = _tags!.Tags.Select((tag, id) => (id, text: $"{id}: {tag.Describe(Language)}   [{tag.Key}]")).ToList();
            void Fill()
            {
                string find = filter.Text.Trim();
                list.BeginUpdate();
                list.Items.Clear();
                foreach (var (id, text) in all)
                    if (find.Length == 0 || id.ToString() == find || text.Contains(find, StringComparison.OrdinalIgnoreCase))
                        list.Items.Add(new TagItem(id, text));
                list.EndUpdate();
                if (current is int wanted)
                    list.SelectedItem = list.Items.Cast<TagItem>().FirstOrDefault(i => i.Id == wanted);
            }
            filter.TextChanged += (_, _) => Fill();
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 38, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(4) };
            bottom.Controls.AddRange([cancel, ok]);
            list.DoubleClick += (_, _) => form.DialogResult = DialogResult.OK;
            form.Controls.Add(list);
            form.Controls.Add(filter);
            form.Controls.Add(bottom);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            Fill();
            return form.ShowDialog(this) == DialogResult.OK && list.SelectedItem is TagItem item ? item.Id : null;
        }

        private sealed record TagItem(int Id, string Text)
        {
            public override string ToString() => Text;
        }

        // ---- tags page ----

        private void RefillTags(int? select = null)
        {
            if (_tags == null)
                return;
            select ??= SelectedTagId();
            _tagUse = _cards.Values.SelectMany(l => l).GroupBy(r => r.TagId).ToDictionary(g => g.Key, g => g.Count());
            string find = _tagFind.Text.Trim();
            _tagRows = Enumerable.Range(0, _tags.Tags.Count).Where(id =>
            {
                if (find.Length == 0 || id.ToString() == find)
                    return true;
                var tag = _tags.Tags[id];
                return tag.Key.Contains(find, StringComparison.OrdinalIgnoreCase) || tag.Describe(Language).Contains(find, StringComparison.OrdinalIgnoreCase);
            }).ToList();
            _tagList.VirtualListSize = _tagRows.Count;
            _tagList.SelectedIndices.Clear();
            int index = select is int wanted ? _tagRows.IndexOf(wanted) : -1;
            if (index >= 0)
            {
                _tagList.SelectedIndices.Add(index);
                _tagList.EnsureVisible(index);
            }
            _tagList.Invalidate();
            ShowTag();
        }

        private int? SelectedTagId() =>
            _tagList.SelectedIndices.Count > 0 && _tagList.SelectedIndices[0] < _tagRows.Count ? _tagRows[_tagList.SelectedIndices[0]] : null;

        private void SelectTag(int id)
        {
            _pages.SelectedIndex = 1;
            _tagFind.Text = "";
            RefillTags(id);
        }

        private int? _shownTag;

        private void ShowTag()
        {
            ApplyTagIfEdited();
            _loadingTag = true;
            try
            {
                _shownTag = SelectedTagId();
                if (_shownTag is not int id || _tags == null)
                {
                    _tagHeading.Text = _tags == null ? "" : "Pick a tag.";
                    _tagPreview.Text = "";
                    _tagEditor.Enabled = false;
                    return;
                }
                var tag = _tags.Tags[id];
                bool isNew = _baseTags != null ? id >= _baseTags.Tags.Count : false;
                _tagHeading.Text = $"Tag {id}{(isNew ? " (new)" : "")}: used by {_tagUse.GetValueOrDefault(id)} related-card links";
                _tagPreview.Text = tag.Describe(Language);
                _group.SelectedIndex = Math.Clamp((int)tag.Group, 0, 2);
                _flag1.Value = tag.Flag1;
                for (int i = 0; i < TagInfo.ConditionCount; i++)
                {
                    var c = tag.Conditions[i];
                    _condType[i].SelectedIndex = c.IsEmpty ? 0 : Math.Max(0, TagInfo.TypeKeys.Keys.ToList().IndexOf(c.Type) + 1);
                    ConditionTypeChanged(i);
                    _condOp[i].SelectedIndex = c.IsEmpty ? -1 : TagInfo.OpText.Keys.ToList().IndexOf(c.Op);
                    _condValue[i].Text = c.IsEmpty ? "" : TagInfo.IsNumeric(c.Type) ? c.Value.ToString() : TagInfo.ValueName(c.Type, c.Value);
                }
                _key.Text = tag.Key;
                _texts.Rows.Clear();
                foreach (char language in _tags.Languages)
                    _texts.Rows.Add(language.ToString(), tag.Texts.GetValueOrDefault(language) ?? "");
                _tagEditor.Enabled = true;
            }
            finally
            {
                _loadingTag = false;
            }
        }

        private void ConditionTypeChanged(int slot)
        {
            var type = CondTypeAt(slot);
            var box = _condValue[slot];
            string text = box.Text;
            box.Items.Clear();
            if (type is TagCondType t && TagInfo.ValueNames.TryGetValue(t, out var names))
                box.Items.AddRange(names.Values.Cast<object>().ToArray());
            box.Text = text;
            _condOp[slot].Enabled = box.Enabled = type != null;
            if (type != null && _condOp[slot].SelectedIndex < 0)
                _condOp[slot].SelectedIndex = TagInfo.OpText.Keys.ToList().IndexOf(TagCompareOp.Equal);
        }

        private TagCondType? CondTypeAt(int slot) =>
            _condType[slot].SelectedIndex <= 0 ? null : TagInfo.TypeKeys.Keys.ElementAt(_condType[slot].SelectedIndex - 1);

        /// <summary>The editor's contents on top of <paramref name="start"/>. Unknown value names are left at 0.</summary>
        private TagInfo ReadEditor(TagInfo start)
        {
            var tag = start.Clone();
            tag.Group = (TagGroup)Math.Max(0, _group.SelectedIndex);
            tag.Flag1 = (byte)_flag1.Value;
            int slot = 0;
            for (int i = 0; i < TagInfo.ConditionCount; i++)
                tag.Conditions[i] = TagCondition.Empty;
            for (int i = 0; i < TagInfo.ConditionCount; i++)
            {
                if (CondTypeAt(i) is not TagCondType type)
                    continue;
                var op = _condOp[i].SelectedIndex >= 0 ? TagInfo.OpText.Keys.ElementAt(_condOp[i].SelectedIndex) : TagCompareOp.Equal;
                string text = _condValue[i].Text.Trim();
                int value = text == "-1" ? ushort.MaxValue : int.TryParse(text, out int number) ? number
                    : TagInfo.ValueNames.TryGetValue(type, out var names) ? names.FirstOrDefault(n => n.Value.Equals(text, StringComparison.OrdinalIgnoreCase)).Key : 0;
                tag.Conditions[slot++] = new TagCondition(type, op, (ushort)Math.Clamp(value, 0, ushort.MaxValue));
            }
            tag.UpdateHasNumber();
            tag.Key = _key.Text;
            foreach (DataGridViewRow row in _texts.Rows)
                if (row.Cells[0].Value is string language && language.Length > 0)
                    tag.Texts[language[0]] = row.Cells[1].Value as string ?? "";
            return tag;
        }

        private void ApplyTagIfEdited()
        {
            if (_loadingTag || _shownTag is not int id || _tags == null || id >= _tags.Tags.Count || !_tagEditor.Enabled)
                return;
            var edited = ReadEditor(_tags.Tags[id]);
            if (!edited.SameAs(_tags.Tags[id]))
            {
                _tags.Tags[id] = edited;
                Changed($"Tag {id} changed: {edited.Describe(Language)}");
                _tagList.Invalidate();
            }
        }

        private void ApplyTag()
        {
            ApplyTagIfEdited();
            if (_shownTag is int id)
            {
                _tagPreview.Text = _tags!.Tags[id].Describe(Language);
                RefillCards();
            }
        }

        private void NewTag(int? copyOf)
        {
            if (_tags == null)
                return;
            ApplyTagIfEdited();
            var tag = copyOf is int source ? _tags.Tags[source].Clone() : new TagInfo { Group = TagGroup.Name, Key = "NewTag" };
            if (copyOf == null)
                foreach (char language in _tags.Languages)
                    tag.Texts[language] = "Related to: ";
            _tags.Tags.Add(tag);
            int id = _tags.Tags.Count - 1;
            _tagFind.Text = "";
            RefillTags(id);
            Changed($"Added tag {id}. Set it up on the right, then give it to related cards on the Cards tab.");
        }

        private void CardsUsingTag()
        {
            if (_shownTag is not int id)
                return;
            var users = _cards.Where(c => c.Value.Any(r => r.TagId == id)).Select(c => c.Key).ToList();
            if (users.Count == 0)
            {
                _status.Text = $"No related-card link uses tag {id}.";
                return;
            }
            _status.Text = $"Tag {id} is used in the lists of {users.Count} cards: " + string.Join(", ", users.Take(12).Select(CardName)) + (users.Count > 12 ? "..." : "");
            _pages.SelectedIndex = 0;
            _cardFilter.SelectedIndex = 0;
            _cardFind.Text = "";
            RefillCards(users[0]);
        }
    }
}
