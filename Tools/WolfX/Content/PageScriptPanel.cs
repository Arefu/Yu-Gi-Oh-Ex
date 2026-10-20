using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ScintillaNET;

namespace WolfEx
{
    /// <summary>
    /// Art &amp; menus &gt; Page scripts: pages written in BOB (docs/PageScript.md). Saving writes the script (pages\&lt;name&gt;.bob), the page file
    /// Yu-Gi-Oh-RIX runs (pages\&lt;name&gt;.json) and, for "button on ..." lines, menus\bob-&lt;name&gt;.json. A version 2 page file with no script
    /// opens as a script made from it.
    /// </summary>
    internal sealed class PageScriptPanel : UserControl, IContentPanel
    {
        private const string Template =
            "// A page in BOB (docs/PageScript.md). Save writes pages\\<name>.json for Yu-Gi-Oh-RIX.\n" +
            "header \"My Page\";\n" +
            "button on main \"My Page\" \"Opens my page.\" -> page(\"{name}\");\n\n" +
            "menu choices at 160, 200 size 660, 500 {\n" +
            "    button \"Buy a Blue-Eyes\" \"1,000 DP for one copy.\" -> take_dp(1000) -> { give_card(4007); message(\"Bought!\"); } else message(\"Not enough DP.\");\n" +
            "    choice \"Difficulty\" [\"Easy\", \"Normal\", \"Hard\"] bind var:difficulty default \"Normal\";\n" +
            "    toggle \"Show extra text\" bind var:extra;\n" +
            "    button \"Back\" -> back;\n" +
            "}\n\n" +
            "text \"You have {game:dp} DP. Difficulty: {var:difficulty}\" at 1000, 220 width 760 textSize 26;\n" +
            "text \"The extra text.\" at 1000, 300 width 760 if var:extra;\n";

