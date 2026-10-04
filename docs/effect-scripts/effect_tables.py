import pefile, struct, json, collections, sys, os
sys.stdout.reconfigure(encoding='utf-8')
EXE = r'C:\Users\Johnathon\source\repos\Yu-Gi-Oh-Ex\YuGiOh.exe'
if not os.path.exists(EXE): EXE = r'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOh.exe'
pe = pefile.PE(EXE, fast_load=True); base = pe.OPTIONAL_HEADER.ImageBase
def rd(va, n): return pe.get_data(va - base, n)
game = json.load(open(r'C:\Users\Johnathon\Desktop\New folder\Cards\Game Cards.json', encoding='utf-8-sig')); game = game['cards'] if isinstance(game, dict) else game
name = {c['id']: c['name'] for c in game}
def table(va, count):
    out = []
    for i in range(count):
        raw = rd(va + i*48, 48)
        kid = struct.unpack_from('<H', raw, 0)[0]
        extra = struct.unpack_from('<3H', raw, 2)
        ptrs = struct.unpack_from('<5Q', raw, 8)
        out.append((kid, extra, ptrs))
    return out
tables = {'T1@140B38290': table(0x140B38290, 3123), 'T2@140B5CC20': table(0x140B5CC20, 3489), 'T5@140BA8730': table(0x140BA8730, 2831)}
for tn, t in tables.items():
    ids = [e[0] for e in t]
    print(tn, 'entries', len(t), 'distinct ids', len(set(ids)), 'id range', min(ids), max(ids), 'first ids', ids[:6])
    for pid in (4844, 4343, 4342):
        for e in t:
            if e[0] == pid: print('   ', pid, name.get(pid), e[1], [hex(p) for p in e[2]])
    fc = collections.Counter(p for e in t for p in e[2] if p)
    print('   distinct non-null function pointers', len(fc), ' most shared:', [(hex(p), n) for p, n in fc.most_common(6)])

print()
info = {c['id']: c for c in game}
def cat(c):
    k = c['kind']
    if k in ('Spell', 'Trap'): return k
    return 'Monster'
for tn, t in tables.items():
    comp = collections.Counter(cat(info[e[0]]) if e[0] in info else 'unknown(id %s)' % ('>=14969' if e[0] >= 14969 else '<3900' if e[0] < 3900 else '?') for e in t)
    slots = [sum(1 for e in t if e[2][s]) for s in range(5)]
    print(tn, dict(comp), 'non-null per slot', slots)
def show(tn, cid):
    for e in tables[tn]:
        if e[0] == cid: print('  ', tn[:2], cid, name.get(cid), info[cid]['kind'] if cid in info else '', e[1], [hex(p) if p else '-' for p in e[2]])
print()
for n in ('Blue-Eyes White Dragon', 'Summoned Skull', 'Kuriboh', 'Man-Eater Bug', 'Mirror Force', 'Trap Hole', 'Graceful Charity', 'Monster Reborn', 'Polymerization', 'Mystical Space Typhoon'):
    cid = next(c['id'] for c in game if c['name'] == n)
    print(n, cid, info[cid]['kind'])
    for tn in tables: show(tn, cid)
