# Name members of a local struct (splitting wider placeholder members) with idalib.
# usage: python struct_members.py <idb> <spec.json>
# spec: {"struct": "Duel::PlayerState", "base": "0x143497C40",
#        "members": [{"insn": "0x1400D51AD", "size": 4, "type": "int", "name": "X", "cmt": "..."} |
#                    {"offset": "0x388E", ...}]}
import json, sys
import idapro

idb, specPath = sys.argv[1], sys.argv[2]
spec = json.load(open(specPath))
assert idapro.open_database(idb, False) == 0, "open failed"
import ida_typeinf, ida_ua, idc

base = int(spec["base"], 16)
tif = ida_typeinf.tinfo_t()
assert tif.get_named_type(None, spec["struct"]), "struct not found"
udt = ida_typeinf.udt_type_data_t()
assert tif.get_udt_details(udt)
totalBytes = tif.get_size()

def target_offset(m):
    if "offset" in m:
        return int(m["offset"], 16)
    insn = ida_ua.insn_t()
    ea = int(m["insn"], 16)
    assert ida_ua.decode_insn(insn, ea) > 0, hex(ea)
    for op in insn.ops:
        if op.type == ida_ua.o_mem:
            return op.addr - base
    raise SystemExit("no memory operand at " + hex(ea))

def filler(offBytes, sizeBytes):
    u = ida_typeinf.udm_t()
    u.name = "field_%X" % offBytes
    t = ida_typeinf.tinfo_t()
    ida_typeinf.parse_decl(t, None, "unsigned __int8 x[%d];" % sizeBytes, ida_typeinf.PT_SIL)
    u.type = t
    u.offset = offBytes * 8
    u.size = sizeBytes * 8
    return u

members = [udt[i] for i in range(udt.size())]
for m in spec["members"]:
    off = target_offset(m)
    size = m["size"]
    t = ida_typeinf.tinfo_t()
    decl = m["type"].replace("[", " x[", 1) + ";" if "[" in m["type"] else m["type"] + " x;"
    assert ida_typeinf.parse_decl(t, None, decl, ida_typeinf.PT_SIL), m["type"]
    assert t.get_size() == size, (m["name"], t.get_size(), size)
    lo, hi = off * 8, (off + size) * 8
    kept = []
    for u in members:
        uLo, uHi = u.offset, u.offset + u.size
        if uHi <= lo or uLo >= hi:
            kept.append(u)
            continue
        # a named (non field_) member in the way means the spec is wrong
        if not u.name.startswith("field_") and u.name != m["name"]:
            raise SystemExit("%s overlaps named member %s at 0x%X" % (m["name"], u.name, uLo // 8))
        if uLo < lo:
            kept.append(filler(uLo // 8, (lo - uLo) // 8))
        if uHi > hi:
            kept.append(filler(hi // 8, (uHi - hi) // 8))
    n = ida_typeinf.udm_t()
    n.name = m["name"]
    n.type = t
    n.offset = lo
    n.size = size * 8
    if m.get("cmt"):
        n.cmt = m["cmt"]
    kept.append(n)
    members = sorted(kept, key=lambda u: u.offset)
    print("0x%04X %-28s %s" % (off, m["name"], m["type"]))

out = ida_typeinf.udt_type_data_t()
out.taudt_bits = udt.taudt_bits
out.pack = udt.pack
out.total_size = udt.total_size
out.unpadded_size = udt.unpadded_size
for u in members:
    out.push_back(u)
ntif = ida_typeinf.tinfo_t()
assert ntif.create_udt(out, ida_typeinf.BTF_STRUCT)
assert ntif.get_size() == totalBytes, (ntif.get_size(), totalBytes)
assert ntif.set_named_type(None, spec["struct"], ida_typeinf.NTF_REPLACE) == ida_typeinf.TERR_OK
idapro.close_database(True)
print("saved,", totalBytes, "bytes")
