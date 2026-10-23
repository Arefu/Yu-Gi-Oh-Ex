# Booster packs (packdefdata\_#.bin, packs.zib) and new packs

The shop's booster packs are defined in `main/packdefdata_<L>.bin` (E F G I J S). Their card lists are in `packs.zib`
(`packdata_<name>.bin`). The parser is `File Type Libraries/PackDef` (`PackDefFile`, `PackContents`). All six packdefdata
files round-trip byte for byte (36 packs each).

**Yu-Gi-Oh-Core** (`Packs.cpp`) applies `Yu-Gi-Oh-Ex/packs.json`, which WolfX's New packs page writes. It moved from Yu-Gi-Oh-Cards to
Yu-Gi-Oh-BetterCardShop (2026-10-01), then to Core (2026-10-19): Core holds the limit breakers no store plugin owns, so anyone can build a
store over BOB pages without packs depending on it. Core starts it when it is injected (its log lines are in Core's log).

## The game (IDB)

- `LoadPackDefinitions` (0x14080E3C0) is called by `LoadLanguageContent` at startup and on a language change. It reads
  packdefdata into **`g_PackRecords[128]`** (0x1429241C0, `PackRecord` 0x68 each). A record is written only for the ids the
  file has, and the game uses 36 of them. Then, for every record with a name, it loads `packdata_<name>.bin` (kind 'R') or
  `bpack_<name>.bin` (kind 'B') from packs.zib.

| Offset | Field              | Meaning                                                    |
| ------ | ------------------ | ---------------------------------------------------------- |
| +0x00  | u32 Id             | < 128                                                      |
| +0x04  | u32 Series         | 0-5 = the shop tab, -1 = battle packs                      |
| +0x08  | u32 Cost           | DP                                                         |
| +0x0C  | u32 Kind           | 'R' reward (booster) pack, 'B' battle pack, 0 = empty slot |
| +0x10  | char\* Name        | "1_1"                                                      |
| +0x18  | u16\* Contents     | u16 common count, u16 rare count, common ids, rare ids     |
| +0x20  | void\* BattleData  |                                                            |
| +0x28  | std::wstring Title | built with the game's `std::wstring::assign` (0x140751310) |
| +0x48  | std::wstring Text  |                                                            |

- **Shop list:** `BoosterShop_BuildPackList` (0x14085D6E0) lists every id 0-127 whose record has a kind and whose series
  is the tab. A filled free slot therefore shows up by itself.
- **Unlocking:** `BoosterShop_HandleInput` (0x14085BAC0) only sells a pack whose bit is set in the save's
  **player section +0xB80**, a 128-bit field (u32[4], bit = pack id). Otherwise it shows message 719. The game sets these
  bits after duels, in `DuelResult_GrantRewards_140880240` (a small table plus the duel data's +0x30 pack id). **The save
  already has room for ids 36-127.**
- **Picture:** `Pack_GetArtName` (0x14080E390) formats `wrap_<Name>`, the animation layer to show.
- Opening a pack (`OpenPackAndGrantCards`) draws from `Contents`.

## packs.json

```json
{
    "replaceDefaults": false,
    "packs": [
        { "pack": "1_1", "common": [15300], "rare": [], "replace": false }
    ],
    "newPacks": [
        {
            "id": 36,
            "name": "custom_36",
            "series": 0,
            "cost": 200,
            "art": "1_1",
            "unlockWith": "1_3",
            "title": { "E": "My Pack", "F": "Mon paquet" },
            "text": { "E": "..." },
            "common": [4007, 4041],
            "rare": [15300]
        }
    ]
}
```

- **"packs":** cards added to a game pack, or replacing its cards with `"replace": true` (same as before the move).
  A replaced list that would be empty keeps the game's cards.
- **"newPacks":** packs the game doesn't have.
    - `id`: only for `"native": true` packs (0-127, the id WolfX wrote into the game's pack files). Other packs have no fixed id
      since 2026-10-21 (see *No pack limit* below); an `id` on them is ignored.
    - `name`: letters, digits and `_`, unique: it is the pack's identity.
    - `series`: the shop tab. A row a mod names can hold as many packs as there are free slots (92 in the stock game); packs
      without `series` fill new rows as full as the game's fullest row.
    - `art`: an existing pack's name, whose picture is shown. Without it, the picture is `wrap_<name>`, which only works if the
      game has that picture.
    - `unlockWith`: leave it out to have the pack open from the start, `"never"` to keep it locked, or a pack name to unlock it
      together with that pack.
    - `title`/`text`: per language, falling back to English.
    - `common`/`rare`: both need cards.

## How Core does it

- Hooks `LoadPackDefinitions`. After the game's load, it applies "packs" (a new card list per changed pack, kept alive) and
  fills each new pack's slot: id, series, cost, kind 'R', name, card list, and title/text through the game's own
  `std::wstring::assign` so the game can free them. This runs again after every reload, for the language.
  Core starts the plugin after the first load, so `Packs::Install` also applies once straight away.
- Hooks `Pack_GetArtName`: a new pack with `art` gets `wrap_<art>`.
- Hooks `BoosterShop_BuildPackList`: before a tab is built, it puts that tab's new packs in the free slots and sets their
  unlock bits (see below). Native packs' bits are set once their rule is met and stay in the save.
- Hooks `BoosterShop_Open` / `ShowSeriesArt` / `SetState` when there are rows past the game's 6 (row count, borrowed banner,
  row name label).
- It works without RIX, but the plugin's manifest requires Core and RIX.

## No pack limit (2026-10-21)

The game has 128 pack slots: `Get_PackRecord` (0x14080E340, 29 callers) returns null past 127, `BuildPackList` lists 0-127,
and `widget_PackWrapper::SetPack` (0x1408A34A0) shows any id past 127 as `wrap_locked`, because a pack's unlock is a bit in
the save's 128-bit field. The game uses 36, so 92 are free.

Instead of patching all of that, the new packs take turns in the free slots:

- After the game's first pack load Core notes the empty slots (`FindFreeSlots`).
- `FillSlots(row)` empties them, puts the shown row's packs in first and then as many others as fit (so screens outside the
  shop still see as many new packs as possible), and sets each free slot's unlock bit from its pack's rule. A slot's bit
  is rewritten every time, so one pack's bit never carries over to the next pack in that slot.
- The shop only shows one row at a time, and buying a pack reads the slot while that row is shown, so the game only ever sees
  ids under 128. There is no limit on the number of new packs; a row can hold up to the number of free slots.
- A new pack's unlocked state comes from its rule each time (open, `never`, or "with pack X", where X may be a game pack or
  another new pack), so the save needs no new data for packs. If packs ever unlock by something that has to be remembered
  (progress, purchases), that goes in a Core save extension like the card table's ids past 19999 (`CardTable.cpp`).
