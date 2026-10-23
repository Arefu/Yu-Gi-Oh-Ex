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
  ['the End Phase starts', 'end_phase'],
  ['this card destroys a monster by battle', 'destroys_by_battle'],
  ['this card inflicts battle damage', 'battle_damage'],
  ['this card declares an attack', 'attack_declared'],
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
  ['this card declares an attack', 'attack_declared'], ['you use it while this card is in your GY', 'in_grave']];
const PHASES = [['your Standby Phase', 'standby_phase'], ['the End Phase', 'end_phase']];
const dropdown = (name, options) => ({ type: 'field_dropdown', name, options });
const HATS = [
  { type: 'ys_on_summon', head: () => ['When this card is %1 from %2 by %3 as %4', [dropdown('TRIGGER', SUMMONED), dropdown('FROM', SUMMON_FROM),
      dropdown('BY', SUMMON_BY), dropdown('HOW', SUMMON_HOW)]],
    trigger: (b) => b.getFieldValue('TRIGGER'), has: (t) => SUMMONED.some((o) => o[1] === t),
    tooltip: 'A summon trigger. "from / by / as" narrow it: where the card came from, what summoned it and how (leave them on any).' },
  { type: 'ys_on_event', head: () => ['When %1', [dropdown('TRIGGER', EVENTS)]],
    trigger: (b) => b.getFieldValue('TRIGGER'), has: (t) => EVENTS.some((o) => o[1] === t),
    tooltip: 'A monster effect that happens when something happens to this card.' },
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
  { name: 'search', text: 'add from your Deck to your hand: %SEL', args: [{ kind: 'lit', value: 'deck' }, { kind: 'sel', field: 'SEL' }] },
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
    name: 'special_summon', text: 'Special Summon from %ZONE : %SEL',
    args: [{ kind: 'pick', rule: 'summonZone', field: 'ZONE',
             options: [['your hand', 'hand'], ['your Deck', 'deck'], ['your GY', 'grave'], ['(this card) your hand', 'self'], ['(this card) your GY', 'self_grave']] },
           { kind: 'sel', field: 'SEL' }],
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
  {
    name: 'add_to_hand', text: 'add to your hand from %FROM : %SEL',
    args: [{ kind: 'pick', token: true, field: 'FROM', options: [['your GY', 'grave'], ['your banished cards', 'banished']] }, { kind: 'sel', field: 'SEL' }],
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

// an action's block type: ys_<script word>, or ys_<block> when two grammar rules share the word (banish)
const blockType = (action) => 'ys_' + (action.block || action.name);

// rule = the grammar rule of the cost; num = it takes a number
const COSTS = [
  { rule: 'discardCost', word: 'discard', text: 'discard %N card(s)', num: true },
  { rule: 'payCost', word: 'pay_lp', text: 'pay %N Life Points', num: true, def: 1000 },
  { rule: 'tributeCost', word: 'tribute', text: 'Tribute %N monster(s)', num: true },
  { rule: 'detachCost', word: 'detach', text: 'detach %N material(s) from this card', num: true },
];

const SELF_COSTS = [['banish this card from your GY', 'banish_self'], ['discard this card from your hand', 'discard_self'], ['Tribute this card', 'tribute_self']];

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
      ['only once per turn %1', [{ type: 'field_checkbox', name: 'ONCE', checked: false }]],
    ];
    const def = { type: hat.type, colour: COLOUR.effect, tooltip: hat.tooltip };
    [head, ...rest].forEach(([msg, args], i) => { def['message' + i] = msg; def['args' + i] = args; });
    defs.push(def);
  }
  defs.push({
    type: 'ys_as',
    message0: 'act exactly like the game card %1', args0: [{ type: 'field_input', name: 'CARD', text: 'Pot of Greed' }],
    message1: 'with its own ATK %1 DEF %2 (optional)', args1: [{ type: 'field_input', name: 'ATK', text: '' }, { type: 'field_input', name: 'DEF', text: '' }],
    colour: COLOUR.as,
    tooltip: 'Borrow a game card\'s whole behaviour (name or Konami id). Put more effect hats beside it for a card with several effects; the runtime routes by table and event.',
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
    type: 'ys_hand_limit',
    message0: 'While this card is face-up in %1', args0: [{ type: 'field_dropdown', name: 'WHERE', options: LOCK_WHERE }],
    message1: '%1 hand size limit is %2', args1: [{ type: 'field_dropdown', name: 'WHO', options: HAND_WHO },
      { type: 'field_number', name: 'N', value: 3, min: 0, max: 60, precision: 1 }],
    colour: COLOUR.lock,
    tooltip: 'In the End Phase that player discards down to this many cards (normally 6). The lowest limit on the field wins. Not while this card\'s effects are negated (Script: hand_limit(...);).',
  });
  defs.push({ type: 'ys_req_no_monsters', message0: 'you control no monsters', output: 'Req', colour: COLOUR.when });
  defs.push({ type: 'ys_req_controls', message0: 'you control %1', args0: [{ type: 'input_value', name: 'SEL', check: 'Selector' }], output: 'Req', colour: COLOUR.when });
  for (const cost of COSTS) {
    const { msg, args } = message(cost.text, { N: { type: 'field_number', name: 'N', value: cost.def ?? 1, min: 1, precision: 1 } });
    defs.push({ type: 'ys_cost_' + cost.word, message0: msg, args0: args, output: 'Cost', colour: COLOUR.cost });
  }
  defs.push({ type: 'ys_cost_self', message0: '%1', args0: [{ type: 'field_dropdown', name: 'WHAT', options: SELF_COSTS }], output: 'Cost', colour: COLOUR.cost,
    tooltip: 'Paid with the card itself; this also says where the effect is used from (the GY, the hand, the field), so the effect needs no "when".' });
  for (const action of ACTIONS) {
    const fields = {};
    for (const arg of action.args)
      if (arg.field) fields[arg.field] = actionField(arg);
    const { msg, args } = message(action.text, fields);
    defs.push({ type: blockType(action), message0: msg, args0: args, previousStatement: 'Action', nextStatement: 'Action', colour: COLOUR.action,
      inputsInline: true, tooltip: 'Script: ' + action.name + '(...)' });
  }
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
      { kind: 'category', name: 'While face-up', colour: COLOUR.lock, contents: [block('ys_lock'), block('ys_hand_limit')] },
      { kind: 'category', name: 'Only if', colour: COLOUR.when, contents: [block('ys_req_no_monsters'), withSelector('ys_req_controls', 'monster')] },
      { kind: 'category', name: 'Cost', colour: COLOUR.cost, contents: [...COSTS.map((c) => block('ys_cost_' + c.word)), block('ys_cost_self')] },
      {
        kind: 'category', name: 'Actions', colour: COLOUR.action,
        contents: ACTIONS.map((a) => (a.args.some((arg) => arg.kind === 'sel')
          ? withSelector(blockType(a), a.name === 'destroy' || a.name === 'banish' || a.name === 'return_to_hand' || a.name === 'send_to_grave' ? 'card' : 'monster')
          : block(blockType(a)))),
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

function actionText(block) {
  const action = ACTIONS.find((a) => blockType(a) === block.type);
  if (!action) return '';
  const parts = [];
  for (const arg of action.args) {
    switch (arg.kind) {
      case 'lit': parts.push(arg.value); break;
      case 'num': parts.push(String(block.getFieldValue(arg.field))); break;
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
  const cost = COSTS.find((c) => 'ys_cost_' + c.word === block.type);
  return cost ? cost.word + '(' + block.getFieldValue('N') + ')' : '';
}

function reqText(block) {
  if (!block) return '';
  if (block.type === 'ys_req_no_monsters') return 'no_monsters';
  if (block.type === 'ys_req_controls') return 'controls(' + (selectorText(block.getInputTargetBlock('SEL')) || 'monster') + ')';
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
  return text;
}

function summonConditionsText(block, trigger) {
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
  const body = generateEffects(tops);
  return [lock, hand, body].filter((s) => s).join('\n');
}

function asText(as) {
    const card = String(as.getFieldValue('CARD') || '').trim();
    const stats = [['atk', as.getFieldValue('ATK')], ['def', as.getFieldValue('DEF')]].filter(([, v]) => isNumber(String(v || ''))).map(([s, v]) => s + ' ' + String(v).trim());
    return 'as(' + (isNumber(card) ? card : quote(card)) + ')' + (stats.length ? ' with ' + stats.join(', ') : '') + ';';
}

function generateEffects(tops) {
  // the hats in reading order: top to bottom, then left to right (getTopBlocks(true) sorts that way)
  const effects = tops.filter((b) => b.type === 'ys_as' || (hatOf(b) && effectBody(b)));
  if (effects.length === 0) return '';
  const body = (e) => e.type === 'ys_as' ? asText(e) : effectBody(e);
  if (effects.some((e) => hatOf(e) && String(e.getFieldValue('NAME') || '').trim()))
    return effects.map((e) => {
      const name = hatOf(e) ? String(e.getFieldValue('NAME') || '').trim() : '';
      return 'effect ' + (name ? quote(name) + ' ' : '') + '{\n    ' + body(e) + '\n}';
    }).join('\n');
  return effects.map(body).join('\nalso ');
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

function actionState(actionNode) {
  const inner = actionNode.c[0];
  const name = inner.c[0];
  const action = ACTIONS.find((a) => a.name === name && (!a.rule || a.rule === inner.r));
  if (!action) throw new Unsupported('the block editor has no block for ' + name + '(...) yet');
  const items = inner.c.slice(1).filter((c) => !['(', ')', ','].includes(c));
  const state = { type: blockType(action), fields: {}, inputs: {} };
  let i = 0;
  for (const arg of action.args) {
    const item = items[i];
    switch (arg.kind) {
      case 'lit': if (item === arg.value) i++; break;
      case 'num': if (typeof item === 'string' && isNumber(item)) { state.fields[arg.field] = Number(item); i++; } break;
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
  if (source) return asState(source);
  const trigger = rule(body, 'trigger');
  const t = trigger ? text(rule(trigger, 'triggerName')) : 'NONE';
  const hat = HATS.find((h) => h.has(t));
  if (!hat) throw new Unsupported('no block for the trigger ' + t + ' yet');
  const state = { type: hat.type, fields: { NAME: name || '', QUICK: !!rule(body, 'quickMark'), ONCE: !!rule(body, 'limit') }, inputs: {} };
  if (t !== 'NONE' && t !== 'ignition') state.fields.TRIGGER = t;
  const conditions = trigger && rule(trigger, 'summonConditions');
  if (conditions) {
    if (hat.type !== 'ys_on_summon') throw new Unsupported('summon conditions only go with a summon trigger');
    Object.assign(state.fields, summonConditionsState(conditions));
  }
  const req = rule(body, 'requireClause');
  if (req) {
    const cond = rule(req, 'condName');
    state.inputs.REQ = { block: cond.c[0] === 'no_monsters' ? { type: 'ys_req_no_monsters' } : { type: 'ys_req_controls', inputs: { SEL: { block: selectorState(rule(cond, 'selector')) } } } };
  }
  const cost = rule(body, 'costClause');
  if (cost) {
    const inner = cost.c.find(isNode);
    if (inner.r === 'selfCost') state.inputs.COST = { block: { type: 'ys_cost_self', fields: { WHAT: text(inner) } } };
    else {
      const known = COSTS.find((c) => c.rule === inner.r);
      if (!known) throw new Unsupported('no block for the cost ' + text(inner) + ' yet');
      state.inputs.COST = { block: { type: 'ys_cost_' + known.word, fields: { N: Number(words(inner).find(isNumber)) } } };
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
  const locks = rules(tree, 'lockStatement').map(lockState);
  const hands = rules(tree, 'handLimitStatement').map(handLimitState);
  const blocks = treeEffects(tree);
  return locks.concat(hands, blocks.filter((b) => b));
}

function asState(as) {
    const target = as.c[2].replace(/^"|"$/g, '');
    const stat = (s) => { const p = rules(as, 'statPart').find((x) => x.c[0] === s); return p ? String(p.c[1]) : ''; };
    return { type: 'ys_as', fields: { CARD: target, ATK: stat('atk'), DEF: stat('def') } };
}

function treeEffects(tree) {
  const as = rule(tree, 'asScript');
  if (as) return [asState(as)];
  const effects = rules(tree, 'effect').map((e) => {
    const nameToken = (e.c || []).find((c) => typeof c === 'string' && c.startsWith('"'));
    return effectState(rule(e, 'effectBody'), nameToken ? nameToken.replace(/^"|"$/g, '') : '');
  });
  const bodies = rules(tree, 'effectBody').map((b) => effectState(b, ''));
  return effects.concat(bodies);   // one hat each, laid out top to bottom by load()
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
  return [{ type: 'ys_activate', fields: { NAME: '', QUICK: false, ONCE: false } }];
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
