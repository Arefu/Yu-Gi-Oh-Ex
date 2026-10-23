"""
Sorts the Cards mod's cards (Mods/Cards/Yu-Gi-Oh-Ex/cards.json) by what they need before they play right in a duel, into two lists:

  docs/Card Lists/Cards That Work.csv       nothing to add: the game plays them as they are
  docs/Card Lists/Cards That Need Work.csv  an effect, a summoning rule, a kind the game lacks, or a test

    python card_status.py <Mods/Cards/Yu-Gi-Oh-Ex/cards.json> "<docs/Card Lists>"

What the engine does on its own (docs/EffectSystem.md 25, 36, 41; docs/CustomCards.md):
  - a monster without an effect (Normal, Normal Tuner, a token) needs nothing;
  - Xyz / Synchro: a card the game's tables don't name gets the generic rule: an Xyz needs 2 monsters of its Rank as Level, a Synchro
    1 Tuner + 1 or more non-Tuners of the right total Level. Right when the card's material line says exactly that;
  - Fusion needs a recipe ("fusion" in cards.json; WolfX's Effects tab writes it from the material line), and the Cards mod has none yet;
  - Link materials and Ritual summons go through tables Yu-Gi-Oh-Effects fills (Link editor, Ritual table): built, not yet tested;
  - every effect (Effect monsters, Pendulum effects, Spells, Traps) needs effect work: effectClone / BOB (docs/EffectSystem.md).
Run it again after cards gain effects or recipes: a card with "effectClone" / "effectScript" / "fusion" moves to "needs testing".
"""
import csv
import json
import re
import sys
from pathlib import Path

EXACT_KINDS = {('Ritual', 'Pendulum'): 'Ritual Pendulum', ('Ritual', 'Tuner'): 'Ritual Tuner'}


def materials(card):
    """The material line of an Extra Deck monster (its text's first line)."""
    return (card.get('description') or '').split('\r\n')[0].strip()


def status(card):
    """(works, needs, why): needs is '' when the card works."""
    kind = card.get('kind', '')
    typeline = card.get('typeline', [])
    tags = set(typeline[1:])
    has_effect = 'Effect' in tags or 'Effect' in kind or kind in ('Spell', 'Trap')
    has_script = bool(card.get('effectClone') or card.get('effectScript'))
    lacking = [name for pair, name in EXACT_KINDS.items() if set(pair) <= tags]
    if kind == 'Token' and 'Tuner' in tags:
        lacking.append('Tuner Token')

    needs, why = [], []
    if kind not in ('Spell', 'Trap') and (not typeline or not typeline[0] or not card.get('type')):   # (Level 0 is real: the Rank 0 "Number F0"s)
        needs.append('data')
        why.append('YGOPRODeck has no Type / Level / stats for it yet (a brand-new card): fill them in')
    if lacking:
        needs.append('kind')
        why.append(f'the game has no {" / ".join(lacking)} kind yet (made as {kind})')
    if has_effect:
        if has_script:
            needs.append('testing')
            why.append('has an effect (effectClone / effectScript): test it in a duel')
        else:
            needs.append('effect')
            what = 'its Spell effect' if kind == 'Spell' else 'its Trap effect' if kind == 'Trap' else \
                'its Pendulum and monster effects' if 'Pendulum' in tags else 'its effect'
            why.append(f'{what}: none written yet')
    elif 'Pendulum' in tags and '[Pendulum Effect]' in (card.get('description') or '') and \
            card['description'].split('[Pendulum Effect]', 1)[1].strip():
        needs.append('effect')
        why.append('its Pendulum effect: none written yet')

    line = materials(card)
    if 'Fusion' in tags:
        if card.get('fusion'):
            needs.append('testing')
            why.append('has a Fusion recipe: test it')
        else:
            needs.append('summoning')
            why.append(f'needs a Fusion recipe ("fusion"), from: {line}')
    elif 'Xyz' in tags or 'XYZ' in tags:
        if card.get('xyz'):
            needs.append('testing')
            why.append(f'Xyz materials from the text{" (looser: " + card["xyzLooser"] + ")" if card.get("xyzLooser") else ""}: test it')
        elif not re.fullmatch(r'2 Level \d+ monsters', line, re.I):
            needs.append('summoning')
            why.append(f'not the generic 2-material Xyz rule; needs an "xyz" row, from: {line}')
    elif 'Synchro' in tags:
        if card.get('synchro'):
            needs.append('testing')
            why.append(f'Synchro materials from the text{" (looser: " + card["synchroLooser"] + ")" if card.get("synchroLooser") else ""}: test it')
        elif not re.fullmatch(r'1 Tuner \+ 1\+ non-Tuner monsters', line, re.I):
            needs.append('summoning')
            why.append(f'not the generic Synchro rule; needs a "synchro" row, from: {line}')
    elif 'Link' in tags:
        needs.append('testing')
        why.append(f'Link materials (Link table, untested): {line}')
    elif 'Ritual' in tags:
        # every custom Ritual Monster gets a table row (Yu-Gi-Oh-Effects Ritual.cpp): its own spell ("ritualSpell"), else the generic ones
        needs.append('testing')
        why.append(f'Ritual Summon with Ritual Spell {card["ritualSpell"]}' if card.get('ritualSpell') else
                   'Ritual Summon with the generic Ritual Spells (no spell of its own named)')

    order = ['data', 'kind', 'summoning', 'effect', 'testing']
    needs = sorted(set(needs), key=order.index)
    return '; '.join(needs), ' | '.join(why)


def main():
    cards = json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))['cards']
    out = Path(sys.argv[2])
    columns = ['id', 'password', 'name', 'kind', 'typeline']
    work, need = [], []
    for card in cards:
        row = [card['id'], card.get('password', ''), card['name'], card.get('kind', ''), '/'.join(card.get('typeline', []))]
        needs, why = status(card)
        text = (card.get('description') or '').replace('\r\n', ' ').replace('\n', ' ')
        (need if needs else work).append(row + ([needs, why, text] if needs else []))

    with (out / 'Cards That Work.csv').open('w', encoding='utf-8', newline='') as f:
        w = csv.writer(f)
        w.writerow(columns)
        w.writerows(work)
    with (out / 'Cards That Need Work.csv').open('w', encoding='utf-8', newline='') as f:
        w = csv.writer(f)
        w.writerow(columns + ['needs', 'why', 'text'])
        w.writerows(sorted(need, key=lambda r: (r[5], r[3], r[2])))

    print(f'Work today: {len(work)}')
    counts = {}
    for row in work:
        counts[row[3]] = counts.get(row[3], 0) + 1
    for kind, n in sorted(counts.items(), key=lambda x: -x[1]):
        print(f'  {kind}: {n}')
    print(f'Need work: {len(need)}')
    counts = {}
    for row in need:
        for part in row[5].split('; '):
            counts[part] = counts.get(part, 0) + 1
    for part, n in sorted(counts.items(), key=lambda x: -x[1]):
        print(f'  {part}: {n}')


if __name__ == '__main__':
    main()
