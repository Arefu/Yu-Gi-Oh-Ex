# Characters (chardata_#.bin): the duelists

`main/chardata_<L>.bin` (E F G I J S) holds the duelists: the characters you duel and collect. It sets each one's name, bio,
portrait, series tab, deck, arena and content pack. The parser is `File Type Libraries/CharData` (`CharacterTable`,
`CharacterJson`). The editor is `Tools/Shared/Editors/CharacterEditor.cs`, on the **Characters** tab in both WolfX and WolfEx.
**Yu-Gi-Oh-Campaign** (`Characters.cpp`) applies WolfEx's `characters.json` to the game.

## File layout

u32 count (191), u32 0, then count x 0x38-byte records, then the strings. The strings are zero terminated and packed in record
order (key, name, bio) with no padding.

| Offset | Field | Meaning |
|---|---|---|
| +0x00 | i32 Id | 0-239 |
| +0x04 | i32 Series | 0-5 = the series tab (DM, GX, 5D's, ZEXAL, ARC-V, VRAINS); -1 = none (2 characters), unlocked in every new profile |
| +0x08 | i32 Deck | deckdata id (duels use it + 32); -1 = none (33 characters) |
| +0x0C | u32 Selectable | 1 = Free Duel opponent and counted in the collection; 0 for exactly the 33 without a deck |
| +0x10 | i32 Sku | content pack; -1 in the file = 1, the base game |
| +0x14 | i32 | **never read** (the loader doesn't copy it; 1 on 3 characters) |
| +0x18 | i32 Arena | the arena of duels against them (`Set_CurrentArenaId`) |
| +0x1C | i32 | always 0 |
| +0x20 | u64 | Key (ASCII): the portrait is `<key>_neutral` in the `pdui/chars` sprite sheet |
| +0x28 / +0x30 | u64 | Name / Bio (UTF-16) |

* Every language has the same records and keys. Only names and bios are translated.
* Some bios are dev placeholders ("Akiza Bio", "For Characters who don't exist yet").
* All six files round-trip byte for byte.
* All 158 characters that have a deck own it: the deck's `CharacterId` in deckdata equals the character's id.

## In the exe (IDB)

* `LoadCharacterData` (0x1407FED60) is called by `LoadLanguageContent` at startup and on a language change. It fills
  **`g_CharacterRecords[240]`** (0x142913470, `CharacterRecord` 0x68): +0 Id, +4 Series, +8 Deck, +0xC Selectable, +0x10 Sku,
  +0x14 Arena, +0x18, +0x20 Key, then the Name and Bio `std::wstring`s at +0x28 and +0x48. It forces id 138 to selectable.
* `Character_IsDefined` (Key != NULL), `Character_IsSelectable`, `Get_CharacterSeries`, `Get_CharacterSku` (id 47 forced to 1).
  The portrait functions are `Character_GetPortraitName`, `…NameOr` and `…Cached`.
* **Free Duel:** `FreeDuel_BuildOpponentList` (0x140841900) lists a character if it is defined, selectable, in the tab's series,
  its SKU is owned, **and its deck is defined and owned by it** (`Get_DeckOwnerCharacter(deck) == id`). `FreeDuel_HandleInput`
  starts the duel with deck + 32 and the character's arena.
* **Unlocks:** a 240-bit set at the save's player section +0x18 (bit = character id).
  * `InitPlayerSection` unlocks characters with series -1, plus `g_InitialUnlockedCharacters` (105, 119, 35, 174, 72).
  * `Collection_FormatCharacterCount` shows "unlocked/total (+ DLC)".
* `ChallengeBonus_1407C7040`: in Challenge mode, beating the character whose deck matches gives a 50 bonus.

## characters.json (WolfEx → Yu-Gi-Oh-Campaign)

```json
{ "characters": [ { "id": 192, "key": "yugimuto", "series": 0, "deck": 5, "selectable": 1, "sku": 1, "arena": 2, "unlocked": true,
                    "name": { "E": "My Duelist" }, "bio": { "E": "..." } } ] }
```

It lists only characters that differ from the game's or are new, each one whole.
* **A game id** changes that character; only the fields the entry gives are applied.
* **A free id** (192-239) makes a new character.
* **key:** the portrait. An existing character's key borrows their picture, and no hook is needed because the key is only
  used for the portrait.
* **name / bio:** per language, falling back to English.
* **unlocked** (new characters, default true): the plugin sets the character's save bit when the Free Duel list or the
  collection count is built.

Yu-Gi-Oh-Campaign hooks `LoadCharacterData` (fills the slots again after every reload, for the language), plus
`FreeDuel_BuildOpponentList` and `Collection_FormatCharacterCount` (for the unlock bits). Name and bio go through the game's own
`std::wstring::assign`.

**For a new character to appear in Free Duel it needs a deck it owns**: make one in the Decks tab with the character as its owner
(decks.json, [Decks.md](Decks.md)), then set it as the character's deck. Pointing a new character at an existing deck shows it
everywhere else but not in Free Duel, and the editor warns about this.

Built, but not yet run in the game. Yu-Gi-Oh-Campaign is new, so switch it on in Config.ini: `[Yu-Gi-Oh-Core]`
`Yu-Gi-Oh-Campaign=1`, or use the in-game Plugins menu.
