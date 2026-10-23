#include "Arenas.h"
#include "Common.h"

#include <Windows.h>
#include <cstdint>
#include <cstring>
#include <deque>
#include <format>
#include <string>
#include <vector>

#include <json.hpp>

#include "Logger.h"
#include "Yu-Gi-Oh-Memory.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    using namespace Campaign;

    struct ArenaEntry               // g_ArenaFolderTable row (0x10)
    {
        int32_t Id;
        int32_t Pad;
        const char* Folder;         // "arenas/<folder>/animlist.txt"; null ends the table
    };
    static_assert(sizeof(ArenaEntry) == 0x10, "ArenaEntry layout drifted");

    constexpr uintptr_t kImageBase = 0x140000000;
    constexpr uintptr_t kGameTable = 0x140A52290;   // g_ArenaFolderTable
    constexpr uintptr_t kLeaOperand = 0x14076F249;  // lea rbx, [rip+disp32] at 0x14076F246 (next 0x14076F24D)
    constexpr uintptr_t kLeaNext = 0x14076F24D;
    constexpr uintptr_t kCmpOperand = 0x14076F25F;  // cmp [rip+disp32], r8 at 0x14076F25C (entry 0's folder; next 0x14076F263)
    constexpr uintptr_t kCmpNext = 0x14076F263;

    std::deque<std::string> g_folders;   // the new arenas' folder names (the table points at them)

    void* AllocNearImage(size_t size)
    {
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(kImageBase);
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(kImageBase + dos->e_lfanew);
        uintptr_t address = (kImageBase + nt->OptionalHeader.SizeOfImage + 0xFFFF) & ~static_cast<uintptr_t>(0xFFFF);
        for (; address < kImageBase + 0x7FFF0000; address += 0x10000)
            if (void* memory = VirtualAlloc(reinterpret_cast<void*>(address), size, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE))
                return memory;
        return nullptr;
    }

    bool PointAt(uintptr_t operand, uintptr_t next, uintptr_t target, int32_t expected, const char* what)
    {
        const int64_t disp = static_cast<int64_t>(target) - static_cast<int64_t>(next);
        if (disp < INT32_MIN || disp > INT32_MAX)
            return false;
        const int32_t value = static_cast<int32_t>(disp);
        uint8_t from[4], to[4];
        std::memcpy(from, &expected, 4);
        std::memcpy(to, &value, 4);
        return YGO::Memory::PatchBytes(operand, { from[0], from[1], from[2], from[3] }, { to[0], to[1], to[2], to[3] }, what);
    }
}

namespace Arenas
{
    void Install()
    {
        std::vector<std::string> problems;
        const nlohmann::json root = YGO::Mods::ReadMerged("arenas.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", it is left out", MODULE_NAME, 2);
        if (!root.is_object() || !root.contains("arenas") || !root["arenas"].is_array())
            return;

        // the game's rows first (up to its terminator)
        std::vector<ArenaEntry> rows;
        // volatile: a constant game address can be folded away in Release (see feedback-release-absolute-address)
        for (const volatile ArenaEntry* row = reinterpret_cast<const volatile ArenaEntry*>(kGameTable); row->Folder; ++row)
            rows.push_back({ row->Id, 0, row->Folder });
        const size_t gameRows = rows.size();

        for (const auto& entry : root["arenas"])
        {
            if (!entry.is_object())
                continue;
            const int id = Int(entry, "id").value_or(0);
            const std::string folder = Str(entry, "folder").value_or("");
            std::string why;
            if (id <= 0)
                why = "needs an \"id\" above 0";
            else if (folder.empty() || folder.find_first_of("/\\:.") != std::string::npos)
                why = "needs a \"folder\" name (no slashes or dots)";
            else
                for (const ArenaEntry& row : rows)
                    if (row.Id == id)
                        why = std::format("id {} is already arena \"{}\"", id, row.Folder);
            if (!why.empty())
            {
                Logger::WriteLog(std::format("arenas.json: arena {} skipped: {}", id, why), MODULE_NAME, 2);
                continue;
            }
            g_folders.push_back(folder);
            rows.push_back({ id, 0, g_folders.back().c_str() });
        }
        if (rows.size() == gameRows)
            return;
        rows.push_back({ 0, 0, nullptr });

        auto* table = static_cast<ArenaEntry*>(AllocNearImage(rows.size() * sizeof(ArenaEntry)));
        if (!table)
        {
            Logger::WriteLog("Arenas: no memory near the game for the arena table, new arenas are left out", MODULE_NAME, 2);
            return;
        }
        std::memcpy(table, rows.data(), rows.size() * sizeof(ArenaEntry));
        const auto at = reinterpret_cast<uintptr_t>(table);
        const bool lea = PointAt(kLeaOperand, kLeaNext, at, static_cast<int32_t>(kGameTable - kLeaNext), "Arena table (lea)");
        const bool cmp = PointAt(kCmpOperand, kCmpNext, at + offsetof(ArenaEntry, Folder),
            static_cast<int32_t>(kGameTable + offsetof(ArenaEntry, Folder) - kCmpNext), "Arena table (first folder)");
        Logger::WriteLog(std::format("Arenas: {} new arena(s), table at 0x{:X}: {}", rows.size() - 1 - gameRows, at, lea && cmp ? "on" : "NOT patched"),
            MODULE_NAME, lea && cmp ? 0 : 2);
    }
}
