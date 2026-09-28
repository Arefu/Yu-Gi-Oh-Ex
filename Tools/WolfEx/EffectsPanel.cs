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
            SetEditorEnabled(false);
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
            _compiled.Text = result.Compiled!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            _status.Text = $"Applied to {cards.Count} card(s). Saved with the rest of the cards when you use Save All.";
            _status.ForeColor = Color.DarkGreen;
        }

        /// <summary>
        /// Reads each Normal Spell's card text and attaches the effect when the FIRST sentence is exactly a supported one (EffectTextMatcher).
        /// With several cards selected only those are looked at, otherwise all cards. Cards that already have a script are left alone.
        /// </summary>
        private void btnAuto_Click(object? sender, EventArgs e)
        {
            if (_cardsPanel == null)
                return;
            var selected = CheckedCards;
            var candidates = (selected.Count > 0 ? selected : _cardsPanel.Cards.ToList())
                .Where(card => card.Kind == "Spell" && card.Icon == "Normal" && string.IsNullOrEmpty(card.EffectSource)).ToList();
            if (MessageBox.Show(this, $"Look at the text of {candidates.Count} Normal Spell(s) without a script and attach the effect where the text is a plain supported effect?",
                    "Effects", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;

            var attached = new List<string>();
            foreach (var card in candidates)
            {
                var match = EffectTextMatcher.TryMatch(card.Description);
                if (match == null)
                    continue;
                var compiled = EffectScriptCompiler.Compile(match.Script);
                if (!compiled.Ok)
                    continue;

                card.EffectSource = match.Script;
                card.Effect = compiled.Compiled;
                if (match.Rest.Length > 0)
                {
                    card.Extra ??= [];
                    card.Extra["effectNotImplemented"] = match.Rest;
                }
                attached.Add($"{card.Id} {card.Name}: {match.Script}" + (match.Rest.Length > 0 ? "   [+ text not run]" : ""));
            }

            _status.Text = $"Attached {attached.Count} of {candidates.Count}.";
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
                _compiled.Text = card.Effect?.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) ?? "(not compiled yet)";
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
            _compiled.Text = result.Compiled!.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            _status.Text = "Compiled OK. Saved with the rest of the cards when you use Save All.";
            _status.ForeColor = Color.DarkGreen;
        }
    }
}
