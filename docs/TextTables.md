# WORD and DLG text tables (bin/WORD_*, bin/DLG_*)

Two Indx + Text pairs the game loads in `YGO::CARDS::Setup_CardPropTable` (0x14076BFC6), in every language (E F G I J R S). **R is not
Russian**: it's the Japanese text with ruby readings (`$R特殊(とくしゅ)`); `g_LanguageLetterTable` (0x140A51D30) maps language id 6 to 'r'.

Parser: `File Type Libraries/TextTable` (`Types.TextTable`, `TextTableJson`); round-trips all 28 files byte for byte. Editor:
`Tools/Shared/Editors/TextTableEditor` - the WolfX "Text tables" tab (files on disk, existing entries) and the WolfEx "Text tables" tab
(the game's tables plus `Yu-Gi-Oh-Ex/text.json`, can add entries).

## Format

```text
_Indx_#.bin   (count + 1) x u32 byte offset into the text; the last is the end of the text (no string there)
_Text_#.bin   UTF-16LE strings in order, each with a null, padded with zeros to 4 bytes
```

After loading, the game turns full-width A-Z / a-z (U+FF21-FF5A) into ASCII.

## WORD: card-frame words, YGO::CARDS::Get_WordText 0x14076D7B0

Read as a group start + a card value: `Get_WordBracket` (+1), `Get_WordAttribute` (+10), `Get_WordRace` (+100), `Get_WordKind` (+200).

| index | what |
|---|---|
| 1-3 | `[` `]` `[Pendulum Effect]` |
| 10 + attribute | 0 `?`, 1 LIGHT ... 7 DIVINE, 8 SPELL, 9 TRAP (word 10 `?` is also ATK/DEF 0xFFFF) |
| 20, 21 | Spell, Trap |
| 50-56 | spell/trap icon (Normal, Counter, Field, Equip, Continuous, Quick-Play, Ritual) |
| 60-66, 70-74 | spell types, trap types |
| 100 + type | 0 `?`, 1 Dragon ... 24 Divine-Beast, 25 Creator God |
| 130, 131 | SPELL CARD, TRAP CARD |
| 200 + card kind | the type line: `/Normal`, `/Effect`, `/Fusion/Effect` ... (kinds 0-45) |

A custom card kind, attribute or type shows its word from here, so a new one needs an entry (e.g. kind 46 = word 246).

## DLG: duel prompts, YGO::CARDS::Get_DlgText 0x14076D770

689 entries (688 strings): chain questions, "Select @21@0 @3monster@0 ...", effect choice texts; `%s` is a card name. `@n` colours as
in How to Play.

## text.json (Yu-Gi-Oh-Cards plugin, Text.cpp)

```json
{ "word": { "246": { "E": "/Custom/Effect", "G": "/Eigene/Effekt" } },
  "dlg":  { "688": { "E": "Select @21@0 @3custom@0 card." } } }
```

The plugin hooks `Get_WordText` and `Get_DlgText` (MS Detours). An entry in the JSON wins over the game's: a number past the game's
entries adds one, a number it has replaces it. The text for the current language is used, else English, else any. It's re-read after
each card setup (a language change). WolfEx writes only what differs from the game, keeping the other table's section.
