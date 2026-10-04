using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Types;
using WolfX.Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The Voice Over page: Yu-Gi-Oh-Ex\voices.json for Yu-Gi-Oh-Music (docs/MusicPlugin.md "Voice lines"). Each character gets lines
    /// to say when something happens in a duel ("Kaiba says one of these when my opponent attacks"). Every row reads as a sentence,
    /// shown under the grid. Lines are at most once per duel by default and never repeat a file until all of the row's files have played.
    /// Added files are copied into voices\&lt;character id&gt;\.
    /// </summary>
    public sealed class VoiceEditor : UserControl, IGameEditor
    {
        public const string FileName = "voices.json";

        public sealed class Choice<T>(T key, string text)
        {
            public T Key { get; } = key;
            public string Text { get; } = text;
        }

        /// <summary>Which of a row's extra columns an event uses (the others are greyed out).</summary>
        [Flags]
        private enum Uses { None = 0, Amount = 1, Cards = 2, How = 4, Reason = 8 }

        /// <summary>The events (voices.json "when" + "who"), as the sentence the editor shows. Key = when, or when:opponent.</summary>
        private static readonly (string Key, string Text, Uses Uses)[] WhenChoices =
        [
            ("duelStart", "the duel starts", Uses.None),
            ("turnStart", "my turn starts", Uses.None),
            ("turnStart:opponent", "my opponent's turn starts", Uses.None),
            ("battlePhase", "I enter the Battle Phase", Uses.None),
            ("battlePhase:opponent", "my opponent enters the Battle Phase", Uses.None),
            ("summon", "I summon a monster", Uses.Cards | Uses.How),
            ("summon:opponent", "my opponent summons a monster", Uses.Cards | Uses.How),
            ("attack", "I attack", Uses.Cards),
            ("attack:opponent", "my opponent attacks", Uses.Cards),
            ("inHand", "I hold cards in my hand", Uses.Amount | Uses.Cards),
            ("inHand:opponent", "my opponent holds cards in their hand", Uses.Amount | Uses.Cards),
            ("damage", "I take damage (at least Amount)", Uses.Amount),
            ("damage:opponent", "my opponent takes damage (at least Amount)", Uses.Amount),
            ("lowLP", "my LP fall to Amount or below", Uses.Amount),
            ("lowLP:opponent", "my opponent's LP fall to Amount or below", Uses.Amount),
            ("win", "I win", Uses.Reason),
            ("lose", "I lose", Uses.Reason),
            ("draw", "the duel is a draw", Uses.None),
        ];

        /// <summary>voices.json "how" on a summon line.</summary>
        private static readonly (string Key, string Text)[] SummonKinds =
        [
            ("any", "any way"),
            ("normal", "Normal / Tribute Summon"),
            ("flip", "Flip Summon"),
            ("special", "Special Summon"),
        ];

        /// <summary>voices.json "reason" on a win / lose line: the game's DuelWinReason (index = number), docs/StatsAndMatchResults.md.</summary>
        private static readonly (string Key, string Text)[] WinReasons =
        [
            ("any", "any reason"), ("lp", "LP reduced to 0"), ("deckOut", "deck-out"), ("timeLimit", "time / turn limit"), ("surrender", "surrender"),
            ("rules", "broke the rules"), ("exodia", "Exodia the Forbidden One"), ("destinyBoard", "Destiny Board"), ("yataLock", "Yata-Garasu lock"),
            ("lastTurn", "Last Turn"), ("finalCountdown", "Final Countdown"), ("effect", "a match-winning effect (11)"), ("vennominaga", "Vennominaga"),
            ("exodius", "Exodius the Ultimate Forbidden Lord"), ("effect14", "a match-winning effect (14)"), ("leo", "Number 88: Gimmick Puppet of Leo"),
            ("disasterLeo", "Number C88: Gimmick Puppet Disaster Leo"), ("jackpot7", "Jackpot 7"), ("effect18", "a match-winning effect (18)"),
            ("relaySoul", "Relay Soul"), ("ghostrickAngel", "Ghostrick Angel of Mischief"), ("phantasmSpiral", "Phantasm Spiral Assault"),
            ("faWinners", "F.A. Winners"), ("flyingElephant", "Flying Elephant"), ("exodiaDefender", "Exodia, the Legendary Defender"),
        ];

        private sealed class LineRow
        {
            public string When { get; set; } = "duelStart";
            public int Amount { get; set; }
            public List<int> CardList { get; set; } = [];
            public string Cards => CardList.Count == 0 ? "" : string.Join(", ", CardList.Select(CardCatalog.NameOf));
            public string How { get; set; } = "any";
            public string Reason { get; set; } = "any";
            public int Against { get; set; } = -1;
            public List<string> FileList { get; set; } = [];
            public string Files => FileList.Count == 0 ? "(double-click to pick files)" : string.Join(", ", FileList.Select(Path.GetFileName));
            public int Chance { get; set; } = 100;
            public bool Once { get; set; } = true;
            public double Volume { get; set; } = 1;
        }

        private static readonly JsonSerializerOptions WriteOptions = new() { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true };
        private static readonly JsonDocumentOptions ReadOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _save, _addLine, _addFiles, _remove, _play, _stop, _openFolder;
        private readonly TextBox _folder = new() { Width = 120, Text = "voices" };
        private readonly NumericUpDown _volume = new() { Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.05m, Value = 1, Width = 60 };
        private readonly NumericUpDown _duck = new() { Minimum = 0, Maximum = 100, Increment = 5, Value = 50, Width = 55 };
        private readonly NumericUpDown _gap = new() { Minimum = 0, Maximum = 120, DecimalPlaces = 1, Increment = 0.5m, Value = 3, Width = 55 };
        private readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "Find a character..." };
        private readonly CheckBox _onlyVoiced = new() { Dock = DockStyle.Bottom, Text = "Only characters with lines", AutoSize = true };
        private readonly ListBox _characters = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly Label _heading = new() { Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9.5f, FontStyle.Bold) };
        private readonly Label _sentence = new() { Dock = DockStyle.Bottom, Height = 40, Padding = new Padding(4), Font = new Font("Segoe UI", 9.5f, FontStyle.Italic) };
        private readonly DataGridView _grid = new()
        {
            Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, EditMode = DataGridViewEditMode.EditOnEnter,
        };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private readonly Dictionary<int, BindingList<LineRow>> _rows = [];
        private readonly Dictionary<int, string> _characterNames = [];
        private readonly BindingList<Choice<int>> _againstItems = [];
        private GameFolderFiles? _files;
        private int _shown = -1;
        private string _savedJson = "";
        private bool _previewOpen;

        private sealed record CharacterItem(int Id, string Text)
        {
            public override string ToString() => Text;
        }

        public IReadOnlyCollection<string> Files => [CharacterTable.GamePath('E')];

        public string SavesTo => $"Additional: Yu-Gi-Oh-Ex\\{FileName} and the files in its voices folder (needs Yu-Gi-Oh-Music).";

        public bool Dirty => _files != null && Build().ToJsonString() != _savedJson;

        public VoiceEditor()
        {
            _save = Button("Save", "Save voices.json (Ctrl+S)", () => Save());
            _addLine = Button("Add line...", "Pick one or more mp3 / wav / flac files: a new line for this character (one is picked at random each time)", AddLine);
            _addFiles = Button("Add files to line...", "Add more files to the selected line (more files = less repetition)", AddFilesToLine);
            _remove = Button("Remove", "Remove the selected lines", RemoveRows);
            _play = Button("Play", "Listen to one of the selected line's files", Preview);
            _stop = Button("Stop", "Stop listening", StopPreview);
            _openFolder = Button("Open voices folder", "Open the folder voices.json's files are read from", OpenFolder);
            _tools.Items.AddRange([_save, new ToolStripSeparator(), _addLine, _addFiles, _remove, new ToolStripSeparator(), _play, _stop, new ToolStripSeparator(), _openFolder]);

            var defaults = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4, 2, 4, 2) };
            defaults.Controls.AddRange([Caption("Voices folder (in Yu-Gi-Oh-Ex):"), _folder, Caption("Volume:"), _volume,
                Caption("Music while talking (%):"), _duck, Caption("Quiet time after a line (s):"), _gap]);

            _filter.TextChanged += (_, _) => FillCharacters();
            _onlyVoiced.CheckedChanged += (_, _) => FillCharacters();
            _characters.SelectedIndexChanged += (_, _) =>
            {
                if (_characters.SelectedItem is CharacterItem item)
                    ShowCharacter(item.Id);
            };
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_characters);
            left.Controls.Add(_onlyVoiced);
            left.Controls.Add(_filter);
            left.Controls.Add(new Label { Dock = DockStyle.Top, Height = 34, Text = "Who talks. Custom characters (Characters page) are listed too." });

            BuildColumns();
            _grid.DataError += (_, e) =>
            {
                e.ThrowException = false;
                _status.Text = $"Row {e.RowIndex + 1}: {e.Exception?.Message}";
            };
            _grid.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex < 0)
                    return;
                string column = _grid.Columns[e.ColumnIndex].DataPropertyName;
                if (column == nameof(LineRow.Files))
                    PickFiles(e.RowIndex);
                else if (column == nameof(LineRow.Cards) && _grid.Rows[e.RowIndex].DataBoundItem is LineRow row && UsesOf(row.When).HasFlag(Uses.Cards))
                    PickCards(e.RowIndex);
            };
            _grid.CellBeginEdit += (_, e) =>
            {
                if (_grid.Rows[e.RowIndex].DataBoundItem is LineRow row && ColumnUse(_grid.Columns[e.ColumnIndex].DataPropertyName) is Uses use && !UsesOf(row.When).HasFlag(use))
                    e.Cancel = true;
            };
            _grid.CellFormatting += (_, e) =>
            {
                if (e.RowIndex < 0 || _grid.Rows[e.RowIndex].DataBoundItem is not LineRow row || ColumnUse(_grid.Columns[e.ColumnIndex].DataPropertyName) is not Uses use)
                    return;
                if (!UsesOf(row.When).HasFlag(use))
                {
                    // Not used by this event: blank (combo cells keep their value but are greyed).
                    if (e.CellStyle != null)
                    {
                        e.CellStyle.BackColor = SystemColors.Control;
                        e.CellStyle.ForeColor = SystemColors.GrayText;
                    }
                    if (use is Uses.Amount or Uses.Cards)
                    {
                        e.Value = "";
                        e.FormattingApplied = true;
                    }
                }
                else if (use == Uses.Cards && row.CardList.Count == 0)
                {
                    e.Value = row.When.StartsWith("inHand") ? "(double-click to pick the cards)" : "(any card; double-click to pick)";
                    e.FormattingApplied = true;
                }
            };
            _grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (_grid.IsCurrentCellDirty && _grid.CurrentCell is DataGridViewComboBoxCell or DataGridViewCheckBoxCell)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += (_, _) => { ShowSentence(); _grid.Invalidate(); };
            _grid.SelectionChanged += (_, _) => ShowSentence();

            var right = new Panel { Dock = DockStyle.Fill };
            right.Controls.Add(_grid);
            right.Controls.Add(_sentence);
            right.Controls.Add(new Label
            {
                Dock = DockStyle.Bottom, Height = 36,
                Text = "Several files on one line: one is picked each time, and none repeats until all have played. \"Once\" = at most once per duel. " +
                       "Only one line plays at a time; duel start and the result always get through.",
            });
            right.Controls.Add(_heading);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);
            Load += (_, _) => split.SplitterDistance = Math.Clamp(split.Width * 25 / 100, 220, 340);

            var status = new StatusStrip();
            status.Items.Add(_status);
            Controls.Add(split);
            Controls.Add(defaults);
            Controls.Add(_tools);
            Controls.Add(status);

            _status.Text = "Open the game data (File > Open).";
            SetEditable(false);
        }

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(8, 7, 2, 3) };

        private static Uses UsesOf(string when) => WhenChoices.FirstOrDefault(e => e.Key == when).Uses;

        private static bool UsesAmount(string when) => UsesOf(when).HasFlag(Uses.Amount);

        /// <summary>The event setting a grid column belongs to, or null for the columns every line has.</summary>
        private static Uses? ColumnUse(string property) => property switch
        {
            nameof(LineRow.Amount) => Uses.Amount,
            nameof(LineRow.Cards) => Uses.Cards,
            nameof(LineRow.How) => Uses.How,
            nameof(LineRow.Reason) => Uses.Reason,
            _ => null,
        };

        private void SetEditable(bool editable)
        {
            foreach (ToolStripItem item in _tools.Items)
                item.Enabled = editable;
            _grid.Enabled = _characters.Enabled = editable;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                StopPreview();
            base.Dispose(disposing);
        }

        // ---- grid ----

        private void BuildColumns()
        {
            var when = new DataGridViewComboBoxColumn
            {
                HeaderText = "When", DataPropertyName = nameof(LineRow.When), DataSource = WhenChoices.Select(e => new Choice<string>(e.Key, e.Text)).ToList(),
                ValueMember = nameof(Choice<string>.Key), DisplayMember = nameof(Choice<string>.Text), Width = 270, FlatStyle = FlatStyle.Flat,
            };
            var against = new DataGridViewComboBoxColumn
            {
                HeaderText = "Against", DataPropertyName = nameof(LineRow.Against), DataSource = _againstItems,
                ValueMember = nameof(Choice<int>.Key), DisplayMember = nameof(Choice<int>.Text), Width = 150, FlatStyle = FlatStyle.Flat,
            };
            var how = new DataGridViewComboBoxColumn
            {
                HeaderText = "Summoned", DataPropertyName = nameof(LineRow.How), DataSource = SummonKinds.Select(k => new Choice<string>(k.Key, k.Text)).ToList(),
                ValueMember = nameof(Choice<string>.Key), DisplayMember = nameof(Choice<string>.Text), Width = 150, FlatStyle = FlatStyle.Flat,
                ToolTipText = "Summon lines: only when the monster is summoned this way",
            };
            var reason = new DataGridViewComboBoxColumn
            {
                HeaderText = "Win by", DataPropertyName = nameof(LineRow.Reason), DataSource = WinReasons.Select(r => new Choice<string>(r.Key, r.Text)).ToList(),
                ValueMember = nameof(Choice<string>.Key), DisplayMember = nameof(Choice<string>.Text), Width = 170, FlatStyle = FlatStyle.Flat,
                ToolTipText = "Win / lose lines: only when the duel is won this way (Exodia, deck-out, surrender...). Said before the finish animation.",
            };
            _grid.Columns.AddRange(
            [
                when,
                new DataGridViewTextBoxColumn { HeaderText = "Amount", DataPropertyName = nameof(LineRow.Amount), Width = 60, ToolTipText = "Damage: at least this much. LP: this many or fewer. In hand: how many of the cards (0 = all of them)." },
                new DataGridViewTextBoxColumn { HeaderText = "Cards (double-click)", DataPropertyName = nameof(LineRow.Cards), ReadOnly = true, Width = 180, ToolTipText = "Summon / attack: only these cards. In hand: the cards to hold." },
                how,
                reason,
                against,
                new DataGridViewTextBoxColumn { HeaderText = "Files (double-click)", DataPropertyName = nameof(LineRow.Files), ReadOnly = true, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 160 },
                new DataGridViewTextBoxColumn { HeaderText = "Chance %", DataPropertyName = nameof(LineRow.Chance), Width = 65 },
                new DataGridViewCheckBoxColumn { HeaderText = "Once", DataPropertyName = nameof(LineRow.Once), Width = 45, ToolTipText = "At most once per duel" },
                new DataGridViewTextBoxColumn { HeaderText = "Volume", DataPropertyName = nameof(LineRow.Volume), Width = 55 },
            ]);
        }

        private string NameOf(int id) => _characterNames.GetValueOrDefault(id, $"character {id}");

        private BindingList<LineRow> RowsOf(int character)
        {
            if (!_rows.TryGetValue(character, out var rows))
            {
                _rows[character] = rows = [];
                rows.ListChanged += (_, _) => RefreshCharacterText(character);
            }
            return rows;
        }

        private void ShowCharacter(int id)
        {
            _grid.EndEdit();
            _shown = id;
            _grid.DataSource = RowsOf(id);
            _heading.Text = $"{NameOf(id)} (character {id}) says...";
            ShowSentence();
        }

        private LineRow? Selected => _grid.CurrentRow?.DataBoundItem as LineRow;

        /// <summary>The selected row as a sentence, so the row's meaning is never a guess.</summary>
        private void ShowSentence()
        {
            if (Selected is not { } row || _shown < 0)
            {
                _sentence.Text = _shown < 0 ? "" : "Add a line: pick its files, then choose when it's said.";
                return;
            }
            string when = WhenChoices.FirstOrDefault(e => e.Key == row.When).Text ?? row.When;
            when = when.Replace(" (at least Amount)", $" (at least {row.Amount})").Replace("Amount", row.Amount.ToString());
            string cards = CardsText(row.CardList, "or");
            bool opponent = row.When.EndsWith(":opponent");
            switch (row.When.Split(':')[0])
            {
                case "summon":
                    when = (opponent ? "my opponent summons " : "I summon ") + (row.CardList.Count == 0 ? "any monster" : cards);
                    if (row.How != "any")
                        when += $" ({SummonKinds.FirstOrDefault(k => k.Key == row.How).Text})";
                    break;
                case "attack" when row.CardList.Count > 0:
                    when = (opponent ? "my opponent attacks with " : "I attack with ") + cards;
                    break;
                case "inHand":
                {
                    int needed = row.Amount > 0 ? Math.Min(row.Amount, row.CardList.Count) : row.CardList.Count;
                    string what = row.CardList.Count == 0 ? "(no cards picked yet)"
                        : row.CardList.Count == 1 ? cards
                        : needed == row.CardList.Count ? $"all of {CardsText(row.CardList, "and")}" : $"{needed} of {CardsText(row.CardList, "and")}";
                    when = (opponent ? $"my opponent holds {what} in their hand" : $"I hold {what} in my hand") + " (said as it happens, not again while it stays true)";
                    break;
                }
                case "win" or "lose" when row.Reason != "any":
                    when += $" by {WinReasons.FirstOrDefault(r => r.Key == row.Reason).Text} (said before the finish animation)";
                    break;
                case "win" or "lose" or "draw":
                    when += " (said before the finish animation)";
                    break;
            }
            string files = row.FileList.Count switch { 0 => "nothing yet (no files)", 1 => Path.GetFileName(row.FileList[0]), _ => $"one of {row.FileList.Count} files" };
            var parts = new List<string> { $"{NameOf(_shown)} says {files} when {when}" };
            if (row.Against >= 0)
                parts.Add($"only against {NameOf(row.Against)}");
            if (row.Chance < 100)
                parts.Add($"{row.Chance}% of the time");
            parts.Add(row.Once ? "at most once per duel" : "every time");
            _sentence.Text = string.Join(", ", parts) + ".";
        }

        private static string CardsText(List<int> cards, string joiner) => cards.Count switch
        {
            0 => "",
            1 => CardCatalog.NameOf(cards[0]),
            _ => string.Join(", ", cards.Take(cards.Count - 1).Select(CardCatalog.NameOf)) + $" {joiner} " + CardCatalog.NameOf(cards[^1]),
        };

        private void FillCharacters()
        {
            int keep = _shown;
            string filter = _filter.Text.Trim();
            _characters.BeginUpdate();
            _characters.Items.Clear();
            foreach (var (id, name) in _characterNames.OrderBy(p => p.Key))
            {
                int count = _rows.TryGetValue(id, out var rows) ? rows.Count : 0;
                if (_onlyVoiced.Checked && count == 0)
                    continue;
                if (filter.Length > 0 && !name.Contains(filter, StringComparison.OrdinalIgnoreCase) && id.ToString() != filter)
                    continue;
                var item = new CharacterItem(id, CharacterText(id, name, count));
                _characters.Items.Add(item);
                if (id == keep)
                    _characters.SelectedItem = item;
            }
            _characters.EndUpdate();
        }

        private static string CharacterText(int id, string name, int lines) => lines == 0 ? $"{id}: {name}" : $"{id}: {name}  ({lines} line{(lines == 1 ? "" : "s")})";

        private void RefreshCharacterText(int character)
        {
            for (int i = 0; i < _characters.Items.Count; i++)
                if (_characters.Items[i] is CharacterItem item && item.Id == character)
                {
                    string text = CharacterText(character, NameOf(character), _rows.TryGetValue(character, out var rows) ? rows.Count : 0);
                    if (text != item.Text)
                        _characters.Items[i] = new CharacterItem(character, text);
                }
        }

        // ---- files ----

        private string VoicesFolder => _files == null ? "" : Path.Combine(_files.ExFolder, _folder.Text.Trim().Length > 0 ? _folder.Text.Trim() : "voices");

        private string FullPath(string file) => Path.IsPathRooted(file) ? file : Path.Combine(VoicesFolder, file);

        /// <summary>Copies a file into voices\&lt;character&gt;\ (unless it's in the voices folder already) and gives the name voices.json uses.</summary>
        private string Bring(string path, int character)
        {
            string root = VoicesFolder;
            string full = Path.GetFullPath(path);
            if (full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(root, full).Replace('\\', '/');
            string folder = Path.Combine(root, character.ToString());
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, Path.GetFileName(full));
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(full).Length)
                File.Copy(full, target, overwrite: true);
            return $"{character}/{Path.GetFileName(full)}";
        }

        private static OpenFileDialog AudioDialog() => new() { Filter = "Audio (*.mp3;*.wav;*.flac)|*.mp3;*.wav;*.flac|All files (*.*)|*.*", Multiselect = true };

        private List<string>? AskFiles()
        {
            if (_shown < 0)
            {
                _status.Text = "Pick a character on the left first.";
                return null;
            }
            using var dialog = AudioDialog();
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return null;
            try
            {
                return dialog.FileNames.Select(f => Bring(f, _shown)).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, ex.Message, "Voice Over", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private void AddLine()
        {
            _grid.EndEdit();
            if (AskFiles() is not { } files)
                return;
            var rows = RowsOf(_shown);
            rows.Add(new LineRow { When = Selected?.When ?? "duelStart", FileList = files });
            _grid.CurrentCell = _grid.Rows[rows.Count - 1].Cells[0];
            _status.Text = $"Added a line with {files.Count} file(s); they're in {Path.Combine(VoicesFolder, _shown.ToString())}. Now choose when it's said.";
        }

        private void AddFilesToLine()
        {
            _grid.EndEdit();
            if (Selected is not { } row)
            {
                _status.Text = "Select a line first (or use Add line).";
                return;
            }
            if (AskFiles() is not { } files)
                return;
            row.FileList.AddRange(files.Where(f => !row.FileList.Contains(f, StringComparer.OrdinalIgnoreCase)));
            RowsOf(_shown).ResetItem(RowsOf(_shown).IndexOf(row));
            ShowSentence();
        }

        private void PickFiles(int index)
        {
            if (_grid.Rows[index].DataBoundItem is not LineRow row || AskFiles() is not { } files)
                return;
            row.FileList = files;
            RowsOf(_shown).ResetItem(index);
            ShowSentence();
        }

        /// <summary>Summon / attack / in-hand lines: pick the cards (the picked list replaces the old one; pick none for any card).</summary>
        private void PickCards(int index)
        {
            if (_grid.Rows[index].DataBoundItem is not LineRow row)
                return;
            _grid.EndEdit();
            using var picker = new CardPickerDialog(CardCatalog.Get(this), row.When.StartsWith("inHand") ? "Cards to hold in the hand" : "Which cards (none = any)", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;
            row.CardList = picker.Result.Select(pick => pick.Card.Id).Distinct().ToList();
            RowsOf(_shown).ResetItem(index);
            ShowSentence();
        }

        private void RemoveRows()
        {
            _grid.EndEdit();
            if (_shown < 0)
                return;
            var rows = RowsOf(_shown);
            foreach (var line in _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem).OfType<LineRow>().ToList())
                rows.Remove(line);
        }

        private void OpenFolder()
        {
            if (_files == null)
                return;
            string folder = _shown >= 0 && Directory.Exists(Path.Combine(VoicesFolder, _shown.ToString())) ? Path.Combine(VoicesFolder, _shown.ToString()) : VoicesFolder;
            Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }

        // ---- preview (MCI: mp3 and wav; flac only with a codec installed) ----

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
        private static extern int mciSendString(string command, StringBuilder? result, int length, IntPtr callback);

        private void Preview()
        {
            if (Selected is not { } row || row.FileList.Count == 0)
            {
                _status.Text = "Pick a line with files to listen to it.";
                return;
            }
            string path = FullPath(row.FileList[Random.Shared.Next(row.FileList.Count)]);
            if (!File.Exists(path))
            {
                _status.Text = $"{path} doesn't exist.";
                return;
            }
            StopPreview();
            int error = mciSendString($"open \"{path}\" type mpegvideo alias wolfxvoice", null, 0, IntPtr.Zero);
            if (error == 0)
                error = mciSendString("play wolfxvoice", null, 0, IntPtr.Zero);
            _previewOpen = error == 0;
            _status.Text = error == 0 ? $"Playing {Path.GetFileName(path)}." : $"Windows can't play {Path.GetFileName(path)} here (MCI error {error}); the game still can.";
        }

        private void StopPreview()
        {
            if (!_previewOpen)
                return;
            mciSendString("close wolfxvoice", null, 0, IntPtr.Zero);
            _previewOpen = false;
        }

        // ---- opening ----

        public void Open(GameFolderFiles files)
        {
            _files = files;
            ReadNames(files);
            _rows.Clear();
            _folder.Text = "voices";
            _volume.Value = 1;
            _duck.Value = 50;
            _gap.Value = 3;
            string path = files.ExPath(FileName);
            string note = "no voices.json yet";
            if (File.Exists(path))
            {
                try
                {
                    Read(JsonNode.Parse(File.ReadAllText(path), documentOptions: ReadOptions) as JsonObject ?? throw new InvalidDataException("not a JSON object"));
                    note = $"{_rows.Values.Sum(r => r.Count)} line(s) for {_rows.Count(r => r.Value.Count > 0)} character(s)";
                }
                catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
                {
                    note = $"{FileName} couldn't be read: {ex.Message}";
                }
            }
            foreach (int id in _rows.Keys.Where(id => !_characterNames.ContainsKey(id)).ToList())
                _characterNames[id] = $"character {id}";
            _againstItems.Clear();
            _againstItems.Add(new Choice<int>(-1, "(anyone)"));
            foreach (var (id, name) in _characterNames.OrderBy(p => p.Key))
                _againstItems.Add(new Choice<int>(id, $"{id}: {name}"));

            _shown = -1;
            _grid.DataSource = null;
            _heading.Text = "Pick a character";
            FillCharacters();
            if (_characters.Items.Count > 0)
                _characters.SelectedIndex = Math.Max(0, _characters.Items.Cast<CharacterItem>().ToList().FindIndex(i => _rows.ContainsKey(i.Id) && _rows[i.Id].Count > 0));
            _savedJson = Build().ToJsonString();
            SetEditable(true);
            _status.Text = $"{path}: {note}. The voices folder is {VoicesFolder}.";
        }

        private void ReadNames(GameFolderFiles files)
        {
            _characterNames.Clear();
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

        private static double? Num(JsonNode? node) => node is JsonValue value && value.TryGetValue<double>(out double d) ? d : null;

        /// <summary>"reason" is a name ("exodia") or the DuelWinReason number.</summary>
        private static string ReasonKey(JsonNode? node)
        {
            if (Num(node) is double number)
                return number >= 0 && number < WinReasons.Length ? WinReasons[(int)number].Key : "any";
            string text = node?.ToString() ?? "";
            return WinReasons.FirstOrDefault(r => string.Equals(r.Key, text, StringComparison.OrdinalIgnoreCase)).Key ?? "any";
        }

        private void Read(JsonObject root)
        {
            if (root["folder"] is JsonValue folder && folder.TryGetValue<string>(out var f))
                _folder.Text = f;
            _volume.Value = (decimal)Math.Clamp(Num(root["volume"]) ?? 1, 0, 2);
            _duck.Value = (decimal)Math.Clamp(Math.Round((Num(root["duck"]) ?? 0.5) * 100), 0, 100);
            _gap.Value = (decimal)Math.Clamp(Num(root["gap"]) ?? 3, 0, 120);
            if (root["characters"] is not JsonObject characters)
                return;
            foreach (var (id, list) in characters)
            {
                if (!int.TryParse(id, out int character) || list is not JsonArray lines)
                    continue;
                var rows = RowsOf(character);
                foreach (var node in lines.OfType<JsonObject>())
                {
                    string when = node["when"]?.ToString() ?? "";
                    if (node["who"]?.ToString() == "opponent")
                        when += ":opponent";
                    if (!WhenChoices.Any(e => e.Key == when))
                        continue;
                    var row = new LineRow
                    {
                        When = when,
                        Amount = (int)(Num(node["count"]) ?? Num(node["amount"]) ?? 0),
                        How = SummonKinds.FirstOrDefault(k => string.Equals(k.Key, node["how"]?.ToString(), StringComparison.OrdinalIgnoreCase)).Key ?? "any",
                        Reason = ReasonKey(node["reason"]),
                        Against = (int)(Num(node["against"]) ?? -1),
                        Chance = (int)Math.Clamp(Num(node["chance"]) ?? 100, 0, 100),
                        Once = node["once"] is not JsonValue once || !once.TryGetValue<bool>(out bool o) || o,
                        Volume = Num(node["volume"]) ?? 1,
                    };
                    if (node["files"] is JsonArray fileList)
                        row.FileList.AddRange(fileList.OfType<JsonValue>().Select(v => v.ToString()));
                    if (node["file"] is JsonValue one)
                        row.FileList.Add(one.ToString());
                    foreach (string key in new[] { "cards", "card" })
                    {
                        if (Num(node[key]) is double single)
                            row.CardList.Add((int)single);
                        else if (node[key] is JsonArray cardList)
                            row.CardList.AddRange(cardList.Select(Num).OfType<double>().Select(d => (int)d));
                    }
                    if (row.Against >= 0 && !_characterNames.ContainsKey(row.Against))
                        _characterNames[row.Against] = $"character {row.Against}";
                    rows.Add(row);
                }
            }
        }

        // ---- saving ----

        private JsonObject Build()
        {
            var root = new JsonObject { ["folder"] = _folder.Text.Trim().Length > 0 ? _folder.Text.Trim() : "voices" };
            if (_volume.Value != 1)
                root["volume"] = (double)_volume.Value;
            root["duck"] = (double)_duck.Value / 100;
            root["gap"] = (double)_gap.Value;
            var characters = new JsonObject();
            foreach (var (id, rows) in _rows.Where(p => p.Value.Count > 0).OrderBy(p => p.Key))
            {
                var lines = new JsonArray();
                foreach (var row in rows)
                {
                    string[] when = row.When.Split(':');
                    var line = new JsonObject { ["when"] = when[0] };
                    if (when.Length > 1)
                        line["who"] = when[1];
                    var uses = UsesOf(row.When);
                    if (when[0] == "inHand")
                    {
                        if (row.Amount > 0)
                            line["count"] = row.Amount;
                    }
                    else if (uses.HasFlag(Uses.Amount))
                        line["amount"] = row.Amount;
                    if (uses.HasFlag(Uses.Cards) && row.CardList.Count > 0)
                        line["cards"] = new JsonArray([.. row.CardList.Select(c => JsonValue.Create(c))]);
                    if (uses.HasFlag(Uses.How) && row.How != "any")
                        line["how"] = row.How;
                    if (uses.HasFlag(Uses.Reason) && row.Reason != "any")
                        line["reason"] = row.Reason;
                    if (row.Against >= 0)
                        line["against"] = row.Against;
                    line["files"] = new JsonArray([.. row.FileList.Select(f => JsonValue.Create(f))]);
                    if (row.Chance != 100)
                        line["chance"] = row.Chance;
                    line["once"] = row.Once;
                    if (row.Volume != 1)
                        line["volume"] = row.Volume;
                    lines.Add(line);
                }
                characters[id.ToString()] = lines;
            }
            root["characters"] = characters;
            return root;
        }

        public bool Save()
        {
            if (_files == null)
                return false;
            _grid.EndEdit();
            var problems = new List<string>();
            foreach (var (id, rows) in _rows)
                foreach (var row in rows)
                {
                    if (row.FileList.Count == 0)
                        problems.Add($"{NameOf(id)}: a line has no files.");
                    foreach (var file in row.FileList.Where(file => !File.Exists(FullPath(file))))
                        problems.Add($"{NameOf(id)}: {FullPath(file)} doesn't exist (that file is skipped in game).");
                    if (row.When.StartsWith("inHand") && row.CardList.Count == 0)
                        problems.Add($"{NameOf(id)}: an \"in hand\" line has no cards (double-click its Cards cell).");
                    if (row.Chance is < 1 or > 100)
                        problems.Add($"{NameOf(id)}: chance must be 1-100.");
                }
            if (problems.Any(p => !p.Contains("doesn't exist")))
            {
                MessageBox.Show(this, string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (problems.Count > 0 && MessageBox.Show(this, string.Join("\n", problems) + "\n\nSave anyway?", "Voice Over", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;

            var root = Build();
            Directory.CreateDirectory(_files.ExFolder);
            File.WriteAllText(_files.ExPath(FileName), root.ToJsonString(WriteOptions) + "\n", new UTF8Encoding(false));
            _savedJson = root.ToJsonString();
            _status.Text = $"Saved {_files.ExPath(FileName)}. Yu-Gi-Oh-Music reads it when the game starts.";
            return true;
        }
    }
}
