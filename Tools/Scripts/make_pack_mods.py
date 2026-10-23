r"""
Builds the two ways to get the Cards mod's cards (Mods/Cards, made by make_cards_mod.py), each its own mod on top of it:

  Mods/Cards - Unlocked       unlocks.json: every profile owns 3 of every new card (the game's own default unlocks stay).
  Mods/Cards - Booster Packs  packs.json: one shop pack per set the cards first came out in (by its TCG date), named after it, in a
                              shop row per year. Cards never printed in the TCG are one "OCG Exclusives" pack, promos and prize cards one
                              "Promos & Prizes" pack, and the few older cards the game never had one "Lost Classics" pack.

    python make_pack_mods.py <ygoprodeck cardinfo.json> <ygoprodeck cardsets.json> "<docs/Card Lists>" <Mods folder> <sheet folder>

cardsets.json is https://db.ygoprodeck.com/api/v7/cardsets.php (set names with their TCG dates and pictures). <sheet folder> holds the
game's pdui\PackWrappers.dfymoo and .png (WolfX's Sprite sheets page can save them out): each pack whose set has a picture gets its own
wrapper sprite in a copy of that sheet, in the mod's YGO_2020\pdui; the rest borrow one of the game's wrappers. A pack's commons are the cards printed
Common in that set, the rest are its rares; a pack needs both, so a set with only one kind uses it for both lists. The packs need no
fixed id: Yu-Gi-Oh-Core puts each row's packs in the game's free pack slots when the shop shows it (docs/Packs.md).
"""
import csv
import html
import json
import re
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
from collections import defaultdict
from pathlib import Path

from PIL import Image

GAME_END = '2019-07-01'   # sets before this are older sets the game left cards out of
FIRST_YEAR_ROW = 6        # the game's rows are 0-5
ROW_SIZE = 6              # packs a shop row shows (no scrolling: a year with more gets more rows)
# the game's pack wrappers (pdui\PackWrappers.dfymoo), borrowed in turn by a pack whose set has no picture on YGOPRODeck
PACK_ART = [f'{row}_{n}' for row in range(1, 7) for n in range(1, 7) if f'{row}_{n}' not in ('4_6', '5_6', '6_6')]
WRAP_SIZE = (211, 300)    # every pack wrapper in the sheet
SHEET_WIDTH = 4096
PROMO = re.compile(r'promo|prize|participation|premiere|winner.s pack|lost art|championship', re.I)


def fetch_set_image(url, cache):
    """A set's picture from YGOPRODeck (kept in cache, so a rerun downloads nothing), or None."""
    path = cache / url.rsplit('/', 1)[-1]
    if not path.exists():
        try:
            request = urllib.request.Request(url, headers={'User-Agent': 'Yu-Gi-Oh-Ex-PackMods/1.0'})
            with urllib.request.urlopen(request, timeout=30) as response:
                path.write_bytes(response.read())
            time.sleep(0.06)   # YGOPRODeck allows 20 requests a second
        except (urllib.error.URLError, OSError):
            return None
    try:
        return Image.open(path).convert('RGBA')
    except OSError:
        return None


