#include "Tutorials.h"
#include "Common.h"

#include <Windows.h>
#include <intrin.h>
#include <cctype>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <deque>
#include <filesystem>
#include <format>
#include <fstream>
#include <map>
#include <optional>
#include <set>
#include <string>
#include <vector>

#include <json.hpp>

#include "Detours.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Core.h"

#pragma intrinsic(_ReturnAddress)

namespace
{
    using namespace Campaign;

    // ---- the game (names and comments in the IDB) ----

    constexpr int kFirstNew = 27;          // the game's tables stop at 26 (27 entries with the unused 0)
    constexpr int kMaxNumber = 99;         // "steam_tutorial_%02d"
    constexpr int kDoneMark = 334;         // the mark RefreshItems puts on a finished tutorial's row

    using GetInt_t = int(__fastcall*)();
    using EmplaceInt_t = int*(__fastcall*)(void* vector, int* where, const int* value);
    using SetMark_t = void(__fastcall*)(void* rowPart, int mark);
    using SetText_t = void(__fastcall*)(void* textWidget, intptr_t stringIdOrWideString);
    using LoadScriptFile_t = void*(__fastcall*)(const void* path);
    using SetArena_t = int64_t(__fastcall*)(unsigned int arena);
    using OnEnter_t = void(__fastcall*)(uint8_t* screen, void* ui);
    using RefreshItems_t = void(__fastcall*)(uint8_t* screen, char keepScroll);

    const auto GetTutorialDuelIndex = reinterpret_cast<GetInt_t>(0x140769180);   // YGO::DUEL::Get_TutorialDuelIndex
    const auto VectorEmplaceInt = reinterpret_cast<EmplaceInt_t>(0x140862EA0);   // std::vector<int> grow + insert (the game's heap)
    const auto SetRowMark = reinterpret_cast<SetMark_t>(0x14089FD60);            // a list row's icon (-1 = none)
    const auto SetTextById = reinterpret_cast<SetText_t>(0x14075E000);           // RIX::widget_Text::SetTextById

    uintptr_t orig_LoadScriptFile = 0x1408709D0;   // YGO::TUTORIAL::LoadScriptFile(path): loads + fixes up the step file
    uintptr_t orig_SetCurrentArenaId = 0x140769540;
    uintptr_t orig_OnEnter = 0x1408643F0;          // RIX::ScreenSelectTutorial::OnEnter
    uintptr_t orig_RefreshItems = 0x140864560;     // RIX::ScreenSelectTutorial::RefreshItems

    constexpr uintptr_t kEndMarkDone = 0x1407F6470;     // ExecuteStep End: call Get_TutorialDuelIndex; bts [save+2960+...]; request a save
    constexpr uintptr_t kEndAfterMark = 0x1407F649F;    // ExecuteStep End: carries on with P1 (menu / win / leave)
    constexpr uintptr_t kArenaCallReturn = 0x140863D14; // ScreenSelectTutorial::Update's Set_CurrentArenaId(g_TutorialArenaIds[n])
    constexpr uintptr_t kGameMenuList = 0x140A780C0;    // g_TutorialMenuList, 0-terminated

    // RIX::ScreenSelectTutorial
    constexpr size_t kList = 656;        // list state: int cursor, count, first shown, rows shown
    constexpr size_t kNumbers = 712;     // std::vector<int>: the tutorial number of each list entry
    constexpr size_t kRows = 1016;       // std::vector of the shown rows (176 bytes each)
    constexpr size_t kRowSize = 176;
    constexpr size_t kRowTitle = 0x40;   // row: the title's text widget
    constexpr size_t kRowMark = 0x70;    // row: the done mark

    // ---- Yu-Gi-Oh-Ex/tutorials ----

    struct Tutorial
    {
        std::map<char, std::vector<uint8_t>> Files;   // by language letter: the game's .bin layout, offsets not yet fixed up
        std::map<char, std::wstring> Titles;
        int Arena = 1;
        bool InMenu = true;
        bool SettingsFromEnglish = false;
    };

    std::map<int, Tutorial> g_tutorials;
    std::deque<std::wstring> g_titles;   // every title handed to the game; kept so a reload never frees a string a widget still compares with

    std::filesystem::path Folder() { return std::filesystem::path(GameFolder()) / "Yu-Gi-Oh-Ex" / "tutorials"; }

