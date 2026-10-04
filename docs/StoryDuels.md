# Story duels (dueldata_#.bin): the campaign

`main/dueldata_<L>.bin` (E F G I J S) holds the campaign's story duels: which series and position each one has, who plays whom with
which decks, the arena, the pack it unlocks, and its title, description and loss tip. The parser is `File Type Libraries/DuelData`
(`StoryDuelTable`, `StoryDuelJson`). The editor is `Tools/Shared/Editors/StoryDuelEditor.cs`, the **Story editor** tab in both WolfX and WolfEx
(its Duel tab; the scene tabs beside it edit the duel's dialog, [StoryScenes.md](StoryScenes.md)). **Yu-Gi-Oh-Campaign** (`StoryDuels.cpp`) applies WolfEx's `storyduels.json` to the game.

The dialog before and after each duel is not in this file: it's in `main/scriptdata_<L>.bin`, found by the duel's key
([StoryScenes.md](StoryScenes.md)).

## File layout

u32 count (183), u32 0, then count x 0x5C-byte records, then the strings. The strings are zero terminated and packed in record order
(key, costume 0, costume 1, title, description, tip) with no padding. The u64 offsets sit at +0x2C, so they are not 8-byte aligned.

| Offset | Field | Meaning |
|---|---|---|
| +0x00 | i32 Id | 1-225 (the game has 226 slots; 0 = empty). Ids 166 and 172 are unused. |
| +0x04 | i32 Series | 0 DM, 1 GX, 2 5D's, 3 ZEXAL, 4 ARC-V, 5 VRAINS |
| +0x08 | i32 Order | position in the series' list, from 1 |
| +0x0C / +0x10 | i32 Character[2] | chardata ids: [0] player, [1] opponent |
| +0x14 / +0x18 | i32 Deck[2] | deckdata ids (the duel uses + 32) |
| +0x1C | i32 Arena | |
| +0x20 | i32 RewardPack | pack unlocked on the first win; -1 = none (the loader stores 0) |
| +0x24 | i32 Sku | content pack; -1 in the file = 1, the base game |
| +0x28 | i32 ExcludeFromDeckUnlock | 1 on the two crossover duels (113 Pegasus vs Crowler, 128 Jaden vs Yugi) |
| +0x2C | u64 Key (ASCII) | names the dialog scripts `<key>_INTRO`, `<key>_OUTRO`, `<key>_OUTRO_LOSE` in scriptdata |
| +0x34 / +0x3C | u64 Costume[2] (ASCII) | portrait `<character key>_<costume>_neutral` ("" = normal): "notattoo", "dark", "glasses", "barian"... |
| +0x44 / +0x4C / +0x54 | u64 Title / Description / Tip (UTF-16) | the tip is shown after a loss |

* Every language has the same records, keys and costumes. Only the three texts are translated.
* All six files round-trip byte for byte.

## In the exe (IDB)

* `LoadStoryDuelData` (0x1407FF6D0), called by `LoadLanguageContent` right after `LoadStoryScriptData` (scriptdata), copies each record
  into **`g_StoryDuelRecords[226]`** (0x142919DA0, `StoryDuelRecord` 0xB0) through `StoryDuel_FromFileRecord` (0x1407FF810).
  * The runtime record holds: Id, Sides[2] {Character, Deck, Costume}, Arena, Sku, RewardPack, Key, the Title/Description/Tip
    `std::wstring`s, then Series, Order and ExcludeFromDeckUnlock.
  * It also marks the decks as this duel's story decks (deck record +120 = duel id, +124 = 1). For Order 1 it marks only the
    opponent's deck.
* `Campaign_BuildSeriesDuelLists` (0x14074A3B0) runs later in `LoadLanguageContent` and builds `g_CampaignSeries[6]`.
  * Each series gets a vector indexed by Order that gives the duel id; index 0 is unused. If two duels share an order, only one is listed.
  * `LastBaseGameOrder` is the highest order with Sku 1.
* **The campaign menu** (`CampaignMenu_BuildDuelList`, `…SelectDuel`, `…Update`) lists the series' duels whose SKU is owned.
  * Order 1 is the series' tutorial duel (`g_CampaignSeriesTutorial`) and has no reverse duel.
  * A reverse duel (`Campaign_IsReverseDuel`) swaps the sides.
* **Winning** (`DuelResult_GrantRewards_140880240`) does several things:
  * marks the duel cleared, and unlocks the next Sku-1 duel in the series and the reverse duel;
  * unlocks the opponent character and their deck;
  * unlocks **RewardPack** (pack bit at save +0xB80);
  * once every story deck of the opponent is cleared (skipping ExcludeFromDeckUnlock duels), unlocks the opponent's own deck.
* **Losing** shows the Tip.
* **Save:** `Section_0x1B70` keeps 1208 bytes per series, with 24 bytes per order at +16 (+0 normal state, +4 reverse state; 3 = cleared).
  The last order that fits is 48, so the editor and plugin limit Order to 1-48.
* `StoryDuel_FindDialogScript` (0x14082D660) looks up `<Key>_INTRO` / `_OUTRO` / `_OUTRO_LOSE` in `g_pStoryScriptFile` (scriptdata).

## storyduels.json (WolfEx → Yu-Gi-Oh-Campaign)

```json
{ "duels": [ { "id": 186, "series": 0, "order": 33, "key": "MyDuel",
               "player": { "character": 104, "deck": 5, "costume": "" }, "opponent": { "character": 94, "deck": 6, "costume": "" },
               "arena": 1, "rewardPack": -1, "sku": 1,
               "title": { "E": "..." }, "description": { "E": "..." }, "tip": { "E": "..." } } ] }
```

It lists only duels that differ from the game's or are new, each one whole.
* **A game id** changes that duel; only the fields the entry gives are applied.
* **A free id** (up to 225) makes a new duel. It goes into its series' list at its order after the game rebuilds the lists.
* **Texts:** per language, falling back to English.

Yu-Gi-Oh-Campaign hooks `LoadStoryDuelData`, which runs at start and on a language change. After the game loads its own duels, the
plugin builds a file-style record for each entry and passes it to the game's own `StoryDuel_FromFileRecord`. For a changed duel that
record starts from the game's values. This means the texts are game-allocated and the decks are marked exactly as the game does it.
If the plugin arrives after the first load, it applies the entries and calls `Campaign_BuildSeriesDuelLists` itself.

The plugin skips an entry and logs why if:
* the series isn't 0-5;
* the order isn't 1-48;
* a deck isn't 0-699 (the game would write through a null deck record);
* it has no key.

**A new duel has no dialog until it has `<key>_INTRO` / `_OUTRO` scenes**: make them in the Story editor's scene tabs.

Built, but not yet run in the game. Yu-Gi-Oh-Campaign must be switched on in Config.ini: `[Yu-Gi-Oh-Core]` `Yu-Gi-Oh-Campaign=1`.
