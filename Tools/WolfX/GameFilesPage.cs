using System.Runtime.InteropServices;
using Wolf.Editors;

namespace WolfX
{
    /// <summary>
    /// Where the game's files come from, and the settings for it (Yu-Gi-Oh-Core Patch.h / Loading.h, in the game's Config.ini [Yu-Gi-Oh-Core]):
    /// which copy wins (FileOrder: loose files or WolfX's patch), loose loading and its folder, the patch archive's name; and every file that
    /// isn't the game's own: the ones WolfX's patch changes and the loose ones, with the copy the game loads. WolfX reads by the same rules.
    /// </summary>
    internal sealed class GameFilesPage : UserControl, IGameEditor
    {
        private readonly RadioButton _looseFirst = new() { Text = "Loose files win (a file in the loose folder beats WolfX's patch)", AutoSize = true };
        private readonly RadioButton _patchFirst = new() { Text = "WolfX's patch wins (your WolfX edits beat loose files)", AutoSize = true };
        private readonly CheckBox _loose = new() { Text = "Load loose files from the folder", AutoSize = true, Margin = new Padding(3, 6, 3, 3) };
        // Typed or picked: the folders in the game folder / the archives (.toc with its .dat) next to the game's own
        private readonly ComboBox _looseFolder = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly ComboBox _patchName = new() { Width = 160, DropDownStyle = ComboBoxStyle.DropDown };
        private readonly Button _apply = new() { Text = "Apply", AutoSize = true };
        private readonly ListView _files = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true };
        private readonly Label _summary = new() { Dock = DockStyle.Top, Height = 40, Padding = new Padding(4, 8, 4, 0), ForeColor = SystemColors.GrayText };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private GameFolderFiles? _gameFiles;

