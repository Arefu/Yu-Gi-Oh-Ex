import pefile, capstone, struct, re, json, collections, sys, bisect
sys.stdout.reconfigure(encoding='utf-8')
EXE = r'C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution\YuGiOh.exe'
pe = pefile.PE(EXE); base = pe.OPTIONAL_HEADER.ImageBase
text=[s for s in pe.sections if s.Name.startswith(b'.text')][0]; code=text.get_data(); tva=base+text.VirtualAddress
md=capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64); md.detail=False
g=json.load(open(r'C:\Users\Johnathon\Desktop\New folder\Cards\Game Cards.json',encoding='utf-8-sig')); g=g['cards'] if isinstance(g,dict) else g
cards={x['id']:x for x in g}
# function bounds from .pdata
funcs=sorted({(base+e.struct.BeginAddress, base+e.struct.EndAddress) for e in pe.DIRECTORY_ENTRY_EXCEPTION})
starts=[f[0] for f in funcs]
def func_of(va):
    i=bisect.bisect_right(starts,va)-1
    return funcs[i] if i>=0 and funcs[i][0]<=va<funcs[i][1] else (va-0x400,va+0x10)
def rd(va,n): return pe.get_data(va-base,n)
def insns(s,e): return list(md.disasm(code[s-tva:e-tva], s))
OFFER=0x1400AAE50; ZONE=0x1400AAF90; TRY=0x1400AB050
NAMED=[0x1400AB360,0x1400AB5E0,0x1400AB750,0x1400AB8C0,0x1400ABA70,0x1400ABF60,0x1400ABE00,0x1400ABC30]
targets={OFFER:'offer',ZONE:'zone',TRY:'try',**{a:'named' for a in NAMED}}
sites=[]
for i in range(len(code)-5):
    if code[i]==0xE8:
        d=tva+i+5+struct.unpack_from('<i',code,i+1)[0]
        if d in targets: sites.append((tva+i,targets[d]))
def back(va,n=14):
    for b in (90,70,60,50,40):
        ins=insns(va-b,va+5)
        if ins and ins[-1].address==va: return ins[-n-1:-1]
    return []
res=collections.defaultdict(list)
for va,kind in sites:
    pre=back(va); ev=None
    if kind in ('zone','named'):
        for x in reversed(pre):
            m=re.match(r'(r8d|r8b|r8w), (0x[0-9a-f]+|\d+)$',x.op_str)
            if x.mnemonic=='mov' and m: ev=int(m.group(2),0); break
            if x.op_str.startswith(('r8d,','r8b,','r8w,')): ev='dyn'; break
    else:
        for x in reversed(pre):
            for m in re.findall(r'0x[0-9a-f]+',x.op_str):
                v=int(m,16)
                if x.mnemonic=='or' and v>=0x10000 and v<0x100000000 and (v>>25)&0x3F and not (0x140000000<=v):
                    ev=(v>>25)&0x3F; break
                if x.mnemonic=='or' and 0x100<=v<0x10000 and v&0x20|v&0x40:  # high-word form, shifted later
                    ev=(v>>9)&0x3F; break
            if ev is not None: break
        if ev is None: ev="dyn"
    res[ev if ev is not None else "dyn"].append((va,kind))
# per event: functions, and card ids referenced (immediates) in those functions
def ids_in(fs,fe):
    out=set()
    for x in insns(fs,min(fe,fs+0x6000)):
        for m in re.findall(r'0x[0-9a-f]+',x.op_str):
            v=int(m,16)
            if 3900<=v<=14968 and v in cards and x.mnemonic in ('cmp','mov','sub','lea'): out.add(v)
    return out
for ev in sorted(res,key=lambda k:(isinstance(k,str),k)):
    fs=sorted({func_of(va) for va,_ in res[ev]})
    print(f'=== event {ev}: {len(res[ev])} site(s), {len(fs)} function(s)')
    for f in fs[:8]:
        ids=ids_in(*f)
        smp=sorted(ids)[:4]
        print(f'   fn {f[0]:X} size {f[1]-f[0]:X} kinds {collections.Counter(k for v,k in res[ev] if f[0]<=v<f[1])} ids {len(ids)}: ' + ' / '.join(f"{i} {cards[i]['name']}: {(cards[i].get('description') or '')[:70]}" for i in smp))
