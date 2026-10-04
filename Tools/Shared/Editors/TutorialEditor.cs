using System.IO;
using StartingCollection;
using Types;
using WolfX.Types;

namespace Wolf.Editors
{
    /// <summary>
    /// Edits the Steam tutorials (duel/tutorial/steam_tutorial_NN_&lt;L&gt;.bin, 26 of them in 6 languages; File Type Libraries/Tutorial)
    /// two ways, as the How to Play editor does:
    /// Visual - the steps in a list, the selected step's fields (op, parameters with names for that op, its text), and a frame by
    /// frame playback of the tutorial (<see cref="TutorialPlayback"/>: play, pause, one frame, one step, scrub) drawn by
    /// <see cref="TutorialPreview"/>;
    /// Script - the whole tutorial as text, one step a line, for bulk edits.
    /// "Open from game" opens the game's tutorials (or your Yu-Gi-Oh-Ex\tutorials\*.json). Save puts a tutorial the game has (its number
    /// exists in any language) back into the game data; a new number is saved as JSON there (TutorialFile.ToJson) for a plugin, since only
    /// a plugin can list it in the menu (Yu-Gi-Oh-Campaign does, with the title / arena / listed fields shown for such a number; docs/Tutorials.md).
    /// Open... / Save as... work on .bin files anywhere on disk.
    /// </summary>
    public sealed class TutorialEditor : UserControl, IGameEditor
    {
        private static readonly char[] ShippedLanguages = ['E', 'F', 'G', 'I', 'J', 'S'];

        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStrip _stepTools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripComboBox _tutorial = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly ToolStripComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 130 };
        private readonly ToolStripButton _openFromGame;
        private readonly ToolStripLabel _where = new() { ForeColor = SystemColors.GrayText };

