"""
Catalogues Master Duel's effect step functions that have no twin in the game (docs/MasterDuelStepFunctions.md): for each, the cards whose
effect rows use it (table, slot), its size, and the card ids its own code compares against (a card-specific branch).

    python md_function_catalogue.py <duel.dll> <md_rows.json (md_effect_rows.py)> <ygoprodeck cardinfo.json> <out .md> <out .json>

A step function is GENERIC (a part BOB / BAB could offer) when its own code compares no card id and two or more cards use it; one that compares
ids is CARD-SPECIFIC (it may still be generic for ids it does not list - IDA decides that, function by function); one used by a single card
is SINGLE. Heuristic: the scan reads the function's own bytes only (its callees can still branch on ids) and counts an id when it is the
immediate of a compare (cmp eax / r32 / r16, imm) or a mov of an immediate into a register, and the value is a card id Master Duel has.
Function bounds are the x64 unwind table's (.pdata), so they are exact. Nothing from duel.dll is copied out but addresses and counts.
"""
import json
import struct
import sys
from collections import defaultdict
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from md_effect_rows import PE, TABLES  # noqa: E402

SLOTS = ['action (slot 0)', 'target (slot 1)', 'condition (slot 2)', 'cost (slot 3)', 'prompt / pick (slot 4)']


def function_bounds(pe):
    """RUNTIME_FUNCTION entries of .pdata: begin RVA -> end RVA."""
    pdata = next((s for s in pe.sections if s[0] == '.pdata'), None)
    bounds = {}
    if not pdata:
        return bounds
    name, size, va, raw = pdata
    for o in range(raw, raw + size - 11, 12):
        begin, end, _ = struct.unpack_from('<III', pe.data, o)
        if begin and end > begin:
            bounds.setdefault(begin, end)
    return bounds


def card_ids_in(code, card_ids):
    """Card ids compared against / loaded as immediates in a function's bytes."""
    found = set()
    n = len(code)
    for i in range(n - 5):
        b = code[i]
        imm = None
        if b == 0x3D:                                              # cmp eax, imm32
            imm = struct.unpack_from('<I', code, i + 1)[0]
        elif b == 0x81 and 0xF8 <= code[i + 1] <= 0xFF and i + 6 <= n:   # cmp r32, imm32 (also after a REX byte)
            imm = struct.unpack_from('<I', code, i + 2)[0]
        elif b == 0x66 and code[i + 1] == 0x81 and 0xF8 <= code[i + 2] <= 0xFF and i + 5 <= n:   # cmp r16, imm16
            imm = struct.unpack_from('<H', code, i + 3)[0]
        elif b == 0x66 and code[i + 1] == 0x3D:                    # cmp ax, imm16
            imm = struct.unpack_from('<H', code, i + 2)[0]
        elif 0xB8 <= b <= 0xBF:                                    # mov r32, imm32
            imm = struct.unpack_from('<I', code, i + 1)[0]
        if imm is not None and imm in card_ids:
            found.add(imm)
    return found


