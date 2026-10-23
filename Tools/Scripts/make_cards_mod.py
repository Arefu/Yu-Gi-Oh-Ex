"""
Builds the "Cards" mod: every card released after the game (docs/Card Lists/Missing Cards.csv) plus the new tokens named in card text, as
cards.json entries with art, from YGOPRODeck's card list. Stats, kind, text and art only: effects are added later.

    python make_cards_mod.py <ygoprodeck cardinfo.json (?misc=yes)> "<docs/Card Lists>" <mod folder>

Download the card list once by hand (https://db.ygoprodeck.com/api/v7/cardinfo.php?misc=yes). Art is downloaded once per card and kept,
so running it again only fetches what is new. Each card's id is kept in <mod>/card-ids.json (password or token name -> id), so ids never
move when the list grows: tokens from 21000, cards from 21100.

Art goes in Yu-Gi-Oh-Ex/art/<kind folder>/<password>.jpg (304 x 304 JPEG, the game's card art size), one folder per kind, so the
folders show what the mod holds. Tokens have no art of their own on YGOPRODeck (only printed token cards are listed, and the game has all
of those), so a token uses the art of the card that summons it, as Konami's own games do.
"""
import csv
import io
import json
import re
import sys
import time
import urllib.error
import urllib.request
from collections import Counter, defaultdict
from pathlib import Path

from PIL import Image

FIRST_TOKEN_ID = 21000   # the user's own custom cards sit at 15300-19999; Forbidden Memories uses 30000+
FIRST_CARD_ID = 21100
FIRST_NEW_ARCHETYPE = 1100   # past the game's archetype list (codes up to 1032); codes from 419 up are custom to the engine
ARCHETYPE_LIST = Path(__file__).resolve().parents[2] / 'File Type Libraries' / 'CARD_Named.bin' / 'Archetypes.json'

TOKEN = re.compile(r'"([^"]+? Tokens?)"\s*\(([^)]*?)\)', re.I)
TOKEN_STATS = re.compile(r'^(?P<types>.+?)/(?P<attr>LIGHT|DARK|WATER|FIRE|EARTH|WIND|DIVINE)/Level\s*(?P<level>\d+)/ATK\s*(?P<atk>\d+|\?)/DEF\s*(?P<def>\d+|\?)$', re.I)
CANNOT_NORMAL_SUMMON = re.compile(r'Cannot be Normal Summoned', re.I)

# cards.json kind (Yu-Gi-Oh-Cards KindNames) -> art folder
FOLDERS = {
    'Token': 'Tokens', 'Normal': 'Normal', 'Effect': 'Effect', 'EffectSpecialSummonOnly': 'Effect (Special Summon only)',
    'FlipEffect': 'Flip', 'Toon': 'Toon', 'Spirit': 'Spirit', 'SpiritSpecialSummonOnly': 'Spirit (Special Summon only)',
    'Union': 'Union', 'Gemini': 'Gemini', 'TunerNormal': 'Tuner Normal', 'TunerEffect': 'Tuner Effect',
    'TunerEffectSpecialSummonOnly': 'Tuner Effect (Special Summon only)', 'TunerFlipEffect': 'Tuner Flip', 'UnionTunerEffect': 'Tuner Union',
    'Ritual': 'Ritual', 'RitualEffect': 'Ritual Effect', 'RitualSpiritEffect': 'Ritual Spirit',
    'Fusion': 'Fusion', 'FusionEffect': 'Fusion Effect', 'FusionTuner': 'Fusion Tuner',
    'Synchro': 'Synchro', 'SynchroEffect': 'Synchro Effect', 'SynchroTunerEffect': 'Synchro Tuner Effect',
    'Xyz': 'Xyz', 'XyzEffect': 'Xyz Effect', 'Link': 'Link', 'LinkEffect': 'Link Effect',
    'Pendulum': 'Pendulum Normal', 'PendulumEffect': 'Pendulum Effect', 'PendulumEffectSpecialSummonOnly': 'Pendulum Effect (Special Summon only)',
    'PendulumTunerNormal': 'Pendulum Tuner Normal', 'PendulumTunerEffect': 'Pendulum Tuner Effect', 'PendulumFlipEffect': 'Pendulum Flip',
    'PendulumSpiritEffect': 'Pendulum Spirit', 'FusionPendulumEffect': 'Pendulum Fusion', 'SynchroPendulumEffect': 'Pendulum Synchro',
    'XyzPendulumEffect': 'Pendulum Xyz', 'Spell': 'Spell', 'Trap': 'Trap',
}


