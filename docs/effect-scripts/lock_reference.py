"""Curated continuous locks, which live outside the game's effect tables.

Only Spell Canceller's entire effect is expressed by the current lock language.
Keep the other cards partial: cloning a lock does not reproduce their exceptions,
costs, or additional effects. See EffectSystem.md section 44.
"""

LOCKS = [
    (5594, 'Spell Canceller', 'Effect', 'lock(spell);',
     'Spell Cards, and their effects on the field, cannot be activated. Negate all Spell effects on the field.', []),
    (4758, 'Jinzo', 'Effect', 'lock(trap);',
     'Trap Cards, and their effects on the field, cannot be activated. Negate all Trap effects on the field.',
     ['Amplifier can make the original Jinzo lock affect only the opponent; this template locks both players.']),
    (4866, 'Royal Decree', 'Trap', 'lock(trap, spelltrap_zone);',
     'Negate all other Trap effects on the field.',
     ['The original uses an activation-order threshold and excludes itself; this template locks all Traps, including activation.']),
    (5740, 'Skill Drain', 'Trap', 'lock(monster, spelltrap_zone);',
     'Activate this card by paying 1000 LP. Negate the effects of all face-up monsters while they are face-up on the field (but their effects can still be activated).',
     ['The 1000 LP activation cost and original activation-order threshold are not reproduced.']),
    (6166, 'Silent Swordsman LV7', 'Effect', 'lock(spell);',
     'Cannot be Normal Summoned/Set. Must be Special Summoned by "Silent Swordsman LV5". Negate all Spell effects on the field.',
     ['Summoning restrictions are not expressed; this template also prevents Spell activation.']),
]


def add_lock_references(entries, cards):
    """Replace curated ids once, preserving all other generated entries."""
    ids = {row[0] for row in LOCKS}
    entries[:] = [entry for entry in entries if entry['id'] not in ids]
    for cid, name, kind, script, text, notes in LOCKS:
        card = cards.get(cid, {})
        entries.append({
            'id': cid, 'name': card.get('name', name), 'kind': card.get('kind', kind),
            'icon': card.get('icon', ''), 'text': card.get('description') or text,
            'script': script + ''.join('\n// not covered by the script: ' + note for note in notes),
            'trigger': '', 'complete': not notes, 'sourceOk': False, 'unread': 0,
            'notes': notes, 'table': 'hardcoded lock', 'cost': '', 'firstClause': False,
            'action': 'lock',
        })
    entries.sort(key=lambda entry: entry['id'])
