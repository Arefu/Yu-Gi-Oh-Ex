using System.IO;
using System.Text.Json.Nodes;
using Types;
using WolfX.Types;

namespace Wolf.Editors
{
    /// <summary>
    /// The card id table (bin/CARD_INTID.bin, File Type Libraries/CardIntId): which internal id (the card's props, name, art) each Konami
    /// id uses, with the problems the game would have with it.
    /// A game Konami id's internal id can be changed and is saved into the game's file (the game ignores the file unless it stays 22138
    /// bytes, which the library keeps). Custom cards don't go in it (Yu-Gi-Oh-MoreCards gives them their own slots): your
    /// Yu-Gi-Oh-Ex\cards.json cards are listed, with the next Konami id free for a new card. Ids in the table without a card are reserved
    /// (the exe keeps effect data for them) and never offered as free; only 15300+ is.
    /// </summary>
    public sealed class CardIdMapEditor : UserControl, IGameEditor
    {
        private readonly ToolStrip _tools = new() { GripStyle = ToolStripGripStyle.Hidden };
        private readonly ToolStripButton _save, _copyFree;
        private readonly ToolStripComboBox _filter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly ToolStripTextBox _find = new() { Width = 160, ToolTipText = "Konami id, internal id or part of a name" };
        private readonly ListView _list = new()
        {
            Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = true, VirtualMode = true, HideSelection = false,
            MultiSelect = false,
        };
        private readonly NumericUpDown _internal = new() { Minimum = 0, Maximum = CardIdMap.InternalCount - 1, Width = 90 };
        private readonly Button _apply = new() { Text = "Set", AutoSize = true };
        private readonly Label _selected = new() { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
        private readonly FlowLayoutPanel _editRow = new() { Dock = DockStyle.Bottom, Height = 34, WrapContents = false };
        private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };

        private sealed record Row(int KonamiId, int InternalId, string Note);

        private CardIdMap? _map;
        private byte[] _saved = [];
        private GameFolderFiles? _gameFiles;
        private readonly Dictionary<int, string> _customNames = [];   // cards.json cards (new ones and overrides)
        private List<Row> _rows = [];
        private Dictionary<int, int> _useCount = [];

        public CardIdMapEditor()
        {
            _save = Button("Save", "Save the table into the game data", () => Save());
            _copyFree = Button("Copy next free id", "Copy the first Konami id no card uses yet (for a new card in cards.json)", CopyFreeId);
            _tools.Items.AddRange([_save, new ToolStripSeparator(), new ToolStripLabel("Show:")]);
            _filter.Items.AddRange(["All ids", "Cards", "Reserved ids (no card)", "Problems", "Your cards (cards.json)"]);
            _filter.SelectedIndex = 1;
            _filter.SelectedIndexChanged += (_, _) => Refill();
            _tools.Items.Add(_filter);
            _tools.Items.Add(new ToolStripLabel("Find:"));
            _find.TextChanged += (_, _) => Refill();
            _tools.Items.Add(_find);
            _tools.Items.Add(new ToolStripSeparator());
            _tools.Items.Add(_copyFree);

            _list.Columns.Add("Konami id", 80);
            _list.Columns.Add("Internal id", 80);
            _list.Columns.Add("Name", 380);
            _list.Columns.Add("", 380);
            _list.RetrieveVirtualItem += (_, e) =>
            {
                var row = _rows[e.ItemIndex];
                e.Item = new ListViewItem([row.KonamiId.ToString(), row.InternalId == 0 ? "" : row.InternalId.ToString(), Name(row.KonamiId), row.Note]);
            };
            _list.SelectedIndexChanged += (_, _) => ShowSelected();

            _editRow.Controls.Add(_selected);
            _editRow.Controls.Add(new Label { Text = "Internal id (0 = no card):", AutoSize = true, Margin = new Padding(12, 7, 3, 3) });
            _editRow.Controls.Add(_internal);
            _apply.Click += (_, _) => Apply();
            _editRow.Controls.Add(_apply);

            var status = new StatusStrip { SizingGrip = false };
            status.Items.Add(_status);
            Controls.Add(_list);
            Controls.Add(_editRow);
            Controls.Add(_tools);
            Controls.Add(status);
            SetEditable(false);
            _status.Text = "Open the game data (File > Open).";
        }

