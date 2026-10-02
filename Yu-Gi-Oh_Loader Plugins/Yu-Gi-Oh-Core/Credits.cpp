#include <Windows.h>
#include <filesystem>
#include <format>
#include <fstream>
#include <mutex>
#include <string>
#include <vector>

#include <json.hpp>

#include "Credits.h"
#include "Detours.h"
#include "Host.h"
#include "Logger.h"
#include "Save.h"

using json = nlohmann::json;

// How the game builds the Credits screen (YuGiOh.exe.i64):
//  - ScreenHelpCredits_LoadCredits (0x140844B10) loads main/ui/credits/credits.dat into the std::wstring at screen+0x290 (the whole file, BOM
//    included) and calls ScreenHelpCredits_ParseCredits (0x140845400) with the screen;
//  - ParseCredits skips the first character (the BOM), splits the rest on '\n', drops '\r', and turns each line into a 56 byte entry in the
//    vector at screen+0x2B0 ('[' heading, '*' picture, empty = gap); screen+0x314 is the total height in pixels. Nothing else calls it.
// The hook puts Core's text into the wstring (after the BOM, so before the game's credits) before ParseCredits runs. The string is changed with the game's own std::wstring::assign
// (0x140751310), so its buffer is allocated and freed by the game's heap, never this DLL's.
namespace
{
    constexpr size_t kCreditsText = 0x290;   // std::wstring (MSVC: buffer/pointer +0, size +0x10, capacity +0x18)

    struct GameWString
    {
        union
        {
            wchar_t Buffer[8];
            wchar_t* Pointer;
        };
        size_t Size;
        size_t Capacity;

        const wchar_t* Data() const { return Capacity >= 8 ? Pointer : Buffer; }
    };

    auto orig_ParseCredits = reinterpret_cast<void(__fastcall*)(uint8_t*)>(0x140845400);
    auto GameWString_Assign = reinterpret_cast<GameWString*(__fastcall*)(GameWString*, const wchar_t*, size_t)>(0x140751310);

    struct Section
    {
        std::wstring Title;
        std::vector<std::wstring> Lines;
        std::wstring Image;
    };

    // Always in the credits, whatever credits.json says.
    const Section kBuiltIn[] = {
        { L"ssjriou", { L"EH WHO CARES - I answered a question 8 months ago, I guess?" }, {} },
        { L"Death", { L"I don't mod LE anymore but will give you an answer that is three times longer than needed and make you forget what you asked in the first place." }, {} },
        { L"Very Tired Anna", { L"We are gonna make Link Evolution great again and yugiboomers are gonna pay for it." }, {} }
    };

    std::mutex g_Lock;
    std::vector<Section> g_Added;   // from Core_AddCredits

    std::wstring Widen(const std::string& text)
    {
        if (text.empty())
            return {};
        int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
        std::wstring wide(size, L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), size);
        return wide;
    }

    std::vector<std::wstring> SplitLines(const std::wstring& text)
    {
        std::vector<std::wstring> lines;
        size_t start = 0;
        while (start <= text.size())
        {
            size_t end = text.find(L'\n', start);
            if (end == std::wstring::npos)
                end = text.size();
            std::wstring line = text.substr(start, end - start);
            if (!line.empty() && line.back() == L'\r')
                line.pop_back();
            lines.push_back(std::move(line));
            start = end + 1;
        }
        return lines;
    }

    std::filesystem::path JsonPath()
    {
        return std::filesystem::path(Save::GameFolder()) / "Yu-Gi-Oh-Ex" / "credits.json";
    }

    json ReadJson()
    {
        std::ifstream file(JsonPath(), std::ios::binary);
        if (!file)
            return json::object();
        json parsed = json::parse(file, nullptr, false);
        if (!parsed.is_object())
        {
            Logger::WriteLog(std::format("{} is not valid JSON, it is left out of the credits", JsonPath().string()), MODULE_NAME, 1);
            return json::object();
        }
        return parsed;
    }

    void AppendSection(std::wstring& out, const Section& section)
    {
        if (!section.Title.empty())
            out += L"[" + section.Title + L"]\n";
        if (!section.Image.empty())
            out += L"*" + section.Image + L"*\n";
        for (const std::wstring& line : section.Lines)
        {
            // A text line starting with '*' would be read as a picture, and a missing picture crashes the game.
            size_t skip = line.find_first_not_of(L'*');
            out += (skip == std::wstring::npos ? std::wstring() : line.substr(skip)) + L"\n";
        }
        out += L"\n";
    }

    // Everything Core adds, as credits.dat lines ending in '\n'.
    std::wstring BuildText()
    {
        const json file = ReadJson();

        std::wstring text;
        for (const Section& section : kBuiltIn)
            AppendSection(text, section);

        if (file.contains("sections") && file["sections"].is_array())
        {
            for (const json& entry : file["sections"])
            {
                if (!entry.is_object())
                    continue;
                Section section;
                if (entry.contains("title") && entry["title"].is_string())
                    section.Title = Widen(entry["title"].get<std::string>());
                if (entry.contains("image") && entry["image"].is_string())
                    section.Image = Widen(entry["image"].get<std::string>());
                if (entry.contains("lines") && entry["lines"].is_array())
                {
                    for (const json& line : entry["lines"])
                    {
                        if (line.is_string())
                            section.Lines.push_back(Widen(line.get<std::string>()));
                    }
                }
                AppendSection(text, section);
            }
        }

        {
            std::lock_guard lock(g_Lock);
            for (const Section& section : g_Added)
                AppendSection(text, section);
        }

        if (file.value("plugins", true))
        {
            Section plugins{ L"Yu-Gi-Oh-Ex Plugins", {}, {} };
            const int count = Host::Count();
            for (int i = 0; i < count; ++i)
            {
                CorePluginInfo info{};
                info.Size = sizeof(info);
                if (Host::Info(i, info) && info.Loaded)
                    plugins.Lines.push_back(Widen(info.Title[0] ? info.Title : info.Name));
            }
            if (!plugins.Lines.empty())
                AppendSection(text, plugins);
        }
        return text;
    }

    void __fastcall Hook_ParseCredits(uint8_t* screen)
    {
        auto* credits = reinterpret_cast<GameWString*>(screen + kCreditsText);

        // Everything Core adds goes first, then the game's own credits. The game always skips the first character as the BOM, so Core's text
        // goes in after it (a missing credits.dat, an empty string, gets one).
        std::wstring text(credits->Data(), credits->Size);
        if (text.empty())
            text = std::wstring(1, static_cast<wchar_t>(0xFEFF));
        text.insert(1, BuildText());
        GameWString_Assign(credits, text.c_str(), text.size());

        orig_ParseCredits(screen);
    }
}

bool Credits::Install()
{
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_ParseCredits, Hook_ParseCredits);
    LONG err = DetourTransactionCommit();

    Logger::WriteLog(std::format("Credits hook: {}", err), MODULE_NAME, err == 0 ? 0 : 2);
    return err == 0;
}

void Credits::Add(const std::string& Heading, const std::string& Lines)
{
    Section section{ Widen(Heading), SplitLines(Widen(Lines)), {} };
    std::lock_guard lock(g_Lock);
    g_Added.push_back(std::move(section));
}
