"""Builds effect_reference.json: for every game card that has an effect in the game's own tables, what the effect is called in EffectScript
(docs/EffectLanguage.md) when the language can express it.  WolfX's "Effect library" page reads this file.

  python build_effect_reference.py [output.json]      (default: Tools\\WolfX\\Content\\effect_reference.json)

Reads YuGiOh.exe (tables: effect rows, draw counts, life point table, filter rows, deck/graveyard list rows), the desktop Game Cards.json
(names and texts, made by WolfX) and archetypes.json (archetype names). All paths below are the author's.
"""
import json, os, re, struct, sys
import pefile

sys.stdout.reconfigure(encoding='utf-8')
EXE = r'C:\Users\Johnathon\source\repos\Yu-Gi-Oh-Ex\YuGiOh.exe'
if not os.path.exists(EXE):
    EXE = r'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOh.exe'
DESK = r'C:\Users\Johnathon\Desktop\New folder'
OUT = sys.argv[1] if len(sys.argv) > 1 else r'C:\Users\Johnathon\source\repos\Yu-Gi-Oh-Ex\Tools\WolfX\Content\effect_reference.json'

pe = pefile.PE(EXE, fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase


def rd(va, n):
    return pe.get_data(va - base, n)


game = json.load(open(os.path.join(DESK, 'Cards', 'Game Cards.json'), encoding='utf-8-sig'))
game = game['cards'] if isinstance(game, dict) else game
cards = {c['id']: c for c in game}
arch_names = {}
try:
    for e in json.load(open(os.path.join(DESK, 'archetypes.json'), encoding='utf-8-sig'))['archetypes']:
        arch_names[e['code']] = e['name']
except Exception as e:
    print('no archetype names', e)

# ---- tables -----------------------------------------------------------------------------------------------------------------------
EFFECT_TABLES = {'T5 spell/trap': (0x140BA8730, 2831), 'T2 monster': (0x140B5CC20, 3489), 'T4 grave/leave': (0x140B85A50, 2970), 'T1 continuous': (0x140B38290, 3123)}
rows = {}   # card id -> [(table, extra0, slots[5])]
for tname, (va, n) in EFFECT_TABLES.items():
    for i in range(n):
        raw = rd(va + i * 48, 48)
        cid, = struct.unpack_from('<H', raw, 0)
        extra = struct.unpack_from('<3H', raw, 2)
        slots = struct.unpack_from('<5Q', raw, 8)
        rows.setdefault(cid, []).append((tname, extra[0], slots))

draw_count = {}
for i in range(470):
    cid, cnt = struct.unpack('<Hh', rd(0x140B15110 + i * 4, 4))
    draw_count[cid] = cnt
lp_change = {}
prev = 0
for i in range(400):
    cid, amount = struct.unpack('<Hh', rd(0x140B15B30 + i * 4, 4))
    if cid < prev or cid == 0:
        break
    prev = cid
    lp_change[cid] = amount

filter_rows = {}   # id -> list of (param, extra, flags)
for i in range(1637):
    cid, param, extra, flags = struct.unpack('<HHII', rd(0x140B16220 + i * 12, 12))
    filter_rows.setdefault(cid, []).append((param, extra, flags))

list_rows = {}   # id -> list of (param(s16), scan fn, flags)
prev = 0
for i in range(5924):
    raw = rd(0x140AD2A80 + i * 24, 24)
    cid, param = struct.unpack_from('<Hh', raw, 0)
    fn, = struct.unpack_from('<Q', raw, 8)
    flags, = struct.unpack_from('<I', raw, 16)
    list_rows.setdefault(cid, []).append((param, fn, flags))

RACES = ['?', 'Dragon', 'Zombie', 'Fiend', 'Pyro', 'SeaSerpent', 'Rock', 'Machine', 'Fish', 'Dinosaur', 'Insect', 'Beast', 'BeastWarrior', 'Plant', 'Aqua', 'Warrior',
         'WingedBeast', 'Fairy', 'Spellcaster', 'Thunder', 'Reptile', 'Psychic', 'Wyrm', 'Cyberse', 'DivineBeast', 'CreatorGod']
ATTRS = ['?', 'Light', 'Dark', 'Water', 'Fire', 'Earth', 'Wind', 'Divine']

DECK_SCANS = {0x14052F2A0, 0x14052EE80}
GRAVE_OWN = {0x14052F4D0, 0x14052ED40}
GRAVE_OWN_SUMMON = {0x14052EC80}
GRAVE_OPP = {0x14052EE00}
GRAVE_BOTH = {0x140531D70}

H = {0x14015F9C0: 'draw', 0x14015E570: 'lp', 0x14016D5A0: 'search', 0x140169590: 'revive', 0x140178460: 'send', 0x140156E70: 'destroyAll', 0x140156D40: 'destroyTarget'}
TRIGGERS = {('T2 monster', 0): 'When Normal Summoned', ('T2 monster', 21): 'If Normal or Special Summoned', ('T2 monster', 5): 'If Summoned', ('T2 monster', 16): 'FLIP',
            ('T4 grave/leave', 0): 'When destroyed by battle and sent to the GY', ('T4 grave/leave', 5): 'If sent to the GY', ('T1 continuous', 1): 'Ignition (once per turn)'}


TRIGGER_TEXT = [
    ('normal_summoned', r'(?:When|If) this card is Normal Summoned'),
    ('special_summoned', r'(?:When|If) this card is Special Summoned'),
    ('summoned', r'(?:When|If) this card is Summoned'),
    ('normal_or_special_summoned', r'(?:When|If) this card is Normal or Special Summoned'),
    ('flip', r'FLIP'),
    ('destroyed_by_battle', r'(?:When|If) this card is destroyed by battle and sent to the (?:GY|Graveyard)'),
    ('sent_to_grave', r'(?:When|If) this card is sent to the (?:GY|Graveyard)'),
    ('sent_from_field_to_grave', r'(?:When|If) this card is sent from the field to the (?:GY|Graveyard)'),
    # Added 2026-10-01 with the phase / battle / ignition hooks (docs/EffectSystem.md sections 29-30).
    ('standby_phase', r'(?:Once per turn, )?[Dd]uring (?:each of )?your Standby Phases?'),
    ('end_phase', r'(?:Once per turn, )?[Dd]uring (?:the|each|your|each of your) End Phases?'),
    ('destroys_by_battle', r"(?:When|If) this card destroys (?:a|an opponent's|your opponent's) monster by battle(?: and sends it to the (?:GY|Graveyard))?"),
    ('battle_damage', r'(?:When|If) this card inflicts (?:battle damage|Battle Damage) to your opponent(?: by a direct attack)?'),
    ('attack_declared', r'(?:When|If) this card declares an attack'),
    ('ignition', r'Once per turn'),
]


def trigger_class(text):
    """The language trigger a monster's text starts with ("FLIP: ...", "When this card is Normal Summoned: ..."), or ''."""
    m = re.match(r'([^:]{0,90}):', text)
    if not m:
        return ''
    clause = m.group(1).strip()
    for name, rx in TRIGGER_TEXT:
        if re.fullmatch(rx, clause):
            return name
    return ''


# ---- whole-text trigger reading (2026-10-02) ---------------------------------------------------------------------------------------------------
# trigger_class only reads a text's first "X:" clause, so ~780 game cards were "unclassified": old texts write "When X, do Y" (no colon), say
# "Graveyard" / "Life Points" / "-Type", and many cards put the trigger in a later sentence. trigger_from_text reads every sentence (the one that
# names the row's action first), every head ("X:" and each "X, " prefix, longest first), and knows the wordings WolfX's CardTextTranslator
# (Triggers / LooseTriggers) reads. A LOOSE match is shown with a note and never becomes a trigger source (its engine wiring is narrower or
# different: a "destroyed" trigger does not fire when the card is only sent to the GY).
STRICT_TRIGGERS = [
    ('normal_or_special_summoned', r'(?:When|If) this card is Normal or Special Summoned'),
    ('normal_summoned', r'(?:When|If) this card is Normal Summoned'),
    ('special_summoned', r'(?:When|If) this card is Special Summoned'),
    ('summoned', r'(?:When|If) this card is Summoned'),
    ('flip', r'FLIP'),
    ('flip', r'(?:When|If) this card is flipped face-up'),
    ('destroyed_by_battle', r'(?:When|If) this card is destroyed by battle(?: and sent to the GY)?'),
    ('sent_from_field_to_grave', r'(?:When|If) this (?:face-up )?card (?:on the field )?is sent from the field to the GY'),
    ('sent_from_field_to_grave', r'(?:When|If) this face-up card (?:on the field )?is sent to the GY'),
    ('sent_to_grave', r'(?:When|If) this card is sent to the GY'),
    ('standby_phase', r'(?:Once per turn, )?[Dd]uring (?:each of )?your Standby Phases?'),
    ('end_phase', r'(?:Once per turn, )?[Dd]uring (?:the|each|your|each of your) End Phases?'),
    ('destroys_by_battle', r"(?:When|If) this card destroys (?:a|an opponent's|your opponent's) monster by battle(?: and sends it to the GY)?"),
    ('battle_damage', r"(?:When|If|Each time) this card inflicts battle damage to your opponent(?: by a direct attack)?"),
    ('attack_declared', r'(?:When|If) this card declares an attack'),
    ('ignition', r'(?:Once per turn|During your Main Phase|Once per turn, during your Main Phase)'),
]
LOOSE_TRIGGERS = [
    ('special_summoned', r'(?:When|If) this card is Special Summoned (?:from|by|because|while|during|in) .+', 'only when Special Summoned in this way'),
    ('normal_summoned', r'(?:When|If) this card is Normal Summoned (?:from|by|while|during) .+', 'only when Normal Summoned in this way'),
    ('normal_summoned', r'(?:When|If) this card is Tribute Summoned(?: .+)?', 'Tribute Summon read as Normal Summon'),
    ('summoned', r'(?:When|If) this card is (?:Normal or Flip|Flip|Normal Summoned or Flip) Summoned', 'Normal / Flip Summon only, not Special'),
    ('special_summoned', r'(?:When|If) this card is (?:Synchro|Xyz|Link|Fusion|Ritual|Pendulum) Summoned(?: .+)?', 'Synchro / Xyz / Link / Fusion / Ritual / Pendulum Summon read as any Special Summon'),
    ('sent_from_field_to_grave', r"(?:When|If) this card (?:on the field |in the Monster Zone )?is destroyed(?: by battle or card effect| by (?:a )?card effect| by an opponent's card(?: effect)?)?(?: and sent to the GY)?", 'destroyed, not only sent to the GY'),
    ('sent_to_grave', r'(?:When|If) this card is (?:discarded|sent)(?: directly)? from (?:your|the) hand to (?:your|the) GY(?: .+)?', 'only when sent from the hand'),
    ('sent_to_grave', r'(?:When|If) this card (?:on the field )?is sent to the GY (?:by|because|as|to|for|while|except) .+', 'only when sent to the GY in this way'),
    ('sent_to_grave', r'(?:When|If) this card is (?:discarded|Tributed)(?: .+)?', 'discarded / Tributed read as sent to the GY'),
]
# Words of each action (EffectScript name before the bracket), to find the sentence that belongs to the row.
ACTION_WORDS = {'draw': r'\bdraw', 'gain_lp': r'\b(?:gain|increase)\b', 'burn': r'\bdamage\b', 'search': r'\badd\b', 'revive': r'Special Summon',
                'send_to_grave': r'\bsend\b', 'destroy': r'\bdestroy'}


def normalise_text(text):
    """Old card wording -> the wording of modern texts (and of CardTextTranslator)."""
    t = re.sub(r'\bGraveyard\b', 'GY', text)
    t = re.sub(r'(\w)-Type\b', r'\1', t)
    t = re.sub(r'\bBattle Damage\b', 'battle damage', t)
    t = re.sub(r"to your opponent's Life Points\b", 'to your opponent', t)
    return re.sub(r'\bLife Points\b', 'LP', t)


# After the head, text the trigger alone does not cover: a condition ("if you control ...", "or when ...") or a cost written in the effect ("you
# can send 1 "Iron Chain" monster you control to the GY to inflict ..."; the game checks it inside the card's own handlers, not in slot 3).
REST_CONDITION = r'(?:if|while|or|and|unless|except|when|during|at)\b'
REST_COST = r"you can (?:send|discard|Tribute|tribute|banish|remove|pay|return|reveal|detach|shuffle|offer|change) .+? (?:to|and) "


def trigger_from_text(text, action):
    """-> (trigger, loose note or None) from the sentence that names the row's action (any sentence when none does), or (None, None)."""
    sentences = re.split(r'(?<=\.) (?=[A-Z"(])', normalise_text(text))
    words = ACTION_WORDS.get(action)
    hit = [s for s in sentences if words and re.search(words, s)]
    for s in hit or sentences:
        heads = [(s.split(':', 1)[0], s.split(':', 1)[1])] if ':' in s else []
        heads += [(s[:m.start()], s[m.end():]) for m in reversed(list(re.finditer(', ', s)))]
        for head, rest in heads:
            head = re.sub(r'^\(Quick Effect\)\s*', '', head.strip())
            head = re.sub(r'\s*\(except during the Damage Step\)$', '', head)
            rest = rest.strip()
            extra = ('condition not read: ' + rest.split(':')[0].split(';')[0][:80] if re.match(REST_CONDITION, rest, re.I)
                     else 'cost in the text not read' if re.match(REST_COST, rest) else None)
            for h in (head, re.sub(r'^Once per turn, ', '', head)):
                for name, rx in STRICT_TRIGGERS:
                    if re.fullmatch(rx, h):
                        if name == 'ignition' and extra and extra.startswith('condition'):
                            break   # "Once per turn, when/if X: ..." is a conditional trigger, not an ignition effect
                        return name, extra
                for name, rx, note in LOOSE_TRIGGERS:
                    if re.fullmatch(rx, h):
                        return name, note + ('; ' + extra if extra else '')
    return None, None


# Costs are slot 3 of a row, one shared function per cost (docs/EffectSystem.md sections 34, 37, 38). Amounts are per-card ladders in the game,
# so they are read from the text. 0x1401FF020 (send this card from the hand to the GY) has no EffectScript word yet.
COST_SLOTS = {0x1401FA670: 'banish_self', 0x1401FCEB0: 'discard_self', 0x1401F8BA0: 'tribute_self', 0x1401FDDF0: 'tribute(1)',
              0x1401F7FD0: 'discard', 0x1401F7CC0: 'pay_lp', 0x1401FF4D0: 'detach'}


def cost_of(slots, text):
    """-> EffectScript cost ('discard(1)', 'banish_self', ...), '' when the row has no known cost, or None when the amount is not in the text."""
    cost = COST_SLOTS.get(slots[3])
    if cost in (None, 'banish_self', 'discard_self', 'tribute_self', 'tribute(1)'):
        return cost or ''
    t = normalise_text(text)
    num = r'(\d+|1|a|an|one|two)'
    rx = {'discard': r'\bdiscard ' + num + r' (?:[\w-]+ )?cards?', 'pay_lp': r'\bpay (\d+) LP', 'detach': r'\bdetach ' + num + r' (?:Xyz )?materials?'}[cost]
    m = re.search(rx, t, re.I)
    if not m:
        return None
    n = {'a': '1', 'an': '1', 'one': '1', 'two': '2'}.get(m.group(1).lower(), m.group(1))
    return '%s(%s)' % (cost, n)


def name_of(cid):
    c = cards.get(cid)
    return c['name'] if c else str(cid)


def selector_from_list_row(param, flags):
    """-> (selector text, notes)"""
    notes = []
    kinds = []
    if flags & 2: kinds.append('monster')
    if flags & 4: kinds.append('spell')
    if flags & 8: kinds.append('trap')
    conds = []
    race = (flags >> 27) & 31
    if race:
        conds.append('race = ' + RACES[race] if race < len(RACES) else 'race = %d' % race)
    attr = (flags >> 24) & 7
    if attr:
        conds.append('attribute = ' + ATTRS[attr])
    if flags & 0x3C0000:
        lv = (flags >> 18) & 15
        op = '>=' if flags & 0x400000 else '<=' if flags & 0x800000 else '='
        conds.append('level %s %d' % (op, lv))
    if param >= 3000:
        conds.append('name = %d' % param)
        notes.append('name = the game card "%s"' % name_of(param))
    elif param > 0:
        conds.append('archetype = "%s"' % arch_names.get(param, param))
    elif param == -1:
        notes.append('the card must have the SAME NAME as this card (not expressible yet)')
    elif param < -1:
        notes.append('but NOT archetype "%s" (not expressible yet)' % arch_names.get(-param, -param))
    if flags & 0x2000: conds.append('atk <= 1500')
    if flags & 0x400: notes.append('normal-monster style check (not expressible yet)')
    if flags & 0x800: notes.append('must be Special Summonable')
    if flags & 0x4000 or flags & 0x8000: notes.append('kind-table flag (Tuner/Flip style, not expressible yet)')
    kind = kinds[0] if len(kinds) == 1 else 'card' if not kinds else None
    if kind is None:
        kind = 'card'
        notes.append('types: ' + '/'.join(kinds))
    if conds and kind == 'card' and (race or attr or flags & 0x3C0000):
        kind = 'monster'
    text = kind + (' where ' + ' and '.join(conds) if conds else '')
    return text, notes


def selector_from_filter_row(param, flags):
    side = {1: 'own', 2: 'opponent'}.get(flags & 3, 'any')
    top = flags >> 28
    notes = []
    cond = None
    if top == 3 and 0 < param < len(RACES): cond = 'race = ' + RACES[param]
    elif top == 4 and 0 < param < len(ATTRS): cond = 'attribute = ' + ATTRS[param]
    elif top == 5: cond = 'level <= %d' % param
    elif top == 6: cond = 'level >= %d' % param
    elif top == 1 and 0 < param < 3000: cond = 'archetype = "%s"' % arch_names.get(param, param)
    elif top != 0:
        return None
    zone = flags & 0x70
    kind = 'monster'
    if zone not in (0, 0x10):
        kind = 'spell'
        notes.append('targets the Spell & Trap zones (zone class 0x%x): the language only has "spell" for these' % zone)
        if cond:
            notes.append('condition ignored for Spell/Trap cards')
            cond = None
    if flags & 0x180:
        notes.append('also requires a position / face-up condition (not expressible yet)')
    return side, (kind + (' where ' + cond if cond else '')), notes


def describe_effect(cid, slots):
    """-> (script line, notes) or None"""
    action = H.get(slots[0])
    if not action:
        return None
    notes = []
    if action == 'draw':
        n = draw_count.get(cid)
        return ('draw(%d)' % n, notes) if n and n > 0 else None
    if action == 'lp':
        a = lp_change.get(cid)
        if a is None: return None
        return (('gain_lp(%d)' % a) if a > 0 else ('burn(%d)' % -a), notes)
    if action in ('search', 'revive', 'send'):
        lr = list_rows.get(cid)
        if not lr: return None
        param, fn, flags = lr[0]
        sel, n2 = selector_from_list_row(param, flags)
        notes += n2
        if action == 'search':
            if fn not in DECK_SCANS: return None
            return ('search(deck, %s)' % sel, notes)
        if action == 'send':
            if fn not in DECK_SCANS: return None
            return ('send_to_grave(deck, %s)' % sel, notes)
        zone = 'grave' if fn in GRAVE_OWN | GRAVE_OWN_SUMMON else 'opponent_grave' if fn in GRAVE_OPP else 'either_grave' if fn in GRAVE_BOTH else None
        if not zone: return None
        return ('revive(%s, %s)' % (zone, sel), notes)
    if action in ('destroyAll', 'destroyTarget'):
        fr = filter_rows.get(cid)
        if not fr: return None
        d = selector_from_filter_row(fr[0][0], fr[0][2])
        if not d: return None
        side, sel, n2 = d
        return ('destroy(%s, %s, %s)' % ('all' if action == 'destroyAll' else 'target', side, sel), notes + n2)
    return None


out = []
for cid in sorted(rows):
    c = cards.get(cid)
    if not c:
        continue
    described = []
    unknown = 0
    rows_slots = {}
    for tname, extra0, slots in rows[cid]:
        d = describe_effect(cid, slots)
        if d:
            described.append((tname, extra0, d))
            rows_slots.setdefault((cid, tname, extra0), slots)
        elif slots[0]:
            unknown += 1
    if not described:
        continue
    tname, extra0, (line, notes) = described[0]
    text = re.sub(r'\s+', ' ', (c.get('description') or '').replace('\r\n', ' ')).strip()
    tclass = '' if tname.startswith('T5') else trigger_class(text)
    first_clause = bool(tclass)   # read the old way (first "X:" clause): such sources stay first in trigger_sources.json
    # The row's Extra[0] is NOT the trigger (T2 extra 0 holds FLIP and Normal Summon cards alike; its low nibble is a once-per-turn class), so the
    # trigger comes from the card text.
    loose = None
    action_name = line.split('(')[0]
    if not tclass and not tname.startswith('T5'):
        tclass, loose = trigger_from_text(text, action_name)
        tclass = tclass or ''
        if loose:
            notes = notes + ['trigger read loosely: ' + loose]
    cost = cost_of(rows_slots[(cid, tname, extra0)], text) if not tname.startswith('T5') else ''
    # A row with a cost and no trigger in the text is an activated effect (ignition, or from the hand / GY): not unclassified.
    trigger = tclass or ('' if tname.startswith('T5') or cost else 'unclassified (%s)' % tname.split()[0])
    sentences = [s for s in re.split(r'(?<=\.) (?=[A-Z"])', text) if not s.startswith('You can only activate 1')]
    complete = unknown == 0 and len(described) == 1 and len(sentences) <= 1 and not notes
    # one effect only; filter notes do not matter, a clone replaces the filter. A loose trigger or a cost (slot 3 is part of the borrowed row, the
    # clone would pay it too) never makes a trigger source.
    source_ok = unknown == 0 and len(described) == 1 and len(sentences) <= 1 and not loose and not cost
    opt = ' once_per_turn;' if (extra0 & 0xF) in (5, 9) else ''   # the row's use-limit class (docs/EffectSystem.md section 20)
    if cost is None:
        notes = notes + ['a cost whose amount is not in the text (slot 3 %#x)' % rows_slots[(cid, tname, extra0)][3]]
    if tclass:
        script = 'on %s: %s;%s' % (tclass, line, opt)
        if cost:
            script += '\n// cost: ' + cost   # the language takes no cost after a trigger
    else:
        script = '%s%s;%s' % ('cost %s: ' % cost if cost else '', line, opt)
        if trigger:
            script = '// trigger: %s (not one of the language\'s triggers yet)\n%s' % (trigger, script)
    for n in notes:
        script += '\n// not covered by the script: ' + n
    if len(described) > 1:
        script += '\n// %d more effect(s) on this card were also recognised: %s' % (len(described) - 1, '; '.join(d[2][0] for d in described[1:]))
    out.append({'id': cid, 'name': c['name'], 'kind': c.get('kind', ''), 'icon': c.get('icon', ''), 'text': text, 'script': script, 'trigger': trigger,
                'complete': complete, 'sourceOk': source_ok, 'unread': unknown, 'notes': notes, 'table': tname.split()[0], 'cost': cost or '',
                'firstClause': first_clause, 'action': line.split('(')[0] + ('_' + line.split('(')[1].split(',')[0] if line.startswith('destroy') else '')})

# Which vanilla card a (trigger, action) pair borrows its behaviour from: the exact single-effect cards of each pair, shortest text first.
SOURCES_OUT = os.path.join(os.path.dirname(OUT), 'trigger_sources.json')
sources = {}
for e in out:
    # A list row with param -1 means "a card with this card's own name" (Poki Draco): the search machine reads that row itself, outside the
    # clone's deck override, and auto-picks the first candidate without showing the list (in-duel 2026-10-01). Never borrow from such a card.
    if any(r[0] == -1 for r in list_rows.get(e['id'], [])):
        continue
    if e['sourceOk'] and e['trigger'] and not e['trigger'].startswith('unclassified') and e['kind'] not in ('Spell', 'Trap'):
        sources.setdefault(e['trigger'], {}).setdefault(e['action'], []).append(e)
for t in sources:
    for a in sources[t]:
        sources[t][a] = [{'id': e['id'], 'name': e['name']} for e in sorted(sources[t][a], key=lambda e: (not e['firstClause'], len(e['text']), e['id']))[:5]]

# Hand-checked sources whose trigger is not the first clause of their text (trigger_class only reads that), docs/EffectSystem.md section 30:
# Solar Flare Dragon's End Phase burn sits behind its "cannot be attacked while you control another Pyro" sentence; plain LP row, one engine check.
# 'compose' entries are trigger-only sources: a composed effect (WolfX compiler, "actionFrom") uses only their row / event wiring, never their
# action slots, so a card whose own action is unusable (Edge Imp Chain searches its own name) still gives the trigger. Edge Imp Chain is in the
# engine's attack-declared list (EventIdList_AttackDeclared_Ev18).
# Botanical Girl ("sent from the field to the GY": search, confirmed in a duel through a custom card 2026-10-01, test 13) and Superheavy Samurai
# Drum ("if this card on the field is destroyed and sent to the GY": revive, leave-field table, test 13): their text starts with "When" / names a
# condition trigger_class does not read as this trigger.
MANUAL_SOURCES = {('end_phase', 'burn'): [5974], ('attack_declared', 'compose'): [11688],
                  ('sent_from_field_to_grave', 'search'): [7892], ('sent_from_field_to_grave', 'revive'): [11950]}
for (t, a), ids in MANUAL_SOURCES.items():
    entries = sources.setdefault(t, {}).setdefault(a, [])
    for cid in ids:
        if all(e['id'] != cid for e in entries):
            entries.append({'id': cid, 'name': name_of(cid)})

# Every game card's name -> Konami id, for EffectScript's as("Card name") (WolfX EffectScriptCompiler.CardIdByName).
NAMES_OUT = os.path.join(os.path.dirname(OUT), 'card_names.json')
json.dump({c['name']: c['id'] for c in game if c.get('name')}, open(NAMES_OUT, 'w', encoding='utf-8'), ensure_ascii=False)
json.dump({'note': 'Made by build_effect_reference.py: for each trigger and action, the game cards (exactly that one effect) a custom card can borrow from. First entry is used.', 'sources': sources},
          open(SOURCES_OUT, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
for t, acts in sources.items():
    print(t, {a: len(v) for a, v in acts.items()})

os.makedirs(os.path.dirname(OUT), exist_ok=True)
json.dump({'note': 'Made by docs/effect-scripts/build_effect_reference.py from the game tables. complete = the whole card text is this one effect.', 'cards': out},
          open(OUT, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print(len(out), 'cards with an effect the language can describe;', sum(1 for c in out if c['complete']), 'complete;', 'written to', OUT)