def norm(text):
    return re.sub(r'[^a-z0-9]', '', text.lower())


def type_name(race):
    return ''.join(word.capitalize() for word in re.split(r'[\s-]+', race))


def kind_of(card, notes):
    """The game's kind for a YGOPRODeck monster (its typeline after the monster type: Fusion, Tuner, Effect, ...)."""
    tags = set(card.get('typeline', [])[1:])
    effect = 'Effect' in tags
    special_only = bool(CANNOT_NORMAL_SUMMON.search(card.get('desc', '')))
    if 'Pendulum' in tags:
        if 'Fusion' in tags: return 'FusionPendulumEffect'
        if 'Synchro' in tags: return 'SynchroPendulumEffect'
        if 'Xyz' in tags: return 'XyzPendulumEffect'
        if 'Ritual' in tags:
            notes['Ritual Pendulum made as Ritual Effect (the game has no Ritual Pendulum kind)'].append(card['name'])
            return 'RitualEffect'
        if 'Tuner' in tags: return 'PendulumTunerEffect' if effect else 'PendulumTunerNormal'
        if 'Flip' in tags: return 'PendulumFlipEffect'
        if 'Spirit' in tags: return 'PendulumSpiritEffect'
        if not effect: return 'Pendulum'
        return 'PendulumEffectSpecialSummonOnly' if special_only else 'PendulumEffect'
    if 'Link' in tags: return 'LinkEffect' if effect else 'Link'
    if 'Xyz' in tags: return 'XyzEffect' if effect else 'Xyz'
    if 'Synchro' in tags:
        if 'Tuner' in tags: return 'SynchroTunerEffect'
        return 'SynchroEffect' if effect else 'Synchro'
    if 'Fusion' in tags:
        if 'Tuner' in tags: return 'FusionTuner'
        return 'FusionEffect' if effect else 'Fusion'
    if 'Ritual' in tags:
        if 'Spirit' in tags: return 'RitualSpiritEffect'
        if 'Tuner' in tags:
            notes['Ritual Tuner made as Ritual Effect (the game has no Ritual Tuner kind)'].append(card['name'])
        return 'RitualEffect' if effect else 'Ritual'
    if 'Tuner' in tags:
        if 'Normal' in tags: return 'TunerNormal'
        if 'Flip' in tags: return 'TunerFlipEffect'
        if 'Union' in tags: return 'UnionTunerEffect'
        return 'TunerEffectSpecialSummonOnly' if special_only else 'TunerEffect'
    if 'Flip' in tags: return 'FlipEffect'
    if 'Toon' in tags: return 'Toon'
    if 'Spirit' in tags: return 'SpiritSpecialSummonOnly' if special_only else 'Spirit'
    if 'Union' in tags: return 'Union'
    if 'Gemini' in tags: return 'Gemini'
    if 'Normal' in tags: return 'Normal'
    if not effect:
        notes['No typeline, made as Effect'].append(card['name'])
    return 'EffectSpecialSummonOnly' if special_only else 'Effect'


def game_text(text):
    return re.sub(r'\r?\n', '\r\n', (text or '').strip())


def description(card):
    """The card's text the way the game writes it: a Pendulum monster's monster text, then [Pendulum Effect] and its Pendulum text."""
    if 'pend_desc' in card:
        return game_text(card.get('monster_desc', '')) + '\r\n[Pendulum Effect]\r\n' + game_text(card['pend_desc'])
    return game_text(card.get('desc', ''))


def stat(value):
    return '?' if value is None or value < 0 else value


