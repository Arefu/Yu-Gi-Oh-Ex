# CARD_Kana1/2/3_#.bin (a card's index letters)

`bin/CARD_Kana1_<L>.bin`, `CARD_Kana2_<L>.bin` and `CARD_Kana3_<L>.bin` (L = E F G I J R S) hold the first, second and third
"index letter" of every card: one UTF-16 character per **internal** id, 10166 x 2 bytes, with internal id 0 meaning no card. The parser
is `File Type Libraries/CARD_Kana.bin` (`Types.CardKanaTable`). All 21 files round-trip byte for byte.

* **E F G I S:** the name's first three characters exactly as written (`Ins`, `Kur`, `B. `, `7 C`). All five languages match the names
  with no exceptions, so `CardKanaTable.FromName` rebuilds them.
* **J and R** (Japanese): the first three kana of the reading, in hiragana (`いんせ`, `くりぼ`). They have to be typed.

## What the exe does with it

* `Setup_CardPropTable` loads **only CARD_Kana1_#** (into `g_CardDataFiles.CardKana1`, +0xF0). For languages 5 and 6 it maps voiced kana
  to the plain ones through `word_140A51A20`. CARD_Kana2 and CARD_Kana3 are never loaded.
* `Get_CardIndexInitialFromKonamiId` (0x14076D480, previously misnamed `Get_CardImageDataFromKonamiId`) gives each card's index initial,
  which `Setup_FullCardProps` stores at `FULL_CARD_PROPS` +0x1A. **Only the Japanese build** (`g_bIsJpVersion`) takes it from
  CARD_Kana1. Every other build uses the first character of the card's name, skipping a leading `$R...(` ruby block.

So editing these files only changes the Japanese release. Elsewhere, renaming the card is what changes its initial.

## WolfX

**Card Manager > Text > Index letters:** the three letters in the picked language, saved into the three files for that language. **From
name** fills them from the name. Renaming a card in E F G I S updates them too, as long as they still matched the old name, so a typed
reading is never overwritten.

## Custom cards

Yu-Gi-Oh-MoreCards (`Card.cpp`, WriteGameTableEntry) now gives a custom card its index initial: the first character of its name, the
non-Japanese rule. Before this, it had none, because `Setup_FullCardProps` ran before the card had a name.
