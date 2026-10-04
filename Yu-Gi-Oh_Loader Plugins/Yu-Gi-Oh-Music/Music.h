#pragma once

// Yu-Gi-Oh-Music: music slot overrides. Yu-Gi-Oh-Ex\music.json maps the game's music slots (by name or number, see
// docs/AudioSystem.md and docs/MusicPlugin.md) to files the plugin plays itself, everywhere or per arena / opponent / story duel;
// slots without an entry play the game's own Wwise music. Voice lines are Voice.h (voices.json).
namespace Music
{
    // Reads music.json and attaches the hooks (PlayMusicSlot, ShutdownSoundSlots, SoundManager_Suspend/Resume).
    void Setup();
    // Re-reads music.json. The track that is playing keeps playing until the game changes music.
    void Reload();
}
