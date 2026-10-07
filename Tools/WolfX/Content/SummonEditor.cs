using System.Text.Json.Nodes;
using WolfX.Types;

namespace WolfEx
{
    /// <summary>The card the Summoning editor shows, and where its requirement keys live.</summary>
    internal sealed class SummonTarget
    {
        public required int Id { get; init; }
        public required string Name { get; init; }
        /// <summary>"Fusion / Effect", "Synchro / Tuner / Effect", "Xyz" ... (New cards kinds or CardNames.KindName).</summary>
        public required string Kind { get; init; }
        /// <summary>A Spell with the Ritual icon.</summary>
        public bool RitualSpell { get; init; }
        public string Description { get; init; } = "";
        /// <summary>Level, or Rank for an Xyz Monster.</summary>
        public int Level { get; init; }
        /// <summary>The keys: "fusion", "ritualSpell", "ritualMonsters", "synchro", "xyz", "link" (a custom card's Extra, or a game card's summoning.json entry).</summary>
        public required JsonObject Data { get; init; }
        /// <summary>A game card: its requirements start as the game's and changes go to summoning.json.</summary>
        public bool GameCard { get; init; }
        /// <summary>A game card whose requirements are changed (it has a summoning.json entry).</summary>
        public bool Changed { get; init; }
        /// <summary>A custom Ritual Spell: what its effect is, and how to give it Black Luster Ritual's (null: not shown).</summary>
        public string? RitualEffectNote { get; init; }
        public Action? GiveRitualEffect { get; init; }
    }

    /// <summary>
    /// The Summoning tab, the same on the New cards page and the Card Manager: what a Fusion Monster is made from ("fusion", Yu-Gi-Oh-Effects
    /// Fusion.cpp), the Ritual Spell of a Ritual Monster and the Ritual Monsters of a Ritual Spell ("ritualSpell" / "ritualMonsters", Ritual.cpp),
    /// and a Synchro, Xyz or Link Monster's materials ("synchro" / "xyz" / "link", SynchroXyz.cpp). docs/EffectSystem.md sections 36, 40, 41, 42.
    /// </summary>
    internal sealed class SummonEditor : UserControl
    {
        private const int MinMaterials = 2, MaxMaterials = 5;

        private enum Section { None, Fusion, RitualMonster, RitualSpell, Synchro, Xyz, Link }

        private SummonTarget? _target;

        /// <summary>After every change to the target's Data.</summary>
        public event Action<SummonTarget>? DataChanged;
        /// <summary>A game card: "Use the game's" - the host drops its summoning.json entry and binds again.</summary>
        public event Action<SummonTarget>? ResetToGame;

        /// <summary>The name of a card id (custom cards first). Set by the host.</summary>
        public Func<int, string> CardName { get; set; } = CardCatalog.NameOf;
        /// <summary>A card id by its name (custom cards first), 0 = none. Set by the host.</summary>
        public Func<string, int> CardIdByName { get; set; } = EffectScriptCompiler.TryCardIdByName;
        /// <summary>The custom cards' names and archetypes (reading archetype names in card text).</summary>
        public Func<IEnumerable<(string Name, List<int> Archetypes)>> CustomCards { get; set; } = () => [];

        private readonly Label _note = new() { Dock = DockStyle.Top, AutoSize = false, Height = 58, Padding = new Padding(9, 6, 6, 0) };
        private readonly FlowLayoutPanel _gameBar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = false, Padding = new Padding(6, 2, 6, 0) };
        private readonly Label _gameState = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

        // Fusion
        private readonly Panel _fusionBox = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        private readonly ListBox _materials = new() { Dock = DockStyle.Fill, IntegralHeight = false };

        // Ritual Monster
        private readonly Panel _ritualMonsterBox = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        private readonly Label _ritualSpell = new() { AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

        // Ritual Spell
        private readonly Panel _ritualSpellBox = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        private readonly ListBox _ritualMonsters = new() { Dock = DockStyle.Fill, IntegralHeight = false };
        private readonly Label _ritualSpellEffect = new() { AutoSize = true, MaximumSize = new Size(520, 0), Margin = new Padding(3, 8, 3, 3) };
        private readonly System.Windows.Forms.Button _ritualEffectButton;

        // Synchro
        private readonly Panel _synchroBox = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        private readonly MaterialCodePicker _tuner = new(synchro: true), _nonTuner = new(synchro: true);
        private readonly NumericUpDown _synchroCount = new() { Minimum = 2, Maximum = 20, Width = 60 };
        private readonly CheckBox _synchroExactly = new() { Text = "exactly (no more non-Tuners)", AutoSize = true, Margin = new Padding(8, 6, 3, 3) };
        private readonly Label _synchroState = new() { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 8, 3, 3) };

