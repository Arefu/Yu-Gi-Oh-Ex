r"""Builds summon_tables.json: the game's own summoning requirements, so WolfX's Card Manager (tab "Summoning") can show what a game card
needs before it is changed (the changes go to Yu-Gi-Oh-Ex\summoning.json, read by Yu-Gi-Oh-Effects).

  python build_summon_tables.py [output.json]      (default: Tools\WolfX\Content\summon_tables.json)

Tables (docs/EffectSystem.md sections 36, 40, 41):
  FUSION_RECIPES_2_3         0x140ACCAD0  320 x {u16 fusion, u16 material x3}
  FUSION_RECIPES_4_5         0x140ACD4D0   11 x {u16 fusion, u16 material x5}
  RitualMonsterSpellTable    0x140AD07C0   96 x {u16 monster, u16 spell}
  XyzSummonRequirements      0x140ACF2E0  219 x {i16 xyz, i16 material code, i16 materials}
  SynchroSummonRequirements  0x140AD0BA0  194 x {i16 synchro, i16 tuner code, i16 non-tuner code, i16 materials (<0 = exactly)}
"""
import json, os, struct, sys
import pefile

EXE = r'C:\Users\Johnathon\source\repos\Yu-Gi-Oh-Ex\YuGiOh.exe'
if not os.path.exists(EXE):
    EXE = r'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOh.exe'
OUT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(__file__), '..', '..', 'Tools', 'WolfX', 'Content', 'summon_tables.json')

pe = pefile.PE(EXE, fast_load=True)
base = pe.OPTIONAL_HEADER.ImageBase


def rows(va, count, fmt):
    size = struct.calcsize(fmt)
    data = pe.get_data(va - base, count * size)
    return [struct.unpack_from(fmt, data, i * size) for i in range(count)]


fusion = {}
for row in rows(0x140ACCAD0, 320, '<4H') + rows(0x140ACD4D0, 11, '<6H'):
    if row[0]:
        fusion[str(row[0])] = [m for m in row[1:] if m]
ritual = [[m, s] for m, s in rows(0x140AD07C0, 96, '<2H') if m]
xyz = {str(i): {'material': c, 'materials': n} for i, c, n in rows(0x140ACF2E0, 219, '<3h')}
synchro = {str(i): {'tuner': t, 'nonTuner': nt, 'materials': abs(n), 'exactly': n < 0} for i, t, nt, n in rows(0x140AD0BA0, 194, '<4h')}

with open(OUT, 'w', encoding='utf-8', newline='\n') as f:
    json.dump({'fusion': fusion, 'ritual': ritual, 'xyz': xyz, 'synchro': synchro}, f, indent=1)
print(f'{OUT}: {len(fusion)} fusion, {len(ritual)} ritual, {len(xyz)} xyz, {len(synchro)} synchro')
