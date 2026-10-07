using StartingCollection;

namespace Wolf.Mods
{
    /// <summary>
    /// Packs content into a mod .zip to share (docs/Mods.md): what's in a Yu-Gi-Oh-Ex folder (the game folder's by default), WolfX's
    /// YGO_2020-Ex patch (changed game files), loose game files, plugins to bring along, and mod.json (name, author, the plugins it needs and
    /// where to get them). Used by the Mod Manager and by WolfX (File > Export as mod).
    /// </summary>
    public sealed class CreateModForm : Form
    {
        private readonly string _gameFolder;
        private readonly TextBox _name = new(), _version = new() { Text = "1.0" }, _author = new(), _website = new(), _description = new();
        private readonly TextBox _details = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Height = 60 };
        private readonly TextBox _contentFolder = new() { ReadOnly = true };
        private readonly CheckedListBox _content = new() { CheckOnClick = true, IntegralHeight = false, Height = 130 };
        private readonly CheckBox _patch = new() { AutoSize = true };
        private readonly TextBox _loose = new();
        private readonly ListBox _plugins = new() { IntegralHeight = false, Height = 54 };
        private readonly DataGridView _requires = new()
        {
            Height = 110, AllowUserToAddRows = true, AllowUserToDeleteRows = true, RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = SystemColors.Window, BorderStyle = BorderStyle.FixedSingle,
        };
        private readonly TextBox _output = new();
        private string _patchToc = "";

        /// <summary>The .zip written, once Create worked.</summary>
        public string? CreatedZip { get; private set; }

