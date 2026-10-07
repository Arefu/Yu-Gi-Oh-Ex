using System.Text.Json;
using System.Text.Json.Nodes;

namespace WolfEx
{
    /// <summary>
    /// Authors a card's EffectScript (Yu-Gi-Oh_Loader Plugins\Yu-Gi-Oh-Effects\Script\EffectScript.g4) and
    /// compiles it down to the plain JSON Yu-Gi-Oh-Effects reads at runtime - the game process never parses
    /// the DSL itself (see ygo-effects-moonshot-plan). Two lists ("Show"):
    /// <list type="bullet">
    /// <item>New cards: edits the same CardModel instances CardsPanel owns (shared by reference, not a copy), so CardsPanel's own SaveTo
    /// is still the only thing that writes cards.json.</item>
    /// <item>Game cards: the game's own cards; compiling a script for one OVERRIDES its effect in the game. Those are this page's own file,
    /// Yu-Gi-Oh-Ex\effects.json ("overridden": true, see GameEffectsFile), which is what IContentPanel saves.</item>
    /// </list>
    /// </summary>
    internal sealed partial class EffectsPanel : UserControl, IContentPanel
    {
        private CardsPanel? _cardsPanel;
        private bool _binding;

        // ---- game cards (effects.json) ----
        private readonly ComboBox _show = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };
        private readonly Button _btnUseGame = new() { Text = "Use the game's effect", AutoSize = true, Margin = new Padding(0, 6, 8, 0), Visible = false, UseVisualStyleBackColor = true };
        private const int ShowNew = 0, ShowGame = 1, ShowChanged = 2;
        private bool _showChosen;                                   // the user picked a list (else it follows "are there new cards")
        private List<GameEffectCard>? _gameCards;                   // read when the game list is first shown
        private readonly SortedDictionary<int, GameEffectCard> _overrides = [];   // effects.json's entries, by id
        private string _savedOverrides = "";

        private bool GameMode => _show.SelectedIndex is ShowGame or ShowChanged;

        public EffectsPanel()
        {
            InitializeComponent();
            AddBlocksTab();
            AddGameCards();
            SetEditorEnabled(false);
            // the designer's distances were set while the page was still its default size, which squeezed the card list
            Load += (_, _) =>
            {
                split.SplitterDistance = Math.Clamp(Width * 28 / 100, 220, 320);
                right.SplitterDistance = Math.Clamp(right.Height * 62 / 100, 200, Math.Max(200, right.Height - 120));
            };
        }

        private void AddGameCards()
        {
            _show.Items.AddRange(["Show: new cards (cards.json)", "Show: game cards (override their effect)", "Show: game cards I changed"]);
            _show.SelectedIndex = ShowNew;
            _show.SelectedIndexChanged += (_, _) =>
            {
                if (_binding)
                    return;
                _showChosen = true;
                RefreshList();
            };
            // the list picker goes above the filter (both in the first row of the left column)
            left.Controls.Remove(_filter);
            var head = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
            head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            head.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            head.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            head.Controls.Add(_show, 0, 0);
            head.Controls.Add(_filter, 0, 1);
            left.Controls.Add(head, 0, 0);

            _btnUseGame.Click += (_, _) => UseGameEffect();
            buttons.Controls.Add(_btnUseGame);
            buttons.Controls.SetChildIndex(_btnUseGame, buttons.Controls.GetChildIndex(_status));
        }

        private GameEffectCard? SelectedGame => _list.SelectedItem as GameEffectCard;

        private List<GameEffectCard> GameCards()
        {
            if (_gameCards != null)
                return _gameCards;
            _gameCards = [];
            foreach (var card in GameEffectsFile.ReadGameCards(this))
            {
                // an overridden card is the object effects.json was read into (so edits and saving share it)
                if (_overrides.TryGetValue(card.Id, out var changed))
                {
                    changed.Kind = card.Kind;
                    changed.Text = card.Text;
                    changed.GameScript = card.GameScript;
                    if (string.IsNullOrEmpty(changed.Name))
                        changed.Name = card.Name;
                    _gameCards.Add(changed);
                }
                else
                    _gameCards.Add(card);
            }
            foreach (var changed in _overrides.Values.Where(o => !_gameCards.Contains(o)))
                _gameCards.Add(changed);   // in effects.json but not in this game's catalog: still shown, so it can be removed
            _gameCards.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return _gameCards;
        }

