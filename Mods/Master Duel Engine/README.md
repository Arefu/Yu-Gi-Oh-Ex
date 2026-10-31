# Master Duel Engine

Offline duels in Legacy of the Duelist: Link Evolution play on **Master Duel's own duel engine** (`duel.dll`) instead of the game's
built-in one, so every card Master Duel knows plays with Konami's own effects.

`duel.dll` is Konami's (Master Duel build `80460 : 20260629`, SHA-256
`97bd4d136e39b0872e4a8a9171632f1f0bd9bb04d69af08e836c56e684d43c44`) and is included with Konami's written permission. It is not
covered by Yu-Gi-Oh-Ex's license.

## Setting it up

1. Install the mod (it comes in `Mods.zip`, or copy this folder into `<game>\Mods\`).
2. Switch it on in the **Mod Manager**.
3. Start the game through `Yu-Gi-Oh_Loader.exe`.

That's all: Yu-Gi-Oh-Core finds `duel.dll` in this folder, and this mod's `Config.ini` turns `[Yu-Gi-Oh-Core] MasterDuelEngine` on
while the mod is on. Switch the mod off to go back to the game's engine. `console.log` (Console plugin) says
`Master Duel engine loaded from ...\Mods\Master Duel Engine\duel.dll` and shows a CPU-vs-CPU test duel at start-up.

## What to know

- Experimental. Tag, online and tutorial duels stay on the game's own engine.
- The Cards mod's new cards play with their Master Duel effects (by their Konami id).
- `MasterDuelLog=1` or `2` in this folder's `Config.ini` logs the engine's calls, for bug reports.
- Details: `docs/MasterDuelEngine.md` in the Yu-Gi-Oh-Ex repo.
