#include <Windows.h>

#include <format>
#include <memory>
#include <mutex>
#include <vector>

#include <miniaudio.h>

#include "Common.h"
#include "Logger.h"
#include "Player.h"

namespace Player
{
    namespace
    {
        struct Playing
        {
            ma_sound Sound{};
            uint32_t FadeOutMs = 0;
        };

        std::recursive_mutex g_Lock;
        ma_engine g_Engine{};
        bool g_EngineReady = false;
        bool g_EngineFailed = false;            // don't retry a device that failed to open on every slot change
        bool g_Suspended = false;
        float g_MasterVolume = 0.5f;
        float g_Duck = 1.0f;                    // music level while a voice line plays (1 = not ducked)
        float g_VoiceVolume = 0.5f;
        ma_sound_group g_MusicGroup{};          // music: the game's music volume x duck
        ma_sound_group g_VoiceGroup{};          // voice lines: the game's sound effect volume
        std::unique_ptr<Playing> g_Current;
        std::unique_ptr<ma_sound> g_Voice;      // the voice line playing (one at a time)
        std::unique_ptr<ma_sound> g_Voice2;     // a line said at the same time (PlayVoiceAlongside)
        std::vector<std::unique_ptr<Playing>> g_Retired;   // fading out; freed by Reap once silent

        // A video's sound track: the decoded PCM, the miniaudio buffer reading it and the sound playing the buffer.
        struct Pcm
        {
            std::vector<float> Samples;
            ma_audio_buffer Buffer{};
            ma_sound Sound{};
            uint32_t SampleRate = 0;
        };
        std::unique_ptr<Pcm> g_Pcm;

        void StopPcmLocked()
        {
            if (!g_Pcm)
                return;
            ma_sound_uninit(&g_Pcm->Sound);
            ma_audio_buffer_uninit(&g_Pcm->Buffer);
            g_Pcm.reset();
        }

        bool EnsureEngine()
        {
            if (g_EngineReady)
                return true;
            if (g_EngineFailed)
                return false;
            ma_engine_config config = ma_engine_config_init();
            config.noAutoStart = g_Suspended ? MA_TRUE : MA_FALSE;
            const ma_result result = ma_engine_init(&config, &g_Engine);
            if (result != MA_SUCCESS)
            {
                g_EngineFailed = true;
                Logger::Log(std::format("Audio device failed to open (miniaudio error {}): custom music is off, the game's own music plays", static_cast<int>(result)), MODULE_NAME, 2);
                return false;
            }
            ma_sound_group_init(&g_Engine, 0, nullptr, &g_MusicGroup);
            ma_sound_group_init(&g_Engine, 0, nullptr, &g_VoiceGroup);
            ma_sound_group_set_volume(&g_MusicGroup, g_MasterVolume);
            ma_sound_group_set_fade_in_milliseconds(&g_MusicGroup, -1.0f, g_Duck, 0);
            ma_sound_group_set_volume(&g_VoiceGroup, g_VoiceVolume);
            g_EngineReady = true;
            Logger::Log(std::format("Audio device open ({} Hz)", ma_engine_get_sample_rate(&g_Engine)), MODULE_NAME, 0);
            return true;
        }

        void Retire(std::unique_ptr<Playing> playing, bool immediate)
        {
            if (!playing)
                return;
            if (immediate || playing->FadeOutMs == 0 || g_Suspended)
            {
                ma_sound_uninit(&playing->Sound);
                return;
            }
            ma_sound_stop_with_fade_in_milliseconds(&playing->Sound, playing->FadeOutMs);
            g_Retired.push_back(std::move(playing));
        }

        void StopOne(std::unique_ptr<ma_sound>& voice)
        {
            if (!voice)
                return;
            ma_sound_uninit(voice.get());
            voice.reset();
        }

        void StopVoiceLocked()
        {
            StopOne(g_Voice);
            StopOne(g_Voice2);
        }

