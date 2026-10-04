# WolfX: standard and additional content

WolfX is the one editor (WolfEx was merged into it on 2026-10-01). Open the game's **YGO_2020.dat** (File > Open YGO_2020.dat, Ctrl+O)
or an **extracted YGO_2020 folder** of any name (File > Open extracted folder, Ctrl+Shift+O). With nothing open the window shows a start
screen with both, the Steam install when it finds one (`GameLocator.cs`: Steam's registry path + libraryfolders.vdf) and the recent list;
a folder or YGO_2020.dat dropped on the window opens too. The title bar says what is open. If files every page needs are missing (card
bins, the art and deck/pack `.zib`s) the status bar says so (click it for the list).

The **New cards** page is laid out like the Card Manager (`CardSummary.cs` is the shared preview): the custom cards on the left, the
picked card's face and summary, then Properties / Text / Art tabs; "Genres, related cards, links..." shows the card in the Card Manager.
Its Properties tab has the Card Manager's fields in the same order and the same "13: Spell" lists (plus the pendulum `scale` the plugin
reads), then what only a new card has (limitation, copies owned from the start).

**Larger previews** (`Tools/Shared/Editors/PreviewPopOut.cs`): every preview (card face, card art, How to Play, tutorial, story scene,
animlist, sprite sheet, text, page designer) has a small button in its top-right corner (double-click works on most) that moves the live
preview into its own big window, in the shape of what it shows (16:9 for game screens, the card's shape for a card; `SetShape`) as large
as fits the screen; it keeps updating and stays interactive, and Esc / closing it puts it back. The story scene and tutorial windows carry
their playback buttons (`PopOutAction`). The **Story editor** plays a scene (Play / Pause, Space on the step list): each step stays up
for as long as its text takes to read. Pages are laid out at
least 760 x 470: in a smaller window they scroll instead of squeezing (`WolfUI.Scrolling`).

**Effects as blocks**: the Effects page and the Effect library have a Script tab and a Blocks tab, always in step
(`Content/ScriptBlocksTabs.cs`): a block change rewrites the script at once; the blocks are redrawn when their tab opens and when the
script changes while it is open. While the script doesn't parse (or uses something with no block yet) the blocks keep their last good
state and are locked with a note, so they never overwrite it. The blocks are Blockly (Apache 2.0, shipped in `Content/Blocks`, offline) in a WebView2
(NuGet `Microsoft.Web.WebView2`; needs the Edge WebView2 Runtime Windows 10/11 already has). WolfX parses the script and hands the page the
plain parse tree (`EffectScriptTree.cs`); `Content/Blocks/effect-blocks.js` holds every block, the tree -> blocks loader and the blocks ->
script generator, so a new action or cost in the grammar is one table entry there (ACTIONS / COSTS). Checked: all 1171 library scripts go
script -> blocks -> script to the same parse tree (`WolfX.exe --script-tree <in> <out>` dumps trees). The **Effect library** is editable:
changes to the game cards' scripts and entries of your own are kept in `%APPDATA%\WolfX\effect_library.json` (Ctrl+S; "Revert" gives
the generated script back; effect_reference.json is never written).

Every page works on that data (`Tools/Shared/Editors/GameFiles.cs`, `GameFolderFiles.Current`) and saves by one rule:

