using System.IO;
using Types;

namespace Wolf.Editors
{
    /// <summary>
    /// Edits the How to Play help (main/howto_db/howtoplay_&lt;L&gt;.bin, one file per language; File Type Libraries/HowToPlay) two ways:
    /// Visual - the chapters and topics in a tree, their fields, and a preview of the game's screen that follows the selection;
    /// Script - the whole file as text (# chapter, ## topic, ### sub-topic, #! picture N, then the body), for bulk edits.
    /// "Open from game" opens the game's file for a language (or the JSON the old WolfEx saved in Yu-Gi-Oh-Ex\howtoplay, which moves into the
    /// game data when saved); Save puts it and your new pictures back into the game data. Open... / Save as... work on files anywhere on disk.
    /// There is no fixed limit on chapters or topics (the game keeps them in vectors), and new pictures can be added as
    /// main/howto_img/help_duelimg_NNN.png (1-255).
    /// </summary>
    public sealed class HowToPlayEditor : UserControl, IGameEditor
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripComboBox _language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
        private readonly ToolStripButton _openFromGame;
        private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
        private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false, FullRowSelect = true };
        private readonly TextBox _title = new() { Dock = DockStyle.Top };
        private readonly CheckBox _subTopic = new() { Text = "Sub-topic (the game shows it with a \"-\" in front)", Dock = DockStyle.Top, Height = 24 };
        private readonly NumericUpDown _picture = new() { Minimum = 0, Maximum = 255, Width = 70 };
        private readonly TextBox _body = new()
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Segoe UI", 10f),
        };
        private readonly TextBox _script = new()
        {
            Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true, ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = new Font("Consolas", 10f),
        };
        private readonly HowToPreview _preview = new() { Dock = DockStyle.Fill };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly System.Windows.Forms.Timer _previewTimer = new() { Interval = 200 };
        private readonly Dictionary<int, Image?> _pictures = [];
        private readonly Dictionary<int, byte[]> _importedPictures = [];

        private List<HowToChapter> _chapters = [];
        private byte[] _savedBytes = [];
        private string? _diskFile;          // a file on disk
        private char _gameLanguage = 'E';   // or the game's file for this language
        private bool _fromGame;
        private bool _syncing;
        private GameFolderFiles? _gameFiles;

        public HowToPlayEditor()
        {
            PreviewPopOut.Attach(_preview, "How to Play preview", doubleClick: true);
            PreviewPopOut.SetShape(_preview, new Size(1920, 1080));   // the game screen it draws
            _tools.Items.Add(Button("Open...", "Open a howtoplay_<L>.bin file", OpenFromDisk));
            _openFromGame = Button("Open from game", "Open the game's How to Play for the chosen language (your Yu-Gi-Oh-Ex JSON if you saved one)", () => OpenFromGame(_gameLanguage));
            _openFromGame.Visible = false;
            _tools.Items.Add(_openFromGame);
            _tools.Items.Add(Button("Save", "Ctrl+S", () => Save(false)));
            _tools.Items.Add(Button("Save as...", "Save to another file", () => Save(true)));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("Language:"));
            _language.SelectedIndexChanged += (_, _) => LanguageChosen();
            _tools.Items.Add(_language);
            _tools.Items.Add(Button("New language...", "Start another language's file from this one (the game picks the file by its language letter)", NewLanguage));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(Button("Add chapter", "A new chapter after the selected one", AddChapter));
            _tools.Items.Add(Button("Add topic", "A new topic in the selected chapter", () => AddTopic(false)));
            _tools.Items.Add(Button("Add sub-topic", "A new sub-topic (shown with a \"-\") in the selected chapter", () => AddTopic(true)));
            _tools.Items.Add(Button("Remove", "Remove the selected chapter or topic", Remove));
            _tools.Items.Add(Button("Up", "Move the selection up", () => Move(-1)));
            _tools.Items.Add(Button("Down", "Move the selection down", () => Move(1)));

            // Visual tab: tree | fields + preview
            var fields = new Panel { Dock = DockStyle.Fill };
            var pictureRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 32, WrapContents = false };
            pictureRow.Controls.Add(new Label { Text = "Picture (0 = none):", AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
            pictureRow.Controls.Add(_picture);
            var importButton = new System.Windows.Forms.Button { Text = "Use a picture file...", AutoSize = true };
            importButton.Click += (_, _) => ImportPicture();
            pictureRow.Controls.Add(importButton);
            pictureRow.Controls.Add(new Label
            {
                Text = "Markup: @7 blue, @B red, @0 back to normal, @/ @| other font.", AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 7, 3, 3),
            });
            fields.Controls.Add(_body);
            fields.Controls.Add(new Label { Text = "Text", Dock = DockStyle.Top, Height = 18 });
            fields.Controls.Add(pictureRow);
            fields.Controls.Add(_subTopic);
            fields.Controls.Add(_title);
            fields.Controls.Add(new Label { Text = "Title", Dock = DockStyle.Top, Height = 18 });

            var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            right.Panel1.Controls.Add(_preview);
            right.Panel2.Controls.Add(fields);
            var visual = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            visual.Panel1.Controls.Add(_tree);
            visual.Panel2.Controls.Add(right);

            var visualTab = new TabPage("Visual") { UseVisualStyleBackColor = true };
            visualTab.Controls.Add(visual);
            var scriptTab = new TabPage("Script") { UseVisualStyleBackColor = true };
            scriptTab.Controls.Add(_script);
            scriptTab.Controls.Add(new Label
            {
                Dock = DockStyle.Top, Height = 20, ForeColor = SystemColors.GrayText,
                Text = "# chapter   ## topic   ### sub-topic   #! picture N   then the text. A text line starting with # or \\ needs a \\ in front. Switching back to Visual applies it.",
            });
            _tabs.TabPages.Add(visualTab);
            _tabs.TabPages.Add(scriptTab);
            _tabs.Selecting += TabSelecting;

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(_tabs);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) =>
            {
                visual.SplitterDistance = 300;
                right.SplitterDistance = Math.Max(200, right.Height * 55 / 100);
            };

            _preview.Picture = PictureImage;
            _tree.AfterSelect += (_, _) => ShowSelected();
            _title.TextChanged += (_, _) => FieldChanged();
            _subTopic.CheckedChanged += (_, _) => FieldChanged();
            _picture.ValueChanged += (_, _) => FieldChanged();
            _body.TextChanged += (_, _) => FieldChanged();
            _previewTimer.Tick += (_, _) => { _previewTimer.Stop(); UpdatePreview(); };

            SetStatus("Open a howtoplay_<L>.bin file" + (_gameFiles != null ? ", or the game's" : "") + ".");
        }

        /// <summary>The open data: shows "Open from game" and opens the game's file (the language that was open, English at first).</summary>
        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            _openFromGame.Visible = files.Available;
            if (files.Available && (_diskFile == null || _chapters.Count == 0))
                OpenFromGame(_gameLanguage);
        }

        bool IGameEditor.Save() => Save(false);

        public IReadOnlyCollection<string> Files => _fromGame ? [HowToPlayFile.GamePath(_gameLanguage)] : [];

        public string SavesTo => "Standard: main\\howto_db\\howtoplay_<lang>.bin and new pictures as main\\howto_img\\help_duelimg_NNN.png (the game has no limit on chapters or topics).";

        public bool Dirty => !CurrentBytes().AsSpan().SequenceEqual(_savedBytes);

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
                Save(false);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void SetStatus(string text) => _status.Text = text;

        private byte[] CurrentBytes() => HowToPlayFile.FromChapters(_chapters).ToBytes();

        private bool ConfirmDiscard() =>
            !Dirty || MessageBox.Show(this, "The How to Play text has changes that are not saved. Discard them?", "How to Play",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        // ---- opening / saving ----

        private void SetDocument(HowToPlayFile file, string description)
        {
            var problems = new List<string>();
            _chapters = file.ToChapters(problems);
            _savedBytes = CurrentBytes();
            _importedPictures.Clear();
            ClearPictures();
            FillTree();
            FillLanguages();
            if (_tabs.SelectedIndex == 1)
                _script.Text = ScriptText();
            int topics = _chapters.Sum(c => c.Topics.Count);
            SetStatus($"{description}: {_chapters.Count} chapters, {topics} topics" + (problems.Count > 0 ? $". {problems.Count} odd records: " + string.Join(" ", problems.Take(3)) : ""));
        }

        public void OpenFile(string path)
        {
            try
            {
                var file = HowToPlayFile.Load(path);
                _diskFile = path;
                _fromGame = false;
                _pictureGameSearched = false;   // a file somewhere else may be under another game folder
                SetDocument(file, path);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"Couldn't read {path}:\n{ex.Message}", "How to Play", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenFromDisk()
        {
            if (!ConfirmDiscard())
                return;
            using var dialog = new OpenFileDialog { Title = "Open a How to Play file", Filter = "How to Play (howtoplay_*.bin)|howtoplay_*.bin|All files|*.*" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                OpenFile(dialog.FileName);
        }

        private string JsonPath(char language) => _gameFiles!.ExPath(Path.Combine(HowToPlayFile.JsonFolder, HowToPlayFile.JsonName(language)));

        /// <summary>The JSON the old WolfEx saved in Yu-Gi-Oh-Ex\howtoplay if there is one, else the game's own.</summary>
        private void OpenFromGame(char language)
        {
            if (_gameFiles == null)
                return;
            string json = JsonPath(language);
            string path = HowToPlayFile.GamePath(language);
            try
            {
                HowToPlayFile file;
                string description;
                if (File.Exists(json))
                {
                    file = HowToPlayFile.FromJson(File.ReadAllText(json));
                    description = json;
                }
                else
                {
                    var bytes = _gameFiles.Read(path);
                    if (bytes == null)
                    {
                        SetStatus($"The game has no {path} and there's no {json}.");
                        return;
                    }
                    file = HowToPlayFile.Parse(bytes);
                    description = path + " (the game's)";
                }
                _diskFile = null;
                _fromGame = true;
                _gameLanguage = language;
                SetDocument(file, description);
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException or IOException)
            {
                MessageBox.Show(this, $"Couldn't read How to Play ({language}):\n{ex.Message}", "How to Play", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        /// <summary>The languages there are files for: next to the open file, or in the game.</summary>
        private List<char> AvailableLanguages()
        {
            string jsonFolder = _gameFiles != null ? _gameFiles.ExPath(HowToPlayFile.JsonFolder) : "";
            IEnumerable<string> files = _fromGame && _gameFiles != null
                ? _gameFiles.Paths.Where(p => p.StartsWith(HowToPlayFile.GameFolder + "\\", StringComparison.OrdinalIgnoreCase))
                    .Concat(Directory.Exists(jsonFolder) ? Directory.EnumerateFiles(jsonFolder, "howtoplay_*.json") : [])
                : _diskFile != null && Directory.Exists(Path.GetDirectoryName(_diskFile))
                    ? Directory.EnumerateFiles(Path.GetDirectoryName(_diskFile)!, "howtoplay_*.bin")
                    : [];
            var letters = files.Select(HowToPlayFile.LanguageOf).Where(l => l != null).Select(l => l!.Value).Distinct().OrderBy(l => l).ToList();
            char current = CurrentLanguage();
            if (!letters.Contains(current))
                letters.Add(current);
            return letters;
        }

        private char CurrentLanguage() => _fromGame ? _gameLanguage : (_diskFile != null ? HowToPlayFile.LanguageOf(_diskFile) ?? 'E' : 'E');

        private void FillLanguages()
        {
            _syncing = true;
            _language.Items.Clear();
            foreach (char letter in AvailableLanguages())
                _language.Items.Add(new LanguageItem(letter));
            _language.SelectedItem = _language.Items.Cast<LanguageItem>().FirstOrDefault(i => i.Letter == CurrentLanguage());
            _syncing = false;
        }

        private sealed record LanguageItem(char Letter)
        {
            public override string ToString() => $"{HowToPlayFile.LanguageName(Letter)} ({Letter})";
        }

        private void LanguageChosen()
        {
            if (_syncing || _language.SelectedItem is not LanguageItem item || item.Letter == CurrentLanguage())
                return;
            if (!ConfirmDiscard())
            {
                FillLanguages();
                return;
            }
            if (_fromGame)
                OpenFromGame(item.Letter);
            else if (_diskFile != null)
            {
                string other = Path.Combine(Path.GetDirectoryName(_diskFile)!, HowToPlayFile.FileName(item.Letter));
                if (File.Exists(other))
                    OpenFile(other);
            }
        }

        /// <summary>Starts another language's file from the current text (to translate it): only the target of Save changes.</summary>
        private void NewLanguage()
        {
            using var form = new Form { Text = "New language", Size = new Size(360, 150), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MinimizeBox = false, MaximizeBox = false };
            var box = new TextBox { MaxLength = 1, Location = new Point(250, 16), Width = 40, CharacterCasing = CharacterCasing.Upper };
            form.Controls.Add(new Label { Text = "Language letter (E, F, G, I, J, S, ...):", Location = new Point(12, 19), AutoSize = true });
            form.Controls.Add(box);
            var ok = new System.Windows.Forms.Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(250, 60) };
            form.Controls.Add(ok);
            form.AcceptButton = ok;
            if (form.ShowDialog(this) != DialogResult.OK || box.Text.Length != 1 || !char.IsLetter(box.Text[0]))
                return;
            char letter = box.Text[0];
            if (_fromGame)
                _gameLanguage = letter;
            else if (_diskFile != null)
                _diskFile = Path.Combine(Path.GetDirectoryName(_diskFile)!, HowToPlayFile.FileName(letter));
            _savedBytes = [];   // nothing saved for this language yet
            FillLanguages();
            SetStatus($"Now editing {HowToPlayFile.LanguageName(letter)} ({letter}), starting from the text that was open. Save writes " +
                (_fromGame ? HowToPlayFile.GamePath(letter) + " in the game data" : HowToPlayFile.FileName(letter)) + ".");
        }

        private bool Save(bool saveAs)
        {
            if (!ApplyScriptIfShowing())
                return false;
            byte[] bytes = CurrentBytes();
            string? target = null;
            string pictureFolder;
            if (!saveAs && _fromGame && _gameFiles != null)
                return SaveToGame(bytes);

            if (!saveAs)
                target = _diskFile;
            if (target == null)
            {
                using var dialog = new SaveFileDialog
                {
                    Title = "Save the How to Play file",
                    Filter = "How to Play (howtoplay_*.bin)|howtoplay_*.bin",
                    FileName = HowToPlayFile.FileName(CurrentLanguage()),
                    InitialDirectory = _diskFile != null ? Path.GetDirectoryName(_diskFile) : null,
                };
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return false;
                target = dialog.FileName;
            }
            // main\howto_db\x.bin -> main\howto_img\ next to it, as in the game's layout
            pictureFolder = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(target)!) ?? "", "howto_img");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (target.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                File.WriteAllText(target, HowToPlayFile.FromChapters(_chapters).ToJson(CurrentLanguage()));
            else
                File.WriteAllBytes(target, bytes);
            foreach (var (number, picture) in _importedPictures)
            {
                Directory.CreateDirectory(pictureFolder);
                File.WriteAllBytes(Path.Combine(pictureFolder, $"help_duelimg_{number:000}.png"), picture);
            }
            _importedPictures.Clear();
            _savedBytes = bytes;
            if (!_fromGame || saveAs)
            {
                _diskFile = target;
                _fromGame = false;
            }
            SetStatus($"Saved {target}");
            return true;
        }

        /// <summary>
        /// The file and your new pictures into the game data. Pictures the old WolfEx saved in Yu-Gi-Oh-Ex\howtoplay that this file uses move
        /// in too, and its JSON for this language is deleted.
        /// </summary>
        private bool SaveToGame(byte[] bytes)
        {
            try
            {
                var write = new Dictionary<string, byte[]> { [HowToPlayFile.GamePath(_gameLanguage)] = bytes };
                foreach (var (number, picture) in _importedPictures)
                    write[HowToPlayFile.PicturePath(number)] = picture;
                var moved = new List<string>();
                foreach (int number in _chapters.SelectMany(c => c.Topics).Select(t => t.Picture).Where(n => n > 0).Distinct())
                {
                    string mine = _gameFiles!.ExPath(Path.Combine(HowToPlayFile.JsonFolder, $"help_duelimg_{number:000}.png"));
                    if (!write.ContainsKey(HowToPlayFile.PicturePath(number)) && File.Exists(mine))
                    {
                        write[HowToPlayFile.PicturePath(number)] = File.ReadAllBytes(mine);
                        moved.Add(mine);
                    }
                }
                _gameFiles!.Write(write);
                foreach (string old in moved.Append(JsonPath(_gameLanguage)))
                    if (File.Exists(old))
                        File.Delete(old);
                _importedPictures.Clear();
                _savedBytes = bytes;
                SetStatus($"Saved {HowToPlayFile.GamePath(_gameLanguage)}{(write.Count > 1 ? $" and {write.Count - 1} pictures" : "")} into {_gameFiles.Describe(HowToPlayFile.GamePath(_gameLanguage))}.");
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                SetStatus("Not saved: " + ex.Message);
                return false;
            }
        }

        // ---- pictures ----

        private Image? PictureImage(int number)
        {
            if (_pictures.TryGetValue(number, out var cached))
                return cached;
            byte[]? bytes = _importedPictures.GetValueOrDefault(number);
            string name = $"help_duelimg_{number:000}.png";
            if (bytes == null && _fromGame && _gameFiles != null && File.Exists(_gameFiles.ExPath(Path.Combine(HowToPlayFile.JsonFolder, name))))
                bytes = File.ReadAllBytes(_gameFiles.ExPath(Path.Combine(HowToPlayFile.JsonFolder, name)));   // a picture the old WolfEx saved
            if (bytes == null && _fromGame && _gameFiles != null)
                bytes = _gameFiles.Read(HowToPlayFile.PicturePath(number));
            if (bytes == null && _diskFile != null)
            {
                string folder = Path.GetDirectoryName(_diskFile)!;
                foreach (string candidate in new[] { Path.Combine(folder, "..", "howto_img", name), Path.Combine(folder, name) })
                    if (File.Exists(candidate))
                    {
                        bytes = File.ReadAllBytes(candidate);
                        break;
                    }
            }
            if (bytes == null)
                bytes = (_gameFiles ?? PictureGame())?.Read(HowToPlayFile.PicturePath(number));   // a disk file still shows the game's pictures

            Image? image = null;
            if (bytes != null)
            {
                try
                {
                    using var stream = new MemoryStream(bytes);
                    using var decoded = Image.FromStream(stream);
                    image = new Bitmap(decoded);
                }
                catch (ArgumentException) { }
            }
            _pictures[number] = image;
            return image;
        }

        private GameFolderFiles? _pictureGame;
        private bool _pictureGameSearched;

        /// <summary>
        /// Where a file on disk gets the game's pictures from (they live in YGO_2020.dat, main\howto_img, and are rarely extracted next to
        /// the text): the game folder above the file (the one with YGO_2020.toc - "Remaining Files" sits inside it), else the game folder
        /// WolfX remembers (%AppData%\WolfX\gamefolder.txt).
        /// </summary>
        private GameFolderFiles? PictureGame()
        {
            if (_pictureGameSearched)
                return _pictureGame;
            _pictureGameSearched = true;
            string? folder = null;
            for (var dir = _diskFile != null ? new DirectoryInfo(Path.GetDirectoryName(_diskFile)!) : null; dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "YGO_2020.toc")))
                {
                    folder = dir.FullName;
                    break;
                }
            if (folder == null)
            {
                string setting = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfX", "gamefolder.txt");
                try
                {
                    if (File.Exists(setting) && File.Exists(Path.Combine(File.ReadAllText(setting).Trim(), "YGO_2020.toc")))
                        folder = File.ReadAllText(setting).Trim();
                }
                catch (IOException) { }
            }
            _pictureGame = folder != null ? new GameFolderFiles(folder) : null;
            return _pictureGame;
        }

        private void ClearPictures()
        {
            foreach (var image in _pictures.Values)
                image?.Dispose();
            _pictures.Clear();
        }

        /// <summary>Uses a PNG for the selected topic: as its current picture number, or the next free one. Written when you save.</summary>
        private void ImportPicture()
        {
            if (_tree.SelectedNode?.Tag is not HowToTopic topic)
            {
                SetStatus("Pick a topic first: pictures belong to a topic's text.");
                return;
            }
            using var dialog = new OpenFileDialog { Title = "Picture for this topic", Filter = "PNG pictures (*.png)|*.png" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            int number = topic.Picture;
            if (number == 0)
            {
                var used = _chapters.SelectMany(c => c.Topics).Select(t => (int)t.Picture).ToHashSet();
                number = Enumerable.Range(101, 155).FirstOrDefault(n => !used.Contains(n) && PictureImage(n) == null);
                if (number == 0)
                {
                    SetStatus("All picture numbers are taken.");
                    return;
                }
            }
            _importedPictures[number] = File.ReadAllBytes(dialog.FileName);
            if (_pictures.Remove(number, out var old))
                old?.Dispose();
            _picture.Value = number;
            FieldChanged();
            SetStatus($"Picture {number} (help_duelimg_{number:000}.png) will be saved with the text.");
        }

        // ---- tree and fields ----

        private void FillTree(object? select = null)
        {
            _syncing = true;
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            TreeNode? selectedNode = null;
            foreach (var chapter in _chapters)
            {
                var node = new TreeNode(chapter.Title) { Tag = chapter };
                foreach (var topic in chapter.Topics)
                {
                    var child = new TreeNode(topic.ToString()) { Tag = topic };
                    node.Nodes.Add(child);
                    if (topic == select)
                        selectedNode = child;
                }
                _tree.Nodes.Add(node);
                if (chapter == select)
                    selectedNode = node;
            }
            _tree.EndUpdate();
            _syncing = false;
            _tree.SelectedNode = selectedNode ?? (_tree.Nodes.Count > 0 ? _tree.Nodes[0] : null);
            if (_tree.SelectedNode == null)
                ShowSelected();
        }

        private void ShowSelected()
        {
            if (_syncing)
                return;
            _syncing = true;
            object? tag = _tree.SelectedNode?.Tag;
            bool isTopic = tag is HowToTopic;
            _title.Enabled = tag != null;
            _subTopic.Enabled = _picture.Enabled = _body.Enabled = isTopic;
            _title.Text = tag switch { HowToChapter c => c.Title, HowToTopic t => t.Title, _ => "" };
            _subTopic.Checked = tag is HowToTopic { IsSubTopic: true };
            _picture.Value = tag is HowToTopic topic ? topic.Picture : 0;
            _body.Text = tag is HowToTopic t2 ? t2.Body.Replace("\n", "\r\n") : "";
            _syncing = false;
            UpdatePreview();
        }

        private void FieldChanged()
        {
            if (_syncing)
                return;
            switch (_tree.SelectedNode?.Tag)
            {
                case HowToChapter chapter:
                    chapter.Title = _title.Text;
                    _tree.SelectedNode.Text = chapter.Title;
                    break;
                case HowToTopic topic:
                    topic.Title = _title.Text;
                    topic.IsSubTopic = _subTopic.Checked;
                    topic.Picture = (byte)_picture.Value;
                    topic.Body = _body.Text.Replace("\r\n", "\n");
                    _tree.SelectedNode.Text = topic.ToString();
                    break;
            }
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private (int Chapter, int Topic) Selection()
        {
            var node = _tree.SelectedNode;
            if (node == null)
                return (-1, -1);
            if (node.Tag is HowToTopic)
                return (node.Parent.Index, node.Index);
            return (node.Index, _chapters[node.Index].Topics.Count > 0 ? 0 : -1);
        }

        private void UpdatePreview()
        {
            var (chapter, topic) = Selection();
            _preview.Show(_chapters, chapter, topic);
        }

        private HowToChapter? SelectedChapter() => _tree.SelectedNode?.Tag switch
        {
            HowToChapter c => c,
            HowToTopic => _tree.SelectedNode.Parent?.Tag as HowToChapter,
            _ => null,
        };

        private void AddChapter()
        {
            var chapter = new HowToChapter { Title = "New chapter" };
            var after = SelectedChapter();
            _chapters.Insert(after == null ? _chapters.Count : _chapters.IndexOf(after) + 1, chapter);
            chapter.Topics.Add(new HowToTopic { Title = "New topic", Body = "Text" });
            FillTree(chapter);
            _title.Focus();
            _title.SelectAll();
        }

        private void AddTopic(bool sub)
        {
            var chapter = SelectedChapter();
            if (chapter == null)
            {
                SetStatus("Add a chapter first.");
                return;
            }
            var topic = new HowToTopic { Title = sub ? "New sub-topic" : "New topic", IsSubTopic = sub, Body = "Text" };
            int at = _tree.SelectedNode?.Tag is HowToTopic selected ? chapter.Topics.IndexOf(selected) + 1 : chapter.Topics.Count;
            chapter.Topics.Insert(at, topic);
            FillTree(topic);
            _title.Focus();
            _title.SelectAll();
        }

        private void Remove()
        {
            switch (_tree.SelectedNode?.Tag)
            {
                case HowToChapter chapter:
                    if (MessageBox.Show(this, $"Remove the chapter \"{chapter.Title}\" and its {chapter.Topics.Count} topics?", "How to Play",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                        return;
                    int index = _chapters.IndexOf(chapter);
                    _chapters.Remove(chapter);
                    FillTree(_chapters.Count > 0 ? _chapters[Math.Min(index, _chapters.Count - 1)] : null);
                    break;
                case HowToTopic topic:
                    var owner = SelectedChapter()!;
                    int at = owner.Topics.IndexOf(topic);
                    owner.Topics.Remove(topic);
                    FillTree(owner.Topics.Count > 0 ? owner.Topics[Math.Min(at, owner.Topics.Count - 1)] : owner);
                    break;
            }
        }

        private void Move(int direction)
        {
            switch (_tree.SelectedNode?.Tag)
            {
                case HowToChapter chapter:
                    MoveIn(_chapters, chapter, direction);
                    FillTree(chapter);
                    break;
                case HowToTopic topic:
                    MoveIn(SelectedChapter()!.Topics, topic, direction);
                    FillTree(topic);
                    break;
            }
        }

        private static void MoveIn<T>(List<T> list, T item, int direction)
        {
            int index = list.IndexOf(item), to = index + direction;
            if (index < 0 || to < 0 || to >= list.Count)
                return;
            list.RemoveAt(index);
            list.Insert(to, item);
        }

        // ---- script tab ----

        private string ScriptText() => HowToPlayFile.FromChapters(_chapters).ToScript().Replace("\n", "\r\n");

        private void TabSelecting(object? sender, TabControlCancelEventArgs e)
        {
            if (e.TabPageIndex == 1)
                _script.Text = ScriptText();
            else if (!ApplyScript())
                e.Cancel = true;
        }

        private bool ApplyScriptIfShowing() => _tabs.SelectedIndex != 1 || ApplyScript();

        /// <summary>Reads the script tab back into the chapters. On errors, says which lines and keeps the script as typed.</summary>
        private bool ApplyScript()
        {
            var problems = new List<string>();
            var file = HowToPlayFile.ParseScript(_script.Text, problems);
            if (problems.Count > 0)
            {
                MessageBox.Show(this, "The script has problems (nothing was changed):\n\n" + string.Join("\n", problems.Take(15)), "How to Play",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            var (chapter, topic) = Selection();
            _chapters = file.ToChapters();
            object? select = chapter >= 0 && chapter < _chapters.Count
                ? topic >= 0 && topic < _chapters[chapter].Topics.Count ? _chapters[chapter].Topics[topic] : _chapters[chapter]
                : null;
            FillTree(select);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _previewTimer.Dispose();
                ClearPictures();
            }
            base.Dispose(disposing);
        }
    }
}
