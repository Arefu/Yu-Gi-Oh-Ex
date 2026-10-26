# Multi-effect routing regressions

Build `EffectRouting.vcxproj` with Visual Studio C++ tools (Release/x64), then run `bin/EffectRouting.exe`.
The test includes the production `EffectClone.cpp`, replaces the vanilla lookup pointers, and exercises row lookup, slot resolution,
draw counts, raw and explicit triggers, generic Spell/Trap fallback rows, three effect tables, and the shared source-lending policy.
Since 2026-10-25 it also plays the plugin's own parts through IDA-transcribed copies of the game's state machines
(docs/EffectSystem.md sections 58-68, 74): `TestPhaseDriverStateMachine` (whose Standby / End Phase), `TestOwnTriggerStateMachine`
(own trigger rows and offers), `TestOwnMoveTriggers` (sent to the GY / banished, with conditions), `TestOwnAttackTriggers`,
`TestOwnSummonReactions` and `TestUseLimitStateMachine` (once per turn / per Duel, twice per turn). `TestRawPickCosts` (section 90) checks the
raw pick costs: the game's cost machine per verb, the hand picker's per-index scan, and the count hooks for the card's own id inside and between
the machine's steps. `TestOwnWatchers` (section 91) checks the watcher triggers after the leave-the-field and move evaluators: by battle / by
effect, whose card, a GY watcher on a hand-to-GY move, and the own rows per table. `TestBattlePhaseTriggers` (section 93) checks the Battle Phase trigger events (4 / 24), the end-of-Battle-Phase handler families and
`Check_Phase` over phases and turn players. `TestTargetVerbs` (section 92) checks the target verbs: the game's
change-position / face-down / take-control machine or the result step (ATK key 1040 with its amount, negate key 1027) in slot 0, the targeting
slots, and the filter's position / change bits.
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
menu worked in game with the test card. Per-effect once-per-turn limits: each once-per-turn effect or `limitGroup` gets the next of the
engine's classes 5-8 (`AssignLimitClasses`, docs/EffectSystem.md section 58); the routing test checks the assignment, and Test-Compiler checks `or` groups.
