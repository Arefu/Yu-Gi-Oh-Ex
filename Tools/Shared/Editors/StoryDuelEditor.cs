using System.IO;
using DeckData;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The Story editor: the campaign's story duels and their scenes together.
    /// <list type="bullet">
    /// <item>Duel tab: main/dueldata_&lt;lang&gt;.bin (File Type Libraries/DuelData, docs/StoryDuels.md): series and order in the series' list, player
    /// and opponent (character, deck, portrait costume), arena, reward pack, content pack, key, and title, description and loss tip per language.</item>
    /// <item>Intro / Win / Lose tabs: the duel's scenes &lt;key&gt;_INTRO, _OUTRO, _OUTRO_LOSE from main/scriptdata_&lt;lang&gt;.bin (File Type
    /// Libraries/ScriptData, docs/StoryScenes.md) in the scene editor: the steps, and the stage with the game's backgrounds and character
    /// sprites (drag them between LEFT / CENTER / RIGHT, or off the stage).</item>
    /// </list>
    /// Character names come from chardata and deck titles from deckdata_E.bin when they're there.
    /// Opens the game's duels and scenes with Yu-Gi-Oh-Ex\storyduels.json and storyscripts.json on top. Changes to the game's duels and
    /// scenes are saved into dueldata_*.bin and scriptdata_*.bin; new duels (in free ids: the game has 226 slots) and new scenes go to the
    /// two JSON files, which Yu-Gi-Oh-Campaign applies.
    /// </summary>
    public sealed class StoryDuelEditor : UserControl, IGameEditor
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _save, _newDuel, _duplicate, _deleteDuel;
        private readonly ToolStripComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 45 };
        private readonly ToolStripComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly ToolStripTextBox _find = new() { Width = 170, ToolTipText = "Id, key, part of a title or a character name" };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false, MultiSelect = false,
        };
        private readonly Label _heading = new() { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        private readonly TextBox _key = new() { Width = 260 };
        private readonly ComboBox _series = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        private readonly NumericUpDown _order = new() { Minimum = 1, Maximum = StoryDuelTable.MaxOrder, Width = 60 };
        private readonly Label _orderInfo = new() { AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
        private readonly IdCombo[] _character = [new(220), new(220)];
        private readonly Label[] _characterInfo = [new() { AutoSize = true, Margin = new Padding(6, 7, 3, 3) }, new() { AutoSize = true, Margin = new Padding(6, 7, 3, 3) }];
        private readonly IdCombo[] _deck = [new(260), new(260)];
        private readonly Label[] _deckInfo = [new() { AutoSize = true, Margin = new Padding(6, 7, 3, 3) }, new() { AutoSize = true, Margin = new Padding(6, 7, 3, 3) }];
        private readonly TextBox[] _costume = [new() { Width = 100 }, new() { Width = 100 }];
        private readonly IdCombo _arena = new(220);
        private readonly IdCombo _rewardPack = new(260);
        private readonly IdCombo _sku = new(220);
        private readonly CheckBox _exclude = new() { Text = "Not needed to unlock the opponent's own deck (the game's two crossover duels)", AutoSize = true };
        private readonly DataGridView _texts = new()
        {
            Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
        };
        private readonly Panel _editor = new() { Dock = DockStyle.Fill, Enabled = false, AutoScroll = true };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly TabControl _pages = new() { Dock = DockStyle.Fill };
        private readonly SceneEditor[] _scenes = [new() { Dock = DockStyle.Fill }, new() { Dock = DockStyle.Fill }, new() { Dock = DockStyle.Fill }];
        private static readonly string[] SceneTitles = ["Intro scene", "Win scene", "Lose scene"];
        private readonly ToolStripButton _removeScene;

        private StoryDuelTable? _table, _baseline;
        private StoryScriptTable? _scripts, _scriptBaseline;
        private CharacterTable? _characters;
        private DeckDataFile? _decks;
        private List<StoryDuel> _rows = [];
        private GameFolderFiles? _gameFiles;
        private bool _changed, _binding;
        private int? _shown;

        private string JsonPath => _gameFiles!.ExPath(StoryDuelJson.FileName);
        private string ScriptJsonPath => _gameFiles!.ExPath(StoryScriptJson.FileName);

        public IReadOnlyCollection<string> Files =>
            [.. StoryDuelTable.AllLanguages.SelectMany(l => new[] { StoryDuelTable.GamePath(l), StoryScriptTable.GamePath(l), CharacterTable.GamePath(l) }), @"main\deckdata_E.bin"];

        public string SavesTo => $"Standard: main\\dueldata_<lang>.bin and scriptdata_<lang>.bin (the game's duels and scenes). Additional (new duels and scenes): Yu-Gi-Oh-Ex\\{StoryDuelJson.FileName} and {StoryScriptJson.FileName} (needs Yu-Gi-Oh-Campaign).";

        private char Language => _language.SelectedItem is string text && text.Length > 0 ? text[0] : 'E';

        public bool Dirty => _changed;

        public StoryDuelEditor()
        {

            ListPick.FirstWhenShown(_list);   // open on the first entry, not an empty "Pick a ..." panel
            _save = Button("Save", "The game's duels and scenes into dueldata / scriptdata, new ones into storyduels.json / storyscripts.json (Ctrl+S)", () => Save());
            _removeScene = Button("Remove scene", "Remove the scene on the open tab (the duel then plays without it)", RemoveScene);
            _newDuel = Button("New duel", "Add a duel in the next free id, at the end of the shown series (the game has 226 slots)", () => NewDuel(null));
            _duplicate = Button("Duplicate", "Add a copy of the selected duel in the next free id, at the end of its series", () => NewDuel(Selected()));
            _deleteDuel = Button("Delete duel...", "Delete a duel you added (New duel / Duplicate), with its scenes. The game's own duels can't be deleted", DeleteDuel);
            _tools.Items.AddRange([_save, new ToolStripSeparator(), new ToolStripLabel("Language:")]);
            foreach (char language in StoryDuelTable.AllLanguages)
                _language.Items.Add(language.ToString());
            _language.SelectedIndex = 0;
            _language.SelectedIndexChanged += (_, _) =>
            {
                foreach (var scene in _scenes)
                    scene.Language = Language;
                _list.Invalidate();
                FillNames();
                ShowSelected();
            };
            _tools.Items.Add(_language);
            _tools.Items.Add(new ToolStripLabel("Series:"));
            _filter.Items.Add("All series");
            _filter.Items.AddRange(StoryDuelTable.SeriesNames);
            _filter.Items.Add("Changed / new");
            _filter.SelectedIndex = 0;
            _filter.SelectedIndexChanged += (_, _) => Refill();
            _tools.Items.Add(_filter);
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _find.TextChanged += (_, _) => Refill();
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.AddRange([_newDuel, _duplicate, _deleteDuel, _removeScene]);

            _list.Columns.Add("Id", 40);
            _list.Columns.Add("Series", 90);
            _list.Columns.Add("#", 35);
            _list.Columns.Add("Title", 190);
            _list.Columns.Add("Player vs opponent", 220);
            _list.Columns.Add("", 60);
            _list.RetrieveVirtualItem += (_, e) =>
            {
                if (e.ItemIndex >= _rows.Count) { e.Item = new ListViewItem(new string[_list.Columns.Count]); return; }   // stale index while the list shrinks
                var d = _rows[e.ItemIndex];
                e.Item = new ListViewItem([d.Id.ToString(), StoryDuelTable.SeriesName(d.Series), d.Order.ToString(), d.Title(Language),
                    $"{CharacterName(d.Characters[0])} vs {CharacterName(d.Characters[1])}", Note(d)]);
            };
            _list.SelectedIndexChanged += (_, _) => ShowSelected();

            _series.Items.AddRange(StoryDuelTable.SeriesNames.Select((name, i) => $"{i}: {name}").ToArray());
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
            void Row(string label, params Control[] controls)
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
                var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                flow.Controls.AddRange(controls);
                grid.Controls.Add(flow);
            }
            Label Small(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 7, 3, 3) };
            Row("Key (dialog <key>_INTRO / _OUTRO / _OUTRO_LOSE):", _key);
            Row("Series:", _series);
            Row($"Order in the series (1-{StoryDuelTable.MaxOrder}):", _order, _orderInfo);
            Row("Player character:", _character[0], _characterInfo[0]);
            Row("Player deck:", _deck[0], _deckInfo[0], Small("costume:"), _costume[0]);
            Row("Opponent character:", _character[1], _characterInfo[1]);
            Row("Opponent deck:", _deck[1], _deckInfo[1], Small("costume:"), _costume[1]);
            Row("Arena:", _arena);
            Row("Reward pack (first win):", _rewardPack);
            Row("Content pack (sku):", _sku);
            Row("", _exclude);
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Language", ReadOnly = true, FillWeight = 9 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Title", FillWeight = 21 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Description", FillWeight = 40, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Tip (after a loss)", FillWeight = 30, DefaultCellStyle = { WrapMode = DataGridViewTriState.True } });
            foreach (char language in StoryDuelTable.AllLanguages)
                _texts.Rows.Add(language.ToString(), "", "", "");

            var textsPanel = new Panel { Dock = DockStyle.Fill };
            textsPanel.Controls.Add(_texts);
            _editor.Controls.Add(textsPanel);
            _editor.Controls.Add(grid);

            foreach (Control control in new Control[] { _key, _series, _order, _character[0], _character[1], _deck[0], _deck[1], _costume[0], _costume[1], _arena, _rewardPack, _sku, _exclude })
            {
                switch (control)
                {
                    case TextBox box: box.TextChanged += (_, _) => Edited(); break;
                    case IdCombo id: id.ValueChanged += (_, _) => Edited(); break;
                    case ComboBox combo: combo.SelectedIndexChanged += (_, _) => Edited(); break;
                    case NumericUpDown number: number.ValueChanged += (_, _) => Edited(); break;
                    case CheckBox check: check.CheckedChanged += (_, _) => Edited(); break;
                }
            }
            _texts.CellValueChanged += (_, _) => Edited();

            var duelPage = new TabPage("Duel") { UseVisualStyleBackColor = true };
            duelPage.Controls.Add(_editor);
            _pages.TabPages.Add(duelPage);
            for (int i = 0; i < _scenes.Length; i++)
            {
                var page = new TabPage(SceneTitles[i]) { UseVisualStyleBackColor = true };
                page.Controls.Add(_scenes[i]);
                _pages.TabPages.Add(page);
                _scenes[i].NameOf = KeyName;
                _scenes[i].CreateScene = CreateScene;
                _scenes[i].Changed += () =>
                {
                    _changed = true;
                    _list.Invalidate();
                };
            }
            _pages.SelectedIndexChanged += (_, _) => _removeScene.Enabled = _scripts != null && _pages.SelectedIndex > 0;
            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_pages);
            right.Controls.Add(_heading);
            var split = new SplitContainer { Dock = DockStyle.Fill };
            split.Panel1.Controls.Add(_list);
            split.Panel2.Controls.Add(right);

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) => split.SplitterDistance = Math.Max(280, split.Width * 27 / 100);
            SetEditable(false);
            _status.Text = "Open the game data (File > Open).";
        }

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            if (!Open(language => files.Read(StoryDuelTable.GamePath(language)), language => files.Read(StoryScriptTable.GamePath(language)),
                    language => files.Read(CharacterTable.GamePath(language)), files.Read(@"main\deckdata_E.bin"), files.Describe(StoryDuelTable.GamePath('E')), files))
            {
                _table = _baseline = null;
                SetEditable(false);
            }
        }

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

        private void SetEditable(bool editable)
        {
            _save.Enabled = _newDuel.Enabled = _duplicate.Enabled = editable;
            _deleteDuel.Enabled = editable && IsAdded(_shown);
            _removeScene.Enabled = editable && _scripts != null && _pages.SelectedIndex > 0;
        }

        // ---- text helpers ----

        private string CharacterName(int id) => _characters?.Find(id) is Character c ? c.Name(Language) : $"#{id}";

        /// <summary>The name for a character key (the dialogue box's name plate), found the way Character_FindByKey does.</summary>
        private string KeyName(string key) =>
            _characters?.Characters.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase)) is Character c ? c.Name(Language) : key;

        private static string SceneName(StoryDuel d, int scene) => d.Key + StoryScriptTable.SceneSuffixes[scene];

        private bool ScenesChanged(StoryDuel d)
        {
            if (_scripts == null || _scriptBaseline == null)
                return false;
            for (int i = 0; i < StoryScriptTable.SceneSuffixes.Length; i++)
            {
                var (now, game) = (_scripts.Find(SceneName(d, i)), _scriptBaseline.Find(SceneName(d, i)));
                if ((now == null) != (game == null) || (now != null && game != null && !now.SameAs(game)))
                    return true;
            }
            return false;
        }

        private IGameFiles? _names;

        /// <summary>The dropdowns' names: characters (in the shown language), decks, packs, arenas, content packs.</summary>
        private void FillNames()
        {
            foreach (var combo in _character)
                combo.SetItems(GameNames.Characters(_characters, Language));
            foreach (var combo in _deck)
                combo.SetItems(GameNames.Decks(_decks));
            _arena.SetItems(GameNames.Arenas(_names));
            _rewardPack.SetItems(GameNames.Packs(_names));
            _sku.SetItems(GameNames.ContentPacks(_names));
        }

        private string DeckTitle(int id) => _decks == null || _decks.Find((uint)Math.Max(0, id)) != null ? "" : "not a deck in deckdata";

        private string Note(StoryDuel d)
        {
            if (_baseline == null)
                return "";
            var game = _baseline.Find(d.Id);
            return game == null ? "new" : game.SameAs(d) && !ScenesChanged(d) ? "" : "changed";
        }

        // ---- opening / saving ----

        private bool Open(Func<char, byte[]?> read, Func<char, byte[]?> readScripts, Func<char, byte[]?> readCharacters, byte[]? deckData, string from, IGameFiles pictures)
        {
            var files = new Dictionary<char, byte[]>();
            foreach (char language in StoryDuelTable.AllLanguages)
                if (read(language) is byte[] bytes)
                    files[language] = bytes;
            if (files.Count == 0)
            {
                _status.Text = $"{from}: no dueldata_*.bin.";
                return false;
            }
            try
            {
                _table = StoryDuelTable.Parse(files);
                var characterFiles = new Dictionary<char, byte[]>();
                var scriptFiles = new Dictionary<char, byte[]>();
                foreach (char language in StoryDuelTable.AllLanguages)
                {
                    if (readCharacters(language) is byte[] c) characterFiles[language] = c;
                    if (readScripts(language) is byte[] s) scriptFiles[language] = s;
                }
                _characters = characterFiles.Count > 0 ? CharacterTable.Parse(characterFiles) : null;
                _decks = deckData != null ? DeckDataFile.Parse(deckData) : null;
                if (_decks != null)
                    DeckJson.ApplyTo(_decks, DeckJson.Load(_gameFiles!.ExPath(DeckJson.FileName)), 'E');   // new and changed decks from decks.json
                _scripts = scriptFiles.Count > 0 ? StoryScriptTable.Parse(scriptFiles) : null;
                _scriptBaseline = _scripts?.Clone();
                _baseline = _table.Clone();
                int fromJson = StoryDuelJson.Apply(StoryDuelJson.Load(JsonPath), _table);
                int scenesFromJson = _scripts != null ? StoryScriptJson.Apply(StoryScriptJson.Load(ScriptJsonPath), _scripts) : 0;
                foreach (var scene in _scenes)
                    scene.UseFiles(pictures, _characters?.Characters.Select(c => c.Key) ?? [], _characters?.Find(139)?.Name(Language) ?? "");
                _names = pictures;
                FillNames();
                _changed = false;
                SetEditable(true);
                Refill();
                _status.Text = $"{from}: {_baseline.Duels.Count} story duels (of {StoryDuelTable.SlotCount - 1} ids), languages {string.Join("", _table.Languages)}" +
                    (_scripts != null ? $", {_scripts.Scripts.Count} scenes" : "; no scriptdata, so no scenes") +
                    (fromJson + scenesFromJson > 0 ? $"; {fromJson} duels from {StoryDuelJson.FileName}, {scenesFromJson} scenes from {StoryScriptJson.FileName}" : "") +
                    (_characters == null ? "; no chardata, so no character names" : "") + (_decks == null ? "; no deckdata_E.bin, so no deck titles" : "") + ".";
                return true;
            }
            catch (InvalidDataException ex)
            {
                _status.Text = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Standard content: the game's duels and scenes (with your changes; removed game scenes are left out) go back into every language's
        /// dueldata_*.bin and scriptdata_*.bin. Additional content: new duels and new scenes go to storyduels.json and storyscripts.json;
        /// each is deleted when there is nothing in it.
        /// </summary>
        public bool Save()
        {
            if (_table == null || _baseline == null || _gameFiles == null)
                return false;
            var problems = _table.Problems();
            if (_scripts != null)
                problems.AddRange(_scripts.Scripts.Where(s => s.Lines.Count == 0).Select(s => $"Scene {s.Name} has no steps (remove it instead)."));
            problems.AddRange(_table.Duels.Where(d => d.Key.Any(ch => ch > 127) || d.Costumes.Any(c => c.Any(ch => ch > 127))).Select(d => $"Duel {d.Id}: the key and costumes must be ASCII."));
            if (problems.Count > 0)
            {
                MessageBox.Show(this, string.Join("\n", problems.Take(30)), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                var write = new Dictionary<string, byte[]>();
                var standard = _table.Clone();
                standard.Duels.RemoveAll(d => _baseline.Find(d.Id) == null);
                foreach (char language in standard.Languages)
                {
                    byte[] bytes = standard.ToBytes(language);
                    if (!bytes.AsSpan().SequenceEqual(_baseline.ToBytes(language)))
                        write[StoryDuelTable.GamePath(language)] = bytes;
                }
                StoryScriptTable? standardScripts = null;
                if (_scripts != null && _scriptBaseline != null)
                {
                    standardScripts = _scripts.Clone();
                    standardScripts.Scripts.RemoveAll(s => _scriptBaseline.Find(s.Name) == null);
                    foreach (char language in standardScripts.Languages)
                    {
                        byte[] bytes = standardScripts.ToBytes(language);
                        if (!bytes.AsSpan().SequenceEqual(_scriptBaseline.ToBytes(language)))
                            write[StoryScriptTable.GamePath(language)] = bytes;
                    }
                }
                if (write.Count > 0)
                    _gameFiles.Write(write);

                var root = StoryDuelJson.Diff(standard, _table);
                int count = ((System.Text.Json.Nodes.JsonArray)root["duels"]!).Count;
                if (count > 0)
                    StoryDuelJson.Save(JsonPath, root);
                else if (File.Exists(JsonPath))
                    File.Delete(JsonPath);
                int scenes = 0;
                if (standardScripts != null)
                {
                    var sceneRoot = StoryScriptJson.Diff(standardScripts, _scripts!);
                    scenes = ((System.Text.Json.Nodes.JsonArray)sceneRoot["scripts"]!).Count;
                    if (scenes > 0)
                        StoryScriptJson.Save(ScriptJsonPath, sceneRoot);
                    else if (File.Exists(ScriptJsonPath))
                        File.Delete(ScriptJsonPath);
                }
                _baseline = standard;
                _scriptBaseline = standardScripts ?? _scriptBaseline;
                _changed = false;
                Refill();
                _status.Text = (write.Count > 0 ? $"Saved {string.Join(", ", write.Keys.Select(Path.GetFileName))} into {_gameFiles.Describe(StoryDuelTable.GamePath('E'))}" : "The game's story is unchanged") +
                    (count + scenes > 0 ? $"; {count} new duels and {scenes} new scenes to {_gameFiles.ExFolder} (Yu-Gi-Oh-Campaign applies them)." : ".");
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        // ---- list ----

        private StoryDuel? Selected() =>
            _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;

        private void Refill(int? select = null)
        {
            if (_table == null)
                return;
            select ??= Selected()?.Id;
            string find = _find.Text.Trim();
            int filter = _filter.SelectedIndex;   // 0 all, 1-6 a series, 7 changed/new
            _rows = _table.Duels.OrderBy(d => d.Series).ThenBy(d => d.Order).ThenBy(d => d.Id).Where(d =>
                (filter < 1 || filter > 6 || d.Series == filter - 1) &&
                (filter != 7 || Note(d).Length > 0) &&
                (find.Length == 0 || d.Id.ToString() == find || d.Key.Contains(find, StringComparison.OrdinalIgnoreCase) ||
                 d.Titles.Values.Any(t => t.Contains(find, StringComparison.OrdinalIgnoreCase)) ||
                 d.Characters.Any(c => CharacterName(c).Contains(find, StringComparison.OrdinalIgnoreCase)))).ToList();
            _list.VirtualListSize = _rows.Count;
            _list.SelectedIndices.Clear();
            int index = select is int wanted ? _rows.FindIndex(d => d.Id == wanted) : -1;
            if (index >= 0)
            {
                _list.SelectedIndices.Add(index);
                _list.EnsureVisible(index);
            }
            _list.Invalidate();
            ShowSelected();
        }

        private void ShowSelected()
        {
            var d = Selected();
            _shown = d?.Id;
            _editor.Enabled = d != null;
            _deleteDuel.Enabled = _save.Enabled && IsAdded(_shown);
            if (d == null)
            {
                _heading.Text = _table == null ? "" : "Pick a story duel.";
                ShowScenes(null);
                return;
            }
            _binding = true;
            try
            {
                bool isNew = _baseline?.Find(d.Id) == null;
                _heading.Text = $"{d.Title(Language)} ({d.Id}){(isNew ? " - new" : "")}";
                _key.Text = d.Key;
                _series.SelectedIndex = Math.Clamp(d.Series, 0, StoryDuelTable.SeriesNames.Length - 1);
                _order.Value = Math.Clamp(d.Order, 1, StoryDuelTable.MaxOrder);
                for (int side = 0; side < 2; side++)
                {
                    _character[side].Value = d.Characters[side];
                    _deck[side].Value = d.Decks[side];
                    _costume[side].Text = d.Costumes[side];
                }
                _arena.Value = d.Arena;
                _rewardPack.Value = d.RewardPack;
                _sku.Value = d.Sku;
                _exclude.Checked = d.ExcludeFromDeckUnlock != 0;
                for (int i = 0; i < StoryDuelTable.AllLanguages.Length; i++)
                {
                    char language = StoryDuelTable.AllLanguages[i];
                    _texts.Rows[i].Cells[1].Value = d.Titles.GetValueOrDefault(language, "");
                    _texts.Rows[i].Cells[2].Value = d.Descriptions.GetValueOrDefault(language, "");
                    _texts.Rows[i].Cells[3].Value = d.Tips.GetValueOrDefault(language, "");
                }
                ShowInfo(d);
                ShowScenes(d);
            }
            finally
            {
                _binding = false;
            }
        }

        private void ShowScenes(StoryDuel? d)
        {
            for (int i = 0; i < _scenes.Length; i++)
            {
                _scenes[i].Enabled = d != null && _scripts != null;
                _scenes[i].Show(d != null ? SceneName(d, i) : "", d != null ? _scripts?.Find(SceneName(d, i)) : null);
            }
        }

        /// <summary>A new scene for the selected duel: the background of its other scenes, the two duelists in their places.</summary>
        private StoryScript? CreateScene(string name)
        {
            if (_scripts == null || _shown is not int id || _table?.Find(id) is not StoryDuel d)
                return null;
            string KeyOf(int character) => _characters?.Find(character)?.Key ?? "";
            string background = Enumerable.Range(0, StoryScriptTable.SceneSuffixes.Length).Select(i => _scripts.Find(SceneName(d, i)))
                .SelectMany(s => s?.Lines ?? []).FirstOrDefault(l => l.IsCommand && l.Position.Equals("BG", StringComparison.OrdinalIgnoreCase))?.Expression
                ?? DefaultBackground(d.Series);
            var script = new StoryScript { Name = name };
            script.Lines.Add(new ScriptLine { Speaker = ScriptLine.Command, Position = "BG", Expression = background });
            script.Lines.Add(new ScriptLine { Speaker = KeyOf(d.Characters[0]), Position = "LEFT", Expression = d.Costumes[0].Length > 0 ? $"{d.Costumes[0]}_neutral" : "neutral" });
            script.Lines.Add(new ScriptLine { Speaker = KeyOf(d.Characters[1]), Position = "RIGHT", Expression = d.Costumes[1].Length > 0 ? $"{d.Costumes[1]}_neutral" : "neutral" });
            script.Lines[2].Texts['E'] = "...";
            _scripts.Scripts.Add(script);
            _changed = true;
            _list.Invalidate();
            return script;
        }

        /// <summary>A background of the series (the files are named classic_, gx_, 5ds_, zexal_, arcv_...; VRAINS has none), else the first.</summary>
        private string DefaultBackground(int series)
        {
            string[] prefixes = ["classic_", "gx_", "5ds_", "zexal_", "arcv_", "vrains_"];
            var all = _scenes[0].Backgrounds;
            string prefix = series >= 0 && series < prefixes.Length ? prefixes[series] : "";
            return all.FirstOrDefault(b => prefix.Length > 0 && b.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ?? all.FirstOrDefault() ?? "";
        }

        private void RemoveScene()
        {
            int scene = _pages.SelectedIndex - 1;
            if (_scripts == null || scene < 0 || _shown is not int id || _table?.Find(id) is not StoryDuel d || _scripts.Find(SceneName(d, scene)) is not StoryScript script)
                return;
            if (MessageBox.Show(this, $"Remove the scene {script.Name}? The duel then plays without it.", "Remove scene", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            _scripts.Scripts.Remove(script);
            _changed = true;
            ShowScenes(d);
            _list.Invalidate();
        }

        /// <summary>A duel added here (New duel / Duplicate, or one from storyduels.json): one the game's own file doesn't have.</summary>
        private bool IsAdded(int? id) => id is int value && _table?.Find(value) != null && _baseline?.Find(value) == null;

        /// <summary>Deletes an added duel and its scenes (a scene another duel also uses, by the same key, stays).</summary>
        private void DeleteDuel()
        {
            if (_table == null || _shown is not int id || _table.Find(id) is not StoryDuel d)
                return;
            if (!IsAdded(id))
            {
                _status.Text = $"Duel {id} is one of the game's own: only duels you added can be deleted (change or blank this one instead).";
                return;
            }
            if (MessageBox.Show(this, $"Delete duel {id} \"{d.Titles.GetValueOrDefault('E', d.Key)}\" and its scenes?", "Delete duel",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            int row = _rows.IndexOf(d);
            _table.Duels.Remove(d);
            if (_scripts != null && !_table.Duels.Any(o => o.Key.Equals(d.Key, StringComparison.OrdinalIgnoreCase)))
                for (int i = 0; i < StoryScriptTable.SceneSuffixes.Length; i++)
                    if (_scripts.Find(SceneName(d, i)) is StoryScript scene)
                        _scripts.Scripts.Remove(scene);
            _changed = true;
            var next = _rows.Where(o => o != d).ElementAtOrDefault(Math.Max(0, row - 1));
            Refill(next?.Id);
            _status.Text = $"Duel {id} deleted (saved when you press Save).";
        }

        private void ShowInfo(StoryDuel d)
        {
            for (int side = 0; side < 2; side++)
            {
                _characterInfo[side].Text = _characters == null || _characters.Find(d.Characters[side]) != null ? "" : "not a character";
                _deckInfo[side].Text = DeckTitle(d.Decks[side]);
            }
            var clash = _table?.Duels.FirstOrDefault(o => o != d && o.Series == d.Series && o.Order == d.Order);
            _orderInfo.Text = clash != null ? $"duel {clash.Id} has this order too: only one is listed"
                : d.Order == 1 ? "the series' tutorial duel (no reverse duel)" : "";
        }

        private void Edited()
        {
            if (_binding || _table == null || _shown is not int id || _table.Find(id) is not StoryDuel d)
                return;
            string newKey = _key.Text.Trim();
            if (_scripts != null && newKey.Length > 0 && !newKey.Equals(d.Key, StringComparison.OrdinalIgnoreCase))
            {
                // the scenes are found by the key, so they follow it (unless another duel's scenes already have the new name)
                for (int i = 0; i < StoryScriptTable.SceneSuffixes.Length; i++)
                    if (_scripts.Find(SceneName(d, i)) is StoryScript scene && _scripts.Find(newKey + StoryScriptTable.SceneSuffixes[i]) == null)
                        scene.Name = newKey + StoryScriptTable.SceneSuffixes[i];
            }
            d.Key = newKey;
            d.Series = _series.SelectedIndex;
            d.Order = (int)_order.Value;
            for (int side = 0; side < 2; side++)
            {
                d.Characters[side] = _character[side].Value;
                d.Decks[side] = _deck[side].Value;
                d.Costumes[side] = _costume[side].Text.Trim();
            }
            d.Arena = _arena.Value;
            d.RewardPack = _rewardPack.Value;
            d.Sku = _sku.Value;
            d.ExcludeFromDeckUnlock = _exclude.Checked ? 1 : 0;
            for (int i = 0; i < StoryDuelTable.AllLanguages.Length; i++)
            {
                char language = StoryDuelTable.AllLanguages[i];
                void Take(int column, Dictionary<char, string> into)
                {
                    if (_texts.Rows[i].Cells[column].Value is string text && (text.Length > 0 || into.ContainsKey(language)))
                        into[language] = text;
                }
                Take(1, d.Titles);
                Take(2, d.Descriptions);
                Take(3, d.Tips);
            }
            _changed = true;
            ShowInfo(d);
            _list.Invalidate();
        }

        private void NewDuel(StoryDuel? copyOf)
        {
            if (_table == null)
                return;
            if (_table.FreeId() is not int id)
            {
                _status.Text = $"All {StoryDuelTable.SlotCount - 1} story duel ids are used.";
                return;
            }
            int series = copyOf?.Series ?? (_filter.SelectedIndex is >= 1 and <= 6 ? _filter.SelectedIndex - 1 : 0);
            var d = copyOf?.Clone() ?? new StoryDuel { Series = series, Sku = 1, RewardPack = -1 };
            d.Id = id;
            d.Order = _table.NextOrder(series);
            if (copyOf == null)
            {
                d.Key = $"NewDuel{id}";
                d.Titles['E'] = "New Duel";
                d.Descriptions['E'] = "";
                d.Tips['E'] = "";
                if (_table.Duels.LastOrDefault(o => o.Series == series) is StoryDuel last)
                {
                    for (int side = 0; side < 2; side++)
                    {
                        d.Characters[side] = last.Characters[side];
                        d.Decks[side] = last.Decks[side];
                    }
                    d.Arena = last.Arena;
                }
            }
            else
            {
                d.Key = $"{copyOf.Key}_{id}";
                for (int i = 0; i < StoryScriptTable.SceneSuffixes.Length && _scripts != null; i++)
                    if (_scripts.Find(SceneName(copyOf, i)) is StoryScript scene)
                    {
                        var copy = scene.Clone();
                        copy.Name = SceneName(d, i);
                        _scripts.Scripts.Add(copy);
                    }
            }
            _table.Duels.Add(d);
            _changed = true;
            _find.Text = "";
            Refill(id);
            _status.Text = $"Duel {id} added as {StoryDuelTable.SeriesName(series)} #{d.Order}" +
                (d.Order > StoryDuelTable.MaxOrder ? $" - over the {StoryDuelTable.MaxOrder} the save has room for, pick a free order" : "") +
                (copyOf == null ? ". It has no scenes yet: the Intro / Win / Lose tabs make them." : ", with copies of its scenes.");
        }
    }
}