- `Packs::RareCards()` covers every new pack, not only the ones in a slot.

## WolfX

Packs page (new packs also have their own **New packs** page):

- Game packs keep the "extra cards" editor.
- **New pack** adds one in the next free id, with a name, shop tab, cost, picture, unlock rule, title and text per language,
  and common/rare cards.
- **Remove new pack** removes it.
- Save checks for names, ids, card lists and an English title.

## Scrolling tabs with more than six packs

Overflowing tabs show Previous pack and Next pack help prompts. Keyboard,
controller and mouse clicks use the game's existing navigation. Switching to
a tab with six or fewer packs removes these extra prompts.

The purchase check uses the selected pack's position in the six visible widgets,
subtracting the first visible index from the absolute selection. The plugin
checks the original instruction bytes before installing this patch; if they
differ or installation fails, new packs are disabled and a diagnostic is logged.
If the required Detours hooks fail to commit, packs are not applied.

`Tests/CardShopOverflow/CardShopOverflow.vcxproj` builds a standalone Windows x64
regression executable. It installs and executes the real patch on a synthetic
screen for all visible positions in lists of up to 128 packs and checks rejection
of unsupported instruction bytes. Build with MSBuild, Configuration=Release and
Platform=x64, then run `Tests/CardShopOverflow/bin/CardShopOverflow.exe`.

In-game verification remains necessary for prompt layout, tab changes, input
devices and purchases of locked and complete packs. The regression executable
does not access the game or its saves.
