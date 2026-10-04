#include <Windows.h>
#include <algorithm>
#include <cctype>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <format>
#include <mutex>
#include <string>
#include <unordered_set>
#include <vector>

#include <detours.h>
#include "Loading.h"
#include "Logger.h"
#include "Patch.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    // ---- the game's archive functions (Patch.h) ----

    // Load_FileContent(archive, name, extra): the whole file in a buffer of max(size rounded up to 4, size + extra) bytes, zero filled past
    // the data (the game frees it). Null when the archive doesn't have it.
    using LoadFileContent_t = __int64*(__fastcall*)(void* archive, const char* name, size_t extra);
    uintptr_t orig_LoadFileContent = 0x14080DDE0;
    // Archive_OpenEntry(archive, name): an entry handle for reading the file as a stream, or null.
    using OpenEntry_t = void*(__fastcall*)(void* archive, const char* name);
    uintptr_t orig_OpenEntry = 0x14080DD40;
    // Archive_GetFileSize(archive, name): the size, or -1.
    using GetFileSize_t = __int64(__fastcall*)(void* archive, const char* name);
    uintptr_t orig_GetFileSize = 0x14080D290;
    // Archive_HasFile(archive, name)
    using HasFile_t = bool(__fastcall*)(void* archive, const char* name);
    uintptr_t orig_HasFile = 0x14080D270;
    // Archives_Mount(): opens YGO_2020 into g_Archives[0]
    using Mount_t = char(__fastcall*)();
    uintptr_t orig_Mount = 0x14080E1F0;

    // not hooked: used to mount and search the patches
    using FindEntry_t = void*(__fastcall*)(void* archive, const char* name);
    const FindEntry_t FindEntry = reinterpret_cast<FindEntry_t>(0x14080DFD0);
    using ArchiveCtor_t = void*(__fastcall*)(void* archive);
    const ArchiveCtor_t ArchiveCtor = reinterpret_cast<ArchiveCtor_t>(0x14080C3A0);
    using GameAlloc_t = void*(__fastcall*)(size_t);
    const GameAlloc_t GameAlloc = reinterpret_cast<GameAlloc_t>(0x14090D2B8);
    constexpr size_t kArchiveSize = 184;   // YGO_2020_Struct

    // ---- the layers: where a game file can come from, highest priority first ----

    struct Layer
    {
        std::string Label;                          // for the log: "Yu-Gi-Oh-Ex" or the mod's name
        std::filesystem::path Loose;                // loose files folder, empty = none
        bool LooseLive = false;                     // look on the disk every time (the game folder's own loose folder, edited while playing)
        std::unordered_set<std::string> LooseIndex; // otherwise: the files in it, as Key() spells them (built on first use)
        std::string PatchName;                      // <name>.toc / .dat relative to the game folder, empty = none
        void* Patch = nullptr;                      // the patch, mounted as a game archive (never in g_Archives)
        bool PatchFirst = false;                    // FileOrder=patch: the patch beats the loose files of the same layer
    };

    std::vector<Layer> g_layers;
    std::once_flag g_indexed;

    // An archive path as the index keeps it: upper case, backslashes.
    std::string Key(const char* name)
    {
        std::string key(name);
        for (char& c : key)
            c = c == '/' ? '\\' : static_cast<char>(std::toupper(static_cast<unsigned char>(c)));
        return key;
    }

    // The loose files of the mods are listed once, so a file the game asks for costs a lookup, not a disk check per mod.
    void IndexLoose()
    {
        for (Layer& layer : g_layers)
        {
            if (layer.Loose.empty() || layer.LooseLive)
                continue;
            std::error_code error;
            for (std::filesystem::recursive_directory_iterator it(layer.Loose, error), end; !error && it != end; it.increment(error))
            {
                if (it->is_regular_file(error))
                    layer.LooseIndex.insert(Key(YGO::Mods::Utf8(std::filesystem::relative(it->path(), layer.Loose, error).wstring()).c_str()));
            }
            Logger::WriteLog(std::format("{}: {} loose game file(s)", layer.Label, layer.LooseIndex.size()), MODULE_NAME, 0);
        }
    }

    bool IsOurPatch(void* archive)
    {
        return std::any_of(g_layers.begin(), g_layers.end(), [&](const Layer& layer) { return layer.Patch && layer.Patch == archive; });
    }

    // Mounts a layer's <name>.toc / .dat as an archive of its own, the way Archives_Mount mounts YGO_2020. FS::LoadDAT opens the files
    // relative to the game's working folder (the game folder), like it does "YGO_2020"; it formats "%s.toc" into a std::string, so a
    // path (Mods\<id>\YGO_2020-Ex) works as a name.
    void MountPatch(Layer& layer)
    {
        if (layer.Patch || layer.PatchName.empty())
            return;
        void* archive = GameAlloc(kArchiveSize);
        if (!archive)
            return;
        std::memset(archive, 0, kArchiveSize);
        ArchiveCtor(archive);
        const __int64 error = Loading::OpenArchive(archive, layer.PatchName.c_str());   // not renamed to [..] Archive
        if (error != 0)
        {
            // left allocated: the constructor's members have no destructor we can call safely here, and this happens at most once per layer
            Logger::WriteLog(std::format("{}: the patch archive {} couldn't be opened by the game (error {}), it is left out",
                                         layer.Label, layer.PatchName, error), MODULE_NAME, 2);
            return;
        }
        layer.Patch = archive;
        Logger::WriteLog(std::format("{}: patch archive {} mounted", layer.Label, layer.PatchName), MODULE_NAME, 0);
    }

    std::filesystem::path LoosePath(const Layer& layer, const char* name)
    {
        if (layer.Loose.empty() || !name)
            return {};
        if (!layer.LooseLive)
            return layer.LooseIndex.count(Key(name)) ? layer.Loose / YGO::Mods::Wide(name) : std::filesystem::path();
        std::error_code error;
        auto path = layer.Loose / YGO::Mods::Wide(name);
        return std::filesystem::is_regular_file(path, error) ? path : std::filesystem::path();
    }

    // Reads a loose file into a buffer from the game's allocator, as Load_FileContent returns it; null if it can't.
    __int64* ReadLoose(const std::filesystem::path& path, size_t extra)
    {
        FILE* file = nullptr;
        if (_wfopen_s(&file, path.c_str(), L"rb") != 0 || !file)
            return nullptr;
        __int64* result = nullptr;
        if (_fseeki64(file, 0, SEEK_END) == 0)
        {
            const size_t bytes = static_cast<size_t>(_ftelli64(file));
            _fseeki64(file, 0, SEEK_SET);
            const size_t total = (std::max)((bytes + 3) & ~size_t(3), bytes + extra);
            if (void* buffer = GameAlloc(total))
            {
                std::memset(buffer, 0, total);
                if (std::fread(buffer, 1, bytes, file) == bytes)
                    result = static_cast<__int64*>(buffer);
                // a short read leaves the buffer to the game's allocator (small, and only with a broken file)
            }
        }
        std::fclose(file);
        return result;
    }

    // Which copy of a file wins: the first layer that has it, 'l' loose or 'p' patch; null when only the archive asked has it.
    struct Pick
    {
        Layer* From = nullptr;
        char Kind = 0;
        std::filesystem::path Path;   // the loose file
    };

    Pick Source(void* archive, const char* name)
    {
        std::call_once(g_indexed, IndexLoose);
        if (!name || IsOurPatch(archive))
            return {};
        for (Layer& layer : g_layers)
        {
            const bool patch = layer.Patch && FindEntry(layer.Patch, name) != nullptr;
            if (patch && layer.PatchFirst)
                return { &layer, 'p', {} };
            if (auto path = LoosePath(layer, name); !path.empty())
                return { &layer, 'l', std::move(path) };
            if (patch)
                return { &layer, 'p', {} };
        }
        return {};
    }

    __int64* __fastcall Hook_LoadFileContent(void* archive, const char* name, size_t extra)
    {
        const Pick pick = Source(archive, name);
        if (pick.Kind == 'l')
        {
            if (__int64* data = ReadLoose(pick.Path, extra))
            {
                Logger::WriteLog(std::format("{} from {} (loose)", name, pick.From->Label), MODULE_NAME, 0);
                return data;
            }
        }
        else if (pick.Kind == 'p')
        {
            if (__int64* data = reinterpret_cast<LoadFileContent_t>(orig_LoadFileContent)(pick.From->Patch, name, extra))
            {
                Logger::WriteLog(std::format("{} from {} (patch)", name, pick.From->Label), MODULE_NAME, 0);
                return data;
            }
        }
        return reinterpret_cast<LoadFileContent_t>(orig_LoadFileContent)(archive, name, extra);
    }

    // Streams (the card art .zibs, decks.zib, ...): the first patch that has the file. A loose file can't be streamed (no archive object).
    void* __fastcall Hook_OpenEntry(void* archive, const char* name)
    {
        if (name && !IsOurPatch(archive))
        {
            for (Layer& layer : g_layers)
            {
                if (!layer.Patch || !FindEntry(layer.Patch, name))
                    continue;
                if (void* entry = reinterpret_cast<OpenEntry_t>(orig_OpenEntry)(layer.Patch, name))
                {
                    Logger::WriteLog(std::format("{} streamed from {} (patch)", name, layer.Label), MODULE_NAME, 0);
                    return entry;
                }
            }
        }
        return reinterpret_cast<OpenEntry_t>(orig_OpenEntry)(archive, name);
    }

    __int64 __fastcall Hook_GetFileSize(void* archive, const char* name)
    {
        const Pick pick = Source(archive, name);
        if (pick.Kind == 'l')
        {
            std::error_code error;
            const auto size = std::filesystem::file_size(pick.Path, error);
            if (!error)
                return static_cast<__int64>(size);
        }
        else if (pick.Kind == 'p')
            return reinterpret_cast<GetFileSize_t>(orig_GetFileSize)(pick.From->Patch, name);
        return reinterpret_cast<GetFileSize_t>(orig_GetFileSize)(archive, name);
    }

    bool __fastcall Hook_HasFile(void* archive, const char* name)
    {
        return Source(archive, name).Kind != 0 || reinterpret_cast<HasFile_t>(orig_HasFile)(archive, name);
    }

    char __fastcall Hook_Mount()
    {
        const char result = reinterpret_cast<Mount_t>(orig_Mount)();
        for (Layer& layer : g_layers)
            MountPatch(layer);
        return result;
    }

    bool HasPatch(const std::filesystem::path& game, const std::string& name)
    {
        std::error_code error;
        const auto base = game / YGO::Mods::Wide(name);
        return std::filesystem::exists(base.wstring() + L".toc", error) && std::filesystem::exists(base.wstring() + L".dat", error);
    }
}