* **Standard content** - a change to something the game already has - goes back into the game's own file, in the game's own format.
  In the game folder that is **inside `YGO_2020.dat`** (see below); a loose file in `<game>\YGO_2020\` (the loose-file folder, `[Yu-Gi-Oh-Core] LooseLoading=1`)
  stays loose. In an extracted folder it is the file there.
* **Additional content** - something the game's files can't hold (custom cards, new entries past the game's limits, new packs, pages) -
  goes to **JSON in `<game>\Yu-Gi-Oh-Ex`**, which a launcher plugin applies. (For an extracted folder, `Yu-Gi-Oh-Ex` is next to it.)

Each page's header says which files it writes and which plugin its JSON needs.

| Page | Standard (game files) | Additional (Yu-Gi-Oh-Ex) | Plugin |
|---|---|---|---|
| Card Manager | `bin\CARD_Prop.bin`, `bin\CARD_Indx/Name/Desc_<L>.bin`, `bin\CARD_Pass.bin`, `bin\CARD_Named.bin` (game cards) | custom cards: `cards.json` via New cards; genres / related / links as below | |
| Card genres | `bin\CARD_Genre.bin` | `genres.json`: cards not in CARD_IntID.bin | Yu-Gi-Oh-MoreCards (`Genres.cpp`) |
| Related cards | `bin\tagdata.bin`, `bin\taginfo_<L>.bin` | `relatedcards.json`: new tags, custom cards, related cards that are custom or use a new tag | Yu-Gi-Oh-MoreCards (`Related.cpp`) |
| Card links | `bin\CARD_Link.bin` | `cardlinks.json`: links with custom cards / archetypes (419+) | none yet (the game never reads the file) |
| Card ids | `bin\CARD_IntID.bin` (game ids only) | - (custom cards get slots from Yu-Gi-Oh-MoreCards) | |
| Name sort | `bin\CARD_Sort_<L>.bin`, `CARD_Sort2_<L>.bin` | - (custom cards are ranked by the plugin) | |
| Text tables | `bin\WORD_/DLG_ Indx + Text` (the game's entries) | `text.json`: entries added after the game's | Yu-Gi-Oh-MoreCards (`Text.cpp`; Core had it 2026-10-02 to 2026-10-04) |
| Characters | `main\chardata_<L>.bin` (the game's characters) | `characters.json`: new characters | Yu-Gi-Oh-Campaign |
| Decks | `main\deckdata_<L>.bin`, `decks.zib` | `decks.json`: new decks, and the cards of a game deck that holds custom cards | Yu-Gi-Oh-Campaign |
| Story editor | `main\dueldata_<L>.bin`, `main\scriptdata_<L>.bin` | `storyduels.json`, `storyscripts.json`: new duels and scenes | Yu-Gi-Oh-Campaign |
| Tutorials | `duel\tutorial\steam_tutorial_NN_<L>.bin` (numbers the game has) | `tutorials\*.json`: new numbers | none yet |
| How to Play | `main\howto_db\howtoplay_<L>.bin`, `main\howto_img\help_duelimg_NNN.png` | - (no limits: everything fits the game's file) | |
| Sprite sheets | the game's `.dfymoo` (+ `.png` when replaced) | `sprites\*.json`: new sheets | none yet |
| Animlists | the game's animlist + your pictures | - (the game only plays the animations it lists) | |
| Packs | `main\packdefdata_<L>.bin`, `packs.zib` | | |
| Strings | `strings\Strings_STEAM_<L>.BND`, `main\ui\credits\credits.dat` | | |
| Forbidden & Limited | `bin\pd_limits.bin` | | |
| New cards / Effects | | `cards.json` + art (effects inside it) | Yu-Gi-Oh-MoreCards (+ Yu-Gi-Oh-Effects for effects) |
| New packs | | `packs.json` | Yu-Gi-Oh-BetterCardShop |
| Unlocks | | `unlocks.json` | Yu-Gi-Oh-MoreCards |
| Pages / Menus | | `pages\*.json`, `menus\menus.json` | Yu-Gi-Oh-RIX |

JSON the old WolfEx wrote for game content (changes to game entries in `genres.json`, a tutorial or How to Play JSON for the game's own
file...) still opens on top of the game's data; the next save moves it into the game's file and leaves only additional content in the JSON.
Each JSON file is deleted when nothing is left in it.

## The Card Manager

`Tools/WolfX/CardManager.cs`. Every card (the game's and cards.json's) in one list; the picked card's face drawn **as the game draws
it** (`Tools/Shared/Editors/GameCardPainter.cs`, following docs/CardRendering.md: the archive's frames incl. Pendulum / Link / Xyz ones,
its bitmap fonts from `fontbin\` - Matrix Caps 21 name, Stone Serif type lines, Matrix Book 18..10 text, CARD_ATKDEF - the
`ICON_ID_ATTR_L_*`, `ICON_ID_LEVEL/RANK`, `ICON_ID_ICON_CARD_L_*` and `ICON_ID_LINK_*` icons, which sit on a 400 x 580 canvas), its name,
type line, stats and text, and tabs. (DuelIt keeps the AnimeCards layout of `CardFacePainter.cs`.)
Link arrows are the DEF bits of a Link monster (`CardRecord.LinkArrows`; cards.json `linkmarkers`), edited with `LinkArrowPicker`. The
game has **one** Pendulum Scale per card (CARD_Prop's 4 bits at 23); the 4 bits at 27 are something else (0-3 on the game's cards).

* **Properties**: kind, attribute, type, level / rank / link rating, ATK, DEF (or "?"), Spell/Trap icon, pendulum scales, password,
  archetypes. **Text**: name and text in every language. Both for game cards (`File Type Libraries/CARD_Props/CardTable.cs`,
  `CARD_Pass`, `CARD_Named`; all round-trip byte for byte); a custom card has an "Edit on New cards" button instead.
* **Genres / Related cards / Links**: the Card genres, Related cards and Card links pages themselves (one instance each, `ICardFocus`),
  showing only the picked card while they are in the Card Manager, the whole page again when opened from the list on the left.

`WolfX.exe --card 4007` opens it on a card.

## Saving: the patch archive (YGO_2020-Ex), the game's archive is never written

Since 2026-10-02 WolfX keeps every change to the game's files in a **patch archive** next to the game's own, like an IPS patch:

```
<game>\YGO_2020.toc / .dat         the game's, never written
<game>\YGO_2020-Ex.toc / .dat      only the files WolfX changed: "UB\n", per file u32 path length, path, u64 size, u64 offset;
                                   the .dat holds just those files, each padded to 4 bytes
