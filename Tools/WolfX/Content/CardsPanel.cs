using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Wolf.Editors;
using WolfX;

namespace WolfEx
{
    /// <summary>
    /// Yu-Gi-Oh-Ex/cards.json: new cards, and their art next to it. Field names and values are the ones the
    /// Yu-Gi-Oh-MoreCards plugin reads (Card.h / Card.cpp).
    /// </summary>
    internal sealed partial class CardsPanel : UserControl, IContentPanel
    {
        // Every kind the game has: its card frame comes from the kind, so an Xyz Effect is drawn as one.
        internal static readonly (string Name, int Value)[] Kinds =
        {
            ("Normal", 0),
            ("Effect", 1),
            ("Fusion", 2),
            ("Fusion Effect", 3),
            ("Ritual", 4),
            ("Ritual Effect", 5),
            ("Toon", 6),
            ("Spirit", 7),
            ("Union", 8),
            ("Gemini", 9),
            ("Token", 10),
            ("Spell", 13),
            ("Trap", 14),
            ("Tuner Normal", 15),
            ("Tuner Effect", 16),
            ("Synchro", 17),
            ("Synchro Effect", 18),
            ("Synchro Tuner Effect", 19),
            ("Xyz", 22),
            ("Xyz Effect", 23),
            ("Flip Effect", 24),
            ("Pendulum", 25),
            ("Pendulum Effect", 26),
            ("Special Summoned Effect", 27),
            ("Toon Effect", 28),
            ("Spirit Effect", 29),
            ("Tuner", 30),
            ("Tuner Flip Effect", 32),
            ("Pendulum Tuner Effect", 33),
            ("Xyz Pendulum Effect", 34),
            ("Pendulum Flip Effect", 35),
            ("Synchro Pendulum Effect", 36),
            ("Union Tuner Effect", 37),
            ("Ritual Spirit Effect", 38),
            ("Fusion Tuner", 39),
            ("Pendulum Effect Alt", 40),
            ("Fusion Pendulum Effect", 41),
            ("Link", 42),
            ("Link Effect", 43),
            ("Pendulum Tuner Normal", 44),
            ("Pendulum Spirit Effect", 45),
        };
        internal static readonly (string Name, int Value)[] Attributes =
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

        // cards.json's "linkmarkers" names, in bit order (Yu-Gi-Oh-MoreCards Card.cpp LinkMarkerBits)
        internal static readonly string[] LinkMarkerNames = ["Top-Left", "Top", "Top-Right", "Left", "Right", "Bottom-Left", "Bottom", "Bottom-Right"];

        // The plugin accepts ids from the first one after the game's own to the last one the save can hold.
        private const int MinId = 15300;   // the game has effect data for ids 14969-15234; see docs/EffectSystem.md
        private const int MaxId = 19999;

        private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg"];

        // internal, not private: the Effects tab (EffectsPanel) edits EffectSource/Effect on these same
        // instances directly - there is one writer of cards.json (this panel's SaveTo), so every other tab
        // that touches a card's data shares this model rather than keeping its own copy to save separately.
        internal sealed class CardModel
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
            public string EffectSource = "";     // EffectScript source (Effects tab) - not read by the game
            public JsonObject? Effect;            // compiled from EffectSource (Effects tab): the card's "effectClone" object, what Yu-Gi-Oh-Effects runs
            // Every property of the card's cards.json entry this editor has no field for (konamiId, password, archetypes,
            // frameType, card_sets, ... everything YGOPRODeck gave it). Kept as it is and written back on save, so
            // saving here never drops data.
            public JsonObject? Extra;

            /// <summary>The archetype codes of this card (the "archetypes" list in cards.json, kept in Extra): one or many.</summary>
            public List<int> Archetypes
            {
                get => Extra?["archetypes"] is JsonArray array ? array.Select(node => node!.GetValue<int>()).ToList() : [];
                set
                {
                    if (value.Count == 0)
                    {
                        Extra?.Remove("archetypes");
                        return;
                    }
                    Extra ??= [];
                    var array = new JsonArray();
                    foreach (int code in value)
                        array.Add(code);
                    Extra["archetypes"] = array;
                }
            }

            /// <summary>The pendulum scale ("scale" in cards.json, kept in Extra; the plugin reads it for Pendulum kinds).</summary>
            public int Scale
            {
                get => Extra?["scale"] is JsonValue value && value.TryGetValue<int>(out int scale) ? scale : 0;
                set
                {
                    if (value == 0)
                        Extra?.Remove("scale");
                    else
                    {
                        Extra ??= [];
                        Extra["scale"] = value;
                    }
                }
            }

