using System.Text.Json.Nodes;
using Types;
using Wolf.Editors;

namespace WolfEx
{
    /// <summary>
    /// What a compiled effect will do on this card that the compiler can't see (it never knows the card): a monster trigger on a Spell / Trap,
    /// several Activate effects on one Spell / Trap (the player picks 1), a Field / Continuous / Equip card's effect cloned where it can't last
    /// (issue #38). Short lines for the Effects page's status bar; none = nothing to say.
    /// </summary>
    internal static class EffectNotes
    {
        // "When this card is Summoned / flipped / battles": only a monster on the field is ever any of these
        private static readonly HashSet<string> MonsterTriggers = ["normal_summoned", "special_summoned", "summoned", "normal_or_special_summoned", "flip",
            "destroyed_by_battle", "destroys_by_battle", "battle_damage", "attack_declared", "attacked", "damage_step_battles", "damage_step_attacks"];

        // keys an as(...) clone may carry besides "from" (EffectScriptCompiler.CompileAs plus the effect block's own words)
        private static readonly HashSet<string> CloneKeys = ["from", "stats", "effectOnly", "label", "oncePerTurn", "oncePerTurnPerCopy", "limitGroup", "parts"];

        private static Dictionary<int, CardRecord>? _props;

        public static List<string> For(JsonObject compiled, string cardKind, string cardIcon)
        {
            var notes = new List<string>();
            bool spellTrap = cardKind is "Spell" or "Trap";
            var effects = new List<JsonObject> { compiled };
            if (compiled["parts"] is JsonArray parts)
                effects.AddRange(parts.OfType<JsonObject>());

            if (spellTrap)
            {
                foreach (var trigger in effects.Select(e => e["trigger"]?.GetValue<string>() ?? "").Where(MonsterTriggers.Contains).Distinct())
                    notes.Add($"\"on {trigger}\" is a monster's trigger: a {cardKind} is never summoned or in battle, so it never runs. Use an effect with no trigger (Activate).");
                var activations = effects.Where(e => e["trigger"] == null).ToList();
                if (activations.Count > 1 && activations.All(e => e["limitGroup"] == null))
                    notes.Add($"{activations.Count} Activate effects: the player picks 1 each time. To do them all, put the actions in one effect.");
            }

            foreach (var effect in effects.Where(e => e["from"] != null && e.Select(p => p.Key).All(CloneKeys.Contains)))
            {
                int from = effect["from"]!.GetValue<int>();
                if (Source(from) is not { } source || source.Kind is not (CARDS_INFO.CARD_Kind.Spell or CARDS_INFO.CARD_Kind.Trap))
                    continue;
                string? lasting = (source.Kind, source.Icon) switch
                {
                    (CARDS_INFO.CARD_Kind.Spell, 2) => "Field",
                    (_, 3) => "Equip",
                    (_, 4) => "Continuous",
                    _ => null,
                };
                if (lasting == null)
                    continue;
                string name = WolfX.Types.CardCatalog.NameOf(from);
                string kind = $"{lasting} {(source.Kind == CARDS_INFO.CARD_Kind.Spell ? "Spell" : "Trap")}";
                if (effects.Count > 1)
                    notes.Add($"{name} is a {kind}: its effect lasts while it is on the field, so it only works as the card's only effect.");
                else if (cardIcon != lasting || cardKind != (source.Kind == CARDS_INFO.CARD_Kind.Spell ? "Spell" : "Trap"))
                    notes.Add($"{name} is a {kind}: give this card that kind and icon, or it leaves the field before its effect can last.");
            }
            return notes;
        }

        // the game card's CARD_Prop record (kind, Spell / Trap icon), null without game files
        private static CardRecord? Source(int id)
        {
            if (_props == null)
            {
                if (GameFolderFiles.Current is not { } files)
                    return null;   // asked again once a game folder is open
                _props = [];
                try
                {
                    if (files.Read(CardPropTable.GamePath) is { } data)
                        foreach (var record in CardPropTable.Parse(data).Records)
                            _props.TryAdd(record.KonamiId, record);
                }
                catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
                {
                    // no notes about sources then
                }
            }
            return _props.TryGetValue(id, out var card) ? card : null;
        }
    }
}
