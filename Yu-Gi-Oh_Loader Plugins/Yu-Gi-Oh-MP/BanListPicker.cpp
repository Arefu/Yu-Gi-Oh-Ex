#include "BanListPicker.h"

#include <Windows.h>
#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <format>
#include <string>
#include <vector>

#include "BanList.h"
#include "Logger.h"
#include "Yu-Gi-Oh-RIX.h"

namespace
{
    constexpr int kPageSize = 8;   // rows a page shows (the menu has no scrolling)

    bool g_Registered = false;

    std::string Var(const char* name)
    {
        char buffer[256] = {};
        const int length = RIX::Functions().GetPageVar(name, buffer, sizeof(buffer));
        return length > 0 ? std::string(buffer, (std::min)(length, static_cast<int>(sizeof(buffer)) - 1)) : std::string();
    }

    std::wstring Wide(const std::string& text)
    {
        if (text.empty())
            return {};
        const int n = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring wide(n, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), n);
        return wide;
    }

    bool Any(const std::string& filter) { return filter.empty() || _stricmp(filter.c_str(), "all") == 0; }

    // Indexes into BanList::Saved() of the lists the filters let through.
    std::vector<size_t> Matching()
    {
        const std::string era = Var("banlist.era"), year = Var("banlist.year");
        std::vector<size_t> found;
        const auto& saved = BanList::Saved();
        for (size_t i = 0; i < saved.size(); ++i)
        {
            if (!Any(era) && _stricmp(saved[i].Era.c_str(), era.c_str()) != 0)
                continue;
            if (!Any(year) && saved[i].Date.rfind(year, 0) != 0)
                continue;
            found.push_back(i);
        }
        return found;
    }

    int Pages(size_t count) { return (std::max)(1, static_cast<int>((count + kPageSize - 1) / kPageSize)); }

    // var:banlist.page, kept inside the pages there are
    int CurrentPage(size_t count)
    {
        const int page = std::clamp(std::atoi(Var("banlist.page").c_str()), 0, Pages(count) - 1);
        RIX::Functions().SetPageVar("banlist.page", std::to_string(page).c_str());
        return page;
    }

    std::string Describe(const BanList::List& list)
    {
        std::string text;   // the formats are in the name already
        if (!list.Date.empty())
            text = "From " + list.Date;
        if (!list.Era.empty())
            text += (text.empty() ? "" : " - ") + list.Era + " era";
        text += std::format("{}{} Forbidden, {} Limited, {} Semi-Limited", text.empty() ? "" : " - ", list.Forbidden.size(), list.Limited.size(),
            list.SemiLimited.size());
        if (!list.Allowed.empty())
            text += std::format(" - {} cards out by then", list.Allowed.size());
        return text;
    }

    void __cdecl ListRows(void* rows, void*)
    {
        const std::vector<size_t> found = Matching();
        const int page = CurrentPage(found.size());
        const auto& saved = BanList::Saved();
        for (size_t i = static_cast<size_t>(page) * kPageSize; i < found.size() && i < static_cast<size_t>(page + 1) * kPageSize; ++i)
        {
            const BanList::List& list = saved[found[i]];
            RIX::Functions().PageListAdd(rows, Wide(list.Name).c_str(), Wide(Describe(list)).c_str(), std::to_string(found[i]).c_str());
        }
    }

    void __cdecl UseChosen(int, void*)
    {
        const std::string index = Var("banlist");
        const auto& saved = BanList::Saved();
        char* end = nullptr;
        const unsigned long at = std::strtoul(index.c_str(), &end, 10);
        if (index.empty() || *end || at >= saved.size())
        {
            Logger::WriteLog("Ban list picker: no list chosen (var:banlist = '" + index + "')", MODULE_NAME, 1);
            return;
        }
        BanList::UseCustom(saved[at]);
    }

    void __cdecl UseGame(int, void*) { BanList::UseGame(); }
    void __cdecl UseOff(int, void*) { BanList::UseOff(); }

    int Write(const std::string& text, char* buffer, int size)
    {
        if (size <= 0)
            return 0;
        const int length = (std::min)(static_cast<int>(text.size()), size - 1);
        std::memcpy(buffer, text.data(), length);
        buffer[length] = 0;
        return length;
    }

    int __cdecl Value(const char* argument, char* buffer, int size, void*)
    {
        const std::string name = argument ? argument : "";
        if (name == "banlist")
            return Write(BanList::CurrentMode() == BanList::Mode::Off ? "Off" : BanList::CurrentMode() == BanList::Mode::Custom
                ? BanList::CurrentList().Name : "Game", buffer, size);
        if (name == "banlist.count")
            return Write(std::to_string(Matching().size()), buffer, size);
        if (name == "banlist.pages")
            return Write(std::to_string(Pages(Matching().size())), buffer, size);
        if (name == "banlist.pagetext")
        {
            const size_t count = Matching().size();
            return Write(std::format("Page {} of {}", CurrentPage(count) + 1, Pages(count)), buffer, size);
        }
        return 0;
    }
}

namespace BanListPicker
{
    void Register()
    {
        if (g_Registered || !RIX::Load())
            return;
        g_Registered = true;
        RIX::Functions().RegisterPageList("mp.banlists", &ListRows, nullptr);
        RIX::Functions().RegisterAction("mp.banlist.use", &UseChosen, nullptr);
        RIX::Functions().RegisterAction("mp.banlist.game", &UseGame, nullptr);
        RIX::Functions().RegisterAction("mp.banlist.off", &UseOff, nullptr);
        if (RIX::Functions().RegisterPageValue)
            RIX::Functions().RegisterPageValue("mp", &Value, nullptr);
        Logger::WriteLog(std::format("Ban list picker ready ({} lists)", BanList::Saved().size()), MODULE_NAME, 0);
    }

    bool Open()
    {
        Register();
        if (!g_Registered)
            return false;
        RIX::Functions().SetPageVar("banlist.page", "0");
        return RIX::Functions().OpenPageFile("banlists") != 0;
    }
}
