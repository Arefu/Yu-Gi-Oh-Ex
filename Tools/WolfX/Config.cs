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

        public Config()
        {
            InitializeComponent();
        }

        private void Config_Load(object sender, EventArgs e)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "Config.ini");
                if (File.Exists(candidate)) { LoadFile(candidate); return; }
            }
            LoadFile("");
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
        private static List<ConfigSetting> PluginRows(string file)
        {
            var rows = new List<ConfigSetting>();
            if (file.Length == 0) return rows;

            var buffer = new char[32 * 1024];
            uint length = GetPrivateProfileSection(ConfigCatalog.PluginSection, buffer, (uint)buffer.Length, file);
            foreach (var line in new string(buffer, 0, (int)length).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                int equals = line.IndexOf('=');
                if (equals > 0 && !ConfigCatalog.IsPluginListSetting(line[..equals])) rows.Add(ConfigCatalog.ForPlugin(line[..equals]));
            }
            return rows.OrderBy(r => r.Key.StartsWith(ConfigCatalog.GuiPluginPrefix, StringComparison.OrdinalIgnoreCase)).ThenBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void LoadFile(string file)
        {
            _path = file;
            _loading = true;
            grid.Rows.Clear();
            pathBox.Text = file.Length == 0 ? "No Config.ini open - use Browse" : file;

            foreach (var setting in ConfigCatalog.All.Concat(PluginRows(file)))
            {
                var raw = file.Length == 0 ? Absent : Read(file, setting);
                bool present = raw != Absent;
                var value = present ? raw : setting.Default;

                int index = grid.Rows.Add();
                var row = grid.Rows[index];
                row.Tag = setting;
                row.Cells[colSection.Index].Value = setting.Section;
                row.Cells[colSetting.Index].Value = setting.Key;
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
            }

            _loading = false;
            saveButton.Enabled = file.Length > 0;
            reloadButton.Enabled = file.Length > 0;
            ApplyFilter();
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
            grid.CurrentCell = null;
            foreach (DataGridViewRow row in grid.Rows)
            {
                var s = (ConfigSetting)row.Tag!;
                row.Visible = text.Length == 0
                    || s.Key.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || s.Section.Contains(text, StringComparison.OrdinalIgnoreCase)
                    || s.Help.Contains(text, StringComparison.OrdinalIgnoreCase);
            }
            ShowHelp();
        }

        private void grid_SelectionChanged(object sender, EventArgs e)
        {
            if (!_loading) ShowHelp();
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
            row.DefaultCellStyle.Font = Current(row) == s.Default ? null : new Font(grid.Font, FontStyle.Bold);
        }

        private void grid_DataError(object sender, DataGridViewDataErrorEventArgs e) => e.ThrowException = false;

        private void filterBox_TextChanged(object sender, EventArgs e) => ApplyFilter();

        private void browseButton_Click(object sender, EventArgs e)
        {
            using var open = new OpenFileDialog { Filter = "Config Files (*.ini)|*.ini", Title = "Select Config File" };
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
