"""Fetch the real extended-art (over-frame) card pictures from Yugipedia into the Rarities mod (docs/Shiny.md, "Card pictures").

Konami prints some cards with "extended art" (TCG) / "Over-Frame" (OCG): the art breaks out of its frame. Yugipedia lists them on its
"Extended art" page, and keeps Master Duel's own render of each as <Card>-<Set>-EN-VG-EA.png: the whole card, frame painted in, no
text, 702 x 1024. That is exactly Core's broken-frame picture (Rarity::ArtBroken), so it is saved, squeezed to 400 x 580, as
  <Rarities mod>\\Yu-Gi-Oh-Ex\\cardart\\<card id>.broken.png    (the repo's Mods\\Rarities by default)
for every LOTD card and every custom card (cards.json in the game's and the repo's mods) with that English name. A card with only a
scan of its printed extended-art card (text in the picture) gets that as <card id>.printed.png: Core shows it as it is and draws no
text over it (Rarity::ArtPrinted).

  python -I Tools/Scripts/fetch_extended_art.py [--game <game folder>] [--mod <mod folder>] [--dry-run]
"""
import argparse
import glob
import io
import json
import os
import re
import struct
import sys
import time
import urllib.parse
import urllib.request

from PIL import Image

DEFAULT_GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution"
API = "https://yugipedia.com/api.php"
AGENT = "Yu-Gi-Oh-Ex extended art fetcher (personal use; one request a second)"
FACE = (400, 580)


def get(url):
    time.sleep(1.0)   # Yugipedia asks bots to go slowly
    request = urllib.request.Request(url, headers={"User-Agent": AGENT})
    with urllib.request.urlopen(request, timeout=60) as response:
        return response.read()


def api(**params):
    params["format"] = "json"
    return json.loads(get(API + "?" + urllib.parse.urlencode(params)))


def key(name):
    """Names compared without case, quotes, punctuation or spaces ("Dark Magician, the Pharaoh's Servant")."""
    return re.sub(r"[^0-9a-z]+", "", name.lower().replace("\u2019", "'"))


class Archive:
    """YGO_2020.toc / .dat: a text toc ("size namelength name", sizes in hex), files back to back, each padded to 4 bytes."""

    def __init__(self, game):
        self.dat = os.path.join(game, "YGO_2020.dat")
        self.items = {}
        offset = 0
        for line in open(os.path.join(game, "YGO_2020.toc"), encoding="ascii", errors="replace").read().splitlines()[1:]:
            parts = line.split(None, 2)
            if len(parts) < 3:
                continue
            size = int(parts[0], 16)
            self.items[parts[2].strip().lower()] = (offset, size)
            offset += (size + 3) & ~3

    def read(self, path):
        offset, size = self.items[path.lower()]
        with open(self.dat, "rb") as f:
            f.seek(offset)
            return f.read(size)


def card_ids_by_name(game, repo):
    """English name key -> game ids: LOTD's cards (bin\\CARD_Prop, CARD_Indx_E, CARD_Name_E) and custom cards from cards.json files."""
    ids = {}
    archive = Archive(game)
    prop = archive.read(r"bin\CARD_Prop.bin")
    indx = archive.read(r"bin\CARD_Indx_E.bin")
    names = archive.read(r"bin\CARD_Name_E.bin")
    for i in range(len(prop) // 8):
        card = struct.unpack_from("<I", prop, i * 8)[0] & 0x3FFF
        if (i + 1) * 8 > len(indx):
            break
        start = struct.unpack_from("<I", indx, i * 8)[0]
        end = names.find(b"\0\0", start)
        while end != -1 and (end - start) % 2:
            end = names.find(b"\0\0", end + 1)
        name = names[start:end].decode("utf-16-le", "replace")
        if card and name:
            ids.setdefault(key(name), set()).add(card)
    paths = glob.glob(os.path.join(game, "Mods", "*", "Yu-Gi-Oh-Ex", "cards.json")) + glob.glob(os.path.join(repo, "Mods", "*", "Yu-Gi-Oh-Ex", "cards.json"))
    for path in paths:
        try:
            root = json.load(open(path, encoding="utf-8-sig"))
        except (OSError, ValueError):
            continue
        for card in root.get("cards", []) if isinstance(root, dict) else root:
            if isinstance(card, dict) and card.get("name") and isinstance(card.get("id"), int):
                ids.setdefault(key(card["name"]), set()).add(card["id"])
    return ids


def extended_art_cards():
    """Card page names listed on Yugipedia's "Extended art" page."""
    text = api(action="parse", page="Extended_art", prop="wikitext")["parse"]["wikitext"]["*"]
    section = text.split("== List of cards with extended art ==", 1)[-1]
    cards = []
    for line in section.splitlines():
        m = re.match(r'\*\s*"\[\[([^\]|]+)(?:\|[^\]]*)?\]\]"', line.strip())
        if m and m.group(1) not in cards:
            cards.append(m.group(1))
    return cards


def extended_picture(card_page):
    """(style, file title, url): the card's text-free extended-art render (-VG-EA, "broken"), else a scan of its printed extended-art
    card (-EA, "printed"), English first. None when Yugipedia has neither."""
    pages = api(action="query", titles=card_page, prop="images", imlimit="500")["query"]["pages"]
    files = [i["title"] for p in pages.values() for i in p.get("images", [])]
    vg = [f for f in files if re.search(r"-VG-EA\.(png|jpg)$", f, re.I)]
    scans = [f for f in files if re.search(r"-EA\.(png|jpg)$", f, re.I) and f not in vg]
    for style, choices in (("broken", vg), ("printed", scans)):
        if choices:
            choices.sort(key=lambda f: (0 if "-EN-" in f else 1, f))
            info = api(action="query", titles=choices[0], prop="imageinfo", iiprop="url")["query"]["pages"]
            return style, choices[0], next(iter(info.values()))["imageinfo"][0]["url"]
    return None


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    repo = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    parser.add_argument("--game", default=DEFAULT_GAME)
    parser.add_argument("--mod", default=os.path.join(repo, "Mods", "Rarities"), help="the Rarities mod's folder")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    by_name = card_ids_by_name(args.game, repo)
    out = os.path.join(args.mod, "Yu-Gi-Oh-Ex", "cardart")
    if not args.dry_run:
        os.makedirs(out, exist_ok=True)

    cards = extended_art_cards()
    print(f"{len(cards)} cards with extended art on Yugipedia")
    counts = {"broken": 0, "printed": 0}
    no_picture = not_in_game = 0
    for page in cards:
        name = page.split(" (card)")[0].strip("‎ ")
        ids = by_name.get(key(name), set())
        if not ids:
            not_in_game += 1
            print(f"  -  {name}: not in LOTD or any cards.json")
            continue
        picture = extended_picture(page)
        if not picture:
            no_picture += 1
            print(f"  ?  {name}: no picture on Yugipedia yet")
            continue
        style, title, url = picture
        print(f"  +  {name} -> {', '.join(map(str, sorted(ids)))}  {style} ({title})")
        if args.dry_run:
            continue
        face = Image.open(io.BytesIO(get(url))).convert("RGBA").resize(FACE, Image.LANCZOS)
        for card in ids:
            face.save(os.path.join(out, f"{card}.{style}.png"), optimize=True)
        counts[style] += 1
    print(f"Done: {counts['broken']} text-free broken frames, {counts['printed']} printed scans, {no_picture} with no picture yet, "
          f"{not_in_game} not in the game or a cards.json")


if __name__ == "__main__":
    main()
