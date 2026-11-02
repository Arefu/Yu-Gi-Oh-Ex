"""Add your own Master Duel install's "Over Frame" cards to the Rarities mod (docs/Shiny.md, "Card pictures").

Master Duel draws a few cards as one 512 x 1024 picture of the whole card: the frame is painted in and the art breaks out of it (its
"Over Frame" cards, the broken-frame look). Squeezed to the game's 400 x 580 face, Master Duel's art window lands on LOTD's
(49-351 x 107-407), so the picture is the face; Core draws the name, text and icons on top (Rarity::ArtBroken).

Which cards: Master Duel's md/of_card_asset.bytes (plain, little-endian u32 entries). An entry is either a card id (the card's own
picture has the frame painted in: Red-Eyes Black Dragon Exceed, The Winged Dragon of Ra Sun God of Darkness, ...) or a pair, low u16 =
picture number, high u16 = the card (an alternate picture: 22812 is Super Polymerization 7445). Card ids are the game's Konami ids, so a
pair or id LOTD doesn't have only shows up on a custom card with that id. (Master Duel's other 512 x 1024 pictures are Pendulum art.)

Writes, from an AssetStudio export of Master Duel, into the Rarities mod (the repo's Mods\\Rarities, shipped in releases):
  mod.json, Yu-Gi-Oh-Ex\\rarities.json                               (Over Rush / Full Over Rush roll from packs)
  Yu-Gi-Oh-Ex\\cardart\\<card id>.broken.png                         (400 x 580)
The Rarities mod's other pictures come from fetch_extended_art.py and fetch_alt_arts.py.

  python -I Tools/Scripts/make_fullframe_mod.py [--export <ASExport\\assets>] [--game <game folder>] [--mod <mod folder>] [--dry-run]
"""
import argparse
import glob
import json
import os
import struct
import sys
import zlib

from PIL import Image

DEFAULT_EXPORT = r"C:\Users\Johnathon\Downloads\AssetStudioModCLI_net472_win32_64\ASExport\assets"
DEFAULT_GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution"
FACE = (400, 580)
REPO = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))


