using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WolfX.WolfX.File_Type_UI
{
    /// <summary>Edits the game's Config.ini: one row per setting any of the plugins reads, with a hint for the selected one.</summary>
    public partial class Config : Form
    {
        private string _path = "";
        private bool _loading;

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern uint GetPrivateProfileString(string section, string key, string def, StringBuilder result, uint size, string file);

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern bool WritePrivateProfileString(string section, string key, string? value, string file);

        private const string Absent = "\u0001<absent>\u0001";

        private readonly Button _pick = new() { Text = "Pick...", Anchor = AnchorStyles.Top | AnchorStyles.Right, Size = new Size(90, 25), Enabled = false };

        public Config()
        {
            InitializeComponent();
            // Pick: browse for the value of a path, an archive or a folder (also a double-click on its value)
            _pick.Location = new Point(resetButton.Left - _pick.Width - 6, resetButton.Top);
            _pick.UseVisualStyleBackColor = true;
            _pick.Click += (_, _) => Pick();
            resetButton.Parent!.Controls.Add(_pick);
            new ToolTip().SetToolTip(_pick, "Browse for this setting's file, folder or archive");
            grid.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex >= 0 && e.ColumnIndex == colValue.Index && grid.Rows[e.RowIndex].Tag is ConfigSetting s && CanPick(s))
                    Pick();
            };
        }

        private static bool CanPick(ConfigSetting s) => s.Kind == SettingKind.Path || s.From != null;

        private string GameFolder => _path.Length > 0 ? Path.GetDirectoryName(_path) ?? "" : "";

        /// <summary>A path inside the game folder is kept relative to it (the plugins resolve from there); one outside it stays whole.</summary>
        private string Relative(string path)
        {
            if (GameFolder.Length == 0)
                return path;
            string relative = Path.GetRelativePath(GameFolder, path);
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative) ? path : relative;
        }

        private void Pick()
        {
            if (grid.CurrentRow?.Tag is not ConfigSetting s || !CanPick(s))
                return;
            var cell = grid.CurrentRow.Cells[colValue.Index];
            string current = cell.Value?.ToString() ?? "";
            string start = current.Length > 0 && GameFolder.Length > 0 ? Path.Combine(GameFolder, current) : GameFolder;
            string? picked = null;
            string from = s.From ?? "";
            bool folder = from.Equals("folders", StringComparison.OrdinalIgnoreCase)
                          || (from.Length == 0 && (s.Key.EndsWith("Dir", StringComparison.OrdinalIgnoreCase) || s.Key.EndsWith("Path", StringComparison.OrdinalIgnoreCase)
                                                   || s.Key.EndsWith("Folder", StringComparison.OrdinalIgnoreCase)));
            if (folder)
            {
                using var dialog = new FolderBrowserDialog { Description = $"[{s.Section}] {s.Key}", UseDescriptionForTitle = true,
                                                             InitialDirectory = Directory.Exists(start) ? start : GameFolder };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    picked = from.Length > 0 ? Relative(dialog.SelectedPath) : dialog.SelectedPath;
                    if (s.Key.EndsWith("Path", StringComparison.OrdinalIgnoreCase) && !picked.EndsWith('\\'))
                        picked += "\\";   // PluginsPath must end with a backslash
                }
            }
            else
            {
                bool archive = from.Equals("archives", StringComparison.OrdinalIgnoreCase);
                string filter = "All files (*.*)|*.*";
                string initial = GameFolder;
                if (archive)
                    filter = "Game archives (*.toc)|*.toc";
                else if (from.StartsWith("files:", StringComparison.OrdinalIgnoreCase))
                {
                    string pattern = from[6..];
                    filter = $"{Path.GetFileName(pattern)}|{Path.GetFileName(pattern)}|All files (*.*)|*.*";
                    initial = Path.Combine(GameFolder, Path.GetDirectoryName(pattern) ?? "");
                }
                else if (s.Help.Contains(".ydc", StringComparison.OrdinalIgnoreCase))
                    filter = "Decks (*.ydc)|*.ydc|All files (*.*)|*.*";
                using var dialog = new OpenFileDialog { Title = $"[{s.Section}] {s.Key}", Filter = filter,
                                                        InitialDirectory = File.Exists(start) ? Path.GetDirectoryName(start) : Directory.Exists(initial) ? initial : GameFolder };
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    picked = archive ? Path.GetFileNameWithoutExtension(dialog.FileName) : Relative(dialog.FileName);
            }
            if (picked != null)
                cell.Value = picked;
        }

        /// <summary>Where the game's Config.ini may be, best first: the game folder WolfX has open, the Steam install, then the folders above WolfX.</summary>
        private static IEnumerable<string> GameFolders()
        {
            if (Wolf.Editors.GameFolderFiles.Current is { } open && open.GameFolder.Length > 0)
                yield return open.GameFolder;
            if (global::WolfX.GameLocator.FindSteamInstall() is { } steam)
                yield return steam;
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
                yield return dir.FullName;
        }

        private void Config_Load(object sender, EventArgs e)
        {
            var folders = GameFolders().ToList();
            var existing = folders.Select(f => Path.Combine(f, "Config.ini")).FirstOrDefault(File.Exists);
            if (existing != null) { LoadFile(existing); return; }
            // a game folder the loader hasn't run in yet: open its Config.ini anyway, Save creates it
            var game = folders.FirstOrDefault(f => File.Exists(Path.Combine(f, "YuGiOh.exe")));
            LoadFile(game != null ? Path.Combine(game, "Config.ini") : "");
        }

        private static string Read(string file, ConfigSetting setting)
        {
            var sb = new StringBuilder(2048);
            GetPrivateProfileString(setting.Section, setting.Key, Absent, sb, (uint)sb.Capacity, file);
            return sb.ToString();
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern uint GetPrivateProfileSection(string section, char[] result, uint size, string file);

        /// <summary>The plugin list in the file: the loader writes one Name=0/1 line per plugin, plus YGO-Ex/Name for the GUI's.</summary>
        private static List<ConfigSetting> PluginRows(string file, IReadOnlyList<ConfigSetting> settings)
        {
            var rows = new List<ConfigSetting>();
            if (file.Length == 0) return rows;

            var buffer = new char[32 * 1024];
            uint length = GetPrivateProfileSection(ConfigCatalog.PluginSection, buffer, (uint)buffer.Length, file);
            foreach (var line in new string(buffer, 0, (int)length).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = line.IndexOf('=');
                if (equals > 0 && !ConfigCatalog.IsPluginListSetting(line[..equals], settings)) rows.Add(ConfigCatalog.ForPlugin(line[..equals]));
            }
            return rows.OrderBy(r => r.Key.StartsWith(ConfigCatalog.GuiPluginPrefix, StringComparison.OrdinalIgnoreCase)).ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void LoadFile(string file)
        {
            _path = file;
            _loading = true;
            grid.Rows.Clear();
            pathBox.Text = file.Length == 0 ? "No Config.ini found - use Browse" : file;
            statusLabel.Text = file.Length > 0 && !File.Exists(file) ? "The game folder has no Config.ini yet: Save creates it." : "";

            // Core's section holds its settings and the plugin list: every key that isn't a known setting is a plugin
            var settings = ConfigCatalog.WithManifests(file);
            var pluginRows = PluginRows(file, settings);
            _pluginRows.Clear();
            _pluginRows.UnionWith(pluginRows);
            // grouped by plugin, each plugin's on/off line first, then its settings in catalog order
            var rows = settings.Concat(pluginRows).OrderBy(Owner, StringComparer.OrdinalIgnoreCase).ThenBy(s => IsPluginRow(s) ? 0 : 1);
            foreach (var setting in rows)
            {
                var raw = file.Length == 0 ? Absent : Read(file, setting);
                bool present = raw != Absent;
                var value = present ? raw : setting.Default;

                int index = grid.Rows.Add();
                var row = grid.Rows[index];
                row.Tag = setting;
                row.Cells[colSection.Index].Value = setting.Section;
                row.Cells[colSetting.Index].Value = IsPluginRow(setting) ? "Load this plugin" : setting.Key;
                row.Cells[colDefault.Index].Value = setting.Default;
                row.Cells[colInFile.Index].Value = present ? "yes" : "";

                switch (setting.Kind)
                {
                    case SettingKind.Toggle:
                        row.Cells[colValue.Index] = new DataGridViewCheckBoxCell { Value = value.Trim() == "1" };
                        break;
                    case SettingKind.Choice:
                        var combo = new DataGridViewComboBoxCell { FlatStyle = FlatStyle.Flat };
                        combo.Items.AddRange(setting.Choices ?? []);
                        if (!combo.Items.Contains(value)) combo.Items.Add(value);
                        combo.Value = value;
                        row.Cells[colValue.Index] = combo;
                        break;
                    default:
                        row.Cells[colValue.Index].Value = value;
                        break;
                }
                if (Current(row) != setting.Default)
                    row.DefaultCellStyle.Font = _bold;
            }

            _loading = false;
            saveButton.Enabled = file.Length > 0;
            reloadButton.Enabled = file.Length > 0;
            BuildTree();
            ApplyFilter();
        }

        // ---- the plugin list on the left: picks which rows the grid shows ----

        /// <summary>A plugin in the list: its name (without the count) and its on/off line in the plugin list, when the file has one.</summary>
        private sealed record Group(string Name, DataGridViewRow? LoadRow);

        private readonly HashSet<ConfigSetting> _pluginRows = [];
        private Font? _boldFont;
        private Font _bold => _boldFont ??= new Font(grid.Font, FontStyle.Bold);

        private bool IsPluginRow(ConfigSetting s) => _pluginRows.Contains(s);

        /// <summary>The plugin a row belongs to: its section, or for a plugin-list line (Name=0/1, YGO-Ex/Name=0/1) the plugin it loads.</summary>
        private string Owner(ConfigSetting s) =>
            !IsPluginRow(s) ? s.Section
            : s.Key.StartsWith(ConfigCatalog.GuiPluginPrefix, StringComparison.OrdinalIgnoreCase) ? s.Key[ConfigCatalog.GuiPluginPrefix.Length..] : s.Key;



        private void BuildTree()
        {
            string? selected = sectionTree.SelectedNode?.Name;
            sectionTree.BeginUpdate();
            sectionTree.Nodes.Clear();

            var rows = grid.Rows.Cast<DataGridViewRow>().ToList();
            foreach (string plugin in rows.Select(r => Owner((ConfigSetting)r.Tag!)).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
            {
                var loadRow = rows.FirstOrDefault(r => r.Tag is ConfigSetting s && IsPluginRow(s) && Owner(s).Equals(plugin, StringComparison.OrdinalIgnoreCase));
                sectionTree.Nodes.Add(plugin, plugin).Tag = new Group(plugin, loadRow);
            }

            sectionTree.ShowNodeToolTips = true;
            var found = selected != null ? sectionTree.Nodes.Find(selected, false) : [];
            _building = true;
            sectionTree.SelectedNode = found.Length > 0 ? found[0] : sectionTree.Nodes.Count > 0 ? sectionTree.Nodes[0] : null;
            _building = false;
            sectionTree.EndUpdate();
        }

        private bool _building;

        private bool MatchesText(ConfigSetting s, string text) =>
            text.Length == 0
            || s.Key.Contains(text, StringComparison.OrdinalIgnoreCase)
            || Owner(s).Contains(text, StringComparison.OrdinalIgnoreCase)
            || s.Help.Contains(text, StringComparison.OrdinalIgnoreCase);

        /// <summary>Puts how many rows each node shows (with the filter) after its name; nodes with none go grey while filtering.</summary>
        private void UpdateCounts()
        {
            var text = filterBox.Text.Trim();
            var owners = grid.Rows.Cast<DataGridViewRow>().Select(r => (ConfigSetting)r.Tag!).Where(s => MatchesText(s, text)).Select(Owner).ToList();
            sectionTree.BeginUpdate();
            foreach (TreeNode node in sectionTree.Nodes)
            {
                var group = (Group)node.Tag!;
                int count = owners.Count(o => o.Equals(group.Name, StringComparison.OrdinalIgnoreCase));
                string label = $"{group.Name}  ({count})";
                if (node.Text != label) node.Text = label;
                // a plugin switched off in the plugin list: its settings do nothing until it is loaded
                bool off = group.LoadRow != null && Current(group.LoadRow) == "0";
                node.ToolTipText = off ? "This plugin is off: tick \"Load this plugin\" for its settings to apply." : "";
                node.ForeColor = off || (text.Length > 0 && count == 0) ? SystemColors.GrayText : SystemColors.WindowText;
            }
            sectionTree.EndUpdate();
        }

        /// <summary>The number in a node's label: how many of its rows match the filter.</summary>
        private static int CountOf(TreeNode node) =>
            int.TryParse(System.Text.RegularExpressions.Regex.Match(node.Text, @"\((\d+)\)$").Groups[1].Value, out int n) ? n : 0;

        private void sectionTree_AfterSelect(object? sender, TreeViewEventArgs e)
        {
            if (!_loading && !_building) ApplyFilter();
        }

        private string Current(DataGridViewRow row)
        {
            var value = row.Cells[colValue.Index].Value;
            return value is bool b ? (b ? "1" : "0") : value?.ToString() ?? "";
        }

        private void ShowHelp()
        {
            if (grid.CurrentRow?.Tag is ConfigSetting s)
            {
                helpTitle.Text = $"[{s.Section}]  {s.Key}";
                helpText.Text = s.Help + (s.Default.Length > 0 ? $"\r\n\r\nDefault: {s.Default}" : "\r\n\r\nDefault: (empty)");
            }
            else
            {
                helpTitle.Text = "";
                helpText.Text = "Select a setting to see what it does.";
            }
        }

        private void ApplyFilter()
        {
            var text = filterBox.Text.Trim();
            UpdateCounts();
            // filtering: when the picked plugin has no match, jump to the first one that has
            if (text.Length > 0 && sectionTree.SelectedNode is { } picked && CountOf(picked) == 0
                && sectionTree.Nodes.Cast<TreeNode>().FirstOrDefault(n => CountOf(n) > 0) is { } first)
            {
                _building = true;
                sectionTree.SelectedNode = first;
                _building = false;
            }
            var group = sectionTree.SelectedNode?.Tag as Group;
            grid.CurrentCell = null;
            grid.SuspendLayout();
            foreach (DataGridViewRow row in grid.Rows)
            {
                var s = (ConfigSetting)row.Tag!;
                row.Visible = group != null && Owner(s).Equals(group.Name, StringComparison.OrdinalIgnoreCase) && MatchesText(s, text);
            }
            grid.ResumeLayout();
            ShowHelp();
        }

        private void grid_SelectionChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            ShowHelp();
            _pick.Enabled = grid.CurrentRow?.Tag is ConfigSetting s && CanPick(s);
        }

        private void grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (_loading || e.RowIndex < 0 || e.ColumnIndex != colValue.Index) return;
            var row = grid.Rows[e.RowIndex];
            var s = (ConfigSetting)row.Tag!;
            row.DefaultCellStyle.Font = Current(row) == s.Default ? null : _bold;
            // counts and the greyed-out plugins follow the edit; rows stay where they are until the list or the filter changes
            UpdateCounts();
        }

        private void grid_DataError(object sender, DataGridViewDataErrorEventArgs e) => e.ThrowException = false;

        private void filterBox_TextChanged(object sender, EventArgs e) => ApplyFilter();

        private void browseButton_Click(object sender, EventArgs e)
        {
            using var open = new OpenFileDialog { Filter = "Config Files (*.ini)|*.ini", Title = "Select Config File",
                                                  InitialDirectory = Directory.Exists(GameFolder) ? GameFolder : "" };
            if (open.ShowDialog() == DialogResult.OK) LoadFile(open.FileName);
        }

        private void reloadButton_Click(object sender, EventArgs e) => LoadFile(_path);

        private void resetButton_Click(object sender, EventArgs e)
        {
            if (grid.CurrentRow?.Tag is not ConfigSetting s) return;
            var cell = grid.CurrentRow.Cells[colValue.Index];
            cell.Value = s.Kind == SettingKind.Toggle ? s.Default == "1" : s.Default;
        }

        private void saveButton_Click(object sender, EventArgs e)
        {
            int written = 0;
            foreach (DataGridViewRow row in grid.Rows)
            {
                var s = (ConfigSetting)row.Tag!;
                var value = Current(row);
                bool inFile = row.Cells[colInFile.Index].Value?.ToString() == "yes";
                if (!inFile && value == s.Default) continue;   // leave the file alone for untouched defaults
                WritePrivateProfileString(s.Section, s.Key, value, _path);
                written++;
            }
            LoadFile(_path);
            statusLabel.Text = $"Saved {written} setting(s).";
        }
    }
}