        public GameFilesPage()
        {
            var settings = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
            static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 7, 12, 3) };
            var order = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            order.Controls.AddRange([_looseFirst, _patchFirst]);
            var looseRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            looseRow.Controls.AddRange([_loose, _looseFolder, Note("in the game folder (FolderName)")]);
            var patchRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            patchRow.Controls.AddRange([_patchName, Note(".toc / .dat next to YGO_2020.dat (PatchArchive; empty = no patch)")]);
            settings.Controls.Add(Caption("When both have a file:"), 0, 0);
            settings.Controls.Add(order, 1, 0);
            settings.Controls.Add(Caption("Loose files:"), 0, 1);
            settings.Controls.Add(looseRow, 1, 1);
            settings.Controls.Add(Caption("WolfX's patch:"), 0, 2);
            settings.Controls.Add(patchRow, 1, 2);
            var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            buttons.Controls.Add(_apply);
            buttons.Controls.Add(Note("Saved in the game's Config.ini [Yu-Gi-Oh-Core]; Yu-Gi-Oh-Core (always on) reads it when the game starts."));
            settings.Controls.Add(buttons, 1, 3);

            var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
            tools.Items.Add(Button("Refresh", "Read the folders and the patch again", () => { if (_gameFiles != null) Open(_gameFiles); }));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Button("Remove WolfX's patch...", "Delete the patch archive: the game uses its own data (and your loose files) again", RemovePatch));
            tools.Items.Add(Button("Open the loose folder", "Show the loose folder in Explorer", () => ShowFolder(_gameFiles?.OverrideFolder)));
            tools.Items.Add(Button("Open the game folder", "Show the game folder in Explorer", () => ShowFolder(_gameFiles?.GameFolder)));
            tools.Items.Add(new ToolStripSeparator());
            tools.Items.Add(Button("All plugin settings...", "Every Config.ini setting of every plugin (the same list as the game's Plugins > Plugin Settings)", () =>
            {
                using var config = new global::WolfX.WolfX.File_Type_UI.Config();
                config.ShowDialog(this);
            }));

            _files.Columns.Add("File", 320);
            _files.Columns.Add("The game loads", 160);
            _files.Columns.Add("Also in", 200);
            _apply.Click += (_, _) => Apply();

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(_files);
            Controls.Add(_summary);
            Controls.Add(settings);
            Controls.Add(tools);
            Controls.Add(status);
        }

        private static Label Note(string text) => new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(8, 7, 3, 3) };

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode)]
        private static extern bool WritePrivateProfileString(string section, string key, string? value, string file);

        // ---- IGameEditor ----

        public bool Dirty => false;

        public IReadOnlyCollection<string> Files => [];

        public string SavesTo => "Settings in the game's Config.ini [Yu-Gi-Oh-Core]: where the game's files come from (loose files, WolfX's patch, the game's archive).";

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            bool game = !files.IsExtracted;
            foreach (Control control in new Control[] { _looseFirst, _patchFirst, _loose, _looseFolder, _patchName, _apply })
                control.Enabled = game;
            _looseFirst.Checked = !files.PatchFirst;
            _patchFirst.Checked = files.PatchFirst;
            _loose.Checked = files.LooseLoading;
            _looseFolder.Items.Clear();
            _patchName.Items.Clear();
            if (!files.IsExtracted && Directory.Exists(files.GameFolder))
            {
                foreach (string folder in Directory.EnumerateDirectories(files.GameFolder).Select(d => Path.GetFileName(d)).Order(StringComparer.OrdinalIgnoreCase))
                    _looseFolder.Items.Add(folder);
                _patchName.Items.Add("");   // off
                foreach (string toc in Directory.EnumerateFiles(files.GameFolder, "*.toc").Where(t => File.Exists(Path.ChangeExtension(t, ".dat"))))
                {
                    string name = Path.GetFileNameWithoutExtension(toc);
                    if (!name.Equals(Path.GetFileNameWithoutExtension(files.ArchiveName), StringComparison.OrdinalIgnoreCase))
                        _patchName.Items.Add(name);
                }
            }
            _looseFolder.Text = files.IsExtracted ? "" : Path.GetFileName(files.OverrideFolder);
            _patchName.Text = files.IsExtracted ? "" : files.PatchName;
            Fill(files);
        }

        public bool Save() => true;

        // ---- the files ----

        private void Fill(GameFolderFiles files)
        {
            _files.BeginUpdate();
            _files.Items.Clear();
            if (files.IsExtracted)
            {
                _summary.Text = $"An extracted folder is all loose files ({files.Folder}): there is no archive or patch to choose between.";
                _files.EndUpdate();
                return;
            }
            var patched = new HashSet<string>(files.PatchedFiles, StringComparer.OrdinalIgnoreCase);
            var loose = Directory.Exists(files.OverrideFolder)
                ? Directory.EnumerateFiles(files.OverrideFolder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(files.OverrideFolder, f)).ToList()
                : [];
            foreach (string path in patched.Union(loose, StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                string source = files.SourceOf(path) switch
                {
                    "loose" => "the loose file",
                    "patch" => "WolfX's patch",
                    "archive" => "YGO_2020.dat (loose loading is off)",
                    _ => "-",
                };
                var also = new List<string>();
                if (patched.Contains(path) && files.SourceOf(path) != "patch")
                    also.Add("WolfX's patch");
                if (loose.Contains(path, StringComparer.OrdinalIgnoreCase) && files.SourceOf(path) != "loose")
                    also.Add("loose folder");
                _files.Items.Add(new ListViewItem([path, source, string.Join(", ", also)])
                {
                    ForeColor = files.SourceOf(path) == "archive" ? SystemColors.GrayText : SystemColors.WindowText,
                });
            }
            _files.EndUpdate();
            _summary.Text = $"WolfX's patch ({files.PatchName}) changes {patched.Count} file(s); the loose folder ({Path.GetFileName(files.OverrideFolder)}) has {loose.Count}" +
                            (files.LooseLoading ? "" : " (loose loading is off, so the game ignores them)") + ". Everything else comes from YGO_2020.dat.";
        }

        private void Apply()
        {
            if (_gameFiles is not { IsExtracted: false } files)
                return;
            string ini = Path.Combine(files.GameFolder, "Config.ini");
            WritePrivateProfileString("Yu-Gi-Oh-Core", "FileOrder", _patchFirst.Checked ? "patch" : "loose", ini);
            WritePrivateProfileString("Yu-Gi-Oh-Core", "LooseLoading", _loose.Checked ? "1" : "0", ini);
            WritePrivateProfileString("Yu-Gi-Oh-Core", "FolderName", _looseFolder.Text.Trim().Length > 0 ? _looseFolder.Text.Trim() : "YGO_2020", ini);
            WritePrivateProfileString("Yu-Gi-Oh-Core", "PatchArchive", _patchName.Text.Trim(), ini);
            files.ReloadSettings();
            GameFolderFiles.SetCurrent(files);   // every page reads again by the new rules
            _status.Text = "Saved. The game uses it from its next start; WolfX already does.";
        }

        private void RemovePatch()
        {
            if (_gameFiles is not { HasChanges: true } files)
            {
                _status.Text = "There is no patch to remove.";
                return;
            }
            if (MessageBox.Show(this, $"Delete {files.PatchName}.dat / .toc ({files.PatchedFiles.Count} changed files), so the game uses its own data again?\n\n" +
                                      "Your Yu-Gi-Oh-Ex JSON and your loose files are not touched.", "Remove WolfX's patch", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
                return;
            try
            {
                files.RestoreOriginal();
                GameFolderFiles.SetCurrent(files);
                _status.Text = "The patch is gone.";
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not removed: " + ex.Message;
            }
        }

        private static void ShowFolder(string? folder)
        {
            if (folder != null && Directory.Exists(folder))
                System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
        }
    }
}
