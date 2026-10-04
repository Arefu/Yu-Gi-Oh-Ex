using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using PackDef;
using StartingCollection;
using Types;

namespace WolfEx
{
    /// <summary>
    /// Yu-Gi-Oh-Ex/packs.json, read by Yu-Gi-Oh-BetterCardShop (Packs.cpp, docs/Packs.md):
    /// "packs" - cards added to (or replacing) the shop's reward packs, and "newPacks" - packs the game doesn't have, each in a free
    /// pack id (the game has 128 slots and uses 36) with its own name, series (shop tab), cost, picture (an existing pack's), unlock rule,
    /// title and text per language, and common/rare cards. Nothing in the game files is changed.
    /// </summary>
    internal sealed partial class PacksPanel : UserControl, IContentPanel
    {
        private const int MinId = 1;
        private const int MaxId = 19999;
        private const int PackSlots = 128;
        private static readonly string[] Languages = ["E", "F", "G", "I", "J", "S"];

        private sealed class PackEntry
        {
            public string Name = "";
            public string Title = "";
            public int Common;         // cards the game has in the pack
            public int Rare;
            public string ExtraCommon = "";   // new pack: its whole common list
            public string ExtraRare = "";
            public bool Replace;       // the lists are the pack's whole contents instead of additions

            // new packs only
            public bool IsNew;
            public int Id;
            public int Series;
            public int Cost = 200;
            public string Art = "";
            public string UnlockWith = "";    // "" = from the start, "never", or a pack name
            public Dictionary<string, string> Titles = [];
            public Dictionary<string, string> Texts = [];

            public bool HasChanges => IsNew || ExtraCommon.Trim().Length > 0 || ExtraRare.Trim().Length > 0;
            public override string ToString() => $"{Name}  -  {Titles.GetValueOrDefault("E", "(new pack)")}  (id {Id})";
        }

        private readonly List<PackEntry> _packs = [];
        private readonly HashSet<int> _gameIds = [];
        private readonly Dictionary<int, string> _seriesNames = [];   // series -> the titles of its first packs, to recognise the tab
        private bool _replaceAll; // packs.json "replaceDefaults", kept as it was read
        // packs.json entries that add to (or replace) a game pack's cards: kept as they are and written back, but not listed here any more -
        // the game's packs are edited on the Packs page, straight in packs.zib
        private Dictionary<string, (string Common, string Rare, bool Replace)> _kept = [];
        private bool _binding;

        // the new-pack editor (built in code, under the designer's controls)
        private readonly GroupBox _newBox = new() { Text = "New pack", Dock = DockStyle.Top, AutoSize = true, Visible = false };
        private readonly TextBox _name = new() { Width = 160 };
        private readonly NumericUpDown _id = new() { Minimum = 0, Maximum = PackSlots - 1, Width = 60 };
        private readonly ComboBox _series = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly NumericUpDown _cost = new() { Minimum = 0, Maximum = 99999, Increment = 50, Width = 80 };
        private readonly ComboBox _art = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly ComboBox _unlock = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 330 };
        private readonly DataGridView _texts = new()
        {
            AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, Height = 190, Dock = DockStyle.Top,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        };
        private readonly List<string> _gamePackNames = [];

        public string Title => "Packs";

        public PacksPanel()
        {
            InitializeComponent();
            BuildNewPackEditor();
            SetEditorEnabled(false);
            // the designer's distance was set while the page was still its default size, which squeezed the pack list
            Load += (_, _) => split.SplitterDistance = Math.Clamp(Width * 26 / 100, 220, 340);
        }

        private void BuildNewPackEditor()
        {
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = false };
            var add = new Button { Text = "New pack", AutoSize = true };
            var remove = new Button { Text = "Remove new pack", AutoSize = true };
            add.Click += (_, _) => AddNewPack();
            remove.Click += (_, _) => RemoveNewPack();
            buttons.Controls.AddRange([add, remove]);
            split.Panel1.Controls.Add(buttons);

