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
];

const SIDE = [['(default)', 'DEFAULT'], ['you control', 'own'], ['your opponent controls', 'opponent'], ['on either side', 'any']];

// text: what the block says, %NAME = one of its fields / inputs. args: the script's arguments in order.
//   lit: a fixed word in the script, no field     num: a number field      sel: a "which cards" input (optional in the script)
//   pick: a dropdown; rule = the grammar rule that holds the word, or token: true when it is a plain word; optional = may be left out
//   stat: "atk N" / "def N" (left out when blank)
const ACTIONS = [
  { name: 'draw', text: 'draw %N card(s)', args: [{ kind: 'num', field: 'N', def: 1, min: 1 }] },
  { name: 'gain_lp', text: 'gain %N Life Points', args: [{ kind: 'num', field: 'N', def: 500, min: 0 }] },
  { name: 'burn', text: 'inflict %N damage to your opponent', args: [{ kind: 'num', field: 'N', def: 500, min: 0 }] },
  { name: 'search', text: 'add from your Deck to your hand: %SEL', args: [{ kind: 'lit', value: 'deck' }, { kind: 'sel', field: 'SEL' }] },
  { name: 'send_to_grave', text: 'send from your Deck to the GY: %SEL', args: [{ kind: 'lit', value: 'deck' }, { kind: 'sel', field: 'SEL' }] },
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
    name: 'banish', text: 'banish %SCOPE card(s) %SIDE : %SEL',
    args: [{ kind: 'pick', rule: 'scope', field: 'SCOPE', options: [['1 chosen', 'target'], ['all', 'all']] },
           { kind: 'pick', rule: 'side', field: 'SIDE', optional: true, options: SIDE }, { kind: 'sel', field: 'SEL' }],
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
    // negate(what[, opponent][, property]); the other words (your_turn, battle_phase, targets_one, effect ...) are text-editor only
    name: 'negate', text: 'negate the activation of %WHAT %WHO %PROP , and if you do, destroy it',
    args: [{ kind: 'pick', rule: 'negateArg', field: 'WHAT', options: [['a Spell Card', 'spell'], ['a Trap Card', 'trap'], ['a monster effect', 'monster'], ['a Spell/Trap Card', 'spelltrap'], ['any card or effect', 'any']] },
           { kind: 'pick', rule: 'negateArg', field: 'WHO', optional: true, options: [['(by either player)', 'DEFAULT'], ['by your opponent', 'opponent']] },
           { kind: 'pick', rule: 'negateArg', field: 'PROP', optional: true, options: [['(any kind)', 'DEFAULT'], ['Normal', 'normal'], ['Counter', 'counter'], ['Field', 'field'], ['Equip', 'equip'], ['Continuous', 'continuous'], ['Quick-Play', 'quickplay'], ['Ritual', 'ritual']] }],
  },
  {
    name: 'equip', text: 'the equipped monster gains ATK %ATK DEF %DEF',
    args: [{ kind: 'stat', stat: 'atk', field: 'ATK' }, { kind: 'stat', stat: 'def', field: 'DEF' }],
  },
];

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

const COLOUR = { effect: 210, when: 290, cost: 20, action: 120, cards: 45, as: 330 };

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
  defs.push({
    type: 'ys_effect',
    message0: 'Effect %1', args0: [{ type: 'field_input', name: 'NAME', text: '' }],
    message1: 'when %1', args1: [{ type: 'field_dropdown', name: 'TRIGGER', options: TRIGGERS }],
    message2: 'only if %1', args2: [{ type: 'input_value', name: 'REQ', check: 'Req' }],
    message3: 'cost %1', args3: [{ type: 'input_value', name: 'COST', check: 'Cost' }],
    message4: 'do %1', args4: [{ type: 'input_statement', name: 'DO', check: 'Action' }],
    message5: 'only once per turn %1', args5: [{ type: 'field_checkbox', name: 'ONCE', checked: false }],
    previousStatement: 'Effect', nextStatement: 'Effect', colour: COLOUR.effect,
    tooltip: 'One effect of the card. The name is optional. Put actions in "do"; they run one after another (then). Stack more effects underneath for a card with several.',
  });
  defs.push({
    type: 'ys_as',
    message0: 'act exactly like the game card %1', args0: [{ type: 'field_input', name: 'CARD', text: 'Pot of Greed' }],
    message1: 'with its own ATK %1 DEF %2 (optional)', args1: [{ type: 'field_input', name: 'ATK', text: '' }, { type: 'field_input', name: 'DEF', text: '' }],
    colour: COLOUR.as,
    tooltip: 'The whole effect of a game card (its name or Konami id). In a duel the card plays as that card, so everything the game knows about it applies.',
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
    defs.push({ type: 'ys_' + action.name, message0: msg, args0: args, previousStatement: 'Action', nextStatement: 'Action', colour: COLOUR.action,
      inputsInline: true, tooltip: 'Script: ' + action.name + '(...)' });
  }
  defs.push({
    type: 'ys_selector',
    message0: 'a %1', args0: [{ type: 'field_dropdown', name: 'KIND', options: [['monster', 'monster'], ['Spell', 'spell'], ['Trap', 'trap'], ['card', 'card'], ['(anything)', 'NONE']] }],
    message1: 'where %1', args1: [{ type: 'input_statement', name: 'CONDS', check: 'Cond' }],
    output: 'Selector', colour: COLOUR.cards, tooltip: 'Which cards: a kind, and any number of conditions (all must be true).',
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
      { kind: 'category', name: 'Effect', colour: COLOUR.effect, contents: [block('ys_effect'), block('ys_as')] },
      { kind: 'category', name: 'Only if', colour: COLOUR.when, contents: [block('ys_req_no_monsters'), withSelector('ys_req_controls', 'monster')] },
      { kind: 'category', name: 'Cost', colour: COLOUR.cost, contents: [...COSTS.map((c) => block('ys_cost_' + c.word)), block('ys_cost_self')] },
      {
        kind: 'category', name: 'Actions', colour: COLOUR.action,
        contents: ACTIONS.map((a) => (a.args.some((arg) => arg.kind === 'sel') ? withSelector('ys_' + a.name, a.name === 'destroy' || a.name === 'banish' || a.name === 'return_to_hand' ? 'card' : 'monster') : block('ys_' + a.name))),
      },
      {
        kind: 'category', name: 'Which cards', colour: COLOUR.cards,
        contents: [block('ys_selector'), block('ys_cond_race'), block('ys_cond_attribute'), block('ys_cond_number'), block('ys_cond_archetype'), block('ys_cond_name'), block('ys_cond_raw')],
      },
    ],
  };
}

