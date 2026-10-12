# Mod delta and binary merge regression checks

On Windows with .NET 8 and the Windows SDK:

```powershell
dotnet build Tests/ModDelta/ModDelta.csproj -m:1
dotnet run --project Tests/ModDelta/ModDelta.csproj --no-build
```

The fixtures are synthetic and never open or write installed game data. Checks cover unchanged copies, independent changes,
same-record conflicts, additions/removals, rebuilt strings/offsets, duplicate card weights, streamed ZIB output, source precedence,
order/enabled-state changes, damaged output, and a modified baseline. Temporary fixture folders are created below the current directory.

For the native Core cache validator, use VS 2022 Build Tools with C++ and the Windows SDK. Generate a persistent fixture, build the
native project, then point it at the fixture:

```powershell
dotnet run --project Tests/ModDelta/ModDelta.csproj --no-build -- --fixture Tests/ModDelta/bin/fixture
msbuild Tests/ModDelta/MergeCacheTests.vcxproj /p:Configuration=Release /p:Platform=x64 /p:PreferredToolArchitecture=x64
Tests/ModDelta/bin/native/MergeCacheTests.exe Tests/ModDelta/bin/fixture
```

Native checks verify compatibility with the .NET-generated manifest and reject stale order, enable state, archive/settings, changed
input/output stamps, newly added loose files, malformed manifests and paths outside the cache namespace. The validator tests do not
launch or inject into the game.

## Live verification

Verified on Link Evolution for Windows using a separate copy of the game archive and an isolated unlocked test save:

1. Mod A supplied a loose `main/chardata_E.bin` copied from the original, renaming Alister (ID 75) to `MERGE ALPHA`.
2. Mod B supplied the same original table in `YGO_2020-Ex.toc/.dat`, renaming Arkana (ID 76) to `MERGE BETA`.
3. Preparing the active list produced one shared-table overlay with no conflicts.
4. With the rebuilt Core injected, both names appeared together under Duelist Challenges > Yu-Gi-Oh!.
5. Selecting `MERGE ALPHA` loaded its original Dragon Buster deck and reached the first playable turn, with the merged name displayed.

An additional mixed loose/patch fixture prepared 24 overlay files without conflicts. Each mod changed separate records or slots in
the same original files. The following results were observed in the running game on 2026-10-12:

| Format | Live result |
| --- | --- |
| `Strings_STEAM_E.BND` | Both `MERGE SINGLE PLAYER` and `MERGE DECK EDIT` appeared in the main menu. |
| `packdefdata_E.bin` | Grandpa and Mai showed `MERGE PACK ALPHA`/`BETA`, priced at 111/222. |
| Pack reward binary inside `packs.zib` | Purchasing Grandpa's pack produced seven ALPHA common cards and one BETA rare in the same opening. |
| Battle Pack binary inside `packs.zib` | Epic Dawn sealed generation produced ten ALPHA cards and ten BETA cards, one from each edited slot per pack. |
| `dueldata_E.bin` | Both `MERGE DUEL ALPHA` and `MERGE DUEL BETA` appeared in campaign selection. |
| `scriptdata_E.bin` | Selecting each duel displayed its respective inserted `MERGE SCENE` line. The second duel reached a playable turn. |
| CARD index/name/description triplet | Both renamed cards and their separate description markers rendered in the deck editor and rewards. Stock end-of-pool index sentinels are preserved and rebuilt. |
| `CARD_Prop.bin` | ALPHA/BETA displayed 3210/3320 ATK. The user additionally confirmed ALPHA's 3210 ATK during turn 3 of a duel. |
| `pd_limits.bin` | Both cards displayed the Limited (1) indicator. |
| WORD index/text pair | The ALPHA Dragon and BETA Warrior labels rendered in card details. |
| Large card-art ZIB | Both independently replaced images rendered together (Pot of Greed art on ALPHA, Raigeki art on BETA). The user also confirmed ALPHA's replacement image on the duel field. The approximately 600 MB archive was streamed. |
| `tagdata.bin` | BETA's Related Cards panel contained the single ALPHA card specified by the fixture. |

Core's runtime log also confirmed reads from the merged overlay for `deckdata_E.bin`, `CARD_IntID.bin`, `CARD_Same.bin` (the generic
8-byte adapter), `CARD_Genre.bin`, the DLG pair, `taginfo_E.bin`, `decks.zib`, and `credits.dat`. These are **load smoke checks only**:
their edited behavior was not individually exercised before the user took over the duel and stopped UI automation.
`CARD_Pass.bin` was generated and covered by the merge regression, but no runtime read or password behavior was observed.
The related-card text fixture initially used condition-based tags, whose display text the engine constructs from conditions;
changing their stored text alone therefore was not a visible taginfo test.

The live fixture exposed the stock CARD index's final end-of-pool pair, which the original merger incorrectly treated as another
card. The fix skips that pair when reading and rebuilds it when writing; the regression suite now includes a sentinel assertion
and passes 40 binary merge/cache checks. This report distinguishes visible behavior from successful file loading and does not
claim complete gameplay coverage of every adapter. Installed game files, mods, and the normal save were not modified.
