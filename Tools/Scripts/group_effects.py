"""
Groups the effects still to write (docs/Card Lists/Effects To Write.csv, from WolfX --attach-effects) by what they do, in the game's own
effect categories (CARD_Genre, the names WolfX's Effect library groups by under "What it does"), read from each card's text.

    python group_effects.py <Mods/Cards/Yu-Gi-Oh-Ex/cards.json> "<docs/Card Lists>"

Effects To Write.csv gains two columns and is sorted by them: "group" (the card's main category, the first rule below its text matches:
one place per card) and "genres" (every category its text touches). Cards the translator already ran part of come first in a group.
The categories are guesses from the wording; they don't change the cards.
"""
import csv
import json
import re
import sys
from collections import Counter
from pathlib import Path

# (category, pattern) in priority order: the first match is the card's group. Patterns read the English card text.
RULES = [
    ('Negate effect', r'\bnegate\b'),
    ('Search Deck', r'add [^.;]*? from your Deck to your hand|add [^.;]*?from your Deck or GY to your hand'),
    ('Help Draw', r'\bdraws? \d+ cards?\b|\bdraw \d+\b|\bdraws? (?:a|1) card\b'),
    ('Recover from Graveyard', r'(?:add|special summon|return)[^.;]*? from (?:your|either) (?:GY|Graveyard)|from your (?:GY|Graveyard)[^.;]*?to your hand'),
    ('Special Summon', r'\bspecial summon\b'),
    ('Destroy Monster', r'destroy[^.;]*?\bmonsters?\b'),
    ('Destroy Spell Card', r'destroy[^.;]*?\b(?:spell|trap|spell/trap)s?\b|destroy[^.;]*?\bcards?\b'),
    ('Remove Card', r'\bbanish'),
    ('Return Card', r'return[^.;]*? to the (?:hand|Deck|Extra Deck)|shuffle[^.;]*? into the Deck'),
    ('Send to Graveyard', r'send[^.;]*? to the (?:GY|Graveyard)'),
    ('Destroy Hand', r'discard'),
    ('Set controls', r'take control|gain control'),
    ('Change battle position', r'change[^.;]*? battle position|to face-down Defense Position'),
    ('Increase/Decrease ATK/DEF', r'\b(?:gains?|loses?) [\d,]+ ATK|ATK/DEF become|ATK becomes|\b(?:gains?|loses?) ATK'),
    ('Damage LP', r'inflict[^.;]*? damage'),
    ('Recover LP', r'gain [\d,]+ (?:Life Points|LP)|gain LP'),
    ('Cannot be destroyed', r'cannot be destroyed'),
    ('Limit Attack', r'cannot (?:declare an )?attack'),
    ('Piercing', r'piercing'),
    ('Direct Attack', r'attack directly'),
    ('Attack multiple times', r'(?:second|two|2) attacks?|attack (?:twice|all)'),
    ('Token', r'\btoken\b'),
    ('Counter', r'\bcounters?\b'),
    ('Level Modifier', r'\bLevel (?:becomes|of)|increase its Level|reduce its Level'),
    ('Gamble', r'\b(?:toss a coin|roll a (?:six-sided )?die)\b'),
]


def genres(text):
    return [name for name, pattern in RULES if re.search(pattern, text, re.I)]


def main():
    cards = {str(c['id']): c for c in json.loads(Path(sys.argv[1]).read_text(encoding='utf-8'))['cards']}
    path = Path(sys.argv[2]) / 'Effects To Write.csv'
    with path.open(encoding='utf-8') as f:
        rows = list(csv.DictReader(f))
    for row in rows:
        text = (cards.get(row['id'], {}).get('description') or '').replace('\r\n', ' ')
        found = genres(text)
        row['group'] = found[0] if found else 'Other'
        row['genres'] = '; '.join(found)
        row['text'] = text
    order = {name: i for i, (name, _) in enumerate(RULES)}
    rows.sort(key=lambda r: (order.get(r['group'], len(order)), -int(r['effects read'] or 0), r['name']))
    columns = ['group', 'genres', 'id', 'name', 'kind', 'effects read', 'not run', 'why not run', 'script so far', 'text']
    with path.open('w', encoding='utf-8', newline='') as f:
        w = csv.DictWriter(f, fieldnames=columns, extrasaction='ignore')
        w.writeheader()
        w.writerows(rows)
    counts = Counter(r['group'] for r in rows)
    partial = Counter(r['group'] for r in rows if int(r['effects read'] or 0) > 0)
    for name, n in sorted(counts.items(), key=lambda x: order.get(x[0], len(order))):
        print(f'{name}: {n} ({partial[name]} part translated)')


if __name__ == '__main__':
    main()
