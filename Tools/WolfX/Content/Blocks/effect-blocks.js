// WolfX block editor for EffectScript (Yu-Gi-Oh_Loader Plugins\Yu-Gi-Oh-Effects\Script\EffectScript.g4, docs/EffectLanguage.md).
//
// Blocks -> script: generate() below writes the same text you would type in the Script tab; WolfX puts it in the editor and the
// Compile button compiles it as usual.
// Script -> blocks: WolfX parses the script with the grammar and sends the parse tree here (EffectScriptTree.cs: {r: rule, c: [children]},
// tokens as text); load() turns it into blocks. Everything about the language on this side is in the tables below: a new action in the
// grammar is one entry in ACTIONS (and a new cost one in COSTS).
'use strict';

// ---------------------------------------------------------------- the language, in beginner words

const TRIGGERS = [
  ['it is activated (a Spell or Trap)', 'NONE'],
  ['this card is Normal Summoned', 'normal_summoned'],
  ['this card is Special Summoned', 'special_summoned'],
  ['this card is Summoned (any way)', 'summoned'],
  ['this card is Normal or Special Summoned', 'normal_or_special_summoned'],
  ['this card is flipped face-up (FLIP)', 'flip'],
  ['this card is destroyed by battle', 'destroyed_by_battle'],
  ['this card is sent to the GY', 'sent_to_grave'],
  ['this card is sent from the field to the GY', 'sent_from_field_to_grave'],
  ['your Standby Phase starts', 'standby_phase'],
  ["your opponent's Standby Phase starts", 'opponent_standby_phase'],
  ['each Standby Phase starts', 'each_standby_phase'],
  ['your End Phase starts', 'end_phase'],
  ["your opponent's End Phase starts", 'opponent_end_phase'],
  ['each End Phase starts', 'each_end_phase'],
  ['the End Phase starts and this card was sent to the GY this turn', 'grave_end_phase'],
  ['the End Phase starts and this card was banished this turn', 'banished_end_phase'],
  ['this card destroys a monster by battle', 'destroys_by_battle'],
  ['this card inflicts battle damage', 'battle_damage'],
  ['this card declares an attack', 'attack_declared'],
  ['the Damage Step starts and this card battles', 'damage_step_battles'],
  ['the Damage Step starts and this card attacks', 'damage_step_attacks'],
  ['the Battle Phase starts (either player\'s)', 'battle_phase_start'], ['your Battle Phase starts', 'your_battle_phase_start'],
  ["your opponent's Battle Phase starts", 'opponent_battle_phase_start'], ['the Battle Phase ends (either player\'s)', 'battle_phase_end'],
  ['your Battle Phase ends', 'your_battle_phase_end'],
  ['you choose to use it (ignition, once per turn)', 'ignition'],
  ['you choose to use it while this card is in your GY', 'in_grave'],
];

// How the card must have been summoned (summon triggers only): on special_summoned(from grave, by effect, xyz):
const SUMMON_TRIGGERS = ['normal_summoned', 'special_summoned', 'summoned', 'normal_or_special_summoned'];
const SUMMON_FROM = [['anywhere', 'ANY'], ['your hand', 'hand'], ['your Deck', 'deck'], ['the Extra Deck', 'extra'], ['the GY', 'grave'],
  ['banishment', 'banished'], ['a Spell & Trap Zone', 'spelltrap']];
const SUMMON_BY = [['any way', 'ANY'], ['a card effect', 'effect'], ['not a card effect', 'not_effect'], ['its own effect', 'own'],
  ['a Spell effect', 'spell'], ['a Trap effect', 'trap'], ['a monster effect', 'monster']];
const SUMMON_HOW = [['any summon', 'ANY'], ['Fusion Summon', 'fusion'], ['Synchro Summon', 'synchro'], ['Xyz Summon', 'xyz'],
  ['Ritual Summon', 'ritual'], ['Link Summon', 'link'], ['Pendulum Summon', 'pendulum'], ['Tribute Summon', 'tribute'],
  ['Tribute Summon (2+ Tributes)', 'tribute 2'], ['Tribute Summon (3 Tributes)', 'tribute 3']];

// The effect hats: what the headline says, the script trigger it writes (trigger(block)) and which triggers it shows when a script is
// loaded (has(trigger)). TRIGGERS above stays the full list the language knows.
const SUMMONED = [['Normal Summoned', 'normal_summoned'], ['Special Summoned', 'special_summoned'], ['Summoned (any way)', 'summoned'],
  ['Normal or Special Summoned', 'normal_or_special_summoned']];
const EVENTS = [['this card is flipped face-up (FLIP)', 'flip'], ['this card is destroyed by battle', 'destroyed_by_battle'],
  ['this card is sent to the GY', 'sent_to_grave'], ['this card is sent from the field to the GY', 'sent_from_field_to_grave'],
  ['this card destroys a monster by battle', 'destroys_by_battle'], ['this card inflicts battle damage', 'battle_damage'],
  ['this card declares an attack', 'attack_declared'], ['you use it while this card is in your GY', 'in_grave'], ['this card is banished', 'banished'],
  ['you use it while this card is in your hand', 'in_hand'], ["an opponent's monster declares an attack", 'opponent_attack_declared'],
  ['this card is targeted for an attack', 'attacked'], ['your opponent Special Summons a monster', 'opponent_special_summoned'],
  ['the Damage Step starts and this card battles', 'damage_step_battles'], ['the Damage Step starts and this card attacks', 'damage_step_attacks'],
  ['your opponent Normal or Special Summons a monster', 'opponent_summoned'], ['a monster is Special Summoned', 'monster_special_summoned'],
  ['a monster is Normal or Special Summoned', 'monster_summoned'],
  ['the Battle Phase starts (either player\'s)', 'battle_phase_start'], ['your Battle Phase starts', 'your_battle_phase_start'],
  ["your opponent's Battle Phase starts", 'opponent_battle_phase_start'], ['the Battle Phase ends (either player\'s)', 'battle_phase_end'],
  ['your Battle Phase ends', 'your_battle_phase_end']];
// Where a moved card came from / how (the move triggers only, docs/EffectSystem.md section 65): on sent_to_grave(from hand, by effect):
// ... and the summon reactions (section 68: where the summoned monster came from)
const MOVE_TRIGGERS = ['sent_to_grave', 'sent_from_field_to_grave', 'destroyed_by_battle', 'banished',
  'opponent_special_summoned', 'opponent_summoned', 'monster_special_summoned', 'monster_summoned'];
// the summon reactions also take the summoned monster and whose field it went to (Script: is <selector>, own | opponent; section 87)
const SUMMON_REACTIONS = ['opponent_special_summoned', 'opponent_summoned', 'monster_special_summoned', 'monster_summoned'];
const REACT_SIDE = [['(either field)', 'ANY'], ['to your field', 'own'], ["to your opponent's field", 'opponent']];
const MOVE_FROM = [['anywhere', 'ANY'], ['your hand', 'hand'], ['your Deck', 'deck'], ['the GY', 'grave'], ['the field', 'field'],
  ['a Monster Zone', 'monster'], ['a Spell & Trap Zone', 'spelltrap'], ['the Extra Deck', 'extra']];
const MOVE_BY = [['any way', 'ANY'], ['battle', 'battle'], ['a card effect', 'effect']];
// whose phase: the engine's phase handlers test the turn player against the card's controller (docs/EffectSystem.md section 60)
const PHASES = [['your Standby Phase', 'standby_phase'], ["your opponent's Standby Phase", 'opponent_standby_phase'], ['each Standby Phase', 'each_standby_phase'],
  ['your End Phase', 'end_phase'], ["your opponent's End Phase", 'opponent_end_phase'], ['each End Phase', 'each_end_phase'],
  // the game's "arrived this turn" End Phase handler (docs/EffectSystem.md section 79)
  ['the End Phase, if this card was sent to the GY this turn', 'grave_end_phase'], ['the End Phase, if this card was banished this turn', 'banished_end_phase']];
// Another card is destroyed / leaves the field / is sent to the GY / is banished: the watcher triggers (docs/EffectSystem.md section 91)
const WATCHES = [['destroyed', 'card_destroyed'], ['leaves the field', 'card_left_field'], ['sent to the GY', 'card_sent_to_grave'], ['banished', 'card_banished']];
const WATCH_SIDE = [['(either side)', 'ANY'], ['you control', 'own'], ['your opponent controls', 'opponent']];
const WATCH_FROM = [['anywhere', 'ANY'], ['the field', 'field'], ['a Monster Zone', 'monster'], ['a Spell & Trap Zone', 'spelltrap'], ['the hand', 'hand'],
  ['the Deck', 'deck'], ['the GY', 'grave'], ['the hand or Deck', 'hand+deck']];
const WATCH_BY = [['any way', 'ANY'], ['battle', 'battle'], ['a card effect', 'effect'], ["an opponent's card effect", 'opponent']];
const dropdown = (name, options) => ({ type: 'field_dropdown', name, options });
const checkbox = (name) => ({ type: 'field_checkbox', name, checked: false });
const HATS = [
  { type: 'ys_on_summon', head: () => ['When this card is %1 from %2 by %3 as %4', [dropdown('TRIGGER', SUMMONED), dropdown('FROM', SUMMON_FROM),
      dropdown('BY', SUMMON_BY), dropdown('HOW', SUMMON_HOW)]],
    trigger: (b) => b.getFieldValue('TRIGGER'), has: (t) => SUMMONED.some((o) => o[1] === t),
    tooltip: 'A summon trigger. "from / by / as" narrow it: where the card came from, what summoned it and how (leave them on any).' },
  { type: 'ys_on_event', head: () => ['When %1 (moved from %2 by %3) summoned monster %4 %5', [dropdown('TRIGGER', EVENTS), dropdown('MFROM', MOVE_FROM),
      dropdown('MBY', MOVE_BY), dropdown('RSIDE', REACT_SIDE), { type: 'input_value', name: 'WHICH', check: 'Selector' }]],
    trigger: (b) => b.getFieldValue('TRIGGER'), has: (t) => EVENTS.some((o) => o[1] === t),
    tooltip: 'A monster effect that happens when something happens to this card. "moved from / by" narrow the GY / banished triggers (leave them on anywhere / any way).\n' +
      '"summoned monster" narrows the "a monster is Summoned" reactions: whose field it went to and which monsters count (leave them empty for any).' },
  { type: 'ys_on_watch', head: () => ['When a card %1 %2 is %3 (from %4 by %5) not this card %6 while this card is in your GY %7',
      [{ type: 'input_value', name: 'WHICH', check: 'Selector' }, dropdown('WSIDE', WATCH_SIDE), dropdown('TRIGGER', WATCHES), dropdown('WFROM', WATCH_FROM),
       dropdown('WBY', WATCH_BY), checkbox('WOTHER'), checkbox('WGRAVE')]],
    trigger: (b) => b.getFieldValue('TRIGGER'), has: (t) => WATCHES.some((o) => o[1] === t),
    tooltip: 'Another card is destroyed / leaves the field / is sent to the GY / is banished ("If a monster you control is destroyed ...").\n' +
      'Which cards and whose (leave empty / either for any); "while this card is in your GY" makes this card watch from the GY instead of the field.' },
  { type: 'ys_during', head: () => ['During %1', [dropdown('TRIGGER', PHASES)]],
    trigger: (b) => b.getFieldValue('TRIGGER'), has: (t) => PHASES.some((o) => o[1] === t),
    tooltip: 'A phase effect, while this card is face-up on the field.' },
  { type: 'ys_ignition', head: () => ['You can (an ignition effect, in your Main Phase)', []],
    trigger: () => 'ignition', has: (t) => t === 'ignition',
    tooltip: 'An effect the player chooses to use. Tick "only once per turn" for "You can only use this effect once per turn".' },
  { type: 'ys_activate', head: () => ['Activate (a Spell / Trap, or a cost that says where it is used)', []],
    trigger: () => 'NONE', has: (t) => t === 'NONE',
    tooltip: 'No trigger: a Spell or Trap card\'s effect, or a monster effect whose cost says where it is used (discard this card, banish it from the GY ...).' },
];
const hatOf = (block) => HATS.find((h) => h.type === block.type);
// The "act exactly like" block's trigger: none = the game card is used its own way; any other = this card's monster trigger runs it.
const AS_WHEN = [['the game card is used (its own way)', 'NONE'], ...TRIGGERS.filter((t) => t[1] !== 'NONE')];

