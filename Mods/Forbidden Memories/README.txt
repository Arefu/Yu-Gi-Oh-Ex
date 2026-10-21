Yu-Gi-Oh! Forbidden Memories mod (test build 0.4)
=================================================

Yu-Gi-Oh! and all related names, card art, data and graphics are (c) Konami / Kazuki Takahashi.
Forbidden Memories card data and graphics were read from the FM (USA) disc and belong to Konami.
This is an unofficial, non-commercial fan project, not affiliated with or endorsed by Konami.

What it does
- Only Forbidden Memories' 722 cards exist, with FM's ATK/DEF, levels, types, attributes, art and card frames.
- The 20 FM cards this game lacks are added as new cards (ids 30000 + FM number).
- Shop packs hold FM cards, a new profile gets FM's random 40-card starter deck,
  and Free Duel opponents play FM duelist decks (dealt again before each duel).
- FM duel rules (Yu-Gi-Oh-FM plugin): fusion from the hand without Polymerization, no Tributes.
  Guardian Stars, field/equip boosts and starchips are still to come.

Install
1. Copy the "Forbidden Memories" folder into <game folder>\Mods\
   (so you have <game folder>\Mods\Forbidden Memories\mod.json).
2. Make sure these plugins are installed and enabled:
   Yu-Gi-Oh-MoreCards, Yu-Gi-Oh-AnimeCards, Yu-Gi-Oh-Campaign, Yu-Gi-Oh-FM
3. Enable the mod in the Mod Manager (it is exclusive: turn other mods off).
4. Start a NEW save. The mod needs its own profile; old saves will not work with it.

Settings - nothing to edit
The mod brings its own settings in its Config.ini (the Anime Cards layout that fits the
art, name, stars and ATK/DEF into FM's frames, and the FM rules). Yu-Gi-Oh-Core uses them
instead of yours while the mod is switched on, and your own Config.ini goes back to being
used when you switch it off. Needs a Yu-Gi-Oh-Core from 2026-10-22 or later.
To change one, edit the mod's Config.ini, or in WolfX use File > Edit a mod, then
Tools > Config Editor. [Yu-Gi-Oh-Core] ModConfig=0 in your Config.ini turns mod settings off.
