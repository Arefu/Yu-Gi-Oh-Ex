#pragma once
#include <string>

// Which archive the game opens and whether more than one game window may run. Settings are in [Yu-Gi-Oh-Core] of Config.ini (this was the
// Yu-Gi-Oh-BetterLoad plugin until 2026-10-04; its [Yu-Gi-Oh-BetterLoad] keys are no longer read):
//
//   Archive            = YGO_2020   the archive the game opens (<name>.toc / .dat); FS::LoadDAT (0x14080D3D0) is given this name
//   AllowMultiInstance = 0          1 = more than one game window (the game's single-instance mutex is skipped)
//   LooseLoading       = 0          1 = files in <game>\<FolderName>\<path> are used instead of the archive's (Patch.h serves them)
//   FolderName         = YGO_2020
namespace Loading
{
    bool Install();

    // The folder YuGiOh.exe is in, with a trailing backslash.
    const std::string& GameFolder();

    // FS::LoadDAT(archive, name) without this plugin's archive renaming: for mounting another archive (the patch, Patch.h). 0 = opened.
    __int64 OpenArchive(void* archive, const char* name);

    // A [Yu-Gi-Oh-Core] setting, else fallback (an empty value written on purpose counts as set).
    std::string Setting(const char* key, const char* fallback);
}
