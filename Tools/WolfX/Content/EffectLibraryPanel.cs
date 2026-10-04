using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Types;
using Wolf.Editors;

namespace WolfEx
{
    /// <summary>
    /// "Effect library": the game's own cards with their effect written in EffectScript, wherever the language can say it (made by
    /// docs/effect-scripts/build_effect_reference.py from the game's effect tables, shipped next to WolfX as effect_reference.json), plus
    /// your own entries. It exists to learn the language from cards you know, to keep scripts you reuse, and to copy one as the start of a
    /// new card's effect.
    /// Everything is editable, as script or as blocks: changing a game card's script, or adding an entry of your own, is kept in
    /// %APPDATA%\WolfX\effect_library.json ("Save to my library", Ctrl+S); effect_reference.json is never written, so "Revert" gives the
    /// generated script back. The library is a tool for writing effects: nothing in it reaches the game until it is used on a card.
    /// The entries are a tree: "What it does" groups them by genre (the game's own effect categories, bin\CARD_Genre.bin of the open game data -
    /// a card with several genres is under each of them), "Script action" by what the script does first (search, revive, draw ...).
    /// </summary>
    internal sealed class EffectLibraryPanel : UserControl
    {
        private enum Origin { Game, Edited, Mine }

        private sealed class Entry
        {
            public int Id;
            public string Name = "", Kind = "", Text = "", Script = "", GameScript = "";
            public bool Complete;
            public string[] Notes = [];
            public Origin Origin;
            public string Action = "";   // the script's main action ("search", "destroy" ...)
            public ulong Genres;          // CARD_Genre bits of the game card (0 = none or no game data)

            public override string ToString() =>
                (Origin == Origin.Mine ? "★ " : Origin == Origin.Edited ? "✎ " : "") + (Complete || Origin == Origin.Mine ? "" : "~ ") + Name;
        }

        private static string UserFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WolfX", "effect_library.json");