    constexpr std::pair<const char*, uint8_t> kOps[] = {
        { "SetupScenario", 0x00 }, { "Message", 0x01 }, { "Hint", 0x02 }, { "SetTurnPlayer", 0x03 }, { "Phase", 0x04 }, { "MoveCursor", 0x05 },
        { "Wait", 0x06 }, { "WaitForAction", 0x07 }, { "Flag09", 0x09 }, { "Set0A", 0x0A }, { "ShowCard", 0x0B }, { "PointerSimple", 0x0C },
        { "Pointer", 0x0D }, { "ActivateAt", 0x0E }, { "Set0F", 0x0F }, { "Label", 0x11 }, { "RetryStart", 0x12 }, { "RetryEnd", 0x13 },
        { "AllowInput", 0x14 }, { "Set15", 0x15 }, { "StackDeck", 0x17 }, { "Flag18", 0x18 }, { "Set19", 0x19 }, { "Set1B", 0x1B },
        { "AllowCards", 0x1C }, { "Set1D", 0x1D }, { "Goto", 0x1E }, { "End", 0x7F },
    };
    constexpr uint8_t kEnd = 0x7F;

    // An op by name (any case) or number ("0x08", "8"), as TutorialFile.TryOp reads it.
    std::optional<uint8_t> Op(const std::string& token)
    {
        if (token.empty())
            return std::nullopt;
        for (auto& [name, value] : kOps)
            if (_stricmp(name, token.c_str()) == 0)
                return value;
        const bool hex = token.size() > 2 && token[0] == '0' && (token[1] == 'x' || token[1] == 'X');
        const char* digits = token.c_str() + (hex ? 2 : 0);
        char* end = nullptr;
        const long value = std::strtol(digits, &end, hex ? 16 : 10);
        if (end != digits && *end == '\0' && value >= 0 && value <= 255)
            return static_cast<uint8_t>(value);
        return std::nullopt;
    }

    template <typename T>
    void Put(std::vector<uint8_t>& out, T value)
    {
        const auto* bytes = reinterpret_cast<const uint8_t*>(&value);
        out.insert(out.end(), bytes, bytes + sizeof(T));
    }

    // TutorialFile.FromJson + ToBytes: u16 steps, u16 texts, u32 0, u32 0; 12-byte steps; {u32 length, u32 0} per text; the UTF-16 texts.
    std::vector<uint8_t> Build(const nlohmann::json& root, const std::string& name)
    {
        struct Step { uint8_t Op, P1; uint16_t P2, P3, P4, Id; int16_t Text; };
        std::vector<Step> steps;
        std::vector<std::wstring> texts;
        int nextId = 0, index = 0;
        auto list = root.find("steps");
        if (list != root.end() && list->is_array())
        {
            for (auto& node : *list)
            {
                index++;
                auto op = node.is_object() ? Op(Str(node, "op").value_or("")) : std::nullopt;
                if (!op)
                {
                    Logger::WriteLog(std::format("{}: step {} has no op the game knows, skipped", name, index), MODULE_NAME, 1);
                    continue;
                }
                Step step{ *op, static_cast<uint8_t>(Int(node, "p1").value_or(0)), static_cast<uint16_t>(Int(node, "p2").value_or(0)),
                           static_cast<uint16_t>(Int(node, "p3").value_or(0)), static_cast<uint16_t>(Int(node, "p4").value_or(0)), 0, -1 };
                step.Id = static_cast<uint16_t>(Int(node, "id").value_or(*op == kEnd ? 0xFFFF : nextId));
                if (step.Id != 0xFFFF)
                    nextId = (step.Id + 1) & 0xFFFF;
                if (auto text = Str(node, "text"))
                {
                    step.Text = static_cast<int16_t>(texts.size());
                    texts.push_back(Utf8ToWide(*text));
                }
                else if (auto raw = Int(node, "textIndex"))
                    step.Text = static_cast<int16_t>(*raw);
                steps.push_back(step);
            }
        }
        if (steps.empty() || steps.back().Op != kEnd)
        {
            Logger::WriteLog(std::format("{}: the last step isn't End; added one so the game stops there", name), MODULE_NAME, 1);
            steps.push_back({ kEnd, 0, 0, 0, 0, 0xFFFF, -1 });
        }

        std::vector<uint8_t> out;
        Put<uint16_t>(out, static_cast<uint16_t>(steps.size()));
        Put<uint16_t>(out, static_cast<uint16_t>(texts.size()));
        Put<uint32_t>(out, 0);
        Put<uint32_t>(out, 0);
        for (auto& s : steps)
        {
            Put(out, s.Op);
            Put(out, s.P1);
            Put(out, s.P2);
            Put(out, s.P3);
            Put(out, s.P4);
            Put(out, s.Id);
            Put(out, s.Text);
        }
        for (auto& text : texts)
        {
            Put<uint32_t>(out, static_cast<uint32_t>(text.size()));
            Put<uint32_t>(out, 0);
        }
        for (auto& text : texts)
        {
            for (wchar_t c : text)
                Put<uint16_t>(out, static_cast<uint16_t>(c));
            Put<uint16_t>(out, 0);
        }
        return out;
    }

