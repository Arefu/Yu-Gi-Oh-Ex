# New campaign series (campaigns.json)

Yu-Gi-Oh-Campaign `Series.cpp`. Built 2026-10-11.

## The game (IDB)

* `CampaignSeriesSelect_BuildSeriesList` (0x140862140) fills the screen's series id vector (+3424) with 0-5 and sets the list (ListState at +656: cursor, count, top, shown) to 6 rows, 6 shown.
* `CampaignSeriesSelect_Refresh` (0x140862B90) draws the 6 panels (+720, 424 bytes each). Panel i always shows ids[i]; the highlight is cursor - top. Clicking a panel picks row top + i.
* `Campaign_GetSeriesLabel` (0x1407564C0) and `Campaign_GetSeriesLogoKey` (0x1407564A0) read a 6-entry table at 0x140A4DEF0 and return nothing for 6 and up.
* Every duel list read goes through `Campaign_GetSeriesDuelCount` (0x14074A220) and `Campaign_GetDuelIdAt` (0x14074A200) on `g_CampaignSeries[6]`.
* Save: Section_0x1B70 = blob + 0x1B70. Its dword 0 is the current series; series s is a 1208-byte block at +8 + 1208 * s (u32 unlocked, then 24 bytes per duel order). There are 6 blocks and no room for more.
* The game writes the save only through `WriteSaveFile` (0x1408DA240), which Yu-Gi-Oh-Core replaces.

## How a new series works

* The list gets one row per new series and scrolls: the 6 panels are a window on it. Above and below sit the arrows the campaign duel list uses to show there is more (pdui/doShared `arrow_1`, `arrow_2` on hover; the up one is the same picture flipped), only when there is more that way; clicking one scrolls a row. Tried and dropped first: a drawn scroll bar (the game's scroll bar art, loose PNGs in pdui\shared, can't be loaded on this screen; naming a PNG path as a sheet crashes the layer loader, YuGiOh.exe+0x747A98), "More series" text, and Next/Previous page panels.
* Every new-series row points at game slot 5. While the cursor is on one, the series **borrows** slot 5:
  * slot 5's save block is set aside;
  * the new series' block takes its place. New series progress is kept **in the save file**, through Yu-Gi-Oh-Core's save extension (tag `campaign.series`, after the game's 44008 bytes; see `Core_GetSaveExtension` in Yu-Gi-Oh-Core.h). Campaign hands it to Core and Core writes it. The file only grows once a new series has progress; the 44008-byte layout is packed with no free gap, so the data can't go inside it. A `<save>.series.bin` from the first build is taken in once;
  * its unlock flag is set by the series' rule;
  * slot 5's name, logo and duel list come from the new series.
* Moving to a game row, or going back out of the series select screen, puts slot 5 back and hands the progress to Core.
* `Core_AddSaveWriteFilter`: every save write is a copy with slot 5's own block put back (and the new series' progress handed to Core's save extension), re-checksummed. The game's 44008 bytes only ever hold the game's own data.

## campaigns.json

```json
{ "series": [ { "id": 6, "name": { "E": "TEST" }, "logo": 1, "unlock": "start" } ] }
```

* `id` is 6-63. The series' duels are storyduels.json duels with that `"series"` (orders 1-48; StoryDuels.cpp now accepts series 0-63).
* `logo` is the game series 1-6 whose logo it shows.
* `unlock` is `"start"` (the default: open from the start), `"never"`, or `{ "after": n }`: open once the **last duel** (highest order) of series n is won; n is a game series (0-5) or another new one.
* WolfX: **Campaign > Campaign series** edits campaigns.json (number, name per language, logo, Opens); **Story editor** lists the new series in its Series filter and the duel's Series box, so New duel / Duplicate make duels in them (saved to storyduels.json).

## Overall campaign %

`CampaignSeriesSelect_OnEnter` (0x140862970) shows "Campaign x% complete" as the sum of `Campaign_GetSeriesCompletion` (0x14085F910) for series 0-5 / 6. The hook works out the % of every series, the new ones included (a new series with no progress is 0 %), with the same rule (sku-1 duels; first state 3, and the second state 3 for every duel but order 1). While OnEnter averages, each of its 6 calls answers that overall figure, so the game's "/ 6" shows the total. The per-series % on the panels stays the series' own.

## Logos and the Campaign series page

* `"logo"` is 1-6 (a game series' logo) or a name: the series list shows sprite `logo_<name>` of `pdui\SeriesLogo.dfymoo` (the game's keys: classic, gx, 5ds, zexal, arcv, vrains; table 0x140A4DEF0). Add a new logo to that sheet (Sprite sheets page, Ex patch) and it is offered on the Campaign series page, which reads the sheet as the game will (patch included). The same sheet holds the shop's `wrap_series_1..6` banners.
* The Campaign series page lists the game's 6 series first (read-only, marked "(game)"), then the new ones.
