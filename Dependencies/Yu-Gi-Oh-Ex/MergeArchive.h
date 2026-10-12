#pragma once
#include "Yu-Gi-Oh-Mods.h"

// Matches Tools/Shared/Mods/ModMergeArchive.cs. Cache files are an optimization, never an authority for load order.
namespace YGO::Mods::MergeCache
{
    inline nlohmann::json Stamps(const std::filesystem::path& game, std::vector<std::wstring> files)
    {
        std::sort(files.begin(), files.end()); // .NET's ordinal UTF-16 path order
        auto result = nlohmann::json::array();
        for (const auto& file : files)
        {
            WIN32_FILE_ATTRIBUTE_DATA data{};
            long long size = -1, time = 0;
            if (GetFileAttributesExW((game / file).c_str(), GetFileExInfoStandard, &data)
                && !(data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY))
            {
                size = static_cast<long long>((static_cast<unsigned long long>(data.nFileSizeHigh) << 32) | data.nFileSizeLow);
                time = static_cast<long long>((static_cast<unsigned long long>(data.ftLastWriteTime.dwHighDateTime) << 32) | data.ftLastWriteTime.dwLowDateTime);
            }
            result.push_back({ {"path", Utf8(file)}, {"size", size}, {"time", time} });
        }
        return result;
    }

    inline nlohmann::json Snapshot(const std::filesystem::path& game, const std::vector<Mod>& active, const std::string& archive)
    {
        std::vector<std::wstring> files = { Wide(archive + ".toc"), Wide(archive + ".dat"), Wide(archive + ".toc.original") };
        for (const auto& mod : active)
        {
            const std::wstring prefix = L"Mods\\" + Wide(mod.Id) + L"\\";
            files.push_back(prefix + L"mod.json");
            files.push_back(prefix + L"YGO_2020-Ex.toc");
            files.push_back(prefix + L"YGO_2020-Ex.dat");
            const auto loose = mod.Folder / L"YGO_2020";
            if (std::filesystem::is_directory(loose))
                for (const auto& entry : std::filesystem::recursive_directory_iterator(loose))
                    if (entry.is_regular_file())
                        files.push_back(prefix + L"YGO_2020\\" + std::filesystem::relative(entry.path(), loose).wstring());
        }
        return Stamps(game, std::move(files));
    }

    inline std::string Find(const std::filesystem::path& game, const std::vector<Mod>& active,
                            const std::string& archive, bool patchFirst, bool* current = nullptr)
    {
        if (current) *current = false;
        try
        {
            std::ifstream stream(game / L"Mods" / L"merged.json");
            if (!stream) return {};
            const auto manifest = nlohmann::json::parse(stream);
            auto ids = nlohmann::json::array();
            for (const auto& mod : active) ids.push_back(mod.Id);
            if (manifest.at("version") != 1 || manifest.at("archive") != archive
                || manifest.at("patchFirst") != patchFirst || manifest.at("mods") != ids
                || manifest.at("inputs") != Snapshot(game, active, archive)) return {};
            const std::string patch = manifest.at("patch").get<std::string>();
            if (patch.empty())
            {
                if (current) *current = true; // Valid cache with no overlaps; no generated layer is needed.
                return {};
            }
            const std::string prefix = "Mods\\.merged\\", suffix = "\\overlay";
            if (patch.size() != prefix.size() + 32 + suffix.size() || patch.substr(0, prefix.size()) != prefix
                || patch.substr(prefix.size() + 32) != suffix) return {};
            for (size_t i = prefix.size(); i < prefix.size() + 32; ++i)
                if (!((patch[i] >= '0' && patch[i] <= '9') || (patch[i] >= 'a' && patch[i] <= 'f'))) return {};
            if (!std::filesystem::is_regular_file(game / Wide(patch + ".toc"))
                || !std::filesystem::is_regular_file(game / Wide(patch + ".dat"))) return {};
            if (manifest.at("outputs") != Stamps(game, { Wide(patch + ".toc"), Wide(patch + ".dat") })) return {};
            if (current) *current = true;
            return patch;
        }
        catch (const std::exception&) { return {}; } // incomplete, stale, or malformed cache: use ordinary layers
    }
}