```

* The order is `[Yu-Gi-Oh-Core] FileOrder`: `loose` (default) = a loose file wins over the patch, `patch` = WolfX's patch wins; the
  game's archive is always last. Loose files count only with `[Yu-Gi-Oh-Core] LooseLoading=1` (folder `FolderName`, default YGO_2020, off
  by default). The patch needs nothing switched on: Core is always loaded and mounts it whenever the files exist.
  This was the Yu-Gi-Oh-BetterLoad plugin until 2026-10-04; it is folded into Core (Loading.cpp, Patch.cpp) and its `[Yu-Gi-Oh-BetterLoad]`
  keys are **not read any more** (a breaking change: move them to `[Yu-Gi-Oh-Core]`).
  WolfX's **Files > Game files & loading** page sets all of it and lists every patched / loose file with the copy the game loads.
* WolfX (`Tools/Shared/Editors/GameFiles.cs`) reads by the same rules (and saves into the copy the game loads). Each save
  rewrites the patch with every file it already had plus the new ones (`TocArchive.WritePatch`; temporary names, moved into place, the
  .toc last). Files are copied across in pieces (`TocArchive.PatchSource`), never held in memory, because a patch with changed card art
  holds a 600 MB art .zib.
* **Yu-Gi-Oh-Core** (`Yu-Gi-Oh-Core/Patch.cpp`) mounts the patch as a second game archive (the game's own archive object and
  FS::LoadDAT, which reads the `UB` toc) when the game mounts YGO_2020 (Archives_Mount 0x14080E1F0). `g_Archives` only holds one pointer, so
  the patch is kept aside and the per-archive functions are sent to it for the files it has: Load_FileContent (0x14080DDE0, whole files; also
  serves the loose files in FileOrder's order), Archive_OpenEntry (0x14080DD40, files read as **streams**: the art .zibs, decks.zib),
  Archive_GetFileSize and Archive_HasFile (so sizes match the copy served). Everything else comes from the game's archive.
* `[Yu-Gi-Oh-Core] PatchArchive` (Config.ini, also in WolfX's Config editor) names it: default `YGO_2020-Ex`, empty = off. WolfX saves
  into the same name.
* With no patch files (or `PatchArchive=` empty) the game is exactly vanilla. **File > Remove WolfX's patch** deletes the two files (and undoes in-place saves an older
  WolfX made into YGO_2020.dat, below). Your Yu-Gi-Oh-Ex JSON is not touched. Nothing is written while the game is running.
* **Card art** (2026-10-04): the Card Manager's **Art** tab shows a game card's pictures as two slots, **Censored**
  (`2020.full.illust_j.jpg.zib`, every card) and, for the cards that have one, **Uncensored** (`2020.full.illust_a.jpg.zib`); a card
  without one has "Add an uncensored picture...". Each slot is changed on its own (choose, paste or drop; Export; Use the game's / Take it
  out). A picture is made into the game's format (square, 304 x 304, baseline JPEG, `<Konami id>.jpg`) and Save writes each changed .zib
  into the patch, **YGO_2020-Ex.dat** (WolfX refuses a PatchArchive named like the game's archive, so YGO_2020.dat is never written), streamed by
  `ZibArchive.Rewrite` (`Tools/Shared/Editors/CardArt.cs` `VanillaArt`). The game opens those .zibs as streams (CardArt_OpenZibs 0x14086CD60
  -> Zib_OpenStream -> Stream_Open -> Archive_OpenEntry), which is why Core redirects Archive_OpenEntry. Rewrite tested on the real
  .zibs (every other picture byte-equal, about a second for the 600 MB one). [Not yet confirmed in game: check a changed card's ATK, a deck,
  a pack and a changed card's art after saving.]

### Older WolfX: saving inside YGO_2020.dat (still undone by Remove)

`StartingCollection/TocArchive.cs` `Write`. The game's loader (FS::LoadDAT 0x14080D3D0) reads two kinds of toc, picked by its first bytes:
the text one it ships (`UT`) and a binary one (`UB`: path, u64 size, u64 **offset** per file); a first byte `E` means an encrypted .dat.
Before 2026-10-02 WolfX copied the toc to `YGO_2020.toc.original`, wrote changed files after the original end of the .dat and switched the
toc to the binary kind. WolfX no longer does that; `RestoreOriginal` still puts the original toc back and cuts the .dat if it finds one.

## Which plugins the content needs: content.json

A plugin's manifest (`<DLL>.json`, [PluginManifests.md](PluginManifests.md)) lists the Yu-Gi-Oh-Ex files it applies in `"content"`
(`"cards.json"`, `"pages\\"` for a folder). WolfX reads those from the plugin folder the game uses (`[Yu-Gi-Oh-Core] PluginsPath`) and
keeps `Yu-Gi-Oh-Ex\content.json` up to date (`Tools/WolfX/ContentManifest.cs`; it watches the folder, so any page's save, or an edit by
hand, updates it):

```json
{ "content": [ { "file": "cards.json", "plugins": [ "Yu-Gi-Oh-MoreCards", "Yu-Gi-Oh-Effects" ] },
               { "file": "tutorials\\", "plugins": [], "note": "no plugin applies this yet" } ] }