def over_frame_cards(export):
    """[(card id, picture number)] from md/of_card_asset.bytes."""
    found = glob.glob(os.path.join(export, "resourcesassetbundle", "card", "data", "*", "md", "of_card_asset.bytes"))
    if not found:
        sys.exit(f"No card/data/*/md/of_card_asset.bytes under {export}: export Master Duel's card data too")
    data = open(found[0], "rb").read()
    cards = []
    for (value,) in struct.iter_unpack("<I", data[: len(data) // 4 * 4]):
        picture, card = value & 0xFFFF, value >> 16
        cards.append((card, picture) if card else (picture, picture))
    return cards


def md_table(export, name):
    """A Master Duel CARD_* table, decrypted: per byte b ^= ((i + key + 0x23D) * key ^ (i % 7)), then zlib. The key changes between
    versions, so every key is tried."""
    found = glob.glob(os.path.join(export, "resourcesassetbundle", "card", "data", "*", "en-us", name))
    if not found:
        return None
    data = open(found[0], "rb").read()
    for key in range(256):
        out = bytes(b ^ (((i + key + 0x23D) * key ^ (i % 7)) & 0xFF) for i, b in enumerate(data[:64]))
        try:
            zlib.decompressobj().decompress(out)
        except zlib.error:
            continue
        full = bytes(b ^ (((i + key + 0x23D) * key ^ (i % 7)) & 0xFF) for i, b in enumerate(data))
        try:
            return zlib.decompress(full)
        except zlib.error:
            continue
    return None


def md_names(export):
    """Master Duel card id -> English name."""
    prop, indx, names = (md_table(export, n) for n in ("CARD_Prop.bytes", "CARD_Indx.bytes", "CARD_Name.bytes"))
    if not (prop and indx and names):
        return {}
    out = {}
    for i in range(len(prop) // 8):
        card = struct.unpack_from("<I", prop, i * 8)[0] & 0xFFFF
        if (i + 1) * 8 + 4 > len(indx):
            break
        start, end = struct.unpack_from("<I", indx, i * 8)[0], struct.unpack_from("<I", indx, (i + 1) * 8)[0]
        out[card] = names[start:end].split(b"\0")[0].decode("utf-8", "replace")
    return out


def custom_cards(game, repo):
    """Name (lower case) -> the game ids custom cards with that name use, from every cards.json in the game's and the repo's mods."""
    ids = {}
    paths = glob.glob(os.path.join(game, "Mods", "*", "Yu-Gi-Oh-Ex", "cards.json"))
    paths += glob.glob(os.path.join(game, "Yu-Gi-Oh-Ex", "cards.json"))
    paths += glob.glob(os.path.join(repo, "Mods", "*", "Yu-Gi-Oh-Ex", "cards.json"))
    for path in paths:
        try:
            root = json.load(open(path, encoding="utf-8-sig"))
        except (OSError, ValueError):
            continue
        for card in root.get("cards", []) if isinstance(root, dict) else root:
            if isinstance(card, dict) and card.get("name") and isinstance(card.get("id"), int):
                ids.setdefault(card["name"].lower(), set()).add(card["id"])
    return ids


def find_picture(export, picture):
    """The 1:2 picture with that number (resourcesassetbundle wins over resources: it is the newer copy)."""
    for root in ("resourcesassetbundle", "resources"):
        for path in glob.glob(os.path.join(export, root, "card", "images", "illust", "**", f"{picture}.png"), recursive=True):
            with Image.open(path) as im:
                if im.height == 2 * im.width:
                    return path
    return None


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--export", default=DEFAULT_EXPORT, help="AssetStudio export's assets folder")
    parser.add_argument("--game", default=DEFAULT_GAME, help="LOTD's game folder")
    parser.add_argument("--mod", default=os.path.join(REPO, "Mods", "Rarities"), help="the Rarities mod's folder")
    parser.add_argument("--dry-run", action="store_true", help="only list what would be written")
    args = parser.parse_args()

    out = args.mod
    art = os.path.join(out, "Yu-Gi-Oh-Ex", "cardart")
    if not args.dry_run:
        os.makedirs(art, exist_ok=True)

    # A card LOTD doesn't have may be a custom card (the Cards mod) under its own game id: matched by English name.
    names = md_names(args.export)
    customs = custom_cards(args.game, REPO)

    written = 0
    for card, picture in over_frame_cards(args.export):
        path = find_picture(args.export, picture)
        name = names.get(card, "?")
        note = "" if picture == card else f" (picture {picture})"
        if not path:
            print(f"{card:6d} {name}{note}: no 1:2 picture {picture}.png in the export")
            continue
        ids = {card} | customs.get(name.lower(), set())
        print(f"{card:6d} {name}{note} -> ids {', '.join(map(str, sorted(ids)))}")
        if not args.dry_run:
            with Image.open(path) as im:
                face = im.convert("RGBA").resize(FACE, Image.LANCZOS)
                for target in ids:
                    face.save(os.path.join(art, f"{target}.broken.png"), optimize=True)
        written += 1

    if args.dry_run:
        print(f"{written} broken-frame pictures (dry run, nothing written)")
        return

    write_mod_files(out)
    print(f"Wrote {out}: {written} broken-frame pictures")


def write_mod_files(mod):
    """The Rarities mod's mod.json and rarities.json (the same in fetch_extended_art.py and fetch_alt_arts.py)."""
    os.makedirs(os.path.join(mod, "Yu-Gi-Oh-Ex"), exist_ok=True)
    with open(os.path.join(mod, "mod.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({
            "name": "Rarities",
            "version": "1.1",
            "description": "Real-card showcase prints: broken frames, full art and alternate artworks.",
            "details": "Needs Shiny=1 (Yu-Gi-Oh-Cards). Over Rush Rare: the card's real extended-art (over-frame) print, the art breaking out "
                       "of its frame. Full Over Rush Rare: the art over the whole card. Every card pulled comes in one of its printed artworks. "
                       "ShinyPreview=over-rush, full-over-rush or alt1 shows them on every card. Made by Tools/Scripts/make_fullframe_mod.py, "
                       "fetch_extended_art.py and fetch_alt_arts.py.",
            "requires": ["Yu-Gi-Oh-Cards/Shiny"],
        }, f, indent=2)
        f.write("\n")
    # Over Rush (broken frame) lands only on cards with a picture; Full Over Rush (full art) on any card that isn't a Pendulum
    with open(os.path.join(mod, "Yu-Gi-Oh-Ex", "rarities.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump({"rarities": [{"key": "over-rush", "odds": 24, "slot": "rare"},
                                {"key": "full-over-rush", "odds": 60, "slot": "rare"}]}, f, indent=2)
        f.write("\n")


if __name__ == "__main__":
    main()
