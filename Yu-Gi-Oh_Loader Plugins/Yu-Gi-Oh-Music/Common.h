#pragma once

#include <Windows.h>
#include <algorithm>
#include <cstdint>
#include <string>

// Helpers and game addresses shared by the Music plugin. The audio RE is in docs/AudioSystem.md (IDB: YGO::SOUND::).
namespace Music
{
    using PlayMusicSlot_t = char(__fastcall*)(int slot);
    using ShutdownSoundSlots_t = void(__fastcall*)();
    using SoundManagerSuspend_t = int64_t(__fastcall*)(int64_t manager, uint8_t renderAnyway);
    using SoundManagerResume_t = int64_t(__fastcall*)(int64_t manager);
    using SoundStop_t = void(__fastcall*)(int64_t sound, char immediate);
    using SoundPlay_t = bool(__fastcall*)(int64_t sound);
    using SoundSetVolume_t = char(__fastcall*)(int64_t sound, float volume);
    using SetVolumeLevel_t = void(__fastcall*)(int channel, int level);
    using SetArenaId_t = int64_t(__fastcall*)(unsigned int value);
    using SetArenaFromMatchType_t = int64_t(__fastcall*)();

    inline const auto Sound_Stop = reinterpret_cast<SoundStop_t>(0x14087A3C0);              // YGO::SOUND::Sound_Stop
    inline const auto Sound_Play = reinterpret_cast<SoundPlay_t>(0x14087A1C0);              // YGO::SOUND::Sound_Play
    inline const auto Sound_SetVolume = reinterpret_cast<SoundSetVolume_t>(0x14087A880);    // YGO::SOUND::Sound_SetVolumeClamped (bus volume 0..1)

    inline int64_t* const g_SoundSlots = reinterpret_cast<int64_t*>(0x143329500);         // 73 Wwise sound objects
    inline float* const g_MusicVolume = reinterpret_cast<float*>(0x143329748);            // 0..1 (level / 10)
    inline float* const g_SfxVolume = reinterpret_cast<float*>(0x143329750);              // 0..1 (level / 10); voice lines follow it
    inline bool* const g_bSoundSlotsReady = reinterpret_cast<bool*>(0x143329758);
    inline int* const g_CurrentMusicSlot = reinterpret_cast<int*>(0x14332975C);           // -1 = none

    inline const int* const g_CurrentArenaId = reinterpret_cast<const int*>(0x140C8D1F0);
    inline const int* const g_DuelSideCharacters = reinterpret_cast<const int*>(0x140C8D1F8); // [0] player, [1] opponent (chardata ids)

    constexpr int kSlotCount = 73;

    inline std::string Narrow(const std::wstring& w)
    {
        if (w.empty())
            return {};
        int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), static_cast<int>(w.size()), nullptr, 0, nullptr, nullptr);
        std::string s(n, '\0');
        WideCharToMultiByte(CP_UTF8, 0, w.data(), static_cast<int>(w.size()), s.data(), n, nullptr, nullptr);
        return s;
    }

    inline std::wstring Widen(const std::string& s)
    {
        if (s.empty())
            return {};
        int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0);
        std::wstring w(n, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), w.data(), n);
        return w;
    }

    // A path from a JSON file: absolute as is, otherwise relative to folder (which ends in a backslash). '/' works too.
    inline std::wstring ResolvePath(const std::wstring& folder, const std::string& file)
    {
        std::wstring path = Widen(file);
        std::replace(path.begin(), path.end(), L'/', L'\\');
        const bool absolute = path.size() > 1 && (path[1] == L':' || (path[0] == L'\\' && path[1] == L'\\'));
        return absolute ? path : folder + path;
    }

    inline std::wstring GameFolder()
    {
        wchar_t path[MAX_PATH]{};
        GetModuleFileNameW(nullptr, path, MAX_PATH);
        std::wstring full(path);
        return full.substr(0, full.find_last_of(L'\\') + 1);
    }
}
