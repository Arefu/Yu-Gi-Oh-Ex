// Menu files: the codeless way to add buttons. Every *.json in <game folder>\Yu-Gi-Oh-Ex\menus is read at start-up; see docs/MenuFiles.md.
//
//   {
//     "buttons": [ { "key": "mymod.credits", "menu": "main", "page": "main", "label": "Credits", "description": "...",
//                    "look": "helpAndOptions", "action": { "goto": "credits" } } ],
//     "edit":    [ { "item": "quit", "label": "Exit to Desktop", "hidden": false } ]
//   }
#include "MainMenu.h"

#include <Windows.h>
#include <algorithm>
#include <deque>
#include <filesystem>
#include <format>
#include <fstream>
#include <map>
#include <mutex>
#include <string>
#include <vector>

#include <json.hpp>

#include "Logger.h"
#include "YuGiOh/YuGiOh-RIX.h"

namespace
{
    using json = nlohmann::json;

    struct NamedId { const char* Name; int Id; };

    // The ids the game gives its screens (RIX::ScreenBase::SetScreenId), named after its screen classes.
    constexpr NamedId kScreens[] =
    {
        { "title", 5 }, { "signIn", 6 }, { "commonBg", 7 }, { "mainMenu", 8 }, { "loading", 9 }, { "gameBegin", 10 }, { "exGameDuel", 11 },
        { "helpAndOptions", 12 }, { "settings", 13 }, { "videoSettings", 14 }, { "credits", 15 }, { "controllerSettings", 16 },
        { "howToPlay", 17 }, { "statistics", 18 }, { "voices", 19 }, { "pauseMenu", 20 }, { "duelistChallenge", 21 }, { "campaignDialog", 22 },
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
        enum Kind { Goto, Press, Call, Quit } Type = Goto;
        int Id = 0;             // screen or main menu item
        std::string Name;       // named action
    };
    using Action = std::vector<Step>;

    std::deque<Action> g_Actions;   // what a button's callback points at; never shrinks

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
            }
        }
    }

    bool ParseStep(const json& node, Step& step, std::string& problem)
    {
        if (!node.is_object() || node.size() != 1)
        {
            problem = "an action is an object with one of goto, press, call or quit";
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
        else
        {
            problem = "an action is goto, press, call or quit";
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

    void LoadMenuFiles()
    {
        char exe[MAX_PATH]{};
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        const std::filesystem::path folder = std::filesystem::path(exe).parent_path() / "Yu-Gi-Oh-Ex" / "menus";

        std::error_code error;
        if (!std::filesystem::is_directory(folder, error))
            return;

        std::vector<std::filesystem::path> files;
        for (const auto& entry : std::filesystem::directory_iterator(folder, error))
        {
            if (entry.is_regular_file() && entry.path().extension() == ".json")
                files.push_back(entry.path());
        }
        std::sort(files.begin(), files.end());

        for (const auto& file : files)
            LoadFile(file);
    }
}