        private readonly List<Entry> _all = [];
        private readonly HashSet<Entry> _unsaved = [];
        private readonly TextBox _filter = new() { PlaceholderText = "Filter: name, text or script", Dock = DockStyle.Top };
        private readonly CheckBox _onlyComplete = new() { Text = "Only cards that are exactly this effect", Dock = DockStyle.Top, AutoSize = true };
        private readonly CheckBox _spellsOnly = new() { Text = "Spells and Traps only", Dock = DockStyle.Top, AutoSize = true };
        private readonly CheckBox _mineOnly = new() { Text = "Only mine and edited ones", Dock = DockStyle.Top, AutoSize = true };
        private GameFolderFiles? _genreSource;   // the game data the genres were read from
        // root (grouping) -> category -> entries; a category's entries are added when it is opened
        private readonly TreeView _tree = new() { Dock = DockStyle.Fill, HideSelection = false, ShowLines = true };
        private readonly TextBox _name = new() { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 11F, FontStyle.Bold), BorderStyle = BorderStyle.None };
        private readonly Label _origin = new() { AutoSize = true, ForeColor = SystemColors.GrayText, Anchor = AnchorStyles.Right };
        private readonly TextBox _text = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };
        private readonly ScriptEditor _script = new() { Dock = DockStyle.Fill };
        // fixed heights: an AutoSize label as wide as its text would widen the whole column past the window
        private readonly Label _notes = new() { Dock = DockStyle.Fill, Height = 34, AutoEllipsis = true, ForeColor = Color.DarkGoldenrod };
        private readonly Label _status = new() { Dock = DockStyle.Fill, Height = 34, AutoEllipsis = true };
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden, Dock = DockStyle.Top };
        private readonly ToolStripButton _save, _new, _check, _revert, _use, _copy;
        private bool _binding;

        /// <summary>Raised when the user wants the script in the Effects tab (the handler switches tabs and returns whether it was applied).</summary>
        public Func<string, bool>? UseTemplate;

        public EffectLibraryPanel()
        {
            _save = Button("Save to my library", "Keep your changes and entries (Ctrl+S): %APPDATA%\\WolfX\\effect_library.json", SaveLibrary);
            _new = Button("New entry", "An entry of your own: a script you want to reuse", NewEntry);
            _check = Button("Check", "Compile the script and say whether the game can run it", Check);
            _revert = Button("Revert", "", RevertOrDelete);
            _use = Button("Use on the Effects page", "Put this script into the editor of the card picked on the Effects page", Use);
            _copy = Button("Copy script", "", () =>
            {
                if (Current is { } entry && entry.Script.Length > 0)
                {
                    Clipboard.SetText(entry.Script);
                    _status.Text = "Copied.";
                }
            });
            _tools.Items.AddRange([_save, new ToolStripSeparator(), _new, _revert, new ToolStripSeparator(), _check, _use, _copy]);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            var left = new Panel { Dock = DockStyle.Fill };
            left.Controls.Add(_tree);
            left.Controls.Add(_mineOnly);
            left.Controls.Add(_spellsOnly);
            left.Controls.Add(_onlyComplete);
            left.Controls.Add(_filter);
            split.Panel1.Controls.Add(left);

            var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Height = 30, Margin = Padding.Empty };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            heading.Controls.Add(_name, 0, 0);
            heading.Controls.Add(_origin, 1, 0);

            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8, 4, 8, 4) };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 32F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 18F));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 82F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
            right.Controls.Add(heading, 0, 0);
            right.Controls.Add(_text, 0, 1);
            right.Controls.Add(_script, 0, 2);
            right.Controls.Add(_notes, 0, 3);
            right.Controls.Add(_status, 0, 4);
            split.Panel2.Controls.Add(right);
            split.Panel2.Controls.Add(_tools);
            Controls.Add(split);
            ScriptBlocksTabs.Replace(_script);
            Load += (_, _) => split.SplitterDistance = Math.Clamp(Width * 30 / 100, 220, 320);

            _filter.TextChanged += (_, _) => Refill();
            _onlyComplete.CheckedChanged += (_, _) => Refill();
            _spellsOnly.CheckedChanged += (_, _) => Refill();
            _mineOnly.CheckedChanged += (_, _) => Refill();
            _tree.BeforeExpand += (_, e) => FillCategory(e.Node);
            // WolfX opens the last game data after the pages are made (and File > Open changes it): read its genres each time
            Action dataChanged = () =>
            {
                if (GameFolderFiles.Current != _genreSource)
                    FillCategories();
            };
            GameFolderFiles.CurrentChanged += dataChanged;
            Disposed += (_, _) => GameFolderFiles.CurrentChanged -= dataChanged;
            VisibleChanged += (_, _) =>
            {
                if (Visible && GameFolderFiles.Current != null)
                    FillCategories();   // genres saved on the Card genres page since (the open, picked entry and filters are kept)
            };
            _tree.AfterSelect += (_, _) =>
            {
                if (!_filling)
                    Show(Current);
            };
            _script.TextChanged += (_, _) => Edited(entry => entry.Script = _script.Text);
            _name.TextChanged += (_, _) => Edited(entry => entry.Name = _name.Text, mineOnly: true);
            _text.TextChanged += (_, _) => Edited(entry => entry.Text = _text.Text, mineOnly: true);

            LoadLibrary();
            FillCategories();
        }

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { DisplayStyle = ToolStripItemDisplayStyle.Text, ToolTipText = tip };
            button.Click += (_, _) => click();
            return button;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveLibrary();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Entry? Current => _tree.SelectedNode?.Tag as Entry;

        // ---- reading and writing ----

        private void LoadLibrary()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "effect_reference.json");
            if (!File.Exists(path))
                _status.Text = "effect_reference.json is missing next to WolfX (make it with docs\\effect-scripts\\build_effect_reference.py).";
            else
            {
                try
                {
                    if (JsonNode.Parse(File.ReadAllText(path))?["cards"] is JsonArray cards)
                        foreach (var node in cards.OfType<JsonObject>())
                        {
                            string script = node["script"]?.GetValue<string>() ?? "";
                            _all.Add(new Entry
                            {
                                Id = node["id"]?.GetValue<int>() ?? 0, Name = node["name"]?.GetValue<string>() ?? "", Kind = node["kind"]?.GetValue<string>() ?? "",
                                Text = node["text"]?.GetValue<string>() ?? "", Script = script, GameScript = script, Complete = node["complete"]?.GetValue<bool>() ?? false,
                                Notes = (node["notes"] as JsonArray)?.Select(n => n?.GetValue<string>() ?? "").ToArray() ?? [], Origin = Origin.Game,
                                Action = node["action"]?.GetValue<string>() ?? ActionOf(script),
                            });
                        }
                }
                catch (Exception e) when (e is IOException or JsonException or InvalidOperationException)
                {
                    _status.Text = "Could not read effect_reference.json: " + e.Message;
                }
            }

            // yours: edits of game entries (by id) and your own entries
            try
            {
                if (File.Exists(UserFile) && JsonNode.Parse(File.ReadAllText(UserFile))?["entries"] is JsonArray mine)
                    foreach (var node in mine.OfType<JsonObject>())
                    {
                        string script = node["script"]?.GetValue<string>() ?? "";
                        if (node["mine"]?.GetValue<bool>() == true)
                            _all.Insert(0, new Entry
                            {
                                Name = node["name"]?.GetValue<string>() ?? "", Kind = node["kind"]?.GetValue<string>() ?? "", Text = node["text"]?.GetValue<string>() ?? "",
                                Script = script, Complete = true, Origin = Origin.Mine, Action = ActionOf(script),
                            });
                        else if (_all.FirstOrDefault(e => e.Origin == Origin.Game && e.Id == (node["id"]?.GetValue<int>() ?? -1)) is { } game)
                        {
                            game.Script = script;
                            game.Origin = Origin.Edited;
                        }
                    }
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidOperationException)
            {
                _status.Text = $"Could not read {UserFile}: {e.Message}";
            }
            if (_status.Text.Length == 0)
                _status.Text = $"{_all.Count(e => e.Origin != Origin.Mine)} game cards with an effect the language can describe, {_all.Count(e => e.Origin == Origin.Mine)} of your own. " +
                               "~ = the card does more than the script says; ✎ = you changed it; ★ = your own entry.";
        }

        private void SaveLibrary()
        {
            var entries = new JsonArray();
            foreach (var entry in _all)
            {
                if (entry.Origin == Origin.Mine)
                    entries.Add(new JsonObject { ["mine"] = true, ["name"] = entry.Name, ["kind"] = entry.Kind, ["text"] = entry.Text, ["script"] = entry.Script });
                else if (entry.Origin == Origin.Edited)
                    entries.Add(new JsonObject { ["id"] = entry.Id, ["name"] = entry.Name, ["script"] = entry.Script });
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(UserFile)!);
                var options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true };
                File.WriteAllText(UserFile, new JsonObject { ["entries"] = entries }.ToJsonString(options) + "\n", new UTF8Encoding(false));
                _unsaved.Clear();
                _status.Text = $"Saved {entries.Count} entr{(entries.Count == 1 ? "y" : "ies")} to {UserFile}.";
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + e.Message;
            }
        }

        // ---- categories ----

        /// <summary>One category node: its name and which entries belong to it.</summary>
        private sealed record Category(string Name, Func<Entry, bool> Match);

        private const string GenreRoot = "What it does", ActionRoot = "Script action";
        private readonly List<(string Root, Category Category)> _categories = [];
        private bool _filling;

        // the script's first action: "on summon: search(deck, ...)" -> "search"; as("Card") -> "as (clone)"
        private static readonly Regex FirstCall = new(@"(?:^|[;:]\s*)(?!on\b|once_per_turn\b|cost\b|if\b)(?<name>[a-z_]+)\s*\(", RegexOptions.Compiled | RegexOptions.Multiline);

        private static string ActionOf(string script)
        {
            string code = string.Join("\n", script.Split('\n').Where(line => !line.TrimStart().StartsWith("//")));
            var match = FirstCall.Match(code);
            return !match.Success ? "" : match.Groups["name"].Value == "as" ? "as (clone)" : match.Groups["name"].Value;
        }

        /// <summary>Reads each game card's genres from the open game data (bin\CARD_Genre.bin by CARD_IntID.bin); none without game data.</summary>
        private bool ReadGenres()
        {
            var files = GameFolderFiles.Current;
            _genreSource = files;
            foreach (var entry in _all)
                entry.Genres = 0;
            if (files == null)
                return false;
            try
            {
                if (files.Read(CardGenreTable.GamePath) is not { } genreBytes || files.Read(CardIdMap.GamePath) is not { } idBytes)
                    return false;
                var genres = CardGenreTable.Parse(genreBytes);
                var ids = CardIdMap.Parse(idBytes);
                foreach (var entry in _all)
                {
                    int id = entry.Id;
                    if (entry.Origin == Origin.Mine || id <= 0)
                        continue;
                    int index = ids.InternalOf(id);
                    if (index > 0 && index < genres.Masks.Length)
                        entry.Genres = genres.Masks[index] & ~CardGenreTable.HiddenMask;
                }
                return true;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
            {
                _status.Text = "Could not read the genres: " + e.Message;
                return false;
            }
        }

        /// <summary>The categories of both groupings, biggest first (genres need the game data: read again when it changes).</summary>
        private void FillCategories()
        {
            _categories.Clear();
            if (ReadGenres())
            {
                var genres = new List<Category>();
                foreach (var genre in CardGenreTable.Genres.Where(g => !g.Hidden && !g.Unused))
                {
                    ulong bit = 1UL << genre.Bit;
                    if (_all.Any(e => (e.Genres & bit) != 0))
                        genres.Add(new Category(genre.Name, e => (e.Genres & bit) != 0));
                }
                foreach (var category in genres.OrderByDescending(c => _all.Count(c.Match)))
                    _categories.Add((GenreRoot, category));
                if (_all.Any(e => e.Genres == 0))
                    _categories.Add((GenreRoot, new Category("No genre", e => e.Genres == 0)));
            }
            foreach (var group in _all.GroupBy(e => e.Action).OrderByDescending(g => g.Count()))
            {
                string action = group.Key;
                _categories.Add((ActionRoot, new Category(action.Length == 0 ? "(no action)" : action, e => e.Action == action)));
            }
            Refill();
        }

        // ---- the list ----

        private bool Passes(Entry entry)
        {
            string filter = _filter.Text.Trim();
            if (_onlyComplete.Checked && !entry.Complete)
                return false;
            if (_spellsOnly.Checked && entry.Kind != "Spell" && entry.Kind != "Trap")
                return false;
            if (_mineOnly.Checked && entry.Origin == Origin.Game)
                return false;
            return filter.Length == 0 || entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || entry.Text.Contains(filter, StringComparison.OrdinalIgnoreCase)
                   || entry.Script.Contains(filter, StringComparison.OrdinalIgnoreCase);
        }

        private static string PathOf(TreeNode? node) => node == null ? "" : node.Parent == null ? node.Name : PathOf(node.Parent) + "/" + node.Name;

        /// <summary>Adds a category's entries the first time it is opened (its placeholder child goes).</summary>
        private void FillCategory(TreeNode node)
        {
            if (node.Tag is not Category category || node.Nodes.Count != 1 || node.Nodes[0].Tag != null)
                return;
            _tree.BeginUpdate();
            node.Nodes.Clear();
            foreach (var entry in _all.Where(e => category.Match(e) && Passes(e)))
                node.Nodes.Add(new TreeNode(entry.ToString()) { Tag = entry, Name = entry.GetHashCode().ToString() });
            _tree.EndUpdate();
        }

        /// <summary>Builds the tree again (filters changed), keeping what was open and the picked entry where it was.</summary>
        private void Refill(Entry? select = null)
        {
            select ??= Current;
            string selectedPath = PathOf(_tree.SelectedNode?.Tag is Entry ? _tree.SelectedNode.Parent : _tree.SelectedNode);
            var open = new HashSet<string>();
            foreach (TreeNode root in _tree.Nodes)
            {
                if (root.IsExpanded)
                    open.Add(PathOf(root));
                foreach (TreeNode category in root.Nodes)
                    if (category.IsExpanded)
                        open.Add(PathOf(category));
            }
            bool first = _tree.Nodes.Count == 0;
            bool filtering = _filter.Text.Trim().Length > 0;

            _filling = true;
            _tree.BeginUpdate();
            try
            {
                _tree.Nodes.Clear();
                var all = _all.Where(Passes).ToList();
                foreach (string rootName in new[] { GenreRoot, ActionRoot })
                {
                    var root = _tree.Nodes.Add(rootName, rootName);
                    var categories = _categories.Where(c => c.Root == rootName).ToList();
                    if (categories.Count == 0)
                        root.Nodes.Add(rootName == GenreRoot ? "(no genres: the game data has no bin\\CARD_Genre.bin)" : "(no entries)");
                    foreach (var (_, category) in categories)
                    {
                        int count = all.Count(category.Match);
                        if (count == 0 && (filtering || _onlyComplete.Checked || _spellsOnly.Checked || _mineOnly.Checked))
                            continue;   // empty because of the filters: hidden
                        var node = root.Nodes.Add(category.Name, $"{category.Name} ({count})");
                        node.Tag = category;
                        node.Nodes.Add(new TreeNode("...") { Tag = null });   // placeholder: filled on opening
                        if (open.Contains(PathOf(node)) || (filtering && count > 0) || PathOf(node) == selectedPath)
                        {
                            FillCategory(node);
                            node.Expand();
                        }
                    }
                    if (first || open.Contains(PathOf(root)) || filtering)
                        root.Expand();
                }
            }
            finally
            {
                _tree.EndUpdate();
                _filling = false;
            }

            // the picked entry: where it was, else its first place in the tree
            TreeNode? target = null;
            if (select != null)
            {
                var categoryNode = _tree.Nodes.Find(selectedPath.Split('/').LastOrDefault() ?? "", true)
                    .FirstOrDefault(n => PathOf(n) == selectedPath && n.Tag is Category);
                if (categoryNode == null)
                    categoryNode = _tree.Nodes.Cast<TreeNode>().SelectMany(r => r.Nodes.Cast<TreeNode>())
                        .FirstOrDefault(n => n.Tag is Category c && c.Match(select) && Passes(select));
                if (categoryNode != null)
                {
                    FillCategory(categoryNode);
                    target = categoryNode.Nodes.Cast<TreeNode>().FirstOrDefault(n => n.Tag == select);
                }
            }
            if (target != null)
            {
                _tree.SelectedNode = target;
                target.EnsureVisible();
            }
            else
                Show(Current);
        }

        private void Show(Entry? entry)
        {
            _binding = true;
            try
            {
                _name.Text = entry?.Name ?? "";
                _text.Text = entry?.Text ?? "";
                _script.Text = entry?.Script ?? "";
                bool mine = entry?.Origin == Origin.Mine;
                _name.ReadOnly = _text.ReadOnly = !mine;
                _name.BackColor = mine ? SystemColors.Window : SystemColors.Control;
                _text.BackColor = mine ? SystemColors.Window : SystemColors.Control;
                _script.Enabled = entry != null;
                _origin.Text = entry == null ? "" : entry.Origin switch
                {
                    Origin.Mine => "your own entry" + (_unsaved.Contains(entry) ? " (not saved)" : ""),
                    Origin.Edited => $"game card {entry.Id}, {entry.Kind}: script changed by you" + (_unsaved.Contains(entry) ? " (not saved)" : ""),
                    _ => $"game card {entry.Id}, {entry.Kind}",
                };
                _notes.Text = entry == null || entry.Notes.Length == 0 ? "" : "Not expressed by the generated script: " + string.Join("; ", entry.Notes);
                _revert.Text = mine ? "Delete" : "Revert";
                _revert.ToolTipText = mine ? "Remove this entry of yours" : "Put the generated script back";
                _revert.Enabled = entry != null && entry.Origin != Origin.Game;
                _use.Enabled = _copy.Enabled = _check.Enabled = entry != null;
            }
            finally
            {
                _binding = false;
            }
        }

        // ---- edits ----

        private void Edited(Action<Entry> change, bool mineOnly = false)
        {
            if (_binding || Current is not { } entry || (mineOnly && entry.Origin != Origin.Mine))
                return;
            change(entry);
            if (entry.Origin == Origin.Game)
                entry.Origin = Origin.Edited;
            else if (entry.Origin == Origin.Edited && entry.Script == entry.GameScript)
                entry.Origin = Origin.Game;
            _unsaved.Add(entry);
            _revert.Enabled = entry.Origin != Origin.Game;
            // its name and mark, wherever it is in the tree
            string text = entry.ToString();
            foreach (var node in _tree.Nodes.Find(entry.GetHashCode().ToString(), true).Where(n => n.Tag == entry))
                node.Text = text;
            _status.Text = "Changed: Save to my library (Ctrl+S) keeps it.";
        }

        private void NewEntry()
        {
            var entry = new Entry { Name = "My effect", Kind = "Spell", Text = "What the effect does, in words.", Script = Current?.Script ?? "draw(1);", Complete = true, Origin = Origin.Mine };
            entry.Action = ActionOf(entry.Script);
            _all.Insert(0, entry);
            _unsaved.Add(entry);
            _filter.Text = "";
            FillCategories();
            Refill(entry);
            _name.Focus();
            _name.SelectAll();
            _status.Text = "A new entry of your own (starting from the script that was shown). Name it, write the script, then Save to my library.";
        }

        private void RevertOrDelete()
        {
            if (Current is not { } entry)
                return;
            if (entry.Origin == Origin.Mine)
            {
                if (MessageBox.Show(this, $"Delete \"{entry.Name}\" from your library?", "Effect library", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
                var next = _tree.SelectedNode?.NextNode?.Tag as Entry;
                _all.Remove(entry);
                FillCategories();   // its action may have been the only one of its kind
                Refill(next);
            }
            else
            {
                entry.Script = entry.GameScript;
                entry.Origin = Origin.Game;
                Refill(entry);
                Show(entry);
            }
            SaveLibrary();
        }

        private void Check()
        {
            if (Current is not { } entry)
                return;
            var result = EffectScriptCompiler.Compile(entry.Script);
            if (result.Ok)
            {
                _status.Text = "OK: the game can run this script.";
                _status.ForeColor = Color.DarkGreen;
            }
            else
            {
                _script.ShowError(result.Error);
                _status.Text = "Not runnable yet: " + result.Error;
                _status.ForeColor = Color.Firebrick;
            }
        }

        private void Use()
        {
            if (Current is not { } entry)
                return;
            bool ok = UseTemplate?.Invoke(entry.Script) ?? false;
            _status.ForeColor = SystemColors.ControlText;
            _status.Text = ok ? "Put into the Effects page." : "Pick a card on the Effects page first.";
        }
    }
}