        // Xyz
        private readonly Panel _xyzBox = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        private readonly MaterialCodePicker _xyzMaterial = new(synchro: false);
        private readonly NumericUpDown _xyzCount = new() { Minimum = 1, Maximum = 20, Width = 60 };
        private readonly Label _xyzState = new() { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 8, 3, 3) };

        // Link: g_LinkMaterialRequirements' three codes (two every material must meet, one at least one must)
        private readonly Panel _linkBox = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        private readonly MaterialCodePicker _linkCondition = new(MaterialCodePicker.Mode.LinkEach), _linkMaterial = new(MaterialCodePicker.Mode.LinkEach);
        private readonly MaterialCodePicker _linkIncluding = new(MaterialCodePicker.Mode.LinkIncluding);
        private readonly Label _linkState = new() { AutoSize = true, MaximumSize = new Size(560, 0), Margin = new Padding(3, 8, 3, 3) };

        private bool _binding;

        private static System.Windows.Forms.Button SmallButton(string text, Action click)
        {
            var button = new System.Windows.Forms.Button { Text = text, AutoSize = true };
            button.Click += (_, _) => click();
            return button;
        }

        private static FlowLayoutPanel Row(params Control[] controls)
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
            row.Controls.AddRange(controls);
            return row;
        }

        private static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 8, 3, 3) };

        public SummonEditor()
        {
            Dock = DockStyle.Fill;

            // Fusion: the material list with its buttons on the right
            var fusionButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            fusionButtons.Controls.AddRange(
            [
                SmallButton("Add cards...", AddMaterialCards),
                SmallButton("Add condition...", () => EditMaterial(-1)),
                SmallButton("Edit...", () => EditMaterial(_materials.SelectedIndex)),
                SmallButton("Duplicate", DuplicateMaterial),
                SmallButton("Remove", RemoveMaterial),
                SmallButton("Move up", () => MoveMaterial(-1)),
                SmallButton("Move down", () => MoveMaterial(1)),
                SmallButton("Read card text", MaterialsFromText),
            ]);
            _materials.DoubleClick += (_, _) => EditMaterial(_materials.SelectedIndex);
            _fusionBox.Controls.Add(_materials);
            _fusionBox.Controls.Add(fusionButtons);
            _fusionBox.Controls.Add(new Label { Text = "Fusion Materials (2 to 5, each is one card):", Dock = DockStyle.Top, Height = 20 });

            // Ritual Monster: one Ritual Spell
            var monsterFlow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            monsterFlow.Controls.Add(Row(Caption("Ritual Spell:"), _ritualSpell));
            monsterFlow.Controls.Add(Row(SmallButton("Choose Ritual Spell...", ChooseRitualSpell), SmallButton("Generic spells only", () => SetRitualSpell(0))));
            _ritualMonsterBox.Controls.Add(monsterFlow);

            // Ritual Spell: the monsters it summons
            var spellButtons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            spellButtons.Controls.AddRange([SmallButton("Add monsters...", AddRitualMonsters), SmallButton("Remove", RemoveRitualMonster)]);
            _ritualEffectButton = SmallButton("Use Black Luster Ritual's effect", () => _target?.GiveRitualEffect?.Invoke());
            var effectRow = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            effectRow.Controls.Add(_ritualSpellEffect);
            effectRow.Controls.Add(_ritualEffectButton);
            _ritualSpellBox.Controls.Add(_ritualMonsters);
            _ritualSpellBox.Controls.Add(spellButtons);
            _ritualSpellBox.Controls.Add(effectRow);
            _ritualSpellBox.Controls.Add(new Label { Text = "Ritual Monsters this card Ritual Summons:", Dock = DockStyle.Top, Height = 20 });

            // Synchro: Tuner, non-Tuners, how many
            var synchroFlow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            synchroFlow.Controls.Add(Row(Caption("Tuner:"), _tuner));
            synchroFlow.Controls.Add(Row(Caption("non-Tuners:"), _nonTuner));
            synchroFlow.Controls.Add(Row(Caption("Materials in all (the Tuner + the non-Tuners):"), _synchroCount, _synchroExactly));
            synchroFlow.Controls.Add(Row(SmallButton("Read card text", SynchroFromText), SmallButton("Use the generic rule", () => SetKey("synchro", null))));
            synchroFlow.Controls.Add(_synchroState);
            _synchroBox.Controls.Add(synchroFlow);
            _tuner.ValueChanged += SynchroEdited;
            _nonTuner.ValueChanged += SynchroEdited;
            _synchroCount.ValueChanged += SynchroEdited;
            _synchroExactly.CheckedChanged += SynchroEdited;

            // Xyz: the material condition and how many
            var xyzFlow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            xyzFlow.Controls.Add(Row(Caption("Each material:"), _xyzMaterial));
            xyzFlow.Controls.Add(Row(Caption("Materials:"), _xyzCount));
            xyzFlow.Controls.Add(Row(SmallButton("Read card text", XyzFromText), SmallButton("Use the generic rule", () => SetKey("xyz", null))));
            xyzFlow.Controls.Add(_xyzState);
            _xyzBox.Controls.Add(xyzFlow);
            _xyzMaterial.ValueChanged += XyzEdited;
            _xyzCount.ValueChanged += XyzEdited;

            // Link: the number of materials is the Link Rating's business (the game's generic rule), the table only says what they must be
            var linkFlow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            linkFlow.Controls.Add(Row(Caption("\"N+ ...\" / \"except ...\" - every material is:"), _linkCondition));
            linkFlow.Controls.Add(Row(Caption("\"exactly N ...\" - every material is:"), _linkMaterial));
            linkFlow.Controls.Add(Row(Caption("Including at least one:"), _linkIncluding));
            linkFlow.Controls.Add(Row(SmallButton("Read card text", LinkFromText), SmallButton("Any monsters", SetLinkAny)));
            linkFlow.Controls.Add(_linkState);
            _linkBox.Controls.Add(linkFlow);
            _linkCondition.ValueChanged += LinkEdited;
            _linkMaterial.ValueChanged += LinkEdited;
            _linkIncluding.ValueChanged += LinkEdited;

            _gameBar.Controls.Add(_gameState);
            _gameBar.Controls.Add(SmallButton("Use the game's", () => { if (_target != null) ResetToGame?.Invoke(_target); }));

            Controls.Add(_fusionBox);
            Controls.Add(_ritualMonsterBox);
            Controls.Add(_ritualSpellBox);
            Controls.Add(_synchroBox);
            Controls.Add(_xyzBox);
            Controls.Add(_linkBox);
            Controls.Add(_note);
            Controls.Add(_gameBar);
        }

        private static Section SectionOf(SummonTarget target) =>
            target.RitualSpell ? Section.RitualSpell
            : target.Kind.Contains("Fusion") ? Section.Fusion
            : target.Kind.Contains("Ritual") ? Section.RitualMonster
            : target.Kind.Contains("Synchro") ? Section.Synchro
            : target.Kind.Contains("Xyz") ? Section.Xyz
            : target.Kind.Contains("Link") ? Section.Link
            : Section.None;

        /// <summary>Shows the part the card's kind uses, filled from the target (null: nothing).</summary>
        public void Bind(SummonTarget? target)
        {
            _target = target;
            var section = target == null ? Section.None : SectionOf(target);
            _fusionBox.Visible = section == Section.Fusion;
            _ritualMonsterBox.Visible = section == Section.RitualMonster;
            _ritualSpellBox.Visible = section == Section.RitualSpell;
            _synchroBox.Visible = section == Section.Synchro;
            _xyzBox.Visible = section == Section.Xyz;
            _linkBox.Visible = section == Section.Link;
            _gameBar.Visible = target is { GameCard: true } && section is not Section.None;
            if (target != null)
            {
                _gameState.Text = target.Changed ? "Changed: saved to Yu-Gi-Oh-Ex\\summoning.json (Yu-Gi-Oh-Effects applies it)." : "The game's own requirements.";
                _gameState.ForeColor = target.Changed ? Color.FromArgb(170, 90, 0) : SystemColors.GrayText;
            }
            _note.ForeColor = SystemColors.GrayText;
            _note.Text = section switch
            {
                Section.Fusion => "Polymerization (and every other Fusion card) needs these materials. A material is a card, or a condition " +
                                  "(\"1 Dragon monster\", \"1 LIGHT Warrior monster\", \"1 non-Tuner\"). Saved as \"fusion\" (Yu-Gi-Oh-Effects).",
                Section.RitualMonster => "The Ritual Spell that Ritual Summons this monster, with Tributes whose Levels add up to its Level. " +
                                         "Generic Ritual Spells (Advanced Ritual Art and the like) can summon it either way. Saved as \"ritualSpell\".",
                Section.RitualSpell => "The Ritual Monsters this Ritual Spell summons. A monster has one Ritual Spell: picking a monster here " +
                                       "takes it away from its own spell. Saved as \"ritualMonsters\".",
                Section.Synchro => "1 Tuner + non-Tuners whose Levels add up to this card's Level. Each side can be any monster, a Type, an Attribute, " +
                                   "an archetype, a kind or (Tuner/non-Tuner) one named card. Saved as \"synchro\"; without it the game's generic rule applies.",
                Section.Xyz => $"Monsters of the card's Rank ({target!.Level}) as their Level, optionally all of a Type, Attribute, archetype or kind. " +
                               "Saved as \"xyz\"; without it the game's generic rule applies (2 materials).",
                Section.Link => "What the Link Materials must be. Konami puts \"2+ Effect Monsters\" / \"except Tokens\" in the first slot and " +
                                "\"1 Normal Monster\" / \"2 Spellcaster monsters\" in the second; the game tests both on EVERY material (how many is the " +
                                "Link Rating's business). The third holds for at least one (\"including a Link Monster\"). Saved as \"link\"; without it any monsters.",
                _ => "Only Fusion, Ritual, Synchro, Xyz and Link Monsters and Ritual Spells (a Spell with the Ritual icon) have requirements to pick here.",
            };
            if (target == null)
                return;

            _binding = true;
            try
            {
                switch (section)
                {
                    case Section.Fusion:
                        FillMaterials(target);
                        break;
                    case Section.RitualMonster:
                        int spell = target.Data["ritualSpell"] is JsonValue value && value.TryGetValue<int>(out int id) ? id : 0;
                        _ritualSpell.Text = spell == 0 ? "(generic Ritual Spells only)" : CardText(spell);
                        break;
                    case Section.RitualSpell:
                        _ritualMonsters.Items.Clear();
                        foreach (int monster in RitualMonsterIds(target))
                            _ritualMonsters.Items.Add(CardText(monster));
                        _ritualSpellEffect.Visible = _ritualEffectButton.Visible = target.RitualEffectNote != null;
                        _ritualSpellEffect.Text = target.RitualEffectNote ?? "";
                        _ritualSpellEffect.ForeColor = target.RitualEffectNote?.StartsWith('!') == true ? Color.Firebrick : SystemColors.ControlText;
                        break;
                    case Section.Synchro:
                        BindSynchro(target);
                        break;
                    case Section.Xyz:
                        BindXyz(target);
                        break;
                    case Section.Link:
                        BindLink(target);
                        break;
                }
            }
            finally
            {
                _binding = false;
            }
        }

        private string CardText(int id) => $"{CardName(id)} ({id})";

        private void Changed()
        {
            if (_target == null)
                return;
            DataChanged?.Invoke(_target);
        }

        private void SetKey(string key, JsonNode? value)
        {
            if (_target == null)
                return;
            if (value == null)
                _target.Data.Remove(key);
            else
                _target.Data[key] = value;
            Changed();
            Bind(_target);
        }

        // ---------------------------------------------------------------- Fusion

        private JsonArray Materials(SummonTarget target) => target.Data["fusion"] as JsonArray ?? [];

        private void SetMaterials(JsonArray materials, int select)
        {
            if (_target == null)
                return;
            if (materials.Count == 0)
                _target.Data.Remove("fusion");
            else
                _target.Data["fusion"] = materials;
            _target.Data.Remove("fusionLooser");   // picked by hand now: no longer "looser than the text"
            Changed();
            FillMaterials(_target);
            if (select >= 0 && select < _materials.Items.Count)
                _materials.SelectedIndex = select;
        }

        private void FillMaterials(SummonTarget target)
        {
            _materials.Items.Clear();
            var materials = Materials(target);
            foreach (var node in materials)
                _materials.Items.Add(DescribeMaterial(node));
            if (materials.Count is > 0 and < MinMaterials or > MaxMaterials)
            {
                _note.ForeColor = Color.Firebrick;
                _note.Text = $"! A Fusion needs {MinMaterials} to {MaxMaterials} materials; with {materials.Count} the game ignores the recipe.";
            }
            else if (target.Data["fusionLooser"] is JsonValue looser)
                _note.Text += $"\r\nRead from the card text, left out: {looser}";
        }

        private string DescribeMaterial(JsonNode? node) => node switch
        {
            JsonValue v when v.TryGetValue<int>(out int id) => id >= 3000 ? CardText(id) : MaterialCondition.DescribeCode(id),
            JsonValue v when v.TryGetValue<string>(out string? text) => MaterialCondition.Describe(text),
            JsonArray parts => string.Join(" + ", parts.Select(DescribeMaterial)) is { Length: > 0 } all ? $"1 monster that is: {all}" : "(empty)",
            JsonObject any when any["any"] is JsonArray parts => "one of: " + string.Join(" | ", parts.Select(DescribeMaterial)),
            _ => node?.ToJsonString() ?? "(empty)",
        };

        private void AddMaterialCards()
        {
            if (_target is not { } target)
                return;
            using var picker = new CardPickerDialog(CardCatalog.Get(this), $"Materials for {target.Name}", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                return;
            var materials = (JsonArray)Materials(target).DeepClone();
            foreach (var (picked, _) in picker.Result)
                materials.Add(picked.Id);
            SetMaterials(materials, materials.Count - 1);
        }

        private void EditMaterial(int index)
        {
            if (_target is not { } target)
                return;
            var materials = (JsonArray)Materials(target).DeepClone();
            JsonNode? current = index >= 0 && index < materials.Count ? materials[index] : null;
            if (current is JsonValue v && v.TryGetValue<int>(out int id))
            {
                if (id >= 3000)
                {
                    // a card: pick another one
                    using var picker = new CardPickerDialog(CardCatalog.Get(this), "Replace the material", askCopies: false, maxCopies: 1);
                    if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                        return;
                    materials[index] = picker.Result[0].Card.Id;
                    SetMaterials(materials, index);
                    return;
                }
                // a game code: as its word when it has one
                current = MaterialCondition.WordOfCode(id) is { } word ? JsonValue.Create(word) : current;
            }
            using var dialog = new MaterialConditionDialog(current, target.Name);
            if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Result == null)
                return;
            if (index >= 0 && index < materials.Count)
                materials[index] = dialog.Result;
            else
            {
                materials.Add(dialog.Result);
                index = materials.Count - 1;
            }
            SetMaterials(materials, index);
        }

        private void DuplicateMaterial()
        {
            if (_target is not { } target || _materials.SelectedIndex < 0)
                return;
            var materials = (JsonArray)Materials(target).DeepClone();
            int index = _materials.SelectedIndex;
            materials.Insert(index + 1, materials[index]?.DeepClone());
            SetMaterials(materials, index + 1);
        }

        private void RemoveMaterial()
        {
            if (_target is not { } target || _materials.SelectedIndex < 0)
                return;
            var materials = (JsonArray)Materials(target).DeepClone();
            int index = _materials.SelectedIndex;
            materials.RemoveAt(index);
            SetMaterials(materials, Math.Min(index, materials.Count - 1));
        }

        private void MoveMaterial(int by)
        {
            if (_target is not { } target || _materials.SelectedIndex < 0)
                return;
            var materials = (JsonArray)Materials(target).DeepClone();
            int from = _materials.SelectedIndex, to = from + by;
            if (to < 0 || to >= materials.Count)
                return;
            var node = materials[from];
            materials.RemoveAt(from);
            materials.Insert(to, node);
            SetMaterials(materials, to);
        }

        private void MaterialsFromText()
        {
            if (_target is not { } target)
                return;
            var recipe = FusionMaterialText.TryParse(target.Description, CardIdByName, name => FusionMaterialText.ArchetypeCodesFor(name, CustomCards()), out string why);
            if (recipe == null)
            {
                MessageBox.Show(this, $"The text's first line is not a material list this can read ({why}). Add the materials by hand.", "Read card text",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (Materials(target).Count > 0 && MessageBox.Show(this, "Replace the materials with the ones read from the card text?", "Read card text",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            SetMaterials(recipe.Materials, 0);
            if (recipe.Dropped.Count > 0)
            {
                target.Data["fusionLooser"] = string.Join("; ", recipe.Dropped);
                Changed();
                FillMaterials(target);
            }
        }

        // ---------------------------------------------------------------- Ritual

        private void ChooseRitualSpell()
        {
            if (_target is not { } target)
                return;
            using var picker = new CardPickerDialog(CardCatalog.Get(this), $"Ritual Spell for {target.Name}", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                return;
            SetRitualSpell(picker.Result[0].Card.Id);
        }

        private void SetRitualSpell(int spell)
        {
            // a game monster keeps a row with spell 0 ("generic only"); a custom one simply has no key
            SetKey("ritualSpell", spell == 0 && _target?.GameCard != true ? null : JsonValue.Create(spell));
        }

        private static List<int> RitualMonsterIds(SummonTarget target) =>
            target.Data["ritualMonsters"] is JsonArray array
                ? array.Select(n => n is JsonValue v && v.TryGetValue<int>(out int id) ? id : 0).Where(id => id > 0).ToList()
                : [];

        private void SetRitualMonsterIds(List<int> ids) =>
            SetKey("ritualMonsters", ids.Count == 0 ? null : new JsonArray(ids.Select(id => (JsonNode)id).ToArray()));

        private void AddRitualMonsters()
        {
            if (_target is not { } target)
                return;
            using var picker = new CardPickerDialog(CardCatalog.Get(this), $"Ritual Monsters {target.Name} summons", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                return;
            var ids = RitualMonsterIds(target);
            foreach (var (picked, _) in picker.Result)
                if (!ids.Contains(picked.Id))
                    ids.Add(picked.Id);
            SetRitualMonsterIds(ids);
        }

        private void RemoveRitualMonster()
        {
            if (_target is not { } target || _ritualMonsters.SelectedIndex < 0)
                return;
            var ids = RitualMonsterIds(target);
            int index = _ritualMonsters.SelectedIndex;
            if (index < ids.Count)
                ids.RemoveAt(index);
            SetRitualMonsterIds(ids);
            if (_ritualMonsters.Items.Count > 0)
                _ritualMonsters.SelectedIndex = Math.Min(index, _ritualMonsters.Items.Count - 1);
        }

        // ---------------------------------------------------------------- Synchro / Xyz

        private void BindSynchro(SummonTarget target)
        {
            var synchro = target.Data["synchro"] as JsonObject;
            _tuner.CardName = _nonTuner.CardName = CardName;
            _tuner.Value = synchro?["tuner"];
            _nonTuner.Value = synchro?["nonTuner"];
            _synchroCount.Value = Math.Clamp(synchro?["materials"] is JsonValue n && n.TryGetValue<int>(out int count) ? count : 2, 2, 20);
            _synchroExactly.Checked = synchro?["exactly"] is JsonValue e && e.TryGetValue<bool>(out bool exactly) && exactly;
            _synchroState.Text = synchro == null
                ? "No requirements set: the game's generic rule (1 Tuner + 1 or more non-Tuners). Change anything above to set them."
                : SynchroText(synchro);
        }

        private string SynchroText(JsonObject synchro)
        {
            int count = synchro["materials"] is JsonValue n && n.TryGetValue<int>(out int c) ? c : 2;
            bool exactly = synchro["exactly"] is JsonValue e && e.TryGetValue<bool>(out bool x) && x;
            string tuner = MaterialCodePicker.Describe(synchro["tuner"], CardName);
            string nonTuner = MaterialCodePicker.Describe(synchro["nonTuner"], CardName);
            int others = count - 1;
            return $"Reads: 1 {(tuner == "any" ? "" : tuner + " ")}Tuner + {others}{(exactly ? "" : "+")} {(nonTuner == "any" ? "" : nonTuner + " ")}non-Tuner monster{(others == 1 && exactly ? "" : "s")}.";
        }

        private void SynchroEdited(object? sender, EventArgs e)
        {
            if (_binding || _target == null)
                return;
            var synchro = new JsonObject();
            if (_tuner.Value is { } tuner)
                synchro["tuner"] = tuner.DeepClone();
            if (_nonTuner.Value is { } nonTuner)
                synchro["nonTuner"] = nonTuner.DeepClone();
            synchro["materials"] = (int)_synchroCount.Value;
            if (_synchroExactly.Checked)
                synchro["exactly"] = true;
            _target.Data["synchro"] = synchro;
            Changed();
            _synchroState.Text = SynchroText(synchro);
        }

        private void BindXyz(SummonTarget target)
        {
            var xyz = target.Data["xyz"] as JsonObject;
            _xyzMaterial.CardName = CardName;
            _xyzMaterial.Value = xyz?["material"];
            _xyzCount.Value = Math.Clamp(xyz?["materials"] is JsonValue n && n.TryGetValue<int>(out int count) ? count : 2, 1, 20);
            _xyzState.Text = xyz == null
                ? $"No requirements set: the game's generic rule (2 Level {target.Level} monsters). Change anything above to set them."
                : XyzText(xyz, target.Level);
        }

        private string XyzText(JsonObject xyz, int rank)
        {
            int count = xyz["materials"] is JsonValue n && n.TryGetValue<int>(out int c) ? c : 2;
            string material = MaterialCodePicker.Describe(xyz["material"], CardName);
            return $"Reads: {count} Level {rank} {(material == "any" ? "" : material + " ")}monster{(count == 1 ? "" : "s")}.";
        }

        private void XyzEdited(object? sender, EventArgs e)
        {
            if (_binding || _target == null)
                return;
            var xyz = new JsonObject();
            if (_xyzMaterial.Value is { } material)
                xyz["material"] = material.DeepClone();
            xyz["materials"] = (int)_xyzCount.Value;
            _target.Data["xyz"] = xyz;
            Changed();
            _xyzState.Text = XyzText(xyz, _target.Level);
        }

        // ---------------------------------------------------------------- Link

        private void BindLink(SummonTarget target)
        {
            var link = target.Data["link"] as JsonObject;
            _linkCondition.CardName = _linkMaterial.CardName = _linkIncluding.CardName = CardName;
            _linkCondition.Value = link?["condition"];
            _linkMaterial.Value = link?["material"];
            _linkIncluding.Value = link?["including"];
            _linkState.Text = link == null
                ? "No requirements set: any monsters can be its materials. Change anything above to set them."
                : LinkText(link);
        }

        private string LinkText(JsonObject link)
        {
            string condition = MaterialCodePicker.Describe(link["condition"], CardName);
            string material = MaterialCodePicker.Describe(link["material"], CardName);
            string including = MaterialCodePicker.Describe(link["including"], CardName);
            var each = new[] { condition, material }.Where(t => t != "any").ToList();
            string text = "Reads: " + (each.Count == 0 ? "any monsters" : string.Join(" ", each) + " monsters");
            if (including != "any")
                text += $", including a {including} monster";
            return text + ".";
        }

        private void LinkEdited(object? sender, EventArgs e)
        {
            if (_binding || _target == null)
                return;
            var link = new JsonObject();
            if (_linkCondition.Value is { } condition)
                link["condition"] = condition.DeepClone();
            if (_linkMaterial.Value is { } material)
                link["material"] = material.DeepClone();
            if (_linkIncluding.Value is { } including)
                link["including"] = including.DeepClone();
            _target.Data["link"] = link;
            Changed();
            _linkState.Text = LinkText(link);
        }

        /// <summary>Any monsters: a new card simply has no "link"; a game card keeps an empty one, which takes away the game's row.</summary>
        private void SetLinkAny() => SetKey("link", _target?.GameCard == true ? new JsonObject() : null);

        private void LinkFromText()
        {
            if (_target is not { } target)
                return;
            var read = ExtraDeckMaterialText.Link(target.Description, CardIdByName, name => FusionMaterialText.ArchetypeCodesFor(name, CustomCards()), out string why);
            if (read == null)
            {
                MessageBox.Show(this, $"The text's material line can't be read as Link Materials ({why}). Set them by hand.", "Read card text",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetKey("link", read);
            if (why.Length > 0)
                _linkState.Text += $"\r\nLeft out: {why}";
        }

        private void SynchroFromText()
        {
            if (_target is not { } target)
                return;
            var read = ExtraDeckMaterialText.Synchro(target.Description, CardIdByName, name => FusionMaterialText.ArchetypeCodesFor(name, CustomCards()), out string why);
            if (read == null)
            {
                MessageBox.Show(this, $"The text's material line can't be read as Synchro materials ({why}). Set them by hand.", "Read card text",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetKey("synchro", read);
            if (why.Length > 0)
                _synchroState.Text += $"\r\nLeft out: {why}";
        }

        private void XyzFromText()
        {
            if (_target is not { } target)
                return;
            var read = ExtraDeckMaterialText.Xyz(target.Description, name => FusionMaterialText.ArchetypeCodesFor(name, CustomCards()), out int level, out string why);
            if (read == null)
            {
                MessageBox.Show(this, $"The text's material line can't be read as Xyz materials ({why}). Set them by hand.", "Read card text",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            SetKey("xyz", read);
            if (level != target.Level)
                why = (why.Length > 0 ? why + "; " : "") + $"the text says Level {level}, the game uses the card's Rank ({target.Level})";
            if (why.Length > 0)
                _xyzState.Text += $"\r\nLeft out: {why}";
        }
    }

    /// <summary>
    /// One Synchro, Xyz or Link material condition, the way the game's tables hold it: any monster, a Type, an Attribute, an archetype (the
    /// game's, up to 418), a kind, a Level (Link) or one named card (Synchro, Link "including"). Each mode offers what that check knows
    /// (SynchroXyz.cpp AllowedCode / AllowedLinkCode). Codes the pickers cannot say (the game's own rules, e.g. 97) are kept as they are.
    /// </summary>
    internal sealed class MaterialCodePicker : FlowLayoutPanel
    {
        private sealed record Choice(JsonNode? Value, string Text)
        {
            public override string ToString() => Text;
        }

        /// <summary>Which check the value is for: what it can be differs (Link_CardIsValidMaterial knows Levels and more kinds).</summary>
        public enum Mode { Synchro, Xyz, LinkEach, LinkIncluding }

        private const string GameRule = "Game rule";
        private const string NotToken = "notToken";   // code 96, read by SynchroXyz.cpp's Link parser

        private readonly Mode _mode;
        private bool AllowsCard => _mode is Mode.Synchro or Mode.LinkIncluding;
        private readonly ComboBox _category = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        private readonly ComboBox _value = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
        private readonly System.Windows.Forms.Button _pick = new() { Text = "Pick card...", AutoSize = true };
        private JsonNode? _card;   // the picked card (Card category)
        private JsonNode? _raw;    // a code the categories can't say
        private bool _filling;

        public event EventHandler? ValueChanged;
        public Func<int, string> CardName { get; set; } = CardCatalog.NameOf;

        public MaterialCodePicker(bool synchro) : this(synchro ? Mode.Synchro : Mode.Xyz)
        {
        }

        public MaterialCodePicker(Mode mode)
        {
            _mode = mode;
            AutoSize = true;
            WrapContents = false;
            Margin = new Padding(0);
            _category.Items.AddRange(new List<string> { "Any monster", "Type", "Attribute", "Archetype", "Kind" }
                .Concat(mode == Mode.LinkEach ? ["Level"] : []).Concat(AllowsCard ? ["Card"] : []).ToArray<object>());
            _category.SelectedIndexChanged += (_, _) =>
            {
                if (_filling)
                    return;
                FillValues();
                Raise();
            };
            _value.SelectedIndexChanged += (_, _) => Raise();
            _pick.Click += (_, _) => PickCard();
            Controls.AddRange([_category, _value, _pick]);
            _category.SelectedIndex = 0;
        }

        private void Raise()
        {
            if (!_filling)
                ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private static IEnumerable<Choice> KindChoices(Mode mode)
        {
            if (mode == Mode.LinkEach)
            {
                yield return new Choice(JsonValue.Create("effect"), "Effect");
                yield return new Choice(JsonValue.Create(NotToken), "not a Token");
                yield return new Choice(JsonValue.Create("normal"), "Normal");
                yield return new Choice(JsonValue.Create("pendulum"), "Pendulum");
                yield return new Choice(JsonValue.Create("xyz"), "Xyz");
                yield return new Choice(JsonValue.Create("link"), "Link");
                yield break;
            }
            if (mode == Mode.LinkIncluding)
            {
                yield return new Choice(JsonValue.Create("link"), "Link");
                yield return new Choice(JsonValue.Create("synchro"), "Synchro");
                yield return new Choice(JsonValue.Create("tuner"), "Tuner");
                yield break;
            }
            yield return new Choice(JsonValue.Create("normal"), "Normal");
            yield return new Choice(JsonValue.Create("gemini"), "Gemini");
            yield return new Choice(JsonValue.Create("pendulum"), "Pendulum");
            yield return new Choice(JsonValue.Create(94), "DARK Pendulum");
            if (mode == Mode.Synchro)
                yield return new Choice(JsonValue.Create("synchro"), "Synchro");
        }

        private static IEnumerable<Choice> LevelChoices()
        {
            for (int level = 1; level <= 12; level++)
            {
                yield return new Choice(JsonValue.Create($"level:{level}"), $"Level {level}");
                yield return new Choice(JsonValue.Create($"level>={level}"), $"Level {level} or higher");
                yield return new Choice(JsonValue.Create($"level<={level}"), $"Level {level} or lower");
            }
        }

        private void FillValues()
        {
            _value.Items.Clear();
            string category = _category.SelectedItem as string ?? "";
            IEnumerable<Choice> choices = category switch
            {
                "Type" => MaterialCondition.Types.Take(24).Select(t => new Choice(JsonValue.Create(t.Word), t.Text)),
                "Attribute" => MaterialCondition.AttributeWords.Select(a => new Choice(JsonValue.Create(a.Word), a.Text)),
                "Archetype" => ArchetypeCatalog.Codes().Where(code => code < ArchetypeCatalog.FirstCustomCode)
                    .Select(code => new Choice(JsonValue.Create($"archetype:{code}"), $"{ArchetypeCatalog.NameOf(code)} ({code})"))
                    .OrderBy(c => c.Text, StringComparer.OrdinalIgnoreCase),
                "Kind" => KindChoices(_mode),
                "Level" => LevelChoices(),
                "Card" => _card is JsonValue v && v.TryGetValue<int>(out int id) ? [new Choice(_card, $"{CardName(id)} ({id})")] : [],
                GameRule => _raw != null ? [new Choice(_raw, MaterialCodePicker.Describe(_raw, CardName))] : [],
                _ => [],
            };
            _value.Items.AddRange(choices.Cast<object>().ToArray());
            _value.Visible = category is not ("Any monster" or "");
            _pick.Visible = category == "Card";
            if (_value.Items.Count > 0)
                _value.SelectedIndex = 0;
        }

        private void PickCard()
        {
            using var picker = new CardPickerDialog(CardCatalog.Get(this), "The material card", askCopies: false, maxCopies: 1);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Result.Count == 0)
                return;
            _card = JsonValue.Create(picker.Result[0].Card.Id);
            FillValues();
            Raise();
        }

        /// <summary>null = any monster; else a word ("DARK", "archetype:12"), a card id or a raw game code.</summary>
        public JsonNode? Value
        {
            get => _category.SelectedItem as string is null or "Any monster" ? null : (_value.SelectedItem as Choice)?.Value;
            set
            {
                _filling = true;
                try
                {
                    _raw = null;
                    string category = "Any monster";
                    string? word = null;
                    if (value is JsonValue v && v.TryGetValue<int>(out int code))
                    {
                        if (code == 96 && _mode == Mode.LinkEach)
                            value = JsonValue.Create(NotToken);   // the game's code, shown as its word
                        if (code >= 3000 && AllowsCard)
                        {
                            _card = value;
                            category = "Card";
                        }
                        else if (code == 96 && _mode == Mode.LinkEach)
                            word = NotToken;
                        else if (code != 0)
                            word = MaterialCondition.WordOfCode(code) ?? (code == 94 ? "94" : null);
                        if (code != 0 && word == null && category != "Card")
                            _raw = value;
                    }
                    else if (value is JsonValue s && s.TryGetValue<string>(out string? text) && !string.Equals(text, "any", StringComparison.OrdinalIgnoreCase))
                        word = text;

                    if (word != null)
                    {
                        category = word == "94" || IsNotToken(word) ? "Kind" : MaterialCondition.Parse(word)?.Category switch
                        {
                            MaterialCondition.Category.Type => "Type",
                            MaterialCondition.Category.Attribute => "Attribute",
                            MaterialCondition.Category.Archetype => "Archetype",
                            MaterialCondition.Category.Kind => "Kind",
                            MaterialCondition.Category.Level or MaterialCondition.Category.LevelOrHigher or MaterialCondition.Category.LevelOrLower
                                when _mode == Mode.LinkEach => "Level",
                            _ => GameRule,
                        };
                        if (category == GameRule)
                            _raw = value;
                    }
                    if (category == GameRule && !_category.Items.Contains(GameRule))
                        _category.Items.Add(GameRule);
                    else if (category != GameRule)
                        _category.Items.Remove(GameRule);
                    _category.SelectedItem = category;
                    FillValues();
                    if (word != null && category != GameRule)
                    {
                        var parsed = word == "94" || IsNotToken(word) ? null : MaterialCondition.Parse(word);
                        int index = _value.Items.Cast<Choice>().ToList().FindIndex(c =>
                            word == "94" ? c.Value is JsonValue cv && cv.TryGetValue<int>(out int n) && n == 94
                            : IsNotToken(word) ? c.Value is JsonValue cv3 && cv3.TryGetValue<string>(out string? t) && IsNotToken(t)
                            : c.Value is JsonValue cv2 && cv2.TryGetValue<string>(out string? w) && parsed != null && MaterialCondition.Parse(w) == parsed with { Not = false });
                        if (index >= 0)
                            _value.SelectedIndex = index;
                        else
                        {
                            // a word these lists don't offer (a kind like "tuner"): kept as it is
                            _raw = value;
                            if (!_category.Items.Contains(GameRule))
                                _category.Items.Add(GameRule);
                            _category.SelectedItem = GameRule;
                            FillValues();
                        }
                    }
                }
                finally
                {
                    _filling = false;
                }
            }
        }

        private static bool IsNotToken(string? word) =>
            word != null && System.Text.RegularExpressions.Regex.Replace(word, "[ _-]", "").ToLowerInvariant() is "nottoken" or "excepttoken" or "excepttokens";

        /// <summary>A material value for the summary lines: "any", "DARK", "\"Blackwing\"", "Junk Synchron (7687)".</summary>
        public static string Describe(JsonNode? value, Func<int, string> cardName) => value switch
        {
            null => "any",
            JsonValue v when v.TryGetValue<string>(out string? t) && IsNotToken(t) => "non-Token",
            JsonValue v when v.TryGetValue<int>(out int code) => code == 0 ? "any" : code >= 3000 ? $"{cardName(code)} ({code})" : MaterialCondition.DescribeCode(code),
            JsonValue v when v.TryGetValue<string>(out string? text) => string.Equals(text, "any", StringComparison.OrdinalIgnoreCase) ? "any" : MaterialCondition.Describe(text),
            _ => value.ToJsonString(),
        };
    }
}
