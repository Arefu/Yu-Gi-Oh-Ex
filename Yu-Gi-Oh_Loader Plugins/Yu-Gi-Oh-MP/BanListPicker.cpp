#include "BanListPicker.h"

#include <Windows.h>
#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <format>
#include <string>
#include <vector>

#include "BanList.h"
#include "Config.h"
#include "Logger.h"
#include "Yu-Gi-Oh-RIX.h"

namespace
{
    constexpr int kPageSize = 8;   // rows a page shows (the menu has no scrolling); Game's list and No list are the first two rows of page 1
    constexpr int kFixedRows = 2;
    // ICON_ID_CHECKMARK (336) as text: the game's text layout draws U+E000 + n as icon n (YGO::TEXT::Layout_PlaceIcon 0x140765A70)
    constexpr const wchar_t* kInUse = L"\xE150 ";

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
        const std::string category = Var("banlist.category"), era = Var("banlist.era"), year = Var("banlist.year");
        std::vector<size_t> found;
        const auto& saved = BanList::Saved();
        for (size_t i = 0; i < saved.size(); ++i)
        {
            if (!Any(category) && _stricmp(saved[i].Category.c_str(), category.c_str()) != 0)
                continue;
            if (!Any(era) && _stricmp(saved[i].Era.c_str(), era.c_str()) != 0)
                continue;
            if (!Any(year) && saved[i].Date.rfind(year, 0) != 0)
                continue;
            found.push_back(i);
        }
        return found;
    }

    int Pages(size_t count) { return (std::max)(1, static_cast<int>((count + kFixedRows + kPageSize - 1) / kPageSize)); }

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

    // The game's own list, read once the card data is there (it never changes while the game runs).
    const BanList::List& GameList()
    {
        static BanList::List game;
        if (game.Forbidden.empty() && game.Limited.empty() && game.SemiLimited.empty())
            game = BanList::GameList();
        return game;
    }

    // A row's value: "game", "off" or an index into BanList::Saved(); empty = the list in use. Null for "off" (no cards).
    const BanList::List* ListFor(const std::string& value)
    {
        if (value.empty())
            return BanList::CurrentMode() == BanList::Mode::Custom ? &BanList::CurrentList() : BanList::CurrentMode() == BanList::Mode::Off ? nullptr : &GameList();
        if (value == "game")
            return &GameList();
        if (value == "off")
            return nullptr;
        const auto& saved = BanList::Saved();
        char* end = nullptr;
        const unsigned long at = std::strtoul(value.c_str(), &end, 10);
        return *end || at >= saved.size() ? nullptr : &saved[at];
    }

    void __cdecl ListRows(void* rows, void*)
    {
        const std::vector<size_t> found = Matching();
        const int page = CurrentPage(found.size());
        const auto& saved = BanList::Saved();
        // the list in use gets a tick before its name
        const BanList::Mode mode = BanList::CurrentMode();
        auto label = [](bool inUse, const std::wstring& name) { return inUse ? kInUse + name : name; };
        if (page == 0)   // the two that are always there, first
        {
            RIX::Functions().PageListAdd(rows, label(mode == BanList::Mode::Game, L"Game's list").c_str(), L"The game's own Forbidden & Limited List.", "game");
            RIX::Functions().PageListAdd(rows, label(mode == BanList::Mode::Off, L"No list").c_str(), L"Every card at 3 copies.", "off");
        }
        // row r of all of them (the two fixed rows, then the saved lists) = saved list r - 2
        const size_t first = static_cast<size_t>((std::max)(0, page * kPageSize - kFixedRows));
        const size_t last = static_cast<size_t>((page + 1) * kPageSize - kFixedRows);
        for (size_t i = first; i < found.size() && i < last; ++i)
        {
            const BanList::List& list = saved[found[i]];
            const bool inUse = mode == BanList::Mode::Custom && BanList::CurrentList().Name == list.Name && BanList::CurrentList().Date == list.Date;
            RIX::Functions().PageListAdd(rows, label(inUse, Wide(list.Name)).c_str(), Wide(Describe(list)).c_str(), std::to_string(found[i]).c_str());
        }
    }

    void __cdecl UseChosen(int, void*)
    {
        const std::string index = Var("banlist");
        if (index == "game" || index == "off")
        {
            index == "game" ? BanList::UseGame() : BanList::UseOff();
            return;
        }
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
        // the preview of a row (its value after the prefix: game / off / an index): banlist.cards.<kind>.<row> = the Konami ids, comma
        // separated (a card box reads them), banlist.size.<kind>.<row> = how many; kind = forbidden / limited / semi.
        // banlist.title.<row> and banlist.about.<row> = its name and the line Describe makes.
        for (const char* prefix : { "banlist.cards.", "banlist.size." })
            if (name.rfind(prefix, 0) == 0)
            {
                const std::string rest = name.substr(std::strlen(prefix));
                const size_t dot = rest.find('.');
                const std::string kind = rest.substr(0, dot), row = dot == std::string::npos ? std::string() : rest.substr(dot + 1);
                const BanList::List* list = ListFor(row);
                static const std::vector<uint16_t> none;
                const std::vector<uint16_t>& ids = !list ? none : kind == "forbidden" ? list->Forbidden : kind == "limited" ? list->Limited
                    : kind == "semi" ? list->SemiLimited : none;
                if (prefix[8] == 's')
                    return Write(std::to_string(ids.size()), buffer, size);
                std::string text;
                for (uint16_t id : ids)
                    text += std::to_string(id) + ",";
                return Write(text, buffer, size);
            }
        if (name.rfind("banlist.title.", 0) == 0 || name.rfind("banlist.about.", 0) == 0)
        {
            const std::string row = name.substr(14);
            const BanList::List* list = ListFor(row);
            const bool title = name[8] == 't';
            if (row == "off" || (!list && row.empty()))
                return Write(title ? "No list" : "Every card at 3 copies.", buffer, size);
            if (!list)
                return 0;
            if (list == &GameList())
                return Write(title ? "Game's list" : "The game's own Forbidden & Limited List.", buffer, size);
            return Write(title ? list->Name : Describe(*list), buffer, size);
        }
        // the deck-slot preview's lines: banlist.kind.<row> (TCG list / OCG list / ...), banlist.counts.<row>, banlist.when.<row> (date, era)
        for (const char* prefix : { "banlist.kind.", "banlist.counts.", "banlist.when." })
            if (name.rfind(prefix, 0) == 0)
            {
                const std::string row = name.substr(std::strlen(prefix));
                const BanList::List* list = ListFor(row);
                const char what = prefix[8];   // k / c / w
                if (!list)
                    return Write(what == 'k' ? "No list" : what == 'c' ? "Every card at 3 copies" : "", buffer, size);
                if (what == 'k')
                    return Write(list == &GameList() ? "Game's list" : list->Category.empty() ? "Ban list" : list->Category + " list", buffer, size);
                if (what == 'c')
                    return Write(std::format("{} Forbidden, {} Limited, {} Semi-Limited", list->Forbidden.size(), list->Limited.size(),
                        list->SemiLimited.size()), buffer, size);
                std::string when = list->Date;
                if (!list->Era.empty())
                    when += (when.empty() ? "" : " - ") + list->Era + " era";
                return Write(when, buffer, size);
            }
        if (name == "banlist.pagelist")   // the Page row's values: "Page 1 of 3=0|Page 2 of 3=1|..."
        {
            const int pages = Pages(Matching().size());
            std::string text;
            for (int i = 0; i < pages; ++i)
                text += std::format("{}Page {} of {}={}", i ? "|" : "", i + 1, pages, i);
            return Write(text, buffer, size);
        }
        if (name == "banlist.saved")
            return Write(std::to_string(BanList::Saved().size()), buffer, size);
        if (name == "banlist.nomatch")   // 1 = there are lists, but none match the filters
            return Write(!BanList::Saved().empty() && Matching().empty() ? "1" : "0", buffer, size);
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
        // this plugin's pages (banlists, mp-match-settings) ship in Plugins\Yu-Gi-Oh-MP\pages; a copy in Yu-Gi-Oh-Ex\pages or a mod wins
        if (RIX::Functions().AddPageFolder)
            RIX::Functions().AddPageFolder((Config::Get_ContentFolder() + L"\\pages").c_str());
        else
            Logger::WriteLog("This Yu-Gi-Oh-RIX can't open plugin pages (no RIX_AddPageFolder): update it", MODULE_NAME, 1);
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
        RIX::Functions().SetPageVar("banlist.preview", "");   // until the rows menu's highlight sets it: the list in use
        return RIX::Functions().OpenPageFile("banlists") != 0;
    }
}
