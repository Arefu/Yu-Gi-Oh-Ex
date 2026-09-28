using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CARD_Named;

namespace WolfEx
{
    /// <summary>
    /// Archetype code -> name for the card editor. The game's own archetypes (codes below 419) are named from the known game files
    /// (CARD_Named.bin, named by Archetypes.ps1); custom archetypes (419 up) are the ones in Yu-Gi-Oh-Ex/Archetypes.json. The vanilla
    /// engine only knows its own codes, so a custom archetype is only ever seen by our own code (the Yu-Gi-Oh-Cards hook and, later,
    /// effect scripts) - see the ygo-effects-moonshot-plan memory.
    /// </summary>
    internal static class ArchetypeCatalog
    {
        public const int FirstCustomCode = 419;

        private static string _path = "";
        private static readonly Dictionary<int, string> _custom = [];

        /// <summary>
        /// Reads the custom archetypes from Yu-Gi-Oh-Ex/Archetypes.json ({ "archetypes": [ { "code": 419, "name": "...", "names": { "E": "..." } } ] };
        /// the file also lists the game's own, which come from the library's embedded copy and are ignored here).
        /// </summary>
        public static void Load(string extraCardsFolder)
        {
            _path = Path.Combine(extraCardsFolder, "Archetypes.json");
            _custom.Clear();
            if (!File.Exists(_path))
                return;

            try
            {
                if (JsonNode.Parse(File.ReadAllText(_path))?["archetypes"] is not JsonArray entries)
                    return;
                foreach (var entry in entries.OfType<JsonObject>())
                {
                    int code = entry["code"]?.GetValue<int>() ?? 0;
                    if (code >= FirstCustomCode)
                        _custom[code] = entry["name"]?.GetValue<string>() ?? $"Archetype {code}";
                }
            }
            catch { /* a bad Archetypes.json just means no custom names */ }
        }

        public static string NameOf(int code) => _custom.TryGetValue(code, out var name) ? name : Card_Named.NameOf(code);

        /// <summary>The code of an archetype by its English name (case-insensitive); 0 when there is none.</summary>
        public static int CodeOf(string name) =>
            Codes().FirstOrDefault(code => string.Equals(NameOf(code), name, StringComparison.OrdinalIgnoreCase));

        public static IEnumerable<int> Codes() =>
            Card_Named.Names.Keys.Where(code => code > 0 && code < FirstCustomCode).Concat(_custom.Keys).Distinct().OrderBy(code => code);

        public static string Describe(IEnumerable<int> codes)
        {
            var list = codes.OrderBy(code => code).Select(code => $"{code} {NameOf(code)}").ToList();
            return list.Count == 0 ? "(none)" : string.Join(", ", list);
        }

        /// <summary>A new custom archetype: the next free code from 419 up, added to Archetypes.json (the rest of the file is kept as it is).</summary>
        public static int Add(string name)
        {
            int code = Math.Max(FirstCustomCode, _custom.Count == 0 ? 0 : _custom.Keys.Max() + 1);
            _custom[code] = name;

            JsonObject root = [];
            try { if (File.Exists(_path) && JsonNode.Parse(File.ReadAllText(_path)) is JsonObject existing) root = existing; } catch { }
            var entries = root["archetypes"] as JsonArray ?? [];
            entries.Add(new JsonObject { ["code"] = code, ["name"] = name, ["names"] = new JsonObject { ["E"] = name } });
            root["archetypes"] = entries;

            var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
            File.WriteAllText(_path, root.ToJsonString(options) + Environment.NewLine, new UTF8Encoding(false));
            return code;
        }
    }

    /// <summary>Pick any number of archetypes for a card, or make a new one.</summary>
    internal sealed class ArchetypeDialog : Form
    {
        private readonly TextBox _filter = new() { Dock = DockStyle.Top, PlaceholderText = "Filter by name or code..." };
        private readonly CheckedListBox _list = new() { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        private readonly HashSet<int> _selected;

        public List<int> Result => _selected.OrderBy(code => code).ToList();

        public ArchetypeDialog(string cardName, IEnumerable<int> current)
        {
            _selected = [.. current];
            Text = $"Archetypes - {cardName}";
            Size = new Size(460, 560);
            StartPosition = FormStartPosition.CenterParent;

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 40, Padding = new Padding(6) };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var add = new Button { Text = "New archetype...", AutoSize = true };
            buttons.Controls.AddRange([ok, cancel, add]);

            Controls.Add(_list);
            Controls.Add(_filter);
            Controls.Add(buttons);
            AcceptButton = ok;
            CancelButton = cancel;

            _filter.TextChanged += (_, _) => Fill();
            _list.ItemCheck += (_, e) =>
            {
                if (_filling)
                    return;
                int code = ((Item)_list.Items[e.Index]).Code;
                if (e.NewValue == CheckState.Checked) _selected.Add(code); else _selected.Remove(code);
            };
            add.Click += (_, _) => NewArchetype();
            Fill();
        }

        private sealed record Item(int Code, string Text) { public override string ToString() => Text; }

        private bool _filling;

        private void Fill()
        {
            string filter = _filter.Text.Trim();
            _filling = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            // Selected ones first, so what the card is in is visible without scrolling.
            foreach (int code in ArchetypeCatalog.Codes().Concat(_selected).Distinct().OrderBy(c => _selected.Contains(c) ? 0 : 1).ThenBy(c => c))
            {
                string text = $"{code} - {ArchetypeCatalog.NameOf(code)}{(code >= ArchetypeCatalog.FirstCustomCode ? "  (custom)" : "")}";
                if (filter.Length > 0 && !text.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;
                _list.Items.Add(new Item(code, text), _selected.Contains(code));
            }
            _list.EndUpdate();
            _filling = false;
        }

        private void NewArchetype()
        {
            using var prompt = new Form { Text = "New archetype", Size = new Size(380, 130), StartPosition = FormStartPosition.CenterParent, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false };
            var box = new TextBox { Left = 10, Top = 12, Width = 340 };
            var ok = new Button { Text = "Add", Left = 190, Top = 44, DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", Left = 275, Top = 44, DialogResult = DialogResult.Cancel };
            prompt.Controls.AddRange([box, ok, cancel]);
            prompt.AcceptButton = ok;
            prompt.CancelButton = cancel;
            if (prompt.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(box.Text))
                return;

            int code = ArchetypeCatalog.Add(box.Text.Trim());
            _selected.Add(code);
            Fill();
        }
    }
}