// draw / gain_lp / damage: whose (Yu-Gi-Oh-Effects runs them itself when a player is named or they are chained)
const WHO_OWN = [['you', 'DEFAULT'], ['your opponent', 'opponent'], ['both players', 'any']];
const WHO_OPPONENT = [['your opponent', 'DEFAULT'], ['you', 'own'], ['both players', 'any']];
const SIDE = [['(default)', 'DEFAULT'], ['you control', 'own'], ['your opponent controls', 'opponent'], ['on either side', 'any']];

// text: what the block says, %NAME = one of its fields / inputs. args: the script's arguments in order.
//   lit: a fixed word in the script, no field     num: a number field      sel: a "which cards" input (optional in the script)
//   pick: a dropdown; rule = the grammar rule that holds the word, or token: true when it is a plain word; optional = may be left out
//   stat: "atk N" / "def N" (left out when blank)
const ACTIONS = [
  { name: 'draw', text: '%WHO draw %N card(s)', args: [{ kind: 'num', field: 'N', def: 1, min: 1 }, { kind: 'pick', rule: 'side', field: 'WHO', optional: true, options: WHO_OWN }] },
  { name: 'gain_lp', text: '%WHO gain %N Life Points', args: [{ kind: 'num', field: 'N', def: 500, min: 0 }, { kind: 'pick', rule: 'side', field: 'WHO', optional: true, options: WHO_OWN }] },
  { name: 'damage', text: 'inflict %N damage to %WHO', args: [{ kind: 'num', field: 'N', def: 500, min: 0 }, { kind: 'pick', rule: 'side', field: 'WHO', optional: true, options: WHO_OPPONENT }] },
  { name: 'burn', text: 'inflict %N damage to your opponent', args: [{ kind: 'num', field: 'N', def: 500, min: 0 }] },
  {
    name: 'lock_turn', text: 'for the rest of this turn, negate %WHAT',
    args: [{ kind: 'pick', rule: 'lockTurnArg', field: 'WHAT', options: [['Traps on the field', 'trap'], ['Equip, Field, Continuous and Quick-Play Spells', 'spell'],
           ['the effects of Defense Position monsters', 'defense_position']] }],
  },
  // add up to N cards from any of the ticked zones (one list; the plugin's own step, docs/EffectSystem.md section 98)
  {
    name: 'search', text: 'add up to %N card(s) to your hand from your Deck %DECK GY %GY banished cards %BANISHED : %SEL',
    args: [{ kind: 'zone', rule: 'searchZones', item: 'searchZone', field: 'DECK', value: 'deck', def: true },
           { kind: 'zone', rule: 'searchZones', item: 'searchZone', field: 'GY', value: 'grave' },
           { kind: 'zone', rule: 'searchZones', item: 'searchZone', field: 'BANISHED', value: 'banished', last: true },
           { kind: 'num', field: 'N', def: 1, min: 1, omitOne: true }, { kind: 'sel', field: 'SEL' }],
  },
  // "Set 1 X Spell/Trap directly from your Deck" (the game's set-from-Deck machine, docs/EffectSystem.md section 63)
  { name: 'set', text: 'Set from your Deck: %SEL', args: [{ kind: 'lit', value: 'deck' }, { kind: 'sel', field: 'SEL' }] },
  // "Fusion Summon 1 X Fusion Monster" with Polymerization's materials (hand or field), docs/EffectSystem.md section 64
  // materials: Polymerization's (hand / field) or Dragon's Mirror's (banished from your field / GY, section 94)
  { name: 'fusion_summon', text: 'Fusion Summon a Fusion Monster %ROUTE : %SEL', args: [{ kind: 'sel', field: 'SEL' },
    { kind: 'pick', rule: 'fusionRoute', field: 'ROUTE', optional: true, options: [['using monsters from your hand or field', 'DEFAULT'], ['by banishing its materials from your field or GY', 'banish_materials']] }] },
  {
    name: 'send_to_grave', text: 'send from %FROM to the GY: %SEL',
    args: [{ kind: 'pick', token: true, field: 'FROM', options: [['your Deck', 'deck'], ['your hand', 'hand']] }, { kind: 'sel', field: 'SEL' }],
  },
  {
    name: 'revive', text: 'Special Summon from %GY : %SEL',
    args: [{ kind: 'pick', rule: 'graveyard', field: 'GY', options: [['your GY', 'grave'], ["your opponent's GY", 'opponent_grave'], ['either GY', 'either_grave']] },
           { kind: 'sel', field: 'SEL' }],
  },
  {
    name: 'special_summon', text: 'Special Summon from %ZONE : %SEL', when: (inner) => zoneCount(inner) === 1,
    args: [{ kind: 'pick', rule: 'summonZones', field: 'ZONE',
             options: [['your hand', 'hand'], ['your Deck', 'deck'], ['your GY', 'grave'], ['your banished cards', 'banished'], ['your Extra Deck', 'extra'],
                       ['(this card) your hand', 'self'], ['(this card) your GY', 'self_grave']] },
           { kind: 'sel', field: 'SEL' }],
  },
  {
    // the same script word with several zones joined by 'or': one list over all ticked zones (block = its own block type)
    name: 'special_summon', block: 'summon_zones', when: (inner) => zoneCount(inner) > 1,
    text: 'Special Summon from any of: your hand %HAND Deck %DECK GY %GY banished cards %BANISHED Extra Deck %EXTRA : %SEL',
    args: [{ kind: 'zone', field: 'HAND', value: 'hand', def: true }, { kind: 'zone', field: 'DECK', value: 'deck', def: true },
           { kind: 'zone', field: 'GY', value: 'grave', def: true }, { kind: 'zone', field: 'BANISHED', value: 'banished' },
           { kind: 'zone', field: 'EXTRA', value: 'extra', last: true }, { kind: 'sel', field: 'SEL' }],
  },
  {
    name: 'destroy', text: 'destroy %SCOPE card(s) %SIDE : %SEL',
    args: [{ kind: 'pick', rule: 'scope', field: 'SCOPE', options: [['1 chosen', 'target'], ['all', 'all']] },
           { kind: 'pick', rule: 'side', field: 'SIDE', optional: true, options: SIDE }, { kind: 'sel', field: 'SEL' }],
  },
  {
    name: 'banish', rule: 'banishAction', text: 'banish %SCOPE card(s) %SIDE : %SEL',
    args: [{ kind: 'pick', rule: 'scope', field: 'SCOPE', options: [['1 chosen', 'target'], ['all', 'all']] },
           { kind: 'pick', rule: 'side', field: 'SIDE', optional: true, options: SIDE }, { kind: 'sel', field: 'SEL' }],
  },
  {
    // same script word as the block above, another grammar rule: block = its own block type (ys_banish_from)
    name: 'banish', rule: 'banishFromAction', block: 'banish_from', text: 'banish from %FROM : %SEL',
    args: [{ kind: 'pick', token: true, field: 'FROM', options: [['your Deck', 'deck'], ['your hand', 'hand']] }, { kind: 'sel', field: 'SEL' }],
  },
  {
    name: 'return_to_hand', text: 'return 1 chosen card %SIDE to the hand: %SEL',
    args: [{ kind: 'lit', value: 'target' }, { kind: 'pick', rule: 'side', field: 'SIDE', optional: true, options: SIDE }, { kind: 'sel', field: 'SEL' }],
  },
  // the game's own target machines (docs/EffectSystem.md section 92): 1 chosen monster on the field
  ...[['change_position', 'change the battle position of 1 chosen monster %SIDE %POS : %SEL'],
    ['set_face_down', 'change 1 chosen monster %SIDE %POS to face-down Defense Position : %SEL'],
    ['take_control', 'take control of 1 chosen monster %SIDE %POS until the End Phase : %SEL'],
    ['negate_effects', 'negate the effects of 1 chosen monster %SIDE %POS until the end of the turn : %SEL'],
    ['change_atk', '1 chosen monster %SIDE %POS gains %N ATK until the end of the turn (negative = loses) : %SEL']].map(([name, text]) => ({
    name, text,
    args: [{ kind: 'lit', value: 'target' }, { kind: 'pick', rule: 'side', field: 'SIDE', optional: true, options: SIDE }, { kind: 'sel', field: 'SEL' },
      { kind: 'pick', rule: 'targetPosition', field: 'POS', optional: true, options: [['(any position)', 'DEFAULT'], ['in Attack Position', 'attack'], ['in Defense Position', 'defense']] }]
      .concat(name === 'change_atk' ? [{ kind: 'num', field: 'N', def: 500, min: -32767, signed: true }] : []),
  })),
  {
    // up to N (picked one after another; 1 = the usual one card, section 95)
    name: 'add_to_hand', text: 'add up to %N card(s) to your hand from %FROM : %SEL',
    args: [{ kind: 'pick', token: true, field: 'FROM', options: [['your GY', 'grave'], ['your banished cards', 'banished']] },
      { kind: 'num', field: 'N', def: 1, min: 1, omitOne: true }, { kind: 'sel', field: 'SEL' }],
  },
  {
    name: 'gain_atk', text: '1 chosen face-up monster %SIDE gains %N ATK until the end of the turn',
    args: [{ kind: 'pick', rule: 'side', field: 'SIDE', optional: true, options: SIDE, comma: true }, { kind: 'num', field: 'N', def: 500, min: 0 }],
  },
  {
    // negate(what[, opponent][, effect][, property][, your_turn | opponent_turn | battle_phase][, targets_one]) - the words in this order
    // (a script with them in another order opens in the text editor only)
    name: 'negate', text: 'negate %SCOPE %WHAT %WHO %PROP %WHEN %TARGET , and if you do, destroy it',
    args: [{ kind: 'pick', rule: 'negateArg', field: 'WHAT', options: [['a Spell Card', 'spell'], ['a Trap Card', 'trap'], ['a monster effect', 'monster'], ['a Spell/Trap Card', 'spelltrap'], ['any card or effect', 'any']] },
           { kind: 'pick', rule: 'negateArg', field: 'WHO', optional: true, options: [['(by either player)', 'DEFAULT'], ['by your opponent', 'opponent']] },
           { kind: 'pick', rule: 'negateArg', field: 'SCOPE', optional: true, options: [['the activation of', 'DEFAULT'], ['the activation or effect of', 'effect']] },
           { kind: 'pick', rule: 'negateArg', field: 'PROP', optional: true, options: [['(any kind)', 'DEFAULT'], ['Normal', 'normal'], ['Counter', 'counter'], ['Field', 'field'], ['Equip', 'equip'], ['Continuous', 'continuous'], ['Quick-Play', 'quickplay'], ['Ritual', 'ritual']] },
           { kind: 'pick', rule: 'negateArg', field: 'WHEN', optional: true, options: [['(any time)', 'DEFAULT'], ['during your turn', 'your_turn'], ["during your opponent's turn", 'opponent_turn'], ['during the Battle Phase', 'battle_phase']] },
           { kind: 'pick', rule: 'negateArg', field: 'TARGET', optional: true, options: [['(targeting or not)', 'DEFAULT'], ['that targets 1 card', 'targets_one']] }],
  },
  {
    name: 'equip', text: 'the equipped monster gains ATK %ATK DEF %DEF',
    args: [{ kind: 'stat', stat: 'atk', field: 'ATK' }, { kind: 'stat', stat: 'def', field: 'DEF' }],
  },
];

// move(what, to_x) (docs/EffectSystem.md section 86) has its own block (ys_move): what moves, how many, whose, which cards, where to.
// 'target' = chosen card(s) on the field when activated, 'self' = this card, hand / grave = 1 card picked from that list when it resolves.
const MOVE_WHAT = [['chosen card(s) on the field', 'target'], ['this card', 'self'], ['1 card from your hand', 'hand'], ['1 card from your GY', 'grave'],
  ['1 card from your hand or GY', 'hand or grave']];
const MOVE_DEST = [['to the hand', 'to_hand'], ['to the GY (sent)', 'to_grave'], ['to banishment', 'to_banished'], ['into the Deck (shuffled)', 'to_deck'],
  ['on top of the Deck', 'to_deck_top'], ['on the bottom of the Deck', 'to_deck_bottom'], ['to the Extra Deck', 'to_extra'], ['and destroy it', 'destroy']];

// an action's block type: ys_<script word>, or ys_<block> when two grammar rules share the word (banish)
const blockType = (action) => 'ys_' + (action.block || action.name);
// how many zones a special_summon(...) names (summonZones: hand or deck or grave); picks its block
function zoneCount(inner) { return rules(rule(inner, 'summonZones') || {}, 'summonZone').length; }

