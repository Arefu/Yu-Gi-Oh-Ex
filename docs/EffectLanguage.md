# EffectScript - writing card effects

Goal: a custom card gets its effect by one short line, and effects can be chained, without touching C++.

```
search(deck, monster where race = Dragon and level <= 4);
revive(grave, monster where attribute = Dark);
destroy(all, opponent, monster where level >= 7);
draw(2);
draw(1) then search(deck, monster where race = Zombie);   // draw / LP steps first, then one action
```

A script is just the effect itself, no wrapper needed (`effect "Name" { ... }` around it means the same and is optional). Written on WolfX's **Effects** page, compiled there (ANTLR, `Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Effects/Script/EffectScript.g4`), saved on the card as `effectScript` (the text) and `effectClone` (what the game runs). The plugin never sees the text.

## Why it stays simple

Every action compiles to "borrow the handlers of a vanilla card that already does this, and change its parameters" (docs/EffectSystem.md sections 10-12). The parameters are the same vocabulary the game itself uses for filters, so the language is just that vocabulary with readable names:

| action | borrows | parameters written by the compiler |
|---|---|---|
| `draw(N)` | Pot of Greed 4844 | `draw` |
| `search(deck, selector)` | Reinforcement of the Army 5328 | `deck` filter (`scan: deck`) |
| `revive(grave \| opponent_grave \| either_grave, selector)` | Monster Reborn 4842 | `deck` filter with a graveyard scan |
| `destroy(all, own \| opponent \| any, selector)` | Warrior Elimination 4659 | `filter` (one condition) |

