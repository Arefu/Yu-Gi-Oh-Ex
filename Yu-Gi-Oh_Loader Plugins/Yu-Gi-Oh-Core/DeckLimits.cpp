#include "DeckLimits.h"

#include <Windows.h>
#include <algorithm>
#include <deque>
#include <format>
#include <string>

#include "Detours.h"
#include "Logger.h"
#include "Save.h"
#include "Yu-Gi-Oh-Memory.h"

namespace
{
    constexpr uintptr_t kMinMainImm = 0x1407F9CDE;         // Deck_ValidateAndCountByInternalId: mov eax, 28h (B8 28 00 00 00)
    constexpr uintptr_t kMaxMainImm = 0x1407F9CF8;         // Deck_ValidateAndCountByInternalId: cmp cx, 3Ch (66 83 F9 3C)
    constexpr uintptr_t kSectionMaxSizes = 0x140A521D8;    // g_DeckSectionMaxSizes int[3]
    constexpr uintptr_t kStringLocations = 0x143329E80;    // YGO::LOCAL::g_iStringLocations[7] -> { u32 count; wchar_t** strings (+8) }
    constexpr int kLanguages = 7;

    using LoadStringFiles_t = void(__fastcall*)();
    uintptr_t orig_LoadStringFiles = 0x140870E50;          // YGO::LOCAL::Load_StringFiles (YGO::GAME::LoadLanguageContent, startup and language change)

    struct Limits
    {
        int MinMain = 40, MaxMain = 60, MaxExtra = 15, MaxSide = 15;
    };
    Limits g_config;        // Config.ini's
    Limits g_now;           // in use (Config.ini's, or what Core_SetDeckLimits set)
    bool g_codeOk = false;  // the two immediates are where they were traced

    struct TextNumber
    {
        int Id;          // string id = BND index + 1 (RIX::Screen::ShowMessage reads strings[id - 1])
        int Game;        // the number the game's text says
        int Limits::* Value;
        int Shown;       // the number the loaded text says now
    };

    // g_DeckSectionFullMsgIds (0x140A73028) = 638 main / 639 extra / 637 side; 764 is the deck list's " Less than 40 cards".
    TextNumber g_texts[] = { { 638, 60, &Limits::MaxMain, 60 }, { 639, 15, &Limits::MaxExtra, 15 }, { 637, 15, &Limits::MaxSide, 15 },
                             { 764, 40, &Limits::MinMain, 40 } };

    // Our copies of the rewritten strings. The game points at them, so they are never freed (each change adds a few).
    std::deque<std::wstring> g_kept;

    // Swaps the first stand-alone run of digits equal to From for To ("60" in "...more than 60 cards" but not in "160").
    bool ReplaceNumber(std::wstring& text, int from, int to)
    {
        const std::wstring digits = std::to_wstring(from);
        for (size_t at = text.find(digits); at != std::wstring::npos; at = text.find(digits, at + 1))
        {
            const bool digitBefore = at > 0 && iswdigit(text[at - 1]);
            const bool digitAfter = at + digits.size() < text.size() && iswdigit(text[at + digits.size()]);
            if (!digitBefore && !digitAfter)
            {
                text.replace(at, digits.size(), std::to_wstring(to));
                return true;
            }
        }
        return false;
    }

    // Fresh = the game just loaded its strings (they say the game's numbers again).
    void RewriteStrings(bool fresh)
    {
        int changed = 0;
        for (TextNumber& text : g_texts)
        {
            const int from = fresh ? text.Game : text.Shown;
            const int to = g_now.*text.Value;
            text.Shown = from;
            if (from == to)
                continue;
            for (int language = 0; language < kLanguages; ++language)
            {
                const uintptr_t table = *reinterpret_cast<const uintptr_t*>(kStringLocations + 8 * language);
                if (!table)
                    continue;
                const uint32_t count = *reinterpret_cast<const uint32_t*>(table);
                wchar_t** strings = *reinterpret_cast<wchar_t***>(table + 8);
                if (!strings || static_cast<uint32_t>(text.Id) > count || !strings[text.Id - 1])
                    continue;
                std::wstring value = strings[text.Id - 1];
                if (!ReplaceNumber(value, from, to))
                    continue;
                strings[text.Id - 1] = g_kept.emplace_back(std::move(value)).data();
                ++changed;
            }
            text.Shown = to;
        }
        if (changed)
            Logger::WriteLog(std::format("Deck limit text: {} strings rewritten", changed), MODULE_NAME, 0);
    }