            public bool IsSpellOrTrap => Kind is "Spell" or "Trap";
            public override string ToString() => $"{Id} - {Name}";

            /// <summary>The Link arrows ("linkmarkers" in cards.json, kept in Extra): bit 0 top-left .. 7 bottom-right, as the plugin reads them.</summary>
            public int LinkMarkers
            {
                get
                {
                    int bits = 0;
                    if (Extra?["linkmarkers"] is JsonArray array)
                        foreach (var node in array)
                            if (node is JsonValue value && value.TryGetValue<string>(out string? name) &&
                                Array.FindIndex(LinkMarkerNames, n => n.Equals(name, StringComparison.OrdinalIgnoreCase)) is int bit and >= 0)
                                bits |= 1 << bit;
                    return bits;
                }
                set
                {
                    if (value == 0)
                    {
                        Extra?.Remove("linkmarkers");
                        return;
                    }
                    Extra ??= [];
                    var array = new JsonArray();
                    for (int bit = 0; bit < 8; bit++)
                        if ((value & 1 << bit) != 0)
                            array.Add(LinkMarkerNames[bit]);
                    Extra["linkmarkers"] = array;
                }
            }

            /// <summary>The card as the game would print it (the Card Manager's preview draws it the same way as a game card).</summary>
            public GameCard ToGameCard()
            {
                int kind = Kinds.FirstOrDefault(k => k.Name == Kind).Value;
                bool link = GameCardPainter.FrameOf(kind) == 18;
                return new GameCard
                {
                    Name = Name, Kind = kind, Attribute = Attributes.FirstOrDefault(a => a.Name == Attribute).Value,
                    Race = CardNames.RaceName(Types.FirstOrDefault(t => t.Name == Type).Value), Level = Level, Atk = Atk, Def = link ? 0 : Def,
                    LinkArrows = link ? LinkMarkers : 0, Scale = Scale, Icon = Icons.FirstOrDefault(i => i.Name == Icon).Value,
                    Text = Description,
                };
            }
        }

        private readonly List<CardModel> _cards = [];
        private string _folder = "";
        private bool _binding;

        /// <summary>The Effects tab edits these same instances directly; refreshed after every LoadFrom/add/delete.</summary>
        internal IReadOnlyList<CardModel> Cards => _cards;

        /// <summary>Fires after the card list changes shape (loaded, added, duplicated, deleted) so another tab can refresh its own view of it.</summary>
        public event Action? CardsChanged;

        /// <summary>Something the user should know about the card being edited (shown in the status bar).</summary>
        public event Action<string>? Warning;

        public string Title => "New cards";

