# How to Play help (howtoplay_&lt;L&gt;.bin)

The game's Help > How to Play screen (`RIX::ScreenHelpHowToPlay`) reads `main/howto_db/howtoplay_#.bin`, `#` being the language letter
(E, F, G, I, J, S ship with the game). It's loaded with `LoadFileFromArchives`, so a loose `<game>\YGO_2020\main\howto_db\howtoplay_E.bin`
replaces the archive's copy. Parser: `File Type Libraries/HowToPlay` (`Types.HowToPlayFile`); editor: `Tools/Shared/Editors/HowToPlayEditor`
(WolfX "How to Play" tab for files on disk, WolfEx "How to Play" tab for the game's files, saved as loose overrides).

## Format (big-endian), RIX::ScreenHelpHowToPlay::LoadDatabase 0x1408485E0

```text
u32 count, u32 0
count x { u8 kind, u8 picture, u16 length, u32 0 }     the last record is kind 4 (end)
count x UTF-16BE string, length characters + a null    in record order; the end record's is empty
```

| kind | what | notes |
|---|---|---|
| 0 | chapter | the list on the left ("Duel Basics"); 13 in the game's files |
| 1 | topic | a heading ("●What is a Deck?") |
| 2 | sub-topic | a heading the game shows with "-" in front |
| 0xFF | body | the article text for the heading before it; `picture` = `main/howto_img/help_duelimg_NNN.png` (0 = none) |
| 4 | end | |

Each chapter takes (heading, body) pairs until the next chapter. The game keeps chapters and items in vectors: **no fixed limit**, so
chapters and topics can be added freely. Pictures are 1-255; new ones are loose PNGs in `YGO_2020\main\howto_img`.

All six shipped languages have the same 202 records; the library round-trips every file byte for byte, binary and script.

## Text markup (YGO::TEXT::Layout_BreakLines 0x140764AC0)

* `@` + one of `0-9`, `A-G`: text colour `g_TextColorTable[n]` (0x140C8D170, RGBA): 0 F0F0F0 (normal), 1 002945, 2 37DD97, 3 0EC9FF,
  4 00EAFF, 5 6600FF, 6 FF8400, 7 0084FF, 8 FF00FF, 9 004D0B, A 0000BB, B B00C00, C 00D957, D 808080, E FFFFFF, F FFFFFF, G 0000FF.
  Only one character is read: `@71 [Name]` is colour 7, then the text "1 [Name]" (the numbered callouts on the card picture). `@0` resets.
* `@` + `H`-`Z`: a literal `@`.
* `@/` / `@|`: the alternate font on / off.
* `$R` base `(` reading `)`: ruby (furigana) - the reading shows small above the base (Japanese text).
* `{{`: an inline tag; U+E000-U+F8FF: inline icons.
* `\n` (or `\r\n`): new line.

## Editor

* **Visual**: chapters and topics in a tree, the title / sub-topic / picture / text of the selection, and a preview of the screen: the
  chapter list, the chapter's topics, the article with its picture and the markup applied (colours, fonts, ruby). Text, colours and
  structure are the game's; the panel positions are approximate (the screen's widget layout isn't traced yet).
* **Script**: the whole file as text - `# chapter`, `## topic`, `### sub-topic`, `#! picture N`, then the text (a text line starting
  with `#` or `\` gets a `\` in front). Switching back to Visual applies it; errors name the line.
* Languages: the drop-down switches between the language files there are; **New language...** starts another letter's file from the
  current text for translating.
* **Use a picture file...** gives the selected topic a PNG (its picture number, or the next free one from 101); it's written with Save.
* Files on disk find the game's pictures in the game folder above them (the one with YGO_2020.toc), else WolfX's remembered game folder.