        public CreateModForm(string gameFolder)
        {
            _gameFolder = gameFolder;
            Text = "Create mod";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(640, 560);
            Size = new Size(720, 780);
            Font = SystemFonts.MessageBoxFont ?? Font;

            _requires.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Plugin it needs (DLL name)", FillWeight = 40 });
            _requires.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Where to get it (leave empty for Yu-Gi-Oh-Ex's own plugins)", FillWeight = 60 });

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(10), AutoScroll = true };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            void Row(string label, Control control, string? tip = null)
            {
                var text = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(0, 6, 8, 0) };
                control.Dock = DockStyle.Top;
                control.Margin = new Padding(0, 3, 0, 3);
                grid.Controls.Add(text);
                grid.Controls.Add(control);
                if (tip != null)
                {
                    var tips = new ToolTip();
                    tips.SetToolTip(text, tip);
                    tips.SetToolTip(control, tip);
                }
            }
            Control WithButton(TextBox box, string caption, EventHandler click)
            {
                var panel = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Height = box.Height + 6 };
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                box.Dock = DockStyle.Fill;
                var button = new Button { Text = caption, AutoSize = true, Margin = new Padding(4, 0, 0, 0) };
                button.Click += click;
                panel.Controls.Add(box);
                panel.Controls.Add(button);
                return panel;
            }

            Row("Name", _name, "What the Mod Manager calls it; also the start of the .zip's name.");
            Row("Version", _version);
            Row("Author", _author);
            Row("Website", _website, "Where people find the mod or its updates (optional).");
            Row("Description", _description, "One line.");
            Row("Details", _details, "Longer text the Mod Manager shows (credits, what changed, ...).");
            Row("Content from", WithButton(_contentFolder, "Browse...", (_, _) => PickContentFolder()),
                "A Yu-Gi-Oh-Ex folder: the game folder's own (what you made with WolfX) unless you pick another.");
            Row("Content to include", _content, "New cards, packs, decks, music, pages ... Your save slots and exported decks are never included.");
            Row("Changed game files", _patch, "WolfX's patch (YGO_2020-Ex.toc / .dat next to the game): the game's own files you edited, e.g. the ban list or card art.");
            Row("Loose game files", WithButton(_loose, "Browse...", (_, _) => PickLooseFolder()),
                "Optional: a folder laid out like the archive (bin\\pd_limits.bin, main\\...). Leave empty for none.");
            var pluginButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
            var addPlugin = new Button { Text = "Add plugin DLL...", AutoSize = true };
            var removePlugin = new Button { Text = "Remove", AutoSize = true };
            addPlugin.Click += (_, _) => AddPlugins();
            removePlugin.Click += (_, _) =>
            {
                if (_plugins.SelectedItem != null)
                    _plugins.Items.Remove(_plugins.SelectedItem);
            };
            pluginButtons.Controls.AddRange([addPlugin, removePlugin]);
            Row("Plugins to bring", _plugins, "Plugins that aren't part of Yu-Gi-Oh-Ex and that you are allowed to share. The Mod Manager installs them with the mod.");
            Row("", pluginButtons);
            Row("Needs", _requires, "Filled in from the content (content.json). Add a link for any plugin people have to get somewhere else.");
            Row("Save as", WithButton(_output, "Browse...", (_, _) => PickOutput()));

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(10, 4, 10, 10) };
            var create = new Button { Text = "Create", AutoSize = true };
            var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
            create.Click += (_, _) => CreateMod();
            buttons.Controls.AddRange([cancel, create]);
            AcceptButton = create;
            CancelButton = cancel;

            Controls.Add(grid);
            Controls.Add(buttons);

            _name.TextChanged += (_, _) => SuggestOutput();
            LoadContent(Path.Combine(gameFolder, "Yu-Gi-Oh-Ex"));
            LoadPatch();
            string localLoose = ModLibrary.LocalLooseFolderName(gameFolder);
            if (localLoose.Length > 0 && Directory.Exists(Path.Combine(gameFolder, localLoose)))
                _loose.Text = Path.Combine(gameFolder, localLoose);
            SuggestOutput();
        }

        private string _suggested = "";

        private void SuggestOutput()
        {
            if (_output.Text.Length > 0 && _output.Text != _suggested)
                return;   // the user chose one
            string name = _name.Text.Trim().Length > 0 ? ModLibrary.MakeId(_name.Text) : "my-mod";
            _suggested = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".zip");
            _output.Text = _suggested;
        }

        private void LoadContent(string folder)
        {
            _contentFolder.Text = folder;
            _content.Items.Clear();
            foreach (string entry in ModLibrary.ContentEntries(folder))
                _content.Items.Add(Directory.Exists(Path.Combine(folder, entry)) ? entry + @"\" : entry, true);

            _requires.Rows.Clear();
            foreach (string plugin in ModLibrary.ContentPlugins(folder))
                _requires.Rows.Add(plugin, "");
        }

        private void LoadPatch()
        {
            _patchToc = Path.Combine(_gameFolder, "YGO_2020-Ex.toc");
            int count = 0;
            if (File.Exists(_patchToc) && File.Exists(Path.ChangeExtension(_patchToc, ".dat")))
            {
                try
                {
                    count = TocArchive.TryOpen(_patchToc)?.Count ?? 0;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
                {
                    count = 0;
                }
            }
            _patch.Enabled = count > 0;
            _patch.Checked = count > 0;
            _patch.Text = count > 0 ? $"Include YGO_2020-Ex ({count} changed game file{(count == 1 ? "" : "s")})" : "None (WolfX hasn't changed any game files)";
        }

        private void PickContentFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = "A Yu-Gi-Oh-Ex folder (with cards.json, packs.json, ...)", SelectedPath = _contentFolder.Text };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                LoadContent(dialog.SelectedPath);
        }

        private void PickLooseFolder()
        {
            using var dialog = new FolderBrowserDialog { Description = "A folder laid out like the game's archive (bin\\..., main\\...)" };
            if (_loose.Text.Length > 0)
                dialog.SelectedPath = _loose.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _loose.Text = dialog.SelectedPath;
        }

        private void AddPlugins()
        {
            using var dialog = new OpenFileDialog { Filter = "Plugins (*.dll)|*.dll", Multiselect = true, Title = "Plugins to bring with the mod" };
            string? plugins = ModLibrary.PluginsFolder(_gameFolder);
            if (plugins != null)
                dialog.InitialDirectory = plugins;
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            foreach (string file in dialog.FileNames)
            {
                if (!_plugins.Items.Contains(file))
                    _plugins.Items.Add(file);
                string name = Path.GetFileNameWithoutExtension(file);
                if (ModLibrary.IsStock(name))
                    MessageBox.Show(this, $"{name} comes with Yu-Gi-Oh-Ex; people normally have it already. It is added anyway.", Text,
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void PickOutput()
        {
            using var dialog = new SaveFileDialog { Filter = "Mod (*.zip)|*.zip", FileName = Path.GetFileName(_output.Text), OverwritePrompt = true };
            string? folder = Path.GetDirectoryName(_output.Text);
            if (folder != null && Directory.Exists(folder))
                dialog.InitialDirectory = folder;
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _output.Text = dialog.FileName;
        }

        private void CreateMod()
        {
            if (_name.Text.Trim().Length == 0)
            {
                MessageBox.Show(this, "Give the mod a name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _name.Focus();
                return;
            }
            var entries = _content.CheckedItems.Cast<string>().Select(e => e.TrimEnd('\\')).ToList();
            bool loose = _loose.Text.Trim().Length > 0;
            if (loose && !Directory.Exists(_loose.Text.Trim()))
            {
                MessageBox.Show(this, "The loose game files folder doesn't exist.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (entries.Count == 0 && !_patch.Checked && !loose && _plugins.Items.Count == 0)
            {
                MessageBox.Show(this, "There is nothing to put in the mod: tick some content, the changed game files, a loose folder or a plugin.", Text,
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var info = new ModInfo
            {
                Name = _name.Text.Trim(), Version = _version.Text, Author = _author.Text, Website = _website.Text,
                Description = _description.Text, Details = _details.Text.Replace("\r\n", "\n"),
            };
            foreach (DataGridViewRow row in _requires.Rows)
            {
                string plugin = ModLibrary.StripDll(row.Cells[0].Value?.ToString()?.Trim() ?? "");
                if (plugin.Length > 0 && !info.Requires.Any(r => r.Plugin.Equals(plugin, StringComparison.OrdinalIgnoreCase)))
                    info.Requires.Add(new ModRequirement { Plugin = plugin, Url = row.Cells[1].Value?.ToString()?.Trim() ?? "" });
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                ModLibrary.Create(new ModCreateOptions
                {
                    Info = info,
                    ContentFolder = _contentFolder.Text,
                    ContentEntries = entries,
                    PatchToc = _patch.Checked ? _patchToc : "",
                    LooseFolder = loose ? _loose.Text.Trim() : "",
                    Plugins = [.. _plugins.Items.Cast<string>()],
                }, _output.Text);
                CreatedZip = _output.Text;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                MessageBox.Show(this, "The mod couldn't be written: " + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            MessageBox.Show(this, $"Saved {_output.Text}.\n\nPeople install it with the Mod Manager (Install, or drop the .zip on its window).", Text,
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
