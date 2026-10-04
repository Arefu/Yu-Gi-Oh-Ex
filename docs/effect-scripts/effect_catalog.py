import pefile, struct, json, collections, sys, os
sys.stdout.reconfigure(encoding='utf-8')
EXE = "C:/Program Files (x86)/Steam/steamapps/common/Yu-Gi-Oh! Legacy of the Duelist Link Evolution/YuGiOh.exe"
pe = pefile.PE(EXE, fast_load=True); base = pe.OPTIONAL_HEADER.ImageBase
rd = lambda va, n: pe.get_data(va - base, n)
game = json.load(open(r'C:\Users\Johnathon\Desktop\New folder\Cards\Game Cards.json', encoding='utf-8-sig')); game = game['cards'] if isinstance(game, dict) else game
info = {c['id']: c for c in game}
TABLES = {'T5 spell/trap  @140BA8730': (0x140BA8730, 2831), 'T2 monster     @140B5CC20': (0x140B5CC20, 3489),
          'T4 (unknown)   @140B85A50': (0x140B85A50, 2970), 'T1 trigger/cont @140B38290': (0x140B38290, 3123)}
cat = lambda cid: ('Spell/Trap' if info[cid]['kind'] in ('Spell', 'Trap') else 'Monster') if cid in info else 'non-card'
data = {}
for tn, (va, n) in TABLES.items():
    rows = []
    for i in range(n):
        raw = rd(va + i*48, 48)
        kid = struct.unpack_from('<H', raw, 0)[0]; ex = struct.unpack_from('<3H', raw, 2); ptrs = struct.unpack_from('<5Q', raw, 8)
        rows.append({'id': kid, 'extra': ex, 'fn': [hex(p) if p else None for p in ptrs]})
    data[tn] = rows
    comp = collections.Counter(cat(r['id']) for r in rows)
    tuples = collections.Counter(tuple(r['fn']) for r in rows)
    print(tn, dict(comp), '| distinct tuples', len(tuples), '| top shared tuples', [n for t, n in tuples.most_common(5)])
    print('   extras nonzero:', sum(1 for r in rows if any(r['extra'])), ' sorted by id:', all(rows[i]['id'] <= rows[i+1]['id'] for i in range(len(rows)-1)))
json.dump(data, open(r'C:\Users\Johnathon\Desktop\New folder\effect_tables_raw.json', 'w'))
