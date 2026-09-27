using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using PackDef;
using StartingCollection;
using Types;

namespace WolfEx
{
    /// <summary>
    /// Yu-Gi-Oh-Ex/packs.json: cards added to the shop's reward packs. The pack list comes from the game's own
    /// packdefdata; nothing in the game files is changed, the plugin adds the cards when the game starts.
    /// </summary>
    internal sealed partial class PacksPanel : UserControl, IContentPanel
    {
        private const int MinId = 1;
        private const int MaxId = 19999;

        private sealed class PackEntry
        {
            public string Name = "";
            public string Title = "";
            public int Common;         // cards the game has in the pack
            public int Rare;
            public string ExtraCommon = "";
            public string ExtraRare = "";
            public bool Replace;       // the lists are the pack's whole contents instead of additions

            public bool HasChanges => ExtraCommon.Trim().Length > 0 || ExtraRare.Trim().Length > 0;
            public override string ToString() => (HasChanges ? "* " : "  ") + $"{Name}  -  {Title}";
        }

        private readonly List<PackEntry> _packs = [];
        private bool _replaceAll; // packs.json "replaceDefaults", kept as it was read
        private bool _binding;

        public string Title => "Packs";

        public PacksPanel()
        {
            InitializeComponent();
            SetEditorEnabled(false);
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

        public void LoadFrom(string extraCardsFolder, string gameFolder)
        {
            _packs.Clear();
            _list.Items.Clear();

            var additions = ReadAdditions(Path.Combine(extraCardsFolder, "packs.json"), out _replaceAll);

            try
            {
                var files = GameFiles.FromGameFolder(gameFolder);
                string? defPath = files.Find(Path.Combine("main", "packdefdata_E.bin"));
                string? archive = files.Find("packs.zib");

                if (defPath != null)
                {
                    if (archive != null)
                        ZIB.Load(archive);

                    foreach (var record in PackDefFile.Load(defPath).Records.Where(record => record.IsReward).OrderBy(record => record.Series).ThenBy(record => record.Id))
                    {
                        var entry = new PackEntry { Name = record.Name, Title = record.Title };

                        if (archive != null)
                        {
                            using var stream = ZIB.Get_SpecificItemFromArchive(record.ContentsFile);
                            if (stream != null)
                            {
                                var contents = PackContents.Parse(stream.ToArray());
                                entry.Common = contents.Common.Count;
                                entry.Rare = contents.Rare.Count;
                            }
                        }

                        if (additions.TryGetValue(record.Name, out var extra))
                        {
                            entry.ExtraCommon = extra.Common;
                            entry.ExtraRare = extra.Rare;
                            entry.Replace = extra.Replace;
                            additions.Remove(record.Name);
                        }
                        _packs.Add(entry);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read the game's packs:\n{ex.Message}", "Packs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            // Packs in packs.json the game data does not list are kept, so nothing is lost on save.
            foreach (var (name, extra) in additions)
                _packs.Add(new PackEntry { Name = name, Title = "(not in the game data)", ExtraCommon = extra.Common, ExtraRare = extra.Rare, Replace = extra.Replace });

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

            foreach (var pack in _packs.Where(pack => pack.HasChanges))
            {
                var common = ParseIds(pack.ExtraCommon, problems, pack.Name);
                var rare = ParseIds(pack.ExtraRare, problems, pack.Name);
                array.Add(new JsonObject
                {
                    ["pack"] = pack.Name,
                    ["common"] = new JsonArray(common.Select(id => (JsonNode)id).ToArray()),
                    ["rare"] = new JsonArray(rare.Select(id => (JsonNode)id).ToArray()),
                    ["replace"] = pack.Replace,
                });
            }

            if (problems.Count > 0)
            {
                MessageBox.Show(string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string path = Path.Combine(extraCardsFolder, "packs.json");
            if (array.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return true;
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, new JsonObject { ["replaceDefaults"] = _replaceAll, ["packs"] = array }.ToJsonString(options) + "\n", new UTF8Encoding(false));
            return true;
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

        private static Dictionary<string, (string Common, string Rare, bool Replace)> ReadAdditions(string path, out bool replaceAll)
        {
            replaceAll = false;
            var result = new Dictionary<string, (string, string, bool)>();
            if (!File.Exists(path))
                return result;

            try
            {
                var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not read {path}:\n{ex.Message}", "Packs", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return result;
        }

        private static string Join(JsonNode? node) =>
            node is JsonArray array ? string.Join(", ", array.Select(item => item?.ToString())) : "";

        private void SetEditorEnabled(bool enabled) => _common.Enabled = _rare.Enabled = _replace.Enabled = enabled;

        private void BindSelected()
        {
            // Committing a change swaps the list item, which raises this again: don't rebind (that would reset the box being typed in).
            if (_binding)
                return;

            var pack = Selected;
            SetEditorEnabled(pack != null);
            if (pack == null)
            {
                _info.Text = "The game's packs are listed when the game folder is set.";
                return;
            }

            _binding = true;
            try
            {
                _common.Text = pack.ExtraCommon;
                _rare.Text = pack.ExtraRare;
                _replace.Checked = pack.Replace;
                _info.Text = pack.Common + pack.Rare > 0
                    ? $"{pack.Title}: the game has {pack.Common} common and {pack.Rare} rare cards in this pack."
                    : $"{pack.Title}";
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

            _binding = true;
            try { _list.Items[_list.SelectedIndex] = pack; }
            finally { _binding = false; }
        }
    }
}
