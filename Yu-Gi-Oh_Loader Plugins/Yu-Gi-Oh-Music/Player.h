#pragma once
#include <cstdint>
#include <string>

// The plugin's own music player (miniaudio): the game's Wwise engine can't play loose files, so overridden music slots are
// played here while the Wwise slot stays silent. One track plays at a time; a replaced track can fade out underneath the new one.
// The audio device is opened on the first Play, never in DllMain (loader lock).
namespace Player
{
    struct Track
    {
        std::wstring Path;            // absolute path to a .mp3 / .wav / .flac
        float Volume = 1.0f;          // this track's own volume, on top of the game's music volume
        bool Loop = true;
        double LoopStart = 0.0;       // seconds; where a loop jumps back to
        double LoopEnd = 0.0;         // seconds; 0 = the end of the file
        uint32_t FadeInMs = 0;
        uint32_t FadeOutMs = 0;       // used when this track is replaced or stopped
    };

    // Starts the track, replacing (and fading out) the current one. False if the file can't be opened.
    bool Play(const Track& track);
    // Stops the current track (with its fade out unless immediate).
    void Stop(bool immediate = false);
    bool IsPlaying();

    // The game's music volume (0..1), applied to the music the player plays.
    void SetMasterVolume(float volume);

    // Voice lines (Voice.cpp): one at a time, on their own volume (the game's sound effect volume), separate from the music.
    // A new line replaces the one playing. False if the file can't be opened.
    bool PlayVoice(const std::wstring& path, float volume);
    void StopVoice();
    bool IsVoicePlaying();
    void SetVoiceVolume(float volume);
    // Lowers the plugin's music to `level` (1 = normal) over fadeMs, e.g. while a voice line plays.
    void SetDuck(float level, uint32_t fadeMs);
    // Follows the game's window: Wwise is suspended while the window is hidden, so is the player.
    void Suspend();
    void Resume();
    // Frees sounds that finished fading out (and a finished voice line); called regularly by the watcher thread.
    void Reap();
    void Shutdown();
}
