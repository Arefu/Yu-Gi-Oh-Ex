using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wolf.Mods;

namespace ModManager
{
    /// <summary>
    /// The mod list (ticked = on; top loads first, the bottom wins), what the selected mod is and needs, and what clashes between the mods
    /// that are on. Every change is saved to &lt;game&gt;\Mods\modlist.json straight away and applies the next time the game starts.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private string _game = "";
        private string? _plugins;
        private List<Mod> _mods = [];
        private Mod? _local;
        private List<ModIssue> _issues = [];
        private bool _filling;

        private readonly Label _gameLabel = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, CheckBoxes = true, FullRowSelect = true, HideSelection = false, MultiSelect = false,
            AllowDrop = true,
        };
        private readonly FlowLayoutPanel _details = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(8) };
        private readonly ListView _problems = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        private readonly Button _up = Make("Move up"), _down = Make("Move down"), _install = Make("Install..."), _remove = Make("Remove"),
            _open = Make("Open folder"), _export = Make("Export .zip..."), _create = Make("Create mod..."), _refresh = Make("Refresh"), _play = Make("Play");

        private static Button Make(string text) => new() { Text = text, Width = 120, Height = 30, Margin = new Padding(0, 0, 0, 6) };

        private static string SettingsFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Yu-Gi-Oh-Ex", "ModManager.json");

        public MainForm(string? game, bool create, List<string> zips)
        {
            Text = "Yu-Gi-Oh-Ex Mod Manager";
            Size = new Size(1100, 720);
            MinimumSize = new Size(860, 520);
            StartPosition = FormStartPosition.CenterScreen;
            Font = SystemFonts.MessageBoxFont ?? Font;
            AllowDrop = true;

            _list.Columns.Add("Mod", 260);
            _list.Columns.Add("Version", 70);
            _list.Columns.Add("Author", 120);
            _list.Columns.Add("Changes", 300);
            _list.Columns.Add("Status", 140);
            _problems.Columns.Add("", 70);
            _problems.Columns.Add("Mod", 200);
            _problems.Columns.Add("What", 760);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 8, 8, 0) };
            var change = new Button { Text = "Change game folder...", AutoSize = true };
            change.Click += (_, _) => { if (PickGame()) Reload(); };
            top.Controls.AddRange([change, _gameLabel]);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, FlowDirection = FlowDirection.TopDown, Width = 136, Padding = new Padding(8, 0, 8, 0) };
            buttons.Controls.AddRange([_up, _down, _install, _remove, _open, _export, _create, _refresh, _play]);

            var listArea = new Panel { Dock = DockStyle.Fill };
            listArea.Controls.Add(_list);
            listArea.Controls.Add(buttons);
            var order = new Label
            {
                Dock = DockStyle.Bottom, Height = 22, Padding = new Padding(4, 4, 0, 0), ForeColor = SystemColors.GrayText,
                Text = "Ticked mods load from the top down: a mod lower in the list wins when two change the same thing. Your own files always load last.",
            };
            listArea.Controls.Add(order);

            var upper = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 720 };
            upper.Panel1.Controls.Add(listArea);
            upper.Panel2.Controls.Add(_details);
            var main = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 440 };
            main.Panel1.Controls.Add(upper);
            var problemsBox = new GroupBox { Text = "Things to know about the mods that are on", Dock = DockStyle.Fill, Padding = new Padding(6) };
            problemsBox.Controls.Add(_problems);
            main.Panel2.Controls.Add(problemsBox);

            var strip = new StatusStrip();
            strip.Items.Add(_status);

            Controls.Add(main);
            Controls.Add(top);
            Controls.Add(strip);

            _list.ItemCheck += List_ItemCheck;
            _list.ItemChecked += (_, _) => { if (!_filling) SaveAndCheck(); };
            _list.SelectedIndexChanged += (_, _) => { ShowDetails(); UpdateButtons(); };
            _problems.DoubleClick += Problems_DoubleClick;
            _up.Click += (_, _) => MoveSelected(-1);
            _down.Click += (_, _) => MoveSelected(1);
            _install.Click += (_, _) => PickAndInstall();
            _remove.Click += (_, _) => RemoveSelected();
            _open.Click += (_, _) => { if (Selected() is Mod mod) Open(mod.IsLocal ? Path.Combine(mod.Folder, "Yu-Gi-Oh-Ex") : mod.Folder); };
            _export.Click += (_, _) => ExportSelected();
            _create.Click += (_, _) => CreateMod();
            _refresh.Click += (_, _) => Reload();
            _play.Click += (_, _) => Play();
            DragEnter += Drag_Enter;
            DragDrop += Drag_Drop;
            _list.DragEnter += Drag_Enter;
            _list.DragDrop += Drag_Drop;

            Shown += (_, _) =>
            {
                if (!FindGame(game))
                {
                    Close();
                    return;
                }
                Reload();
                foreach (string zip in zips)
                    Install(zip);
                if (create)
                    CreateMod();
            };
        }

        // ---- the game folder ----

        private static bool IsGameFolder(string folder) => File.Exists(Path.Combine(folder, "YGO_2020.toc"));

        private bool FindGame(string? given)
        {
            if (given != null && IsGameFolder(given))
                return UseGame(given);
            try
            {
                if (File.Exists(SettingsFile) && JsonNode.Parse(File.ReadAllText(SettingsFile))?["game"]?.GetValue<string>() is string saved && IsGameFolder(saved))
                    return UseGame(saved);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException)
            {
                // ask again
            }
            if (WolfX.GameLocator.FindSteamInstall() is string steam)
                return UseGame(steam);
            return PickGame();
        }

        private bool PickGame()
        {
            using var dialog = new FolderBrowserDialog { Description = "The game's folder (the one with YuGiOh.exe and YGO_2020.toc)" };
            while (dialog.ShowDialog(this) == DialogResult.OK)
            {
                if (IsGameFolder(dialog.SelectedPath))
                    return UseGame(dialog.SelectedPath);
                MessageBox.Show(this, "That folder has no YGO_2020.toc; pick the game's folder.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            return _game.Length > 0;
        }

        private bool UseGame(string folder)
        {
            _game = folder;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
                File.WriteAllText(SettingsFile, new JsonObject { ["game"] = folder }.ToJsonString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // only remembered for next time
            }
            return true;
        }

        // ---- the list ----

        private void Reload()
        {
            _plugins = ModLibrary.PluginsFolder(_game);
            _gameLabel.Text = $"Game: {_game}     Plugins: {_plugins ?? "not found (start the game once with Yu-Gi-Oh_Loader)"}";
            _mods = ModLibrary.Load(_game);
            _local = ModLibrary.Local(_game);
            Fill(null);
        }

        private IReadOnlyList<Mod> Ordered() => [.. _mods, _local!];

        private void Fill(Mod? select)
        {
            _filling = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (Mod mod in Ordered())
            {
                var item = new ListViewItem(mod.Info.Name) { Tag = mod, Checked = mod.Enabled || mod.IsLocal };
                item.SubItems.Add(mod.Info.Version);
                item.SubItems.Add(mod.Info.Author);
                item.SubItems.Add(mod.Summary());
                item.SubItems.Add("");
                if (mod.IsLocal)
                    item.ForeColor = SystemColors.GrayText;
                _list.Items.Add(item);
                if (mod == select)
                    item.Selected = true;
            }
            _list.EndUpdate();
            _filling = false;
            Check();
            if (_list.SelectedItems.Count == 0 && _list.Items.Count > 0)
                _list.Items[0].Selected = true;
            ShowDetails();
            UpdateButtons();
        }

        private Mod? Selected() => _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as Mod : null;

        private void List_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (!_filling && _list.Items[e.Index].Tag is Mod { IsLocal: true })
                e.NewValue = CheckState.Checked;   // your own files are always on (delete them in WolfX to take them out)
        }

        private void SaveAndCheck()
        {
            foreach (ListViewItem item in _list.Items)
                if (item.Tag is Mod { IsLocal: false } mod)
                    mod.Enabled = item.Checked;
            Save();
            Check();
            ShowDetails();
        }

        private void Save()
        {
            try
            {
                ModLibrary.SaveList(_game, _mods);
                _status.Text = ModLibrary.IsGameRunning() ? "Saved. The game is running: changes apply the next time it starts." : "Saved.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _status.Text = "The mod list couldn't be saved: " + ex.Message;
            }
        }

        private void MoveSelected(int step)
        {
            if (Selected() is not Mod { IsLocal: false } mod)
                return;
            int index = _mods.IndexOf(mod);
            int target = index + step;
            if (target < 0 || target >= _mods.Count)
                return;
            _mods.RemoveAt(index);
            _mods.Insert(target, mod);
            Save();
            Fill(mod);
        }

        private void UpdateButtons()
        {
            Mod? mod = Selected();
            bool real = mod is { IsLocal: false };
            int index = real ? _mods.IndexOf(mod!) : -1;
            _up.Enabled = real && index > 0;
            _down.Enabled = real && index < _mods.Count - 1;
            _remove.Enabled = real;
            _export.Enabled = real;
            _open.Enabled = mod != null;
            _play.Enabled = ModLibrary.LoaderExe(_plugins) != null;
        }

        // ---- checks ----

        private void Check()
        {
            var active = _mods.Where(m => m.Enabled).Append(_local!).ToList();
            _issues = ModLibrary.Check(active, _plugins);

            foreach (ListViewItem item in _list.Items)
            {
                if (item.Tag is not Mod mod)
                    continue;
                var mine = _issues.Where(i => i.ModId == mod.Id && (mod.Enabled || mod.IsLocal)).ToList();
                string status = !mod.Enabled && !mod.IsLocal ? "off"
                    : mine.Any(i => i.Level == IssueLevel.Error) ? "needs attention"
                    : mine.Any(i => i.Level == IssueLevel.Warning) ? "clashes"
                    : "OK";
                item.SubItems[4].Text = status;
                item.UseItemStyleForSubItems = false;
                item.SubItems[4].ForeColor = status switch
                {
                    "needs attention" => Color.Firebrick,
                    "clashes" => Color.DarkOrange,
                    "OK" => Color.SeaGreen,
                    _ => SystemColors.GrayText,
                };
            }

            _problems.BeginUpdate();
            _problems.Items.Clear();
            foreach (ModIssue issue in _issues.OrderByDescending(i => i.Level))
            {
                Mod? mod = Ordered().FirstOrDefault(m => m.Id == issue.ModId);
                var item = new ListViewItem(issue.Level switch { IssueLevel.Error => "Problem", IssueLevel.Warning => "Clash", _ => "Note" }) { Tag = issue };
                item.SubItems.Add(mod?.Info.Name ?? "");
                item.SubItems.Add(issue.Text + (issue.Url != null ? "  (double-click to open the link)" : ""));
                item.ForeColor = issue.Level switch { IssueLevel.Error => Color.Firebrick, IssueLevel.Warning => Color.DarkOrange, _ => SystemColors.WindowText };
                _problems.Items.Add(item);
            }
            if (_problems.Items.Count == 0)
                _problems.Items.Add(new ListViewItem(["", "", "Nothing: every mod that is on has what it needs, and none of them clash."]));
            _problems.EndUpdate();
        }

        private void Problems_DoubleClick(object? sender, EventArgs e)
        {
            if (_problems.SelectedItems.Count == 0 || _problems.SelectedItems[0].Tag is not ModIssue issue)
                return;
            if (issue.Url != null)
                OpenUrl(issue.Url);
            foreach (ListViewItem item in _list.Items)
                item.Selected = item.Tag is Mod mod && mod.Id == issue.ModId;
        }

        // ---- details ----

        private void ShowDetails()
        {
            _details.SuspendLayout();
            foreach (Control control in _details.Controls.Cast<Control>().ToList())
                control.Dispose();
            _details.Controls.Clear();
            if (Selected() is Mod mod)
            {
                int width = Math.Max(200, _details.ClientSize.Width - 30);
                Label Add(string text, bool bold = false, Color? color = null)
                {
                    var label = new Label
                    {
                        Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(0, 0, 0, 6),
                        Font = bold ? new Font(Font.FontFamily, Font.Size + 3, FontStyle.Bold) : Font,
                        ForeColor = color ?? SystemColors.ControlText,
                    };
                    _details.Controls.Add(label);
                    return label;
                }
                void Link(string text, string url)
                {
                    var link = new LinkLabel { Text = text, AutoSize = true, MaximumSize = new Size(width, 0), Margin = new Padding(12, 0, 0, 4) };
                    link.LinkClicked += (_, _) => OpenUrl(url);
                    _details.Controls.Add(link);
                }

                Add(mod.Info.Name, bold: true);
                string by = string.Join("  ·  ", new[] { mod.Info.Version.Length > 0 ? "version " + mod.Info.Version : "", mod.Info.Author.Length > 0 ? "by " + mod.Info.Author : "" }.Where(s => s.Length > 0));
                if (by.Length > 0)
                    Add(by, color: SystemColors.GrayText);
                if (mod.Info.Website.Length > 0)
                    Link(mod.Info.Website, mod.Info.Website);
                if (mod.Info.Description.Length > 0)
                    Add(mod.Info.Description);
                if (mod.Info.Details.Length > 0)
                    Add(mod.Info.Details);
                Add("Changes: " + mod.Summary());
                if (!mod.IsLocal)
                    Add("Folder: " + mod.Folder, color: SystemColors.GrayText);

                var needs = mod.NeededPlugins();
                var installed = ModLibrary.InstalledPlugins(_plugins);
                var bundled = mod.BundledPlugins();
                if (needs.Count > 0 || bundled.Count > 0)
                {
                    Add("Plugins", bold: false, color: SystemColors.GrayText).Font = new Font(Font, FontStyle.Bold);
                    foreach (ModRequirement need in needs)
                    {
                        bool have = installed.Contains(need.Plugin);
                        bool brings = bundled.Contains(need.Plugin, StringComparer.OrdinalIgnoreCase);
                        string state = have ? "installed" : brings ? "comes with this mod" : ModLibrary.IsStock(need.Plugin) ? "missing (part of Yu-Gi-Oh-Ex)" : "missing";
                        Add($"{(have || brings ? "✓" : "✗")} {need.Plugin}: {state}", color: have || brings ? Color.SeaGreen : Color.Firebrick).Margin = new Padding(12, 0, 0, 2);
                        if (!have && need.Url.Length > 0)
                            Link("Get " + need.Plugin, need.Url);
                    }
                    foreach (string plugin in bundled.Where(b => !needs.Any(n => n.Plugin.Equals(b, StringComparison.OrdinalIgnoreCase))))
                        Add($"+ {plugin}: comes with this mod{(installed.Contains(plugin) ? ", installed" : "")}").Margin = new Padding(12, 0, 0, 2);
                }
                foreach (ModIssue issue in _issues.Where(i => i.ModId == mod.Id))
                    Add((issue.Level == IssueLevel.Info ? "• " : "⚠ ") + issue.Text,
                        color: issue.Level switch { IssueLevel.Error => Color.Firebrick, IssueLevel.Warning => Color.DarkOrange, _ => SystemColors.ControlText });
            }
            _details.ResumeLayout();
        }

        // ---- install, remove, export, create, play ----

        private void Drag_Enter(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                e.Effect = DragDropEffects.Copy;
        }

        private void Drag_Drop(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
                foreach (string file in files.Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    Install(file);
        }

        private void PickAndInstall()
        {
            using var dialog = new OpenFileDialog { Filter = "Mods (*.zip)|*.zip", Multiselect = true, Title = "Install mods" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                foreach (string file in dialog.FileNames)
                    Install(file);
        }

        private void Install(string zip)
        {
            ModLibrary.Package package;
            try
            {
                package = ModLibrary.Inspect(zip);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"{Path.GetFileName(zip)} can't be installed: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string id = package.SuggestedId;
            if (Directory.Exists(Path.Combine(ModLibrary.ModsFolder(_game), id)) &&
                MessageBox.Show(this, $"\"{package.Info.Name}\" is already installed. Replace it with this one{(package.Info.Version.Length > 0 ? " (version " + package.Info.Version + ")" : "")}?\n\n" +
                                      "It keeps its place in the order.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            bool installPlugins = false;
            var dlls = package.PluginDlls.ToList();
            if (dlls.Count > 0)
            {
                if (_plugins == null)
                    MessageBox.Show(this, "This mod brings plugins, but the Plugins folder wasn't found (start the game once with Yu-Gi-Oh_Loader). " +
                                          "The mod is installed without them.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                else
                {
                    var answer = MessageBox.Show(this,
                        $"\"{package.Info.Name}\" brings {dlls.Count} plugin{(dlls.Count == 1 ? "" : "s")}:\n\n  {string.Join("\n  ", dlls)}\n\n" +
                        "Plugins are programs that run inside the game. Only install them from people you trust.\n\n" +
                        "Yes: install the mod and its plugins.\nNo: install the mod without them.\nCancel: don't install.",
                        Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                    if (answer == DialogResult.Cancel)
                        return;
                    installPlugins = answer == DialogResult.Yes;
                }
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                _mods = ModLibrary.Install(_game, package, id, installPlugins, _plugins);
                _status.Text = $"Installed \"{package.Info.Name}\"." + (ModLibrary.IsGameRunning() ? " The game is running: it shows up the next time it starts." : "");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                MessageBox.Show(this, $"\"{package.Info.Name}\" couldn't be installed: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            Fill(_mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
        }

        private void RemoveSelected()
        {
            if (Selected() is not Mod { IsLocal: false } mod)
                return;
            if (MessageBox.Show(this, $"Remove \"{mod.Info.Name}\"? Its folder is deleted.\n\nPlugins it installed stay (other mods may need them).",
                                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            try
            {
                _mods = ModLibrary.Remove(_game, mod, _mods);
                _status.Text = $"Removed \"{mod.Info.Name}\".";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "It couldn't be removed: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                _mods = ModLibrary.Load(_game);
            }
            Fill(null);
        }

        private void ExportSelected()
        {
            if (Selected() is not Mod { IsLocal: false } mod)
                return;
            using var dialog = new SaveFileDialog
            {
                Filter = "Mod (*.zip)|*.zip",
                FileName = mod.Id + (mod.Info.Version.Length > 0 ? "-" + ModLibrary.MakeId(mod.Info.Version) : "") + ".zip",
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                ModLibrary.Export(mod, dialog.FileName);
                _status.Text = "Saved " + dialog.FileName;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, "It couldn't be saved: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void CreateMod()
        {
            using var form = new CreateModForm(_game);
            if (form.ShowDialog(this) == DialogResult.OK && form.CreatedZip != null)
                _status.Text = "Created " + form.CreatedZip;
        }

        private void Play()
        {
            string? loader = ModLibrary.LoaderExe(_plugins);
            if (loader == null)
                return;
            if (ModLibrary.IsGameRunning())
            {
                MessageBox.Show(this, "The game is already running. Close it first: mods are applied when it starts.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(loader) { WorkingDirectory = Path.GetDirectoryName(loader)!, UseShellExecute = true });
        }

        private static void Open(string folder)
        {
            if (Directory.Exists(folder))
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }

        private void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(this, "Not a web link: " + url, Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
