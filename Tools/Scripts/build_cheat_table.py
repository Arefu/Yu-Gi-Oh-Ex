"""Builds YuGiOh.CT (Cheat Engine table) from what the project has mapped.

Sources: Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-DUELSTATE.h (duel layout), the IDB's named globals, Dependencies/Yu-Gi-Oh-Ex/Cards.h
(card names for the card id dropdown). The hand-made table's Screen Status entry and its CheatCodes are carried over from the old file.
Run from the repo root:  python Tools/Scripts/build_cheat_table.py
"""
import re
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape

ROOT = __file__.replace("\\", "/").rsplit("/Tools/", 1)[0] + "/"
OUT = ROOT + "YuGiOh.CT"

old = ET.parse(OUT).getroot()
_ids = iter(range(1000, 100000))


def entry(desc, addr=None, vtype=None, children=None, hex_=False, signed=False, dropdown=None, link=None, bits=None, collapsed=True,
          header=False, script=None):
    x = [f"<CheatEntry><ID>{next(_ids)}</ID><Description>\"{escape(desc)}\"</Description>"]
    if header or (children is not None and addr is None):
        x.append('<Options moHideChildren="1"/>' if collapsed else "")
        x.append("<GroupHeader>1</GroupHeader>")
    if script:
        x.append(f"<VariableType>Auto Assembler Script</VariableType><AssemblerScript>{escape(script)}</AssemblerScript>")
    if dropdown:
        x.append(f'<DropDownList DisplayValueAsItem="1">{escape(dropdown)}</DropDownList>')
    if link:
        x.append(f'<DropDownListLink>{escape(link)}</DropDownListLink>')
    if hex_:
        x.append("<ShowAsHex>1</ShowAsHex>")
    if vtype:
        x.append(f"<ShowAsSigned>{1 if signed else 0}</ShowAsSigned><VariableType>{vtype}</VariableType>")
        if bits:
            x.append(f"<BitStart>{bits[0]}</BitStart><BitLength>{bits[1]}</BitLength><ShowAsBinary>0</ShowAsBinary>")
    if addr is not None:
        x.append(f"<Address>{addr}</Address>")
    if children:
        x.append("<CheatEntries>" + "".join(children) + "</CheatEntries>")
    x.append("</CheatEntry>")
    return "".join(x)


def group(desc, children, collapsed=True):
    return entry(desc, children=children, collapsed=collapsed)


def h(n):
    return f"{n:X}"


# ---- card names (Cards.h: Name = 0xID,) -> one shared dropdown everything links to
cards = []
for name, value in re.findall(r"^\s*(\w+)\s*=\s*(0x[0-9A-Fa-f]+|\d+)\s*,", open(ROOT + "Dependencies/Yu-Gi-Oh-Ex/Cards.h", encoding="utf-8").read(), re.M):
    cards.append((int(value, 0), name.replace("_", " ")))
CARD_LIST = "Card names (dropdown source)"
card_dropdown = "0:(empty)\n" + "\n".join(f"{i}:{n}" for i, n in sorted(set(cards)))

BOOL = "0:No\n1:Yes"
SIDE = "0:Side 1 (seat 0/2)\n1:Side 2 (seat 1/3)"
POSITION = "1:1 (named FDD, unconfirmed)\n2:Face-down attack\n4:Face-up defense\n8:Face-down defense"
STANCE_OLD = "0:Face Down Attack\n1:Face Down Defense\n256:Attack\n257:Defense"
WIN_REASON_NOTE = "see g_DuelWinReasonText / DuelWinReason in the IDB"

PLAYER_STATE, BLOCK = 0x143497C40, 0xD94
ENGINE = 0x143330280
PILES = [("Hand", 0x0C, 0x19C, 120), ("Deck", 0x10, 0x37C, 120), ("Graveyard", 0x14, 0x7B4, 149), ("Extra Deck", 0x18, 0x55C, 150),
         ("Banished", 0x1C, 0xA0C, 226)]


def card_word(desc, addr):
    # packed card word: low 14 bits = card id; the whole word is shown as hex below it
    return entry(desc, addr, "Binary", bits=(0, 14), link=CARD_LIST,
                 children=[entry("Raw word (bit 14 + bits 23-30 = instance)", addr, "4 Bytes", hex_=True)])


def zone(desc, addr):
    return entry(desc, addr, "2 Bytes", link=CARD_LIST, children=[
        entry("Internal id", h(addr + 2), "2 Bytes", hex_=True),
        entry("Unknown +4", h(addr + 4), "2 Bytes"),
        entry("Stance (old table, +6)", h(addr + 6), "2 Bytes", dropdown=STANCE_OLD),
        entry("Position (Duel::CardPosition, +0x0C)", h(addr + 0x0C), "4 Bytes", dropdown=POSITION),
    ])