        public CardsPanel()
        {
            PreviewPopOut.Attach(_preview, "Card art", doubleClick: true);
            BuildLayout();
            Disposed += (_, _) => _preview.Image?.Dispose();
            SetEditorEnabled(false);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.S))
            {
                SaveRequested?.Invoke();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>Every field of the card: Kind also decides which of the others apply.</summary>
        private void Editor_Changed(object? sender, EventArgs e)
        {
            if (sender == _kind)
                ApplyKindRules();
            Commit();
        }

        private void btnAdd_Click(object? sender, EventArgs e) => AddCard(null);

        private void btnDuplicate_Click(object? sender, EventArgs e) => DuplicateCard();

        private void btnDelete_Click(object? sender, EventArgs e) => DeleteCard();

        private void btnChooseArt_Click(object? sender, EventArgs e) => ChooseArt();

        private void btnArchetypes_Click(object? sender, EventArgs e)
        {
            var card = Selected;
            if (card == null)
                return;

            using var dialog = ArchetypeDialog.Create(card.Name, card.Archetypes);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            card.Archetypes = dialog.Result;
            _archetypes.Text = ArchetypeCatalog.Describe(card.Archetypes);
        }

        // ---------------------------------------------------------------- IContentPanel

        private ContentSnapshot? _snapshot;

        public bool Dirty => _snapshot?.Changed == true;

        public void MarkSaved() => (_snapshot ??= new ContentSnapshot(() => _cards)).Mark();

        public void LoadFrom(string extraCardsFolder, string gameFolder)
        {
            _folder = extraCardsFolder;
            ArchetypeCatalog.Load(extraCardsFolder);
            _cards.Clear();
            _summary.Art = GameFolderFiles.Current is { } files ? new WorkspaceCardArt(files) : null;

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

            Refill(_cards.FirstOrDefault());
            _status.Text = _cards.Count == 0 ? $"No custom cards yet in {path}: New card adds one." : $"{_cards.Count} custom cards in {path}.";
            CardsChanged?.Invoke();
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

                var options = new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
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

        // ---------------------------------------------------------------- model <-> UI

        private CardModel? Selected => _list.SelectedIndices.Count > 0 && _list.SelectedIndices[0] < _rows.Count ? _rows[_list.SelectedIndices[0]] : null;

        /// <summary>Fills the list (the find box applies) and picks this card, or the first.</summary>
        private void Refill(CardModel? select)
        {
            string find = _find.Text.Trim();
            _rows = _cards.Where(c => find.Length == 0 || c.Id.ToString() == find || c.Name.Contains(find, StringComparison.OrdinalIgnoreCase))
                .OrderBy(c => c.Id).ToList();
            _binding = true;
            try
            {
                _list.SelectedIndices.Clear();
                _list.VirtualListSize = _rows.Count;
            }
            finally { _binding = false; }
            int index = select == null ? -1 : _rows.IndexOf(select);
            if (index < 0 && _rows.Count > 0)
                index = 0;
            if (index >= 0)
            {
                _list.SelectedIndices.Add(index);
                _list.EnsureVisible(index);
            }
            _list.Invalidate();
            BindSelected();
        }

        private void ShowSummary()
        {
            if (Selected is not { } card)
            {
                _summary.ShowNothing(_cards.Count == 0 ? "No custom cards yet: New card adds one." : "");
                return;
            }
            string? art = ResolveArt(card);
            _summary.ShowCard(card.ToGameCard(),
                $"Konami id {card.Id}   custom card (cards.json)" + (card.PendingArt != null ? "   new art, copied when saved" : ""),
                art != null ? Imaging.Load(art) : null, ownsPicture: true);
        }

        private void SetEditorEnabled(bool enabled)
        {
            foreach (var control in new Control[] { _id, _name, _desc, _kind, _type, _attribute, _icon, _level, _atk, _def, _scale, _limitation, _copies, btnArchetypes, btnChooseArt })
                control.Enabled = enabled;
            _duplicate.Enabled = _delete.Enabled = _inManager.Enabled = enabled;

            if (enabled)
                ApplyKindRules();
        }

        /// <summary>Spells and traps have no type, attribute, level or ATK/DEF: the plugin fills those in itself.</summary>
        private void ApplyKindRules()
        {
            string kind = NameOf(_kind);
            bool spellOrTrap = kind is "Spell" or "Trap";
            _type.Enabled = _attribute.Enabled = _level.Enabled = _atk.Enabled = !spellOrTrap;
            _def.Enabled = !spellOrTrap && !kind.StartsWith("Link");
            _arrows.Enabled = kind.StartsWith("Link");   // a Link has arrows ("linkmarkers") instead of DEF
            _icon.Enabled = spellOrTrap;
            _levelLabel.Text = kind.StartsWith("Xyz") ? "Rank:" : kind.StartsWith("Link") ? "Link rating:" : "Level:";
            _scale.Enabled = kind.Contains("Pendulum");
        }

        private void BindSelected()
        {
            // Committing a change swaps the list item, which raises this again: don't rebind (that would reset the box being typed in).
            if (_binding)
                return;

            var card = Selected;
            SetEditorEnabled(card != null);
            if (card == null)
            {
                ShowSummary();
                return;
            }

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
                _scale.Value = Math.Clamp(card.Scale, 0, 13);
                _arrows.Value = card.LinkMarkers;
                Select(_limitation, card.Limitation);
                _copies.Value = Math.Clamp(card.Copies, 0, 3);
                _archetypes.Text = ArchetypeCatalog.Describe(card.Archetypes);
                ApplyKindRules();
                RefreshArt(card);
            }
            finally { _binding = false; }
            ShowSummary();
        }

        private static void Select(ComboBox box, string name)
        {
            int index = box.Items.Cast<Named>().ToList().FindIndex(item => item.Name == name);
            box.SelectedIndex = index >= 0 ? index : 0;
        }

        private static string NameOf(ComboBox box) => (box.SelectedItem as Named)?.Name ?? "";

        private void Commit()
        {
            var card = Selected;
            if (_binding || card == null)
                return;

            card.Id = (int)_id.Value;
            card.Name = _name.Text;
            card.Description = _desc.Text;
            card.Kind = NameOf(_kind);
            card.Type = NameOf(_type);
            card.Attribute = NameOf(_attribute);
            card.Icon = NameOf(_icon);
            card.Level = (int)_level.Value;
            card.Atk = (int)_atk.Value;
            card.Def = (int)_def.Value;
            card.Limitation = NameOf(_limitation);
            card.Scale = card.Kind.Contains("Pendulum") ? (int)_scale.Value : 0;
            card.LinkMarkers = card.Kind.StartsWith("Link") ? _arrows.Value : 0;
            card.Copies = (int)_copies.Value;

            if (card.IsSpellOrTrap)
                card.Attribute = card.Type = card.Kind;

            _list.Invalidate();
            ShowSummary();
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

        /// <summary>Picks this card (the Card Manager's "Edit on New cards"); 0 adds a new one.</summary>
        internal void SelectCard(int id)
        {
            if (id == 0)
            {
                AddCard(null);
                return;
            }
            if (_cards.FirstOrDefault(card => card.Id == id) is { } found)
            {
                _find.Text = "";
                Refill(found);
            }
        }

        private void AddCard(CardModel? template)
        {
            var card = template ?? new CardModel();
            card.Id = NextFreeId();
            _cards.Add(card);
            _find.Text = "";
            Refill(card);
            _tabs.SelectedIndex = 0;
            CardsChanged?.Invoke();
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
            if (Selected is not { } card || MessageBox.Show($"Delete \"{card.Name}\"?\nIts art file is left in the folder.", "Delete card",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            int index = _rows.IndexOf(card);
            _cards.Remove(card);
            _rows.Remove(card);
            Refill(_rows.Count > 0 ? _rows[Math.Min(index, _rows.Count - 1)] : null);
            CardsChanged?.Invoke();
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
            ShowSummary();
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
                _artInfo.ForeColor = SystemColors.ControlText;
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
                CheckArt(card, image);
            }
            catch (Exception ex)
            {
                _artPath.Text = path;
                _artInfo.Text = "Could not read the image: " + ex.Message;
            }
        }

        /// <summary>The size the game expects for card art; anything else is stretched to fit.</summary>
        private const int ArtSize = 304;

        /// <summary>Warns (in red under the picture and in the status bar) when the picture isn't the recommended 304 x 304, 24 bit, JPG.</summary>
        private void CheckArt(CardModel card, Image image)
        {
            var problems = new List<string>();
            if (image.Width != ArtSize || image.Height != ArtSize)
                problems.Add($"this image is {image.Width} x {image.Height}, not {ArtSize} x {ArtSize} - expect distortion in the game");

            string extension = Path.GetExtension(card.PendingArt ?? card.Image).ToLowerInvariant();
            if (extension is not (".jpg" or ".jpeg"))
                problems.Add("JPG is recommended");
            if (Image.GetPixelFormatSize(image.PixelFormat) != 24)
                problems.Add($"24 bit colour is recommended (this is {Image.GetPixelFormatSize(image.PixelFormat)} bit)");

            if (problems.Count == 0)
            {
                _artInfo.ForeColor = SystemColors.ControlText;
                return;
            }

            string message = $"\"{card.Name}\": " + string.Join("; ", problems) + ".";
            _artInfo.Text += Environment.NewLine + string.Join(Environment.NewLine, problems.Select(problem => "! " + problem));
            _artInfo.ForeColor = Color.Firebrick;
            _status.Text = "Art warning - " + message;
            Warning?.Invoke("Art warning - " + message);
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
            EffectSource = ReadString(json, "effectScript"),
            // DeepClone: a JsonNode can only have one parent, and this one's parent is `json` (the array
            // element being read) - it must be detached before it can be attached to a card's own tree later.
            Effect = json["effectClone"] is JsonObject effect ? effect.DeepClone().AsObject() : null,
            Extra = ReadExtra(json),
        };

        private static readonly HashSet<string> EditedKeys = new(StringComparer.Ordinal)
        {
            "id", "name", "description", "image", "kind", "type", "attribute", "icon", "level", "atk", "def",
            "limitation", "copies", "effectScript", "effect", "effectClone",
        };

        private static JsonObject? ReadExtra(JsonObject json)
        {
            JsonObject? extra = null;
            foreach (var pair in json)
            {
                if (EditedKeys.Contains(pair.Key))
                    continue;
                extra ??= [];
                extra[pair.Key] = pair.Value?.DeepClone();   // DeepClone: a node has one parent, and this one belongs to `json`
            }
            return extra;
        }

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

            if (!string.IsNullOrEmpty(card.EffectSource))
                json["effectScript"] = card.EffectSource;
            if (card.Effect != null)
                json["effectClone"] = card.Effect.DeepClone();   // DeepClone: see the matching note in ReadCard

            if (card.Extra != null)
            {
                foreach (var pair in card.Extra)
                {
                    if (!json.ContainsKey(pair.Key))
                        json[pair.Key] = pair.Value?.DeepClone();
                }
            }

            return json;
        }
    }
}
