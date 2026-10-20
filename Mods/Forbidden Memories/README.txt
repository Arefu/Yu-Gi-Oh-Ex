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

IMPORTANT - change the Anime Cards settings
The FM card frames need these Yu-Gi-Oh-AnimeCards settings. Set them by hand for now,
in Config.ini under [Yu-Gi-Oh-AnimeCards] (or in the plugin's settings in WolfX):

  [Yu-Gi-Oh-AnimeCards]
  CardArtScale=1.05
  CardArtOffsetX=18.5
  CardArtOffsetY=38
  ShowName=1
  HideSpellTrapIcon=1
  StarsAlign=1
  AtkLabel=ATK
  DefLabel=DEF
  SpellTrapAttribute=1

(AtkLabel and DefLabel end with a space.)
Without these the art will not fit FM's window and the card faces will look wrong.
Set them back to the defaults when you go back to normal anime frames.
A later WolfX version may apply these settings for you when the mod is enabled.