// rule = the grammar rule of the cost; num = it takes a number
const COSTS = [
  // sel = an optional "which cards" input (discard / Tribute only matching cards; docs/EffectSystem.md section 90)
  { rule: 'discardCost', word: 'discard', text: 'discard %N card(s) %SEL', num: true, sel: true },
  { rule: 'payCost', word: 'pay_lp', text: 'pay %N Life Points', num: true, def: 1000 },
  { rule: 'tributeCost', word: 'tribute', text: 'Tribute %N monster(s) %SEL', num: true, sel: true },
  { rule: 'detachCost', word: 'detach', text: 'detach %N material(s) from this card', num: true },
];

const SELF_COSTS = [['banish this card from your GY', 'banish_self'], ['discard this card from your hand', 'discard_self'], ['Tribute this card', 'tribute_self'],
  ['send this card from your hand to the GY', 'send_self'], ['send this card from the field to the GY', 'send_self_field'],   // section 84
  ['banish this card from your hand', 'banish_self_hand'], ['banish this card from the field', 'banish_self_field'],
  ['Tribute this card from your hand', 'tribute_self_hand']];   // section 85

const RACES = ['Dragon', 'Zombie', 'Fiend', 'Pyro', 'SeaSerpent', 'Rock', 'Machine', 'Fish', 'Dinosaur', 'Insect', 'Beast', 'BeastWarrior', 'Plant', 'Aqua',
  'Warrior', 'WingedBeast', 'Fairy', 'Spellcaster', 'Thunder', 'Reptile', 'Psychic', 'Wyrm', 'Cyberse', 'DivineBeast', 'CreatorGod'];
const ATTRIBUTES = ['Light', 'Dark', 'Water', 'Fire', 'Earth', 'Wind', 'Divine'];
const spaced = (name) => name.replace(/([a-z])([A-Z])/g, '$1 $2');

const COLOUR = { effect: 210, when: 290, cost: 20, action: 120, cards: 45, as: 330, lock: 0 };

// lock(...): what it locks (checkboxes), who, and where this card has to be (EffectScript lockStatement).
const LOCK_WHAT = [['TRAP', 'trap', 'Trap Cards'], ['SPELL', 'spell', 'Spell Cards'], ['MONSTER', 'monster', 'monster effects'],
  ['CTRAP', 'continuous_trap', 'Continuous Traps'], ['CSPELL', 'continuous_spell', 'Continuous Spells'], ['FSPELL', 'field_spell', 'Field Spells'],
  ['ESPELL', 'equip_spell', 'Equip Spells']];
const LOCK_WHO = [['both players', 'both'], ['the opponent', 'opponent'], ['its controller', 'own']];
const HAND_WHO = [["your opponent's", 'opponent'], ['your', 'own'], ["both players'", 'both']];
const AURA_WHO = [['your', 'own'], ["your opponent's", 'opponent'], ["both players'", 'any'], ['(this card only)', 'self']];
const PROTECT_WHO = [['this card', 'self'], ['your', 'own'], ["your opponent's", 'opponent'], ["both players'", 'any']];
const COUNT_PLACES = [['on the field', 'controls'], ['in the GY', 'in_grave'], ['in the hand', 'in_hand'], ['banished', 'banished']];
// value = statement word : its arguments (indestructible / untargetable / unaffected, docs/EffectSystem.md sections 71-72)
const PROTECT_FROM = [['be destroyed by battle', 'indestructible:battle'], ['be destroyed by card effects', 'indestructible:effects'],
  ["be destroyed by your opponent's card effects", 'indestructible:opponent_effects'], ['be destroyed by battle or card effects', 'indestructible:battle effects'],
  ["be destroyed by battle or your opponent's card effects", 'indestructible:battle opponent_effects'],
  ['be targeted by card effects', 'untargetable:effects'], ["be targeted by your opponent's card effects", 'untargetable:opponent_effects'],
  ['be targeted for attacks', 'untargetable:attacks'],
  ['be affected by Spell effects', 'unaffected:spells'], ['be affected by Trap effects', 'unaffected:traps'],
  ['be affected by Spell or Trap effects', 'unaffected:spells traps'], ['be affected by monster effects', 'unaffected:monster_effects'],
  ["be affected by your opponent's Spell or Trap effects", 'unaffected:opponent_spells opponent_traps'],
  ["be affected by your opponent's monster effects", 'unaffected:opponent_monster_effects']];
const LOCK_WHERE = [['its own zone', 'AUTO'], ['a Monster Zone', 'monster_zone'], ['a Spell/Trap Zone', 'spelltrap_zone'], ['any zone', 'any_zone']];
// the narrow monster locks (one game slot each, both players' monsters): lock(monster, light) -> lock(light)
const LOCK_ONLY = [['every monster', 'ALL'], ['LIGHT monsters', 'light'], ['monsters with an A-Counter', 'a_counter'],
  ["monsters except Koa'ki Meiru", 'except_koaki_meiru'], ['Special Summoned from the Main Deck', 'from_main_deck'],
  ['Special Summoned from the Extra Deck', 'from_extra_deck'], ['Fusion, Synchro and Xyz monsters', 'fusion_synchro_xyz'],
  ['in the Extra Monster Zones', 'extra_monster_zone'], ['except Tribute Summoned ones', 'except_tribute_summoned'],
  ['Special Summoned this turn', 'summoned_this_turn'], ['Defense Position monsters', 'defense_position']];

// ---------------------------------------------------------------- blocks

function message(text, fields) {
  // "%N cards" + {N: arg} -> "%1 cards" + [arg]
  const args = [];
  const msg = text.replace(/%([A-Z]+)/g, (_, name) => { args.push(fields[name]); return '%' + args.length; });
  return { msg, args };
}

function actionField(arg) {
  switch (arg.kind) {
    case 'num': return { type: 'field_number', name: arg.field, value: arg.def, min: arg.min ?? 0, precision: 1 };
    case 'pick': return { type: 'field_dropdown', name: arg.field, options: arg.options };
    case 'sel': return { type: 'input_value', name: arg.field, check: 'Selector' };
    case 'stat': return { type: 'field_input', name: arg.field, text: '' };
    case 'zone': return { type: 'field_checkbox', name: arg.field, checked: !!arg.def };
  }
  return null;
}

