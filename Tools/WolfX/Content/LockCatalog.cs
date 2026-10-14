namespace WolfEx
{
    /// <summary>
    /// The game cards whose effect is a continuous lock. The game has no table for these: Duel_BuildContinuousLockState (0x14010EAA0) names
    /// them in code (docs/EffectSystem.md section 44), so build_effect_reference.py, which reads the effect tables, never finds them. The
    /// Effect library and the Effects page's game cards add them from here, with the lock(...) script that does the same.
    /// Complete = the card text is exactly that lock.
    /// </summary>
    internal static class LockCatalog
    {
        public sealed record LockCard(int Id, string Name, string Script, bool Complete, string Note);

        public static readonly LockCard[] Cards =
        [
            new(4758, "Jinzo", "lock(trap);", true, ""),
            new(5594, "Spell Canceller", "lock(spell);", true, ""),
            new(7580, "Jinzo - Lord", "lock(trap);", false, "its Summon rule is not in the script"),
            new(6166, "Silent Swordsman LV7", "lock(spell);", false, "its Summon rule is not in the script"),
            new(4866, "Royal Decree", "lock(trap, spelltrap_zone);", false, "the game negates only Traps activated after it, and never itself"),
            new(4960, "Imperial Order", "lock(spell, spelltrap_zone);", false, "the 700 LP upkeep is not in the script"),
            new(5984, "Talisman of Trap Sealing", "lock(trap, spelltrap_zone);", false, "its activation condition is not in the script"),
            new(5989, "Talisman of Spell Sealing", "lock(spell, spelltrap_zone);", false, "its activation condition is not in the script"),
            new(5740, "Skill Drain", "lock(monster, spelltrap_zone);", false, "the 1000 LP cost is not in the script"),
            new(6598, "Silent Insect", "lock(continuous_trap, continuous_spell);", false, "its position change is not in the script"),
            new(5269, "The Emperor's Holiday", "lock(equip_spell, spelltrap_zone);", false, ""),
            new(7096, "Destiny HERO - Plasma", "lock(monster, opponent);", false, "its Summon rule and other effects are not in the script"),
        ];

        public static LockCard? Find(int id) => Cards.FirstOrDefault(card => card.Id == id);
    }
}
