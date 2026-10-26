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
| `special_summon(hand \| deck \| grave \| banished \| extra [or ...], selector)` | Special Summon 1 matching monster from your hand / Deck / GY / face-up banished cards / Extra Deck. Join zones with `or` for one list over all of them: `special_summon(hand or deck or grave, monster where level <= 4)` ("from your hand, Deck, or GY"). One of hand / deck / grave keeps its proven game card; several zones, banished or extra use the plugin's own summon step (EffectSystem.md section 57). WolfX blocks: "Special Summon from any of" with a box per zone |
| `send_to_grave(deck, selector)` | send 1 matching card from the Deck to the Graveyard |
| `destroy(all, own \| opponent \| any, selector)` | destroy all matching monsters |
| `destroy(target, side, selector)` | choose 1 matching card (monster, or `spell` for a Spell/Trap) and destroy it |
| `equip(atk N[, def N])` | Equip Spell: the equipped monster gains N ATK / DEF (borrows Axe of Despair; the card needs the Equip icon) |
| `as(<id> \| "Card name") [with atk N, def N];` | the card behaves exactly like that game card. In a duel it plays under that card's id whenever the game card is in neither deck (EffectSystem.md section 32), so EVERYTHING the game knows about that card applies - continuous effects, field spells, restrictions, the AI. `with` sets its own ATK/DEF amounts for equip / boost cards |
| `as(...) effect_only;` | copy ONLY the effect (cards.json `"effectOnly": true`): the card is never lent the game card's id, so it keeps its own kind (monster / Spell / Trap), stats and identity, and the id-keyed rules of the source do not apply. The effect runs through the Effects hooks (section 32's fallback) |
| `on <trigger>: as(...) [effect_only];` | the card's own monster trigger runs the game card's action: the row comes from a game monster with that trigger, the action from the game card (`actionFrom`, EffectSystem.md section 33). This is how a monster uses a Spell's or Trap's effect, e.g. `on ignition: as("Ojama Trio") effect_only;`. WolfX blocks: the "act exactly like" block's "copy only the effect" box and "used when" list |

Selectors also take `archetype = "Dark World"` (a name from Archetypes.json, or a code) and `name = <Konami id>` (search / send only; built, not yet played). Chaining with `then`: draw / gain_lp / burn steps first, then one action (works, confirmed).