// ---------------------------------------------------------------- blocks -> script

const quote = (text) => '"' + text.replace(/"/g, '') + '"';
const isNumber = (text) => /^\d+$/.test(text.trim());

function selectorText(block) {
  if (!block) return '';
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
  if (kind === 'NONE') return where;
  return where ? kind + ' where ' + where : kind;
}

function actionText(block) {
  const action = ACTIONS.find((a) => 'ys_' + a.name === block.type);
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
  const trigger = block.getFieldValue('TRIGGER');
  if (trigger !== 'NONE') text += 'on ' + trigger + ': ';
  const req = reqText(block.getInputTargetBlock('REQ'));
  if (req) text += 'if ' + req + ': ';
  const cost = costText(block.getInputTargetBlock('COST'));
  if (cost) text += 'cost ' + cost + ': ';
  text += actions.join(' then ') + ';';
  if (block.getFieldValue('ONCE') === 'TRUE') text += ' once_per_turn;';
  return text;
}

function generate(workspace) {
  const tops = workspace.getTopBlocks(true);
  const as = tops.find((b) => b.type === 'ys_as');
  if (as) {
    const card = String(as.getFieldValue('CARD') || '').trim();
    const stats = [['atk', as.getFieldValue('ATK')], ['def', as.getFieldValue('DEF')]].filter(([, v]) => isNumber(String(v || ''))).map(([s, v]) => s + ' ' + String(v).trim());
    return 'as(' + (isNumber(card) ? card : quote(card)) + ')' + (stats.length ? ' with ' + stats.join(', ') : '') + ';';
  }
  const effects = [];
  for (const top of tops)
    for (let b = top; b; b = b.getNextBlock())
      if (b.type === 'ys_effect' && effectBody(b)) effects.push(b);
  if (effects.length === 0) return '';
  if (effects.some((e) => String(e.getFieldValue('NAME') || '').trim()))
    return effects.map((e) => {
      const name = String(e.getFieldValue('NAME') || '').trim();
      return 'effect ' + (name ? quote(name) + ' ' : '') + '{\n    ' + effectBody(e) + '\n}';
    }).join('\n');
  return effects.map(effectBody).join('\nalso ');
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
  const state = { type: 'ys_selector', fields: { KIND: kindNode ? text(kindNode) : 'NONE' } };
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

function chain(states) {
  for (let i = states.length - 2; i >= 0; i--) states[i].next = { block: states[i + 1] };
  return states[0];
}

function actionState(actionNode) {
  const inner = actionNode.c[0];
  const name = inner.c[0];
  const action = ACTIONS.find((a) => a.name === name);
  if (!action) throw new Unsupported('the block editor has no block for ' + name + '(...) yet');
  const items = inner.c.slice(1).filter((c) => !['(', ')', ','].includes(c));
  const state = { type: 'ys_' + action.name, fields: {}, inputs: {} };
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

function effectState(body, name) {
  const state = { type: 'ys_effect', fields: { NAME: name || '', TRIGGER: 'NONE', ONCE: !!rule(body, 'limit') }, inputs: {} };
  const trigger = rule(body, 'trigger');
  if (trigger) {
    const t = text(rule(trigger, 'triggerName'));
    if (!TRIGGERS.some((o) => o[1] === t)) throw new Unsupported('no block for the trigger ' + t + ' yet');
    state.fields.TRIGGER = t;
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

function treeToState(tree) {
  const as = rule(tree, 'asScript');
  if (as) {
    const target = as.c[2].replace(/^"|"$/g, '');
    const stat = (s) => { const p = rules(as, 'statPart').find((x) => x.c[0] === s); return p ? String(p.c[1]) : ''; };
    return [{ type: 'ys_as', fields: { CARD: target, ATK: stat('atk'), DEF: stat('def') } }];
  }
  const effects = rules(tree, 'effect').map((e) => {
    const nameToken = (e.c || []).find((c) => typeof c === 'string' && c.startsWith('"'));
    return effectState(rule(e, 'effectBody'), nameToken ? nameToken.replace(/^"|"$/g, '') : '');
  });
  const bodies = rules(tree, 'effectBody').map((b) => effectState(b, ''));
  return [chain(effects.concat(bodies))];
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
  return [{ type: 'ys_effect', fields: { NAME: '', TRIGGER: 'NONE', ONCE: false } }];
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
