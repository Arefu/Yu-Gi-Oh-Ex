using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using StartingCollection;

namespace WolfX.Types
{
    /// <summary>
    /// The cards a new profile owns. Built from the game's own data (starter decks), editable, and exportable either
    /// as starting_collection.json or as the Yu-Gi-Oh-Ex/unlocks.json the Yu-Gi-Oh-MoreCards plugin reads.
    /// </summary>
    public sealed partial class StartingCollectionPage : UserControl
    {
        private sealed class CardRow
        {
            [DisplayName("Konami id")] public int Id { get; set; }
            [DisplayName("Card")] public string Name { get; set; } = "";
            [DisplayName("Copies (1-3)")] public int Copies { get; set; }
        }

        private BindingList<CardRow> _rows = [];
        private StartingCollectionData? _data;

        public StartingCollectionPage()
        {
            InitializeComponent();
        }

        // ---- events wired in the designer ----

        private void btnBrowse_Click(object? sender, EventArgs e) => Browse();

        private void btnBuild_Click(object? sender, EventArgs e) => Build();

        private void btnAddByName_Click(object? sender, EventArgs e) => AddByName();

        private void btnRemove_Click(object? sender, EventArgs e) => RemoveSelected();

        private void btnLoadJson_Click(object? sender, EventArgs e) => LoadJson();

        private void btnSaveJson_Click(object? sender, EventArgs e) => SaveJson();

        private void btnWriteUnlocks_Click(object? sender, EventArgs e) => WriteUnlocks();

        // ---- logic ----

        private void Browse()
        {
            using var dialog = new FolderBrowserDialog { Description = "Select the game folder (the one containing YuGiOh.exe)", UseDescriptionForTitle = true, SelectedPath = _gameFolder.Text };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            _gameFolder.Text = dialog.SelectedPath;
            CardCatalog.Use(dialog.SelectedPath);
        }

        private void AddByName()
        {
            using var picker = new CardPickerDialog(CardCatalog.Get(this), "Add cards to the starting collection");
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;

            _cards.EndEdit();
            foreach (var (card, copies) in picker.Result)
            {
                var row = _rows.FirstOrDefault(existing => existing.Id == card.Id);
                if (row == null)
                    _rows.Add(new CardRow { Id = card.Id, Name = card.Name, Copies = Math.Min(copies, StartingCollectionBuilder.MaxCopies) });
                else
                    row.Copies = Math.Min(copies, StartingCollectionBuilder.MaxCopies);
            }
            _rows.ResetBindings();
        }

        private void RemoveSelected()
        {
            foreach (var row in _cards.SelectedRows.Cast<DataGridViewRow>().Select(gridRow => (CardRow)gridRow.DataBoundItem).ToList())
                _rows.Remove(row);
        }

        private void Build()
        {
            if (!Directory.Exists(_gameFolder.Text))
            {
                MessageBox.Show("Pick the game folder first.", "Starting collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                CardCatalog.Use(_gameFolder.Text);
                Show(StartingCollectionBuilder.Build(GameFiles.FromGameFolder(_gameFolder.Text)));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not build the collection:\n{ex.Message}", "Starting collection", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Show(StartingCollectionData data)
        {
            _data = data;
            _rows = new BindingList<CardRow>(data.Cards.OrderBy(card => card.Id).Select(card => new CardRow { Id = card.Id, Name = CardCatalog.NameOf(card.Id), Copies = card.Copies }).ToList())
            {
                AllowNew = true,
                AllowRemove = true,
            };
            _cards.DataSource = _rows;
            _cards.Columns[nameof(CardRow.Name)]!.ReadOnly = true;

            _decks.Items.Clear();
            foreach (var deck in data.StarterDecks)
                _decks.Items.Add($"{deck.Id}  {deck.Title}  ({deck.Main.Count}/{deck.Extra.Count}/{deck.Side.Count})");

            _info.Text = $"{_rows.Count} cards, {_rows.Sum(row => row.Copies)} copies, {data.StartingPoints} starting points." +
                (data.Warnings.Count > 0 ? $"  Warnings: {string.Join("; ", data.Warnings)}" : "");
        }

        /// <summary>The grid contents, or null (after telling the user) if a row is invalid.</summary>
        private List<StartingCard>? Collect()
        {
            _cards.EndEdit();
            var cards = new List<StartingCard>();
            var seen = new HashSet<int>();
            foreach (var row in _rows)
            {
                if (row.Id < 1 || row.Id > 19999 || row.Copies < 1 || row.Copies > StartingCollectionBuilder.MaxCopies)
                {
                    MessageBox.Show($"Card {row.Id}: the id must be 1 to 19999 and the copies 1 to {StartingCollectionBuilder.MaxCopies}.", "Starting collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                if (!seen.Add(row.Id))
                {
                    MessageBox.Show($"Card {row.Id} is listed twice.", "Starting collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return null;
                }
                cards.Add(new StartingCard { Id = row.Id, Copies = row.Copies });
            }
            return cards;
        }

        private void LoadJson()
        {
            using var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json", Title = "Open starting_collection.json or unlocks.json" };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                string text = File.ReadAllText(dialog.FileName);
                var root = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
                if (root is JsonObject obj && obj["cards"] is JsonArray array && array.Count > 0 && array[0] is JsonObject first && first.ContainsKey("copies") && !first.ContainsKey("internalId"))
                {
                    // unlocks.json: {"replaceDefaults": .., "cards": [{"id", "copies"}]}
                    var data = new StartingCollectionData();
                    foreach (var node in array.OfType<JsonObject>())
                        data.Cards.Add(new StartingCard { Id = node["id"]!.GetValue<int>(), Copies = node["copies"]!.GetValue<int>() });
                    if (obj["replaceDefaults"] is JsonValue replace && replace.TryGetValue<bool>(out var flag))
                        _replaceDefaults.Checked = flag;
                    Show(data);
                }
                else
                    Show(StartingCollectionData.FromJson(text));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read the file:\n{ex.Message}", "Starting collection", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveJson()
        {
            var cards = Collect();
            if (cards == null)
                return;

            using var dialog = new SaveFileDialog { Filter = "JSON (*.json)|*.json", FileName = "starting_collection.json" };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            var data = _data ?? new StartingCollectionData();
            data.Cards = cards;
            File.WriteAllText(dialog.FileName, data.ToJson());
        }

        private void WriteUnlocks()
        {
            var cards = Collect();
            if (cards == null)
                return;

            if (!Directory.Exists(_gameFolder.Text))
            {
                MessageBox.Show("Pick the game folder first.", "Starting collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string folder = Path.Combine(_gameFolder.Text, "Yu-Gi-Oh-Ex");
            Directory.CreateDirectory(folder);
            var array = new JsonArray();
            foreach (var card in cards.OrderBy(card => card.Id))
                array.Add(new JsonObject { ["id"] = card.Id, ["copies"] = card.Copies });

            var root = new JsonObject { ["replaceDefaults"] = _replaceDefaults.Checked, ["cards"] = array };
            File.WriteAllText(Path.Combine(folder, "unlocks.json"), root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            MessageBox.Show($"Wrote {cards.Count} cards to {Path.Combine(folder, "unlocks.json")}", "Starting collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