        std::unique_ptr<ma_sound> OpenVoice(const std::wstring& path, float volume)
        {
            auto voice = std::make_unique<ma_sound>();
            const ma_result result = ma_sound_init_from_file_w(&g_Engine, path.c_str(), MA_SOUND_FLAG_STREAM | MA_SOUND_FLAG_NO_SPATIALIZATION,
                &g_VoiceGroup, nullptr, voice.get());
            if (result != MA_SUCCESS)
            {
                Logger::Log(std::format("Can't open voice line {} (miniaudio error {})", Music::Narrow(path), static_cast<int>(result)), MODULE_NAME, 2);
                return nullptr;
            }
            ma_sound_set_volume(voice.get(), volume);
            return voice;
        }

        // miniaudio multiplies a group's volume by its fader: the volume is the game's music setting, the fader is the duck.
        void ApplyMusicVolume()
        {
            if (g_EngineReady)
                ma_sound_group_set_volume(&g_MusicGroup, g_MasterVolume);
        }

        void ApplyDuck(uint32_t fadeMs)
        {
            if (g_EngineReady)
                ma_sound_group_set_fade_in_milliseconds(&g_MusicGroup, -1.0f, g_Duck, fadeMs);
        }
    }

    bool Play(const Track& track)
    {
        std::lock_guard lock(g_Lock);
        if (!EnsureEngine())
            return false;

        auto next = std::make_unique<Playing>();
        next->FadeOutMs = track.FadeOutMs;
        const ma_result result = ma_sound_init_from_file_w(&g_Engine, track.Path.c_str(), MA_SOUND_FLAG_STREAM | MA_SOUND_FLAG_NO_SPATIALIZATION,
            &g_MusicGroup, nullptr, &next->Sound);
        if (result != MA_SUCCESS)
        {
            Logger::Log(std::format("Can't open {} (miniaudio error {})", Music::Narrow(track.Path), static_cast<int>(result)), MODULE_NAME, 2);
            return false;
        }

        ma_sound_set_volume(&next->Sound, track.Volume);
        ma_sound_set_looping(&next->Sound, track.Loop ? MA_TRUE : MA_FALSE);
        if (track.Loop && (track.LoopStart > 0.0 || track.LoopEnd > 0.0))
        {
            ma_uint32 sampleRate = 0;
            ma_sound_get_data_format(&next->Sound, nullptr, nullptr, &sampleRate, nullptr, 0);
            const ma_uint64 begin = static_cast<ma_uint64>(track.LoopStart * sampleRate);
            const ma_uint64 end = track.LoopEnd > 0.0 ? static_cast<ma_uint64>(track.LoopEnd * sampleRate) : ~0ull;
            if (ma_data_source_set_loop_point_in_pcm_frames(ma_sound_get_data_source(&next->Sound), begin, end) != MA_SUCCESS)
                Logger::Log(std::format("{}: loop points ignored", Music::Narrow(track.Path)), MODULE_NAME, 1);
        }
        if (track.FadeInMs > 0)
            ma_sound_set_fade_in_milliseconds(&next->Sound, 0.0f, 1.0f, track.FadeInMs);

        Retire(std::move(g_Current), false);
        ma_sound_start(&next->Sound);
        g_Current = std::move(next);
        Logger::Log(std::format("Playing {}", Music::Narrow(track.Path)), MODULE_NAME, 0);
        return true;
    }

    void Stop(bool immediate)
    {
        std::lock_guard lock(g_Lock);
        Retire(std::move(g_Current), immediate);
    }

    bool IsPlaying()
    {
        std::lock_guard lock(g_Lock);
        return g_Current != nullptr;
    }

    void SetMasterVolume(float volume)
    {
        std::lock_guard lock(g_Lock);
        g_MasterVolume = volume;
        ApplyMusicVolume();
    }

    bool PlayVoice(const std::wstring& path, float volume)
    {
        std::lock_guard lock(g_Lock);
        if (!EnsureEngine())
            return false;
        auto voice = OpenVoice(path, volume);
        if (!voice)
            return false;
        StopVoiceLocked();
        ma_sound_start(voice.get());
        g_Voice = std::move(voice);
        return true;
    }

    bool PlayVoiceAlongside(const std::wstring& path, float volume)
    {
        std::lock_guard lock(g_Lock);
        if (!EnsureEngine())
            return false;
        auto voice = OpenVoice(path, volume);
        if (!voice)
            return false;
        StopOne(g_Voice2);
        ma_sound_start(voice.get());
        g_Voice2 = std::move(voice);
        return true;
    }

