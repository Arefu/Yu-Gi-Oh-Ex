#pragma once
#include <cstddef>

// The custom-card effect engine's single extension point into the duel engine. See EffectDispatch.cpp
// for the full design note; short version: EffectCondition_CheckCardUsableAtTiming (0x1400679F0 in IDA)
// is the one function every effect-activation/special-summon-condition search in the duel engine funnels
// through, and it is a single giant hardcoded-id ladder, not a per-card table - there is nothing else to
// hook. This file currently only observes (logs) what it is asked about a custom card, as the ground
// truth needed before a real interpreter is designed. See memory note ygo-effects-moonshot-plan.
namespace EffectDispatch
{
    void Setup();

    // How many times a custom-range card id has been seen going through the dispatcher this session
    // (diagnostic counter, not used for anything else yet).
    size_t ObservedCount();
}
