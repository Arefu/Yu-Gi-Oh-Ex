"""Fetch every card's alternate artworks from YGOPRODeck into the Rarities mod (docs/Shiny.md, "Card pictures").

Many cards were printed with more than one artwork (Dark Magician has 8). YGOPRODeck's card database lists each card's artworks; the
first is the card's own, the rest are alternates. They are saved, as YGOPRODeck's cropped art, as
  <Rarities mod>\\Yu-Gi-Oh-Ex\\cardart\\<card id>.alt<n>.jpg     (n = 1 .. 9; the repo's Mods\\Rarities by default)
for every LOTD card (matched by passcode, bin\\CARD_Pass.bin) and every custom card with a "password" (cards.json in the game's and the
repo's mods). Core then gives each card pulled from a pack one of its artworks at random and keeps which one in the save.

YGOPRODeck asks that images are downloaded once and kept, not linked; this keeps them and skips files already there.

  python -I Tools/Scripts/fetch_alt_arts.py [--game <game folder>] [--mod <mod folder>] [--dry-run]
"""
import argparse
import glob
import json
import os
import struct
import sys
import time
import urllib.request

DEFAULT_GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Yu-Gi-Oh! Legacy of the Duelist Link Evolution"
API = "https://db.ygoprodeck.com/api/v7/cardinfo.php"
AGENT = "Yu-Gi-Oh-Ex alternate art fetcher (personal use)"
MAX_ALTS = 9


def get(url):
    request = urllib.request.Request(url, headers={"User-Agent": AGENT})
    with urllib.request.urlopen(request, timeout=120) as response:
        return response.read()


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


def ids_by_passcode(game, repo):
    """passcode -> game ids: LOTD's cards (CARD_Prop and CARD_Pass, both one entry per card in the same order) and custom cards."""
    ids = {}
    archive = Archive(game)
    prop = archive.read(r"bin\CARD_Prop.bin")
    passes = archive.read(r"bin\CARD_Pass.bin")
    for i in range(min(len(prop) // 8, len(passes) // 4)):
        card = struct.unpack_from("<I", prop, i * 8)[0] & 0x3FFF
        passcode = struct.unpack_from("<i", passes, i * 4)[0]
        if card and passcode > 0:
            ids.setdefault(passcode, set()).add(card)
    paths = glob.glob(os.path.join(game, "Mods", "*", "Yu-Gi-Oh-Ex", "cards.json")) + glob.glob(os.path.join(repo, "Mods", "*", "Yu-Gi-Oh-Ex", "cards.json"))
    for path in paths:
        try:
            root = json.load(open(path, encoding="utf-8-sig"))
        except (OSError, ValueError):
            continue
        for card in root.get("cards", []) if isinstance(root, dict) else root:
            if isinstance(card, dict) and isinstance(card.get("id"), int) and isinstance(card.get("password"), int) and card["password"] > 0:
                ids.setdefault(card["password"], set()).add(card["id"])
    return ids


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    repo = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    parser.add_argument("--game", default=DEFAULT_GAME)
    parser.add_argument("--mod", default=os.path.join(repo, "Mods", "Rarities"), help="the Rarities mod's folder")
    parser.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()

    by_pass = ids_by_passcode(args.game, repo)
    data = json.loads(get(API))["data"]
    out = os.path.join(args.mod, "Yu-Gi-Oh-Ex", "cardart")
    if not args.dry_run:
        os.makedirs(out, exist_ok=True)

    cards = pictures = downloaded = 0
    for entry in data:
        images = entry.get("card_images", [])
        if len(images) < 2:
            continue
        # A game may know the card by one of its other artworks' passcodes (LOTD's Monster Reborn is 83764718, YGOPRODeck's 83764719):
        # every artwork the card's own passcode doesn't stand for is an alternate.
        own = next((img["id"] for img in images if img.get("id") in by_pass), None)
        if own is None:
            continue
        ids = by_pass[own]
        alternates = [img for img in images if img.get("id") != own][:MAX_ALTS]
        cards += 1
        for n, img in enumerate(alternates, 1):
            pictures += 1
            targets = [os.path.join(out, f"{card}.alt{n}.jpg") for card in sorted(ids)]
            if args.dry_run or all(os.path.isfile(t) for t in targets):
                continue
            time.sleep(0.06)   # YGOPRODeck allows 20 requests a second
            try:
                body = get(img["image_url_cropped"])
            except OSError as error:   # an artwork YGOPRODeck lists but has no cropped picture for
                print(f"  {entry['name']} artwork {img['id']}: {error}")
                continue
            for t in targets:
                with open(t, "wb") as f:
                    f.write(body)
            downloaded += 1
        if cards % 50 == 0:
            print(f"{cards} cards with alternate art so far ({pictures} artworks)", flush=True)

    print(f"Done: {cards} cards have alternate art ({pictures} artworks, {downloaded} downloaded now){' (dry run)' if args.dry_run else ''}")


if __name__ == "__main__":
    main()