def wrapper(picture):
    """A set picture as a pack wrapper: full height, centred, the sides left transparent (pack photos are narrower than the game's)."""
    width, height = WRAP_SIZE
    scaled = picture.resize((max(1, round(picture.width * height / picture.height)), height), Image.LANCZOS)
    if scaled.width > width:
        scaled = picture.resize((width, max(1, round(picture.height * width / picture.width))), Image.LANCZOS)
    out = Image.new('RGBA', WRAP_SIZE, (0, 0, 0, 0))
    out.paste(scaled, ((width - scaled.width) // 2, (height - scaled.height) // 2))
    return out


def write_sheet(base, art, folder):
    """
    The game's pdui\\PackWrappers sheet with a "wrap_<pack name>" sprite added per pack (art: name -> picture), written as loose files in
    the mod (folder: its YGO_2020\\pdui). The game's sheet stays where it is (top left); the new wrappers go under it, in a grid as wide as
    SHEET_WIDTH. Pack_GetArtName asks for "wrap_<name>", so a pack with its own wrapper needs no "art".
    """
    text = (base / 'PackWrappers.dfymoo').read_text(encoding='utf-8')
    old = Image.open(base / 'PackWrappers.png').convert('RGBA')
    columns = SHEET_WIDTH // (WRAP_SIZE[0] + 2)
    rows = (len(art) + columns - 1) // columns
    top = old.height + 2
    sheet = Image.new('RGBA', (max(old.width, SHEET_WIDTH), top + rows * (WRAP_SIZE[1] + 2)), (0, 0, 0, 0))
    sheet.paste(old, (0, 0))
    lines = text.replace('\r\n', '\n').rstrip('\n').split('\n')
    lines = [f'w {sheet.width}' if l.startswith('w ') else f'h {sheet.height}' if l.startswith('h ') else l for l in lines]
    for i, (name, picture) in enumerate(art.items()):
        x, y = i % columns * (WRAP_SIZE[0] + 2), top + i // columns * (WRAP_SIZE[1] + 2)
        sheet.paste(picture, (x, y))
        lines += [f'n wrap_{name}', f's {x} {y} {WRAP_SIZE[0]} {WRAP_SIZE[1]}', '~']
    folder.mkdir(parents=True, exist_ok=True)
    sheet.save(folder / 'PackWrappers.png', optimize=True)
    (folder / 'PackWrappers.dfymoo').write_bytes('\r\n'.join(lines).encode('utf-8'))   # CRLF, no newline after the last "~", as the game's
    return sheet.size


FIRST_DECK_ID, LAST_DECK_ID = 546, 599   # deckdata ids: the game uses up to 545, Forbidden Memories 600+
DECK_PRICE = 1000                         # DP for a whole Structure Deck (a booster is 200): change it in the page's value


# Deck owners (chardata ids; the portrait the deck picker shows): the duelist a deck belongs with in the anime, by a word of its name.
DECK_OWNERS = [
    ('obelisk', 94), ('blue-eyes', 94), ('slifer', 104), ('sacred beasts', 122), ('cyber', 137), ('crystal beasts', 120),
    ('dark world', 119), ('crimson king', 16), ('elemental hero', 119), ('destiny hero', 109), ('dark magician', 104),
    ('red-eyes', 81), ('harpie', 82), ('stardust', 35), ('black-winged', 10), ('blackwing', 10), ('rose', 1), ('number', 174),
    ('odd-eyes', 72), ('salamangreat', 187), ('code talker', 143), ('borrel', 188), ('gladiator', 40),
]
# the selectable duelists (chardata Selectable = 1) a deck with no owner above draws from
DUELISTS = [1, 5, 10, 16, 19, 25, 35, 37, 38, 40, 43, 52, 61, 65, 68, 69, 71, 72, 75, 77, 79, 80, 81, 82, 83, 84, 94, 102, 103, 104,
            105, 112, 113, 117, 119, 120, 121, 128, 132, 133, 136, 137, 140, 143, 144, 160, 165, 168, 172, 174, 187, 188]


def deck_title(title, all_titles):
    """A deck's name as the game shows it: "Structure Deck: Albaz Strike" is "Albaz Strike", "(All-Foil Edition)" goes, and two decks
    that would share a name are told apart by their product ("Spirit Charmers Chronicles"). At most 30 characters, the game's longest
    deck title ("Melodious Maestra's Fortissimo"), so nothing runs past the deck picker's tile."""
    def short(t):
        return re.sub(r'\s*\([^)]*\)', '', html.unescape(t).split(': ', 1)[-1]).strip()
    name = short(title)
    if sum(short(t) == name for t in all_titles) > 1 and 'chronicles' in title.lower():
        name += ' Chronicles'
    return name[:30]


def deck_owner(title):
    lower = html.unescape(title).lower()
    for word, owner in DECK_OWNERS:
        if word in lower:
            return owner
    return DUELISTS[sum(ord(c) for c in lower) % len(DUELISTS)]


def norm(name):
    return re.sub(r'[^a-z0-9]', '', html.unescape(name).lower())


def yugipedia_page(title, cache):
    """A Yugipedia page's wikitext (its API; kept in cache, so a rerun asks nothing), or ''."""
    path = cache / (re.sub(r'[^A-Za-z0-9 _.-]', '_', title) + '.txt')
    if path.exists():
        return path.read_text(encoding='utf-8')
    url = 'https://yugipedia.com/api.php?' + urllib.parse.urlencode(
        {'action': 'query', 'prop': 'revisions', 'rvprop': 'content', 'format': 'json', 'redirects': 1, 'titles': title})
    try:
        request = urllib.request.Request(url, headers={'User-Agent': 'Yu-Gi-Oh-Ex-PackMods/1.0 (modding tool)'})
        with urllib.request.urlopen(request, timeout=30) as response:
            pages = json.load(response)['query']['pages']
        time.sleep(0.5)
    except (urllib.error.URLError, OSError, KeyError, ValueError):
        return ''
    page = next(iter(pages.values()))
    text = page['revisions'][0]['*'] if 'revisions' in page else ''
    cache.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding='utf-8')
    return text


def yugipedia_deck(name, cache, by_name):
    """A deck set's cards, each as many times as the box holds it (card ids), or None when Yugipedia can't say. The set list's rows are
    "code; name; rarity; print; quantity" (quantity defaults to the list's qty=, else 1); counting stops at the box's "Deck of N cards"."""
    box = re.search(r'Deck of (\d+) cards', yugipedia_page(name, cache))
    text = yugipedia_page(f'Set Card Lists:{name} (TCG-EN)', cache)
    if not box or not text:
        return None
    default = re.search(r'\|qty=(\d+)', text)
    default = int(default.group(1)) if default else 1
    wanted, deck = int(box.group(1)), []
    for line in text.split('\n'):
        if not re.match(r'^[A-Z0-9]+-EN\d', line):
            continue   # -ENT.. rows are tokens
        fields = [f.strip() for f in line.split('//')[0].split(';')]
        copies = int(fields[4]) if len(fields) > 4 and fields[4].isdigit() else default
        card = by_name.get(norm(re.sub(r'\s*\((card|TCG|OCG)\)$', '', fields[1])))
        if card is None:
            if 'token' not in fields[1].lower():
                MISSING.append(f'{name}: {fields[1]} x{copies}')   # a card neither the game nor the Cards mod has
            continue
        deck += [card] * min(copies, wanted - len(deck))
        if len(deck) >= wanted:
            break
    return deck or None


MISSING = []   # deck cards Yugipedia lists that no card id was found for (printed at the end)


def bob_text(text):
    return text.replace('\\', '\\\\').replace('"', '\\"')


def write_page(path, decks):
    """The Card Store's Structure Decks page, in BOB (docs/PageScript.md): the game's own deck picker (Free Duel's) listing the decks for
    sale, the game's deck panel beside it; confirm on a deck to buy it (give_deck saves it in your decks with its cards)."""
    listed = ', '.join(str(deck['id'] + 32) for deck in decks)
    lines = [
        '// Structure Decks (Booster Blast, made by Tools/Scripts/make_pack_mods.py): the Card Store sells each set that is one ready-made deck.',
        '// The game\'s deck picker lists them (decks list "..."); confirm on one to buy it for value:price: it goes into your decks with its',
        '// cards (give_deck, docs/PageScript.md).',
        'header "Structure Decks";',
        '',
        f'value deck_price = "{DECK_PRICE}";   // one Structure Deck; per deck: value price.<deck id> = "1500";',
        'value price = "{value:price.{var:bb.deck}|{value:deck_price}}";',
        '',
        'user;',
        '',
        '// a deck can be bought once per save: save:bb.bought.<deck id> remembers it (in the save), and the list leaves bought decks out',
        f'decks list "{listed}" hide "{{save:bb.bought.#}}" at 118.5, 171 size 500, 760 bind var:bb.deck',
        '    description "Confirm on a deck to buy it: it goes into your decks, with 3 of every card in it. L / R change the tab."',
        '    -> if game:freedecks < 1 -> { sound(71); message("All 32 of your deck slots are in use. Delete a deck in the Deck Editor first."); }',
        '       else if game:dp < value:price',
        '           -> { sound(71); message("{game:deckname.{var:bb.deck}} costs {value:price} DP. You have {game:dp} DP."); }',
        '       else confirm("Buy {game:deckname.{var:bb.deck}} for {value:price} DP? You have {game:dp} DP.")',
        '           -> take_dp(value:price) -> give_deck(var:bb.deck, 3)',
        '               -> { set(save:bb.bought.{var:bb.deck}, "1"); sound(52); message("{game:deckname.{var:bb.deck}} is in your decks, with 3 of every card."); reload; }',
        '               else { give_dp(value:price); message("The deck could not be saved; your DP was given back."); };',
        '',
        '// the highlighted deck\'s cards, as the deck editor shows a deck (Main / Extra rows, the stats bar): the Battle Pack edit screen\'s place',
        'deckcards "{var:bb.deck}" at 646, 120 bind var:bb.card description "The deck\'s cards. Right-click a card for its details; L / R go back to the decks.";',
        '',
    ]
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text('\n'.join(lines), encoding='utf-8', newline='\r\n')


def write_json(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')


def mod_json(name, description, details, requires):
    return {'name': name, 'version': '0.1', 'author': 'Yu-Gi-Oh-Ex', 'description': description, 'details': details, 'requires': requires}


def main():
    cardinfo, cardsets, lists, mods, base_sheet = (Path(a) for a in sys.argv[1:6])
    cards = {c['id']: c for c in json.loads(cardinfo.read_text(encoding='utf-8'))['data']}
    sets = {s['set_name']: s for s in json.loads(cardsets.read_text(encoding='utf-8'))}
    ids = json.loads((mods / 'Cards' / 'card-ids.json').read_text(encoding='utf-8'))
    with (lists / 'Missing Cards.csv').open(encoding='utf-8') as f:
        missing = [int(row['password']) for row in csv.DictReader(f)]

    # ---- Cards - Unlocked ----
    unlocked = mods / 'Cards - Unlocked'
    every = sorted(v for k, v in ids.items() if not k.startswith('token:'))
    write_json(unlocked / 'Yu-Gi-Oh-Ex' / 'unlocks.json', {'replaceDefaults': False, 'cards': [{'id': i, 'copies': 3} for i in every]})
    write_json(unlocked / 'mod.json', mod_json(
        'Cards - Unlocked', f'Every card in the Cards mod ({len(every)}) is yours from the start, 3 of each.',
        'Turn on with the Cards mod. For testing and for playing without collecting; use Cards - Booster Packs to collect them instead.',
        ['Yu-Gi-Oh-MoreCards']))

    # ---- Cards - Booster Packs: each card in the set it first came out in ----
    groups = defaultdict(lambda: {'common': [], 'rare': []})
    for password in missing:
        card = cards.get(password)
        if card is None or str(password) not in ids:
            continue
        printings = [p for p in card.get('card_sets') or [] if sets.get(p['set_name'], {}).get('tcg_date')]
        if not printings:
            key = ('OCG Exclusives', '')
            rarity = 'rare'
        else:
            first = min(printings, key=lambda p: sets[p['set_name']]['tcg_date'])
            name, date = first['set_name'], sets[first['set_name']]['tcg_date']
            in_set = [p for p in printings if p['set_name'] == name]
            rarity = 'common' if any(p.get('set_rarity') == 'Common' for p in in_set) else 'rare'
            if PROMO.search(name):
                key = ('Promos & Prizes', '')
            elif date < GAME_END:
                key = ('Lost Classics', '')
            else:
                key = (name, date)
        groups[key][rarity].append(ids[str(password)])

    dated = sorted((k for k in groups if k[1]), key=lambda k: (k[1], k[0]))

    # A set's pack holds the whole set, as the real booster / deck does: every card printed in it, reprints of cards the game already
    # has included (by the game's own id: Game Cards.csv, password -> gameId; alternate-art passwords count too).
    with (lists / 'Game Cards.csv').open(encoding='utf-8') as f:
        game_id = {int(row['password']): int(row['gameId']) for row in csv.DictReader(f) if int(row['password']) > 0}
    new_in = {key: len(set(groups[key]['common']) | set(groups[key]['rare'])) for key in dated}
    by_title = {key[0]: key for key in dated}
    full = defaultdict(lambda: {'common': set(), 'rare': set()})
    extra_of = {}    # card id -> True when it goes in the Extra Deck (Fusion, Synchro, Xyz, Link)
    signature = {}   # set -> its highest-rarity card (shown on a deck's info panel)
    rank = lambda r: next((i for i, word in enumerate(('Starlight', 'Ghost', 'Quarter Century', 'Secret', 'Ultimate', 'Ultra', 'Super', 'Rare'))
                           if word.lower() in (r or '').lower()), 99)
    unmatched = 0
    for card in cards.values():
        here = [p for p in card.get('card_sets') or [] if p['set_name'] in by_title]
        if not here:
            continue
        passwords = [card['id']] + [image['id'] for image in card.get('card_images', [])]
        cid = next((ids[str(p)] for p in passwords if str(p) in ids), None) or next((game_id[p] for p in passwords if p in game_id), None)
        if cid is None:
            unmatched += 1   # a Skill card, a token, or a card neither the game nor the Cards mod has
            continue
        extra_of[cid] = any(t in card.get('typeline', []) for t in ('Fusion', 'Synchro', 'XYZ', 'Xyz', 'Link'))
        for name in {p['set_name'] for p in here}:
            rarity = 'common' if any(p.get('set_rarity') == 'Common' for p in here if p['set_name'] == name) else 'rare'
            full[by_title[name]][rarity].add(cid)
            best = min(rank(p.get('set_rarity')) for p in here if p['set_name'] == name)
            if best < signature.get(name, (99, 0))[0]:
                signature[name] = (best, cid)
    for key in dated:
        common, rare = full[key]['common'], full[key]['rare'] - full[key]['common']
        groups[key] = {'common': sorted(common), 'rare': sorted(rare)}

    # Decks: a set that is one ready-made deck (a Structure / Starter Deck, or another "Deck" that fits the game's 60 / 15 / 15) is a deck
    # of decks.json (Yu-Gi-Oh-Campaign) that the Structure Decks page sells with give_deck, not a pack. Its exact list comes from Yugipedia
    # (the set list's quantities, up to the box's card count: what follows is tokens and "1 of N" bonus cards); YGOPRODeck lists a set's
    # cards without copies, so it is only the fallback (one of each).
    by_name = {}
    for card in cards.values():
        passwords = [card['id']] + [image['id'] for image in card.get('card_images', [])]
        cid = next((ids[str(p)] for p in passwords if str(p) in ids), None) or next((game_id[p] for p in passwords if p in game_id), None)
        if cid is not None:
            by_name.setdefault(norm(card['name']), cid)
            extra_of.setdefault(cid, any(t in card.get('typeline', []) for t in ('Fusion', 'Synchro', 'XYZ', 'Xyz', 'Link')))
    with (lists / 'Game Cards.csv').open(encoding='utf-8') as f:
        for row in csv.DictReader(f):   # the game's cards by name too: a few (the Egyptian Gods) have passwords YGOPRODeck doesn't use
            if row['name'] and int(row['gameId']) > 0:
                by_name.setdefault(norm(row['name']), int(row['gameId']))
    exact = {}
    for key in dated:
        title = key[0].lower()
        if 'structure deck' in title or 'starter deck' in title or ('deck' in title and 'tin' not in title):
            exact[key] = yugipedia_deck(html.unescape(key[0]), mods / '_downloads' / 'yugipedia', by_name)

    def deck_lists(key):
        if exact.get(key):
            cards_in = exact[key]
            main = [c for c in cards_in if not extra_of.get(c)]
            extra = [c for c in cards_in if extra_of.get(c)]
            side = (main[60:] + extra[15:])[:15]
            return main[:60], extra[:15], side, len(cards_in) - len(main[:60]) - len(extra[:15]) - len(side)
        cards_in = sorted(set(groups[key]['common']) | set(groups[key]['rare']))
        main = [c for c in cards_in if not extra_of.get(c)]
        extra = [c for c in cards_in if extra_of.get(c)]
        side = (main[60:] + extra[15:])[:15]
        return main[:60], extra[:15], side, len(cards_in) - len(main[:60]) - len(extra[:15]) - len(side)
    deck_keys = []
    for key in dated:
        title = key[0].lower()
        if 'structure deck' in title or 'starter deck' in title or ('deck' in title and 'tin' not in title):
            if deck_lists(key)[3] == 0:
                deck_keys.append(key)
    dated = [k for k in dated if k not in deck_keys]

    booster = mods / 'Booster Blast'
    cache = mods / '_downloads' / 'set-images'   # YGOPRODeck's pictures as downloaded (no mod.json: not a mod, never shipped)
    cache.mkdir(parents=True, exist_ok=True)
    art, borrowed = {}, []
    packs = []
    for n, key in enumerate(dated + [k for k in ('Lost Classics', 'Promos & Prizes', 'OCG Exclusives') for k in [(k, '')] if k in groups]):
        title, date = key
        common, rare = sorted(groups[key]['common']), sorted(groups[key]['rare'])
        if not common:
            common = rare
        if not rare:
            rare = common
        kind = 'Structure Deck' if 'structure deck' in title.lower() or 'starter deck' in title.lower() else 'Booster'
        count = len(set(common) | set(rare))
        text = (f'The whole {title} set ({date}): {count} cards, {new_in[key]} of them new to the game.' if date else
                f'{count} cards the game never had.')
        if kind == 'Structure Deck':
            text += ' (A Structure Deck: it may become a ready-made deck later.)'
        name = f'cards_{n + 1:03d}'
        pack = {'name': name, 'title': {'E': html.unescape(title)}, 'text': {'E': html.unescape(text)}}   # YGOPRODeck has "5D&apos;s"
        url = sets.get(title, {}).get('set_image') if date else None
        picture = fetch_set_image(url, cache) if url else None
        if picture is not None:
            art[name] = wrapper(picture)          # its own wrapper sprite, wrap_<name>
        else:
            pack['art'] = PACK_ART[n % len(PACK_ART)]
            borrowed.append(title)
        pack.update({'cost': 200, 'series': 0, 'common': common, 'rare': rare, '$group': date[:4] if date else 'Extras'})
        packs.append(pack)

    # Shop rows: at most six packs a row (the shop shows six, no scrolling), each row one year (a year with more packs gets more rows,
    # in date order), the Extras last. New rows start after the game's six.
    rows, row = [], FIRST_YEAR_ROW
    for group in dict.fromkeys(p['$group'] for p in packs):
        members = [p for p in packs if p['$group'] == group]
        for start in range(0, len(members), ROW_SIZE):
            part = len(members) > ROW_SIZE and f' ({start // ROW_SIZE + 1})' or ''
            rows.append({'row': row, 'name': {'E': group + part}})
            for pack in members[start:start + ROW_SIZE]:
                pack['series'] = row
            row += 1
    for pack in packs:
        del pack['$group']

    size = write_sheet(base_sheet, art, booster / 'YGO_2020' / 'pdui')
    write_json(booster / 'Yu-Gi-Oh-Ex' / 'packs.json', {'rows': rows, 'newPacks': packs})

    # ---- the ready-made decks (decks.json, Yu-Gi-Oh-Campaign) and the page that sells them ----
    ids_file = booster / 'deck-ids.json'   # set name -> deckdata id, so a deck keeps its id when the list grows
    deck_ids = json.loads(ids_file.read_text(encoding='utf-8')) if ids_file.exists() else {}
    decks = []
    for key in sorted(deck_keys, key=lambda k: (k[1], k[0])):
        title, date = key
        if title not in deck_ids:
            used = set(deck_ids.values())
            deck_ids[title] = next(i for i in range(FIRST_DECK_ID, LAST_DECK_ID + 1) if i not in used)
        main, extra, side, _ = deck_lists(key)
        decks.append({
            'id': deck_ids[title], 'file': f'booster_blast_{deck_ids[title]}',
            # no series, like the game's starter decks (380-384); the owner is the deck's portrait in the deck picker: the anime duelist
            # it belongs with, else a duelist picked by its name (the same one every run). Their own deck in Free Duel stays theirs.
            'series': -1, 'character': deck_owner(title), 'signatureCard': signature.get(title, (0, 0xFFFF))[1], 'sku': 1, 'unlocked': False,
            'title': {'E': deck_title(title, [k[0] for k in deck_keys])},
            'cards': {'main': main, 'extra': extra, 'side': side},
            '$set': {'name': html.unescape(title), 'date': date, 'new': new_in[key]},
        })
    write_json(ids_file, deck_ids)
    write_json(booster / 'Yu-Gi-Oh-Ex' / 'decks.json', {'decks': decks})
    write_page(booster / 'Yu-Gi-Oh-Ex' / 'pages' / 'booster-blast-decks.bob', decks)

    write_json(booster / 'mod.json', mod_json(
        'Booster Blast', f'{len(packs)} booster packs and {len(decks)} Structure Decks of the cards released after the game, as the real sets.',
        'Turn on with the Cards mod. Each booster pack is a whole set the Cards mod\'s cards debuted in (by TCG date), reprints of cards the '
        'game already has included, in a shop row per year; OCG-only cards, promos and older cards the game never had get a pack each in the '
        'Extras row (Card Store > Buy Booster Packs). Structure Decks are bought on Card Store > Buy Decks (pages\\booster-blast-decks.bob), '
        'whole and once per save, into your saved decks with 3 of every card. Yu-Gi-Oh-Core shares the game\'s free pack slots between rows, '
        'so there is no limit on packs.',
        ['Yu-Gi-Oh-Core', 'Yu-Gi-Oh-MoreCards', 'Yu-Gi-Oh-Campaign', 'Yu-Gi-Oh-RIX']))

    print(f'Cards - Unlocked: {len(every)} cards')
    print(f'Cards - Booster Packs: {len(packs)} packs in {len(rows)} rows ({unmatched} printed cards skipped: Skill cards, tokens, '
          'or cards neither the game nor the Cards mod has)')
    for key in dated[:3] + dated[-3:]:
        print(f'  {key[0]}: {len(groups[key]["common"]) + len(groups[key]["rare"])} cards ({new_in[key]} new)')
    for shop_row in rows:
        print(f'  row {shop_row["row"]} {shop_row["name"]["E"]}: {sum(1 for p in packs if p["series"] == shop_row["row"])} packs')
    for line in MISSING:
        print('Deck card not found:', line)
    print(f'Pack art: {len(art)} real wrappers (sheet now {size[0]} x {size[1]}), {len(borrowed)} borrow a game wrapper: {", ".join(borrowed)}')


if __name__ == '__main__':
    main()
