"""Decodes the game's "related cards" data (bin/tagdata.bin + bin/taginfo_<lang>.bin + bin/CARD_IntID.bin) to related_cards.json.

tagdata.bin: 10166 (start, count) u32 pairs, one per INTERNAL card id, then a pool of u32 units; a card's list is pool[start:start+count]
(the slot after it is a 0 terminator). Each unit is (u16 related Konami id, u16 tag id). tag id indexes taginfo_<lang>.bin, whose 0x34-byte
records (count u16 at 0, records from offset 4) hold a filter key (u64 pointer at +0x24) and its display text (+0x2c), e.g. {AD}TYPE:SOLDIER
"Affects: [TYPE:SOLDIER icon]". Read: card X is related to card `a` because a's effect tag `b` matches X (Warrior = SOLDIER in the game's words).
Usage: python DecodeRelated.py [bin folder] [Game Cards.json]
"""
import struct, sys, json
sys.stdout.reconfigure(encoding='utf-8')
binf = sys.argv[1] if len(sys.argv) > 1 else r'C:\Users\Johnathon\source\repos\Yu-Gi-Oh-Ex\Binaries\Debug\Tools\YGO_2020\bin'
gamef = sys.argv[2] if len(sys.argv) > 2 else r'.\Cards\Game Cards.json'
d = open(binf + r'\tagdata.bin', 'rb').read(); N = 10166; pool0 = 8 * N
pairs = [struct.unpack_from('<II', d, 8 * i) for i in range(N)]
info = open(binf + r'\taginfo_E.bin', 'rb').read(); T = struct.unpack_from('<H', info, 0)[0]
def s16(off):
    out = []
    while off + 1 < len(info):
        c = struct.unpack_from('<H', info, off)[0]
        if c == 0: break
        out.append(chr(c)); off += 2
    return ''.join(out)
tags = []
for i in range(T):
    rec = info[4 + i * 0x34: 4 + (i + 1) * 0x34]
    tags.append((s16(struct.unpack_from('<Q', rec, 0x24)[0]), s16(struct.unpack_from('<Q', rec, 0x2c)[0])))
iid = struct.unpack('<%dH' % (22138 // 2), open(binf + r'\CARD_IntID.bin', 'rb').read())
konami_of = {v: k + 3900 for k, v in enumerate(iid) if v}
game = json.load(open(gamef, encoding='utf-8-sig')); game = game['cards'] if isinstance(game, dict) else game
name = {c['id']: c['name'] for c in game}
out = {}
for internal in range(N):
    k = konami_of.get(internal)
    s, c = pairs[internal]
    if not k or not c: continue
    units = struct.unpack_from('<%dI' % c, d, pool0 + 4 * s)
    out[str(k)] = {'name': name.get(k), 'related': [{'konamiId': u & 0xFFFF, 'name': name.get(u & 0xFFFF), 'tag': tags[u >> 16][0], 'text': tags[u >> 16][1]} for u in units]}
json.dump({'note': 'card -> related cards, decoded from tagdata.bin. See DecodeRelated.py.', 'cards': out}, open('related_cards.json', 'w', encoding='utf-8'), ensure_ascii=False)
print(len(out), 'cards with related lists,', sum(len(v['related']) for v in out.values()), 'links')
