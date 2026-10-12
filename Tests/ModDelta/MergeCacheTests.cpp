#include "MergeArchive.h"
#include <iostream>
#include <stdexcept>

int wmain(int argc, wchar_t** argv)
{
    if (argc != 2) return 2;
    const std::filesystem::path game(argv[1]);
    const auto mods = YGO::Mods::Active(game);
    const auto manifestPath = game / L"Mods" / L"merged.json";
    auto manifest = nlohmann::json::parse(std::ifstream(manifestPath));
    const auto original = manifest;
    int checks = 0;
    auto check = [&](bool ok, const char* message) { ++checks; if (!ok) throw std::runtime_error(message); };
    auto find = [&]() { return YGO::Mods::MergeCache::Find(game, mods, "YGO_2020", false); };
    auto write = [&]() { std::ofstream(manifestPath) << manifest.dump(); };
    try
    {
        check(mods.size() == 2, "fixture must have two active mods");
        check(!find().empty(), "C++ rejected the .NET-generated cache");
        check(YGO::Mods::MergeCache::Find(game, mods, "YGO_2020", true).empty(), "FileOrder change accepted");
        auto reordered = mods; std::reverse(reordered.begin(), reordered.end());
        check(YGO::Mods::MergeCache::Find(game, reordered, "YGO_2020", false).empty(), "Reordered mods accepted");
        auto disabled = mods; disabled.pop_back();
        check(YGO::Mods::MergeCache::Find(game, disabled, "YGO_2020", false).empty(), "Disabled mod accepted");
        check(YGO::Mods::MergeCache::Find(game, mods, "OtherArchive", false).empty(), "Different baseline accepted");
        manifest["inputs"][0]["size"] = -7; write();
        check(find().empty(), "Changed file stamp accepted");
        manifest = original; manifest["patch"] = "..\\outside"; write();
        check(find().empty(), "Out-of-cache archive path accepted");
        manifest = original; manifest["version"] = 99; write();
        check(find().empty(), "Unknown schema accepted");
        manifest = original; manifest["outputs"][0]["size"] = -7; write();
        check(find().empty(), "Changed generated archive accepted");
        manifest = original; manifest["patch"] = ""; write();
        bool current = false;
        check(YGO::Mods::MergeCache::Find(game, mods, "YGO_2020", false, &current).empty() && current,
              "Valid empty cache incorrectly classified as stale");
        manifest = original; write();
        const auto loose = mods[0].Folder / L"YGO_2020" / L"new.bin";
        std::filesystem::create_directories(loose.parent_path());
        std::ofstream(loose).put('x');
        const bool rejected = find().empty();
        std::filesystem::remove(loose);
        check(rejected, "New loose file accepted with stale cache");
        std::ofstream(manifestPath) << "broken json";
        check(find().empty(), "Malformed cache accepted");
        manifest = original; write();
        check(!find().empty(), "Valid cache failed after restoration");
        std::cout << "Native merge cache checks passed (" << checks << ").\n";
    }
    catch (const std::exception& ex)
    {
        manifest = original; write();
        std::cerr << ex.what() << '\n';
        return 1;
    }
    return 0;
}