function defineBlocks() {
  const defs = [];
  // One hat block per effect, headlined by WHEN it happens (HATS); the slots below are the same on every hat. A card with several effects
  // is several hats on the canvas, written top to bottom.
  for (const hat of HATS) {
    const head = hat.head(), rest = [
      ['name (shown when the player chooses) %1', [{ type: 'field_input', name: 'NAME', text: '' }]],
      ['only if %1', [{ type: 'input_value', name: 'REQ', check: 'Req' }]],
      ['cost %1', [{ type: 'input_value', name: 'COST', check: 'Cost' }]],
      ['do %1', [{ type: 'input_statement', name: 'DO', check: 'Action' }]],
      ['Quick Effect (either turn, in a chain) %1', [{ type: 'field_checkbox', name: 'QUICK', checked: false }]],
      ['only once per turn (per card name) %1', [{ type: 'field_checkbox', name: 'ONCE', checked: false }]],
      // "Once per turn: ...": each copy on the field once per turn (Script: once_per_turn(copy)); the per-name box wins when both are ticked
      ['only once per turn (each copy) %1', [{ type: 'field_checkbox', name: 'ONCE_COPY', checked: false }]],
      // once per Duel / N uses per turn (each copy) (Script: once_per_duel; uses_per_turn(N); docs/EffectSystem.md section 74); the boxes above win
      ['other use limit %1', [{ type: 'field_dropdown', name: 'LIMIT', options: [['none', 'NONE'], ['once per Duel', 'DUEL'],
        ['twice per turn (each copy)', '2'], ['three times per turn (each copy)', '3'], ['four times per turn (each copy)', '4']] }]],
      // "You cannot Special Summon the turn you activate this, except ..." (Script: no_special_summon(...); docs/EffectSystem.md section 77)
      // "For the rest of this turn after this card resolves, ..." (Script: no_special_summon(resolved, ...))
      ['%4 you cannot Special Summon %1 except %2 %3', [
        { type: 'field_dropdown', name: 'NOSUMMON', options: [['(no restriction)', 'NONE'], ['monsters', 'ANY'], ['from the Extra Deck', 'EXTRA']] },
        { type: 'field_dropdown', name: 'NOSUMMON_KIND', options: [['any kind', 'ANY'], ['Fusion', 'fusion'], ['Synchro', 'synchro'], ['Xyz', 'xyz'], ['Link', 'link'], ['Ritual', 'ritual']] },
        { type: 'input_value', name: 'NOSUMMON_EXCEPT', check: 'Selector' },
        { type: 'field_dropdown', name: 'NOSUMMON_WHEN', options: [['the turn you activate it,', 'ACTIVATE'], ['after it resolves this turn,', 'RESOLVE']] }]],
      // "Activate 1 of these effects": this hat and the one below it are one choice and share one once-per-turn limit (Script: 'or').
      ['or the next effect (activate 1 of these) %1', [{ type: 'field_checkbox', name: 'OR_NEXT', checked: false }]],
    ];
    const def = { type: hat.type, colour: COLOUR.effect, tooltip: hat.tooltip };
    [head, ...rest].forEach(([msg, args], i) => { def['message' + i] = msg; def['args' + i] = args; });
    defs.push(def);
  }
  defs.push({
    type: 'ys_as',
    message0: 'act exactly like the game card %1', args0: [{ type: 'field_input', name: 'CARD', text: 'Pot of Greed' }],
    message1: 'with its own ATK %1 DEF %2 (optional)', args1: [{ type: 'field_input', name: 'ATK', text: '' }, { type: 'field_input', name: 'DEF', text: '' }],
    message2: 'copy only the effect (keep this card\'s own type, stats and identity) %1', args2: [{ type: 'field_checkbox', name: 'EFFECT_ONLY', checked: false }],
    message3: 'used when %1', args3: [dropdown('WHEN', AS_WHEN)],
    message4: 'name (shown when the player chooses) %1', args4: [{ type: 'field_input', name: 'NAME', text: '' }],
    colour: COLOUR.as,
    tooltip: 'Borrow a game card\'s whole behaviour (name or Konami id). Put more effect hats beside it for a card with several effects; the runtime routes by table and event.\n' +
      'Copy only the effect: in a duel the card is not lent the game card\'s id, so a monster stays a monster even when it copies a Trap (Script: effect_only).\n' +
      'Used when: a monster copying a Spell or Trap needs a monster trigger, e.g. "you choose to use it" (Script: on ignition: as(...)).',
  });
  defs.push({
    type: 'ys_lock',
    message0: 'Lock: these cannot be activated, and are negated on the field', args0: [],
    message1: 'Traps %1 Spells %2 monster effects %3', args1: [
      { type: 'field_checkbox', name: 'TRAP', checked: true }, { type: 'field_checkbox', name: 'SPELL', checked: false }, { type: 'field_checkbox', name: 'MONSTER', checked: false }],
    message2: 'only these: Continuous Traps %1 Continuous Spells %2 Field Spells %3 Equip Spells %4', args2: [
      { type: 'field_checkbox', name: 'CTRAP', checked: false }, { type: 'field_checkbox', name: 'CSPELL', checked: false },
      { type: 'field_checkbox', name: 'FSPELL', checked: false }, { type: 'field_checkbox', name: 'ESPELL', checked: false }],
    message3: 'of %1 while this card is face-up in %2', args3: [
      { type: 'field_dropdown', name: 'WHO', options: LOCK_WHO }, { type: 'field_dropdown', name: 'WHERE', options: LOCK_WHERE }],
    message4: 'monster effects of only %1', args4: [{ type: 'field_dropdown', name: 'ONLY', options: LOCK_ONLY }],
    colour: COLOUR.lock,
    tooltip: 'While this card is face-up in the chosen zone and its effects are not negated, the ticked cards cannot be activated and their effects on the field are negated. It sits beside the card\'s effects (Script: lock(...);).',
  });
  defs.push({
    // While this card is face-up: monsters' ATK / DEF change (Script: aura(...); docs/EffectSystem.md section 69)
    type: 'ys_aura',
    message0: 'While this card is face-up, %1 monsters %2', args0: [{ type: 'field_dropdown', name: 'WHO', options: AURA_WHO },
      { type: 'input_value', name: 'SEL', check: 'Selector' }],
    message1: 'change ATK by %1 and DEF by %2', args1: [{ type: 'field_number', name: 'ATK', value: 500, precision: 1 },
      { type: 'field_number', name: 'DEF', value: 0, precision: 1 }],
    message2: 'for each %1', args2: [{ type: 'input_value', name: 'PER', check: 'Count' }],
    message3: 'only while %1', args3: [{ type: 'input_value', name: 'WHILE', check: 'Count' }],
    colour: COLOUR.lock,
    tooltip: 'A continuous ATK / DEF change (negative = lose). Only face-up monsters; not while this card\'s effects are negated. The selector may test Type, Attribute and archetype. "For each": the change is multiplied by that count; "only while": it applies only while the condition holds (Script: aura(...); while ...: aura(..., per ...);).',
  });
  // conditions and counts for the continuous blocks (Script: while ...: / per ...; docs/EffectSystem.md section 89)
  defs.push({
    type: 'ys_count',
    message0: '%1 %2 %3 cards %4 %5', args0: [
      { type: 'field_dropdown', name: 'NOT', options: [['at least', 'min'], ['fewer than', 'not']] },
      { type: 'field_number', name: 'N', value: 1, min: 1, precision: 1 },
      { type: 'field_dropdown', name: 'SIDE', options: [['of yours', 'own'], ["of your opponent's", 'opponent'], ["of either player's", 'any']] },
      { type: 'field_dropdown', name: 'PLACE', options: COUNT_PLACES },
      { type: 'input_value', name: 'SEL', check: 'Selector' }],
    message1: 'other than this card %1', args1: [{ type: 'field_checkbox', name: 'OTHER', checked: false }],
    output: 'Count', colour: COLOUR.when,
    tooltip: 'Counts face-up cards on the field, or cards in the GY / hand / banished, that match the selector (kind, Type, Attribute, archetype, name). In "for each" the number and "fewer than" are not used (Script: controls(...), in_grave(...), in_hand(...), banished(...)).',
  });
  defs.push({
    type: 'ys_count_self',
    message0: '%1 %2', args0: [{ type: 'field_dropdown', name: 'TEST', options: [['this card has material (for each: its materials)', 'has_material'],
      ['this card is in Defense Position', 'defense_position'], ['your LP are at most', 'lp_at_most']] },
      { type: 'field_number', name: 'N', value: 2000, min: 0, precision: 1 }],
    output: 'Count', colour: COLOUR.when,
    tooltip: 'This card\'s Xyz materials, its battle position, or your LP (the number is used only for LP) (Script: has_material, defense_position, lp_at_most(N), per materials).',
  });
  defs.push({
    type: 'ys_count_and', message0: '%1 and %2', args0: [{ type: 'input_value', name: 'A', check: 'Count' }, { type: 'input_value', name: 'B', check: 'Count' }],
    inputsInline: true, output: 'Count', colour: COLOUR.when, tooltip: 'Both conditions hold (only while).',
  });
  defs.push({
    // While this card is face-up: cards cannot be destroyed (Script: indestructible(...); docs/EffectSystem.md section 71)
    type: 'ys_protect',
    message0: 'While this card is face-up, %1 monsters %2', args0: [{ type: 'field_dropdown', name: 'WHO', options: PROTECT_WHO },
      { type: 'input_value', name: 'SEL', check: 'Selector' }],
    message1: 'cannot %1', args1: [{ type: 'field_dropdown', name: 'FROM', options: PROTECT_FROM }],
    message2: 'only while %1', args2: [{ type: 'input_value', name: 'WHILE', check: 'Count' }],
    colour: COLOUR.lock,
    tooltip: '"This card" protects only itself; the others protect every face-up monster of that side the selector matches (Type, Attribute, archetype). Not while this card\'s effects are negated (Script: indestructible(...); untargetable(...); unaffected(...);).',
  });
  defs.push({
    // This card's name becomes another card's (Script: name_as(...); docs/EffectSystem.md section 73)
    type: 'ys_name_as',
    message0: "This card's name becomes %1 while %2", args0: [{ type: 'field_input', name: 'CARD', text: 'Harpie Lady' },
      { type: 'field_dropdown', name: 'WHERE', options: [['on the field or in the GY', 'grave'], ['face-up on the field', 'field']] }],
    colour: COLOUR.lock,
    tooltip: 'A card name or id. "On the field or in the GY" is the game\'s own path for these cards (it also counts where the card is looked up by id); "face-up on the field" only while face-up and not negated (Script: name_as(...);).',
  });
  defs.push({
    // "You can only control 1 <this card>" (Script: only_one; / only_one(field); docs/EffectSystem.md section 75)
    type: 'ys_only_one',
    message0: 'There can only be 1 face-up copy of this card %1', args0: [{ type: 'field_dropdown', name: 'WHERE',
      options: [['on your field (you can only control 1)', 'own'], ['on the whole field', 'field']] }],
    colour: COLOUR.lock,
    tooltip: 'The game\'s uniqueness rule: it cannot be summoned or activated while one is already face-up there (Script: only_one;).',
  });
  defs.push({
    // "You can also Xyz Summon this card by using 1 X you control" (Script: xyz_also(...); docs/EffectSystem.md section 94)
    type: 'ys_xyz_also',
    message0: 'You can also Xyz Summon this card by using 1 %1 you control %2 Rank %3 %4 to %5',
    args0: [{ type: 'input_value', name: 'SEL', check: 'Selector' }, { type: 'field_dropdown', name: 'XYZ', options: [['(any monster)', 'ANY'], ['Xyz Monster', 'xyz']] },
      { type: 'field_dropdown', name: 'RANK', options: [['(any)', 'ANY'], ['from', 'range']] },
      { type: 'field_number', name: 'MIN', value: 4, min: 1, max: 13, precision: 1 }, { type: 'field_number', name: 'MAX', value: 4, min: 1, max: 13, precision: 1 }],
    colour: COLOUR.lock, inputsInline: true,
    tooltip: 'The game\'s alternative Xyz Summon: the one material is tested by this rule (Script: xyz_also(...);).',
  });
  defs.push({ type: 'ys_attack_directly', message0: 'This card can attack directly', colour: COLOUR.lock,
    message1: 'only while %1', args1: [{ type: 'input_value', name: 'WHILE', check: 'Count' }],
    tooltip: 'Even while the opponent controls monsters, unless something prevents direct attacks; not while face-down or negated (Script: attack_directly;).' });
  defs.push({
    type: 'ys_hand_limit',
    message0: 'While this card is face-up in %1', args0: [{ type: 'field_dropdown', name: 'WHERE', options: LOCK_WHERE }],
    message1: '%1 hand size limit is %2', args1: [{ type: 'field_dropdown', name: 'WHO', options: HAND_WHO },
      { type: 'field_number', name: 'N', value: 3, min: 0, max: 60, precision: 1 }],
    colour: COLOUR.lock,
    tooltip: 'In the End Phase that player discards down to this many cards (normally 6). The lowest limit on the field wins. Not while this card\'s effects are negated (Script: hand_limit(...);).',
  });
  defs.push({ type: 'ys_req_no_monsters', message0: 'you control no monsters', output: 'Req', colour: COLOUR.when });
  defs.push({ type: 'ys_req_controls', message0: 'you control %1', args0: [{ type: 'input_value', name: 'SEL', check: 'Selector' }], output: 'Req', colour: COLOUR.when });
  // whose Battle Phase (Script: if battle_phase / your_battle_phase / opponent_battle_phase: ...; docs/EffectSystem.md section 81)
  defs.push({ type: 'ys_req_battle_phase', message0: 'it is %1 Battle Phase', args0: [{ type: 'field_dropdown', name: 'WHO',
    options: [['either player\'s', 'battle_phase'], ['your', 'your_battle_phase'], ['your opponent\'s', 'opponent_battle_phase']] }],
    output: 'Req', colour: COLOUR.when, tooltip: 'Only usable during that Battle Phase. Tick "Quick Effect" on the hat: an ignition effect is only usable in the Main Phase.' });
  // any phase window (Script: if [your_ | opponent_]main_phase / main_or_battle_phase / battle_phase, your_turn / opponent_turn; section 93)
  defs.push({ type: 'ys_req_phase', message0: 'it is %1 %2', args0: [
    { type: 'field_dropdown', name: 'WHO', options: [['either player\'s', 'ANY'], ['your', 'your_'], ['your opponent\'s', 'opponent_']] },
    { type: 'field_dropdown', name: 'WHEN', options: [['Main Phase', 'main_phase'], ['Main or Battle Phase', 'main_or_battle_phase'], ['Battle Phase', 'battle_phase'], ['turn', 'turn']] }],
    output: 'Req', colour: COLOUR.when, tooltip: 'Only usable then. A monster effect outside your own Main Phase needs "Quick Effect" ticked on the hat ("either player\'s turn" means any time).' });
  // this card battled this turn (Script: if battled: ...; section 93)
  defs.push({ type: 'ys_req_battled', message0: 'this card battled this turn', output: 'Req', colour: COLOUR.when });
  // several conditions, all needed (Script: if a and b: ...)
  defs.push({ type: 'ys_req_and', message0: '%1 and %2', args0: [{ type: 'input_value', name: 'A', check: 'Req' }, { type: 'input_value', name: 'B', check: 'Req' }],
    output: 'Req', colour: COLOUR.when, inputsInline: true, tooltip: 'Both must hold. Put another "and" in a side for three.' });
  // your LP (Script: if lp_at_most(N): ... / if lp_lower: ...; docs/EffectSystem.md section 82)
  defs.push({ type: 'ys_req_lp_at_most', message0: 'your LP are %1 or less', args0: [{ type: 'field_number', name: 'N', value: 2000, min: 0 }],
    output: 'Req', colour: COLOUR.when });
  defs.push({ type: 'ys_req_lp_lower', message0: 'your LP are lower than your opponent\'s', output: 'Req', colour: COLOUR.when });
  for (const cost of COSTS) {
    const { msg, args } = message(cost.text, { N: { type: 'field_number', name: 'N', value: cost.def ?? 1, min: 1, precision: 1 },
      SEL: { type: 'input_value', name: 'SEL', check: 'Selector' } });
    defs.push({ type: 'ys_cost_' + cost.word, message0: msg, args0: args, output: 'Cost', colour: COLOUR.cost, inputsInline: true });
  }
  defs.push({ type: 'ys_cost_self', message0: '%1', args0: [{ type: 'field_dropdown', name: 'WHAT', options: SELF_COSTS }], output: 'Cost', colour: COLOUR.cost,
    tooltip: 'Paid with the card itself; this also says where the effect is used from (the GY, the hand, the field), so the effect needs no "when".' });
  // "banish N X from your GY" / "banish this card and N X from your GY" (Script: banish_grave(N, sel) / banish_self_and(N, sel); docs section 85)
  // also "send N X from your hand to the GY" / "reveal 1 X in your hand" (Script: send_hand(N, sel) / reveal(sel); section 90)
  defs.push({ type: 'ys_cost_pick', message0: '%1 %2 %3', args0: [
    { type: 'field_dropdown', name: 'SELF', options: [['banish from your GY', 'banish_grave'], ['banish this card and, from your GY,', 'banish_self_and'],
      ['send from your hand to the GY', 'send_hand'], ['reveal from your hand (1 card)', 'reveal'],
      ['send from your hand or field to the GY (1 card)', 'send_hand_field']] },
    { type: 'field_number', name: 'N', value: 1, min: 1, max: 5, precision: 1 },
    { type: 'input_value', name: 'SEL', check: 'Selector' }], output: 'Cost', colour: COLOUR.cost, inputsInline: true,
    tooltip: 'The game\'s own cost machines, run as this card with this list (no other card lent). "this card and": also banishes this card, so the effect is used from the GY. Reveal shows 1 card.' });
  for (const action of ACTIONS) {
    const fields = {};
    for (const arg of action.args)
      if (arg.field) fields[arg.field] = actionField(arg);
    const { msg, args } = message(action.text, fields);
    defs.push({ type: blockType(action), message0: msg, args0: args, previousStatement: 'Action', nextStatement: 'Action', colour: COLOUR.action,
      inputsInline: true, tooltip: 'Script: ' + action.name + '(...)' });
  }
  defs.push({
    type: 'ys_move', message0: 'move %1 (up to %2) %3 : %4 %5', args0: [dropdown('WHAT', MOVE_WHAT),
      { type: 'field_number', name: 'N', value: 1, min: 1, max: 5, precision: 1 }, dropdown('SIDE', SIDE),
      { type: 'input_value', name: 'SEL', check: 'Selector' }, dropdown('DEST', MOVE_DEST)],
    previousStatement: 'Action', nextStatement: 'Action', colour: COLOUR.action, inputsInline: true,
    tooltip: 'Move cards (Script: move(...)). "up to" and the side are for chosen cards on the field only; "this card" ignores the cards input. ' +
      'Sent to the GY is not destroyed; "and destroy it" destroys the chosen cards.',
  });
  defs.push({
    type: 'ys_selector',
    message0: 'a %1', args0: [{ type: 'field_dropdown', name: 'KIND', options: [['monster', 'monster'], ['Spell', 'spell'], ['Trap', 'trap'], ['card', 'card'], ['(anything)', 'NONE']] }],
    message1: 'where %1', args1: [{ type: 'input_statement', name: 'CONDS', check: 'Cond' }],
    message2: 'except this card %1', args2: [{ type: 'field_checkbox', name: 'EXCEPT_SELF', checked: true }],
    output: 'Selector', colour: COLOUR.cards, tooltip: 'Which cards: a kind, and any number of conditions (all must be true). "except this card" (on by default) leaves out cards with the same name as this one when searching, reviving or adding to the hand; untick it to allow them (Script: including self).',
  });
  defs.push({
    type: 'ys_or',
    message0: 'any of %1 or %2', args0: [{ type: 'input_value', name: 'A', check: 'Selector' }, { type: 'input_value', name: 'B', check: 'Selector' }],
    output: 'Selector', colour: COLOUR.cards, inputsInline: false,
    tooltip: 'Cards matching either side (search, revive, add to hand, Special Summon, send). Put another "any of" in a side for three or more (Script: ... or ...).',
  });
  const cond = (type, msg, args, tooltip) => defs.push({ type, message0: msg, args0: args, previousStatement: 'Cond', nextStatement: 'Cond', colour: COLOUR.cards, tooltip });
  cond('ys_cond_race', 'Type is %1', [{ type: 'field_dropdown', name: 'V', options: RACES.map((r) => [spaced(r), r]) }]);
  cond('ys_cond_attribute', 'Attribute is %1', [{ type: 'field_dropdown', name: 'V', options: ATTRIBUTES.map((a) => [a, a]) }]);
  cond('ys_cond_number', '%1 %2 %3', [
    { type: 'field_dropdown', name: 'F', options: [['Level', 'level'], ['ATK', 'atk']] },
    { type: 'field_dropdown', name: 'OP', options: [['is', '='], ['is at most', '<='], ['is at least', '>='], ['is under', '<'], ['is over', '>']] },
    { type: 'field_number', name: 'V', value: 4, min: 0, precision: 1 }]);
  cond('ys_cond_archetype', 'archetype is %1', [{ type: 'field_input', name: 'V', text: 'Gem-Knight' }], 'An archetype name (from Archetypes.json) or code.');
  cond('ys_cond_name', 'card is %1', [{ type: 'field_input', name: 'V', text: '' }], 'A Konami id (search / send only).');
  cond('ys_cond_raw', 'condition %1', [{ type: 'field_input', name: 'V', text: 'level <= 4' }], 'Written as in the script.');
  Blockly.common.defineBlocksWithJsonArray(defs);
}