        /// <summary>"Use the game's effect": the card's override is dropped from effects.json.</summary>
        private void UseGameEffect()
        {
            if (SelectedGame is not { } card)
                return;
            card.Overridden = false;
            card.EffectSource = "";
            card.Effect = null;
            _overrides.Remove(card.Id);
            RedrawSelected();
            BindSelected();
            _status.Text = card.Name + " plays its own effect again (saved to effects.json when you Save).";
            _status.ForeColor = SystemColors.ControlText;
        }

        private void RedrawSelected()
        {
            if (_list.SelectedIndex >= 0)
            {
                _binding = true;
                try { _list.Items[_list.SelectedIndex] = _list.Items[_list.SelectedIndex]; }
                finally { _binding = false; }
            }
        }

        // ---- IContentPanel: effects.json ----

        public string Title => "Effects";

        public void LoadFrom(string extraCardsFolder, string gameFolder)
        {
            _overrides.Clear();
            foreach (var (id, entry) in GameEffectsFile.Load(Path.Combine(extraCardsFolder, GameEffectsFile.FileName)))
                _overrides[id] = new GameEffectCard
                {
                    Id = id,
                    Name = entry["name"]?.GetValue<string>() ?? "",
                    Overridden = entry["overridden"]?.GetValue<bool>() ?? false,
                    EffectSource = entry["effectScript"]?.GetValue<string>() ?? "",
                    Effect = entry["effectClone"] as JsonObject is { } clone ? clone.DeepClone().AsObject() : null,
                };
            _gameCards = null;   // the game folder may be another one now
            if (GameMode)
                RefreshList();
        }

        public bool SaveTo(string extraCardsFolder)
        {
            string path = Path.Combine(extraCardsFolder, GameEffectsFile.FileName);
            if (_overrides.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return true;
            }
            File.WriteAllText(path, GameEffectsFile.Text(_overrides.Values));
            return true;
        }

        public bool Dirty => GameEffectsFile.Text(_overrides.Values) != _savedOverrides;

        public void MarkSaved() => _savedOverrides = GameEffectsFile.Text(_overrides.Values);

        // ---- the drag-and-drop editor beside the script one (ScriptBlocksTabs) ----

        private ScriptBlocksTabs _editorTabs = null!;

        private void AddBlocksTab()
        {
            _editorTabs = ScriptBlocksTabs.Replace(_source);
            lblSource.Text = "The card's effect, as script or as blocks (the two tabs always match; the language: docs\\EffectLanguage.md):";
        }

        /// <summary>Called once from MainForm after both panels exist, and whenever the Effects tab is selected.</summary>
        public void Attach(CardsPanel cardsPanel)
        {
            if (_cardsPanel != null)
                _cardsPanel.CardsChanged -= RefreshList;
            _cardsPanel = cardsPanel;
            _cardsPanel.CardsChanged += RefreshList;
            RefreshList();
        }

        private void RefreshList()
        {
            if (_cardsPanel == null)
                return;

            // With no new cards there is nothing to edit in the new cards' list: start on the game's cards (the user's pick wins).
            if (!_showChosen)
            {
                _binding = true;
                try { _show.SelectedIndex = _cardsPanel.Cards.Count == 0 ? ShowGame : ShowNew; }
                finally { _binding = false; }
            }
            bool game = GameMode;
            _btnSelectAll.Visible = _btnClearChecks.Visible = _btnApplyAll.Visible = _btnAuto.Visible = !game;
            _btnUseGame.Visible = game;
            _list.CheckOnClick = !game;
            if (game)
            {
                RefreshGameList();
                return;
            }

            var previous = Selected;
            string filter = _filter.Text.Trim();
            _refreshing = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var card in _cardsPanel.Cards)
            {
                if (filter.Length == 0 || card.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || card.Id.ToString() == filter
                    || card.Description.Contains(filter, StringComparison.OrdinalIgnoreCase))
                    _list.Items.Add(card, _checked.Contains(card));
            }
            _list.EndUpdate();
            _refreshing = false;
            _checked.RemoveWhere(card => !_cardsPanel.Cards.Contains(card));
            UpdateCheckedCount();

