"""
Adds every card Master Duel's duel engine plays to WolfX's Effect library (effect_reference.json) and the names of the ones the game does
not have to the as("name") list (engine_card_names.json), so BOB / BAB can give a custom card the whole effect of ANY card: as(<id>);
Run it on the effect_reference.json build_effect_reference.py writes (it skips ids already there, so running it twice changes nothing).

    python make_full_effect_library.py <ygoprodeck cardinfo.json (?misc=yes)> <Cards mod cards.json> <WolfX Content folder>

Why it is safe for every card, the specially handled ones included: under Master Duel's engine (Core MasterDuelEngine=1, the Effects plugin
requires it) a single-effect clone PLAYS AS its source for the duel (Yu-Gi-Oh-MoreCards -> Core_EngineLend): the engine's own rows for that
id and every branch Konami keyed on it apply, with the custom card's own stats. docs/MasterDuelEngine.md "Lending".

The entries the reference already has (read from the game's tables by docs/effect-scripts/build_effect_reference.py, with composed scripts)
are kept exactly as they are; every other card the engine has a record for (the game's cards and the Cards mod's cards with a Konami id) is
added with the script as(<Konami id>); and "engine": true. Normal Monsters, Tokens and Skill Cards have no effect and are left out.
"""
import json
import sys
from pathlib import Path

FRAME_KIND = {
    'effect': 'Effect', 'spell': 'Spell', 'trap': 'Trap', 'xyz': 'Xyz_Effect', 'fusion': 'Fusion_Effect', 'synchro': 'Synchro_Effect',
    'link': 'Link_Effect', 'effect_pendulum': 'Pendulum_Effect', 'ritual': 'Ritual_Effect', 'fusion_pendulum': 'Pendulum_Effect',
    'xyz_pendulum': 'Pendulum_Effect', 'synchro_pendulum': 'Pendulum_Effect', 'ritual_pendulum': 'Pendulum_Effect',
}
SKIP_FRAMES = {'normal', 'normal_pendulum', 'token', 'skill'}
NOTE = ('Runs this card\'s own effect from Master Duel\'s duel engine: in a duel the card plays as this card (its rules and every special '
        'case Konami wrote for it), with its own stats. Needs Core MasterDuelEngine=1, and this card must not be in the same duel.')


def main(cardinfo, cards_mod, content):
    content = Path(content)
    data = json.loads(Path(cardinfo).read_text(encoding='utf-8'))['data']
    by_konami = {}
    for card in data:
        misc = (card.get('misc_info') or [{}])[0]
        konami = misc.get('konami_id')
        if konami:
            by_konami.setdefault(int(konami), card)

    # card_names.json (the game's cards) is also how the Card Manager, Summoning and Fusion readers turn a name into a GAME id, so it is not
    # touched: the cards only as(...) can name go in engine_card_names.json (name -> Konami id), which only as(...) reads.
    names = json.loads((content / 'card_names.json').read_text(encoding='utf-8'))
    engine_ids = set(names.values())
    engine_names = {}
    mod = json.loads(Path(cards_mod).read_text(encoding='utf-8'))
    mod_cards = mod['cards'] if isinstance(mod, dict) else mod
    for c in mod_cards:
        if c.get('konamiId'):
            engine_ids.add(int(c['konamiId']))
            if c['name'] not in names:
                engine_names.setdefault(c['name'], int(c['konamiId']))

    reference_path = content / 'effect_reference.json'
    reference = json.loads(reference_path.read_text(encoding='utf-8'))
    have = {entry['id'] for entry in reference['cards']}
    added = skipped = unknown = 0
    for engine_id in sorted(engine_ids):
        if engine_id in have:
            continue
        card = by_konami.get(engine_id)
        if card is None:
            unknown += 1
            continue
        frame = card.get('frameType', '')
        if frame in SKIP_FRAMES:
            skipped += 1
            continue
        reference['cards'].append({
            'id': engine_id, 'name': card['name'], 'kind': FRAME_KIND.get(frame, 'Effect'), 'icon': '', 'text': card.get('desc', ''),
            'script': f'as({engine_id});', 'trigger': '', 'complete': True, 'sourceOk': True, 'unread': 0, 'notes': [NOTE], 'table': '',
            'cost': '', 'firstClause': True, 'action': 'as', 'engine': True,
        })
        added += 1
    reference['note'] = (reference.get('note', '') + ' Every other card Master Duel\'s engine plays was added as as(<id>) by '
                         'Tools/Scripts/make_full_effect_library.py (entries with "engine": true).').strip()
    reference_path.write_text(json.dumps(reference, ensure_ascii=False, indent=None, separators=(',', ':')), encoding='utf-8')
    (content / 'engine_card_names.json').write_text(json.dumps(engine_names, ensure_ascii=False, indent=None, separators=(',', ':')),
                                                    encoding='utf-8')
    print(f'effect library: {len(have)} kept, {added} added as as(<id>), {skipped} without an effect left out, '
          f'{unknown} ids YGOPRODeck has no card for; engine_card_names: {len(engine_names)} names')


if __name__ == '__main__':
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
