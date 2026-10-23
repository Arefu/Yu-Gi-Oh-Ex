# Multi-effect routing regressions

Build `EffectRouting.vcxproj` with Visual Studio C++ tools (Release/x64), then run `bin/EffectRouting.exe`.
The test includes the production `EffectClone.cpp`, replaces the vanilla lookup pointers, and exercises row lookup, slot resolution,
draw counts, raw and explicit triggers, generic Spell/Trap fallback rows, three effect tables, and the shared source-lending policy.
The mock engine memory is local to the test process. It never attaches to the game or writes game/save files.

After building WolfX, run:

```powershell
./Test-Compiler.ps1 -BuildPath '<folder containing WolfX.dll and its content>'
node Test-Blocks.cjs '<same folder>'
./Test-Compiler.ps1 -BuildPath '<same folder>' # Recompile block output and compare its complete JSON.
```

These check both source orders, named clone blocks, mixed scripted/cloned bodies, three sources, stats, locks, and legacy single effects.
The JavaScript test consumes real parse trees produced by WolfX and runs the production block conversion functions.

For a live duel, use a Pendulum Effect monster with `as(6784); also as(4844);`, then another with the order reversed. In each case:

1. Activate it in a Pendulum Zone and confirm it draws two cards.
2. Normal or Special Summon a fresh copy and confirm the Stratos effect is offered and resolves.
3. Include the original source cards in a deck and repeat, checking that source availability does not change the result.
4. Test a custom id above 16383 to exercise scratch-id remapping.

The harness also checks separate trigger and activation offers, duplicate suppression, full native queues, and independently selected
effects sharing the same table, event, and source row. Selected effect parameters and native sub-effect numbers survive lookup and resolution.

Live validation: both composed summon draw effects resolved (drawing three cards in total). The user subsequently confirmed the multi-effect
menu worked in game with the test card. Independent per-effect once-per-turn limits remain outside this change; native card-wide limits apply.
