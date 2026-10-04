# EffectScript - writing card effects

Goal: a custom card gets its effect by one short line, and effects can be chained, without touching C++.

```
search(deck, monster where race = Dragon and level <= 4);
revive(grave, monster where attribute = Dark);
destroy(all, opponent, monster where level >= 7);
draw(2);
draw(1) then search(deck, monster where race = Zombie);   // draw / LP steps first, then one action
```

A script is just the effect itself, no wrapper needed (`effect "Name" { ... }` around it means the same and is optional). Written in the WolfEx **Effects** tab, compiled there (ANTLR, `Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Effects/Script/EffectScript.g4`), saved on the card as `effectScript` (the text) and `effectClone` (what the game runs). The plugin never sees the text.

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
5. Add a test card in `Desktop\New folder\tests`, rename what you found in IDA, note it in EffectSystem.md.

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

## In WolfEx (Effects tab)

The card list has a checkbox per card (plus "Check all shown" with the filter box, "Clear all checks"). Write a script in the editor (Scintilla: colours, line numbers, keyword completion, red squiggle at the compile error), **Apply script to all checked**. **Attach from card text...** reads the text of every Normal Spell (or of the checked cards) and attaches the effect where its first sentence is a plain supported effect; the remaining sentences are stored in `effectNotImplemented` on the card.

**Blocks:** on the Blocks tab next to the Script tab, Scratch-style blocks build the same script (Blockly in a WebView2): an Effect block (when / only if /
cost / do / once per turn), the actions, and "which cards" blocks for selectors. Changing the blocks rewrites the script; typing in the
script (another card, a template) redraws them. Adding a grammar rule = one entry in `Tools\WolfX\Content\Blocks\effect-blocks.js`
(ACTIONS or COSTS); until then a script using it shows a note over the (locked) blocks and is left alone. See docs/WolfXContent.md.

**Effect library tab:** the game's own cards written in EffectScript wherever the language can say it (about 1170 cards; ~250 are exactly the one effect, the rest carry a "~" and a note of what the script does not cover - e.g. "must have the same name", "ATK <= 1500"). Monster cards show their trigger as a comment. Select a card to read its text and script; **Use as template** puts the script into the Effects tab for the card selected there. The data is `Tools\WolfEx\effect_reference.json`, made from the game's tables by `docs\effect-scripts\build_effect_reference.py` (re-run it after learning to decode a new effect type). `Desktop\New folder\Attach-Effects.py` does the same for a whole cards.json without opening the editor (run it after each Delta.ps1).

## Chaining ("then") - what is built and what is not

Stage 1 (built, confirmed): draw / life point steps run back to back ahead of the card's last action. Stage 2 (an interactive action followed by another action) is design only:

Every slot 0 in the game is a step machine (one call = one step, returns the next step; see EffectSystem.md section 12). A chain is therefore a small sequencer of our own that runs the first action's machine until it finishes, resets the step variable, and starts the second - with the same clone impersonation wrapped around each. Costs and conditions (`if you control no monsters`, `pay 800 LP`) fit the same way as slot 2 wrappers. This is the next language feature after the single actions are all proven; until then a script with `then` compiles to an error rather than a card that silently does half of it.

## Also planned

- `discard(N)`, `send(deck -> grave, selector)`, `banish`, `gain_lp(N)` / `pay_lp(N)`, `special_summon(hand, selector)`, `negate` - each needs its vanilla source found first (the backlog groups in `effects_backlog_grouped.xlsx` say which are most common: search 765, destroy 337, draw 255, burn 205).
- Bulk attach in WolfEx (select many cards, paste one script).

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
