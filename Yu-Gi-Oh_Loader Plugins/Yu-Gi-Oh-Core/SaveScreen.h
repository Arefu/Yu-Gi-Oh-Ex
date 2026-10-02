#pragma once

// The save-select screen: a new RIX screen (id 100, Card Shop backdrop) that sits between the title screen and ScreenSignIn.
// Play on the title goes here instead of to SignIn (6); picking a slot points the save redirect at that slot's file and moves on to
// SignIn, which reads the save the way it always does. See docs/SaveSlots.md.
namespace SaveScreen
{
    // Puts in the hooks (ScreenTitle::Update and RIX::Scheduler::RunJobs). Call after SaveSlots::Install. Does nothing when
    // the screen is not wanted (one slot, or SaveSlot in Config.ini).
    bool Install();
}