    void __fastcall Hook_LoadStringFiles()
    {
        reinterpret_cast<LoadStringFiles_t>(orig_LoadStringFiles)();
        RewriteStrings(true);
    }

    void Apply(const Limits& limits)
    {
        g_now = limits;
        if (!g_codeOk)
            return;
        // the legality check (flag 4 = main deck size out of range): the imm32 after B8, the imm8 after 66 83 F9
        YGO::Memory::WriteValue<int32_t>(kMinMainImm + 1, g_now.MinMain);
        YGO::Memory::WriteValue<uint8_t>(kMaxMainImm + 3, static_cast<uint8_t>(g_now.MaxMain));
        // the deck editor's "this section is full"
        const int sizes[3] = { g_now.MaxMain, g_now.MaxExtra, g_now.MaxSide };
        YGO::Memory::WriteBytes(kSectionMaxSizes, sizes, sizeof(sizes));
        RewriteStrings(false);
        Logger::WriteLog(std::format("Deck limits: main {}-{}, extra up to {}, side up to {}", g_now.MinMain, g_now.MaxMain, g_now.MaxExtra,
            g_now.MaxSide), MODULE_NAME, 0);
    }

    Limits Clamped(int minMain, int maxMain, int maxExtra, int maxSide)
    {
        Limits limits;
        limits.MinMain = std::clamp(minMain, 1, 60);
        limits.MaxMain = std::clamp(maxMain, limits.MinMain, 60);
        limits.MaxExtra = std::clamp(maxExtra, 0, 15);
        limits.MaxSide = std::clamp(maxSide, 0, 15);
        return limits;
    }
}

namespace DeckLimits
{
    void Set(int minMain, int maxMain, int maxExtra, int maxSide)
    {
        Apply(minMain < 0 ? g_config : Clamped(minMain, maxMain, maxExtra, maxSide));
    }

    void Install()
    {
        const std::string ini = Save::GameFolder() + "Config.ini";
        const auto read = [&](const char* key, int fallback) { return static_cast<int>(GetPrivateProfileIntA("Yu-Gi-Oh-Core", key, fallback, ini.c_str())); };
        g_config = Clamped(read("MinMainDeck", 40), read("MaxMainDeck", 60), read("MaxExtraDeck", 15), read("MaxSideDeck", 15));

        // the game's own bytes, checked once; after that Apply only rewrites the values
        const auto* minCode = reinterpret_cast<const uint8_t*>(kMinMainImm);
        const auto* maxCode = reinterpret_cast<const uint8_t*>(kMaxMainImm);
        g_codeOk = minCode[0] == 0xB8 && minCode[1] == 0x28 && minCode[2] == 0 && minCode[3] == 0 && minCode[4] == 0 &&
                   maxCode[0] == 0x66 && maxCode[1] == 0x83 && maxCode[2] == 0xF9 && maxCode[3] == 0x3C;
        if (!g_codeOk)
            Logger::WriteLog(std::format("Deck limits: the deck check is not the traced code (found {} / {}); deck limits are off",
                YGO::Memory::HexBytes(minCode, 5), YGO::Memory::HexBytes(maxCode, 4)), MODULE_NAME, 2);

        // Always hooked: Core_SetDeckLimits can change the numbers later.
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_LoadStringFiles, Hook_LoadStringFiles);
        const LONG error = DetourTransactionCommit();
        if (error != NO_ERROR)
            Logger::WriteLog(std::format("Deck limits: text hook failed ({})", error), MODULE_NAME, 2);

        // Core can start after the game's first string load; the hook covers later loads (language changes).
        if (g_config.MinMain != 40 || g_config.MaxMain != 60 || g_config.MaxExtra != 15 || g_config.MaxSide != 15)
            Apply(g_config);
        else
            g_now = g_config;
    }
}