        public bool Dirty => _map != null && !_map.ToBytes().AsSpan().SequenceEqual(_saved);

        public IReadOnlyCollection<string> Files => [CardIdMap.GamePath];

        public string SavesTo => $"Standard: {CardIdMap.GamePath}. Custom cards never go in it (Yu-Gi-Oh-MoreCards gives them slots); they are listed from Yu-Gi-Oh-Ex\\cards.json.";

        private static ToolStripButton Button(string text, string tip, Action click)
        {
            var button = new ToolStripButton(text) { ToolTipText = tip, DisplayStyle = ToolStripItemDisplayStyle.Text };
            button.Click += (_, _) => click();
            return button;
        }

        private static string Name(int konamiId) => CardCatalog.NameOf(konamiId);

        private void SetEditable(bool editable) => _editRow.Enabled = editable;

        // ---- opening / saving ----

        public void Open(GameFolderFiles files)
        {
            _gameFiles = files;
            var bytes = files.Read(CardIdMap.GamePath);
            if (bytes == null)
            {
                _map = null;
                SetEditable(false);
                _status.Text = $"The open data has no {CardIdMap.GamePath}.";
                return;
            }
            try
            {
                _map = CardIdMap.Parse(bytes);
                _saved = _map.ToBytes();
                ReadCustomCards();
                SetEditable(true);
                Loaded(files.Describe(CardIdMap.GamePath));
            }
            catch (InvalidDataException ex)
            {
                _status.Text = ex.Message;
            }
        }

