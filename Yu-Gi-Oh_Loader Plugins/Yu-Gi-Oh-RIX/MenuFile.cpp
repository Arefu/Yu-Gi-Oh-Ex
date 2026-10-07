// Menu files: the codeless way to add buttons. Every *.json in <game folder>\Yu-Gi-Oh-Ex\menus is read at start-up; see docs/MenuFiles.md.
//
//   {
//     "buttons": [ { "key": "mymod.credits", "menu": "main", "page": "main", "label": "Credits", "description": "...",
//                    "look": "helpAndOptions", "action": { "goto": "credits" } } ],
//     "edit":    [ { "item": "quit", "label": "Exit to Desktop", "hidden": false } ]
//   }
//
// Pages made in the WolfEx page designer live in <game folder>\Yu-Gi-Oh-Ex\pages\<name>.json and are opened by the action
// { "page": "<name>" } (see docs/PageDesigner.md). The file is read again when it changed, so a page can be edited and re-opened
// without restarting the game.
#include "MainMenu.h"
#include "Pages.h"

#include <Windows.h>
#include <algorithm>
#include <deque>
#include <filesystem>
#include <format>
#include <fstream>
#include <map>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

#include <json.hpp>

#include "Logger.h"
#include "YuGiOh/YuGiOh-RIX.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    using json = nlohmann::json;

    struct NamedId { const char* Name; int Id; };

    // The ids the game gives its screens (RIX::ScreenBase::SetScreenId), named after its screen classes.
    constexpr NamedId kScreens[] =
    {
        { "title", 5 }, { "signIn", 6 }, { "commonBg", 7 }, { "mainMenu", 8 }, { "loading", 9 }, { "gameBegin", 10 }, { "exGameDuel", 11 },
        { "helpAndOptions", 12 }, { "settings", 13 }, { "videoSettings", 14 }, { "credits", 15 }, { "controllerSettings", 16 },
        { "howToPlay", 17 }, { "statistics", 18 }, { "voices", 19 }, { "pauseMenu", 20 }, { "duelistChallenge", 21 }, { "freeDuel", 21 }, { "campaignDialog", 22 },
        { "campaignSelectDeck", 23 }, { "tutorialList", 24 }, { "deckEditor", 25 }, { "swapCards", 26 }, { "matchResult", 27 },
        { "gameResult", 28 }, { "cardShop", 29 }, { "battlePack", 30 }, { "battlePackDraft", 31 }, { "battlePackEdit", 32 },
        { "playerMatch", 33 }, { "liveSetting", 34 }, { "liveSession", 35 }, { "liveLobby", 36 }, { "liveLoading", 37 },
        { "leaderboard", 38 }, { "inviteLanding", 39 }, { "safetyZone", 40 }, { "duelSelect", 41 }, { "selectRung", 42 }, { "scoreReview", 43 },
    };

    // The game's own main menu buttons (also the "look" a main menu button can borrow).
    constexpr NamedId kMainItems[] =
    {
        { "singlePlayerMenu", RIX_ITEM_SINGLE_PLAYER_MENU }, { "soloDuel", RIX_ITEM_SOLO_DUEL }, { "duelistChallenge", RIX_ITEM_DUELIST_CHALLENGE },
        { "multiplayerMenu", RIX_ITEM_MULTIPLAYER_MENU }, { "multiplayerA", RIX_ITEM_MULTIPLAYER_A }, { "multiplayerB", RIX_ITEM_MULTIPLAYER_B },
        { "leaderboard", RIX_ITEM_LEADERBOARD }, { "battlePack", RIX_ITEM_BATTLE_PACK }, { "deckEditor", RIX_ITEM_DECK_EDITOR },
        { "cardShop", RIX_ITEM_CARD_SHOP }, { "helpAndOptions", RIX_ITEM_HELP_AND_OPTIONS }, { "tutorials", RIX_ITEM_TUTORIALS },
        { "quitGame", RIX_ITEM_QUIT_GAME },
    };

    constexpr NamedId kOptionItems[] =
    {
        { "howToPlay", RIX_OPTION_HOW_TO_PLAY }, { "controllerSettings", RIX_OPTION_CONTROLLER_SETTINGS }, { "settings", RIX_OPTION_SETTINGS },
        { "videoSettings", RIX_OPTION_VIDEO_SETTINGS }, { "credits", RIX_OPTION_CREDITS },
    };

    constexpr NamedId kPages[] = { { "main", RIX_PAGE_MAIN }, { "singlePlayer", RIX_PAGE_SINGLE_PLAYER }, { "multiplayer", RIX_PAGE_MULTIPLAYER } };
    constexpr NamedId kMenus[] = { { "main", RIX_MENU_MAIN }, { "options", RIX_MENU_OPTIONS } };

    template <size_t N>
    bool Lookup(const NamedId (&table)[N], const json& value, int& id)
    {
        if (value.is_number_integer())
        {
            id = value.get<int>();
            return true;
        }
        if (value.is_string())
        {
            const std::string name = value.get<std::string>();
            for (const NamedId& entry : table)
            {
                if (_stricmp(entry.Name, name.c_str()) == 0)
                {
                    id = entry.Id;
                    return true;
                }
            }
        }
        return false;
    }

    std::wstring Wide(const std::string& text)
    {
        int length = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring wide(length, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), length);
        return wide;
    }

    // ---- actions ----

    struct Step
    {
        enum Kind { Goto, Press, Call, Quit, Page } Type = Goto;
        int Id = 0;             // screen or main menu item
        std::string Name;       // named action, or page
    };
    using Action = std::vector<Step>;

    std::deque<Action> g_Actions;   // what a button's callback points at; never shrinks

    bool OpenPageFile(const std::string& name);

    struct Registered { RIX_ButtonCallback Callback; void* User; };
    std::map<std::string, Registered> g_Registry;
    std::mutex g_RegistryLock;

    void __cdecl RunButtonAction(int buttonId, void* user)
    {
        for (const Step& step : *static_cast<const Action*>(user))
        {
            switch (step.Type)
            {
            case Step::Goto:
                RIX_GotoScreen(step.Id);
                break;
            case Step::Press:
                Menu::Press(step.Id);
                break;
            case Step::Quit:
                YGO::RIX::RequestQuit(YGO::RIX::GetGlobalInstance());
                break;
            case Step::Call:
                if (!Menu::RunAction(step.Name))
                    Logger::WriteLog(std::format("Menu action '{}' is not registered by any plugin", step.Name), MODULE_NAME, 1);
                break;
            case Step::Page:
                OpenPageFile(step.Name);
                break;
            }
        }
    }

    bool ParseStep(const json& node, Step& step, std::string& problem)
    {
        if (!node.is_object() || node.size() != 1)
        {
            problem = "an action is an object with one of goto, press, call, page or quit";
            return false;
        }

        if (node.contains("goto"))
        {
            step.Type = Step::Goto;
            if (!Lookup(kScreens, node["goto"], step.Id))
            {
                problem = "unknown screen in \"goto\"";
                return false;
            }
        }
        else if (node.contains("press"))
        {
            step.Type = Step::Press;
            if (!Lookup(kMainItems, node["press"], step.Id))
            {
                problem = "unknown main menu button in \"press\"";
                return false;
            }
        }
        else if (node.contains("quit"))
        {
            step.Type = Step::Quit;
        }
        else if (node.contains("call") && node["call"].is_string())
        {
            step.Type = Step::Call;
            step.Name = node["call"].get<std::string>();
        }
        else if (node.contains("page") && node["page"].is_string())
        {
            step.Type = Step::Page;
            step.Name = node["page"].get<std::string>();
        }
        else
        {
            problem = "an action is goto, press, call, page or quit";
            return false;
        }
        return true;
    }

    // "action": { ... }  or  "action": [ { ... }, { ... } ]
    bool ParseAction(const json& node, Action& action, std::string& problem)
    {
        if (node.is_array())
        {
            for (const json& item : node)
            {
                Step step;
                if (!ParseStep(item, step, problem))
                    return false;
                action.push_back(step);
            }
            return true;
        }

        Step step;
        if (!ParseStep(node, step, problem))
            return false;
        action.push_back(step);
        return true;
    }

    std::string Text(const json& node, const char* key)
    {
        auto it = node.find(key);
        return it != node.end() && it->is_string() ? it->get<std::string>() : std::string();
    }

    // ---- pages (the WolfEx page designer) ----

    std::filesystem::path ContentFolder(const char* name)
    {
        char exe[MAX_PATH]{};
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        return std::filesystem::path(exe).parent_path() / "Yu-Gi-Oh-Ex" / name;
    }

    // A picture on a page: one sprite of a sheet (the designer's "image" element), top-left at X, Y, stretched to Width x Height.
    struct PageImage
    {
        std::string Resource, Sprite;
        float X = 0, Y = 0, Width = 0, Height = 0;
        int Z = 0;
    };

    // Text on a page (the designer's "text" element): the box's top-left at X, Y, Width wide (0 = one line), TextSize pixels high.
    struct PageText
    {
        std::wstring Text;      // the node keeps a pointer to it: PageFile (and so this) lives as long as the game
        float X = 0, Y = 0, Width = 0, TextSize = 30;
        int Z = 0;
        YGO::RIX::Dfx::TextAlign Align = YGO::RIX::Dfx::TextAlign::Left;
        uint32_t Colour = 0xFFFFFFFF;
    };

    // "#RRGGBB" or "#AARRGGBB" -> ARGB (white when missing or unreadable).
    uint32_t ParseColour(const std::string& text)
    {
        if (text.size() != 7 && text.size() != 9 || text[0] != '#')
            return 0xFFFFFFFF;
        uint32_t value = 0;
        for (size_t i = 1; i < text.size(); ++i)
        {
            const char c = static_cast<char>(std::tolower(static_cast<unsigned char>(text[i])));
            const int digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : -1;
            if (digit < 0)
                return 0xFFFFFFFF;
            value = value * 16 + static_cast<uint32_t>(digit);
        }
        return text.size() == 7 ? 0xFF000000 | value : value;
    }

    // One page file as RIX builds it: the header, the first "buttonList" element's buttons, the images and the texts. Every other element is only
    // the designer's preview (docs/PageDesigner.md lists what is built). Never freed: an open page's buttons point at Actions.
    struct PageFile
    {
        std::filesystem::file_time_type Written;
        std::wstring Header;
        std::vector<std::wstring> Labels, Descriptions;
        std::deque<Action> Actions;
        float ButtonsX = 0.0f, ButtonsY = 0.0f;
        std::vector<PageImage> Images;
        std::vector<PageText> Texts;
    };

    // One opening of a page (the same page can be open twice, one on top of the other): the nodes it put on the screen while it shows.
    struct OpenedPage
    {
        std::string Name;
        std::shared_ptr<PageFile> File;
        std::vector<YGO::RIX::SharedNode> Nodes;
        void* Screen = nullptr;
    };
    std::deque<OpenedPage> g_Opened;   // never shrinks: RIX hands the pointer back to OnShow / OnHide until the page is gone

    // Page elements go above the Battle Pack screen's own background and panels (BetterCardShop's widgets use 12 to 20).
    constexpr int kElementZBase = 20;

    void __cdecl ShowPageElements(void* screen, void* user)
    {
        auto* opened = static_cast<OpenedPage*>(user);
        if (!opened->Nodes.empty())
            return;
        opened->Screen = screen;
        auto* root = YGO::RIX::ScreenRoot(screen);
        for (const PageImage& image : opened->File->Images)
        {
            auto node = YGO::RIX::Dfx::AddImage(root, image.Resource.c_str(), image.Sprite.c_str(), kElementZBase + image.Z, image.X, image.Y, image.Width, image.Height);
            if (node.Node)
                opened->Nodes.push_back(node);
            else
                Logger::WriteLog(std::format("Page '{}': sprite '{}' of '{}' was not found", opened->Name, image.Sprite, image.Resource), MODULE_NAME, 1);
        }
        for (const PageText& text : opened->File->Texts)
        {
            auto node = YGO::RIX::Dfx::AddText(root, text.Text.c_str(), kElementZBase + text.Z, text.X, text.Y, text.Width, text.TextSize, text.Align, text.Colour);
            if (node.Node)
                opened->Nodes.push_back(node);
        }
    }

    void __cdecl HidePageElements(void* screen, void* user)
    {
        auto* opened = static_cast<OpenedPage*>(user);
        auto* root = YGO::RIX::ScreenRoot(opened->Screen ? opened->Screen : screen);
        for (auto& node : opened->Nodes)
            YGO::RIX::Dfx::RemoveImage(root, node);
        opened->Nodes.clear();
    }

    std::map<std::string, std::shared_ptr<PageFile>> g_PageFiles;   // by lower-case name: the newest read of each file
    std::vector<std::shared_ptr<PageFile>> g_OldPageFiles;          // replaced reads (a page opened from one may still be showing)

    constexpr float kButtonSpacing = 100.0f;    // RIX pages put their buttons 100 px apart (Pages.cpp kSpacing)

    std::shared_ptr<PageFile> ReadPageFile(const std::string& name)
    {
        std::string key = name;
        std::transform(key.begin(), key.end(), key.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        if (key.empty() || key.find_first_of("/\\:") != std::string::npos)
        {
            Logger::WriteLog(std::format("Page '{}': not a page name", name), MODULE_NAME, 2);
            return nullptr;
        }

        // the copy that wins: the game folder's, else the last mod that has the page (Yu-Gi-Oh-Mods.h)
        std::filesystem::path path = YGO::Mods::Find("pages\\" + name + ".json");
        if (path.empty())
            path = ContentFolder("pages") / (name + ".json");   // for the message below
        std::error_code error;
        const auto written = std::filesystem::last_write_time(path, error);
        if (error)
        {
            Logger::WriteLog(std::format("Page '{}': {} was not found", name, path.string()), MODULE_NAME, 2);
            return nullptr;
        }

        auto cached = g_PageFiles.find(key);
        if (cached != g_PageFiles.end() && cached->second->Written == written)
            return cached->second;

        json root;
        try
        {
            std::ifstream stream(path);
            root = json::parse(stream, nullptr, true, true);
        }
        catch (const std::exception& e)
        {
            Logger::WriteLog(std::format("Page '{}': {}", name, e.what()), MODULE_NAME, 2);
            return nullptr;
        }

        auto page = std::make_shared<PageFile>();
        page->Written = written;
        page->Header = Wide(Text(root, "header"));

        const json* elements = root.contains("elements") && root["elements"].is_array() ? &root["elements"] : nullptr;
        bool haveButtons = false;
        for (size_t i = 0; elements && i < elements->size(); ++i)
        {
            const json& element = (*elements)[i];
            const std::string kind = Text(element, "kind");
            if (kind == "header" && !Text(element, "text").empty())
            {
                page->Header = Wide(Text(element, "text"));
            }
            else if (kind == "image")
            {
                PageImage image;
                image.Resource = Text(element, "resource");
                image.Sprite = Text(element, "sprite");
                image.X = element.value("x", 0.0f);
                image.Y = element.value("y", 0.0f);
                image.Width = element.value("width", 0.0f);
                image.Height = element.value("height", 0.0f);
                image.Z = element.value("z", 0);
                if (image.Resource.empty() || image.Sprite.empty())
                    Logger::WriteLog(std::format("Page '{}': an image needs a \"resource\" and a \"sprite\"", name), MODULE_NAME, 1);
                else
                    page->Images.push_back(std::move(image));
            }
            else if (kind == "text" && !Text(element, "text").empty())
            {
                PageText text;
                text.Text = Wide(Text(element, "text"));
                text.X = element.value("x", 0.0f);
                text.Y = element.value("y", 0.0f);
                text.Width = element.value("width", 0.0f);
                text.Z = element.value("z", 0);
                if (element.contains("textSize") && element["textSize"].is_number())
                    text.TextSize = element["textSize"].get<float>();
                const std::string align = Text(element, "align");
                text.Align = align == "center" || align == "centre" ? YGO::RIX::Dfx::TextAlign::Centre
                           : align == "right" ? YGO::RIX::Dfx::TextAlign::Right : YGO::RIX::Dfx::TextAlign::Left;
                text.Colour = ParseColour(Text(element, "colour"));
                page->Texts.push_back(std::move(text));
            }
            else if (kind == "buttonList" && !haveButtons && element.contains("buttons") && element["buttons"].is_array())
            {
                haveButtons = true;
                const json& buttons = element["buttons"];
                const size_t count = (std::min)(buttons.size(), static_cast<size_t>(RIX_PAGE_MAX_BUTTONS));
                if (buttons.size() > count)
                    Logger::WriteLog(std::format("Page '{}': only the first {} buttons are used", name, RIX_PAGE_MAX_BUTTONS), MODULE_NAME, 1);
                for (size_t b = 0; b < count; ++b)
                {
                    const json& button = buttons[b];
                    Action action;
                    if (button.contains("action") && !button["action"].is_null())
                    {
                        std::string problem;
                        if (!ParseAction(button["action"], action, problem))
                        {
                            Logger::WriteLog(std::format("Page '{}' button {}: {}", name, b + 1, problem), MODULE_NAME, 2);
                            action.clear();
                        }
                    }
                    page->Labels.push_back(Wide(Text(button, "label")));
                    page->Descriptions.push_back(Wide(Text(button, "description")));
                    page->Actions.push_back(std::move(action));
                }

                // The designer's box is x, y, width, height (top left, 1920 x 1080). RIX wants the buttons' centre line and the first
                // button's y; the designer draws each button 100 px high, so the first one's middle is 50 px below the top.
                const float x = element.value("x", 0.0f), y = element.value("y", 0.0f), width = element.value("width", 0.0f);
                page->ButtonsX = x + width / 2.0f;
                page->ButtonsY = y + kButtonSpacing / 2.0f;
            }
        }
        if (page->Header.empty())
            page->Header = Wide(name);

        if (cached != g_PageFiles.end())
            g_OldPageFiles.push_back(cached->second);
        g_PageFiles[key] = page;
        Logger::WriteLog(std::format("Page '{}' read: {} button(s), {} image(s), {} text(s)", name, page->Labels.size(), page->Images.size(), page->Texts.size()), MODULE_NAME, 0);
        return page;
    }

    bool OpenPageFile(const std::string& name)
    {
        std::shared_ptr<PageFile> page = ReadPageFile(name);
        if (!page)
            return false;

        RIX_PageDesc desc{};
        desc.Size = sizeof(desc);
        desc.Header = page->Header.c_str();
        desc.ButtonCount = static_cast<int32_t>(page->Labels.size());
        for (int i = 0; i < desc.ButtonCount; ++i)
        {
            desc.Buttons[i].Label = page->Labels[i].c_str();
            desc.Buttons[i].Description = page->Descriptions[i].c_str();
            desc.Buttons[i].OnPress = &RunButtonAction;
            desc.Buttons[i].User = &page->Actions[i];
        }
        desc.ButtonsX = page->ButtonsX;
        desc.ButtonsY = page->ButtonsY;
        if (!page->Images.empty() || !page->Texts.empty())
        {
            OpenedPage& opened = g_Opened.emplace_back();
            opened.Name = name;
            opened.File = page;
            desc.OnShow = &ShowPageElements;
            desc.OnHide = &HidePageElements;
            desc.User = &opened;
        }
        if (!Pages::Open(desc))
        {
            Logger::WriteLog(std::format("Page '{}' could not be opened", name), MODULE_NAME, 2);
            return false;
        }
        return true;
    }

    void LoadButton(const json& node, const std::string& file, size_t index)
    {
        const std::string where = std::format("{} button #{}", file, index + 1);

        int menu = RIX_MENU_MAIN, page = RIX_PAGE_MAIN;
        if (node.contains("menu") && !Lookup(kMenus, node["menu"], menu))
            return Logger::WriteLog(where + ": unknown \"menu\" (main or options)", MODULE_NAME, 2);
        if (node.contains("page") && !Lookup(kPages, node["page"], page))
            return Logger::WriteLog(where + ": unknown \"page\" (main, singlePlayer or multiplayer)", MODULE_NAME, 2);

        int look = menu == RIX_MENU_MAIN ? RIX_ITEM_DECK_EDITOR : RIX_OPTION_SETTINGS;
        if (node.contains("look"))
        {
            const bool known = menu == RIX_MENU_MAIN ? Lookup(kMainItems, node["look"], look) : Lookup(kOptionItems, node["look"], look);
            if (!known)
                return Logger::WriteLog(where + ": unknown \"look\"", MODULE_NAME, 2);
        }

        const std::string label = Text(node, "label");
        if (label.empty())
            return Logger::WriteLog(where + ": a button needs a \"label\"", MODULE_NAME, 2);

        Action action;
        if (node.contains("action"))
        {
            std::string problem;
            if (!ParseAction(node["action"], action, problem))
                return Logger::WriteLog(where + ": " + problem, MODULE_NAME, 2);
        }

        g_Actions.push_back(std::move(action));

        const std::wstring wideLabel = Wide(label), wideDescription = Wide(Text(node, "description"));
        RIX_ButtonDesc button{};
        button.Size = sizeof(button);
        button.Label = wideLabel.c_str();
        button.Description = wideDescription.c_str();
        button.Menu = menu;
        button.Page = page;
        button.Skin = look;
        button.OnPress = &RunButtonAction;
        button.User = &g_Actions.back();

        int id = Menu::Add(button);
        Logger::WriteLog(id < 0 ? where + ": could not be added" : std::format("{}: \"{}\" is button {}", file, label, id), MODULE_NAME, id < 0 ? 2 : 0);
    }

    void LoadEdit(const json& node, const std::string& file, size_t index)
    {
        const std::string where = std::format("{} edit #{}", file, index + 1);

        int item = -1;
        if (!node.contains("item") || !Lookup(kMainItems, node["item"], item) || item < 0 || item >= RIX_ITEM_QUIT_GAME + 1)
            return Logger::WriteLog(where + ": unknown \"item\"", MODULE_NAME, 2);

        std::wstring label, description;
        const bool hasLabel = node.contains("label") && node["label"].is_string();
        const bool hasDescription = node.contains("description") && node["description"].is_string();
        if (hasLabel)
            label = Wide(node["label"].get<std::string>());
        if (hasDescription)
            description = Wide(node["description"].get<std::string>());

        Menu::EditVanilla(item, hasLabel ? &label : nullptr, hasDescription ? &description : nullptr, node.value("hidden", false));
    }

    void LoadFile(const std::filesystem::path& path)
    {
        const std::string file = path.filename().string();
        std::ifstream stream(path);
        json root;
        try
        {
            root = json::parse(stream, nullptr, true, true);
        }
        catch (const std::exception& e)
        {
            return Logger::WriteLog(std::format("{}: {}", file, e.what()), MODULE_NAME, 2);
        }

        if (root.contains("buttons") && root["buttons"].is_array())
        {
            for (size_t i = 0; i < root["buttons"].size(); ++i)
                LoadButton(root["buttons"][i], file, i);
        }
        if (root.contains("edit") && root["edit"].is_array())
        {
            for (size_t i = 0; i < root["edit"].size(); ++i)
                LoadEdit(root["edit"][i], file, i);
        }
    }
}

namespace Menu
{
    bool RegisterAction(const std::string& name, RIX_ButtonCallback callback, void* user)
    {
        std::lock_guard<std::mutex> guard(g_RegistryLock);
        g_Registry[name] = { callback, user };
        return true;
    }

    bool RunAction(const std::string& name)
    {
        Registered registered;
        {
            std::lock_guard<std::mutex> guard(g_RegistryLock);
            auto it = g_Registry.find(name);
            if (it == g_Registry.end())
                return false;
            registered = it->second;
        }
        registered.Callback(-1, registered.User);
        return true;
    }

    bool OpenPage(const std::string& name)
    {
        return OpenPageFile(name);
    }

    void LoadMenuFiles()
    {
        // menus\*.json of every mod and the game folder (Yu-Gi-Oh-Mods.h); a later folder's file replaces one with the same name
        std::vector<std::filesystem::path> files = YGO::Mods::FilesIn("menus", L".json");
        std::sort(files.begin(), files.end(), [](const std::filesystem::path& a, const std::filesystem::path& b)
        {
            return _wcsicmp(a.filename().c_str(), b.filename().c_str()) < 0;
        });

        for (const auto& file : files)
            LoadFile(file);
    }
}
