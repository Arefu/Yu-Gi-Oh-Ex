using System.Text.Json.Nodes;
using WolfX.Types;

namespace WolfEx
{
    /// <summary>
    /// The "Summoning" tab: the shared <see cref="SummonEditor"/> (the Card Manager shows the same one for game cards) on the card's Extra -
    /// "fusion", "ritualSpell", "ritualMonsters", "synchro", "xyz" - so the rest of the page and saving need nothing new.
    /// </summary>
    internal sealed partial class CardsPanel
    {
        private readonly SummonEditor _summon = new();

        private TabPage RequiredTab()
        {
            _summon.CardName = id => _cards.FirstOrDefault(c => c.Id == id)?.Name ?? CardCatalog.NameOf(id);
            _summon.CardIdByName = name => _cards.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))?.Id
                                           ?? EffectScriptCompiler.TryCardIdByName(name);
            _summon.CustomCards = () => _cards.Select(c => (c.Name, c.Archetypes)).ToList();
            var tab = new TabPage("Summoning") { UseVisualStyleBackColor = true };
            tab.Controls.Add(_summon);
            return tab;
        }

        /// <summary>Shows the part of the tab the card's kind uses, filled from the card.</summary>
        private void BindRequired()
        {
            if (Selected is not { } card)
            {
                _summon.Bind(null);
                return;
            }
            card.Extra ??= [];
            bool ritualSpell = card.Kind == "Spell" && card.Icon == "Ritual";
            bool hasEffect = card.Effect != null || !string.IsNullOrWhiteSpace(card.EffectSource);
            _summon.Bind(new SummonTarget
            {
                Id = card.Id,
                Name = card.Name,
                Kind = card.Kind,
                RitualSpell = ritualSpell,
                Description = card.Description,
                Level = card.Level,
                Data = card.Extra,
                RitualEffectNote = !ritualSpell ? null : hasEffect
                    ? "This card has an effect (Effects tab). It needs one that Ritual Summons, such as as(\"Black Luster Ritual\")."
                    : "! This card has no effect yet, so it does nothing in a duel. A Ritual Spell borrows the effect of one of the game's.",
                GiveRitualEffect = GiveRitualSpellEffect,
            });
        }

        /// <summary>Black Luster Ritual Ritual Summons the one monster its table row names, so cloned it summons whatever this card lists.</summary>
        private void GiveRitualSpellEffect()
        {
            if (Selected is not { } card)
                return;
            if (!string.IsNullOrWhiteSpace(card.EffectSource) && MessageBox.Show(this, "Replace this card's effect script with as(\"Black Luster Ritual\")?",
                    "Ritual Spell effect", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                return;
            const string script = "as(\"Black Luster Ritual\");";
            var result = EffectScriptCompiler.Compile(script);
            if (!result.Ok || result.Compiled == null)
            {
                MessageBox.Show(this, "Could not compile the effect: " + result.Error, "Ritual Spell effect", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            card.EffectSource = script;
            card.Effect = result.Compiled.DeepClone().AsObject();
            BindRequired();
            CardsChanged?.Invoke();   // the Effects tab shows the new script
        }
    }
}