    bool PlayPcm(std::vector<float> samples, uint32_t channels, uint32_t sampleRate, float volume)
    {
        std::lock_guard lock(g_Lock);
        if (!EnsureEngine() || channels == 0 || sampleRate == 0 || samples.empty())
            return false;
        StopPcmLocked();
        auto pcm = std::make_unique<Pcm>();
        pcm->Samples = std::move(samples);
        pcm->SampleRate = sampleRate;
        ma_audio_buffer_config config = ma_audio_buffer_config_init(ma_format_f32, channels, pcm->Samples.size() / channels, pcm->Samples.data(), nullptr);
        config.sampleRate = sampleRate;
        if (ma_audio_buffer_init(&config, &pcm->Buffer) != MA_SUCCESS)
            return false;
        if (ma_sound_init_from_data_source(&g_Engine, &pcm->Buffer, MA_SOUND_FLAG_NO_SPATIALIZATION, &g_VoiceGroup, &pcm->Sound) != MA_SUCCESS)
        {
            ma_audio_buffer_uninit(&pcm->Buffer);
            return false;
        }
        ma_sound_set_volume(&pcm->Sound, volume);
        ma_sound_start(&pcm->Sound);
        g_Pcm = std::move(pcm);
        return true;
    }

    double PcmSeconds()
    {
        std::lock_guard lock(g_Lock);
        if (!g_Pcm)
            return -1.0;
        ma_uint64 cursor = 0;
        ma_data_source_get_cursor_in_pcm_frames(&g_Pcm->Buffer, &cursor);
        return static_cast<double>(cursor) / g_Pcm->SampleRate;
    }

    void StopPcm()
    {
        std::lock_guard lock(g_Lock);
        StopPcmLocked();
    }

    void StopVoice()
    {
        std::lock_guard lock(g_Lock);
        StopVoiceLocked();
    }

    bool IsVoicePlaying()
    {
        std::lock_guard lock(g_Lock);
        return (g_Voice && ma_sound_is_playing(g_Voice.get())) || (g_Voice2 && ma_sound_is_playing(g_Voice2.get()));
    }

    void SetVoiceVolume(float volume)
    {
        std::lock_guard lock(g_Lock);
        g_VoiceVolume = volume;
        if (g_EngineReady)
            ma_sound_group_set_volume(&g_VoiceGroup, volume);
    }

    void SetDuck(float level, uint32_t fadeMs)
    {
        std::lock_guard lock(g_Lock);
        if (level == g_Duck)
            return;
        g_Duck = level;
        ApplyDuck(fadeMs);
    }

    void Suspend()
    {
        std::lock_guard lock(g_Lock);
        if (g_Suspended)
            return;
        g_Suspended = true;
        if (g_EngineReady)
            ma_engine_stop(&g_Engine);
    }

    void Resume()
    {
        std::lock_guard lock(g_Lock);
        if (!g_Suspended)
            return;
        g_Suspended = false;
        if (g_EngineReady)
            ma_engine_start(&g_Engine);
    }

    void Reap()
    {
        std::lock_guard lock(g_Lock);
        std::erase_if(g_Retired, [](std::unique_ptr<Playing>& playing)
        {
            if (ma_sound_is_playing(&playing->Sound))
                return false;
            ma_sound_uninit(&playing->Sound);
            return true;
        });
        if (g_Voice && !g_Suspended && ma_sound_at_end(g_Voice.get()))
            StopOne(g_Voice);
        if (g_Voice2 && !g_Suspended && ma_sound_at_end(g_Voice2.get()))
            StopOne(g_Voice2);
    }

    void Shutdown()
    {
        std::lock_guard lock(g_Lock);
        Retire(std::move(g_Current), true);
        for (auto& playing : g_Retired)
            ma_sound_uninit(&playing->Sound);
        g_Retired.clear();
        StopVoiceLocked();
        StopPcmLocked();
        if (g_EngineReady)
        {
            ma_sound_group_uninit(&g_MusicGroup);
            ma_sound_group_uninit(&g_VoiceGroup);
            ma_engine_uninit(&g_Engine);
            g_EngineReady = false;
        }
    }
}
