#pragma once
#include <cstdint>
#include <string>

// mp4 videos (docs/MediaPlugin.md "Videos"). The game plays its movies (summon cut-ins movies/<card id>.bik, movies/exodia.bik) through
// Bink into a texture it draws itself (Movie_Open 0x14080B440 / Movie_UpdateFrame 0x14080AF90). A movies/<name>.mp4 in Yu-Gi-Oh-Ex (or a
// mod) plays instead: Windows Media Foundation decodes it on a worker thread and each frame is uploaded into the game's movie texture on
// the game's own deferred context, where Bink's frame would have been drawn. A name the game has no .bik for opens exodia.bik as a muted,
// paused carrier, so the game's movie screen runs as usual.
namespace Video
{
    void Setup();

    // Plays movies/<name>.mp4 (or the game's .bik) as the game plays a summon cut-in. Queued: it starts on the game thread during a duel.
    void Play(const std::string& name);

    // A monster was summoned (Voice.cpp's field watch): plays movies/<card id>.mp4 when there is one and the game has no cut-in of its own.
    void OnSummon(uint16_t card);

    // Called on the game thread by the duel message pump: starts queued movies.
    void Pump();

    // Plays movies/<name>.mp4 over the whole game (startup intros, menus, story duels, the Video_Play export): letterboxed on black,
    // Esc or a click skips it. Queued; one at a time.
    void PlayFullScreen(const std::string& name);
    // The watcher thread: hooks Present once the game's swap chain exists, registers the RIX "video:<name>" actions, starts videos.json's
    // startup videos.
    void Tick();
    // videos.json "storyDuels": { "<id>": { "before": name, "after": name } }; -1 = not a story duel.
    void OnDuelStart(int storyDuel);
    void OnDuelEnd(int storyDuel);
}