    void Load()
    {
        g_tutorials.clear();
        std::error_code error;
        if (!std::filesystem::is_directory(Folder(), error))
            return;
        for (auto& entry : std::filesystem::directory_iterator(Folder(), error))
        {
            // steam_tutorial_NN_L.json (24 characters: the number at 15-16, the language letter at 18)
            const std::string name = entry.path().filename().string();
            if (name.size() != 24 || _strnicmp(name.c_str(), "steam_tutorial_", 15) != 0 || name[17] != '_' || _stricmp(name.c_str() + 19, ".json") != 0 ||
                !std::isdigit(static_cast<unsigned char>(name[15])) || !std::isdigit(static_cast<unsigned char>(name[16])) ||
                !std::isalpha(static_cast<unsigned char>(name[18])))
                continue;
            const int number = (name[15] - '0') * 10 + (name[16] - '0');
            const char letter = static_cast<char>(std::toupper(static_cast<unsigned char>(name[18])));
            if (number < kFirstNew || number > kMaxNumber)
                continue;   // the game's own numbers are edited in the game data, not here
            try
            {
                std::ifstream file(entry.path());
                auto root = nlohmann::json::parse(file, nullptr, true, true);
                auto& tutorial = g_tutorials[number];
                tutorial.Files[letter] = Build(root, name);
                if (auto title = Str(root, "title"); title && !title->empty())
                    tutorial.Titles[letter] = Utf8ToWide(*title);
                // "arena" and "menu" are the same in every language: the English file decides, else the first one read
                if (letter == 'E' || !tutorial.SettingsFromEnglish)
                {
                    tutorial.Arena = Int(root, "arena").value_or(1);
                    auto menu = root.find("menu");
                    tutorial.InMenu = menu == root.end() || !menu->is_boolean() || menu->get<bool>();
                    tutorial.SettingsFromEnglish = letter == 'E';
                }
            }
            catch (const std::exception& ex)
            {
                Logger::WriteLog(std::format("Couldn't read tutorials\\{}: {}", name, ex.what()), MODULE_NAME, 2);
            }
        }
    }

    const Tutorial* Find(int number)
    {
        auto it = g_tutorials.find(number);
        return it != g_tutorials.end() && !it->second.Files.empty() ? &it->second : nullptr;
    }

    // ---- done marks: Yu-Gi-Oh-Ex/tutorials/done.json = { "saves": { "savegame-ex.dat": [27, 30] } } ----

    nlohmann::json g_done;
    bool g_doneRead = false;

    std::string SaveKey()
    {
        char name[MAX_PATH]{};
        if (Core::Load() && Core::Functions().GetSaveFile && Core::Functions().GetSaveFile(name, MAX_PATH) > 0)
            return name;
        return "savegame-ex.dat";
    }

    nlohmann::json& DoneList()
    {
        if (!g_doneRead)
        {
            g_doneRead = true;
            try
            {
                std::ifstream file(Folder() / "done.json");
                if (file)
                    g_done = nlohmann::json::parse(file, nullptr, true, true);
            }
            catch (const std::exception& ex)
            {
                Logger::WriteLog(std::format("Couldn't read tutorials\\done.json: {}", ex.what()), MODULE_NAME, 2);
            }
            if (!g_done.is_object())
                g_done = nlohmann::json::object();
        }
        auto& saves = g_done["saves"];
        if (!saves.is_object())
            saves = nlohmann::json::object();
        auto& list = saves[SaveKey()];
        if (!list.is_array())
            list = nlohmann::json::array();
        return list;
    }

    bool IsDone(int number)
    {
        for (auto& value : DoneList())
            if (value.is_number_integer() && value.get<int>() == number)
                return true;
        return false;
    }

