import pefile, struct, sys, json, collections
sys.stdout.reconfigure(encoding='utf-8')
EXE = "C:/Program Files (x86)/Steam/steamapps/common/Yu-Gi-Oh! Legacy of the Duelist Link Evolution/YuGiOh.exe"
pe = pefile.PE(EXE, fast_load=True); base = pe.OPTIONAL_HEADER.ImageBase
text = [(s.VirtualAddress + base, s.VirtualAddress + base + s.Misc_VirtualSize) for s in pe.sections if s.Name.startswith(b'.text')][0]
found = []
for sec in pe.sections:
    name = sec.Name.rstrip(b'\0').decode()
    if name not in ('.rdata', '.data'): continue
    data = sec.get_data(); start = sec.VirtualAddress + base
    n = len(data)
    for stride in (4, 6, 8, 12, 16, 20, 24, 32, 40, 48, 56, 64):
        i = 0
        while i + stride < n:
            # try a run starting at i (aligned to 2)
            v = struct.unpack_from('<H', data, i)[0]
            if not (3400 <= v <= 15300): i += 2; continue
            j = i; count = 1; prev = v
            while j + stride + 2 <= n:
                nv = struct.unpack_from('<H', data, j + stride)[0]
                if 3000 <= nv <= 15300 and nv >= prev: prev = nv; count += 1; j += stride
                else: break
            if count >= 80:
                found.append((start + i, stride, count, name)); i = j + stride
            else: i += 2
# dedupe: keep the longest per start region
found.sort(key=lambda t: (t[0], -t[2]))
res = []; last_end = 0
for a, s, c, nm in sorted(found, key=lambda t: -t[2]):
    if any(a < ra + rc*rs and ra < a + c*s for ra, rs, rc, _ in res): continue
    res.append((a, s, c, nm))
res.sort()
print(len(res), 'id-keyed tables (>=80 rows, ids sorted)')
def fnptrs(a, s, c):
    hits = 0; tot = 0
    for r in range(min(c, 60)):
        raw = pe.get_data(a - base + r*s, s)
        for k in range(0, s - 7, 8):
            q = struct.unpack_from('<Q', raw, k)[0]
            if text[0] <= q < text[1]: hits += 1
            tot += 1
    return hits
for a, s, c, nm in res:
    print(f'0x{a:X} stride {s:2} rows {c:5} {nm:6} fn-pointer fields sampled: {fnptrs(a, s, c)}')
json.dump([{'addr': hex(a), 'stride': s, 'rows': c} for a, s, c, _ in res], open(r'C:\Users\Johnathon\Desktop\New folder\id_tables.json', 'w'))