function toolbox() {
  const block = (type, extra) => Object.assign({ kind: 'block', type }, extra || {});
  const withSelector = (type, kind) => block(type, { inputs: { SEL: { block: { type: 'ys_selector', fields: { KIND: kind } } } } });
  return {
    kind: 'categoryToolbox',
    contents: [
      { kind: 'category', name: 'Effect', colour: COLOUR.effect, contents: [...HATS.map((h) => block(h.type)), block('ys_as')] },
      { kind: 'category', name: 'While face-up', colour: COLOUR.lock, contents: [block('ys_lock'), block('ys_hand_limit'), block('ys_aura'), block('ys_protect'), block('ys_name_as'), block('ys_only_one'), block('ys_attack_directly'), withSelector('ys_xyz_also', 'monster'),
        withSelector('ys_count', 'monster'), block('ys_count_self'), block('ys_count_and')] },
      { kind: 'category', name: 'Only if', colour: COLOUR.when, contents: [block('ys_req_no_monsters'), withSelector('ys_req_controls', 'monster'), block('ys_req_phase'), block('ys_req_battled'), block('ys_req_and'), block('ys_req_lp_at_most'), block('ys_req_lp_lower')] },
      { kind: 'category', name: 'Cost', colour: COLOUR.cost, contents: [...COSTS.map((c) => block('ys_cost_' + c.word)), block('ys_cost_self'), withSelector('ys_cost_pick', 'monster')] },
      {
        kind: 'category', name: 'Actions', colour: COLOUR.action,
        contents: ACTIONS.map((a) => (a.args.some((arg) => arg.kind === 'sel')
          ? withSelector(blockType(a), a.name === 'destroy' || a.name === 'banish' || a.name === 'return_to_hand' || a.name === 'send_to_grave' ? 'card' : a.name === 'set' ? 'spell' : 'monster')
          : block(blockType(a)))).concat([withSelector('ys_move', 'card')]),
      },
      {
        kind: 'category', name: 'Which cards', colour: COLOUR.cards,
        contents: [block('ys_selector'), block('ys_or'), block('ys_cond_race'), block('ys_cond_attribute'), block('ys_cond_number'), block('ys_cond_archetype'), block('ys_cond_name'), block('ys_cond_raw')],
      },
    ],
  };
}

// ---------------------------------------------------------------- blocks -> script

