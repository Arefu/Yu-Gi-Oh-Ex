namespace WolfEx
{
    /// <summary>
    /// The game cards whose effect is a continuous lock. The game has no table for these: Duel_BuildContinuousLockState (0x14010EAA0) names
    /// them in code (docs/EffectSystem.md section 44), so build_effect_reference.py, which reads the effect tables, never finds them. The
    /// Effect library (its "Continuous locks" section) and the Effects page's game cards add them from here, with the lock(...) script that
    /// does the same. Every card the builder names is listed (checked against the decompile 2026-10-14). The narrow monster locks are lock words
    /// of their own (lock(light) ...: one slot each of the lock state). The this-turn locks and exemptions are flag markers the game keys on the
    /// card's id, so a card that does one borrows that card whole with as(...) (source-id lending, docs/EffectSystem.md section 32).
    /// Complete = the card text is exactly that lock.
    /// </summary>
    internal static class LockCatalog
    {
        public sealed record LockCard(int Id, string Name, string Group, string Script, bool Complete, string Note)
        {
            /// <summary>The script is a real lock(...) statement (not only a comment).</summary>
            public bool IsLock => Script.StartsWith("lock(", StringComparison.Ordinal);
        }

        public const string Traps = "Trap locks", Spells = "Spell locks", Monsters = "Monster effect locks", Types = "Spell/Trap kind locks",
            Narrow = "Narrow monster locks", ThisTurn = "This-turn locks", Exempt = "Exemptions (borrowed with as)";

        /// <summary>The library's order of the groups.</summary>
        public static readonly string[] Groups = [Traps, Spells, Monsters, Types, Narrow, ThisTurn, Exempt];

        public static readonly LockCard[] Cards =
        [
            // lock state +0 / +4: Traps can't be activated and are negated
            new(4758, "Jinzo", Traps, "lock(trap);", true, "with Amplifier (4819) equipped, only the opponent's Traps (the game's exemption)"),
            new(7580, "Jinzo - Lord", Traps, "lock(trap);", false, "its Summon rule and its destroy-Traps effect are not in the script"),
            new(4866, "Royal Decree", Traps, "lock(trap);", false, "the game negates only Traps activated after it, and never itself"),
            new(5984, "Talisman of Trap Sealing", Traps, "lock(trap);", false, "its activation condition (Sealmaster Meisei) is not in the script"),
            new(7114, "Sky Scourge Invicil", Traps, "lock(trap);", false, "the game picks Spells or Traps by what was Tributed (LIGHT Fairy: Spells, DARK Fiend: Traps); the script always locks Traps"),

            // +12 / +16: Spells
            new(5594, "Spell Canceller", Spells, "lock(spell);", true, ""),
            new(6166, "Silent Swordsman LV7", Spells, "lock(spell);", false, "its Summon rule is not in the script"),
            new(4960, "Imperial Order", Spells, "lock(spell);", false, "the 700 LP upkeep is not in the script"),
            new(5989, "Talisman of Spell Sealing", Spells, "lock(spell);", false, "its activation condition (Sealmaster Meisei) is not in the script"),

            // +20..+32: monster effects
            new(5740, "Skill Drain", Monsters, "lock(monster);", false, "the 1000 LP cost is not in the script"),
            new(7096, "Destiny HERO - Plasma", Monsters, "lock(monster, opponent);", false, "its Summon rule and other effects are not in the script"),

            // +88..+100: kinds of Spell/Trap
            new(6598, "Silent Insect", Types, "lock(continuous_trap, continuous_spell);", false, "its position change is not in the script"),
            new(5269, "The Emperor's Holiday", Types, "lock(equip_spell);", false, "the card negates every Equip Card; the script only Equip Spells"),

            // +52..+84 and the Spellstone loop: monster locks with a filter lock(...) has no word for
            new(6801, "Alien Mars", Narrow, "lock(a_counter);", false, "the game exempts the original Alien Mars by id; a custom card is not exempted (it has no A-Counter unless one is placed)"),
            new(8179, "Brain Golem", Narrow, "lock(light);", false, "its attack-again effect is not in the script; the game exempts a few LIGHT cards by id (7838, 10823, 11310)"),
            new(11486, "Nekroz of Unicore", Narrow, "lock(from_extra_deck);", false, "its Ritual Summon rule and its discard effect are not in the script"),
            new(12316, "Amorphactor Pain, the Imagination Dracoverlord", Narrow, "lock(fusion_synchro_xyz);", false, "its skip-Main-Phase-1 and search effects are not in the script"),
            new(14476, "Beatraptor", Narrow, "lock(extra_monster_zone);", false, "the game also needs it in a Main Monster Zone; its battle effect is not in the script"),
            new(11105, "The Monarchs Erupt", Narrow, "lock(except_tribute_summoned);", false, "its activation condition and End Phase self-send are not in the script"),
            new(8837, "Corridor of Agony", Narrow, "lock(from_main_deck);", false, "the can't-attack part is not in the script"),
            new(11750, "Lose 1 Turn", Narrow, "lock(summoned_this_turn);", false, "its activation condition and the change to Defense Position are not in the script"),
            new(9155, "Powersink Stone", Narrow, "as(\"Powersink Stone\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(10821, "Moon Dance Ritual", Narrow, "as(\"Moon Dance Ritual\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),

            // flag markers: an activated effect locks for the rest of the turn
            new(8223, "Trap Stun", ThisTurn, "lock_turn(trap);", true, ""),
            new(9592, "Gishki Emilia", ThisTurn, "as(\"Gishki Emilia\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(6208, "Triangle Ecstasy Spark", ThisTurn, "as(\"Triangle Ecstasy Spark\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(12013, "Dark Contract with Errors", ThisTurn, "as(\"Dark Contract with Errors\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(13551, "White Howling", ThisTurn, "as(\"White Howling\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(5753, "Judgment of the Pharaoh", ThisTurn, "as(\"Judgment of the Pharaoh\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(7275, "Elemental HERO Chaos Neos", ThisTurn, "as(\"Elemental HERO Chaos Neos\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(8313, "Koa'ki Meiru Hydro Barrier", ThisTurn, "as(\"Koa'ki Meiru Hydro Barrier\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(4799, "Gorgon's Eye", ThisTurn, "lock_turn(defense_position);", true, ""),
            new(11096, "Forbidden Scripture", ThisTurn, "as(\"Forbidden Scripture\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(14122, "Update Jammer", ThisTurn, "as(\"Update Jammer\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(11994, "D/D/D Duo-Dawn King Kali Yuga", ThisTurn, "as(\"D/D/D Duo-Dawn King Kali Yuga\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(10298, "Magic Deflector", ThisTurn, "lock_turn(spell);", true, ""),
            new(4942, "Armored Glass", ThisTurn, "as(\"Armored Glass\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(4943, "World Suppression", ThisTurn, "as(\"World Suppression\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(4944, "Mystic Probe", ThisTurn, "as(\"Mystic Probe\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(4945, "Metal Detector", ThisTurn, "as(\"Metal Detector\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),

            // +112 / +116 and Duel_IsCardExemptFromLocks: cards that get around a lock
            new(11274, "Magical Spring", Exempt, "as(\"Magical Spring\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(4819, "Amplifier", Exempt, "as(\"Amplifier\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
            new(9156, "Tyrant's Temper", Exempt, "as(\"Tyrant's Temper\");", false, "borrows the whole card (as): the game keys this lock on the card's id; untested in a duel"),
        ];

        public static LockCard? Find(int id) => Cards.FirstOrDefault(card => card.Id == id);
    }
}
