using System.Buffers.Binary;
using System.Text;
using Wolf.Editors;
using WolfX.Types;

namespace WolfX
{
    /// <summary>
    /// The .zib archives inside the open game data (card art, busts, decks, packs): pick one, see its files (with the card's name for card
    /// art), preview pictures, decks and pack lists, extract any of them, and replace or add files. Only the archive's index is read to list
    /// it, and only the picked file to preview it, so the 600 MB art archives open at once; archives that large can't be rewritten here
    /// (it would put another 600 MB into YGO_2020.dat), so they are view and extract only.
    /// </summary>
    internal sealed class ArchivesPage : UserControl, IGameEditor
    {
        private const long EditableLimit = 64L * 1024 * 1024;

        private sealed record ZibItem(string Name, long Start, int Size, int Flags);

        private readonly ListView _archives = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false };
        private readonly ListView _items = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, VirtualMode = true };
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripTextBox _find = new() { Width = 160, ToolTipText = "Part of a file name, or a card name for card art" };
        private readonly ToolStripButton _save, _extract, _extractAll, _replace, _add;
        private readonly PictureBox _picture = new() { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(40, 40, 46) };
        private readonly TextBox _info = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9.5f) };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private GameFolderFiles? _files;
        private string? _zib;
        private List<ZibItem> _all = [];
        private List<ZibItem> _rows = [];
        // files replaced or added in the picked archive, written on Save
        private readonly Dictionary<string, byte[]> _pending = new(StringComparer.Ordinal);

        public ArchivesPage()
        {
            _tools.Items.Add(_save = Button("Save", "Write the replaced / added files into the archive, in the game data (Ctrl+S)", () => Save()));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(_extract = Button("Extract...", "Save the selected files to a folder", () => Extract(selectedOnly: true)));
            _tools.Items.Add(_extractAll = Button("Extract all...", "Save every file of this archive to a folder", () => Extract(selectedOnly: false)));
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(_replace = Button("Replace...", "Replace the selected file with one from disk (saved into the archive on Save)", Replace));
            _tools.Items.Add(_add = Button("Add files...", "Add files to this archive (saved on Save)", AddFiles));

            _archives.Columns.Add("Archive", 190);
            _archives.Columns.Add("Size", 80, HorizontalAlignment.Right);
            _archives.SelectedIndexChanged += (_, _) => { if (_archives.SelectedItems.Count > 0) OpenZib((string)_archives.SelectedItems[0].Tag!); };

            _items.Columns.Add("File", 230);
            _items.Columns.Add("Size", 80, HorizontalAlignment.Right);
            _items.Columns.Add("What", 260);
            _items.RetrieveVirtualItem += (_, e) =>
            {
                var item = _rows[e.ItemIndex];
                e.Item = new ListViewItem([item.Name, Size(item.Size), Describe(item)])
                {
                    ForeColor = _pending.ContainsKey(item.Name) ? Color.FromArgb(170, 90, 0) : SystemColors.WindowText,
                };
            };
            _items.SelectedIndexChanged += (_, _) => Preview();
            _find.TextChanged += (_, _) => Refill();

            var preview = new Panel { Dock = DockStyle.Fill };
            var previewSplit = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            previewSplit.Panel1.Controls.Add(_picture);
            previewSplit.Panel2.Controls.Add(_info);
            preview.Controls.Add(previewSplit);
            PreviewPopOut.Attach(_picture, "Archive picture", doubleClick: true);

            var right = new SplitContainer { Dock = DockStyle.Fill };
            right.Panel1.Controls.Add(_items);
            right.Panel2.Controls.Add(preview);
            var main = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1 };
            main.Panel1.Controls.Add(_archives);
            main.Panel2.Controls.Add(right);
            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(main);
            Controls.Add(_tools);
            Controls.Add(status);
            Load += (_, _) =>
            {
                main.SplitterDistance = 290;
                right.SplitterDistance = Math.Max(300, right.Width * 55 / 100);
                previewSplit.SplitterDistance = Math.Max(120, previewSplit.Height * 60 / 100);
            };
            UpdateButtons();
        }

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
                Save();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static string Size(long bytes) => bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024):0.0} MB" : bytes >= 1 << 10 ? $"{bytes / 1024.0:0.0} KB" : $"{bytes} B";

        private long ArchiveSize => _zib != null && _files != null ? _all.Count == 0 ? 0 : _all.Max(i => i.Start + i.Size) : 0;

        private bool Editable => _zib != null && ArchiveSize <= EditableLimit;

        private void UpdateButtons()
        {
            _save.Enabled = _pending.Count > 0;
            _extract.Enabled = _items.SelectedIndices.Count > 0;
            _extractAll.Enabled = _all.Count > 0;
            _replace.Enabled = Editable && _items.SelectedIndices.Count == 1;
            _add.Enabled = Editable;
        }

        // ---- IGameEditor ----

        public bool Dirty => _pending.Count > 0;

        public IReadOnlyCollection<string> Files => _zib != null ? [_zib] : [];

        public string SavesTo => "Standard: the .zib archives inside the game data (files you replace or add). The card art archives are view and extract only.";

        public void Open(GameFolderFiles files)
        {
            _files = files;
            _pending.Clear();
            string? keep = _zib;
            _archives.BeginUpdate();
            _archives.Items.Clear();
            foreach (string path in files.Paths.Where(p => p.EndsWith(".zib", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                long size = files.Read(path, 0, 8) is { } head ? Math.Max(0, IndexEnd(head)) : 0;
                _archives.Items.Add(new ListViewItem([path, ""]) { Tag = path });
            }
            _archives.EndUpdate();
            _zib = null;
            var again = _archives.Items.Cast<ListViewItem>().FirstOrDefault(i => (string)i.Tag! == keep) ?? (_archives.Items.Count > 0 ? _archives.Items[0] : null);
            if (again != null)
                again.Selected = true;
            else
            {
                _all = [];
                Refill();
                _status.Text = "The open data has no .zib archives.";
            }
        }

        private static long IndexEnd(byte[] head) => BinaryPrimitives.ReadUInt32BigEndian(head) & ~3u;

        /// <summary>Reads only the archive's index (one 64 byte entry per file, up to where the first file starts).</summary>
        private void OpenZib(string path)
        {
            if (_files == null || path == _zib)
                return;
            if (_pending.Count > 0 && MessageBox.Show(this, $"{_zib} has replaced or added files that are not saved. Leave them?", "Archives",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            _pending.Clear();
            _zib = path;
            _all = [];
            try
            {
                if (_files.Read(path, 0, 8) is { Length: 8 } head)
                {
                    int indexBytes = (int)Math.Min(IndexEnd(head), 16 * 1024 * 1024);
                    if (_files.Read(path, 0, indexBytes) is { } index)
                    {
                        for (int at = 0; at + 64 <= index.Length; at += 64)
                        {
                            uint raw = BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(at));
                            int size = (int)BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(at + 4));
                            var name = index.AsSpan(at + 8, 56);
                            int length = name.IndexOf((byte)0);
                            if (raw == 0 && size == 0)
                                break;
                            _all.Add(new ZibItem(Encoding.UTF8.GetString(name[..(length < 0 ? 56 : length)]), raw & ~3u, size, (int)(raw & 3)));
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException)
            {
                _status.Text = $"Couldn't read {path}: {ex.Message}";
            }
            foreach (ListViewItem item in _archives.Items)
                if ((string)item.Tag! == path)
                    item.SubItems[1].Text = Size(ArchiveSize);
            _find.Text = "";
            Refill();
        }

        private void Refill()
        {
            string find = _find.Text.Trim();
            _rows = _all.Where(i => find.Length == 0 || i.Name.Contains(find, StringComparison.OrdinalIgnoreCase) || Describe(i).Contains(find, StringComparison.OrdinalIgnoreCase)).ToList();
            _items.SelectedIndices.Clear();
            _items.VirtualListSize = _rows.Count;
            _items.Invalidate();
            _status.Text = _zib == null ? "" : $"{_zib}: {_all.Count} files, {Size(ArchiveSize)}" + (Editable ? "" : " (view and extract only: too large to rewrite inside YGO_2020.dat)") +
                                               (_pending.Count > 0 ? $", {_pending.Count} replaced / added (not saved)" : "");
            Preview();
        }

        /// <summary>What a file is: the card for card art (&lt;Konami id&gt;.jpg), the deck or pack it holds.</summary>
        private static string Describe(ZibItem item)
        {
            string stem = Path.GetFileNameWithoutExtension(item.Name);
            if (int.TryParse(stem, out int id) && CardCatalog.NameOf(id) is { Length: > 0 } card && card != id.ToString())
                return card;
            return Path.GetExtension(item.Name).ToLowerInvariant() switch
            {
                ".ydc" => "deck",
                ".bin" when item.Name.StartsWith("bpack_", StringComparison.OrdinalIgnoreCase) => "battle pack cards",
                ".bin" when item.Name.StartsWith("packdata_", StringComparison.OrdinalIgnoreCase) => "pack cards",
                ".png" or ".jpg" or ".jpeg" => "picture",
                _ => "",
            };
        }

        private byte[]? Data(ZibItem item) => _pending.TryGetValue(item.Name, out var pending) ? pending : _files?.Read(_zib!, item.Start, item.Size);

        private void Preview()
        {
            UpdateButtons();
            var old = _picture.Image;
            _picture.Image = null;
            old?.Dispose();
            if (_items.SelectedIndices.Count != 1 || _items.SelectedIndices[0] >= _rows.Count)
            {
                _info.Text = _items.SelectedIndices.Count > 1 ? $"{_items.SelectedIndices.Count} files picked." : "";
                return;
            }
            var item = _rows[_items.SelectedIndices[0]];
            byte[]? data = Data(item);
            if (data == null)
            {
                _info.Text = "Couldn't read this file.";
                return;
            }
            var text = new StringBuilder($"{item.Name}\r\n{Size(data.Length)}" + (_pending.ContainsKey(item.Name) ? "  (replaced, not saved)" : "") + "\r\n\r\n");
            if (Imaging.Decode(data) is { } picture)
            {
                _picture.Image = picture;
                text.Append($"{picture.Width} x {picture.Height}\r\n");
            }
            else if (item.Name.EndsWith(".ydc", StringComparison.OrdinalIgnoreCase))
                DescribeDeck(data, text);
            else if (item.Name.StartsWith("packdata_", StringComparison.OrdinalIgnoreCase) || item.Name.StartsWith("bpack_", StringComparison.OrdinalIgnoreCase))
                DescribePack(item.Name, data, text);
            else
                Hex(data, text);
            _info.Text = text.ToString();
        }

        private static void DescribeDeck(byte[] data, StringBuilder text)
        {
            try
            {
                var deck = global::DeckData.YdcDeck.Parse(data);
                foreach (var (title, cards) in new[] { ("Main", deck.Main), ("Extra", deck.Extra), ("Side", deck.Side) })
                {
                    text.Append($"{title} deck ({cards.Count}):\r\n");
                    foreach (var group in cards.GroupBy(c => c))
                        text.Append($"  {group.Count()}x {CardCatalog.NameOf(group.Key)} ({group.Key})\r\n");
                }
            }
            catch (InvalidDataException ex)
            {
                text.Append("Not a deck file: " + ex.Message);
            }
        }

        private static void DescribePack(string name, byte[] data, StringBuilder text)
        {
            try
            {
                if (name.StartsWith("bpack_", StringComparison.OrdinalIgnoreCase))
                {
                    var battle = PackDef.BattlePackContents.Parse(data);
                    for (int i = 0; i < battle.Slots.Count; i++)
                        text.Append($"Slot {i + 1}: {battle.Slots[i].Count} entries, {battle.Slots[i].Distinct().Count()} cards\r\n");
                }
                else
                {
                    var pack = PackDef.PackContents.Parse(data);
                    text.Append($"{pack.Common.Count} common, {pack.Rare.Count} rare cards\r\n");
                }
                text.Append("\r\nEdit the cards on the Packs page.");
            }
            catch (InvalidDataException ex)
            {
                text.Append(ex.Message);
            }
        }

        private static void Hex(byte[] data, StringBuilder text)
        {
            for (int row = 0; row < Math.Min(data.Length, 512); row += 16)
            {
                var bytes = data.AsSpan(row, Math.Min(16, data.Length - row));
                text.Append($"{row:x4}  {string.Join(" ", bytes.ToArray().Select(b => b.ToString("x2"))),-48}  ");
                foreach (byte b in bytes)
                    text.Append(b is >= 32 and < 127 ? (char)b : '.');
                text.Append("\r\n");
            }
            if (data.Length > 512)
                text.Append("...");
        }

        // ---- extract, replace, add ----

        private void Extract(bool selectedOnly)
        {
            if (_zib == null)
                return;
            var items = selectedOnly ? _items.SelectedIndices.Cast<int>().Select(i => _rows[i]).ToList() : _all;
            if (items.Count == 0)
                return;
            using var dialog = new FolderBrowserDialog { Description = $"Extract {items.Count} file(s) of {_zib} into:", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            int done = 0;
            foreach (var item in items)
            {
                if (Data(item) is { } data)
                {
                    File.WriteAllBytes(Path.Combine(dialog.SelectedPath, item.Name), data);
                    done++;
                }
            }
            _status.Text = $"Extracted {done} file(s) to {dialog.SelectedPath}.";
        }

        private void Replace()
        {
            if (!Editable || _items.SelectedIndices.Count != 1)
                return;
            var item = _rows[_items.SelectedIndices[0]];
            using var dialog = new OpenFileDialog { Title = $"Replace {item.Name} with" };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            _pending[item.Name] = File.ReadAllBytes(dialog.FileName);
            _items.Invalidate();
            Refill();
        }

        private void AddFiles()
        {
            if (!Editable)
                return;
            using var dialog = new OpenFileDialog { Title = $"Add files to {_zib}", Multiselect = true };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            foreach (string path in dialog.FileNames)
            {
                string name = Path.GetFileName(path);
                if (Encoding.UTF8.GetByteCount(name) >= 56)
                {
                    MessageBox.Show(this, $"{name}: a name in a .zib is at most 55 characters.", "Archives", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    continue;
                }
                _pending[name] = File.ReadAllBytes(path);
                if (_all.All(i => i.Name != name))
                    _all.Add(new ZibItem(name, 0, _pending[name].Length, 0));
            }
            Refill();
        }

        public bool Save()
        {
            if (_files == null || _zib == null || _pending.Count == 0)
                return true;
            try
            {
                if (_files.Read(_zib) is not { } whole)
                    return false;
                var archive = global::Types.ZibArchive.Parse(whole);
                foreach (var (name, data) in _pending)
                    archive.Set(name, data);
                _files.Write(new Dictionary<string, byte[]> { [_zib] = archive.ToBytes() });
                string saved = _zib;
                int count = _pending.Count;
                _pending.Clear();
                _zib = null;
                OpenZib(saved);
                _status.Text = $"Saved {count} file(s) into {saved} ({_files.Describe(saved)}).";
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }
    }
}