A **selector** is `[monster|spell|trap|card] [where] condition {and condition}` with conditions on `race` (`= Dragon`), `attribute` (`= Dark`), `level` (`= <= >= < >`). destroy takes one condition (the game's filter rows match a single property; `archetype = <code>` is also allowed there).
Race and attribute names are the game's (Dragon ... Creator God; Light, Dark, Water, Fire, Earth, Wind, Divine), case, spaces and "-Type" are ignored.

## Adding a new kind of action (the recipe)

1. Find vanilla cards that do it (`effect_tables.xlsx`, `filter_rows.py`, `deck_filter_table.py`).
2. Decompile their slot 0/2/4 handlers; find the id-keyed table or ladder that supplies the parameter.
3. Hook that lookup for the active clone in `EffectClone.cpp` (see `Hook_GetDraw`, `Hook_GetRow`, `Hook_Collect`).
4. Add the JSON key to `EffectClone.cpp`, one grammar rule, and one branch in `EffectScriptCompiler.cs`.
5. Add a test card (a small test cards.json), rename, retype and comment what you found in IDA, note it in EffectSystem.md, and add the BAB block (`Tools\WolfX\Content\Blocks\effect-blocks.js`).

## Current actions (all confirmed in a duel except where noted)

| script | does |
|---|---|
| `draw(N)` | draw N |
| `gain_lp(N)` / `burn(N)` | gain N Life Points / inflict N damage to the opponent |
| `search(deck, selector)` | add 1 matching card from the Deck to the hand |
| `revive(grave \| opponent_grave \| either_grave, selector)` | Special Summon 1 matching monster from a Graveyard |
| `send_to_grave(deck, selector)` | send 1 matching card from the Deck to the Graveyard |
| `destroy(all, own \| opponent \| any, selector)` | destroy all matching monsters |
| `destroy(target, side, selector)` | choose 1 matching card (monster, or `spell` for a Spell/Trap) and destroy it |
| `equip(atk N[, def N])` | Equip Spell: the equipped monster gains N ATK / DEF (borrows Axe of Despair; the card needs the Equip icon) |
| `as(<id> \| "Card name") [with atk N, def N];` | the card behaves exactly like that game card. In a duel it plays under that card's id whenever the game card is in neither deck (EffectSystem.md section 32), so EVERYTHING the game knows about that card applies - continuous effects, field spells, restrictions, the AI. `with` sets its own ATK/DEF amounts for equip / boost cards |

Selectors also take `archetype = "Dark World"` (a name from Archetypes.json, or a code) and `name = <Konami id>` (search / send only; built, not yet played). Chaining with `then`: draw / gain_lp / burn steps first, then one action (works, confirmed).

## Triggers (monster effects)

```
on sent_to_grave: search(deck, monster where atk <= 1500);
on flip: burn(500);
```
`on <trigger>:` makes it a monster effect: `normal_summoned`, `special_summoned`, `summoned`, `normal_or_special_summoned`, `flip`, `destroyed_by_battle`, `sent_to_grave`, `sent_from_field_to_grave`, `standby_phase`, `end_phase`, `destroys_by_battle`, `battle_damage`, `attack_declared` (no game card to borrow from yet), `ignition` ("Once per turn: you can ..."). The compiler borrows a vanilla MONSTER that has that trigger (by its card text) and that action; `Tools\WolfEx\trigger_sources.json` lists what exists (e.g. there is no vanilla "on summoned: draw", so that pair is an error that names the triggers the game does have for the action). Not written = a Spell. `gain_lp` borrows a `burn` source (the life point handler takes any amount). Summon triggers are confirmed in a duel; `flip` (a Flip Summon, or a set monster flipped by an attack or effect), `destroyed_by_battle`, `sent_to_grave` and `sent_from_field_to_grave` are hooked since 2026-10-01 but not yet played (`tests\12_flip_and_grave.json`, EffectSystem.md section 29). "When this card declares an attack" and "inflicts battle damage" triggers are not supported yet; the compiler output has been checked for nine scripts headlessly (`WolfEx.exe --compile <script file> <result file>`).

**Ctrl+Space** in the editor lists what can follow at the caret (triggers after `on`, actions after `then`, races after `race =`, archetype names after `archetype =`, ...) with a hint beside the highlighted entry; typing two letters offers it by itself.

## In WolfX (Effects page)

The card list has a checkbox per card (plus "Check all shown" with the filter box, "Clear all checks"). Write a script in the editor (Scintilla: colours, line numbers, keyword completion, red squiggle at the compile error), **Apply script to all checked**. **Attach from card text...** reads the text of every Normal Spell (or of the checked cards) and attaches the effect where its first sentence is a plain supported effect; the remaining sentences are stored in `effectNotImplemented` on the card.

**Blocks** (the first tab since 2026-10-14; the Script tab is the text behind them): one **hat block per effect**, headlined by when it happens - "When this card is [Summoned] from / by / as", "When [this card is flipped / destroyed by battle / sent to the GY / declares an attack ...]", "During [your Standby Phase / the End Phase]", "You can (ignition)", "Activate (a Spell / Trap, or a self cost)". Every hat has the same slots: name (the effect's name, for the choose-an-effect prompt), only if, cost, do (actions, run in order = then), only once per turn. Several effects = several hats, read top to bottom. Beside them: "act exactly like the game card" (as) and the Lock block. Changing the blocks rewrites the script; typing in the script redraws them. A new action = one entry in `Tools\WolfX\Content\Blocks\effect-blocks.js` (ACTIONS / COSTS); a new trigger = an entry in HATS' lists. Until then a script using it shows a note over the (locked) blocks and is left alone. See docs/WolfXContent.md.

**Effect library tab:** the game's own cards written in EffectScript wherever the language can say it (about 1170 cards; ~250 are exactly the one effect, the rest carry a "~" and a note of what the script does not cover - e.g. "must have the same name", "ATK <= 1500"). Monster cards show their trigger as a comment. Select a card to read its text and script; **Use as template** puts the script into the Effects tab for the card selected there. The data is `Tools\WolfX\Content\effect_reference.json`, made from the game's tables by `docs\effect-scripts\build_effect_reference.py` (re-run it after learning to decode a new effect type). `WolfX.exe --attach-effects <cards.json> <report> <work csv>` attaches effects from card text to a whole cards.json without opening the editor.

## Chaining ("then") - what is built and what is not

Stage 1 (built, confirmed): draw / life point steps run back to back ahead of the card's last action. Stage 2 (built 2026-10-21, not run in a duel): any action can come before `then` (`search(...) then special_summon(...)`); each runs its own game machine to the end before the next starts (EffectSystem.md section 53). Only the last action may target.

## Also planned

- `discard(N)`, `send(deck -> grave, selector)`, `banish`, `gain_lp(N)` / `pay_lp(N)`, `special_summon(hand, selector)`, `negate` - each needs its vanilla source found first (the backlog groups in `effects_backlog_grouped.xlsx` say which are most common: search 765, destroy 337, draw 255, burn 205).
- (Some of the above are built since: discard / Tribute / banish-self costs, special summon from the hand, negate. What's still missing is in [GLOSSARY.md](../GLOSSARY.md#effects) and `docs/Card Lists/Effects To Write.csv`.)

## Costs paid with the card, conditions, Special Summon this card, ATK (2026-10-02, EffectSystem.md section 37)

```
cost banish_self: draw(1);                      // in the GY: "You can banish this card from your GY; draw 1 card"
cost discard_self: search(deck, monster where race = Warrior);   // in the hand: "You can discard this card; ..."
cost tribute_self: special_summon(hand, monster where level <= 4);   // on the field: "You can Tribute this card; ..."
if no_monsters: special_summon(self);           // "If you control no monsters: You can Special Summon this card from your hand"
if controls(monster where race = Warrior): special_summon(self);   // "If you control a Warrior monster: ..."
cost pay_lp(1000): special_summon(self_grave);  // "If this card is in your GY: You can pay 1000 LP; Special Summon this card"
gain_atk(700);                                  // "Target 1 face-up monster; it gains 700 ATK until the end of this turn" (gain_atk(own, N) = one you control)
```

A self cost already says where the effect is activated, so it takes no `on ...:` trigger; `special_summon(self)` / `self_grave` neither. `if controls(...)` uses the effect's one filter row, so it cannot be combined with destroy / banish.

**From card text:** the Effects tab's "Attach from card text..." (`CardTextTranslator.cs`) writes these scripts from a card's real text, every effect it can, and keeps the rest in `effectNotImplemented` / `effectLooser`.

Also (section 38): `cost detach(N):` (Xyz Monster ignition), `cost tribute(1):`, `return_to_hand(target, own | opponent | any, selector)`.

## Locks (2026-10-11, EffectSystem.md section 44)

`lock(...)` is a continuous effect that sits beside the card's other effects. While the card is face-up in its zone and not negated, the
chosen cards can't be activated and are negated on the field:

    lock(trap);                       // Jinzo
    lock(spell);                      // Spell Canceller
    lock(monster, spelltrap_zone);    // Skill Drain
    lock(trap, opponent);             // only the opponent's Traps
    lock(continuous_trap, field_spell, any_zone);

* What it locks: trap, spell, monster (monster effects), continuous_trap, continuous_spell, field_spell, equip_spell.
* Whose cards: both (the default), opponent, own.
* Where this card has to be: monster_zone (the default), spelltrap_zone, any_zone.

A card has one lock. It compiles to `"lock"` in the card's JSON, which Yu-Gi-Oh-Effects Lock.cpp reads.

## Summon conditions (2026-10-11, EffectSystem.md section 46)

A summon trigger can say how the card must have been summoned, in brackets after it:

    on special_summoned(from grave): draw(1);                 // "If this card is Special Summoned from the GY"
    on special_summoned(from hand, by effect): draw(1);       // "... from your hand by a card effect"
    on special_summoned(xyz): search(deck, monster where race = Dragon);   // "If this card is Xyz Summoned"
    on normal_summoned(tribute 2): draw(1);                   // Tribute Summoned with 2 or more Tributes
    on special_summoned(by "Dark World"): draw(1);            // by the effect of a "Dark World" card

* `from hand | deck | extra | grave | banished | spelltrap` (several = any of them)
* `by effect | not_effect | own | spell | trap | monster | "Archetype" | <card id>` (one)
* `fusion | synchro | xyz | ritual | link` = Summoned properly as its kind; `pendulum`; `tribute [N]`

Only for normal_summoned, special_summoned, summoned and normal_or_special_summoned. Built, not yet played.

## Except this card (2026-10-14)

A search / revive / add_to_hand / special_summon(hand|deck|grave, ...) **never picks a card with this card's own name** - "except <this card>", the way real cards word it. That is the default; end the selector with `including self` to allow it:

    on sent_from_field_to_grave: revive(grave, monster where level <= 4);                 // never revives itself
    on sent_from_field_to_grave: revive(grave, monster where level <= 4 including self);  // may

Compiles to `"exceptSelf": true` in the `"deck"` filter. Yu-Gi-Oh-Effects sets the game row's name test (param = minus the id the card plays under this duel), so every copy with the same name is left out. With an `archetype =` or `name =` condition (the row's one name test is taken) the plugin drops this card's name from the matches after the scan instead (EffectSystem.md section 47). Blocks: the selector's "except this card" checkbox (ticked by default).

## Or (2026-10-14)

search / revive / add_to_hand / special_summon / send_to_grave take several selectors joined with `or` (any of them): `search(deck, monster where race = Warrior or monster where race = Dragon)`. Compiles to the first filter plus `"or": [...]`; Yu-Gi-Oh-Effects runs one scan per alternative and keeps each card once. Limit: the game's own "is there any match" checks that read the filter row directly see only the first alternative. BAB: the "any of ... or ..." block in Which cards (nest it for three or more).

## Hand size limit (2026-10-14)

    hand_limit(3, opponent);             // while face-up in a Monster Zone: your opponent discards down to 3 in the End Phase
    hand_limit(4, both, spelltrap_zone); // both players, while face-up in a Spell & Trap Zone

Who (required, no default): opponent, own, both. Where: monster_zone (default), spelltrap_zone, any_zone. The lowest limit on the field wins; not while this
card's effects are negated. BAB: "While this card is face-up in ... : ... hand size limit is N" (While face-up category). Compiles to `"handLimit"`.

## Effect labels (2026-10-14)

A card with several effects of the same kind asks the player which one to use. The options show each effect's name (`effect "Draw" { ... }`, the
hat's "name" slot in BAB), or a short summary of what it does when it has none.

## For the rest of this turn (2026-10-14)

    lock_turn(trap);                              // a Spell: "Negate all Trap effects on the field this turn" (Trap Stun's way)
    on normal_summoned: lock_turn(defense_position);   // "the effects of Defense Position monsters are negated this turn"
    lock_turn(spell);                             // Equip, Field, Continuous and Quick-Play Spells (Magic Deflector)

An action like any other (BAB: Actions > "for the rest of this turn, negate ..."). See EffectSystem.md section 48.

## Whose draw / life points, and chains of them (2026-10-14)

    draw(1, opponent);                         // your opponent draws 1
    gain_lp(500, any);                         // both players gain 500
    damage(800, own);                          // you take 800 (damage defaults to your opponent, like burn)
    gain_lp(500) then draw(1) then damage(300);   // any chain of these, under any trigger

Run by Yu-Gi-Oh-Effects itself (EffectSystem.md section 50). BAB: the "whose" dropdown on draw / gain LP / damage.