**Several effects.** `a; also b;` = two effects on one card. `a; or b;` (or `effect "A" { ... } or effect "B" { ... }`) = "activate 1 of these effects": the player picks one, and the joined effects share ONE once-per-turn limit (`once_per_turn` on any of them limits the group). Up to 4 effects per `or` group (the game's prompt shows 4). Example, "Once per turn: You can activate 1 of these effects. ● Draw 1 card. ● Gain 1000 LP.": `on ignition: draw(1); once_per_turn; or on ignition: gain_lp(1000);`. A card can have up to 4 independent once-per-turn limits (EffectSystem.md section 58). WolfX blocks: the "or the next effect (activate 1 of these)" box on an effect hat.

**Once per turn.** `once_per_turn;` = "You can only use this effect of X once per turn" (per card name). `once_per_turn(copy);` = "Once per turn: ..." (each copy on the field, once per turn; the game only counts it while the card is on the field). Section 59.

**Whose phase.** `on standby_phase:` / `on end_phase:` = YOUR Standby / End Phase. `on opponent_standby_phase:`, `on each_standby_phase:`, `on opponent_end_phase:`, `on each_end_phase:` = your opponent's / either player's ("during the End Phase" in card text is either player's). Monsters on the field. Section 60.

## Triggers (monster effects)

```
on sent_to_grave: search(deck, monster where atk <= 1500);
on flip: burn(500);
```
`on <trigger>:` makes it a monster effect: `normal_summoned`, `special_summoned`, `summoned`, `normal_or_special_summoned`, `flip`, `destroyed_by_battle`, `sent_to_grave`, `sent_from_field_to_grave`, `standby_phase`, `end_phase`, `destroys_by_battle`, `battle_damage`, `attack_declared`, `ignition` ("Once per turn: you can ..."), plus the triggers in "More triggers" below (`banished`, `in_hand`, `in_grave`, `opponent_attack_declared`, `attacked`, the summon reactions, the End Phase / Damage Step ones). The compiler borrows a vanilla MONSTER that has that trigger (by its card text) and that action; `Tools\WolfX\Content\trigger_sources.json` lists what exists (e.g. there is no vanilla "on summoned: draw", so that pair is an error that names the triggers the game does have for the action). Not written = a Spell. `gain_lp` borrows a `burn` source (the life point handler takes any amount). Summon triggers are confirmed in a duel; `flip` (a Flip Summon, or a set monster flipped by an attack or effect), `destroyed_by_battle`, `sent_to_grave` and `sent_from_field_to_grave` are hooked since 2026-10-01 but not yet played (`tests\12_flip_and_grave.json`, EffectSystem.md section 29). "When this card declares an attack" is an own trigger since 2026-10-25 (section 67); the phase, move, attack and summon-reaction triggers with no exact game card compile to the plugin's own trigger (`ownTrigger`, sections 61 and 65-68) instead of borrowing a monster. The compiler output has been checked for nine scripts headlessly (`WolfX.exe --compile <script file> <result file>`).

### More triggers (2026-10-21 / 10-25)

| script | card text | EffectSystem.md |
|---|---|---|
| `on in_grave:` | "If this card is in your GY: You can ..." (add `quick;` for a Quick Effect) | section 51 |
| `on in_hand:` | "If this card is in your hand: You can ..." (`on in_hand: X; or on in_grave: X;` for "in your hand or GY") | section 66 |
| `on banished:` / `on banished(from grave):` | "If this card is banished (from the GY)" | section 65 |
| `on sent_to_grave(from hand, from field, by effect):` | the move triggers take conditions: from hand / deck / grave / field / monster / spelltrap, by battle / effect | section 65 |
| `on opponent_attack_declared:` / `on attacked:` | "When an opponent's monster declares an attack" / "When this card is targeted for an attack" | section 67 |
| `on opponent_special_summoned:` / `on opponent_summoned:` / `on monster_special_summoned:` / `on monster_summoned:` | "If your opponent Special Summons a monster(s)" / "... Normal or Special Summons ..." / "If a monster(s) is Special Summoned" / "... Normal or Special Summoned" | section 68 |
| `on monster_summoned(is monster where race = Insect, own):` | the summon reactions also take the summoned monster (`is <selector>`: Type / Attribute / archetype / kind) and whose field it went to (`own` / `opponent`): "If an Insect monster(s) is Normal or Special Summoned to your field" | section 87 |
| `on card_destroyed(is monster where race = Insect, own, by battle):` / `on card_left_field(own, other, by opponent):` / `on card_sent_to_grave(is monster, opponent, from hand, from deck, in_grave):` / `on card_banished:` | ANOTHER card is destroyed / leaves the field / is sent to the GY / is banished: "If a face-up Insect monster(s) you control is destroyed by battle", "If another card(s) you control leaves the field by an opponent's card effect", "If a monster(s) is sent from your opponent's hand or Deck to the GY, while this card is in your GY". `is` (race / attribute / archetype / name), `own` / `opponent` (whose card), `from` field / monster / spelltrap / hand / deck / grave, `by battle` / `effect` / `opponent`, `other`, `in_grave` (this card watches from the GY) | section 91 |
| `on grave_end_phase:` / `on banished_end_phase:` | "During the End Phase, if this card is in the GY (banished) because it was sent there this turn" | section 79 |
| `on damage_step_battles:` / `on damage_step_attacks:` | "At the start of the Damage Step, if this card battles / attacks" | section 80 |

Summon triggers take how the card was summoned: `on special_summoned(from grave, by effect):`, `on normal_summoned(tribute 2):`, `on special_summoned(synchro):`.

**Only during the Battle Phase.** `if battle_phase: ...; quick;` (or `your_battle_phase`, `opponent_battle_phase`): "During the (your / your opponent's) Battle Phase". A monster's one must be a Quick Effect (section 81).

**Use limits.** Besides `once_per_turn`: `once_per_duel;` and `uses_per_turn(N);` ("twice per turn", "thrice per turn", section 74).

### More actions (2026-10-25)

| script | does | EffectSystem.md |
|---|---|---|
| `set(deck, selector)` | "Set 1 X Spell/Trap directly from your Deck" (the game's set-from-Deck machine) | section 63 |
| `fusion_summon(selector)` | "Fusion Summon 1 (X) Fusion Monster from your Extra Deck, using monsters from your hand or field as material" (Polymerization's machine, the Extra Deck list filtered). Not yet: fusions that banish / shuffle materials from the GY | section 64 |
| `add_to_hand(grave \| banished, selector)` | add 1 matching card from your GY / banished cards to your hand | |
| `banish(all \| target, side, selector)` / `banish(deck \| hand \| extra \| grave, selector)` | banish matching cards on the field, or 1 card from a zone | section 55 |
| `return_to_hand(target, side, selector)` | target 1 card on the field and return it to the hand | |
| `gain_atk(side, N)` | target 1 face-up monster: it gains N ATK until the end of this turn | |
| `damage(N, side)` | N damage to the named player (default your opponent, like `burn`) | |
| `change_position(target[, side][, selector][, attack \| defense])` | target 1 monster: change its battle position (`attack` = only one in Attack Position: "change it to Defense Position") | section 92 |
| `set_face_down(target[, side][, selector])` | target 1 monster: change it to face-down Defense Position | section 92 |
| `take_control(target[, side][, selector])` | target 1 monster: take control of it until the End Phase | section 92 |
| `negate_effects(target[, side][, selector])` | target 1 face-up monster: negate its effects until the end of this turn | section 92 |
| `change_atk(target[, side][, selector][, position], [-]N)` | target 1 face-up monster: it gains (`-`: loses) N ATK until the end of this turn | section 92 |
| `if a and b: ...` | several conditions; new: `main_phase`, `your_main_phase`, `opponent_main_phase`, `main_or_battle_phase` (+ your_ / opponent_), `your_turn`, `opponent_turn`, `battled` (this card battled this turn) | section 93 |
| `fusion_summon(selector, banish_materials)` | "Fusion Summon 1 X Fusion Monster, by banishing the materials listed on it from your field or GY" (Dragon's Mirror's route) | section 94 |
| `xyz_also(selector[, xyz_monster][, rank N \| rank_at_least N \| rank_at_most N]);` | "You can also Xyz Summon this card by using 1 X you control" (card-level, like only_one) | section 94 |
| `on battle_phase_start:` / `your_battle_phase_start` / `opponent_battle_phase_start` / `battle_phase_end` / `your_battle_phase_end` | at the start / end of the (your / your opponent's) Battle Phase (a monster on the field) | section 93 |

**Ctrl+Space** in the editor lists what can follow at the caret (triggers after `on`, actions after `then`, races after `race =`, archetype names after `archetype =`, ...) with a hint beside the highlighted entry; typing two letters offers it by itself.

## In WolfX (Effects page)

The card list has a checkbox per card (plus "Check all shown" with the filter box, "Clear all checks"). Write a script in the editor (Scintilla: colours, line numbers, keyword completion, red squiggle at the compile error), **Apply script to all checked**. **Attach from card text...** reads the text of every Normal Spell (or of the checked cards) and attaches the effect where its first sentence is a plain supported effect; the remaining sentences are stored in `effectNotImplemented` on the card.

**Blocks** (the first tab since 2026-10-14; the Script tab is the text behind them): one **hat block per effect**, headlined by when it happens - "When this card is [Summoned] from / by / as", "When [this card is flipped / destroyed by battle / sent to the GY / declares an attack ...]", "During [your Standby Phase / the End Phase]", "You can (ignition)", "Activate (a Spell / Trap, or a self cost)". Every hat has the same slots: name (the effect's name, for the choose-an-effect prompt), only if, cost, do (actions, run in order = then), only once per turn. Several effects = several hats, read top to bottom. Beside them: "act exactly like the game card" (as) and the Lock block. Changing the blocks rewrites the script; typing in the script redraws them. A new action = one entry in `Tools\WolfX\Content\Blocks\effect-blocks.js` (ACTIONS / COSTS); a new trigger = an entry in HATS' lists. Until then a script using it shows a note over the (locked) blocks and is left alone. See docs/WolfXContent.md.

**Effect library tab:** the game's own cards written in EffectScript wherever the language can say it (about 1170 cards; ~250 are exactly the one effect, the rest carry a "~" and a note of what the script does not cover - e.g. "must have the same name", "ATK <= 1500"). Monster cards show their trigger as a comment. Select a card to read its text and script; **Use as template** puts the script into the Effects tab for the card selected there. The data is `Tools\WolfX\Content\effect_reference.json`, made from the game's tables by `docs\effect-scripts\build_effect_reference.py` (re-run it after learning to decode a new effect type). `WolfX.exe --attach-effects <cards.json> <report> <work csv>` attaches effects from card text to a whole cards.json without opening the editor.

## Chaining ("then") - what is built and what is not

Stage 1 (built, confirmed): draw / life point steps run back to back ahead of the card's last action. Stage 2 (built 2026-10-21, not run in a duel): any action can come before `then` (`search(...) then special_summon(...)`); each runs its own game machine to the end before the next starts (EffectSystem.md section 53). Only the last action may target.

## Also planned

- `discard(N)`, `send(deck -> grave, selector)`, `banish`, `gain_lp(N)` / `pay_lp(N)`, `special_summon(hand, selector)`, `negate` - each needs its vanilla source found first (the backlog groups in `effects_backlog_grouped.xlsx` say which are most common: search 765, destroy 337, draw 255, burn 205).
- (Some of the above are built since: discard / Tribute / banish-self costs, special summon from the hand, negate. What's still missing is in [GLOSSARY.md](../GLOSSARY.md#effects) and `docs/Card Lists/Effects Coverage.xlsx`.)

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

## Continuous ATK / DEF and cannot be destroyed (2026-10-25)

    aura(atk -500, opponent);                                   // your opponent's monsters lose 500 ATK
    aura(atk 300, def 300, own, monster where race = Dragon);   // your Dragons gain 300 ATK and DEF
    indestructible(battle);                                     // this card cannot be destroyed by battle
    indestructible(battle, effects);                            // ... by battle or card effects
    indestructible(opponent_effects);                           // ... by your opponent's card effects
    indestructible(battle, own, monster where race = Warrior);  // your Warriors cannot be destroyed by battle
    untargetable(opponent_effects);                             // this card cannot be targeted by your opponent's card effects
    untargetable(attacks);                                      // ... cannot be targeted for attacks
    unaffected(spells, traps);                                  // this card is unaffected by Spell and Trap effects
    unaffected(opponent_monster_effects, own);                  // your monsters are unaffected by your opponent's monster effects

Who: self (indestructible's default, this card only), own, opponent, any; aura's default is own. The selector may test race, attribute and archetype.
Both apply only while this card is face-up and its effects are not negated, and sit beside the card's effects. BAB: "While face-up" category.
Compile to `"aura"` and `"protect"` (docs/EffectSystem.md sections 69, 71 and 72).

    name_as("Harpie Lady");      // this card's name becomes "Harpie Lady" while on the field or in the GY (the game's own path)
    name_as(6390, field);        // ... while face-up on the field only
    only_one;                    // you can only control 1 of this card;   only_one(field): only 1 face-up on the whole field
    attack_directly;             // this card can attack directly
    while controls(monster where race = Dinosaur, other): untargetable(attacks);   // "While you control another Dinosaur monster, ..."
    while in_grave(25): aura(atk 2500, def 2500, self);     // "While you have 25 or more cards in your GY, this card gains 2500 ATK/DEF"
    while not in_hand(card): aura(atk -800, opponent);      // "While you have no cards in your hand, ..."
    while has_material and lp_at_most(2000): attack_directly;   // also: defense_position; 'not' = fewer than N
    aura(atk 400, self, per controls(other, monster where race = Plant));   // "gains 400 ATK for each other Plant monster you control"
    aura(atk 500, self, per materials);                     // "... for each material attached to it"

`while <tests>:` goes before aura / indestructible / untargetable / unaffected / attack_directly. Count tests: `controls`, `in_grave`,
`in_hand`, `banished` `(N, own | opponent | any, other, selector)` (N = how many, default 1; the selector may test kind, race, attribute,
archetype, name), `materials`; plus `has_material`, `defense_position`, `lp_at_most(N)`. docs/EffectSystem.md section 89.
    on ignition: draw(1); once_per_duel;      // once per Duel
    on ignition: draw(1); uses_per_turn(2);   // twice per turn (counted per copy on the field)
    on ignition: draw(1); once_per_turn; no_special_summon(extra, synchro, except monster where attribute = Dark);
                                 // you cannot Special Summon from the Extra Deck the turn you activate this, except DARK Synchro Monsters
    on ignition: search(deck, card where archetype = 632); no_special_summon(extra, resolved);
                                 // "..., also you cannot Special Summon from the Extra Deck for the rest of this turn" (locked once it resolves)
    if lp_at_most(2000): draw(1);   // "If your LP are 2000 or less: ..."   if lp_lower: = "If your LP are lower than your opponent's"
    cost send_self: draw(1);        // "You can send this card from your hand to the GY; ..." (a move, not a discard)
    cost send_self_field: draw(1);  // "You can send this card from the field to the GY; ..."
    cost banish_self_hand: draw(1);   // banish this card from your hand   (banish_self_field: from the field; tribute_self_hand: Tribute it from your hand)
    cost tribute_self_hand: draw(1) or cost tribute_self: draw(1); once_per_turn;   // "Tribute this card from your hand or field" (one row per zone)
    cost banish_grave(1, monster where attribute = Water): draw(1);         // "banish 1 WATER monster from your GY; ..."
    cost banish_self_and(1, monster where archetype = 74): draw(1);         // "banish this card and 1 "Dragunity" monster from your GY; ..."
    cost discard(1, spell): draw(1);                         // "discard 1 Spell; ..."   (discard(N) = any N cards)
    cost tribute(1, monster where race = Dragon): draw(2);   // "Tribute 1 Dragon monster; ..."   (tribute(N) = any N monsters)
    cost send_hand(1, monster where attribute = Water): draw(1);   // "send 1 WATER monster from your hand to the GY; ..."
    cost reveal(monster where archetype = 74): draw(1);      // "reveal 1 "Dragunity" monster in your hand; ..."
    cost send_hand_field(card): draw(1);                     // "send 1 card from your hand or field to the GY; ..."

These pick costs borrow no card: the game's own cost machine for each runs as this card, with this filter (docs/EffectSystem.md section 90).

Moving cards (section 86): `move(what, destination)`. Destinations: `to_hand`, `to_grave` (sent, not destroyed), `to_banished`, `to_deck`
(shuffled in), `to_deck_top`, `to_deck_bottom`, `to_extra`, `destroy`.

    move(target, opponent, monster, to_deck);          // "target 1 monster your opponent controls; shuffle it into the Deck"
    move(target, 2, any, card, to_hand);               // "target up to 2 cards on the field; return them to the hand"
    move(target, 3, opponent, monster, destroy);       // "target up to 3 monsters your opponent controls; destroy them"
    move(self, to_hand);                               // "return this card to the hand" / "add this card (from the GY) to your hand"
    move(grave, monster where race = Fish, to_deck);   // "shuffle 1 Fish monster from your GY into the Deck" (picked when it resolves)
    move(hand or grave, card, to_deck_bottom);         // "place 1 card from your hand or GY on the bottom of the Deck"
    move(target, any, card, to_deck) then draw(1);     // a target move may be followed by other steps

`target` = chosen on the field when the effect is activated (up to 5 with a count); `self` = this card (on the field, or from the GY / banished
to the hand, or from the GY to the Deck); `hand` / `grave` = 1 card picked from that list when it resolves, to the Deck or Extra Deck only.

docs/EffectSystem.md sections 73-77 and 82-87. "You can reveal this card in your hand; X" is written `on in_hand: X` (section 83).

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