def side(n):
    base = PLAYER_STATE + BLOCK * n
    kids = [
        entry("Life points (decoded)", h(base), "Custom", children=[entry("Raw (XOR key)", h(base), "4 Bytes")]).replace(
            "<VariableType>Custom</VariableType>", "<VariableType>Custom</VariableType><CustomType>LP (XOR key)</CustomType>"),
        group("Counts", [entry(name, h(base + c), "4 Bytes") for name, c, _, _ in PILES], collapsed=False),
        group("Monster zones", [zone(f"Monster zone {i + 1}", base + 0x4C + 24 * i) for i in range(5)]),
        group("Spell/Trap zones", [zone(f"Spell/Trap zone {i + 1}", base + 0xF0 + 24 * i) for i in range(5)]),
        zone("Field spell", base + 0x168),
    ]
    for name, _, arr, mx in PILES:
        kids.append(group(f"{name} cards ({mx})", [card_word(f"{name} {i + 1}", h(base + arr + 4 * i)) for i in range(mx)]))
    return group(f"Side {n + 1}" + (" (you, offline)" if n == 0 else ""), kids, collapsed=n != 0)


# ---- screen status: the old pointer entry with its dropdown, kept as it was
screen = None
for e in old.iter("CheatEntry"):
    if (e.findtext("Description") or "").strip('"') == "Screen Status":
        screen = ET.tostring(e, encoding="unicode")
        break

game = group("Game", ([screen] if screen else []) + [
    entry("Language (g_iGameLanguageID)", "14332A344", "4 Bytes"),
    entry("JP build (g_bIsJpVersion)", "14332A348", "Byte", dropdown=BOOL),
    entry("Window active", "140D326F9", "Byte", dropdown=BOOL),
    entry("Window visible", "140D326FA", "Byte", dropdown=BOOL),
    entry("Game content loaded", "1427D0560", "Byte", dropdown=BOOL),
    entry("Duel module ready", "1429275D1", "Byte", dropdown=BOOL),
    group("Audio", [
        entry("Music volume", "143329748", "Float"), entry("Music volume level", "14332974C", "4 Bytes"),
        entry("SFX volume", "143329750", "Float"), entry("SFX volume level", "143329754", "4 Bytes"),
        entry("Current music slot", "14332975C", "4 Bytes"), entry("Last UI sound slot", "1433294F0", "4 Bytes"),
    ]),
    group("Old table leftovers (meaning unconfirmed)", [
        entry("Selected slot", "14278EE78", "Byte"), entry("Paused", "14278EF14", "Byte"), entry("Grid location / mouse?", "14278EE70", "2 Bytes"),
    ]),
], collapsed=False)

setup = group("Duel setup (set before the duel starts)", [
    entry("Multiplayer duel", "140C8D1E6", "Byte", dropdown=BOOL),
    entry("Campaign duel", "140C8D1D8", "Byte", dropdown=BOOL),
    entry("Battle pack duel", "140C8D1D0", "Byte", dropdown=BOOL),
    entry("Challenge duel", "140C8D1E5", "Byte", dropdown=BOOL),
    entry("Tutorial duel", "140C8D1EA", "Byte", dropdown=BOOL),
    entry("Tutorial duel index", "140C8D1EC", "4 Bytes"),
    entry("Tag duel", "140C8D35D", "Byte", dropdown=BOOL),
    entry("Story duel id", "140C8D1E0", "4 Bytes"),
    entry("Arena id", "140C8D1F0", "4 Bytes"),
    group("Side characters (per seat)", [entry(f"Seat {i}", h(0x140C8D1F8 + 4 * i), "4 Bytes") for i in range(4)]),
    group("Side decks (per seat)", [entry(f"Seat {i}", h(0x140C8D208 + 4 * i), "4 Bytes") for i in range(4)]),
    entry("Starting life points", "140C8D370", "4 Bytes"),
    entry("Starting player (team 0/1)", "140C8D384", "4 Bytes", dropdown=SIDE),
    entry("Local player seat", "140C8D39C", "4 Bytes"),
    entry("Match round index", "140C8D374", "4 Bytes"),
    entry("Duel time limit", "140C8D368", "4 Bytes"),
    entry("Timer increment", "140C8D364", "4 Bytes"),
    entry("Engine rules flag (dead, see memory)", "140C8D1C9", "Byte"),
    entry("Duel base music slot", "140C8D1B8", "4 Bytes"),
], collapsed=False)

live = group("Online (live session)", [
    entry("Live session", "140C8D399", "Byte", dropdown=BOOL),
    entry("Live host", "140C8D1E7", "Byte", dropdown=BOOL),
    entry("Live match type", "140C8D1E8", "4 Bytes", dropdown="0:Friendly\n1:Ranked"),
    entry("Opponent left", "140C8D390", "Byte", dropdown=BOOL),
    entry("Local forfeit reason", "140C8D394", "4 Bytes"),
    entry("Live local seat", "140D4FE40", "4 Bytes"),
    entry("Outgoing net queue size", "1427D0570", "4 Bytes"),
    entry("Incoming net queue size", "1427D0770", "4 Bytes"),
])