bool Patch::Install()
{
    const std::filesystem::path game(YGO::Mods::Wide(Loading::GameFolder()));
    const bool patchFirst = _stricmp(Loading::Setting("FileOrder", "loose").c_str(), "patch") == 0;

    // The game folder's own files first (WolfX's workspace): [Yu-Gi-Oh-Core] LooseLoading / FolderName and PatchArchive.
    Layer local;
    local.Label = "Yu-Gi-Oh-Ex";
    local.LooseLive = true;
    local.PatchFirst = patchFirst;
    if (Loading::Setting("LooseLoading", "0") == "1")
    {
        const std::string folder = Loading::Setting("FolderName", "YGO_2020");
        const auto path = game / YGO::Mods::Wide(folder);
        if (std::filesystem::is_directory(path))
            local.Loose = path;
        else
            Logger::WriteLog(std::format("LooseLoading is on but '{}' isn't a folder: no loose files", YGO::Mods::Utf8(path.wstring())), MODULE_NAME, 2);
    }
    const std::string name = Loading::Setting("PatchArchive", "YGO_2020-Ex");
    const std::string archive = Loading::Setting("Archive", "YGO_2020");
    if (_stricmp(name.c_str(), "YGO_2020") == 0 || _stricmp(name.c_str(), archive.c_str()) == 0)
        Logger::WriteLog(std::format("PatchArchive is {}, the game's own archive: no patch (set it to YGO_2020-Ex)", name), MODULE_NAME, 2);
    else if (!name.empty())
    {
        if (HasPatch(game, name))
        {
            local.PatchName = name;
            Logger::WriteLog(std::format("Patch archive {}: mounted when the game mounts its own archive", name), MODULE_NAME, 0);
        }
        else
            Logger::WriteLog(std::format("No patch archive ({}.toc not found)", name), MODULE_NAME, 0);
    }
    g_layers.push_back(std::move(local));

    // Then the mods that are on, the last in the load order first (Yu-Gi-Oh-Mods.h): Mods\<id>\YGO_2020\ and Mods\<id>\YGO_2020-Ex.
    const std::vector<YGO::Mods::Mod> mods = YGO::Mods::Active(game);
    for (auto it = mods.rbegin(); it != mods.rend(); ++it)
    {
        Layer layer;
        layer.Label = "mod " + it->Name;
        layer.PatchFirst = patchFirst;
        if (std::filesystem::is_directory(it->Folder / L"YGO_2020"))
            layer.Loose = it->Folder / L"YGO_2020";
        const std::string patch = "Mods\\" + it->Id + "\\YGO_2020-Ex";
        if (HasPatch(game, patch))
            layer.PatchName = patch;
        if (!layer.Loose.empty() || !layer.PatchName.empty())
            g_layers.push_back(std::move(layer));
    }
    Logger::WriteLog(std::format("{} mod(s) on, {} change game files", mods.size(), g_layers.size() - 1), MODULE_NAME, 0);

    const bool anyPatch = std::any_of(g_layers.begin(), g_layers.end(), [](const Layer& layer) { return !layer.PatchName.empty(); });

    // Installed even without a patch: the same hooks serve the loose files (see Patch.h).
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_LoadFileContent, Hook_LoadFileContent);
    DetourAttach(&(PVOID&)orig_GetFileSize, Hook_GetFileSize);
    DetourAttach(&(PVOID&)orig_HasFile, Hook_HasFile);
    if (anyPatch)
    {
        DetourAttach(&(PVOID&)orig_OpenEntry, Hook_OpenEntry);
        DetourAttach(&(PVOID&)orig_Mount, Hook_Mount);
    }
    LONG err = DetourTransactionCommit();
    if (err != NO_ERROR)
        Logger::WriteLog(std::format("The game-file hooks could not be installed (Detours error {})", err), MODULE_NAME, 2);
    return err == NO_ERROR;
}
