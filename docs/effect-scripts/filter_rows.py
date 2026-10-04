# Dumps the generic filter-row table 0x140B16220 (12-byte rows: id, param, flagsExtra, flags) with decoded flag bits and the card text.
import pefile, struct, json, sys, os, collections
sys.stdout.reconfigure(encoding='utf-8')
EXE = r'C:\Users\Johnathon\source\repos\Yu-Gi-Oh-Ex\YuGiOh.exe'
if not os.path.exists(EXE): EXE = r'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOh.exe'
pe = pefile.PE(EXE, fast_load=True); base = pe.OPTIONAL_HEADER.ImageBase
game = json.load(open(r'C:\Users\Johnathon\Desktop\New folder\Cards\Game Cards.json', encoding='utf-8-sig')); game = game['cards'] if isinstance(game, dict) else game
card = {c['id']: c for c in game}
SIDE = {0: 'any-side', 1: 'own', 2: 'opp', 3: 'either+zonecheck'}
ZONE = {0: '', 0x10: 'zone<7', 0x20: 'zone20', 0x30: 'zone30', 0x40: 'zone40', 0x50: 'zone7-11', 0x60: 'zone60'}
BITS = {0x4: 'occupied', 0x8: 'empty', 0x80: 'b2==0', 0x100: 'b2!=0', 0x400: 'f400', 0x800: 'f800', 0x1000: 'f1000', 0x2000: 'f2000',
        0x4000: 'f4000', 0x8000: 'f8000', 0x10000: 'f10000', 0x20000: 'f20000', 0x40000: 'notSelf', 0x80000: 'f80000', 0x100000: 'f100000',
        0x200000: 'f200000', 0x400000: 'targeted', 0x800000: 'f800000', 0x1000000: 'f1000000', 0x2000000: 'f2000000', 0x4000000: 'f4000000'}
PARAM = {0: '', 1: 'attr/type=param', 2: 'type(dup)', 3: 'level bit', 4: 'special', 5: 'atk<=', 6: 'atk>='}
def dec(f):
    out = [SIDE[f & 3], ZONE.get(f & 0x70, hex(f & 0x70))]
    out += [n for b, n in BITS.items() if f & b]
    p = (f >> 28) & 7
    out.append({0: '', 1: 'P:tag/lookup', 2: 'P:kindtable', 3: 'P:bit(level?)', 4: 'P:bitmask', 5: 'P:atk<=param', 6: 'P:atk>=param', 7: 'P:special#param'}[p])
    return ' '.join(x for x in out if x)
def rd(va, n): return pe.get_data(va - base, n)
rows = [struct.unpack('<HHII', rd(0x140B16220 + i * 12, 12)) for i in range(1637)]
byid = collections.defaultdict(list)
for r in rows: byid[r[0]].append(r)
print(len(rows), 'rows,', len(byid), 'cards')
want = [int(a) for a in sys.argv[1:]] or list(byid)[:40]
for cid in want:
    c = card.get(cid, {})
    print('\n%d %s' % (cid, c.get('name')))
    print('   ', (c.get('desc') or c.get('description') or '')[:200].replace('\n', ' '))
    for r in byid.get(cid, []):
        print('    param=%d extra=%08X flags=%08X  %s' % (r[1], r[2], r[3], dec(r[3])))