    void MarkDone(int number)
    {
        if (IsDone(number))
            return;
        DoneList().push_back(number);
        std::error_code error;
        std::filesystem::create_directories(Folder(), error);
        std::ofstream file(Folder() / "done.json", std::ios::trunc);
        file << g_done.dump(2);
        Logger::WriteLog(std::format("Tutorial {} done ({}), kept in tutorials\\done.json", number, SaveKey()), MODULE_NAME, 0);
    }

    // ---- hooks ----

    void* __fastcall Hook_LoadScriptFile(const void* path)
    {
        const int number = GetTutorialDuelIndex();
        const Tutorial* tutorial = Find(number);
        if (!tutorial)
            return reinterpret_cast<LoadScriptFile_t>(orig_LoadScriptFile)(path);

        auto file = tutorial->Files.find(CurrentLanguage());
        if (file == tutorial->Files.end())
            file = tutorial->Files.find('E');
        if (file == tutorial->Files.end())
            file = tutorial->Files.begin();
        const auto& bytes = file->second;

        // LoadTutorial frees this with the CRT's free, so it comes from the same CRT
        static const auto crtMalloc = reinterpret_cast<void*(__cdecl*)(size_t)>(GetProcAddress(GetModuleHandleA("ucrtbase.dll"), "malloc"));
        auto* data = static_cast<uint8_t*>(crtMalloc ? crtMalloc(bytes.size()) : nullptr);
        if (!data)
            return reinterpret_cast<LoadScriptFile_t>(orig_LoadScriptFile)(path);
        std::memcpy(data, bytes.data(), bytes.size());

        // the fix-up LoadScriptFile does: the step and text table offsets, then each text's offset (0 for an empty one)
        const uint16_t stepCount = *reinterpret_cast<uint16_t*>(data);
        const uint16_t textCount = *reinterpret_cast<uint16_t*>(data + 2);
        const uint32_t textTable = 12u * (stepCount + 1u);
        *reinterpret_cast<uint32_t*>(data + 4) = 12;
        *reinterpret_cast<uint32_t*>(data + 8) = textTable;
        uint32_t offset = textTable + 8u * textCount;
        for (uint32_t i = 0; i < textCount; i++)
        {
            auto* entry = reinterpret_cast<uint32_t*>(data + textTable + 8 * i);
            entry[1] = entry[0] != 0 ? offset : 0;
            offset += 2 * entry[0] + 2;
        }
        Logger::WriteLog(std::format("Tutorial {} ({}) from tutorials\\: {} steps, {} texts", number, file->first, stepCount, textCount), MODULE_NAME, 0);
        return data;
    }

    int64_t __fastcall Hook_SetCurrentArenaId(unsigned int arena)
    {
        if (reinterpret_cast<uintptr_t>(_ReturnAddress()) == kArenaCallReturn)
        {
            // the tutorial list read g_TutorialArenaIds[n], which ends at 26
            const int number = GetTutorialDuelIndex();
            if (number >= kFirstNew)
                arena = Find(number) ? static_cast<unsigned int>(Find(number)->Arena) : 1u;
        }
        return reinterpret_cast<SetArena_t>(orig_SetCurrentArenaId)(arena);
    }

    // The list's numbers: the game's own (g_TutorialMenuList), then ours. Done on every entry so a WolfX save shows without a restart;
    // the count is written in place so the cursor the game keeps after a duel stays where it was.
    void __fastcall Hook_OnEnter(uint8_t* screen, void* ui)
    {
        Load();
        auto** numbers = reinterpret_cast<int**>(screen + kNumbers);   // begin, end, capacity end
        size_t gameCount = 0;
        while (reinterpret_cast<const int*>(kGameMenuList)[gameCount] != 0)
            gameCount++;
        if (numbers[0] && static_cast<size_t>(numbers[1] - numbers[0]) > gameCount)
            numbers[1] = numbers[0] + gameCount;   // what an earlier visit added
        for (auto& [number, tutorial] : g_tutorials)
        {
            if (!tutorial.InMenu || tutorial.Files.empty())
                continue;
            int value = number;
            if (numbers[1] != numbers[2])
                *numbers[1]++ = value;
            else
                VectorEmplaceInt(numbers, numbers[1], &value);
        }
        auto* list = reinterpret_cast<int*>(screen + kList);
        const int count = static_cast<int>(numbers[1] - numbers[0]);
        if (list[1] != count)
        {
            list[1] = count;
            if (list[0] >= count || list[2] >= count)
                list[0] = list[2] = 0;
        }
        reinterpret_cast<OnEnter_t>(orig_OnEnter)(screen, ui);
    }

