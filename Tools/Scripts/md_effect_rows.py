"""
Reads Master Duel's duel engine (masterduel_Data/Plugins/x86_64/duel.dll) for the effect rows Konami gave each card released after the game,
and writes them as rows of the game's own functions: the source for real effect rows (docs/EffectSystem.md section 104), no card text read.

    python md_effect_rows.py <duel.dll> <YuGiOh.exe> <ygoprodeck cardinfo.json (?misc=yes)> <Cards mod cards.json> <output .json>

duel.dll is the same engine as YuGiOh.exe, extended: the same five effect tables of 48-byte rows {u16 card id, 3 x u16 extra, 5 slot
functions}, the same card ids (Konami's database ids: Pot of Greed is 4844 in both, YGOPRODeck's misc_info konami_id), the same shared
machines. A Master Duel function is the game's function when the cards both games have use them in the same place: every card in both
tables, row by row and slot by slot, votes for a pair. Nothing from duel.dll is copied out but row shapes and function addresses; read your
own install, do not ship the dll or its tables.
"""
import json
import struct
import sys
from collections import Counter, defaultdict

# The five tables, by the table selector Get_EffectTableEntryForCard (0x1400DFBC0) uses (record word4). The game's start / count are the
# lookup's own; Master Duel's were found by the same extent scan (which gives the lookup's counts exactly on YuGiOh.exe).
TABLES = {
    'spellTrap': (0x140BA8730, 0x180F9BD70),   # word4 0 (and 2 with word3 40)
    'monster': (0x140B5CC20, 0x180FD4180),     # word4 1: monster triggers
    'handGrave': (0x140B85A50, 0x1810110C0),   # word4 2: hand / GY / banished
    'pendulum': (0x140B38290, 0x181051720),    # word4 3, Pendulum Monster in a Pendulum Zone (the 131-row head)
    'ignition': (0x140B39B20, 0x181053E80),    # word4 3: field ignition / Quick Effects
}


class PE:
    def __init__(self, path):
        self.data = data = open(path, 'rb').read()
        pe = struct.unpack_from('<I', data, 0x3C)[0]
        count = struct.unpack_from('<H', data, pe + 6)[0]
        optional = struct.unpack_from('<H', data, pe + 20)[0]
        self.base = struct.unpack_from('<Q', data, pe + 48)[0]
        self.sections = []
        for i in range(count):
            o = pe + 24 + optional + i * 40
            name = data[o:o + 8].rstrip(b'\0').decode()
            size, va, raw_size, raw = struct.unpack_from('<IIII', data, o + 8)
            self.sections.append((name, size, va, raw))
        self.text = next((va, size) for name, size, va, raw in self.sections if name == '.text')

    def offset(self, address):
        rva = address - self.base
        for name, size, va, raw in self.sections:
            if va <= rva < va + size:
                return rva - va + raw
        raise ValueError(hex(address))

    def is_function(self, p):
        return p == 0 or self.base + self.text[0] <= p < self.base + self.text[0] + self.text[1]

    def row(self, o):
        return (struct.unpack_from('<H', self.data, o)[0], list(struct.unpack_from('<3H', self.data, o + 2)),
                list(struct.unpack_from('<5Q', self.data, o + 8)))

    def table(self, start):
        """Rows from start while every slot is a function (or empty) and the ids stay sorted."""
        rows, o = [], self.offset(start)
        while True:
            r = self.row(o)
            if not all(self.is_function(s) for s in r[2]) or (rows and r[0] < rows[-1][0]):
                return rows
            rows.append(r)
            o += 48


def by_id(rows):
    cards = defaultdict(list)
    for r in rows:
        if r[0] != 3000:   # the Spell/Trap table's default rows
            cards[r[0]].append(r)
    return cards


def main(dll_path, exe_path, cardinfo_path, mod_path, out_path):
    md, game = PE(dll_path), PE(exe_path)
    md_tables = {t: by_id(md.table(m)) for t, (g, m) in TABLES.items()}
    game_tables = {t: by_id(game.table(g)) for t, (g, m) in TABLES.items()}

    # Function map: the cards both games have, same table, same number of rows -> slot by slot pairs.
    votes = defaultdict(Counter)
    extras = Counter()
    for t in TABLES:
        for cid, game_rows in game_tables[t].items():
            md_rows = md_tables[t].get(cid)
            if not md_rows or len(md_rows) != len(game_rows):
                continue
            for g, m in zip(game_rows, md_rows):
                extras[(m[1][0], g[1][0])] += 1
                for gs, ms in zip(g[2], m[2]):
                    if ms:
                        votes[ms][gs] += 1
    function_map, ambiguous = {}, {}
    for ms, c in votes.items():
        (best, n), = c.most_common(1)
        if best and n * 2 > sum(c.values()):
            function_map[ms] = best
        if len(c) > 1:
            ambiguous[hex(ms)] = {hex(k): v for k, v in c.items()}

    # The new cards: Cards mod password -> YGOPRODeck konami_id -> Master Duel rows.
    konami = {}
    for card in json.load(open(cardinfo_path, encoding='utf-8'))['data']:
        kid = (card.get('misc_info') or [{}])[0].get('konami_id')
        if kid:
            konami[card['id']] = kid
    cards, summary = [], Counter()
    for card in json.load(open(mod_path, encoding='utf-8-sig'))['cards']:
        if card.get('kind') == 'Token':
            continue
        password = int(card['password']) if str(card.get('password', '')).isdigit() else None
        kid = konami.get(password)
        entry = {'id': int(card['id']), 'name': card['name'], 'password': password, 'konamiId': kid, 'rows': [], 'missing': []}
        for t in TABLES:
            for _, extra, slots in md_tables[t].get(kid, []) if kid else []:
                mapped = [function_map.get(s, 0) if s else 0 for s in slots]
                missing = [hex(s) for s, g in zip(slots, mapped) if s and not g]
                entry['rows'].append({'table': t, 'mdExtra': [hex(x) for x in extra], 'slots': [hex(s) if s else None for s in mapped],
                                      'md': [hex(s) if s else None for s in slots]})
                entry['missing'] += missing
        status = ('no konami id' if not kid else 'no Master Duel rows' if not entry['rows']
                  else 'all game functions' if not entry['missing'] else 'some functions missing')
        entry['status'] = status
        summary[status] += 1
        cards.append(entry)

    missing_use = Counter(m for c in cards for m in set(c['missing']))
    out = {
        'summary': dict(summary),
        'functionMap': {hex(k): hex(v) for k, v in sorted(function_map.items())},
        'ambiguous': ambiguous,
        'extraPairs': {f'{hex(m)} -> {hex(g)}': n for (m, g), n in extras.most_common(40)},
        'missingFunctions': {k: v for k, v in missing_use.most_common()},
        'cards': cards,
    }
    json.dump(out, open(out_path, 'w', encoding='utf-8'), indent=1)
    print(f'tables (rows, Master Duel / game):', {t: (sum(map(len, md_tables[t].values())), sum(map(len, game_tables[t].values()))) for t in TABLES})
    print(f'functions mapped {len(function_map)}, ambiguous {len(ambiguous)}')
    print('new cards:', dict(summary))
    print(f'missing functions {len(missing_use)}; the 10 used most:', missing_use.most_common(10))


if __name__ == '__main__':
    if len(sys.argv) != 6:
        sys.exit(__doc__)
    main(*sys.argv[1:])
