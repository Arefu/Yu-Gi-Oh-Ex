using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WolfEx
{
    /// <summary>
    /// Yu-Gi-Oh-Ex/cards.json: new cards, and their art next to it. Field names and values are the ones the
    /// Yu-Gi-Oh-MoreCards plugin reads (Card.h / Card.cpp).
    /// </summary>
    internal sealed class CardsPanel : UserControl, IContentPanel
    {
        private static readonly (string Name, int Value)[] Kinds = { ("Normal", 0x0), ("Effect", 0x1), ("Spell", 0xD), ("Trap", 0xE) };
        private static readonly (string Name, int Value)[] Attributes =
        {
            ("Special", 0), ("Light", 1), ("Dark", 2), ("Water", 3), ("Fire", 4),
            ("Earth", 5), ("Wind", 6), ("Divine", 7), ("Spell", 8), ("Trap", 9),
        };
        private static readonly (string Name, int Value)[] Types =
        {
            ("Dragon", 0x1), ("Zombie", 0x2), ("Fiend", 0x3), ("Pyro", 0x4), ("SeaSerpent", 0x5), ("Rock", 0x6),
            ("Machine", 0x7), ("Fish", 0x8), ("Dinosaur", 0x9), ("Insect", 0xA), ("Beast", 0xB), ("BeastWarrior", 0xC),
            ("Plant", 0xD), ("Aqua", 0xE), ("Warrior", 0xF), ("WingedBeast", 0x10), ("Fairy", 0x11),
            ("Spellcaster", 0x12), ("Thunder", 0x13), ("Reptile", 0x14), ("Psychic", 0x15), ("Wyrm", 0x16),
            ("Cyberse", 0x17), ("DivineBeast", 0x18), ("CreatorGod", 0x19), ("Spell", 0x1E), ("Trap", 0x1F),
        };
        private static readonly (string Name, int Value)[] Icons =
        {
            ("Normal", 0), ("Counter", 1), ("Field", 2), ("Equip", 3), ("Continuous", 4), ("QuickPlay", 5), ("Ritual", 6),
        };
        private static readonly (string Name, int Value)[] Limitations =
        {
            ("Forbidden", 0), ("Limited", 1), ("SemiLimited", 2), ("Unlimited", 3),
        };

        // The plugin accepts ids from the first one after the game's own to the last one the save can hold.
        private const int MinId = 14969;
        private const int MaxId = 19999;

        private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

        private sealed class CardModel
        {
            public int Id = MinId;
            public string Name = "New Card";
            public string Description = "";
            public string Image = "";            // file name inside the Yu-Gi-Oh-Ex folder
            public string? PendingArt;           // newly picked file, copied on save
            public string Kind = "Effect";
            public string Type = "Warrior";
            public string Attribute = "Light";
            public string Icon = "Normal";
            public int Level = 4;
            public int Atk;
            public int Def;
            public string Limitation = "Unlimited";
            public int Copies = 3;

            public bool IsSpellOrTrap => Kind is "Spell" or "Trap";
            public override string ToString() => $"{Id} - {Name}";
        }

        private readonly List<CardModel> _cards = [];
        private string _folder = "";
        private bool _binding;

        private readonly ListBox _list = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly NumericUpDown _id = new() { Minimum = MinId, Maximum = MaxId, Value = MinId };
        private readonly TextBox _name = new();
        private readonly TextBox _desc = new() { Multiline = true, Height = 90, ScrollBars = ScrollBars.Vertical };
        private readonly ComboBox _kind = MakeCombo(Kinds);
        private readonly ComboBox _type = MakeCombo(Types);
        private readonly ComboBox _attribute = MakeCombo(Attributes);
        private readonly ComboBox _icon = MakeCombo(Icons);
        private readonly NumericUpDown _level = new() { Minimum = 1, Maximum = 12, Value = 4 };
        private readonly NumericUpDown _atk = new() { Minimum = 0, Maximum = 9990, Increment = 100 };
        private readonly NumericUpDown _def = new() { Minimum = 0, Maximum = 9990, Increment = 100 };
        private readonly ComboBox _limitation = MakeCombo(Limitations);
        private readonly NumericUpDown _copies = new() { Minimum = 0, Maximum = 3, Value = 3 };
        private readonly TextBox _artPath = new() { ReadOnly = true, Dock = DockStyle.Fill };
        private readonly PictureBox _preview = new() { SizeMode = PictureBoxSizeMode.Zoom, BorderStyle = BorderStyle.FixedSingle, Size = new Size(200, 200) };
        private readonly Label _artInfo = new() { AutoSize = true };

        public string Title => "New cards";

        public CardsPanel()
        {
            var left = new Panel { Dock = DockStyle.Fill };
            var leftButtons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 34, Padding = new Padding(2) };
            leftButtons.Controls.Add(MakeButton("Add", (_, _) => AddCard(null)));
            leftButtons.Controls.Add(MakeButton("Duplicate", (_, _) => DuplicateCard()));
            leftButtons.Controls.Add(MakeButton("Delete", (_, _) => DeleteCard()));
            left.Controls.Add(_list);
            left.Controls.Add(leftButtons);

            var form = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };
            form.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            form.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            AddRow(form, "Card ID:", _id);
            AddRow(form, "Name:", _name);
            AddRow(form, "Description:", _desc);
            AddRow(form, "Kind:", _kind);
            AddRow(form, "Type:", _type);
            AddRow(form, "Attribute:", _attribute);
            AddRow(form, "Icon (Spell/Trap):", _icon);
            AddRow(form, "Level:", _level);
            AddRow(form, "ATK:", _atk);
            AddRow(form, "DEF:", _def);
            AddRow(form, "Limitation:", _limitation);
            AddRow(form, "Owned from the start:", _copies);

            var artRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2 };
            artRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            artRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            artRow.Controls.Add(_artPath, 0, 0);
            artRow.Controls.Add(MakeButton("Choose art...", (_, _) => ChooseArt()), 1, 0);
            AddRow(form, "Art (png / jpg):", artRow);
            AddRow(form, "", _preview);
            AddRow(form, "", _artInfo);

            var right = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            right.Controls.Add(form);

            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, SplitterDistance = 260 };
            split.Panel1.Controls.Add(left);
            split.Panel2.Controls.Add(right);
            Controls.Add(split);

            _list.SelectedIndexChanged += (_, _) => BindSelected();
            foreach (var control in new Control[] { _id, _name, _desc, _kind, _type, _attribute, _icon, _level, _atk, _def, _limitation, _copies })
            {
                switch (control)
                {
                    case NumericUpDown number: number.ValueChanged += (_, _) => Commit(); break;
                    case ComboBox combo: combo.SelectedIndexChanged += (_, _) => { if (combo == _kind) ApplyKindRules(); Commit(); }; break;
                    default: control.TextChanged += (_, _) => Commit(); break;
                }
            }
            SetEditorEnabled(false);
        }

        // ---------------------------------------------------------------- IContentPanel

        public void LoadFrom(string extraCardsFolder, string gameFolder)
        {
            _folder = extraCardsFolder;
            _cards.Clear();
            _list.Items.Clear();

            string path = Path.Combine(extraCardsFolder, "cards.json");
            if (File.Exists(path))
            {
                try
                {
                    var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
                    var array = root as JsonArray ?? root?["cards"] as JsonArray ?? throw new InvalidDataException("expected a \"cards\" array");
                    foreach (var node in array.OfType<JsonObject>())
                        _cards.Add(ReadCard(node));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not read {path}:\n{ex.Message}", "New cards", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    _cards.Clear();
                }
            }

            foreach (var card in _cards)
                _list.Items.Add(card);

            if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
            else
                BindSelected();
        }

        public bool SaveTo(string extraCardsFolder)
        {
            var problems = new List<string>();
            foreach (var group in _cards.GroupBy(card => card.Id).Where(g => g.Count() > 1))
                problems.Add($"ID {group.Key} is used by more than one card.");
            foreach (var card in _cards)
            {
                if (card.Id < MinId || card.Id > MaxId)
                    problems.Add($"\"{card.Name}\": the ID must be between {MinId} and {MaxId}.");
                if (string.IsNullOrWhiteSpace(card.Name))
                    problems.Add($"ID {card.Id}: the name is empty.");
                if (!card.IsSpellOrTrap && (card.Atk % 10 != 0 || card.Def % 10 != 0))
                    problems.Add($"\"{card.Name}\": ATK and DEF must be multiples of 10.");
            }

            if (problems.Count > 0)
            {
                MessageBox.Show(string.Join("\n", problems), "Fix these before saving", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            try
            {
                foreach (var card in _cards.Where(card => card.PendingArt != null))
                {
                    // The game decodes PNG and JPEG by their header, so the picture is copied as it is.
                    string extension = Path.GetExtension(card.PendingArt!).ToLowerInvariant();
                    string name = card.Id + (extension == ".jpeg" ? ".jpg" : extension);

                    foreach (string old in ImageExtensions.Select(ext => Path.Combine(extraCardsFolder, card.Id + ext)))
                    {
                        if (File.Exists(old) && !string.Equals(Path.GetFileName(old), name, StringComparison.OrdinalIgnoreCase))
                            File.Delete(old);
                    }

                    string target = Path.Combine(extraCardsFolder, name);
                    if (!string.Equals(Path.GetFullPath(card.PendingArt!), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                        File.Copy(card.PendingArt!, target, overwrite: true);

                    card.Image = name;
                    card.PendingArt = null;
                }

                var array = new JsonArray();
                foreach (var card in _cards.OrderBy(card => card.Id))
                    array.Add(WriteCard(card));

                var options = new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                File.WriteAllText(Path.Combine(extraCardsFolder, "cards.json"), new JsonObject { ["cards"] = array }.ToJsonString(options) + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                MessageBox.Show("Save failed:\n" + ex.Message, "New cards", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            if (Selected is { } selected)
                RefreshArt(selected);
            return true;
        }

        // ---------------------------------------------------------------- layout helpers

        private static ComboBox MakeCombo((string Name, int Value)[] items)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            box.Items.AddRange(items.Select(item => (object)item.Name).ToArray());
            box.SelectedIndex = 0;
            return box;
        }

        private static Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button { Text = text, AutoSize = true };
            button.Click += onClick;
            return button;
        }

        private static void AddRow(TableLayoutPanel table, string label, Control control)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Padding = new Padding(0, 6, 8, 0) }, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            control.Margin = new Padding(3);
            table.Controls.Add(control, 1, row);
        }

        // ---------------------------------------------------------------- model <-> UI

        private CardModel? Selected => _list.SelectedIndex >= 0 ? _cards[_list.SelectedIndex] : null;

        private void SetEditorEnabled(bool enabled)
        {
            foreach (var control in new Control[] { _id, _name, _desc, _kind, _type, _attribute, _icon, _level, _atk, _def, _limitation, _copies })
                control.Enabled = enabled;

            if (enabled)
                ApplyKindRules();
        }

        /// <summary>Spells and traps have no type, attribute, level or ATK/DEF: the plugin fills those in itself.</summary>
        private void ApplyKindRules()
        {
            bool spellOrTrap = (string?)_kind.SelectedItem is "Spell" or "Trap";
            _type.Enabled = _attribute.Enabled = _level.Enabled = _atk.Enabled = _def.Enabled = !spellOrTrap;
            _icon.Enabled = spellOrTrap;
        }

        private void BindSelected()
        {
            var card = Selected;
            SetEditorEnabled(card != null);
            if (card == null)
                return;

            _binding = true;
            try
            {
                _id.Value = Math.Clamp(card.Id, MinId, MaxId);
                _name.Text = card.Name;
                _desc.Text = card.Description;
                Select(_kind, card.Kind);
                Select(_type, card.Type);
                Select(_attribute, card.Attribute);
                Select(_icon, card.Icon);
                _level.Value = Math.Clamp(card.Level, 1, 12);
                _atk.Value = Math.Clamp(card.Atk, 0, 9990);
                _def.Value = Math.Clamp(card.Def, 0, 9990);
                Select(_limitation, card.Limitation);
                _copies.Value = Math.Clamp(card.Copies, 0, 3);
                ApplyKindRules();
                RefreshArt(card);
            }
            finally { _binding = false; }
        }

        private static void Select(ComboBox box, string name)
        {
            int index = box.Items.IndexOf(name);
            box.SelectedIndex = index >= 0 ? index : 0;
        }

        private void Commit()
        {
            var card = Selected;
            if (_binding || card == null)
                return;

            card.Id = (int)_id.Value;
            card.Name = _name.Text;
            card.Description = _desc.Text;
            card.Kind = (string)_kind.SelectedItem!;
            card.Type = (string)_type.SelectedItem!;
            card.Attribute = (string)_attribute.SelectedItem!;
            card.Icon = (string)_icon.SelectedItem!;
            card.Level = (int)_level.Value;
            card.Atk = (int)_atk.Value;
            card.Def = (int)_def.Value;
            card.Limitation = (string)_limitation.SelectedItem!;
            card.Copies = (int)_copies.Value;

            if (card.IsSpellOrTrap)
                card.Attribute = card.Type = card.Kind;

            _binding = true;
            try { _list.Items[_list.SelectedIndex] = card; }
            finally { _binding = false; }
        }

        private int NextFreeId()
        {
            for (int id = MinId; id <= MaxId; id++)
            {
                if (_cards.All(card => card.Id != id))
                    return id;
            }
            return MaxId;
        }

        private void AddCard(CardModel? template)
        {
            var card = template ?? new CardModel();
            card.Id = NextFreeId();
            _cards.Add(card);
            _list.Items.Add(card);
            _list.SelectedIndex = _list.Items.Count - 1;
        }

        private void DuplicateCard()
        {
            var source = Selected;
            if (source == null)
                return;

            AddCard(new CardModel
            {
                Name = source.Name + " (copy)", Description = source.Description, Kind = source.Kind, Type = source.Type,
                Attribute = source.Attribute, Icon = source.Icon, Level = source.Level, Atk = source.Atk, Def = source.Def,
                Limitation = source.Limitation, Copies = source.Copies, PendingArt = source.PendingArt ?? ResolveArt(source),
            });
        }

        private void DeleteCard()
        {
            int index = _list.SelectedIndex;
            if (index < 0 || MessageBox.Show($"Delete \"{_cards[index].Name}\"?\nIts art file is left in the folder.", "Delete card",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _cards.RemoveAt(index);
            _list.Items.RemoveAt(index);
            if (_list.Items.Count > 0)
                _list.SelectedIndex = Math.Min(index, _list.Items.Count - 1);
            else
                BindSelected();
        }

        // ---------------------------------------------------------------- art

        private string? ResolveArt(CardModel card)
        {
            if (card.PendingArt != null)
                return card.PendingArt;

            if (string.IsNullOrEmpty(card.Image) || string.IsNullOrEmpty(_folder))
                return null;

            string path = Path.Combine(_folder, card.Image);
            return File.Exists(path) ? path : null;
        }

        private void ChooseArt()
        {
            var card = Selected;
            if (card == null)
                return;

            using var dialog = new OpenFileDialog { Title = "Choose card art", Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            card.PendingArt = dialog.FileName;
            RefreshArt(card);
        }

        private void RefreshArt(CardModel card)
        {
            var old = _preview.Image;
            _preview.Image = null;
            old?.Dispose();

            string? path = ResolveArt(card);
            if (path == null)
            {
                _artPath.Text = string.IsNullOrEmpty(card.Image) ? "(no art)" : card.Image + "  (file missing)";
                _artInfo.Text = "Without art the game shows a placeholder.";
                return;
            }

            try
            {
                // Read through a stream and copy, so the file is never left locked.
                using var stream = File.OpenRead(path);
                using var image = Image.FromStream(stream);
                _preview.Image = new Bitmap(image);
                _artPath.Text = path;
                _artInfo.Text = $"{image.Width} x {image.Height}" + (card.PendingArt != null ? "  - copied when saved" : "");
            }
            catch (Exception ex)
            {
                _artPath.Text = path;
                _artInfo.Text = "Could not read the image: " + ex.Message;
            }
        }

        // ---------------------------------------------------------------- json

        private static string ReadEnum(JsonObject json, string key, (string Name, int Value)[] table, string fallback)
        {
            if (json[key] is not JsonValue value)
                return fallback;

            if (value.TryGetValue<string>(out var text))
                return table.FirstOrDefault(item => string.Equals(item.Name, text, StringComparison.OrdinalIgnoreCase)).Name ?? fallback;

            if (value.TryGetValue<int>(out var number))
                return table.FirstOrDefault(item => item.Value == number).Name ?? fallback;

            return fallback;
        }

        private static int ReadInt(JsonObject json, string key, int fallback) =>
            json[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;

        private static string ReadString(JsonObject json, string key) =>
            json[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : "";

        private static CardModel ReadCard(JsonObject json) => new()
        {
            Id = ReadInt(json, "id", MinId),
            Name = ReadString(json, "name"),
            Description = ReadString(json, "description"),
            Image = ReadString(json, "image"),
            Kind = ReadEnum(json, "kind", Kinds, "Normal"),
            Type = ReadEnum(json, "type", Types, "Warrior"),
            Attribute = ReadEnum(json, "attribute", Attributes, "Light"),
            Icon = ReadEnum(json, "icon", Icons, "Normal"),
            Level = ReadInt(json, "level", 1),
            Atk = ReadInt(json, "atk", 0),
            Def = ReadInt(json, "def", 0),
            Limitation = ReadEnum(json, "limitation", Limitations, "Unlimited"),
            Copies = ReadInt(json, "copies", 3),
        };

        private static JsonObject WriteCard(CardModel card)
        {
            var json = new JsonObject
            {
                ["id"] = card.Id,
                ["name"] = card.Name,
                ["description"] = card.Description,
            };

            if (!string.IsNullOrEmpty(card.Image))
                json["image"] = card.Image;
            json["kind"] = card.Kind;

            if (card.IsSpellOrTrap)
            {
                if (card.Icon != "Normal")
                    json["icon"] = card.Icon;
            }
            else
            {
                json["type"] = card.Type;
                json["attribute"] = card.Attribute;
                json["level"] = card.Level;
                json["atk"] = card.Atk;
                json["def"] = card.Def;
            }

            json["limitation"] = card.Limitation;
            json["copies"] = card.Copies;
            return json;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _preview.Image?.Dispose();

            base.Dispose(disposing);
        }
    }
}
