# Booster packs (packdefdata_#.bin, packs.zib) and new packs

The shop's booster packs are defined in `main/packdefdata_<L>.bin` (E F G I J S). Their card lists are in `packs.zib`
(`packdata_<name>.bin`). The parser is `File Type Libraries/PackDef` (`PackDefFile`, `PackContents`). All six packdefdata
files round-trip byte for byte (36 packs each).

**Yu-Gi-Oh-BetterCardShop** (`Packs.cpp`) applies `Yu-Gi-Oh-Ex/packs.json`, which WolfEx's Packs tab writes. This moved there
from Yu-Gi-Oh-Cards (2026-10-01). Cards no longer touches packs.

## The game (IDB)

* `LoadPackDefinitions` (0x14080E3C0) is called by `LoadLanguageContent` at startup and on a language change. It reads
  packdefdata into **`g_PackRecords[128]`** (0x1429241C0, `PackRecord` 0x68 each). A record is written only for the ids the
  file has, and the game uses 36 of them. Then, for every record with a name, it loads `packdata_<name>.bin` (kind 'R') or
  `bpack_<name>.bin` (kind 'B') from packs.zib.

| Offset | Field | Meaning |
|---|---|---|
| +0x00 | u32 Id | < 128 |
| +0x04 | u32 Series | 0-5 = the shop tab, -1 = battle packs |
| +0x08 | u32 Cost | DP |
| +0x0C | u32 Kind | 'R' reward (booster) pack, 'B' battle pack, 0 = empty slot |
| +0x10 | char* Name | "1_1" |
| +0x18 | u16* Contents | u16 common count, u16 rare count, common ids, rare ids |
| +0x20 | void* BattleData | |
| +0x28 | std::wstring Title | built with the game's `std::wstring::assign` (0x140751310) |
| +0x48 | std::wstring Text | |

* **Shop list:** `BoosterShop_BuildPackList` (0x14085D6E0) lists every id 0-127 whose record has a kind and whose series
  is the tab. A filled free slot therefore shows up by itself.
* **Unlocking:** `BoosterShop_HandleInput` (0x14085BAC0) only sells a pack whose bit is set in the save's
  **player section +0xB80**, a 128-bit field (u32[4], bit = pack id). Otherwise it shows message 719. The game sets these
  bits after duels, in `DuelResult_GrantRewards_140880240` (a small table plus the duel data's +0x30 pack id). **The save
  already has room for ids 36-127.**
* **Picture:** `Pack_GetArtName` (0x14080E390) formats `wrap_<Name>`, the animation layer to show.
* Opening a pack (`OpenPackAndGrantCards`) draws from `Contents`.

## packs.json

```json
{ "replaceDefaults": false,
  "packs": [ { "pack": "1_1", "common": [15300], "rare": [], "replace": false } ],
  "newPacks": [ { "id": 36, "name": "custom_36", "series": 0, "cost": 200, "art": "1_1", "unlockWith": "1_3",
                  "title": { "E": "My Pack", "F": "Mon paquet" }, "text": { "E": "..." },
                  "common": [4007, 4041], "rare": [15300] } ] }
```

* **"packs":** cards added to a game pack, or replacing its cards with `"replace": true` (same as before the move).
  A replaced list that would be empty keeps the game's cards.
* **"newPacks":** packs the game doesn't have.
  * `id`: 0-127 and not a game pack's id. WolfEx picks the next free id, and it should stay the same because the save's
    unlock bit is by id.
  * `name`: letters, digits and `_`.
  * `series`: the shop tab.
  * `art`: an existing pack's name, whose picture is shown. Without it, the picture is `wrap_<name>`, which only works if the
    game has that picture.
  * `unlockWith`: leave it out to have the pack open from the start, `"never"` to keep it locked, or a pack name to unlock it
    together with that pack.
  * `title`/`text`: per language, falling back to English.
  * `common`/`rare`: both need cards.

## How BetterCardShop does it

* Hooks `LoadPackDefinitions`. After the game's load, it applies "packs" (a new card list per changed pack, kept alive) and
  fills each new pack's slot: id, series, cost, kind 'R', name, card list, and title/text through the game's own
  `std::wstring::assign` so the game can free them. This runs again after every reload, for the language.
  Core starts the plugin after the first load, so `Packs::Install` also applies once straight away.
* Hooks `Pack_GetArtName`: a new pack with `art` gets `wrap_<art>`.
* Hooks `BoosterShop_BuildPackList`: before a tab is built, it sets the unlock bits of the new packs whose rule is met.
  The bits are kept in the save. A game without the plugin ignores them, because the slot is empty there.
* It works without RIX, but the plugin's manifest requires Core and RIX.
* A new pack whose id is a game pack's is skipped, with a log line.

## WolfEx

Packs tab:
* Game packs keep the "extra cards" editor.
* **New pack** adds one in the next free id, with a name, shop tab, cost, picture, unlock rule, title and text per language,
  and common/rare cards.
* **Remove new pack** removes it.
* Save checks for names, ids, card lists and an English title.

Not yet run in the game.

## Art from the sheets (2026-10-11)

* A pack\'s picture is the `wrap_<x>` sprite of `pdui\PackWrappers.dfymoo` (37 in the game). WolfX\'s New packs page lists every `wrap_*` in the sheet as the game reads it (the Ex patch\'s copy included), so a picture added there with **Sprite sheets > Add sprite from PNG...** can be picked; a new pack whose own name has a `wrap_<name>` sprite needs no `"art"`.
* A shop row\'s banner is the `wrap_series_<key>` sprite of `pdui\SeriesLogo.dfymoo`. `"rows"` `"art"`: 1-6 = a game row\'s banner, a name = a new `wrap_series_<name>` added to that sheet (BetterCardShop selects it).
* **Sprite sheets > Replace sprite image...** paints a PNG over a sprite (e.g. a game pack's art or a series logo); both save the sheet's .dfymoo and .png into the Ex patch.
