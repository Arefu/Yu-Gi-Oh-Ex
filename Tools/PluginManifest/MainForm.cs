namespace PluginManifest
{
    /// <summary>
    /// Makes and edits the manifest (&lt;DLL name&gt;.json) next to each plugin DLL: what the in-game Plugins list calls it, whether it can be
    /// switched off, which plugins it needs, and which extra DLLs belong to it. The loader, Yu-Gi-Oh-Core and Yu-Gi-Oh-RIX read these files.
    /// </summary>
    public partial class MainForm : Form
    {
        // A description longer than this does not fit the game's description box, and the box does not scroll.
        private const int LongDescription = 56;

        private string _folder = "";
        private readonly List<PluginFile> _plugins = new();
        private PluginFile? _current;
        private bool _loading;

        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Binaries\<config>\Tools\PluginManifest.exe: the plugins are in Binaries\<config>\Plugins.
            var candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Plugins"));
            LoadFolder(Directory.Exists(candidate) ? candidate : "");
        }

        private void LoadFolder(string folder)
        {
            _folder = folder;
            folderBox.Text = folder.Length == 0 ? "No plugins folder - use Browse" : folder;
            _plugins.Clear();
            pluginList.Items.Clear();

            if (folder.Length > 0)
            {
                foreach (var gui in new[] { false, true })
                {
                    var dir = gui ? Path.Combine(folder, "YGO-Ex") : folder;
                    if (!Directory.Exists(dir)) continue;
                    foreach (var dll in Directory.GetFiles(dir, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                        _plugins.Add(new PluginFile(Path.GetFileNameWithoutExtension(dll), gui, dir));
                }
            }

            foreach (var plugin in _plugins) pluginList.Items.Add(plugin);
            ShowPlugin(null);
            statusLabel.Text = _plugins.Count == 0 ? "No DLLs found." : $"{_plugins.Count} DLL(s), {_plugins.Count(p => p.HasManifest)} with a manifest.";
        }

        private void browseButton_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog { Description = "The Plugins folder (the one with Yu-Gi-Oh-Core.dll in it)", SelectedPath = _folder };
            if (dialog.ShowDialog(this) == DialogResult.OK) LoadFolder(dialog.SelectedPath);
        }

        private void reloadButton_Click(object sender, EventArgs e) => LoadFolder(_folder);

        private void pluginList_SelectedIndexChanged(object sender, EventArgs e)
        {
            ShowPlugin(pluginList.SelectedItem as PluginFile);
        }

        private void ShowPlugin(PluginFile? plugin)
        {
            _loading = true;
            _current = plugin;

            requiresList.Items.Clear();
            dllsList.Items.Clear();
            saveButton.Enabled = plugin != null;
            removeButton.Enabled = plugin != null && plugin.HasManifest;
            manifestGroup.Enabled = plugin != null;

            if (plugin == null)
            {
                nameLabel.Text = "Pick a plugin on the left";
                titleBox.Text = descriptionBox.Text = "";
                enforcedBox.Checked = false;
                previewBox.Text = "";
                _loading = false;
                return;
            }

            var manifest = ManifestModel.Read(plugin.ManifestPath);
            nameLabel.Text = plugin.Name + (plugin.Gui ? "   (started by Yu-Gi-Oh-Core)" : "   (injected by the loader)");
            titleBox.Text = manifest.Title;
            descriptionBox.Text = manifest.Description;
            enforcedBox.Checked = manifest.Enforced;

            // Requirements can be any other plugin, of either kind. A requirement that is not installed is kept and marked so it can be fixed.
            var known = _plugins.Where(p => p != plugin).Select(p => p.Name).ToList();
            foreach (var name in known)
                requiresList.Items.Add(name, manifest.Requires.Contains(name, StringComparer.OrdinalIgnoreCase));
            foreach (var missing in manifest.Requires.Where(r => !known.Contains(r, StringComparer.OrdinalIgnoreCase)))
                requiresList.Items.Add(missing + "   (not installed)", true);

            // The DLLs that belong to a plugin are in the same folder as it.
            foreach (var other in _plugins.Where(p => p != plugin && p.Gui == plugin.Gui))
                dllsList.Items.Add(other.Name, manifest.Dlls.Contains(other.Name, StringComparer.OrdinalIgnoreCase));

            _loading = false;
            UpdatePreview();
        }

        private static string NameOf(object item)
        {
            var text = item.ToString() ?? "";
            var marker = text.IndexOf("   (", StringComparison.Ordinal);
            return marker >= 0 ? text[..marker] : text;
        }

        private ManifestModel Collect()
        {
            var manifest = new ManifestModel
            {
                Title = titleBox.Text.Trim(),
                Description = descriptionBox.Text.Trim(),
                Enforced = enforcedBox.Checked,
            };
            foreach (var item in requiresList.CheckedItems) manifest.Requires.Add(NameOf(item));
            foreach (var item in dllsList.CheckedItems) manifest.Dlls.Add(NameOf(item));
            return manifest;
        }

        private void UpdatePreview()
        {
            if (_loading || _current == null) return;
            previewBox.Text = Collect().ToJson().Replace("\n", "\r\n");

            var length = descriptionBox.Text.Trim().Length;
            descriptionCount.ForeColor = length > LongDescription ? Color.Firebrick : SystemColors.GrayText;
            descriptionCount.Text = length > LongDescription
                ? $"{length} characters: too long for the game's description box (it does not scroll). Keep it under {LongDescription}."
                : $"{length} characters. One short line: the game shows it in a small box, and it does not scroll.";
        }

        private void anyField_Changed(object sender, EventArgs e) => UpdatePreview();

        // The checked items are only up to date after the click has been applied.
        private void anyList_ItemCheck(object sender, ItemCheckEventArgs e) => BeginInvoke(new Action(UpdatePreview));

        private void saveButton_Click(object sender, EventArgs e)
        {
            if (_current == null) return;
            var manifest = Collect();

            var problems = new List<string>();
            if (manifest.Requires.Contains(_current.Name, StringComparer.OrdinalIgnoreCase)) problems.Add("It requires itself.");
            if (manifest.Description.Length > LongDescription) problems.Add($"The description is {manifest.Description.Length} characters, so it will not fit the game's description box.");
            if (manifest.Enforced && manifest.Requires.Count > 0) problems.Add("It is enforced but requires other plugins; they will not be forced on, so it may be blocked.");
            var cycle = FindCycle(_current.Name, manifest);
            if (cycle != null) problems.Add("Requirement cycle: " + cycle);

            if (problems.Count > 0 && MessageBox.Show(this, string.Join("\n", problems) + "\n\nSave anyway?", "Check the manifest", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            File.WriteAllText(_current.ManifestPath, manifest.ToJson(), new System.Text.UTF8Encoding(false));
            statusLabel.Text = $"Saved {_current.ManifestPath}";
            var index = pluginList.SelectedIndex;
            pluginList.Items[index] = _current;   // refreshes the "(no manifest)" marker
            pluginList.SelectedIndex = index;
        }

        /// <summary>A chain of requirements that comes back to the plugin being saved, written as "A -> B -> A", or null.</summary>
        private string? FindCycle(string start, ManifestModel manifest)
        {
            var path = new List<string> { start };
            bool Walk(string name, IEnumerable<string> requires)
            {
                foreach (var next in requires)
                {
                    if (string.Equals(next, start, StringComparison.OrdinalIgnoreCase)) { path.Add(next); return true; }
                    if (path.Contains(next, StringComparer.OrdinalIgnoreCase)) continue;
                    var file = _plugins.FirstOrDefault(p => string.Equals(p.Name, next, StringComparison.OrdinalIgnoreCase));
                    if (file == null) continue;
                    path.Add(next);
                    if (Walk(next, ManifestModel.Read(file.ManifestPath).Requires)) return true;
                    path.RemoveAt(path.Count - 1);
                }
                return false;
            }
            return Walk(start, manifest.Requires) ? string.Join(" -> ", path) : null;
        }

        private void removeButton_Click(object sender, EventArgs e)
        {
            if (_current == null || !_current.HasManifest) return;
            if (MessageBox.Show(this, $"Delete {_current.ManifestPath}?\nThe plugin then shows its DLL name and no description, and requires nothing.", "Delete manifest",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            File.Delete(_current.ManifestPath);
            statusLabel.Text = $"Deleted {_current.ManifestPath}";
            var index = pluginList.SelectedIndex;
            pluginList.Items[index] = _current;
            pluginList.SelectedIndex = index;
        }
    }
}