    // The game draws "no title" and "not done" for numbers past its tables; put ours on the rows that show one.
    void __fastcall Hook_RefreshItems(uint8_t* screen, char keepScroll)
    {
        reinterpret_cast<RefreshItems_t>(orig_RefreshItems)(screen, keepScroll);
        auto** numbers = reinterpret_cast<int**>(screen + kNumbers);
        auto** rows = reinterpret_cast<uint8_t**>(screen + kRows);
        if (!numbers[0] || !rows[0])
            return;
        const int first = reinterpret_cast<const int*>(screen + kList)[2];
        const size_t count = static_cast<size_t>(numbers[1] - numbers[0]);
        const size_t rowCount = static_cast<size_t>(rows[1] - rows[0]) / kRowSize;
        for (size_t row = 0; row < rowCount; row++)
        {
            const size_t index = static_cast<size_t>(first) + row;
            if (index >= count || numbers[0][index] < kFirstNew)
                continue;
            const int number = numbers[0][index];
            uint8_t* item = rows[0] + row * kRowSize;
            const Tutorial* tutorial = Find(number);
            const std::wstring* title = tutorial ? Pick(tutorial->Titles) : nullptr;
            g_titles.push_back(title ? *title : std::format(L"Tutorial {:02}", number));
            if (g_titles.size() > 256)
                g_titles.pop_front();   // long gone from every widget
            SetTextById(*reinterpret_cast<void**>(item + kRowTitle), reinterpret_cast<intptr_t>(g_titles.back().c_str()));
            if (IsDone(number))
                SetRowMark(item + kRowMark, kDoneMark);
        }
    }

    // Called by the End stub in place of the game's done-bit write. true = a new number: skip the write (and the save request after it).
    bool __cdecl OnTutorialEnd()
    {
        const int number = GetTutorialDuelIndex();
        if (number < kFirstNew)
            return false;
        MarkDone(number);
        return true;
    }

    // At ExecuteStep's done-bit write: ask OnTutorialEnd, then either run the game's code (the Detours trampoline) or go past it.
    // Reached by a jump in the middle of the function, where rsp is 16-aligned and only volatile registers (about to be overwritten by
    // the call it replaces) are touched.
    bool AttachEndStub()
    {
        static uint8_t* page = static_cast<uint8_t*>(VirtualAlloc(nullptr, 0x1000, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE));
        if (!page)
            return false;
        const uint8_t code[] = {
            0x48, 0x83, 0xEC, 0x20,                         // sub rsp, 0x20
            0x48, 0xB8, 0, 0, 0, 0, 0, 0, 0, 0,             // mov rax, OnTutorialEnd (+6)
            0xFF, 0xD0,                                     // call rax
            0x48, 0x83, 0xC4, 0x20,                         // add rsp, 0x20
            0x84, 0xC0,                                     // test al, al
            0x75, 0x0E,                                     // jnz past the next jump and its slot
            0xFF, 0x25, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // jmp [rip+0] -> the game's code (Detours trampoline, slot at +30)
            0xFF, 0x25, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, // jmp [rip+0] -> after the write (slot at +44)
        };
        std::memcpy(page, code, sizeof(code));
        const auto onEnd = reinterpret_cast<uintptr_t>(&OnTutorialEnd);
        std::memcpy(page + 6, &onEnd, sizeof(onEnd));
        const uintptr_t after = kEndAfterMark;
        std::memcpy(page + 44, &after, sizeof(after));
        auto* slot = reinterpret_cast<PVOID*>(page + 30);
        *slot = reinterpret_cast<PVOID>(kEndMarkDone);
        FlushInstructionCache(GetCurrentProcess(), page, sizeof(code));
        return DetourAttach(slot, page) == NO_ERROR;
    }
}

void Tutorials::Attach()
{
    Load();
    DetourAttach(&(PVOID&)orig_LoadScriptFile, Hook_LoadScriptFile);
    DetourAttach(&(PVOID&)orig_SetCurrentArenaId, Hook_SetCurrentArenaId);
    DetourAttach(&(PVOID&)orig_OnEnter, Hook_OnEnter);
    DetourAttach(&(PVOID&)orig_RefreshItems, Hook_RefreshItems);
    if (!AttachEndStub())
        Logger::WriteLog("Tutorials: the End stub couldn't be attached", MODULE_NAME, 2);
    Logger::WriteLog(std::format("Tutorials: {} new tutorial(s) in Yu-Gi-Oh-Ex\\tutorials", g_tutorials.size()), MODULE_NAME, 0);
}