            var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(4) };
            void Row(string label, Control control)
            {
                grid.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(3, 7, 3, 3) });
                grid.Controls.Add(control);
            }
            Row("Name (letters, digits, _):", _name);
            Row("Pack id (free slot, keep it):", _id);
            Row("Shop tab (series):", _series);
            Row("Cost (DP):", _cost);
            Row("Picture (an existing pack's):", _art);
            Row("Unlocked:", _unlock);
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Language", ReadOnly = true, FillWeight = 12 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Title", FillWeight = 35 });
            _texts.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Text", FillWeight = 53 });
            foreach (string language in Languages)
                _texts.Rows.Add(language, "", "");
            var textsLabel = new Label
            {
                Text = "Title and text per language (a language left empty uses English):", AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(4, 6, 0, 2),
            };
            _newBox.Controls.Add(_texts);
            _newBox.Controls.Add(textsLabel);
            _newBox.Controls.Add(grid);

            right.RowCount++;
            right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            right.Controls.Add(_newBox, 0, right.RowCount - 1);

            _name.TextChanged += Editor_Changed;
            _id.ValueChanged += Editor_Changed;
            _series.SelectedIndexChanged += Editor_Changed;
            _cost.ValueChanged += Editor_Changed;
            _art.SelectedIndexChanged += Editor_Changed;
            _unlock.SelectedIndexChanged += Editor_Changed;
            _texts.CellValueChanged += (_, _) => Commit();
        }

        private void List_SelectedIndexChanged(object? sender, EventArgs e) => BindSelected();

        private void Editor_Changed(object? sender, EventArgs e) => Commit();

        private void btnCommonByName_Click(object? sender, EventArgs e) => PickInto(_common);

        private void btnRareByName_Click(object? sender, EventArgs e) => PickInto(_rare);

        /// <summary>Opens the card picker and appends the chosen ids to the box.</summary>
        private void PickInto(TextBox box)
        {
            var database = WolfX.Types.CardCatalog.Get(this);
            if (database == null)
                return;

            using var picker = new WolfX.Types.CardPickerDialog(database, "Add cards to the pack", askCopies: false);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                return;

            var existing = box.Text.Split([',', ' ', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries).ToList();
            foreach (var (card, _) in picker.Result)
            {
                if (!existing.Contains(card.Id.ToString()))
                    existing.Add(card.Id.ToString());
            }
            box.Text = string.Join(", ", existing);
        }

        private PackEntry? Selected => _list.SelectedIndex >= 0 ? _packs[_list.SelectedIndex] : null;

        private ContentSnapshot? _snapshot;

        public bool Dirty => _snapshot?.Changed == true;

        public void MarkSaved() => (_snapshot ??= new ContentSnapshot(() => new object[] { _packs, _replaceAll })).Mark();

        public void LoadFrom(string extraCardsFolder, string gameFolder)
        {
            _packs.Clear();
            _list.Items.Clear();
            _gameIds.Clear();
            _gamePackNames.Clear();
            _seriesNames.Clear();

            string jsonPath = Path.Combine(extraCardsFolder, "packs.json");
            var additions = ReadAdditions(jsonPath, out _replaceAll);

            try
            {
                var files = GameFiles.FromGameFolder(gameFolder);
                byte[]? def = files.ReadBytes(Path.Combine("main", "packdefdata_E.bin"));
                byte[]? packsZib = files.ReadBytes("packs.zib");
                var archive = packsZib != null ? ZibArchive.Parse(packsZib) : null;

                if (def != null)
                {
                    var records = PackDefFile.Parse(def).Records;
                    foreach (var record in records)
                        _gameIds.Add((int)record.Id);

                    // the game's packs are only read for what a new pack needs: taken ids and names, the shop tabs, art and unlock choices
                    foreach (var record in records.Where(record => record.IsReward).OrderBy(record => record.Series).ThenBy(record => record.Id))
                    {
                        _gamePackNames.Add(record.Name);
                        if (!_seriesNames.ContainsKey((int)record.Series))
                            _seriesNames[(int)record.Series] = record.Title;
                        else if (!_seriesNames[(int)record.Series].Contains(','))
                            _seriesNames[(int)record.Series] += ", " + record.Title + "...";
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read the game's packs:\n{ex.Message}", "Packs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _kept = additions;

            _packs.AddRange(ReadNewPacks(jsonPath));

            _series.Items.Clear();
            for (int series = 0; series <= 5; series++)
                _series.Items.Add($"{series}: {_seriesNames.GetValueOrDefault(series, "(no game packs)")}");
            _art.Items.Clear();
            _art.Items.Add("(none: wrap_<name>, only if the game has that picture)");
            _art.Items.AddRange([.. _gamePackNames]);
            _unlock.Items.Clear();
            _unlock.Items.Add("From the start");
            _unlock.Items.Add("Never (stays locked)");
            foreach (string name in _gamePackNames)
                _unlock.Items.Add($"With pack {name}");

            foreach (var pack in _packs)
                _list.Items.Add(pack);

            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
            else
                BindSelected();
        }

        public bool SaveTo(string extraCardsFolder)
        {
            var problems = new List<string>();
            var array = new JsonArray();

            foreach (var (name, kept) in _kept)
            {
                var common = ParseIds(kept.Common, problems, name);
                var rare = ParseIds(kept.Rare, problems, name);
                array.Add(new JsonObject
                {
                    ["pack"] = name,
                    ["common"] = new JsonArray(common.Select(id => (JsonNode)id).ToArray()),
                    ["rare"] = new JsonArray(rare.Select(id => (JsonNode)id).ToArray()),
                    ["replace"] = kept.Replace,
                });
            }

            var newPacks = new JsonArray();
            var usedIds = new HashSet<int>();
            var usedNames = new HashSet<string>(_gamePackNames, StringComparer.OrdinalIgnoreCase);
            foreach (var pack in _packs.Where(pack => pack.IsNew))
            {
                string label = pack.Name.Length > 0 ? pack.Name : $"(id {pack.Id})";
                if (pack.Name.Length == 0 || !pack.Name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_'))
                    problems.Add($"New pack {label}: the name must be letters, digits or _ (it is part of file and picture names).");
                else if (!usedNames.Add(pack.Name))
                    problems.Add($"New pack {label}: another pack already has this name.");
                if (_gameIds.Contains(pack.Id))
                    problems.Add($"New pack {label}: id {pack.Id} is a game pack's; pick a free one.");
                else if (!usedIds.Add(pack.Id))
                    problems.Add($"New pack {label}: another new pack has id {pack.Id}.");
                var common = ParseIds(pack.ExtraCommon, problems, label);
                var rare = ParseIds(pack.ExtraRare, problems, label);
                if (common.Count == 0 || rare.Count == 0)
                    problems.Add($"New pack {label}: it needs common and rare cards (the game draws from both).");
                if (pack.Titles.GetValueOrDefault("E", "").Trim().Length == 0)
                    problems.Add($"New pack {label}: give it an English title.");

                var node = new JsonObject
                {
                    ["id"] = pack.Id,
                    ["name"] = pack.Name,
                    ["series"] = pack.Series,
                    ["cost"] = pack.Cost,
                };
                if (pack.Art.Length > 0)
                    node["art"] = pack.Art;
                if (pack.UnlockWith.Length > 0)
                    node["unlockWith"] = pack.UnlockWith;
                node["title"] = Texts(pack.Titles);
                node["text"] = Texts(pack.Texts);
                node["common"] = new JsonArray(common.Select(id => (JsonNode)id).ToArray());
                node["rare"] = new JsonArray(rare.Select(id => (JsonNode)id).ToArray());
                newPacks.Add(node);
            }

            if (problems.Count > 0)
            {
                MessageBox.Show(string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string path = Path.Combine(extraCardsFolder, "packs.json");
            if (array.Count == 0 && newPacks.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return true;
            }

            var root = new JsonObject { ["replaceDefaults"] = _replaceAll, ["packs"] = array };
            if (newPacks.Count > 0)
                root["newPacks"] = newPacks;
            var options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(path, root.ToJsonString(options) + "\n", new UTF8Encoding(false));
            return true;
        }

        private static JsonObject Texts(Dictionary<string, string> texts)
        {
            var node = new JsonObject();
            foreach (string language in Languages)
                if (texts.TryGetValue(language, out var text) && text.Trim().Length > 0)
                    node[language] = text;
            return node;
        }

        private static List<int> ParseIds(string text, List<string> problems, string pack)
        {
            var ids = new List<int>();
            foreach (string token in text.Split([',', ' ', ';', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(token, out int id) || id < MinId || id > MaxId)
                    problems.Add($"Pack {pack}: \"{token}\" is not a card id ({MinId} to {MaxId}).");
                else if (!ids.Contains(id))
                    ids.Add(id);
            }
            return ids;
        }

        private static JsonNode? ReadRoot(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                return JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read {path}:\n{ex.Message}", "Packs", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return null;
            }
        }

        private static Dictionary<string, (string Common, string Rare, bool Replace)> ReadAdditions(string path, out bool replaceAll)
        {
            replaceAll = false;
            var result = new Dictionary<string, (string, string, bool)>();
            var root = ReadRoot(path);
            var array = root as JsonArray ?? root?["packs"] as JsonArray;
            if (array == null)
                return result;

            replaceAll = root is JsonObject rootObject && rootObject["replaceDefaults"] is JsonValue all && all.TryGetValue<bool>(out var replaceValue) && replaceValue;

            foreach (var node in array.OfType<JsonObject>())
            {
                if (node["pack"] is not JsonValue name || !name.TryGetValue<string>(out var packName))
                    continue;

                bool replace = node["replace"] is JsonValue flag && flag.TryGetValue<bool>(out var flagValue) ? flagValue : replaceAll;
                result[packName] = (Join(node["common"]), Join(node["rare"]), replace);
            }
            return result;
        }

        private static List<PackEntry> ReadNewPacks(string path)
        {
            var result = new List<PackEntry>();
            if (ReadRoot(path) is not JsonObject root || root["newPacks"] is not JsonArray array)
                return result;
            foreach (var node in array.OfType<JsonObject>())
            {
                var entry = new PackEntry
                {
                    IsNew = true,
                    Id = Int(node["id"]) ?? 0,
                    Name = Str(node["name"]),
                    Series = Int(node["series"]) ?? 0,
                    Cost = Int(node["cost"]) ?? 200,
                    Art = Str(node["art"]),
                    UnlockWith = Str(node["unlockWith"]),
                    ExtraCommon = Join(node["common"]),
                    ExtraRare = Join(node["rare"]),
                };
                ReadTexts(node["title"], entry.Titles);
                ReadTexts(node["text"], entry.Texts);
                result.Add(entry);
            }
            return result;
        }

        private static void ReadTexts(JsonNode? node, Dictionary<string, string> into)
        {
            if (node is JsonValue single && single.TryGetValue<string>(out var text))
                into["E"] = text;
            else if (node is JsonObject languages)
                foreach (var (language, value) in languages)
                    if (value is JsonValue v && v.TryGetValue<string>(out var t))
                        into[language.ToUpperInvariant()] = t;
        }

        private static int? Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<int>(out int number) ? number : null;

        private static string Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

        private static string Join(JsonNode? node) =>
            node is JsonArray array ? string.Join(", ", array.Select(item => item?.ToString())) : "";

        // ---- new packs ----

        private int NextFreeId()
        {
            for (int id = 0; id < PackSlots; id++)
                if (!_gameIds.Contains(id) && !_packs.Any(p => p.IsNew && p.Id == id))
                    return id;
            return -1;
        }

        private void AddNewPack()
        {
            int id = NextFreeId();
            if (id < 0)
            {
                MessageBox.Show("All 128 pack ids are used.", "Packs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string name = $"custom_{id}";
            var pack = new PackEntry
            {
                IsNew = true, Id = id, Name = name, Art = _gamePackNames.FirstOrDefault() ?? "",
                Titles = { ["E"] = "New Pack" }, Texts = { ["E"] = "" },
            };
            _packs.Add(pack);
            _list.Items.Add(pack);
            _list.SelectedIndex = _list.Items.Count - 1;
        }

        private void RemoveNewPack()
        {
            if (Selected is not { IsNew: true } pack)
            {
                MessageBox.Show("Pick a new pack to remove.", "New packs", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            int index = _list.SelectedIndex;
            _packs.RemoveAt(index);
            _list.Items.RemoveAt(index);
            if (_list.Items.Count > 0)
                _list.SelectedIndex = Math.Min(index, _list.Items.Count - 1);
            else
                BindSelected();
        }

        private void SetEditorEnabled(bool enabled) => _common.Enabled = _rare.Enabled = _replace.Enabled = enabled;

        private void BindSelected()
        {
            // Committing a change swaps the list item, which raises this again: don't rebind (that would reset the box being typed in).
            if (_binding)
                return;

            var pack = Selected;
            SetEditorEnabled(pack != null);
            _newBox.Visible = pack?.IsNew == true;
            _replace.Visible = false;   // only meant something for the game's packs, which are edited on the Packs page now
            if (pack == null)
            {
                _info.Text = "No new packs yet: New pack adds one. (The cards in the game's own packs are edited on the Packs page.)" +
                             (_kept.Count > 0 ? $" packs.json also changes {_kept.Count} of the game's packs; that is kept as it is." : "");
                return;
            }

            _binding = true;
            try
            {
                _common.Text = pack.ExtraCommon;
                _rare.Text = pack.ExtraRare;
                _replace.Checked = pack.Replace;
                if (pack.IsNew)
                {
                    lblCommon.Text = "Common cards (card ids, separated by commas or spaces):";
                    lblRare.Text = "Rare cards:";
                    _info.Text = $"A new pack in slot {pack.Id}. Yu-Gi-Oh-BetterCardShop adds it to the shop tab you pick.";
                    _name.Text = pack.Name;
                    _id.Value = Math.Clamp(pack.Id, 0, PackSlots - 1);
                    _series.SelectedIndex = Math.Clamp(pack.Series, 0, _series.Items.Count - 1);
                    _cost.Value = Math.Clamp(pack.Cost, 0, (int)_cost.Maximum);
                    int art = _gamePackNames.IndexOf(pack.Art);
                    _art.SelectedIndex = art >= 0 ? art + 1 : 0;
                    int unlock = pack.UnlockWith.Length == 0 ? 0 : pack.UnlockWith.Equals("never", StringComparison.OrdinalIgnoreCase) ? 1
                        : _gamePackNames.IndexOf(pack.UnlockWith) is int at and >= 0 ? at + 2 : 0;
                    _unlock.SelectedIndex = unlock;
                    for (int i = 0; i < Languages.Length; i++)
                    {
                        _texts.Rows[i].Cells[1].Value = pack.Titles.GetValueOrDefault(Languages[i], "");
                        _texts.Rows[i].Cells[2].Value = pack.Texts.GetValueOrDefault(Languages[i], "");
                    }
                }
                else
                {
                    lblCommon.Text = "Extra common cards (card ids, separated by commas or spaces):";
                    lblRare.Text = "Extra rare cards:";
                    _info.Text = pack.Common + pack.Rare > 0
                        ? $"{pack.Title}: the game has {pack.Common} common and {pack.Rare} rare cards in this pack."
                        : $"{pack.Title}";
                }
            }
            finally { _binding = false; }
        }

        private void Commit()
        {
            var pack = Selected;
            if (_binding || pack == null)
                return;

            pack.ExtraCommon = _common.Text;
            pack.ExtraRare = _rare.Text;
            pack.Replace = _replace.Checked;
            if (pack.IsNew)
            {
                pack.Name = _name.Text.Trim();
                pack.Id = (int)_id.Value;
                pack.Series = Math.Max(0, _series.SelectedIndex);
                pack.Cost = (int)_cost.Value;
                pack.Art = _art.SelectedIndex > 0 ? _gamePackNames[_art.SelectedIndex - 1] : "";
                pack.UnlockWith = _unlock.SelectedIndex switch
                {
                    <= 0 => "",
                    1 => "never",
                    int i => _gamePackNames[i - 2],
                };
                for (int i = 0; i < Languages.Length; i++)
                {
                    pack.Titles[Languages[i]] = _texts.Rows[i].Cells[1].Value as string ?? "";
                    pack.Texts[Languages[i]] = _texts.Rows[i].Cells[2].Value as string ?? "";
                }
            }

            _binding = true;
            try { _list.Items[_list.SelectedIndex] = pack; }
            finally { _binding = false; }
        }
    }
}