        /// <summary>The cards in Yu-Gi-Oh-Ex\cards.json ("id" and "name"; a list, or { "cards": [...] }).</summary>
        private void ReadCustomCards()
        {
            _customNames.Clear();
            string path = _gameFiles!.ExPath("cards.json");
            if (!File.Exists(path))
                return;
            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new System.Text.Json.JsonDocumentOptions
                {
                    CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true,
                });
                var list = root as JsonArray ?? root?["cards"] as JsonArray;
                foreach (var entry in list ?? [])
                    if (entry?["id"] is JsonValue id && id.TryGetValue(out int konami))
                        _customNames[konami] = entry["name"]?.GetValue<string>() ?? "";
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or IOException)
            {
                _status.Text = $"cards.json couldn't be read: {ex.Message}";
            }
        }

        private void Loaded(string from)
        {
            Refill();
            var problems = _map!.Problems();
            int cards = _map.InternalIds.Count(id => id != 0);
            _status.Text = $"{from}: {cards} cards, {CardIdMap.Count - cards} reserved Konami ids with no card ({CardIdMap.FirstKonamiId}-{CardIdMap.LastKonamiId}; the game keeps data for them, don't reuse them)" +
                (problems.Count > 0 ? $". {problems.Count} problems: {problems[0]}" : ", no problems") +
                $". cards.json: {_customNames.Count} cards; next free id for a new card: {NextFreeId()?.ToString() ?? "none"}";
        }

        /// <summary>Standard content only: the table goes back into the game data.</summary>
        public bool Save()
        {
            if (_map == null || _gameFiles == null)
                return false;
            if (!Dirty)
                return true;
            var problems = _map.Problems().Where(p => !p.EndsWith("can't be reached).")).ToList();
            if (problems.Count > 0 && MessageBox.Show(this, "The table has problems:\n\n" + string.Join("\n", problems.Take(8)) + "\n\nSave anyway?",
                    "Card ids", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return false;
            try
            {
                _gameFiles.Write(CardIdMap.GamePath, _map.ToBytes());
                _saved = _map.ToBytes();
                _status.Text = $"Saved {CardIdMap.GamePath} into {_gameFiles.Describe(CardIdMap.GamePath)}.";
                return true;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                _status.Text = "Not saved: " + ex.Message;
                return false;
            }
        }

        // ---- list ----

        private void Refill()
        {
            if (_map == null)
                return;
            _useCount = [];
            foreach (ushort id in _map.InternalIds)
                if (id != 0)
                    _useCount[id] = _useCount.GetValueOrDefault(id) + 1;

            string find = _find.Text.Trim();
            var rows = new List<Row>();
            bool Matches(int konami, int internalId, string name) =>
                find.Length == 0 || konami.ToString() == find || internalId.ToString() == find || name.Contains(find, StringComparison.OrdinalIgnoreCase);

            if (_filter.SelectedIndex == 4)
            {
                foreach (var (konami, name) in _customNames.OrderBy(c => c.Key))
                {
                    string note = konami is >= CardIdMap.FirstCustomId and <= CardIdMap.LastCustomId ? "your new card (the plugin gives it a slot)"
                        : _map.InternalOf(konami) != 0 ? "your changes to this game card" : "id outside the custom range (15300-19999)";
                    if (Matches(konami, _map.InternalOf(konami), name))
                        rows.Add(new Row(konami, _map.InternalOf(konami), note + (name.Length > 0 ? $": {name}" : "")));
                }
            }
            else
            {
                for (int i = 0; i < CardIdMap.Count; i++)
                {
                    int konami = CardIdMap.FirstKonamiId + i, internalId = _map.InternalIds[i];
                    string note = internalId == 0 ? "reserved: the game keeps data for this id (effects...), not free" : internalId >= CardIdMap.InternalCount ? "internal id past the game's table"
                        : _useCount[internalId] > 1 ? "internal id used more than once" : "";
                    bool show = _filter.SelectedIndex switch
                    {
                        1 => internalId != 0,
                        2 => internalId == 0,
                        3 => note.Length > 0 && internalId != 0,
                        _ => true,
                    };
                    if (show && Matches(konami, internalId, internalId == 0 ? "" : Name(konami)))
                        rows.Add(new Row(konami, internalId, note + (_customNames.ContainsKey(konami) ? " (changed in cards.json)" : "")));
                }
            }
            _rows = rows;
            _list.VirtualListSize = rows.Count;
            _list.Invalidate();
            ShowSelected();
        }

        private Row? SelectedRow() => _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;

        private void ShowSelected()
        {
            var row = SelectedRow();
            _selected.Text = row == null ? "Pick a Konami id." : $"Konami {row.KonamiId}:";
            _internal.Enabled = _apply.Enabled = row != null && row.KonamiId <= CardIdMap.LastKonamiId;
            if (row != null)
                _internal.Value = Math.Clamp(row.InternalId, 0, (int)_internal.Maximum);
        }

        private void Apply()
        {
            if (_map == null || SelectedRow() is not { } row)
                return;
            if (row.InternalId == 0 && _internal.Value != 0 &&
                MessageBox.Show(this, $"Konami {row.KonamiId} is reserved: the game keeps data for it (effects and the like) even though no card " +
                        "uses it, so a card put here inherits that. New cards belong in cards.json (15300 and up). Map it anyway?",
                    "Card ids", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            int index = _list.SelectedIndices[0];
            _map.Set(row.KonamiId, (int)_internal.Value);
            Refill();
            if (index < _rows.Count)
            {
                _list.SelectedIndices.Clear();
                _list.SelectedIndices.Add(index);
            }
            var problems = _map.Problems();
            _status.Text = $"Konami {row.KonamiId} -> internal {(int)_internal.Value}. " + (problems.Count > 0 ? problems[0] : "No problems.");
        }

        // ---- free ids (WolfEx) ----

        /// <summary>The first Konami id in the custom range (15300-19999) that no cards.json card uses.</summary>
        private int? NextFreeId()
        {
            for (int id = CardIdMap.FirstCustomId; id <= CardIdMap.LastCustomId; id++)
                if (!_customNames.ContainsKey(id))
                    return id;
            return null;
        }

        private void CopyFreeId()
        {
            if (NextFreeId() is int id)
            {
                Clipboard.SetText(id.ToString());
                _status.Text = $"Copied {id}: the next free Konami id for a new card in cards.json.";
            }
            else
                _status.Text = "Every custom id (15300-19999) is used.";
        }
    }
}