def main(dll_path, rows_path, cardinfo_path, out_md, out_json):
    pe = PE(dll_path)
    rows = json.loads(Path(rows_path).read_text(encoding='utf-8'))
    names = {}
    for card in json.loads(Path(cardinfo_path).read_text(encoding='utf-8'))['data']:
        kid = (card.get('misc_info') or [{}])[0].get('konami_id')
        if kid:
            names.setdefault(int(kid), card['name'])

    # every id with a row in any of Master Duel's tables = the card ids its code can name
    card_ids = set()
    for table, (_, md_start) in TABLES.items():
        for row in pe.table(md_start):
            card_ids.add(row[0])
    card_ids.discard(3000)

    missing = {int(k, 16): v for k, v in rows['missingFunctions'].items()}
    users = defaultdict(list)   # fn -> [(konami id, name, table, slot)]
    for card in rows['cards']:
        for row in card['rows']:
            for slot, md in enumerate(row['md']):
                if md and int(md, 16) in missing:
                    users[int(md, 16)].append((card['konamiId'], card['name'], row['table'], slot))

    bounds = function_bounds(pe)
    entries = []
    for fn in sorted(missing, key=lambda f: -len({u[0] for u in users[f]})):
        rva = fn - pe.base
        end = bounds.get(rva)
        size = end - rva if end else 0
        code = pe.data[pe.offset(fn):pe.offset(fn) + size] if size else b''
        ids = sorted(card_ids_in(code, card_ids))
        cards = sorted({(u[0], u[1]) for u in users[fn]})
        slots = sorted({(u[2], u[3]) for u in users[fn]})
        user_ids = {c[0] for c in cards}
        verdict = 'generic' if not ids and len(cards) >= 2 else 'single' if len(cards) < 2 else 'card-specific'
        entries.append({
            'function': hex(fn), 'size': size, 'cards': len(cards), 'verdict': verdict,
            'places': [f'{t} {SLOTS[s]}' for t, s in slots],
            'comparesIds': ids, 'comparesOwnUsers': sorted(user_ids & set(ids)),
            'users': [{'konamiId': k, 'name': n} for k, n in cards],
        })

    Path(out_json).write_text(json.dumps(entries, indent=1, ensure_ascii=False), encoding='utf-8')
    counts = defaultdict(int)
    for e in entries:
        counts[e['verdict']] += 1
    lines = [
        '# Master Duel step functions the game does not have',
        '',
        'Made by `Tools/Scripts/md_function_catalogue.py` from duel.dll (build 80460 : 20260629) and `md_rows.json` (Tools/Scripts/md_effect_rows.py).',
        'These are the slot functions of Master Duel\'s effect rows (docs/EffectSystem.md: 48-byte rows, five slots) that no card the two games share',
        'pairs with a game function: the mechanics Konami added after the game. Each is listed with the post-game cards whose rows use it (the',
        'Cards mod\'s cards), where (table and slot), its size, and the card ids its own code compares against.',
        '',
        f'**{len(entries)} functions: {counts["generic"]} generic** (two or more cards, no card id in its own code - candidates for BOB / BAB parts),',
        f'**{counts["card-specific"]} card-specific** (compare card ids: a part only after IDA shows the path for an id they do not list),',
        f'**{counts["single"]} single** (one card uses it: that card\'s own machine).',
        '',
        'Heuristic: only the function\'s own bytes are read (a callee can still branch on ids); "compares" counts cmp / mov immediates that are a',
        'card id Master Duel has. Until each one is traced in IDA, BOB / BAB reach every one of these through as(<card>) - the card plays as the',
        'source under Master Duel\'s engine (Core_EngineLend), so all of them, card-specific ones included, already run for a custom card.',
        '',
        '| function | verdict | cards | size | where | ids its code compares | cards using it (first 6) |',
        '|---|---|---|---|---|---|---|',
    ]
    for e in entries:
        sample = ', '.join(f'{u["name"]} {u["konamiId"]}' for u in e['users'][:6]) + (' ...' if e['cards'] > 6 else '')
        compared = ', '.join(f'{i} {names.get(i, "")}'.strip() for i in e['comparesIds'][:5]) + (' ...' if len(e['comparesIds']) > 5 else '')
        lines.append(f'| `{e["function"]}` | {e["verdict"]} | {e["cards"]} | {e["size"]} | {"; ".join(e["places"])} | {compared} | {sample} |')
    Path(out_md).write_text('\n'.join(lines) + '\n', encoding='utf-8', newline='\r\n')
    print(f'{len(entries)} functions: ' + ', '.join(f'{k} {v}' for k, v in counts.items()))


if __name__ == '__main__':
    if len(sys.argv) != 6:
        sys.exit(__doc__)
    main(*sys.argv[1:])