const quote = (text) => '"' + text.replace(/"/g, '') + '"';
const isNumber = (text) => /^\d+$/.test(text.trim());

function selectorText(block) {
  if (!block) return '';
  if (block.type === 'ys_or')
    return [selectorText(block.getInputTargetBlock('A')), selectorText(block.getInputTargetBlock('B'))].filter((s) => s).join(' or ');
  const kind = block.getFieldValue('KIND');
  const conds = [];
  for (let c = block.getInputTargetBlock('CONDS'); c; c = c.getNextBlock()) {
    const v = c.getFieldValue('V');
    switch (c.type) {
      case 'ys_cond_race': conds.push('race = ' + v); break;
      case 'ys_cond_attribute': conds.push('attribute = ' + v); break;
      case 'ys_cond_number': conds.push(c.getFieldValue('F') + ' ' + c.getFieldValue('OP') + ' ' + v); break;
      case 'ys_cond_archetype': conds.push('archetype = ' + (isNumber(v) ? v.trim() : quote(v))); break;
      case 'ys_cond_name': conds.push('name = ' + (isNumber(v) ? v.trim() : quote(v))); break;
      case 'ys_cond_raw': if (v.trim()) conds.push(v.trim()); break;
    }
  }
  const where = conds.join(' and ');
  const self = block.getFieldValue('EXCEPT_SELF') === 'FALSE' ? ' including self' : '';
  if (kind === 'NONE') return where ? where + self : '';
  return (where ? kind + ' where ' + where : kind) + self;
}

function moveText(block) {
  const what = block.getFieldValue('WHAT') || 'target';
  const parts = [what];
  const sel = selectorText(block.getInputTargetBlock('SEL'));
  if (what === 'target') {
    if (Number(block.getFieldValue('N')) > 1) parts.push(String(block.getFieldValue('N')));
    const side = block.getFieldValue('SIDE');
    if (side && side !== 'DEFAULT') parts.push(side);
    if (sel) parts.push(sel);
  } else if (what !== 'self') {
    if (Number(block.getFieldValue('N')) > 1) parts.push(String(block.getFieldValue('N')));   // up to N picked one after another (section 95)
    if (sel) parts.push(sel);
  }
  return 'move(' + parts.join(', ') + ', ' + (block.getFieldValue('DEST') || 'to_hand') + ')';
}

function actionText(block) {
  if (block.type === 'ys_move') return moveText(block);
  const action = ACTIONS.find((a) => blockType(a) === block.type);
  if (!action) return '';
  const parts = [];
  const zones = [];   // the ticked 'zone' boxes, written as one argument: hand or deck or grave
  for (const arg of action.args) {
    switch (arg.kind) {
      case 'zone':
        if (block.getFieldValue(arg.field) === 'TRUE') zones.push(arg.value);
        if (arg.last) parts.push(zones.join(' or '));
        break;
      case 'lit': parts.push(arg.value); break;
      case 'num': if (!(arg.omitOne && Number(block.getFieldValue(arg.field)) === 1)) parts.push(String(block.getFieldValue(arg.field))); break;
      case 'pick': { const v = block.getFieldValue(arg.field); if (!(arg.optional && v === 'DEFAULT')) parts.push(v); break; }
      case 'sel': { const s = selectorText(block.getInputTargetBlock(arg.field)); if (s) parts.push(s); break; }
      case 'stat': { const v = String(block.getFieldValue(arg.field) || '').trim(); if (isNumber(v)) parts.push(arg.stat + ' ' + v); break; }
    }
  }
  return action.name + '(' + parts.join(', ') + ')';
}

function costText(block) {
  if (!block) return '';
  if (block.type === 'ys_cost_self') return block.getFieldValue('WHAT');
  if (block.type === 'ys_cost_pick') {
    const sel = selectorText(block.getInputTargetBlock('SEL')) || 'monster';
    if (block.getFieldValue('SELF') === 'reveal' || block.getFieldValue('SELF') === 'send_hand_field') return block.getFieldValue('SELF') + '(' + sel + ')';
    return block.getFieldValue('SELF') + '(' + block.getFieldValue('N') + ', ' + sel + ')';
  }
  const cost = COSTS.find((c) => 'ys_cost_' + c.word === block.type);
  if (!cost) return '';
  const sel = cost.sel ? selectorText(block.getInputTargetBlock('SEL')) : '';
  return cost.word + '(' + block.getFieldValue('N') + (sel ? ', ' + sel : '') + ')';
}

function reqText(block) {
  if (!block) return '';
  if (block.type === 'ys_req_no_monsters') return 'no_monsters';
  if (block.type === 'ys_req_controls') return 'controls(' + (selectorText(block.getInputTargetBlock('SEL')) || 'monster') + ')';
  if (block.type === 'ys_req_battle_phase') return block.getFieldValue('WHO') || 'battle_phase';
  if (block.type === 'ys_req_lp_at_most') return 'lp_at_most(' + (block.getFieldValue('N') ?? 0) + ')';
  if (block.type === 'ys_req_lp_lower') return 'lp_lower';
  if (block.type === 'ys_req_phase') {
    const whoField = block.getFieldValue('WHO'), when = block.getFieldValue('WHEN') || 'main_phase';
    const who = !whoField || whoField === 'ANY' ? '' : whoField;
    return when === 'turn' ? (who ? who + 'turn' : '') : who + when;
  }
  if (block.type === 'ys_req_battled') return 'battled';
  if (block.type === 'ys_req_and')
    return [reqText(block.getInputTargetBlock('A')), reqText(block.getInputTargetBlock('B'))].filter((r) => r).join(' and ');
  return '';
}

function effectBody(block) {
  const actions = [];
  for (let a = block.getInputTargetBlock('DO'); a; a = a.getNextBlock()) actions.push(actionText(a));
  if (actions.length === 0) return '';   // nothing to do yet: not written
  let text = '';
  const trigger = hatOf(block).trigger(block);
  if (trigger !== 'NONE') text += 'on ' + trigger + summonConditionsText(block, trigger) + ': ';
  const req = reqText(block.getInputTargetBlock('REQ'));
  if (req) text += 'if ' + req + ': ';
  const cost = costText(block.getInputTargetBlock('COST'));
  if (cost) text += 'cost ' + cost + ': ';
  text += actions.join(' then ') + ';';
  if (block.getFieldValue('QUICK') === 'TRUE') text += ' quick;';
  if (block.getFieldValue('ONCE') === 'TRUE') text += ' once_per_turn;';
  else if (block.getFieldValue('ONCE_COPY') === 'TRUE') text += ' once_per_turn(copy);';
  else if (block.getFieldValue('LIMIT') === 'DUEL') text += ' once_per_duel;';
  else if (['2', '3', '4'].includes(String(block.getFieldValue('LIMIT')))) text += ' uses_per_turn(' + block.getFieldValue('LIMIT') + ');';
  const noSummon = String(block.getFieldValue('NOSUMMON') || 'NONE');
  if (noSummon !== 'NONE') {
    const args = [];
    if (noSummon === 'EXTRA') args.push('extra');
    if (block.getFieldValue('NOSUMMON_WHEN') === 'RESOLVE') args.push('resolved');
    const kind = String(block.getFieldValue('NOSUMMON_KIND') || 'ANY');
    if (kind !== 'ANY') args.push(kind);
    const sel = selectorText(block.getInputTargetBlock('NOSUMMON_EXCEPT'));
    if (sel) args.push('except ' + sel);
    text += ' no_special_summon' + (args.length ? '(' + args.join(', ') + ')' : '') + ';';
  }
  return text;
}

function summonConditionsText(block, trigger) {
  if (WATCHES.some((o) => o[1] === trigger)) {
    const parts = [];
    const which = selectorText(block.getInputTargetBlock('WHICH'));
    if (which) parts.push('is ' + which);
    if (block.getFieldValue('WSIDE') && block.getFieldValue('WSIDE') !== 'ANY') parts.push(block.getFieldValue('WSIDE'));
    if (block.getFieldValue('WFROM') && block.getFieldValue('WFROM') !== 'ANY') parts.push(...block.getFieldValue('WFROM').split('+').map((z) => 'from ' + z));
    if (block.getFieldValue('WBY') && block.getFieldValue('WBY') !== 'ANY') parts.push('by ' + block.getFieldValue('WBY'));
    if (block.getFieldValue('WOTHER') === 'TRUE') parts.push('other');
    if (block.getFieldValue('WGRAVE') === 'TRUE') parts.push('in_grave');
    return parts.length ? '(' + parts.join(', ') + ')' : '';
  }
  if (MOVE_TRIGGERS.includes(trigger)) {
    const parts = [];
    if (block.getFieldValue('MFROM') && block.getFieldValue('MFROM') !== 'ANY') parts.push('from ' + block.getFieldValue('MFROM'));
    if (block.getFieldValue('MBY') && block.getFieldValue('MBY') !== 'ANY') parts.push('by ' + block.getFieldValue('MBY'));
    if (SUMMON_REACTIONS.includes(trigger)) {
      const which = selectorText(block.getInputTargetBlock('WHICH'));
      if (which) parts.push('is ' + which);
      if (block.getFieldValue('RSIDE') && block.getFieldValue('RSIDE') !== 'ANY') parts.push(block.getFieldValue('RSIDE'));
    }
    return parts.length ? '(' + parts.join(', ') + ')' : '';
  }
  if (!SUMMON_TRIGGERS.includes(trigger)) return '';
  const parts = [];
  if (block.getFieldValue('FROM') !== 'ANY') parts.push('from ' + block.getFieldValue('FROM'));
  if (block.getFieldValue('BY') !== 'ANY') parts.push('by ' + block.getFieldValue('BY'));
  if (block.getFieldValue('HOW') !== 'ANY') parts.push(block.getFieldValue('HOW'));
  return parts.length ? '(' + parts.join(', ') + ')' : '';
}

function lockText(block) {
  const only = block.getFieldValue('ONLY');
  const words = LOCK_WHAT.filter(([field]) => block.getFieldValue(field) === 'TRUE' || (field === 'MONSTER' && only !== 'ALL'))
    .map(([, word]) => (word === 'monster' && only !== 'ALL' ? only : word));
  if (words.length === 0) return '';
  if (block.getFieldValue('WHO') !== 'both' && only === 'ALL') words.push(block.getFieldValue('WHO'));   // a filter covers both players
  if (block.getFieldValue('WHERE') !== 'AUTO') words.push(block.getFieldValue('WHERE'));
  return 'lock(' + words.join(', ') + ');';
}

function handLimitText(block) {
  const words = [String(block.getFieldValue('N')), block.getFieldValue('WHO')];   // whose hand is always written (no default)
  if (block.getFieldValue('WHERE') !== 'AUTO') words.push(block.getFieldValue('WHERE'));
  return 'hand_limit(' + words.join(', ') + ');';
}

function generate(workspace) {
  const tops = workspace.getTopBlocks(true);
  const lockBlock = tops.find((b) => b.type === 'ys_lock');
  const lock = lockBlock ? lockText(lockBlock) : '';
  const handBlock = tops.find((b) => b.type === 'ys_hand_limit');
  const hand = handBlock ? handLimitText(handBlock) : '';
  const auras = tops.filter((b) => b.type === 'ys_aura').map(auraText).filter((s) => s).join('\n');
  const protects = tops.filter((b) => b.type === 'ys_protect').map(protectText).join('\n');
  const nameBlock = tops.find((b) => b.type === 'ys_name_as');
  const nameAs = nameBlock ? nameAsText(nameBlock) : '';
  const onlyBlock = tops.find((b) => b.type === 'ys_only_one');
  const onlyOne = onlyBlock ? (onlyBlock.getFieldValue('WHERE') === 'field' ? 'only_one(field);' : 'only_one;') : '';
  const directBlock = tops.find((b) => b.type === 'ys_attack_directly');
  const direct = directBlock ? whileText(directBlock) + 'attack_directly;' : '';
  const xyzBlock = tops.find((b) => b.type === 'ys_xyz_also');
  const xyzAlso = xyzBlock ? xyzAlsoText(xyzBlock) : '';
  const body = generateEffects(tops);
  return [lock, hand, auras, protects, nameAs, onlyOne, direct, xyzAlso, body].filter((s) => s).join('\n');
}

function xyzAlsoText(block) {
  const parts = [selectorText(block.getInputTargetBlock('SEL')) || 'monster'];
  if (block.getFieldValue('XYZ') === 'xyz') parts.push('xyz_monster');
  if (block.getFieldValue('RANK') === 'range') {
    const min = Number(block.getFieldValue('MIN')), max = Number(block.getFieldValue('MAX'));
    if (min === max) parts.push('rank ' + min);
    else {
      if (min > 1) parts.push('rank_at_least ' + min);
      if (max < 13) parts.push('rank_at_most ' + max);
    }
  }
  return 'xyz_also(' + parts.join(', ') + ');';
}

// xyzAlsoStatement: xyz_also ( selector (, xyz_monster | rank N | rank_at_least N | rank_at_most N)* )
function xyzAlsoState(s) {
  const state = { type: 'ys_xyz_also', fields: { XYZ: 'ANY', RANK: 'ANY', MIN: 1, MAX: 13 }, inputs: { SEL: { block: selectorState(rule(s, 'selector')) } } };
  for (const arg of rules(s, 'xyzAlsoArg')) {
    const w = words(arg).map(String);
    if (w[0] === 'xyz_monster') { state.fields.XYZ = 'xyz'; continue; }
    state.fields.RANK = 'range';
    const n = Number(w[1]);
    if (w[0] !== 'rank_at_most') state.fields.MIN = n;
    if (w[0] !== 'rank_at_least') state.fields.MAX = n;
  }
  return state;
}

function protectText(block) {
  const [verb, args] = String(block.getFieldValue('FROM') || 'indestructible:battle').split(':');
  const words = args.split(' ');
  const who = block.getFieldValue('WHO') || 'self';
  if (who !== 'self') {
    words.push(who);
    const sel = selectorText(block.getInputTargetBlock('SEL'));
    if (sel) words.push(sel);
  }
  return whileText(block) + verb + '(' + words.join(', ') + ');';
}

// "while <tests>: " from a continuous block's WHILE input ('' without one; docs/EffectSystem.md section 89)
function whileText(block) {
  const tests = countText(block.getInputTargetBlock('WHILE'), false);
  return tests ? 'while ' + tests + ': ' : '';
}

// a ys_count / ys_count_self / ys_count_and chain as script; per = the "for each" form (no number, no 'not', materials)
function countText(block, per) {
  if (!block) return '';
  if (block.type === 'ys_count_and')
    return [countText(block.getInputTargetBlock('A'), per), countText(block.getInputTargetBlock('B'), per)].filter((s) => s).join(' and ');
  if (block.type === 'ys_count_self') {
    const test = block.getFieldValue('TEST');
    if (per) return test === 'has_material' ? 'materials' : '';
    return test === 'lp_at_most' ? 'lp_at_most(' + (Number(block.getFieldValue('N')) || 0) + ')' : test;
  }
  const args = [];
  const n = Number(block.getFieldValue('N')) || 1;
  if (!per && n !== 1) args.push(String(n));
  if (block.getFieldValue('SIDE') !== 'own') args.push(block.getFieldValue('SIDE'));
  if (block.getFieldValue('OTHER') === 'TRUE') args.push('other');
  args.push(selectorText(block.getInputTargetBlock('SEL')) || 'card');
  return (!per && block.getFieldValue('NOT') === 'not' ? 'not ' : '') + block.getFieldValue('PLACE') + '(' + args.join(', ') + ')';
}

// a whileClause (or one countTest for 'per') -> the block chain
function whileState(clause) {
  const states = rules(clause, 'whileTest').map((test) => {
    const count = rule(test, 'countTest');
    if (count) return countState(count, words(test).map(String).includes('not'));
    const word = String(test.c[0]);
    return { type: 'ys_count_self', fields: { TEST: word, N: word === 'lp_at_most' ? Number(words(test).find(isNumber)) : 2000 } };
  });
  return states.reduceRight((rest, s) => (rest ? { type: 'ys_count_and', inputs: { A: { block: s }, B: { block: rest } } } : s), null);
}

function countState(count, not) {
  const place = String(count.c[0]);
  if (place === 'materials') return { type: 'ys_count_self', fields: { TEST: 'has_material', N: 2000 } };
  const fields = { NOT: not ? 'not' : 'min', N: 1, SIDE: 'own', PLACE: place, OTHER: false };
  const inputs = {};
  for (const arg of rules(count, 'countArg')) {
    const selector = rule(arg, 'selector');
    const side = rule(arg, 'side');
    if (selector) inputs.SEL = { block: selectorState(selector) };
    else if (side) fields.SIDE = text(side);
    else if (text(arg) === 'other') fields.OTHER = true;
    else fields.N = Number(text(arg));
  }
  return { type: 'ys_count', fields, inputs };
}

// a continuous statement's state with its while clause plugged into WHILE
function withWhile(state, continuous) {
  const clause = rule(continuous, 'whileClause');
  if (clause) state.inputs = Object.assign(state.inputs || {}, { WHILE: { block: whileState(clause) } });
  return state;
}

const PROTECT_SIDES = ['own', 'opponent', 'any', 'self'];

// name_as("card" | id, field | grave): the card's name becomes another card's (docs/EffectSystem.md section 73)
function nameAsText(block) {
  const card = String(block.getFieldValue('CARD') || '').trim();
  if (!card) return '';
  const what = isNumber(card) ? card : '"' + card.replace(/"/g, '') + '"';
  return 'name_as(' + what + (block.getFieldValue('WHERE') === 'field' ? ', field' : '') + ');';
}

function nameAsState(statement) {
  const parts = words(statement).map(String);
  const card = parts.find((w) => isNumber(w) || /^".*"$/.test(w)) || '';
  return { type: 'ys_name_as', fields: { CARD: card.replace(/^"|"$/g, ''), WHERE: parts.includes('field') ? 'field' : 'grave' } };
}

function protectState(statement) {
  const verb = String(statement.c[0]);
  const from = [];
  const fields = { WHO: 'self' };
  const inputs = {};
  for (const arg of rules(statement, 'protectArg')) {
    const selector = rule(arg, 'selector');
    const word = text(arg);
    if (selector) inputs.SEL = { block: selectorState(selector) };
    else if (PROTECT_SIDES.includes(word)) fields.WHO = word;
    else from.push(word);
  }
  fields.FROM = verb + ':' + from.join(' ');
  if (!PROTECT_FROM.some(([, v]) => v === fields.FROM)) throw new Unsupported('no block for ' + verb + '(' + from.join(', ') + ')');
  return { type: 'ys_protect', fields, inputs };
}

function auraText(block) {
  const words = [];
  const atk = Number(block.getFieldValue('ATK')) || 0, def = Number(block.getFieldValue('DEF')) || 0;
  if (atk) words.push('atk ' + atk);
  if (def) words.push('def ' + def);
  if (words.length === 0) return '';   // nothing changes yet: not written
  words.push(block.getFieldValue('WHO'));
  const sel = selectorText(block.getInputTargetBlock('SEL'));
  if (sel) words.push(sel);
  const per = countText(block.getInputTargetBlock('PER'), true);
  if (per) words.push('per ' + per);
  return whileText(block) + 'aura(' + words.join(', ') + ');';
}

function auraState(statement) {
  const fields = { WHO: 'own', ATK: 0, DEF: 0 };
  const inputs = {};
  for (const arg of rules(statement, 'auraArg')) {
    const stat = rule(arg, 'auraStat');
    const selector = rule(arg, 'selector');
    const per = rule(arg, 'countTest');
    if (stat) {
      const w = text(stat).split(' ').filter((x) => x.length);   // atk [-] N
      const n = Number(w[w.length - 1]) * (w.includes('-') ? -1 : 1);
      fields[w[0].toUpperCase()] = n;
    } else if (per) inputs.PER = { block: countState(per, false) };
    else if (selector) inputs.SEL = { block: selectorState(selector) };
    else fields.WHO = text(arg);
  }
  return { type: 'ys_aura', fields, inputs };
}

function asText(as) {
    const card = String(as.getFieldValue('CARD') || '').trim();
    const stats = [['atk', as.getFieldValue('ATK')], ['def', as.getFieldValue('DEF')]].filter(([, v]) => isNumber(String(v || ''))).map(([s, v]) => s + ' ' + String(v).trim());
    const when = as.getFieldValue('WHEN');
    return (when && when !== 'NONE' ? 'on ' + when + ': ' : '') + 'as(' + (isNumber(card) ? card : quote(card)) + ')' +
      (stats.length ? ' with ' + stats.join(', ') : '') + (as.getFieldValue('EFFECT_ONLY') === 'TRUE' ? ' effect_only' : '') + ';';
}

function generateEffects(tops) {
  // the hats in reading order: top to bottom, then left to right (getTopBlocks(true) sorts that way)
  const effects = tops.filter((b) => b.type === 'ys_as' || (hatOf(b) && effectBody(b)));
  if (effects.length === 0) return '';
  const body = (e) => e.type === 'ys_as' ? asText(e) : effectBody(e);
  // 'or' after an effect whose "or the next effect" box is ticked ("activate 1 of these effects"), else 'also' / nothing between blocks
  const orNext = (i) => i + 1 < effects.length && effects[i].type !== 'ys_as' && effects[i].getFieldValue('OR_NEXT') === 'TRUE';
  if (effects.some((e) => String(e.getFieldValue('NAME') || '').trim()))
    return effects.map((e, i) => {
      const name = String(e.getFieldValue('NAME') || '').trim();
      return 'effect ' + (name ? quote(name) + ' ' : '') + '{\n    ' + body(e) + '\n}' + (orNext(i) ? '\nor' : '');
    }).join('\n');
  return effects.map((e, i) => (i ? (orNext(i - 1) ? '\nor ' : '\nalso ') : '') + body(e)).join('');
}

// ---------------------------------------------------------------- script (parse tree) -> blocks

class Unsupported extends Error {}

const isNode = (n) => n && typeof n === 'object';
const rules = (node, r) => (node.c || []).filter((c) => isNode(c) && c.r === r);
const rule = (node, r) => rules(node, r)[0];
const text = (n) => (isNode(n) ? (n.c || []).map(text).join(' ') : n);
const words = (node) => (node.c || []).filter((c) => !isNode(c));

function selectorState(node) {
  if (!node) return null;
  const kindNode = rule(node, 'kind');
  const state = { type: 'ys_selector', fields: { KIND: kindNode ? text(kindNode) : 'NONE', EXCEPT_SELF: !rule(node, 'includingSelf') } };
  const conds = rules(node, 'condition').map((c) => {
    const field = text(rule(c, 'field')), op = text(rule(c, 'op')), raw = text(rule(c, 'value'));
    const value = raw.replace(/^"|"$/g, '');
    const norm = value.replace(/[\s_-]|type$/gi, '').toLowerCase();
    if (field === 'race' && op === '=') {
      const race = RACES.find((r) => r.toLowerCase() === norm);
      if (race) return { type: 'ys_cond_race', fields: { V: race } };
    }
    if (field === 'attribute' && op === '=') {
      const attribute = ATTRIBUTES.find((a) => a.toLowerCase() === norm);
      if (attribute) return { type: 'ys_cond_attribute', fields: { V: attribute } };
    }
    if ((field === 'level' || field === 'atk') && isNumber(value)) return { type: 'ys_cond_number', fields: { F: field, OP: op, V: Number(value) } };
    if ((field === 'archetype' || field === 'name') && op === '=') return { type: 'ys_cond_' + field, fields: { V: value } };
    return { type: 'ys_cond_raw', fields: { V: field + ' ' + op + ' ' + raw } };
  });
  if (conds.length) state.inputs = { CONDS: { block: chain(conds) } };
  return state;
}

// "a or b or c" -> any of (a, any of (b, c))
function choicesState(node) {
  const states = rules(node, 'selector').map(selectorState);
  const nest = (list) => (list.length === 1 ? list[0] : { type: 'ys_or', inputs: { A: { block: list[0] }, B: { block: nest(list.slice(1)) } } });
  return nest(states);
}

function chain(states) {
  for (let i = states.length - 2; i >= 0; i--) states[i].next = { block: states[i + 1] };
  return states[0];
}

// moveAction: move ( moveWhat , moveDest )
function moveState(inner) {
  const what = rule(inner, 'moveWhat');
  const first = isNode(what.c[0]) ? 'list' : String(what.c[0]);
  const state = { type: 'ys_move', fields: { N: 1, SIDE: 'DEFAULT', DEST: text(rule(inner, 'moveDest')) }, inputs: {} };
  const selector = rule(what, 'selector') || rule(what, 'choices');
  if (selector) state.inputs.SEL = { block: selector.r === 'choices' ? choicesState(selector) : selectorState(selector) };
  if (first === 'list') {
    state.fields.WHAT = rules(what, 'moveFrom').map(text).join(' or ');
    if (!MOVE_WHAT.some((o) => o[1] === state.fields.WHAT)) throw new Unsupported('no block for move(' + state.fields.WHAT + ', ...) yet');
    const n = words(what).find(isNumber);
    if (n) state.fields.N = Number(n);
  } else {
    state.fields.WHAT = first;
    const n = words(what).find(isNumber);
    if (n) state.fields.N = Number(n);
    const side = rule(what, 'side');
    if (side) state.fields.SIDE = text(side);
  }
  return state;
}

function actionState(actionNode) {
  const inner = actionNode.c[0];
  if (inner.r === 'moveAction') return moveState(inner);
  const name = inner.c[0];
  const action = ACTIONS.find((a) => a.name === name && (!a.rule || a.rule === inner.r) && (!a.when || a.when(inner)));
  if (!action) throw new Unsupported('the block editor has no block for ' + name + '(...) yet');
  const items = inner.c.slice(1).filter((c) => !['(', ')', ','].includes(c));
  const state = { type: blockType(action), fields: {}, inputs: {} };
  let i = 0;
  for (const arg of action.args) {
    const item = items[i];
    switch (arg.kind) {
      case 'zone':
        if (isNode(item) && item.r === (arg.rule || 'summonZones')) {
          state.fields[arg.field] = rules(item, arg.item || 'summonZone').some((z) => text(z) === arg.value);
          if (arg.last) i++;
        }
        break;
      case 'lit': if (item === arg.value) i++; break;
      case 'num':
        if (typeof item === 'string' && isNumber(item)) { state.fields[arg.field] = Number(item); i++; }
        else if (arg.signed && item === '-' && typeof items[i + 1] === 'string' && isNumber(items[i + 1])) { state.fields[arg.field] = -Number(items[i + 1]); i += 2; }
        else if (arg.omitOne) state.fields[arg.field] = 1;
        break;
      case 'pick': {
        const matches = arg.token ? typeof item === 'string' && arg.options.some((o) => o[1] === item) : isNode(item) && item.r === arg.rule && arg.options.some((o) => o[1] === text(item));
        if (matches) { state.fields[arg.field] = text(item); i++; } else if (arg.optional) state.fields[arg.field] = 'DEFAULT';
        break;
      }
      case 'sel':
        if (isNode(item) && item.r === 'selector') { state.inputs[arg.field] = { block: selectorState(item) }; i++; }
        else if (isNode(item) && item.r === 'choices') { state.inputs[arg.field] = { block: choicesState(item) }; i++; }
        else state.inputs[arg.field] = { block: { type: 'ys_selector', fields: { KIND: 'NONE' } } };
        break;
      case 'stat':
        if (isNode(item) && item.r === 'statPart' && item.c[0] === arg.stat) { state.fields[arg.field] = String(item.c[1]); i++; }
        break;
    }
  }
  if (i < items.length) throw new Unsupported('the block for ' + name + ' does not have a place for "' + text(items[i]) + '"');
  return state;
}

function summonConditionsState(conditions) {
  const fields = {};
  for (const c of conditions.c.filter((n) => isNode(n) && n.r === 'summonCondition')) {
    const w = text(c).split(' ').filter((x) => x.length);
    const set = (field, value, options) => {
      if (fields[field] !== undefined || !options.some((o) => o[1] === value)) throw new Unsupported('no block for the summon condition ' + w.join(' ') + ' yet');
      fields[field] = value;
    };
    if (w[0] === 'from') set('FROM', w[1], SUMMON_FROM);
    else if (w[0] === 'by') set('BY', w[1], SUMMON_BY);
    else set('HOW', w.join(' '), SUMMON_HOW);
  }
  return fields;
}

function effectState(body, name) {
  const source = rule(body, 'asScript');
  if (source) return asState(source, rule(body, 'trigger'), name);
  const trigger = rule(body, 'trigger');
  const t = trigger ? text(rule(trigger, 'triggerName')) : 'NONE';
  const hat = HATS.find((h) => h.has(t));
  if (!hat) throw new Unsupported('no block for the trigger ' + t + ' yet');
  const limit = rule(body, 'limit');
  const limitWords = limit ? words(limit).map(String) : [];
  const other = limitWords.includes('once_per_duel') ? 'DUEL' : limitWords.includes('uses_per_turn') ? (limitWords.find(isNumber) || 'NONE') : 'NONE';
  const perCopy = limitWords.includes('copy') || other === '1';
  const state = { type: hat.type, fields: { NAME: name || '', QUICK: !!rule(body, 'quickMark'), ONCE: !!limit && !perCopy && other === 'NONE',
    ONCE_COPY: perCopy, LIMIT: other === '1' ? 'NONE' : other }, inputs: {} };
  if (!['NONE', 'DUEL', '2', '3', '4'].includes(state.fields.LIMIT)) throw new Unsupported('no block for uses_per_turn(' + other + ')');
  const noSummon = rule(body, 'noSummon');
  state.fields.NOSUMMON = 'NONE';
  state.fields.NOSUMMON_KIND = 'ANY';
  state.fields.NOSUMMON_WHEN = 'ACTIVATE';
  if (noSummon) {
    state.fields.NOSUMMON = 'ANY';
    for (const arg of rules(noSummon, 'noSummonArg')) {
      const selector = rule(arg, 'selector');
      const word = text(arg);
      if (selector) state.inputs.NOSUMMON_EXCEPT = { block: selectorState(selector) };
      else if (word === 'extra') state.fields.NOSUMMON = 'EXTRA';
      else if (word === 'resolved') state.fields.NOSUMMON_WHEN = 'RESOLVE';
      else state.fields.NOSUMMON_KIND = word;
    }
  }
  if (t !== 'NONE' && t !== 'ignition') state.fields.TRIGGER = t;
  if (hat.type === 'ys_on_summon') Object.assign(state.fields, { FROM: 'ANY', BY: 'ANY', HOW: 'ANY' });   // the conditions not written
  if (hat.type === 'ys_on_event') Object.assign(state.fields, { MFROM: 'ANY', MBY: 'ANY', RSIDE: 'ANY' });
  if (hat.type === 'ys_on_watch') Object.assign(state.fields, { WSIDE: 'ANY', WFROM: 'ANY', WBY: 'ANY', WOTHER: false, WGRAVE: false });
  const conditions = trigger && rule(trigger, 'summonConditions');
  if (conditions && hat.type === 'ys_on_watch') {
    for (const c of rules(conditions, 'summonCondition')) {
      const w = text(c).split(' ').filter((x) => x.length);
      if (w[0] === 'is' && rule(c, 'selector')) state.inputs.WHICH = { block: selectorState(rule(c, 'selector')) };
      else if (isNode(c.c[0]) && c.c[0].r === 'side') state.fields.WSIDE = w[0] === 'any' ? 'ANY' : w[0];
      else if (w[0] === 'from') {
        const zones = state.fields.WFROM === 'ANY' ? [w[1]] : [...state.fields.WFROM.split('+'), w[1]];
        const both = WATCH_FROM.map((o) => o[1]).find((v) => v.split('+').length === zones.length && zones.every((z) => v.split('+').includes(z))) ?? zones.join('+');
        if (!WATCH_FROM.some((o) => o[1] === both)) throw new Unsupported('no block for "from ' + both.replace('+', ', from ') + '" yet');
        state.fields.WFROM = both;
      } else if (w[0] === 'by') state.fields.WBY = w[1];
      else if (w[0] === 'other') state.fields.WOTHER = true;
      else if (w[0] === 'in_grave') state.fields.WGRAVE = true;
    }
  } else if (conditions) {
    if (MOVE_TRIGGERS.includes(t)) {
      // one "from" and one "by" fit the hat's two boxes; the summon reactions also take "is <selector>" and a side
      for (const c of rules(conditions, 'summonCondition')) {
        const w = text(c).split(' ').filter((x) => x.length);
        if (w[0] === 'is' && rule(c, 'selector') && SUMMON_REACTIONS.includes(t)) state.inputs.WHICH = { block: selectorState(rule(c, 'selector')) };
        else if (isNode(c.c[0]) && c.c[0].r === 'side' && SUMMON_REACTIONS.includes(t)) {
          if (!REACT_SIDE.some((o) => o[1] === w[0])) throw new Unsupported('no block for the summon side ' + w[0] + ' yet');
          state.fields.RSIDE = w[0];
        } else if (w[0] === 'from') {
          if (state.fields.MFROM !== 'ANY') throw new Unsupported('no block for two "from" zones yet');
          state.fields.MFROM = w[1];
        } else if (w[0] === 'by') state.fields.MBY = w[1];
      }
    } else {
      if (hat.type !== 'ys_on_summon') throw new Unsupported('summon conditions only go with a summon trigger');
      Object.assign(state.fields, summonConditionsState(conditions));
    }
  }
  const req = rule(body, 'requireClause');
  if (req) {
    const condState = (cond) => {
      const word = String(cond.c[0]);
      const phase = word.match(/^(your_|opponent_)?(main_phase|main_or_battle_phase|battle_phase|turn)$/);
      return word === 'no_monsters' ? { type: 'ys_req_no_monsters' }
        : word === 'battled' ? { type: 'ys_req_battled' }
        : phase ? { type: 'ys_req_phase', fields: { WHO: phase[1] || 'ANY', WHEN: phase[2] } }
        : word === 'lp_at_most' ? { type: 'ys_req_lp_at_most', fields: { N: Number(text(cond).match(/\d+/)[0]) } }
        : word === 'lp_lower' ? { type: 'ys_req_lp_lower' }
        : { type: 'ys_req_controls', inputs: { SEL: { block: selectorState(rule(cond, 'selector')) } } };
    };
    // "if a and b and c" -> a and (b and c)
    const nest = (list) => (list.length === 1 ? list[0] : { type: 'ys_req_and', inputs: { A: { block: list[0] }, B: { block: nest(list.slice(1)) } } });
    state.inputs.REQ = { block: nest(rules(req, 'condName').map(condState)) };
  }
  const cost = rule(body, 'costClause');
  if (cost) {
    const inner = cost.c.find(isNode);
    if (inner.r === 'selfCost') state.inputs.COST = { block: { type: 'ys_cost_self', fields: { WHAT: text(inner) } } };
    else if (inner.r === 'pickCost')
      state.inputs.COST = { block: { type: 'ys_cost_pick', fields: { SELF: String(inner.c[0]), N: Number(words(inner).find(isNumber) ?? 1) },
        inputs: { SEL: { block: selectorState(rule(inner, 'selector')) } } } };
    else {
      const known = COSTS.find((c) => c.rule === inner.r);
      if (!known) throw new Unsupported('no block for the cost ' + text(inner) + ' yet');
      const selector = rule(inner, 'selector');
      state.inputs.COST = { block: { type: 'ys_cost_' + known.word, fields: { N: Number(words(inner).find(isNumber)) },
        inputs: selector ? { SEL: { block: selectorState(selector) } } : {} } };
    }
  }
  const actions = rules(rule(body, 'chain'), 'action').map(actionState);
  if (actions.length) state.inputs.DO = { block: chain(actions) };
  return state;
}

function lockState(statement) {
  const fields = { WHO: 'both', WHERE: 'AUTO', ONLY: 'ALL' };
  for (const [field] of LOCK_WHAT) fields[field] = false;
  for (const arg of rules(statement, 'lockArg')) {
    const word = text(arg);
    const what = LOCK_WHAT.find(([, w]) => w === word);
    if (what) fields[what[0]] = true;
    else if (LOCK_WHO.some(([, w]) => w === word)) fields.WHO = word;
    else if (LOCK_WHERE.some(([, w]) => w === word)) fields.WHERE = word;
    else if (LOCK_ONLY.some(([, w]) => w === word)) { fields.ONLY = word; fields.MONSTER = true; }
    else throw new Unsupported('no block for the lock word ' + word);
  }
  return { type: 'ys_lock', fields };
}

function handLimitState(statement) {
  const fields = { N: Number(words(statement).find(isNumber)), WHO: 'opponent', WHERE: 'AUTO' };
  for (const arg of rules(statement, 'lockArg')) {
    const word = text(arg);
    if (HAND_WHO.some(([, w]) => w === word)) fields.WHO = word;
    else if (LOCK_WHERE.some(([, w]) => w === word)) fields.WHERE = word;
    else throw new Unsupported('no block for the hand_limit word ' + word);
  }
  return { type: 'ys_hand_limit', fields };
}

function treeToState(tree) {
  // the continuous statements (lock / hand_limit / aura / protection / name_as) sit under 'continuous' nodes beside the effects
  const continuous = (r) => rules(tree, 'continuous').map((c) => rule(c, r)).filter((s) => s);
  // aura / protection / attack_directly may carry a while clause (on the 'continuous' node beside them)
  const withClause = (r, toState) => rules(tree, 'continuous').filter((c) => rule(c, r)).map((c) => withWhile(toState(rule(c, r)), c));
  const names = continuous('nameStatement').map(nameAsState)
    .concat(continuous('onlyOneStatement').map((s) => ({ type: 'ys_only_one', fields: { WHERE: words(s).map(String).includes('field') ? 'field' : 'own' } })))
    .concat(withClause('directStatement', () => ({ type: 'ys_attack_directly', fields: {} })).slice(0, 1))
    .concat(continuous('xyzAlsoStatement').map(xyzAlsoState).slice(0, 1));
  const locks = continuous('lockStatement').map(lockState);
  const hands = continuous('handLimitStatement').map(handLimitState);
  const auras = withClause('auraStatement', auraState);
  const protects = withClause('protectStatement', protectState);
  const blocks = treeEffects(tree);
  return locks.concat(hands, auras, protects, names, blocks.filter((b) => b));
}

function asState(as, trigger, name) {
    const target = as.c[2].replace(/^"|"$/g, '');
    const stat = (s) => { const p = rules(as, 'statPart').find((x) => x.c[0] === s); return p ? String(p.c[1]) : ''; };
    const when = trigger ? text(rule(trigger, 'triggerName')) : 'NONE';
    if (trigger && rule(trigger, 'summonConditions')) throw new Unsupported('no block for summon conditions on as(...) yet');
    if (!AS_WHEN.some((o) => o[1] === when)) throw new Unsupported('no block for the trigger ' + when + ' yet');
    return { type: 'ys_as', fields: { CARD: target, ATK: stat('atk'), DEF: stat('def'), EFFECT_ONLY: !!rule(as, 'effectOnly'), WHEN: when, NAME: name || '' } };
}

function treeEffects(tree) {
  const as = rule(tree, 'asScript');
  if (as) return [asState(as)];
  // the effects in script order; an 'or' token between two of them ticks "or the next effect" on the first
  const states = [];
  for (const c of tree.c || []) {
    if (c === 'or' && states.length) {
      const last = states[states.length - 1];
      if (last.type === 'ys_as') throw new Unsupported('no block for as(...) joined with or yet');
      last.fields.OR_NEXT = true;
    } else if (isNode(c) && c.r === 'effect') {
      const nameToken = (c.c || []).find((x) => typeof x === 'string' && x.startsWith('"'));
      states.push(effectState(rule(c, 'effectBody'), nameToken ? nameToken.replace(/^"|"$/g, '') : ''));
    } else if (isNode(c) && c.r === 'effectBody') states.push(effectState(c, ''));
  }
  return states;   // one hat each, laid out top to bottom by load()
}

// ---------------------------------------------------------------- the page and WolfX

let workspace;
let loading = false;
let lastSent = null;

function showNote(message) {
  const note = document.getElementById('note');
  note.textContent = message || '';
  note.style.display = message ? 'block' : 'none';
}

function send() {
  if (loading || locked) return;
  const script = generate(workspace);
  if (script === lastSent) return;
  lastSent = script;
  lastShown = null;   // the blocks changed here: the next script from WolfX must be drawn even if it matches the last one drawn
  if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage({ type: 'script', text: script });
}

function starter() {
  return [{ type: 'ys_activate', fields: { NAME: '', QUICK: false, ONCE: false, ONCE_COPY: false } }];
}

// Called by WolfX with {tree} / {error} / {empty}.
// Called by WolfX with {tree} / {error} / {empty}: the script beside the blocks changed (another card, or typing paused).
// A script that doesn't parse, or uses something there is no block for yet, leaves the blocks as they were and locks them (with a note),
// so they never overwrite what is being typed; the next script that works unlocks them.
function load(message) {
  let states;
  if (message.empty) states = starter();
  else if (message.error) {
    lock('The script has an error (' + message.error + '). The blocks show the last version that worked and are locked until the script is fixed.');
    return;
  } else {
    try { states = treeToState(message.tree); }
    catch (e) {
      if (!(e instanceof Unsupported)) throw e;
      lock('This script uses something there is no block for yet: ' + e.message + '. Edit it in the script; the blocks are locked so they do not replace it.');
      return;
    }
  }
  lock('');
  const shown = JSON.stringify(states);
  if (shown === lastShown) return;   // same blocks (only spacing or comments changed): leave the view alone
  lastShown = shown;

  const hadBlocks = workspace.getAllBlocks(false).length > 0;
  const view = { scale: workspace.scale };
  loading = true;
  Blockly.Events.disable();
  try {
    workspace.clear();
    let y = 20;
    for (const state of states) {
      const block = Blockly.serialization.blocks.append(Object.assign({ x: 20, y }, state), workspace);
      y += block.getHeightWidth().height + 30;
    }
    // what the blocks say now is what WolfX has: only a change made here is sent back
    lastSent = generate(workspace);
    fit(hadBlocks ? view.scale : 0.8);   // redrawn while typing: keep the zoom you chose
  } finally {
    Blockly.Events.enable();
    loading = false;
  }
}

let locked = false;
let lastShown = null;

function lock(note) {
  locked = !!note;
  showNote(note);
  if (workspace.setIsReadOnly) workspace.setIsReadOnly(locked);
}

// Blocks bigger than the view are zoomed out to fit it (not below 0.5); small ones keep the start size, top left.
function fit(scale) {
  const metrics = workspace.getMetricsManager();
  const content = metrics.getContentMetrics(), view = metrics.getViewMetrics();
  workspace.setScale(scale);
  if (content.width * scale > view.width || content.height * scale > view.height) {
    workspace.zoomToFit();
    if (workspace.scale < 0.5) workspace.setScale(0.5);
  }
  workspace.scroll(0, 0);
}

function setReadOnly(readOnly) {
  if (workspace.setIsReadOnly) workspace.setIsReadOnly(readOnly);
}

window.addEventListener('load', () => {
  defineBlocks();
  workspace = Blockly.inject('blocks', {
    toolbox: toolbox(),
    trashcan: true,
    zoom: { controls: true, wheel: true, startScale: 0.8, maxScale: 2.5, minScale: 0.4, scaleSpeed: 1.15 },
    grid: { spacing: 24, length: 3, colour: '#dde', snap: true },
    move: { scrollbars: true, drag: true, wheel: false },
    renderer: 'zelos',
    sounds: false,
  });
  workspace.addChangeListener((e) => { if (!e.isUiEvent) send(); });
  load({ empty: true });
  if (window.chrome && window.chrome.webview) window.chrome.webview.postMessage({ type: 'ready' });
});

window.wolf = { load, setReadOnly, generate: () => generate(workspace) };