            if (previous != null && _cardsPanel.Cards.Contains(previous))
                _list.SelectedItem = previous;
            else if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
            else
                BindSelected();
        }

        private void RefreshGameList()
        {
            var previous = SelectedGame;
            string filter = _filter.Text.Trim();
            bool changedOnly = _show.SelectedIndex == ShowChanged;
            var shown = GameCards().Where(card => (!changedOnly || _overrides.ContainsKey(card.Id)) &&
                (filter.Length == 0 || card.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || card.Id.ToString() == filter
                 || card.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))).Cast<object>().ToArray();
            _refreshing = true;
            _list.BeginUpdate();
            _list.Items.Clear();
            _list.Items.AddRange(shown);
            _list.EndUpdate();
            _refreshing = false;
            if (previous != null && _list.Items.Contains(previous))
                _list.SelectedItem = previous;
            else if (_list.Items.Count > 0)
                _list.SelectedIndex = 0;
            else
                BindSelected();
        }

        private CardsPanel.CardModel? Selected => _list.SelectedItem as CardsPanel.CardModel;

        private void List_SelectedIndexChanged(object? sender, EventArgs e) => BindSelected();

        private void SetEditorEnabled(bool enabled)
        {
            _source.Enabled = enabled;
            _editorTabs.Blocks.Enabled = enabled;
            _btnCompile.Enabled = enabled;
            _btnApplyAll.Enabled = enabled;
        }

        // The ticked cards are what the bulk buttons act on. The set is kept apart from the list because a filter change rebuilds the list.
        private readonly HashSet<CardsPanel.CardModel> _checked = [];
        private bool _refreshing;

        private List<CardsPanel.CardModel> CheckedCards => _checked.ToList();

        private void UpdateCheckedCount() => _btnApplyAll.Text = _checked.Count == 0 ? "Apply script to all checked" : $"Apply script to {_checked.Count} checked";

        private void List_ItemCheck(object? sender, ItemCheckEventArgs e)
        {
            if (GameMode && !_refreshing)
            {
                e.NewValue = e.CurrentValue;   // game cards are edited one at a time
                return;
            }
            if (_refreshing || _list.Items[e.Index] is not CardsPanel.CardModel card)
                return;
            if (e.NewValue == CheckState.Checked)
                _checked.Add(card);
            else
                _checked.Remove(card);
            BeginInvoke(UpdateCheckedCount);
        }

        private void Filter_TextChanged(object? sender, EventArgs e) => RefreshList();

        private void btnSelectAll_Click(object? sender, EventArgs e)
        {
            _list.BeginUpdate();
            for (int i = 0; i < _list.Items.Count; i++)
                _list.SetItemChecked(i, true);
            _list.EndUpdate();
        }

        private void btnClearChecks_Click(object? sender, EventArgs e)
        {
            _checked.Clear();
            _list.BeginUpdate();
            _refreshing = true;
            for (int i = 0; i < _list.Items.Count; i++)
                _list.SetItemChecked(i, false);
            _refreshing = false;
            _list.EndUpdate();
            UpdateCheckedCount();
        }

        /// <summary>Used by the Effect library tab: puts a script into the editor of the selected card (it is compiled with the Compile button).</summary>
        public bool ApplyTemplate(string script)
        {
            string? name = Selected?.Name ?? SelectedGame?.Name;
            if (name == null)
                return false;
            _source.Text = script;
            _status.Text = "Template inserted for " + name + ". Adjust it, then Compile" + (SelectedGame != null ? " (that overrides the game card's effect)." : ".");
            _status.ForeColor = SystemColors.ControlText;
            return true;
        }

        /// <summary>Compiles the script in the editor once and gives it to every selected card (the shared bulk attach).</summary>
        private void btnApplyAll_Click(object? sender, EventArgs e)
        {
            var cards = CheckedCards;
            if (cards.Count == 0)
            {
                _status.Text = "Tick the cards (the boxes in the list) that should get this script first.";
                _status.ForeColor = Color.Firebrick;
                return;
            }
            if (string.IsNullOrWhiteSpace(_source.Text))
            {
                _status.Text = "Write a script first.";
                _status.ForeColor = Color.Firebrick;
                return;
            }

            var result = EffectScriptCompiler.Compile(_source.Text);
            if (!result.Ok)
            {
                _source.ShowError(result.Error);
                _status.Text = "Not compiled - fix this first: " + result.Error;
                _status.ForeColor = Color.Firebrick;
                return;
            }
            if (cards.Count > 1 && MessageBox.Show(this, $"Give this script to {cards.Count} cards?", "Effects", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            foreach (var card in cards)
            {
                card.EffectSource = _source.Text;
                card.Effect = result.Compiled!.DeepClone().AsObject();
            }
            _compiled.Text = result.Compiled!.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true });
            _status.Text = $"Applied to {cards.Count} card(s). Saved with the rest of the cards when you use Save All.";
            _status.ForeColor = Color.DarkGreen;
        }

        /// <summary>
        /// Reads each card's text and attaches every effect the game can run (CardTextTranslator: triggers, conditions, costs, actions).
        /// With several cards selected only those are looked at, otherwise all cards. Cards that already have a script are left alone.
        /// Fusion Monsters without "fusion" get a recipe from their material line (FusionMaterialText).
        /// </summary>
        private void btnAuto_Click(object? sender, EventArgs e)
        {
            if (_cardsPanel == null)
                return;
            var selected = CheckedCards;
            var candidates = (selected.Count > 0 ? selected : _cardsPanel.Cards.ToList())
                .Where(card => string.IsNullOrEmpty(card.EffectSource) && !string.IsNullOrWhiteSpace(card.Description)).ToList();
            if (MessageBox.Show(this, $"Read the text of {candidates.Count} card(s) without a script and attach every effect the game can run? Text that is not run is kept in \"effectNotImplemented\", dropped details in \"effectLooser\". Fusion Monsters without a recipe also get one from their material line.",
                    "Effects", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            var customIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var card in _cardsPanel.Cards)
                customIds.TryAdd(card.Name, card.Id);
            int CardId(string name) => customIds.TryGetValue(name, out int id) ? id : EffectScriptCompiler.TryCardIdByName(name);
            var customArchetypes = _cardsPanel.Cards.Select(card => (card.Name, card.Archetypes)).ToList();
            var archetypeCache = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
            List<int> ArchetypeCodes(string name) => archetypeCache.TryGetValue(name, out var cached) ? cached
                : archetypeCache[name] = FusionMaterialText.ArchetypeCodesFor(name, customArchetypes);

            // Whole card text -> EffectScript (CardTextTranslator, docs/EffectSystem.md section 37).
            var attached = new List<string>();
            int effects = 0, partial = 0;
            foreach (var card in candidates)
            {
                var result = CardTextTranslator.Translate(card.Name, card.Kind, card.Icon, card.Description, card.Archetypes, CardId, ArchetypeCodes);
                if (result.Effects == 0)
                    continue;
                var compiled = EffectScriptCompiler.Compile(result.Script);
                if (!compiled.Ok)
                    continue;

                card.EffectSource = result.Script;
                card.Effect = compiled.Compiled;
                card.Extra ??= [];
                card.Extra.Remove("effectNotImplemented");
                card.Extra.Remove("effectLooser");
                if (result.NotRun.Count > 0)
                    card.Extra["effectNotImplemented"] = string.Join(" ", result.NotRun.Select(s => s + "."));
                if (result.Looser.Count > 0)
                    card.Extra["effectLooser"] = string.Join("; ", result.Looser);
                effects += result.Effects;
                if (result.NotRun.Count > 0)
                    partial++;
                attached.Add($"{card.Id} {card.Name}: {result.Script}" + (result.NotRun.Count > 0 ? "   [+ text not run]" : ""));
            }
            int scripted = attached.Count;

            // Fusion Monsters without a recipe: the material line becomes "fusion" (FusionMaterialText, Yu-Gi-Oh-Effects Fusion.cpp).
            var fusions = (selected.Count > 0 ? selected : _cardsPanel.Cards.ToList())
                .Where(card => card.Kind.Contains("Fusion") && card.Extra?["fusion"] == null).ToList();
            int recipes = 0;
            var notRead = new List<string>();
            foreach (var card in fusions)
            {
                var recipe = FusionMaterialText.TryParse(card.Description, CardId, ArchetypeCodes, out string why);
                if (recipe == null)
                {
                    notRead.Add($"{card.Id} {card.Name}: {why}");
                    continue;
                }
                card.Extra ??= [];
                card.Extra["fusion"] = recipe.Materials;
                if (recipe.Dropped.Count > 0)
                    card.Extra["fusionLooser"] = string.Join("; ", recipe.Dropped);
                recipes++;
                attached.Add($"{card.Id} {card.Name}: fusion {recipe.Materials.ToJsonString()}" + (recipe.Dropped.Count > 0 ? "   [looser than the text]" : ""));
            }
            if (fusions.Count > 0)
                attached.Add($"Fusion recipes: {recipes} of {fusions.Count} written; not read: {notRead.Count}" +
                    (notRead.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, notRead.Take(15)) : ""));

            _status.Text = $"Attached {effects} effect(s) to {scripted} of {candidates.Count} card(s) ({partial} with text not run); {recipes} fusion recipe(s).";
            _status.ForeColor = Color.DarkGreen;
            MessageBox.Show(this, attached.Count == 0 ? "Nothing matched." : string.Join(Environment.NewLine, attached.Take(30)) + (attached.Count > 30 ? $"{Environment.NewLine}... and {attached.Count - 30} more" : ""),
                "Attached", MessageBoxButtons.OK, MessageBoxIcon.Information);
            BindSelected();
        }

        private void BindSelected()
        {
            if (_binding)
                return;

            if (SelectedGame is { } game)
            {
                SetEditorEnabled(true);
                BindGame(game);
                return;
            }
            var card = Selected;
            SetEditorEnabled(card != null);
            if (card == null)
            {
                _source.Text = "";
                _status.Text = "";
                _compiled.Text = "";
                return;
            }

            _binding = true;
            try
            {
                _source.Text = card.EffectSource;
                _compiled.Text = card.Effect?.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true }) ?? "(not compiled yet)";
                _status.Text = string.IsNullOrEmpty(card.EffectSource) ? "No script yet." : "Loaded. Edit and press Compile to update the compiled JSON below.";
                _status.ForeColor = SystemColors.ControlText;
            }
            finally { _binding = false; }
        }

        private static string Pretty(JsonObject json) =>
            json.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true });

        private void BindGame(GameEffectCard card)
        {
            _binding = true;
            try
            {
                if (card.Overridden)
                {
                    _source.Text = card.EffectSource;
                    _compiled.Text = card.Effect != null ? Pretty(card.Effect) : "(overridden with no effect: the card does nothing in duels)";
                    _status.Text = "Overridden: the game plays this instead of the card's own effect (effects.json, Yu-Gi-Oh-Effects). \"Use the game's effect\" undoes it.";
                }
                else
                {
                    bool pending = !string.IsNullOrEmpty(card.EffectSource);
                    _source.Text = pending ? card.EffectSource : card.GameScript;
                    _compiled.Text = "(the game's own effect)";
                    _status.Text = pending ? "Edited, not compiled yet: Compile to override the game's effect."
                        : card.GameScript.Length > 0 ? "The game's own effect, as the Effect library reads it. Edit it and Compile to override it in the game."
                        : "The game's own effect (no script reading of it). Write one and Compile to override it in the game.";
                }
                _status.ForeColor = SystemColors.ControlText;
            }
            finally { _binding = false; }
        }

        private void CompileGame(GameEffectCard card)
        {
            if (string.IsNullOrWhiteSpace(_source.Text))
            {
                if (MessageBox.Show(this, $"Take {card.Name}'s effect away? It will do nothing in duels (\"Use the game's effect\" undoes it).", "Effects",
                        MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                    return;
                card.EffectSource = "";
                card.Effect = null;
            }
            else
            {
                var result = EffectScriptCompiler.Compile(_source.Text);
                if (!result.Ok)
                {
                    _source.ShowError(result.Error);
                    _status.Text = "Not compiled - fix this first: " + result.Error;
                    _status.ForeColor = Color.Firebrick;
                    return;
                }
                card.EffectSource = _source.Text;
                card.Effect = result.Compiled;
            }
            card.Overridden = true;
            _overrides[card.Id] = card;
            RedrawSelected();
            BindGame(card);
            _status.Text = "Compiled: " + card.Name + "'s effect is overridden. Saved to Yu-Gi-Oh-Ex\\effects.json when you Save (Yu-Gi-Oh-Effects plays it).";
            _status.ForeColor = Color.DarkGreen;
        }

        private void Source_TextChanged(object? sender, EventArgs e)
        {
            if (_binding)
                return;
            if (SelectedGame is { } game)
            {
                game.EffectSource = _source.Text;   // kept with the card; it counts once compiled
                return;
            }
            var card = Selected;
            if (card == null)
                return;
            card.EffectSource = _source.Text;
        }

        private void btnCompile_Click(object? sender, EventArgs e) => Compile();

        private void Compile()
        {
            if (SelectedGame is { } game)
            {
                CompileGame(game);
                return;
            }
            var card = Selected;
            if (card == null)
                return;

            if (string.IsNullOrWhiteSpace(_source.Text))
            {
                card.Effect = null;
                _compiled.Text = "(no script - nothing for the game to read)";
                _status.Text = "Cleared: an empty script compiles to nothing.";
                _status.ForeColor = SystemColors.ControlText;
                return;
            }

            var result = EffectScriptCompiler.Compile(_source.Text);
            if (!result.Ok)
            {
                _source.ShowError(result.Error);
                _status.Text = "Not compiled - fix this first: " + result.Error;
                _status.ForeColor = Color.Firebrick;
                return;
            }

            card.Effect = result.Compiled;
            _compiled.Text = result.Compiled!.ToJsonString(new JsonSerializerOptions { TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(), WriteIndented = true });
            _status.Text = "Compiled OK. Saved with the rest of the cards when you use Save All.";
            _status.ForeColor = Color.DarkGreen;
        }
    }
}