duel = group("Duel (live)", [
    entry("LP XOR key", h(ENGINE), "2 Bytes", hex_=True),
    entry("Local seat parity", h(ENGINE + 4), "4 Bytes", dropdown=SIDE),
    entry("Starting player", h(ENGINE + 0x28), "4 Bytes", dropdown=SIDE),
    entry("Winner (0 none, 1/2 side, 3 draw)", h(PLAYER_STATE + 0x3792), "Byte", dropdown="0:None\n1:Side 1\n2:Side 2\n3:Draw"),
    entry("Last win reason", "1433305B4", "4 Bytes", children=[entry(WIN_REASON_NOTE, None)]),
    entry("RNG state", h(PLAYER_STATE + 0x3768), "4 Bytes", hex_=True),
    entry("Active effect card", "14349BC80", "2 Bytes", link=CARD_LIST),
    entry("Message queue count", "143330250", "4 Bytes"),
    group("Tag duel", [entry(f"Side {s + 1} partner on field", h(0x14332F500 + s), "Byte", dropdown="0:Seat duelist\n1:Partner") for s in range(2)]),
    side(0),
    side(1),
], collapsed=False)

seats = group("Seat decks (Duel_PlayerRecords)", [
    group(f"Seat {s}", [entry(n, h(0x142793578 + 0x4820 * s + 0x40 + 2 * w), "2 Bytes") for n, w in (("Main count", 33), ("Extra count", 34), ("Side count", 35))]
          + [group("Main deck", [entry(f"Main {i + 1}", h(0x142793578 + 0x4820 * s + 0x40 + 2 * (36 + i)), "2 Bytes", link=CARD_LIST) for i in range(60)])])
    for s in range(4)
])

save_script = """{$lua}
if syntaxcheck then return end
[ENABLE]
-- Calls YGO::SAVE::Get_PlayerSection(current profile) in the game every 2 s and keeps the symbol PlayerSection on it.
-- Only switch on with a profile signed in (main menu or later).
if YgoSaveTimer then YgoSaveTimer.destroy() end
YgoSaveTimer = createTimer(nil)
YgoSaveTimer.Interval = 2000
YgoSaveTimer.OnTimer = function()
  local ok, section = pcall(executeCodeEx, 0, 1000, 0x1407F80A0, 0xFFFFFFFD)
  if ok and section and section ~= 0 then unregisterSymbol("PlayerSection"); registerSymbol("PlayerSection", section, true) end
end
YgoSaveTimer.OnTimer()
[DISABLE]
if YgoSaveTimer then YgoSaveTimer.destroy(); YgoSaveTimer = nil end
unregisterSymbol("PlayerSection")
"""
save = entry("Save (tick to track the signed-in profile)", script=save_script, children=[
    entry("DP (wallet)", "PlayerSection+10", "8 Bytes"),
    entry("Menu unlocks (bit0 challenges, bit1 battle packs, bit2 card shop)", "PlayerSection+B94", "4 Bytes", hex_=True),
    entry("Pack unlock bits (+0xB80)", "PlayerSection+B80", "4 Bytes", hex_=True),
])

dropdown_source = entry(CARD_LIST, "0", "4 Bytes", dropdown=card_dropdown)

lua = """-- LP is stored XORed with the u16 at Duel_DuelEngine+0 (YuGiOh-DUELSTATE.h). This custom type shows and writes it decoded.
local KEY = 0x143330280
local function key() return readSmallInteger(KEY) or 0 end
registerCustomTypeLua("LP (XOR key)", 4,
  function(b1, b2, b3, b4) return (byteTableToDword({b1, b2, b3, b4}) ~ key()) & 0xFFFFFFFF end,
  function(value) local t = dwordToByteTable((value ~ key()) & 0xFFFFFFFF); return t[1], t[2], t[3], t[4] end,
  false)
"""

codes = old.find("CheatCodes")
codes_xml = ET.tostring(codes, encoding="unicode") if codes is not None else ""

xml = ('<?xml version="1.0" encoding="utf-8"?>\n<CheatTable CheatEngineTableVersion="46"><CheatEntries>'
       + game + setup + duel + live + seats + save
       + group("Lists (used by the dropdowns)", [dropdown_source])
       + "</CheatEntries>" + codes_xml + "<UserdefinedSymbols/>"
       + f"<LuaScript>{escape(lua)}</LuaScript></CheatTable>")

# pretty-print with CRLF like the original
tree = ET.ElementTree(ET.fromstring(xml.split("\n", 1)[1]))
ET.indent(tree, "  ")
body = ET.tostring(tree.getroot(), encoding="unicode")
with open(OUT, "w", encoding="utf-8", newline="\r\n") as f:
    f.write('<?xml version="1.0" encoding="utf-8"?>\n' + body + "\n")
print(f"wrote {OUT}: {len(cards)} card names")