```

When the game starts, the loader (`Yu-Gi-Oh_Loader/Game.cpp`, `YGO::Manifest::ApplyContent`) switches those plugins on, with everything
they require, and shows a message naming the ones that aren't installed. Because the file names the plugins, a Yu-Gi-Oh-Ex folder copied
to another PC says what it needs there. Only content a plugin applies (and the WolfX formats no plugin reads yet) is listed; backups,
pictures `cards.json` points at and Core's `saves.json` are not.

## Rules for new pages

* Implement `IGameEditor` (`Tools/Shared/Editors/IGameEditor.cs`): `Open(files)` reads from the data (no file dialogs), `Save()` follows
  the rule above, `Files` lists the game files it shows (so it reopens when another page writes them), `SavesTo` is the header line.
* Read and write game files only through `GameFolderFiles` (`Read`, `Write`), never `File.*` on game paths.
* What counts as "the game has it": `GameContent` (card ids 3900-14968, archetypes below 419), or the entry being in the game's file as opened.
* The JSON form lives in the File Type Library, next to the binary one (`ToJson`/`FromJson`, `Diff`/`Apply`), with a round-trip test.
  Give every `JsonSerializerOptions` a `TypeInfoResolver` (a value added as `JsonArray.Add(string)` can't be written without one in .NET 8).
* The plugin side follows `Text.cpp`: read the JSON after the game loads its own, hook the getter or loader, fall back to the game's data.
