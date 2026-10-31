"""
Finds a YuGiOh.exe function's twin in Master Duel's duel.dll by the card ids both compare: the id-laden rule checks and trigger evaluators
(Jinzo, Skill Drain, "cannot be targeted" ...) keep most of their card ids in Master Duel, so the duel.dll function sharing the most of
them is the twin (docs/Overnight-2026-10-30.md, twin table). A candidate, not proof: decompile both before naming it.

    python md_twin_by_ids.py <duel.dll> <YuGiOh.exe> <game function address> [...]

Function bounds come from each binary's .pdata (x64 unwind table); ids are every 32-bit value in the card id range found in the function's
bytes (compares, switch bases, table entries), which is noisy alone but the overlap ranks well (the twin typically shares 80%+ and the next
best a quarter).
"""
import struct
import sys
from collections import Counter

from md_effect_rows import PE

ID_FIRST, ID_LAST = 3900, 30000


def functions(pe):
    """(start, end) of every function in .pdata."""
    pdata = next(((va, size, raw) for name, size, va, raw in pe.sections if name == '.pdata'), None)
    if pdata is None:
        return []
    va, size, raw = pdata
    result = []
    for o in range(raw, raw + size - 11, 12):
        begin, end, _ = struct.unpack_from('<III', pe.data, o)
        if begin and end > begin:
            result.append((pe.base + begin, pe.base + end))
    return result


def ids_in(pe, start, end):
    try:
        o = pe.offset(start)
        e = pe.offset(end - 1) + 1
    except ValueError:
        return set()
    data = pe.data[o:e]
    return {v for i in range(len(data) - 3)
            if ID_FIRST <= (v := struct.unpack_from('<I', data, i)[0]) <= ID_LAST}


def main(dll_path, exe_path, targets):
    md, game = PE(dll_path), PE(exe_path)
    game_bounds = dict(functions(game))
    md_ids = [(s, ids_in(md, s, e)) for s, e in functions(md)]
    owner = {}
    for s, ids in md_ids:
        for v in ids:
            owner.setdefault(v, []).append(s)
    for target in targets:
        end = game_bounds.get(target)
        if end is None:
            print(f'{target:#x}: not a function start in .pdata')
            continue
        ids = ids_in(game, target, end)
        votes = Counter(s for v in ids for s in owner.get(v, ()) if len(owner[v]) < 200)
        best = votes.most_common(3)
        shown = ', '.join(f'{s:#x} ({n})' for s, n in best) or 'none'
        print(f'{target:#x}: {len(ids)} ids -> {shown}')


if __name__ == '__main__':
    if len(sys.argv) < 4:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2], [int(a, 16) for a in sys.argv[3:]])
