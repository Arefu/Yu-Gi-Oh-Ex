# Dumps the deck filter table 0x140AD2A80 (24-byte rows: u16 id, ..., u64 enumerate fn at +8) - the per-card "which Deck cards can I search" functions.
import pefile, struct, json, sys, os, collections
sys.stdout.reconfigure(encoding='utf-8')
EXE = r'C:\Users\Johnathon\source\repos\Yu-Gi-Oh-Ex\YuGiOh.exe'
if not os.path.exists(EXE): EXE = r'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOh.exe'
pe = pefile.PE(EXE, fast_load=True); base = pe.OPTIONAL_HEADER.ImageBase
game = json.load(open(r'C:\Users\Johnathon\Desktop\New folder\Cards\Game Cards.json', encoding='utf-8-sig')); game = game['cards'] if isinstance(game, dict) else game
nm = {c['id']: c['name'] for c in game}
def rd(va, n): return pe.get_data(va - base, n)
rows = []
i = 0
prev = 0
while True:
    raw = rd(0x140AD2A80 + i * 24, 24)
    cid, a, b, c, fn = struct.unpack_from('<HHIIQ', raw, 0)[0], 0, 0, 0, 0
    cid = struct.unpack_from('<H', raw, 0)[0]
    fn = struct.unpack_from('<Q', raw, 8)[0]
    if cid < prev or not (0x140000000 <= fn < 0x145000000): break
    prev = cid
    rows.append((cid, struct.unpack_from('<H', raw, 2)[0], struct.unpack_from('<I', raw, 4)[0], fn, struct.unpack_from('<Q', raw, 16)[0]))
    i += 1
print(len(rows), 'rows')
fc = collections.Counter(r[3] for r in rows)
print('distinct enumerate fns', len(fc))
for fn, n in fc.most_common(12): print(hex(fn), n, [nm.get(r[0], r[0]) for r in rows if r[3] == fn][:3])
for r in rows[:8]: print(r[0], nm.get(r[0]), r[1], hex(r[2]), hex(r[3]), hex(r[4]))
