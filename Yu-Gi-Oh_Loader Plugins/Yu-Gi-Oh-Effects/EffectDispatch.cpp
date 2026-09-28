#include <Windows.h>
#include <detours.h>
#include <cstdint>
#include <format>

#include "EffectDispatch.h"
#include "Logger.h"

// ---------------------------------------------------------------------
// The custom-card effect engine: extension point.
//
// Every special-summon-condition scan, activation-timing search, Fusion/Ritual usability check etc. in
// the duel engine funnels through one function (named in IDA 2026-09-27: EffectCondition_
// CheckCardUsableAtTiming, at 0x1400679F0). It is called as (cardStruct, player, timing, index) once per
// candidate card per timing check. Its body is NOT a per-card table - it is a single giant
// `if (konamiId == X) {...} else if (konamiId == Y) {...}` ladder with 150+ ids hardcoded inline, so there
// is no discrete per-card handler anywhere to hook or reuse. A custom card's id will never match any of
// those literal comparisons, so today it always silently falls through to "no effect here" - which is
// exactly the harmless "summons fine, effect does nothing" behavior already observed in testing.
//
// This file is step one of building a real effect system for custom cards: it does NOT try to intercept
// or change anything yet. It only detours the dispatcher to OBSERVE and LOG every time it is asked about
// a card in the custom id range (>= kFirstExtraCardId), always calling through to the original
// unconditionally afterwards. The goal is ground truth: what timing/index values actually occur for a
// custom card across a real duel (normal summon, each main phase, on-destroy, etc.), which is needed
// before the interpreter that will eventually replace this passthrough can be designed responsibly.
//
// NOTE: a custom card above 0x3FFF currently plays under a BORROWED vanilla id for the duel (see
// Yu-Gi-Oh-Cards' duel-session remap, ygo-duel-id-remap-plan memory) - the id this hook sees for such a
// card is the borrowed id, not its real one, so it will not look like it is in the custom range at all.
// Reverse-resolving a borrowed id back to its real custom id needs a new cross-DLL export from
// Yu-Gi-Oh-Cards (the mapping - g_BorrowedToHigh - is not exposed yet); that is the next piece needed
// once we start actually reacting to these calls rather than just logging them.
namespace
{
    constexpr int kFirstExtraCardId = 0x3BC4;   // 15300, as in Yu-Gi-Oh-Cards/Card.h

    using CheckUsable_t = int64_t(__fastcall*)(uint16_t*, uint32_t, int, int);
    CheckUsable_t orig_CheckUsable = reinterpret_cast<CheckUsable_t>(0x1400679F0);

    size_t g_ObservedCount = 0;

    int64_t __fastcall Hook_CheckUsable(uint16_t* card, uint32_t player, int timing, int index)
    {
        if (card && *card >= kFirstExtraCardId)
        {
            ++g_ObservedCount;
            Logger::WriteLog(std::format("EffectCondition_CheckCardUsableAtTiming: custom card {} (player {}, timing {}, index {})",
                *card, player, timing, index), MODULE_NAME, 0);
        }
        return orig_CheckUsable(card, player, timing, index);
    }
}

void EffectDispatch::Setup()
{
    DetourAttach(&(PVOID&)orig_CheckUsable, Hook_CheckUsable);
    Logger::WriteLog("EffectDispatch: observing EffectCondition_CheckCardUsableAtTiming for custom cards (passthrough only, no logic yet)", MODULE_NAME, 0);
}

size_t EffectDispatch::ObservedCount()
{
    return g_ObservedCount;
}
