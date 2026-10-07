#pragma once

// Small game tweaks (moved from PatchMeOut 2026-10-07), [Yu-Gi-Oh-Core] in Config.ini:
//   PauseInBackground=0 (default): the game keeps running when its window loses focus (its pause, 0x14083C9F0, does nothing).
//   JapaneseVersion=1: the game acts as the Japanese release (g_bIsJpVersion 0x14332A348: the logo and Japanese-only text layouts);
//                      set by YGO::UI::UseJPLogo (0x1408734C0), which normally reads the release.
namespace Tweaks
{
    void Install();
}
