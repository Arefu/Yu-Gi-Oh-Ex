using System.Text.Json;
using System.Text.Json.Nodes;

namespace WolfEx
{
    /// <summary>
    /// Authors a card's EffectScript (Yu-Gi-Oh_Loader Plugins\Yu-Gi-Oh-Effects\Script\EffectScript.g4) and
    /// compiles it down to the plain JSON Yu-Gi-Oh-Effects reads at runtime - the game process never parses
    /// the DSL itself (see ygo-effects-moonshot-plan). Edits the same CardModel instances CardsPanel owns
    /// (shared by reference, not a copy) so CardsPanel's own SaveTo is still the only thing that writes
    /// cards.json - this tab has no file of its own and does not implement IContentPanel.
    /// </summary>
    internal sealed partial class EffectsPanel : UserControl
    {
        private CardsPanel? _cardsPanel;
        private bool _binding;

        public EffectsPanel()
        {
            InitializeComponent();
            AddBlocksTab();
            SetEditorEnabled(false);
            // the designer's distances were set while the page was still its default size, which squeezed the card list
            Load += (_, _) =>
            {
                split.SplitterDistance = Math.Clamp(Width * 28 / 100, 220, 320);
                right.SplitterDistance = Math.Clamp(right.Height * 62 / 100, 200, Math.Max(200, right.Height - 120));
            };
        }

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
            if (Selected == null)
                return false;
            _source.Text = script;
            _status.Text = "Template inserted for " + Selected.Name + ". Adjust it, then Compile.";
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

        private void Source_TextChanged(object? sender, EventArgs e)
        {
            if (_binding)
                return;
            var card = Selected;
            if (card == null)
                return;
            card.EffectSource = _source.Text;
        }

        private void btnCompile_Click(object? sender, EventArgs e) => Compile();

        private void Compile()
        {
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
