using System.ComponentModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using StartingCollection;

namespace WolfEx
{
    /// <summary>
    /// Yu-Gi-Oh-Ex/unlocks.json: cards the player owns at least N copies of, from the start and in every save.
    /// Works for the game's own cards and for cards added in cards.json.
    /// </summary>
    internal sealed partial class UnlocksPanel : UserControl, IContentPanel
    {
        // The game's own card ids run from 3900 to 14968; the save can hold ids up to 19999.
        private const int MinId = 1;
        private const int MaxId = 19999;

        private sealed class Row
        {
            public int Id { get; set; }
            public string Name => WolfX.Types.CardCatalog.NameOf(Id);
            public int Copies { get; set; } = 3;
        }

        private readonly BindingList<Row> _rows = [];
        private string _gameFolder = "";

        public string Title => "Unlocks";

        public UnlocksPanel()
        {
            InitializeComponent();
            _grid.DataSource = _rows;
            _info.Text = "Ids 3900-14968 are the game's cards, 15300-19999 are new cards. \"Add from game data\" lists what a new profile starts with.";
        }

        private void btnAddByName_Click(object? sender, EventArgs e) => AddByName();

        private void btnAddById_Click(object? sender, EventArgs e) => _rows.Add(new Row { Id = MinId });

        private void btnRemove_Click(object? sender, EventArgs e) => RemoveSelected();

        private void btnAddFromGame_Click(object? sender, EventArgs e) => ImportFromGame();

        private void btnImport_Click(object? sender, EventArgs e) => ImportFile();

        private void AddByName()
        {
            var database = WolfX.Types.CardCatalog.Get(this);
            if (database == null)
                return;

            using var picker = new WolfX.Types.CardPickerDialog(database, "Add cards to the unlocks");
            if (picker.ShowDialog(this) != DialogResult.OK)
                return;

            _grid.EndEdit();
            foreach (var (card, copies) in picker.Result)
            {
                var row = _rows.FirstOrDefault(existing => existing.Id == card.Id);
                if (row == null)
                    _rows.Add(new Row { Id = card.Id, Copies = Math.Min(copies, 3) });
                else
                    row.Copies = Math.Min(copies, 3);
            }
            _rows.ResetBindings();
        }

        public void LoadFrom(string extraCardsFolder, string gameFolder)
        {
            _gameFolder = gameFolder;
            _rows.Clear();

            string path = Path.Combine(extraCardsFolder, "unlocks.json");
            if (!File.Exists(path))
                return;

            try
            {
                string json = File.ReadAllText(path);
                foreach (var row in Read(json))
                    _rows.Add(row);

                _replace.Checked = !(JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }) is JsonObject root
                    && root["replaceDefaults"] is JsonValue flag && flag.TryGetValue<bool>(out bool value) && !value);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read {path}:\n{ex.Message}", "Unlocks", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public bool SaveTo(string extraCardsFolder)
        {
            _grid.EndEdit();

            var problems = new List<string>();
            foreach (var group in _rows.GroupBy(row => row.Id).Where(g => g.Count() > 1))
                problems.Add($"Card {group.Key} is listed more than once.");
            foreach (var row in _rows)
            {
                if (row.Id < MinId || row.Id > MaxId)
                    problems.Add($"Card {row.Id}: the id must be between {MinId} and {MaxId}.");
                if (row.Copies < 0 || row.Copies > 3)
                    problems.Add($"Card {row.Id}: copies must be 0 to 3.");
            }

            if (problems.Count > 0)
            {
                MessageBox.Show(string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var array = new JsonArray();
            foreach (var row in _rows.OrderBy(row => row.Id))
                array.Add(new JsonObject { ["id"] = row.Id, ["copies"] = row.Copies });

            var root = new JsonObject { ["replaceDefaults"] = _replace.Checked, ["cards"] = array };
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(extraCardsFolder, "unlocks.json"), root.ToJsonString(options) + "\n", new UTF8Encoding(false));
            return true;
        }

        private static IEnumerable<Row> Read(string json)
        {
            var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            var array = root as JsonArray ?? root?["cards"] as JsonArray ?? throw new InvalidDataException("expected a \"cards\" array");

            foreach (var node in array.OfType<JsonObject>())
            {
                if (node["id"] is JsonValue id && id.TryGetValue<int>(out int idValue))
                    yield return new Row { Id = idValue, Copies = node["copies"] is JsonValue c && c.TryGetValue<int>(out int copies) ? copies : 3 };
            }
        }

        private void Merge(IEnumerable<Row> incoming)
        {
            foreach (var row in incoming)
            {
                var existing = _rows.FirstOrDefault(r => r.Id == row.Id);
                if (existing == null)
                    _rows.Add(row);
                else
                    existing.Copies = Math.Max(existing.Copies, row.Copies);
            }
            _grid.Refresh();
        }

        private void RemoveSelected()
        {
            foreach (var row in _grid.SelectedRows.Cast<DataGridViewRow>().Select(r => (Row)r.DataBoundItem).ToList())
                _rows.Remove(row);
        }

        /// <summary>Lists what a new profile starts with, worked out from the game's own files.</summary>
        private void ImportFromGame()
        {
            if (string.IsNullOrEmpty(_gameFolder))
            {
                MessageBox.Show("Pick the game folder first.", "Unlocks", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                var data = StartingCollectionBuilder.Build(GameFiles.FromGameFolder(_gameFolder));
                if (data.Cards.Count == 0)
                {
                    MessageBox.Show(string.Join("\n", data.Warnings.DefaultIfEmpty("Nothing was found.")), "Unlocks", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Merge(data.Cards.Select(card => new Row { Id = card.Id, Copies = card.Copies }));
                _info.Text = $"Added {data.Cards.Count} cards from {Path.GetFileName(data.Sources["deckdata"])} and the starter decks.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Unlocks", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Accepts unlocks.json or a starting_collection.json export.</summary>
        private void ImportFile()
        {
            using var dialog = new OpenFileDialog { Filter = "JSON (*.json)|*.json" };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            try
            {
                string json = File.ReadAllText(dialog.FileName);
                if (json.Contains("\"StarterDecks\""))
                    Merge(StartingCollectionData.FromJson(json).Cards.Select(card => new Row { Id = card.Id, Copies = card.Copies }));
                else
                    Merge(Read(json));
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Unlocks", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

    }
}