def fetch_art(card, path, failed):
    if path.exists():
        return True
    try:
        url = card['card_images'][0]['image_url_cropped']
        request = urllib.request.Request(url, headers={'User-Agent': 'Yu-Gi-Oh-Ex-CardsMod/1.0'})
        with urllib.request.urlopen(request, timeout=30) as response:
            picture = Image.open(io.BytesIO(response.read())).convert('RGB')
        path.parent.mkdir(parents=True, exist_ok=True)
        picture.resize((304, 304), Image.LANCZOS).save(path, 'JPEG', quality=92)
        time.sleep(0.06)   # YGOPRODeck allows 20 requests a second
        return True
    except (urllib.error.URLError, OSError, KeyError, IndexError) as error:
        failed.append(f'{card["name"]}: {error}')
        return False


def main():
    api, lists, mod = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    cards = json.loads(api.read_text(encoding='utf-8'))['data']
    by_password = {card['id']: card for card in cards}
    with (lists / 'Game Cards.csv').open(encoding='utf-8') as f:
        game_names = {norm(row['name']) for row in csv.DictReader(f)}
    with (lists / 'Missing Cards.csv').open(encoding='utf-8') as f:
        missing = [int(row['password']) for row in csv.DictReader(f)]

    content = mod / 'Yu-Gi-Oh-Ex'
    ids_file = mod / 'card-ids.json'
    ids = json.loads(ids_file.read_text(encoding='utf-8')) if ids_file.exists() else {}

    def id_for(key, first):
        if key not in ids:
            used = [v for v in ids.values() if (v >= FIRST_CARD_ID) == (first >= FIRST_CARD_ID)]
            ids[key] = max(used, default=first - 1) + 1
        return ids[key]

    entries, notes, failed, kinds = [], defaultdict(list), [], Counter()

    # tokens named in card text ("X Token" (Type/ATTRIBUTE/Level n/ATK a/DEF d)) the game doesn't have
    tokens = {}
    for card in sorted(cards, key=lambda c: c['name']):
        for match in TOKEN.finditer(card.get('desc', '')):
            name = re.sub(r'Tokens$', 'Token', match.group(1))
            key = norm(name)
            if key in game_names or key in tokens:
                continue
            stats = TOKEN_STATS.match(re.sub(r'\s*/\s*', '/', match.group(2).strip()))
            if not stats:
                notes['Token skipped: its Level or Attribute is set by the summoning effect'].append(f'{name} ({card["name"]})')
                continue
            types = stats['types'].split('/')
            if any(t.strip().lower() == 'tuner' for t in types[1:]):
                notes['Tuner token made as a plain Token (the Token kind has no Tuner flag)'].append(name)
            tokens[key] = (name, types[0], stats, card)
    for key, (name, race, stats, source) in tokens.items():
        image = f'art/{FOLDERS["Token"]}/{source["id"]}.jpg'
        fetch_art(source, content / image, failed)
        entries.append({
            'id': id_for('token:' + name, FIRST_TOKEN_ID),
            'name': name,
            'description': f'This card can be used as a "{name}". (Summoned by "{source["name"]}".)',
            'copies': 0,
            'image': image,
            'kind': 'Token',
            'type': type_name(race),
            'attribute': stats['attr'].capitalize(),
            'level': int(stats['level']),
            'atk': '?' if stats['atk'] == '?' else int(stats['atk']),
            'def': '?' if stats['def'] == '?' else int(stats['def']),
            'typeline': [race.strip()] + [t.strip() for t in stats['types'].split('/')[1:]] + ['Token'],   # e.g. Winged Beast/Tuner/Token
        })
        kinds['Token'] += 1

    # the missing cards
    for done, password in enumerate(missing, 1):
        card = by_password.get(password)
        if card is None:
            notes['Not in the YGOPRODeck download'].append(str(password))
            continue
        kind_text = card['type']
        if kind_text in ('Spell Card', 'Trap Card'):
            kind = 'Spell' if kind_text == 'Spell Card' else 'Trap'
            entry = {'kind': kind, 'icon': type_name(card.get('race', 'Normal'))}
        else:
            kind = kind_of(card, notes)
            if not card.get('attribute'):
                notes['No Attribute on YGOPRODeck, made LIGHT'].append(card['name'])
            entry = {'kind': kind, 'type': type_name(card.get('race') or ''), 'attribute': (card.get('attribute') or 'Light').capitalize()}
            if kind.startswith('Link'):
                entry['level'] = card.get('linkval', 1)
                entry['atk'] = stat(card.get('atk'))
                entry['def'] = 0
                entry['linkmarkers'] = card.get('linkmarkers', [])
            else:
                entry['level'] = card.get('level', 0)
                entry['atk'] = stat(card.get('atk'))
                entry['def'] = stat(card.get('def'))
            if 'scale' in card:
                entry['scale'] = card['scale']
            # the card's exact kind ("Dragon/Ritual/Pendulum/Effect"): "kind" is the nearest the game has today, this is the truth the
            # effect system will grow to (Ritual Pendulum, Ritual Tuner, ...). Yu-Gi-Oh-MoreCards ignores keys it doesn't know.
            entry['typeline'] = card.get('typeline', [])
        image = f'art/{FOLDERS[kind]}/{password}.jpg'
        fetch_art(card, content / image, failed)
        entries.append({'id': id_for(str(password), FIRST_CARD_ID), 'name': card['name'], 'description': description(card), 'copies': 0,
                        'image': image, **entry, 'password': password})
        kinds[kind] += 1
        if done % 250 == 0:
            print(f'{done} / {len(missing)} cards', flush=True)

    # Archetypes: each card gets the code of its YGOPRODeck archetype ("archetypes", read by Yu-Gi-Oh-MoreCards: the game's archetype checks,
    # Fusion materials such as "2 "Dream Mirror" monsters"). A name the game's list (CARD_Named Archetypes.json) has keeps its code; a new
    # one gets a code from FIRST_NEW_ARCHETYPE, kept in archetype-ids.json, and is listed in the mod's Archetypes.json (WolfX shows the names).
    known = {a['name'].lower(): a['code'] for a in json.loads(ARCHETYPE_LIST.read_text(encoding='utf-8'))['archetypes']}
    arch_file = mod / 'archetype-ids.json'
    new_codes = json.loads(arch_file.read_text(encoding='utf-8')) if arch_file.exists() else {}
    archetype_of = {card['id']: card.get('archetype') for card in cards}
    for entry in entries:
        name = archetype_of.get(entry.get('password')) or ''
        if not name:
            continue
        code = known.get(name.lower())
        if code is None:
            code = new_codes.setdefault(name, max(list(new_codes.values()) + [FIRST_NEW_ARCHETYPE - 1]) + 1)
        entry['archetypes'] = [code]
    arch_file.write_text(json.dumps(new_codes, indent=1, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    (content / 'Archetypes.json').write_text(json.dumps({'archetypes': [{'code': c, 'name': n, 'names': {'E': n}} for n, c in sorted(new_codes.items(), key=lambda x: x[1])]},
                                                        indent=1, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')

    entries.sort(key=lambda e: e['id'])
    # what was added to the cards after they were made (Fusion recipes, Ritual pairs, effects: WolfX --attach-summoning, the Effects tab)
    # is kept: every key of the old entry this script doesn't write itself carries over, by id
    previous = content / 'cards.json'
    if previous.exists():
        old = {c['id']: c for c in json.loads(previous.read_text(encoding='utf-8'))['cards']}
        for entry in entries:
            for key, value in old.get(entry['id'], {}).items():
                entry.setdefault(key, value)
    content.mkdir(parents=True, exist_ok=True)
    (content / 'cards.json').write_text(json.dumps({'cards': entries}, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
    ids_file.write_text(json.dumps(ids, indent=1, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')

    print(f'{len(entries)} cards written to {content / "cards.json"}')
    for kind, count in kinds.most_common():
        print(f'  {FOLDERS[kind]}: {count}')
    for what, names in notes.items():
        print(f'{what} ({len(names)}): {", ".join(names[:12])}{" ..." if len(names) > 12 else ""}')
    if failed:
        print(f'Art not downloaded ({len(failed)}, shown as the missing-art checkerboard; run again to retry):')
        for line in failed[:20]:
            print('  ' + line)


if __name__ == '__main__':
    main()