        private readonly PageScriptEditor _editor = new() { Dock = DockStyle.Fill };
        private readonly TextBox _preview = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5f) };
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripComboBox _pages = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, ToolTipText = "Pages in Yu-Gi-Oh-Ex\\pages (scripts, and version 2 page files)" };
        private readonly Label _status = new() { Dock = DockStyle.Bottom, Height = 22, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        private readonly System.Windows.Forms.Timer _compileTimer = new() { Interval = 300 };
        private string _folder = "", _menusFolder = "";
        private string? _name;
        private string _savedText = "";
        private bool _loadingList;

        public string Title => "Page scripts";

        public bool Dirty => _name != null && _editor.Text != _savedText;

        public PageScriptPanel()
        {
            _tools.Items.Add(new ToolStripLabel("Page:"));
            _tools.Items.Add(_pages);
            _pages.SelectedIndexChanged += (_, _) => { if (!_loadingList && _pages.SelectedItem is string name) Open(name); };
            _tools.Items.Add(new ToolStripButton("New...", null, (_, _) => New()) { ToolTipText = "Start a new page script" });
            _tools.Items.Add(new ToolStripButton("Save", null, (_, _) => Save()) { ToolTipText = "Compile and save (Ctrl+S)" });
            _tools.Items.Add(new ToolStripButton("Delete", null, (_, _) => Delete()) { ToolTipText = "Delete the script, its page file and its menu file" });
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripButton("Reference", null, (_, _) => ShowReference()) { ToolTipText = "What BOB can say on a page" });

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
            split.Panel1.Controls.Add(_editor);
            var previewBox = new GroupBox { Text = "What RIX gets (pages\\<name>.json)", Dock = DockStyle.Fill };
            previewBox.Controls.Add(_preview);
            split.Panel2.Controls.Add(previewBox);
            Controls.Add(split);
            Controls.Add(_tools);
            Controls.Add(_status);
            Load += (_, _) => split.SplitterDistance = Math.Max(200, Width * 3 / 5);

            _editor.TextChanged += (_, _) => { _compileTimer.Stop(); _compileTimer.Start(); };
            _compileTimer.Tick += (_, _) => { _compileTimer.Stop(); Check(); };
            SetEditable(false);
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

        public void LoadFrom(string extraCardsFolder, string gameFolder)
        {
            _folder = Path.Combine(extraCardsFolder, "pages");
            _menusFolder = Path.Combine(extraCardsFolder, "menus");
            RefreshList(_name);
            SetStatus($"Pages in {_folder}.");
        }

        public bool SaveTo(string extraCardsFolder) => !Dirty || Save();

        public void MarkSaved() => _savedText = _editor.Text;

        private void SetEditable(bool editable)
        {
            _editor.ReadOnly = !editable;
            foreach (ToolStripItem item in _tools.Items)
                if (item is ToolStripButton b && b.Text is "Save" or "Delete")
                    b.Enabled = editable;
        }

        private void SetStatus(string text) => _status.Text = text;

        private string ScriptPath(string name) => Path.Combine(_folder, name + ".bob");
        private string PagePath(string name) => Path.Combine(_folder, name + ".json");
        private string MenuPath(string name) => Path.Combine(_menusFolder, "bob-" + name + ".json");

        private void RefreshList(string? select)
        {
            _loadingList = true;
            _pages.Items.Clear();
            if (Directory.Exists(_folder))
            {
                var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string file in Directory.GetFiles(_folder, "*.bob"))
                    names.Add(Path.GetFileNameWithoutExtension(file));
                foreach (string file in Directory.GetFiles(_folder, "*.json"))
                    if (IsVersion2(file))
                        names.Add(Path.GetFileNameWithoutExtension(file));
                foreach (string name in names)
                    _pages.Items.Add(name);
            }
            _pages.SelectedItem = select;
            _loadingList = false;
        }

        private static bool IsVersion2(string file)
        {
            try
            {
                return JsonNode.Parse(File.ReadAllText(file), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })?["version"]?.GetValue<int>() == 2;
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or FormatException)
            {
                return false;
            }
        }

        private bool ConfirmDiscard() =>
            !Dirty || MessageBox.Show(this, $"Discard the changes to {_name}?", "Page scripts", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        private void Open(string name)
        {
            if (name == _name)
                return;
            if (!ConfirmDiscard())
            {
                RefreshList(_name);
                return;
            }
            try
            {
                string text;
                if (File.Exists(ScriptPath(name)))
                    text = File.ReadAllText(ScriptPath(name));
                else
                {
                    var page = (JsonObject)JsonNode.Parse(File.ReadAllText(PagePath(name)), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!;
                    var menus = File.Exists(MenuPath(name)) ? JsonNode.Parse(File.ReadAllText(MenuPath(name))) as JsonObject : null;
                    text = "// Made from " + name + ".json. Saving writes this script beside it.\n" + PageScript.Decompile(page, menus);
                }
                _name = name;
                SetEditable(true);   // first: Scintilla ignores Text while ReadOnly (no page open yet), so the first page opened came up empty
                _editor.Text = text.Replace("\r\n", "\n");
                _savedText = File.Exists(ScriptPath(name)) ? _editor.Text : "";
                _editor.EmptyUndoBuffer();
                Check();
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or InvalidCastException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"{name} could not be opened: {ex.Message}", "Page scripts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshList(_name);
            }
        }

        private void New()
        {
            if (string.IsNullOrEmpty(_folder))
            {
                MessageBox.Show(this, "Pick the game folder first.", "Page scripts");
                return;
            }
            if (!ConfirmDiscard())
                return;
            string? name = Prompt("New page", "The page's name (letters, digits, '.', '_', '-'). Menus open it with page(\"name\").");
            if (name == null)
                return;
            if (File.Exists(ScriptPath(name)) || File.Exists(PagePath(name)))
            {
                MessageBox.Show(this, $"There is already a page called {name}.", "Page scripts");
                return;
            }
            _name = name;
            SetEditable(true);   // before the text: Scintilla ignores Text while ReadOnly
            _editor.Text = Template.Replace("{name}", name);
            _savedText = "";
            Check();
            SetStatus($"New page {name}: not saved yet.");
        }

        private string? Prompt(string title, string text)
        {
            using var form = new Form { Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, ClientSize = new Size(420, 110), MinimizeBox = false, MaximizeBox = false };
            var label = new Label { Text = text, Location = new Point(10, 10), Size = new Size(400, 34) };
            var box = new TextBox { Location = new Point(10, 46), Width = 400 };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(254, 76) };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(334, 76) };
            form.Controls.AddRange([label, box, ok, cancel]);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            while (form.ShowDialog(this) == DialogResult.OK)
            {
                if (Regex.IsMatch(box.Text, @"^[A-Za-z0-9_.\-]+$"))
                    return box.Text;
                MessageBox.Show(this, "Use letters, digits, '.', '_' and '-' only.", title);
            }
            return null;
        }

        private PageScript.Result? Check()
        {
            if (_name == null)
                return null;
            try
            {
                var result = PageScript.Compile(_editor.Text, _name);
                _editor.ClearError();
                _preview.Text = (result.Page.ToJsonString(PageScript.Indented) +
                    (result.Menus != null ? $"\r\n\r\n// menus\\bob-{_name}.json\r\n" + result.Menus.ToJsonString(PageScript.Indented) : "")).Replace("\n", "\r\n").Replace("\r\r", "\r");
                SetStatus($"{_name}: compiles ({(result.Page["elements"] as JsonArray)?.Count ?? 0} widgets{(result.Menus != null ? ", menu buttons" : "")}).{(Dirty ? " Not saved." : "")}");
                return result;
            }
            catch (PageScript.Error ex)
            {
                _editor.ShowError(ex.Line, ex.Column);
                SetStatus(ex.Message);
                return null;
            }
        }

        private bool Save()
        {
            if (_name == null)
                return true;
            var result = Check();
            if (result == null)
            {
                MessageBox.Show(this, "The script has an error (see the bottom line), so nothing was saved.", "Page scripts", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            try
            {
                Directory.CreateDirectory(_folder);
                var options = PageScript.Indented;
                File.WriteAllText(ScriptPath(_name), _editor.Text.Replace("\n", "\r\n"));
                File.WriteAllText(PagePath(_name), result.Page.ToJsonString(options));
                if (result.Menus != null)
                {
                    Directory.CreateDirectory(_menusFolder);
                    File.WriteAllText(MenuPath(_name), result.Menus.ToJsonString(options));
                }
                else if (File.Exists(MenuPath(_name)))
                    File.Delete(MenuPath(_name));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "Save failed: " + ex.Message, "Page scripts", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
            MarkSaved();
            RefreshList(_name);
            SetStatus($"Saved {_name}.bob and {_name}.json{(result.Menus != null ? $" and menus\\bob-{_name}.json" : "")}. Menu buttons show after the game restarts.");
            return true;
        }

        private void Delete()
        {
            if (_name == null || MessageBox.Show(this, $"Delete {_name}.bob, {_name}.json and its menu file?", "Page scripts", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            foreach (string file in new[] { ScriptPath(_name), PagePath(_name), MenuPath(_name) })
                if (File.Exists(file))
                    File.Delete(file);
            _name = null;
            _editor.Text = "";
            _savedText = "";
            _preview.Text = "";
            SetEditable(false);
            RefreshList(null);
        }

        private void ShowReference() => MessageBox.Show(this,
            "header \"Title\";\n" +
            "button on main \"Label\" \"help\" -> action;      (a button on a game menu)\n" +
            "edit cardShop -> page(\"my-page\");      (changes one of the game's main menu buttons)\n" +
            "use \"other-page\";   value name = \"text {var:x}\";      (read as {value:name}, value:name, or by name inside {= ...})\n\n" +
            "Widgets: menu, text, image, slider, digits, card, cards, box, decks, deck, deckcards, series, characters, user, related, progress, option, pack, tag, spinner\n" +
            "  deckcards {var:deck}   progress {var:done} max 40   option \"Difficulty\" values [\"Easy\", \"Hard\"] bind var:d   pack 3\n" +
            "  tag \"Main Deck {var:count}\" width 400   spinner at 1800, 980\n" +
            "  fields: at x, y   size w, h   bind var:x   default v   if <condition>   -> action   onchange action   (any other: name value)\n" +
            "  menu name ... { button \"A\" \"help\" -> ...; choice \"B\" [\"x\", \"y\"] bind var:b; toggle \"C\" bind var:c; rows \"plugin.list\"; }\n\n" +
            "Actions: page(\"name\")  back  goto(deckEditor)  video(\"intro\")  call(\"plugin.action\")  quit\n" +
            "  set(var:x, \"text\")  toggle(var:x)  add(var:x, 1)  message(\"..\")  confirm(\"..\") -> action  if <condition> -> action else action\n" +
            "  popup \"Title\" { button ... }  show_card(4007)  reset_filter  sound(39)\n" +
            "  give_dp(500)  take_dp(500) -> action else action  give_card(4007, 1)  take_card(4007) -> .. else ..\n" +
            "  unlock(card_shop | battle_packs | duelist_challenge)  lock(..)  unlock_character(12)\n" +
            "  { a; b; c; } runs several in order\n\n" +
            "Conditions: var:x   not var:x   var:x = \"v\"   var:x != \"v\"   game:dp >= 1000   game:dp < 1000   joined with and\n" +
            "Values in text: {var:x} {config:Section/Key} {game:dp} {game:owned.4007} {game:cardname.4007} {game:unlocked.card_shop} {game:character.12} {game:language}\n\n" +
            "Cards: {game:atk.ID} def level kind spell trap monster link known rare rarity   {game:median.VALUE.rare}   {name|fallback}\n" +
            "Maths: {= clamp(round(ATK * 1.8 / 5), 100, 999999)}   if(test, a, b)  min max round floor ceil abs exp log sqrt   < <= == != and or not\n\n" +
            "Full reference: docs/PageScript.md", "BOB for pages");
    }

    /// <summary>The page script editor: Scintilla with BOB's colours for pages and a red squiggle at the compile error.</summary>
    internal sealed class PageScriptEditor : Scintilla
    {
        private const int StyleKeyword = 1, StyleAction = 2, StyleNumber = 3, StyleString = 4, StyleComment = 5, StyleRef = 6, StyleWidget = 7;
        private const int ErrorIndicator = 8;

        private static readonly Regex Token = new(
            "(?<comment>//[^\\n]*)|(?<string>\"(?:[^\"\\\\\\n]|\\\\.)*\"?)|(?<ref>(?:var|config|game):[^\\s,;{}()\\[\\]=!<>]+)|(?<number>-?\\d+(?:\\.\\d+)?)|(?<word>[A-Za-z_][A-Za-z_0-9.]*)",
            RegexOptions.Compiled);

        private static readonly HashSet<string> Widgets = new(PageScript.Widgets, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Actions = new(PageScript.Actions, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Keywords = new(PageScript.Keywords.Concat(["toggle", "if", "size", "at"]), StringComparer.OrdinalIgnoreCase);

        public PageScriptEditor()
        {
            WrapMode = WrapMode.None;
            TabWidth = 4;
            IndentWidth = 4;
            UseTabs = false;
            StyleResetDefault();
            Styles[Style.Default].Font = "Consolas";
            Styles[Style.Default].Size = 11;
            StyleClearAll();
            Styles[StyleKeyword].ForeColor = Color.FromArgb(0x00, 0x4F, 0xC4);
            Styles[StyleKeyword].Bold = true;
            Styles[StyleAction].ForeColor = Color.FromArgb(0x8B, 0x1A, 0x9B);
            Styles[StyleAction].Bold = true;
            Styles[StyleWidget].ForeColor = Color.FromArgb(0x00, 0x7A, 0x5E);
            Styles[StyleWidget].Bold = true;
            Styles[StyleNumber].ForeColor = Color.FromArgb(0xB0, 0x5A, 0x00);
            Styles[StyleString].ForeColor = Color.FromArgb(0xA3, 0x15, 0x15);
            Styles[StyleComment].ForeColor = Color.FromArgb(0x5A, 0x8A, 0x3C);
            Styles[StyleComment].Italic = true;
            Styles[StyleRef].ForeColor = Color.FromArgb(0x1E, 0x6E, 0x6E);
            Margins[0].Type = MarginType.Number;
            Margins[0].Width = 36;
            Styles[Style.LineNumber].ForeColor = Color.Gray;
            Indicators[ErrorIndicator].Style = IndicatorStyle.Squiggle;
            Indicators[ErrorIndicator].ForeColor = Color.Red;
            StyleNeeded += (_, _) => Colourise();
            TextChanged += (_, _) => Colourise();
        }

        public void ShowError(int line, int column)
        {
            ClearError();
            if (line - 1 >= Lines.Count || line < 1)
                return;
            var l = Lines[line - 1];
            string lineText = l.Text.TrimEnd('\r', '\n');
            int start = l.Position + System.Text.Encoding.UTF8.GetByteCount(lineText.AsSpan(0, Math.Min(column, lineText.Length)));
            int length = Math.Max(1, System.Text.Encoding.UTF8.GetByteCount(lineText) - (start - l.Position));
            IndicatorCurrent = ErrorIndicator;
            IndicatorFillRange(start, length);
        }

        public void ClearError()
        {
            IndicatorCurrent = ErrorIndicator;
            IndicatorClearRange(0, TextLength);
        }

        private void Colourise()
        {
            string text = Text;
            StartStyling(0);
            SetStyling(TextLength, Style.Default);
            foreach (Match m in Token.Matches(text))
            {
                int style = m.Groups["comment"].Success ? StyleComment : m.Groups["string"].Success ? StyleString : m.Groups["ref"].Success ? StyleRef
                    : m.Groups["number"].Success ? StyleNumber
                    : Widgets.Contains(m.Value) ? StyleWidget : Actions.Contains(m.Value) ? StyleAction : Keywords.Contains(m.Value) ? StyleKeyword : 0;
                if (style == 0)
                    continue;
                // Scintilla positions are bytes; the script is ASCII apart from text in quotes, so convert through the line
                int start = System.Text.Encoding.UTF8.GetByteCount(text.AsSpan(0, m.Index));
                int length = System.Text.Encoding.UTF8.GetByteCount(m.Value);
                StartStyling(start);
                SetStyling(length, style);
            }
        }
    }
}