        // a new number's place in Help > Tutorial (JSON only, applied by Yu-Gi-Oh-Campaign)
        private readonly ToolStrip _menuTools = new() { GripStyle = ToolStripGripStyle.Hidden, Visible = false };
        private readonly ToolStripTextBox _menuTitle = new() { Width = 280, ToolTipText = "The name in Help > Tutorial for this language (empty = \"Tutorial NN\")" };
        private readonly IdCombo _menuArena = new(200);
        private readonly CheckBox _menuListed = new() { Text = "Listed in Help > Tutorial", Checked = true, AutoSize = true, BackColor = Color.Transparent };
        private string _savedMenu = "";
        private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
        private readonly ListView _steps = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, GridLines = true,
        };
        private readonly TutorialPreview _preview = new() { Dock = DockStyle.Fill };
        private readonly TextBox _script = new()
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = new Font("Consolas", 10f),
        };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        // step fields
        private readonly ComboBox _op = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        private readonly NumericUpDown _p1 = new() { Maximum = 255, Width = 90 };
        private readonly NumericUpDown _p2 = new() { Maximum = 65535, Width = 90 };
        private readonly NumericUpDown _p3 = new() { Maximum = 65535, Width = 90 };
        private readonly NumericUpDown _p4 = new() { Maximum = 65535, Width = 90 };
        private readonly NumericUpDown _id = new() { Maximum = 65535, Width = 90 };
        private readonly Label[] _paramLabels = [new(), new(), new(), new()];
        private readonly Button[] _cardButtons = [new(), new(), new(), new()];
        private readonly CheckBox _hasText = new() { Text = "Has text", AutoSize = true };
        private readonly TextBox _text = new()
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10f),
        };
        private readonly Label _help = new() { Dock = DockStyle.Top, Height = 36, ForeColor = SystemColors.GrayText };

        // playback
        private readonly TrackBar _track = new() { Dock = DockStyle.Fill, TickStyle = TickStyle.None, Minimum = 0, Maximum = 0 };
        private readonly Label _time = new() { AutoSize = true, Margin = new Padding(8, 8, 3, 3) };
        private readonly Button _play = new() { Text = "Play", Width = 60 };
        private readonly NumericUpDown _messageFrames = new() { Minimum = 1, Maximum = 6000, Value = 150, Width = 70 };
        private readonly NumericUpDown _actionFrames = new() { Minimum = 1, Maximum = 6000, Value = 90, Width = 70 };
        private readonly CheckBox _follow = new() { Text = "Follow in list", Checked = true, AutoSize = true, Margin = new Padding(8, 6, 3, 3) };
        private readonly System.Windows.Forms.Timer _playTimer = new() { Interval = 16 };
        private readonly System.Windows.Forms.Timer _rebuildTimer = new() { Interval = 250 };

        private TutorialFile _file = new();
        private TutorialPlayback _playback;
        private int _frame, _start;
        private byte[] _savedBytes = [];
        private string? _diskFile;
        private int _number = 1;
        private char _gameLanguage = 'E';
        private bool _fromGame;
        private bool _syncing, _following;
        private GameFolderFiles? _gameFiles;
        private CardDatabase? _cards;

        public TutorialEditor()
        {
            // the larger window gets the playback controls too, since the page's bar stays behind
            PreviewPopOut.Attach(_preview, "Tutorial preview", true,
                new PopOutAction("|<", "Back to the start", () => SeekStart(0)),
                new PopOutAction("<<", "Previous step", () => SeekStart(_start - 1)),
                new PopOutAction("▶ Play / ❚❚ Pause", "Play the tutorial from here", TogglePlay),
                new PopOutAction(">>", "Next step", () => SeekStart(_start + 1)));
            PreviewPopOut.SetShape(_preview, new Size(1920, 1080));   // the game screen it draws
            _playback = new TutorialPlayback(_file);

            _tools.Items.Add(Button("Open...", "Open a steam_tutorial_NN_<L>.bin file", OpenFromDisk));
            _openFromGame = Button("Open from game", "Open the game's tutorial (your Yu-Gi-Oh-Ex JSON if you saved one)", () => OpenFromGame(_number, _gameLanguage));
            _openFromGame.Visible = false;
            _tools.Items.Add(_openFromGame);
            _tools.Items.Add(Button("Save", "Ctrl+S", () => Save(false)));
            _tools.Items.Add(Button("Save as...", "Save to another file", () => Save(true)));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("Tutorial:"));
            _tutorial.SelectedIndexChanged += (_, _) => TutorialChosen();
            _tools.Items.Add(_tutorial);
            _tools.Items.Add(new ToolStripLabel("Language:"));
            _language.SelectedIndexChanged += (_, _) => LanguageChosen();
            _tools.Items.Add(_language);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("New tutorial...", "Start a new tutorial (a blank one or a copy of this one) under a number", NewTutorial));
            _tools.Items.Add(Button("Copy to languages...", "Write this tutorial as the other languages' files too (e.g. a new tutorial, before it's translated)", CopyToLanguages));
            _tools.Items.Add(_where);

            _menuTools.Items.Add(new ToolStripLabel("Help > Tutorial:"));
            _menuTools.Items.Add(new ToolStripControlHost(_menuListed) { Margin = new Padding(4, 2, 8, 0) });
            _menuTools.Items.Add(new ToolStripLabel("Title:"));
            _menuTools.Items.Add(_menuTitle);
            _menuTools.Items.Add(new ToolStripLabel("Arena:"));
            _menuTools.Items.Add(new ToolStripControlHost(_menuArena));
            _menuTools.Items.Add(new ToolStripLabel("(the arena and listing are shared by every language; the English file's win)") { ForeColor = SystemColors.GrayText });
            _menuArena.SetItems(GameNames.Arenas(null, null));
            _menuTitle.TextChanged += (_, _) =>
            {
                if (!_syncing)
                    _file.MenuTitle = _menuTitle.Text.Length > 0 ? _menuTitle.Text : null;
            };
            _menuArena.ValueChanged += (_, _) =>
            {
                if (_syncing)
                    return;
                _file.MenuArena = _menuArena.Value;
                ShowWhere();
            };
            _menuListed.CheckedChanged += (_, _) =>
            {
                if (!_syncing)
                    _file.InMenu = _menuListed.Checked;
            };

            var add = new ToolStripDropDownButton("Add step") { ToolTipText = "Insert a step after the selected one" };
            foreach (TutorialOp op in Enum.GetValues<TutorialOp>())
            {
                var item = new ToolStripMenuItem(TutorialFile.OpName(op)) { Tag = op };
                item.Click += (_, _) => AddStep((TutorialOp)item.Tag!);
                add.DropDownItems.Add(item);
            }
            _stepTools.Items.Add(add);
            _stepTools.Items.Add(Button("Duplicate", "Copy the selected step (and its text) below it", Duplicate));
            _stepTools.Items.Add(Button("Remove", "Remove the selected step", Remove));
            _stepTools.Items.Add(Button("Up", "Move the selected step up", () => MoveStep(-1)));
            _stepTools.Items.Add(Button("Down", "Move the selected step down", () => MoveStep(1)));
            _stepTools.Items.Add(new ToolStripSeparator());
            _stepTools.Items.Add(Button("Renumber ids", "Number the steps 0, 1, 2... (the game never reads the ids)", () =>
            {
                _file.Renumber();
                FillSteps(SelectedIndex());
            }));

            // step list
            _steps.Columns.Add("#", 44);
            _steps.Columns.Add("Id", 50);
            _steps.Columns.Add("Op", 120);
            _steps.Columns.Add("What", 230);
            _steps.Columns.Add("Text", 420);
            _steps.SelectedIndexChanged += (_, _) => ShowSelected();

            // step fields
            foreach (TutorialOp op in Enum.GetValues<TutorialOp>())
                _op.Items.Add(new OpItem(op));
            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, Padding = new Padding(4) };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            AddRow(grid, new Label { Text = "Op", AutoSize = true }, _op, null);
            NumericUpDown[] numbers = [_p1, _p2, _p3, _p4];
            for (int i = 0; i < 4; i++)
            {
                int which = i;
                _paramLabels[i].AutoSize = true;
                _cardButtons[i].Text = "Card...";
                _cardButtons[i].AutoSize = true;
                _cardButtons[i].Click += (_, _) => PickCardInto(numbers[which]);
                AddRow(grid, _paramLabels[i], numbers[i], _cardButtons[i]);
            }
            AddRow(grid, new Label { Text = "Id (not read by the game)", AutoSize = true }, _id, null);
            var textRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false };
            textRow.Controls.Add(_hasText);
            var insertCard = new Button { Text = "Insert card name...", AutoSize = true };
            insertCard.Click += (_, _) => InsertCardName();
            textRow.Controls.Add(insertCard);
            textRow.Controls.Add(new Label
            {
                Text = "$1234 = card name, $A $B $X $Y = buttons, @2 green .. @0 normal, new lines as typed.", AutoSize = true,
                ForeColor = SystemColors.GrayText, Margin = new Padding(8, 7, 3, 3),
            });
            var fields = new Panel { Dock = DockStyle.Fill };
            fields.Controls.Add(_text);
            fields.Controls.Add(textRow);
            fields.Controls.Add(_help);
            fields.Controls.Add(grid);

            _op.SelectedIndexChanged += (_, _) => FieldChanged();
            foreach (var number in numbers.Append(_id))
                number.ValueChanged += (_, _) => FieldChanged();
            _hasText.CheckedChanged += (_, _) => FieldChanged();
            _text.TextChanged += (_, _) => FieldChanged();

            // playback bar
            var bar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false };
            bar.Controls.Add(SmallButton("|<", "Back to the start", () => SeekStart(0)));
            bar.Controls.Add(SmallButton("<<", "Previous step", () => SeekStart(_start - 1)));
            bar.Controls.Add(SmallButton("<", "One frame back", () => SeekFrame(_frame - 1)));
            _play.Click += (_, _) => TogglePlay();
            bar.Controls.Add(_play);
            bar.Controls.Add(SmallButton(">", "One frame on", () => SeekFrame(_frame + 1)));
            bar.Controls.Add(SmallButton(">>", "Next step", () => SeekStart(_start + 1)));
            bar.Controls.Add(_time);
            bar.Controls.Add(new Label { Text = "Message frames:", AutoSize = true, Margin = new Padding(12, 8, 3, 3) });
            bar.Controls.Add(_messageFrames);
            bar.Controls.Add(new Label { Text = "Action frames:", AutoSize = true, Margin = new Padding(8, 8, 3, 3) });
            bar.Controls.Add(_actionFrames);
            bar.Controls.Add(_follow);
            var trackRow = new Panel { Dock = DockStyle.Bottom, Height = 34 };
            trackRow.Controls.Add(_track);
            var previewPanel = new Panel { Dock = DockStyle.Fill };
            previewPanel.Controls.Add(_preview);
            previewPanel.Controls.Add(trackRow);
            previewPanel.Controls.Add(bar);
            new ToolTip().SetToolTip(_messageFrames, "How long playback leaves a message up before 'clicking' on (the game waits for the player)");
            new ToolTip().SetToolTip(_actionFrames, "How long playback waits at a WaitForAction before carrying on as if the player did it");

            _track.Scroll += (_, _) => SeekFrame(_track.Value);
            _messageFrames.ValueChanged += (_, _) => RebuildPlayback();
            _actionFrames.ValueChanged += (_, _) => RebuildPlayback();
            _playTimer.Tick += (_, _) => PlayTick();
            _rebuildTimer.Tick += (_, _) =>
            {
                _rebuildTimer.Stop();
                RebuildPlayback();
            };

            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            right.Panel1.Controls.Add(previewPanel);
            right.Panel2.Controls.Add(fields);
            var visual = new SplitContainer { Dock = DockStyle.Fill };
            visual.Panel1.Controls.Add(_steps);
            visual.Panel2.Controls.Add(right);

            var visualTab = new TabPage("Visual") { UseVisualStyleBackColor = true };
            visualTab.Controls.Add(visual);
            visualTab.Controls.Add(_stepTools);
            var scriptTab = new TabPage("Script") { UseVisualStyleBackColor = true };
            scriptTab.Controls.Add(_script);
            scriptTab.Controls.Add(new Label
            {
                Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText,
                Text = "id: Op p1 p2 p3 p4 | text   (\\n = new line, # = comment; ops by name or 0x..). Switching back to Visual applies it.",
            });
            _tabs.TabPages.Add(visualTab);
            _tabs.TabPages.Add(scriptTab);
            _tabs.Selecting += TabSelecting;

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(_tabs);
            Controls.Add(_menuTools);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) =>
            {
                visual.SplitterDistance = Math.Max(300, visual.Width * 45 / 100);
                right.SplitterDistance = Math.Max(250, right.Height * 60 / 100);
            };

            _preview.CardName = CardName;
            FillTutorials();
            FillLanguages();
            SetEditable(false);
            SetStatus("Open a steam_tutorial_NN_<L>.bin file" + (_gameFiles != null ? ", or the game's" : "") + ".");
            RebuildPlayback();
        }

        /// <summary>The open data: shows "Open from game" and opens the game's tutorial (the one that was open, tutorial 1 in English at first).</summary>
        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            _openFromGame.Visible = files.Available;
            _menuArena.SetItems(GameNames.Arenas(files, null));
            if (files.Available && (_diskFile == null || _file.Steps.Count == 0))
                OpenFromGame(_number, _gameLanguage);
        }

        bool IGameEditor.Save() => Save(false);

        public IReadOnlyCollection<string> Files => _fromGame ? [TutorialFile.GamePath(_number, _gameLanguage)] : [];

        public string SavesTo => "Standard: duel\\tutorial\\steam_tutorial_NN_<lang>.bin for the game's tutorials. Additional (new numbers 27-99): Yu-Gi-Oh-Ex\\tutorials\\*.json (needs Yu-Gi-Oh-Campaign).";

        /// <summary>True when the game has this tutorial number in any language: it is saved into the game data, not as JSON.</summary>
        private bool IsGameTutorial(int number) => _gameFiles != null && ShippedLanguages.Any(l => _gameFiles.Exists(TutorialFile.GamePath(number, l)));

        public bool Dirty => _file.Steps.Count > 0 && (!_file.ToBytes().AsSpan().SequenceEqual(_savedBytes) || MenuState() != _savedMenu);

        /// <summary>The Help > Tutorial fields as one string, to tell whether they changed since the last save.</summary>
        private string MenuState() => $"{_file.MenuTitle}\u0001{_file.MenuArena}\u0001{_file.InMenu}";

        /// <summary>A number only the JSON holds (27-99, not in the game): it gets the Help > Tutorial fields.</summary>
        private bool IsNewNumber => _fromGame && _gameFiles != null && _number >= TutorialFile.FirstNewNumber && _number <= TutorialFile.MaxNumber &&
                                    !IsGameTutorial(_number);

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        private static Button SmallButton(string text, string tip, Action click)
        {
            var button = new Button { Text = text, Width = 38 };
            button.Click += (_, _) => click();
            new ToolTip().SetToolTip(button, tip);
            return button;
        }

        private static void AddRow(TableLayoutPanel grid, Control label, Control field, Control? extra)
        {
            label.Margin = new Padding(3, 6, 3, 3);
            int row = grid.RowCount++;
            grid.Controls.Add(label, 0, row);
            grid.Controls.Add(field, 1, row);
            if (extra != null)
                grid.Controls.Add(extra, 2, row);
        }

        private sealed record OpItem(TutorialOp Op)
        {
            public override string ToString() => $"{TutorialFile.OpName(Op)} (0x{(byte)Op:X2})";
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                Save(false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SetStatus(string text) => _status.Text = text;

        private bool ConfirmDiscard() =>
            !Dirty || MessageBox.Show(this, "The tutorial has changes that are not saved. Discard them?", "Tutorials",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        // ---- card names ----

        private CardDatabase Cards() => _cards ??= CardCatalog.Get(this);

        private string? CardName(int id) => _cards != null && _cards.TryGet(id, out var card) ? card.Name : null;

        private int? PickCard()
        {
            using var dialog = new CardPickerDialog(Cards(), "Pick a card", askCopies: false, maxCopies: 1);
            return dialog.ShowDialog(this) == DialogResult.OK && dialog.Result.Count > 0 ? dialog.Result[0].Card.Id : null;
        }

        private void PickCardInto(NumericUpDown number)
        {
            if (PickCard() is int id)
                number.Value = Math.Clamp(id, 0, (int)number.Maximum);
        }

        private void InsertCardName()
        {
            if (PickCard() is not int id)
                return;
            _hasText.Checked = true;
            _text.SelectedText = "$" + id;
            _text.Focus();
        }

        // ---- opening / saving ----

        private void SetDocument(TutorialFile file, string description)
        {
            StopPlaying();
            _file = file;
            _savedBytes = file.ToBytes();
            _savedMenu = MenuState();
            _cards ??= CardCatalog.Get(this);
            FillTutorials();
            FillLanguages();
            FillSteps(0);
            if (_tabs.SelectedIndex == 1)
                _script.Text = ScriptText();
            SetEditable(true);
            RebuildPlayback();
            SeekStart(0);
            ShowWhere();
            SetStatus($"{description}: {file.Steps.Count} steps, {file.Texts.Count} texts, about {_playback.TotalFrames / 60.0:0} s of playback");
        }

        private void ShowWhere()
        {
            bool isNew = IsNewNumber;
            int arena = isNew ? _file.MenuArena ?? 1 : _number >= 0 && _number < TutorialFile.ArenaIds.Length ? TutorialFile.ArenaIds[_number] : -1;
            _where.Text = $"  {TutorialFile.WhereUsed(_number)}" + (arena >= 0 ? $", arena {arena}" : "");

            _menuTools.Visible = isNew;
            if (!isNew)
                return;
            bool syncing = _syncing;
            _syncing = true;
            _menuListed.Checked = _file.InMenu;
            _menuTitle.Text = _file.MenuTitle ?? "";
            _menuArena.Value = _file.MenuArena ?? 1;
            _syncing = syncing;
        }

        public void OpenFile(string path)
        {
            try
            {
                var file = TutorialFile.Load(path);
                _diskFile = path;
                _fromGame = false;
                if (TutorialFile.NameParts(path) is var (number, language))
                {
                    _number = number;
                    _gameLanguage = language;
                }
                SetDocument(file, path);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"Couldn't read {path}:\n{ex.Message}", "Tutorials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenFromDisk()
        {
            if (!ConfirmDiscard())
                return;
            using var dialog = new OpenFileDialog { Title = "Open a tutorial", Filter = "Tutorials (steam_tutorial_*.bin)|steam_tutorial_*.bin|All files|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                OpenFile(dialog.FileName);
        }

        private string JsonPath(int number, char language) =>
            _gameFiles!.ExPath(Path.Combine(TutorialFile.JsonFolder, TutorialFile.JsonName(number, language)));

        /// <summary>Yu-Gi-Oh-Ex\tutorials\steam_tutorial_NN_L.json if there is one, else the game's own.</summary>
        private void OpenFromGame(int number, char language)
        {
            if (_gameFiles == null)
                return;
            string json = JsonPath(number, language);
            string path = TutorialFile.GamePath(number, language);
            try
            {
                TutorialFile file;
                string description;
                if (File.Exists(json))
                {
                    var problems = new List<string>();
                    file = TutorialFile.FromJson(File.ReadAllText(json), out _, out _, problems);
                    description = json + (problems.Count > 0 ? $" ({problems[0]})" : "");
                }
                else
                {
                    var bytes = _gameFiles.Read(path);
                    if (bytes == null)
                    {
                        SetStatus($"The game has no {path} and there's no {json}.");
                        return;
                    }
                    file = TutorialFile.Parse(bytes);
                    description = path + " (the game's)";
                }
                _diskFile = null;
                _fromGame = true;
                _number = number;
                _gameLanguage = language;
                SetDocument(file, description);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
            {
                MessageBox.Show(this, $"Couldn't read tutorial {number:00} ({language}):\n{ex.Message}", "Tutorials", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>The tutorial files there are (number, language): next to the open file, or in the game.</summary>
        private List<(int Number, char Language)> AvailableFiles()
        {
            string jsonFolder = _gameFiles != null ? _gameFiles.ExPath(TutorialFile.JsonFolder) : "";
            IEnumerable<string> files = _fromGame && _gameFiles != null
                ? _gameFiles.Paths.Where(p => p.StartsWith(TutorialFile.GameFolder + "\\", StringComparison.OrdinalIgnoreCase))
                    .Concat(Directory.Exists(jsonFolder) ? Directory.EnumerateFiles(jsonFolder, "steam_tutorial_*.json") : [])
                : _diskFile != null && Directory.Exists(Path.GetDirectoryName(_diskFile))
                    ? Directory.EnumerateFiles(Path.GetDirectoryName(_diskFile)!, "steam_tutorial_*.bin")
                    : [];
            var found = files.Select(TutorialFile.NameParts).Where(p => p != null).Select(p => p!.Value).ToList();
            if (!found.Contains((_number, _gameLanguage)))
                found.Add((_number, _gameLanguage));
            return found;
        }

        private sealed record TutorialItem(int Number)
        {
            public override string ToString() =>
                $"{Number:00}" + (TutorialFile.MenuList.Contains(Number) ? "" : TutorialFile.CampaignSeriesTutorial.Contains(Number) ? " (campaign)" : " (not listed)");
        }

        private sealed record LanguageItem(char Letter)
        {
            public override string ToString() => $"{HowToPlayFile.LanguageName(Letter)} ({Letter})";
        }

        private void FillTutorials()
        {
            _syncing = true;
            _tutorial.Items.Clear();
            foreach (int number in AvailableFiles().Select(f => f.Number).Distinct().OrderBy(n => n))
                _tutorial.Items.Add(new TutorialItem(number));
            _tutorial.SelectedItem = _tutorial.Items.Cast<TutorialItem>().FirstOrDefault(i => i.Number == _number);
            _syncing = false;
        }

        private void FillLanguages()
        {
            _syncing = true;
            _language.Items.Clear();
            foreach (char letter in AvailableFiles().Where(f => f.Number == _number).Select(f => f.Language).Distinct().OrderBy(l => l))
                _language.Items.Add(new LanguageItem(letter));
            _language.SelectedItem = _language.Items.Cast<LanguageItem>().FirstOrDefault(i => i.Letter == _gameLanguage);
            _syncing = false;
        }

        private void TutorialChosen()
        {
            if (_syncing || _tutorial.SelectedItem is not TutorialItem item || item.Number == _number)
                return;
            if (!ConfirmDiscard())
            {
                FillTutorials();
                return;
            }
            OpenOther(item.Number, _gameLanguage);
        }

        private void LanguageChosen()
        {
            if (_syncing || _language.SelectedItem is not LanguageItem item || item.Letter == _gameLanguage)
                return;
            if (!ConfirmDiscard())
            {
                FillLanguages();
                return;
            }
            OpenOther(_number, item.Letter);
        }

        private void OpenOther(int number, char language)
        {
            if (_fromGame)
            {
                OpenFromGame(number, language);
                return;
            }
            if (_diskFile == null)
                return;
            string folder = Path.GetDirectoryName(_diskFile)!;
            string other = Path.Combine(folder, TutorialFile.FileName(number, language));
            if (!File.Exists(other))
                other = Path.Combine(folder, TutorialFile.FileName(number, 'E'));
            if (File.Exists(other))
                OpenFile(other);
        }

        /// <summary>Whether a tutorial file exists where <see cref="Write"/> would put it (or the game has it).</summary>
        private bool Exists(int number, char language) =>
            _fromGame && _gameFiles != null
                ? _gameFiles.Exists(TutorialFile.GamePath(number, language)) || File.Exists(JsonPath(number, language))
                : _diskFile != null && File.Exists(Path.Combine(Path.GetDirectoryName(_diskFile)!, TutorialFile.FileName(number, language)));

        /// <summary>
        /// Writes the open tutorial as (number, language) and says where: a tutorial the game has goes into the game data (any JSON for it is
        /// deleted), a new number into Yu-Gi-Oh-Ex\tutorials as JSON; a file from disk goes next to it.
        /// </summary>
        private string Write(int number, char language)
        {
            if (_fromGame && _gameFiles != null)
            {
                string json = JsonPath(number, language);
                if (IsGameTutorial(number))
                {
                    string path = TutorialFile.GamePath(number, language);
                    _gameFiles.Write(path, _file.ToBytes());
                    if (File.Exists(json))
                        File.Delete(json);
                    return $"{path} into {_gameFiles.Describe(path)}";
                }
                Directory.CreateDirectory(Path.GetDirectoryName(json)!);
                File.WriteAllText(json, _file.ToJson(number, language));
                return $"{json} (a new tutorial: Yu-Gi-Oh-Campaign adds it to Help > Tutorial)";
            }
            string disk = Path.Combine(_diskFile != null ? Path.GetDirectoryName(_diskFile)! : "", TutorialFile.FileName(number, language));
            File.WriteAllBytes(disk, _file.ToBytes());
            return disk;
        }

        private bool Save(bool saveAs)
        {
            if (!ApplyScriptIfShowing())
                return false;
            if (_file.Steps.Count == 0)
                return false;
            int unused = _file.RemoveUnusedTexts();
            byte[] bytes = _file.ToBytes();
            string? target;
            if (!saveAs && _fromGame && _gameFiles != null)
            {
                try
                {
                    string where = Write(_number, _gameLanguage);
                    _savedBytes = bytes;
                    _savedMenu = MenuState();
                    if (unused > 0)
                        FillSteps(SelectedIndex());
                    FillTutorials();
                    FillLanguages();
                    ShowWhere();
                    SetStatus($"Saved {where}" + (unused > 0 ? $" ({unused} texts no step used were dropped)" : "") + ".");
                    return true;
                }
                catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
                {
                    SetStatus("Not saved: " + ex.Message);
                    return false;
                }
            }
            else
            {
                target = saveAs ? null : _diskFile;
                if (target == null)
                {
                    using var dialog = new SaveFileDialog
                    {
                        Title = "Save the tutorial",
                        Filter = "Tutorials (steam_tutorial_*.bin)|steam_tutorial_*.bin",
                        FileName = TutorialFile.FileName(_number, _gameLanguage),
                        InitialDirectory = _diskFile != null ? Path.GetDirectoryName(_diskFile) : null,
                    };
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                        return false;
                    target = dialog.FileName;
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
            _savedBytes = bytes;
            _savedMenu = MenuState();
            if (!_fromGame || saveAs)
            {
                _diskFile = target;
                _fromGame = false;
                if (TutorialFile.NameParts(target) is var (number, language))
                {
                    _number = number;
                    _gameLanguage = language;
                }
            }
            if (unused > 0)
                FillSteps(SelectedIndex());
            FillTutorials();
            FillLanguages();
            ShowWhere();
            SetStatus($"Saved {target}" + (unused > 0 ? $" ({unused} texts no step used were dropped)" : ""));
            return true;
        }

        /// <summary>Starts a tutorial under a number: blank (board setup, a message, a wait, End) or a copy of the open one.</summary>
        private void NewTutorial()
        {
            if (!ConfirmDiscard())
                return;
            var used = AvailableFiles().Select(f => f.Number).ToHashSet();
            using var form = new Form
            {
                Text = "New tutorial", Size = new Size(560, 300), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false, MaximizeBox = false,
            };
            var number = new NumericUpDown { Minimum = 1, Maximum = 99, Value = Enumerable.Range(27, 73).First(n => !used.Contains(n)), Location = new Point(200, 16), Width = 70 };
            var scenario = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(200, 50), Width = 120 };
            foreach (int s in TutorialFile.Scenarios)
                scenario.Items.Add(s);
            scenario.SelectedIndex = 0;
            var copy = new CheckBox { Text = "Start from a copy of the open tutorial", Location = new Point(16, 88), AutoSize = true, Enabled = _file.Steps.Count > 0 };
            var where = new Label { Location = new Point(16, 120), Size = new Size(520, 80), ForeColor = SystemColors.GrayText };
            void UpdateWhere() => where.Text = $"Tutorial {number.Value:00}: {TutorialFile.WhereUsed((int)number.Value)}." +
                (used.Contains((int)number.Value) ? " There already is a tutorial with this number: saving replaces it." : "") +
                "\nBoard setups are built into the game (Op SetupScenario picks one of these).";
            number.ValueChanged += (_, _) => UpdateWhere();
            UpdateWhere();
            form.Controls.Add(new Label { Text = "Number (steam_tutorial_NN):", Location = new Point(16, 19), AutoSize = true });
            form.Controls.Add(number);
            form.Controls.Add(new Label { Text = "Board setup:", Location = new Point(16, 53), AutoSize = true });
            form.Controls.Add(scenario);
            form.Controls.Add(copy);
            form.Controls.Add(where);
            var ok = new System.Windows.Forms.Button { Text = "Create", DialogResult = DialogResult.OK, Location = new Point(440, 215) };
            form.Controls.Add(ok);
            form.AcceptButton = ok;
            if (form.ShowDialog(this) != DialogResult.OK)
                return;

            var file = copy.Checked ? TutorialFile.Parse(_file.ToBytes()) : TutorialFile.CreateBlank((int)scenario.SelectedItem!);
            _number = (int)number.Value;
            if (_diskFile != null)
                _diskFile = Path.Combine(Path.GetDirectoryName(_diskFile)!, TutorialFile.FileName(_number, _gameLanguage));
            SetDocument(file, $"New tutorial {_number:00}");
            _savedBytes = [];   // nothing saved under this number yet
            SetStatus($"New tutorial {_number:00} ({TutorialFile.WhereUsed(_number)}). Save writes " +
                (_fromGame ? (IsGameTutorial(_number) ? TutorialFile.GamePath(_number, _gameLanguage) + " in the game data" : JsonPath(_number, _gameLanguage)) : TutorialFile.FileName(_number, _gameLanguage)) + ".");
        }

        /// <summary>Writes the open tutorial as other languages' files (same text), e.g. for a new tutorial nobody has translated yet.</summary>
        private void CopyToLanguages()
        {
            if (_file.Steps.Count == 0 || !ApplyScriptIfShowing())
                return;
            if (!_fromGame && _diskFile == null)
            {
                SetStatus("Save the tutorial once first, so it's known which folder the other languages go in.");
                return;
            }
            var others = ShippedLanguages.Where(l => l != _gameLanguage).ToList();
            var existing = others.Where(l => Exists(_number, l)).ToList();
            string question = $"Write tutorial {_number:00} as {string.Join(", ", others.Select(l => HowToPlayFile.LanguageName(l)))}?" +
                (existing.Count > 0 ? $"\n\nThis replaces the {string.Join(", ", existing.Select(l => HowToPlayFile.LanguageName(l)))} version(s)." : "");
            if (MessageBox.Show(this, question, "Tutorials", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            try
            {
                foreach (char language in others)
                    Write(_number, language);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                SetStatus("Not written: " + ex.Message);
                return;
            }
            FillLanguages();
            SetStatus($"Wrote tutorial {_number:00} for {others.Count} other languages.");
        }

        // ---- step list ----

        private int SelectedIndex() => _steps.SelectedIndices.Count > 0 ? _steps.SelectedIndices[0] : -1;

        private ListViewItem ItemFor(int index)
        {
            var step = _file.Steps[index];
            string text = _file.TextOf(step)?.Replace("\n", " / ") ?? (step.Text >= 0 ? $"(missing text {step.Text})" : "");
            string what = TutorialText.Expand(TutorialFile.Describe(step), id => CardName(id) ?? $"#{id}");
            return new ListViewItem([(index + 1).ToString(), step.Id == 0xFFFF ? "-" : step.Id.ToString(), TutorialFile.OpName(step.Op), what, text]);
        }

        private void FillSteps(int select)
        {
            _syncing = true;
            _steps.BeginUpdate();
            _steps.Items.Clear();
            for (int i = 0; i < _file.Steps.Count; i++)
                _steps.Items.Add(ItemFor(i));
            _steps.EndUpdate();
            _syncing = false;
            if (_file.Steps.Count > 0)
            {
                int index = Math.Clamp(select, 0, _file.Steps.Count - 1);
                _steps.Items[index].Selected = true;
                _steps.EnsureVisible(index);
            }
            else
                ShowSelected();
        }

        private void RefreshItem(int index)
        {
            if (index < 0 || index >= _steps.Items.Count)
                return;
            var fresh = ItemFor(index);
            for (int column = 0; column < fresh.SubItems.Count; column++)
                _steps.Items[index].SubItems[column].Text = fresh.SubItems[column].Text;
        }

        private void SetEditable(bool editable)
        {
            _op.Enabled = _p1.Enabled = _p2.Enabled = _p3.Enabled = _p4.Enabled = _id.Enabled = _hasText.Enabled = editable;
            _text.Enabled = editable && _hasText.Checked;
            _stepTools.Enabled = editable;
        }

        private void ShowSelected()
        {
            if (_syncing)
                return;
            int index = SelectedIndex();
            _syncing = true;
            bool has = index >= 0 && index < _file.Steps.Count;
            SetEditable(has);
            if (has)
            {
                var step = _file.Steps[index];
                var item = _op.Items.Cast<OpItem>().FirstOrDefault(i => i.Op == step.Op);
                if (item == null)
                {
                    item = new OpItem(step.Op);
                    _op.Items.Add(item);
                }
                _op.SelectedItem = item;
                _p1.Value = step.P1;
                _p2.Value = step.P2;
                _p3.Value = step.P3;
                _p4.Value = step.P4;
                _id.Value = step.Id;
                _hasText.Checked = step.Text >= 0;
                _text.Text = (_file.TextOf(step) ?? "").Replace("\n", "\r\n");
                _text.Enabled = _hasText.Checked;
                ShowParamNames(step.Op);
            }
            _syncing = false;
            if (has && !_following && !_playTimer.Enabled)
            {
                // show the step as it plays (the first time playback gets there)
                int start = -1;
                for (int i = 0; i < _playback.Starts.Count; i++)
                    if (_playback.Starts[i].Step == index)
                    {
                        start = i;
                        break;
                    }
                if (start >= 0)
                    SeekStart(start, fromList: true);
                else
                    SetStatus($"Step {index + 1} isn't reached when the tutorial plays (after End, or skipped).");
            }
        }

        private void ShowParamNames(TutorialOp op)
        {
            string[] names = ParamNames(op);
            bool[] cards = CardParams(op);
            for (int i = 0; i < 4; i++)
            {
                _paramLabels[i].Text = $"P{i + 1}: {names[i]}";
                _cardButtons[i].Visible = cards[i];
            }
            _help.Text = OpHelp(op);
        }

        private static string[] ParamNames(TutorialOp op) => op switch
        {
            TutorialOp.SetupScenario => ["-", "board setup", "-", "-"],
            TutorialOp.Message => ["-", "-", "upper box (not 0)", "-"],
            TutorialOp.SetTurnPlayer => ["-", "player (0 you, 1 opp.)", "-", "-"],
            TutorialOp.Phase => ["-", "phase (0 Draw .. 5 End)", "banner only (not 0)", "-"],
            TutorialOp.MoveCursor => ["player", "zone", "index / card id", "-"],
            TutorialOp.Wait => ["-", "frames (60 = 1 s)", "-", "-"],
            TutorialOp.WaitForAction => ["action kind", "detail", "-", "-"],
            TutorialOp.Pointer => ["player", "zone", "frames shown", "-"],
            TutorialOp.PointerSimple => ["target (0 hides)", "-", "-", "-"],
            TutorialOp.ActivateAt => ["player", "zone", "-", "-"],
            TutorialOp.Label => ["label number", "-", "-", "-"],
            TutorialOp.Goto => ["-", "label number", "-", "-"],
            TutorialOp.AllowInput => ["-", "on (not 0)", "-", "-"],
            TutorialOp.StackDeck => ["-", "player", "deck index", "card id"],
            TutorialOp.AllowCards => ["-", "card id", "card id", "card id"],
            TutorialOp.ShowCard => ["-", "card id (0 clear, 2 current)", "-", "-"],
            TutorialOp.End => ["0 menu, 1 win, 2 leave", "-", "-", "-"],
            TutorialOp.Flag09 => ["6 = set the flag", "-", "-", "-"],
            TutorialOp.Set0A => ["-", "value", "value", "-"],
            TutorialOp.Set0F => ["-", "value", "-", "value"],
            TutorialOp.Set15 => ["-", "1 = unlock all input", "-", "-"],
            TutorialOp.Set19 or TutorialOp.Set1B or TutorialOp.Set1D => ["-", "value", "-", "-"],
            TutorialOp.Hint or TutorialOp.RetryStart or TutorialOp.RetryEnd or TutorialOp.Flag18 => ["-", "-", "-", "-"],
            _ => ["?", "?", "?", "?"],
        };

        private static bool[] CardParams(TutorialOp op) => op switch
        {
            TutorialOp.MoveCursor => [false, false, true, false],
            TutorialOp.ShowCard => [false, true, false, false],
            TutorialOp.StackDeck => [false, false, false, true],
            TutorialOp.AllowCards => [false, true, true, true],
            _ => [false, false, false, false],
        };

        private static string OpHelp(TutorialOp op) => op switch
        {
            TutorialOp.SetupScenario => "Deals the board for a setup built into the game: " + string.Join(", ", TutorialFile.Scenarios) + ". Another number deals the plain decks.",
            TutorialOp.Message => "Shows the text in the message box and waits for the player to read it.",
            TutorialOp.Hint => "Shows the text on the hint line; no text hides it.",
            TutorialOp.WaitForAction => "Waits until the player does something; the text (optional) goes on the hint line. Kind: " + TutorialFile.ActionName(1) + ", 3 select a card, 5 card menu...",
            TutorialOp.MoveCursor => "Zones (best guess): 0-4 monsters, 5-9 spells/traps, 10 field, 12 + a card id = that card in the hand, 14 Extra Deck, 15 Deck, 16 GY, 17 banished.",
            TutorialOp.Label => "A place Goto (and the game's own retry logic) can jump to.",
            TutorialOp.End => "Ends the tutorial and marks it done in the save.",
            TutorialOp.StackDeck => "Puts a card at a position in a deck (by swapping it up from further down) so the tutorial's draws are fixed.",
            _ => "",
        };

        private void FieldChanged()
        {
            if (_syncing)
                return;
            int index = SelectedIndex();
            if (index < 0 || index >= _file.Steps.Count)
                return;
            var step = _file.Steps[index];
            if (_op.SelectedItem is OpItem item && item.Op != step.Op)
            {
                step.Op = item.Op;
                ShowParamNames(step.Op);
            }
            step.P1 = (byte)_p1.Value;
            step.P2 = (ushort)_p2.Value;
            step.P3 = (ushort)_p3.Value;
            step.P4 = (ushort)_p4.Value;
            step.Id = (ushort)_id.Value;
            if (_hasText.Checked)
            {
                if (step.Text < 0 || step.Text >= _file.Texts.Count)
                {
                    step.Text = (short)_file.Texts.Count;
                    _file.Texts.Add("");
                }
                _file.Texts[step.Text] = _text.Text.Replace("\r\n", "\n");
            }
            else
                step.Text = -1;
            _text.Enabled = _hasText.Checked;
            RefreshItem(index);
            _rebuildTimer.Stop();
            _rebuildTimer.Start();
        }

        private void AddStep(TutorialOp op)
        {
            int at = SelectedIndex() + 1;
            if (at <= 0)
                at = _file.Steps.Count > 0 && _file.Steps[^1].Op == TutorialOp.End ? _file.Steps.Count - 1 : _file.Steps.Count;
            var step = new TutorialStep { Op = op };
            if (op is TutorialOp.Message or TutorialOp.Hint)
            {
                step.Text = (short)_file.Texts.Count;
                _file.Texts.Add("New text");
            }
            if (op == TutorialOp.Wait)
                step.P2 = 60;
            if (op == TutorialOp.End)
                step.Id = 0xFFFF;
            else
                step.Id = (ushort)(_file.Steps.Take(at).Where(s => s.Id != 0xFFFF).Select(s => s.Id + 1).DefaultIfEmpty(0).Max() & 0xFFFF);
            _file.Steps.Insert(at, step);
            SetEditable(true);
            FillSteps(at);
            RebuildPlayback();
            if (step.Text >= 0)
            {
                _text.Focus();
                _text.SelectAll();
            }
        }

        private void Duplicate()
        {
            int index = SelectedIndex();
            if (index < 0)
                return;
            var copy = _file.Steps[index].Clone();
            if (_file.TextOf(copy) is string text)
            {
                copy.Text = (short)_file.Texts.Count;
                _file.Texts.Add(text);
            }
            _file.Steps.Insert(index + 1, copy);
            FillSteps(index + 1);
            RebuildPlayback();
        }

        private void Remove()
        {
            int index = SelectedIndex();
            if (index < 0)
                return;
            _file.Steps.RemoveAt(index);
            FillSteps(Math.Min(index, _file.Steps.Count - 1));
            RebuildPlayback();
        }

        private void MoveStep(int direction)
        {
            int index = SelectedIndex(), to = index + direction;
            if (index < 0 || to < 0 || to >= _file.Steps.Count)
                return;
            var step = _file.Steps[index];
            _file.Steps.RemoveAt(index);
            _file.Steps.Insert(to, step);
            FillSteps(to);
            RebuildPlayback();
        }

        // ---- playback ----

        private void RebuildPlayback()
        {
            _playback = new TutorialPlayback(_file, (int)_messageFrames.Value, (int)_actionFrames.Value);
            _track.Maximum = Math.Max(0, _playback.TotalFrames);
            _frame = Math.Clamp(_frame, 0, _playback.TotalFrames);
            _start = Math.Clamp(_start, 0, Math.Max(0, _playback.Starts.Count - 1));
            if (_playback.Starts.Count > 0 && _playback.Starts[_start].Frame > _frame)
                _start = _playback.StartIndexAt(_frame);
            ShowFrame();
        }

        private void ShowFrame()
        {
            if (_file.Steps.Count == 0)
            {
                _preview.Show(null, 0);
                _time.Text = "";
                return;
            }
            var state = _playback.StateAt(_frame, _start);
            _preview.Show(state, _file.Steps.Count);
            _track.Value = Math.Clamp(_frame, _track.Minimum, _track.Maximum);
            _time.Text = $"frame {_frame}/{_playback.TotalFrames}  {_frame / 60.0:0.00} s  step {state.Step + 1}";
        }

        private void SeekFrame(int frame)
        {
            _frame = Math.Clamp(frame, 0, _playback.TotalFrames);
            _start = _playback.StartIndexAt(_frame);
            ShowFrame();
            FollowInList();
        }

        private void SeekStart(int start, bool fromList = false)
        {
            if (_playback.Starts.Count == 0)
                return;
            _start = Math.Clamp(start, 0, _playback.Starts.Count - 1);
            _frame = _playback.Starts[_start].Frame;
            ShowFrame();
            if (!fromList)
                FollowInList();
        }

        private void FollowInList()
        {
            if (!_follow.Checked || _playback.Starts.Count == 0)
                return;
            int step = _playback.Starts[_start].Step;
            if (step == SelectedIndex() || step >= _steps.Items.Count)
                return;
            _syncing = true;
            _steps.SelectedIndices.Clear();
            _steps.Items[step].Selected = true;
            _steps.EnsureVisible(step);
            _syncing = false;
            // the fields follow too, without seeking the playback back to the step's start
            _following = true;
            ShowSelected();
            _following = false;
        }

        private void TogglePlay()
        {
            if (_playTimer.Enabled)
            {
                StopPlaying();
                return;
            }
            if (_frame >= _playback.TotalFrames)
                SeekStart(0);
            _play.Text = "Pause";
            _playTimer.Start();
        }

        private void StopPlaying()
        {
            _playTimer.Stop();
            _play.Text = "Play";
        }

        private void PlayTick()
        {
            if (_frame >= _playback.TotalFrames)
            {
                StopPlaying();
                return;
            }
            _frame++;
            int start = _playback.StartIndexAt(_frame);
            bool newStep = start != _start;
            _start = start;
            ShowFrame();
            if (newStep)
                FollowInList();
        }

        // ---- script tab ----

        private string ScriptText() => _file.ToScript().Replace("\n", "\r\n");

        private void TabSelecting(object? sender, TabControlCancelEventArgs e)
        {
            if (e.TabPageIndex == 1)
            {
                StopPlaying();
                _script.Text = ScriptText();
            }
            else if (!ApplyScript())
                e.Cancel = true;
        }

        private bool ApplyScriptIfShowing() => _tabs.SelectedIndex != 1 || ApplyScript();

        /// <summary>Reads the script tab back. On errors, says which lines and keeps the script as typed.</summary>
        private bool ApplyScript()
        {
            if (_file.Steps.Count == 0 && _script.Text.Trim().Length == 0)
                return true;
            var problems = new List<string>();
            var file = TutorialFile.ParseScript(_script.Text, problems);
            var serious = problems.Where(p => !p.StartsWith("The last step", StringComparison.Ordinal)).ToList();
            if (serious.Count > 0)
            {
                MessageBox.Show(this, "The script has problems (nothing was changed):\n\n" + string.Join("\n", serious.Take(15)), "Tutorials",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            int selected = SelectedIndex();
            _file = file;
            FillSteps(selected);
            SetEditable(_file.Steps.Count > 0);
            RebuildPlayback();
            if (problems.Count > 0)
                SetStatus(problems[0]);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _playTimer.Dispose();
                _rebuildTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
