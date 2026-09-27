#include <Windows.h>
#include <imgui_impl_dx11.h>
#include <string>
#include <vector>
#include <algorithm>
#include <cctype>
#include <imgui_impl_win32.h>
#include <imgui.h>

#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Log.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-SAVE.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-CARDS.h"

// A card shop that sells single cards for points (DP): search by name or Konami id, pick a card, buy a copy.
// Copies are added to the same table the game's own card shop and duel rewards write to (bits 0-2 = copies owned, max 3).
namespace
{
    constexpr const char* MODULE_NAME = "Yu-Gi-Oh-BetterCardShop";
    constexpr int kMaxCardId = 20000;   // size of the game's card table
    constexpr int kMaxCopies = 3;

    int g_Cost = 500;                   // price of one copy, in points
    int g_SelectedId = 0;
    char g_Search[64] = "";
    std::string g_Message;

    struct Entry
    {
        int Id;
        std::string Name;
        std::string Lower;
    };
    std::vector<Entry> g_Cards;         // every card the game has a name for, built the first time the window opens
    bool g_Built = false;

    const wchar_t* NameOf(int id)
    {
        __try
        {
            return reinterpret_cast<const wchar_t*>(YGO::CARDS::Get_CardNameFromKonamiId(static_cast<short>(id)));
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    std::string ToUtf8(const wchar_t* text)
    {
        int size = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
        if (size <= 1)
            return {};
        std::string result(static_cast<size_t>(size - 1), '\0');
        WideCharToMultiByte(CP_UTF8, 0, text, -1, result.data(), size, nullptr, nullptr);
        return result;
    }

    std::string Lower(std::string text)
    {
        std::transform(text.begin(), text.end(), text.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return text;
    }

    void BuildCardList()
    {
        g_Cards.clear();
        for (int id = 1; id < kMaxCardId; id++)
        {
            const wchar_t* name = NameOf(id);
            if (!name || !*name)
                continue;
            std::string text = ToUtf8(name);
            if (text.empty())
                continue;
            g_Cards.push_back({ id, text, Lower(text) });
        }
        g_Built = true;
        YGO::Log("Found " + std::to_string(g_Cards.size()) + " named cards", MODULE_NAME, 69);
    }

    // The save only exists once a profile is loaded. The window is drawn from the first frame, so the game's save functions are called
    // guarded: before a profile is loaded they crash or hand back an address that cannot be read.
    uint8_t* SavedTable()
    {
        __try
        {
            uint8_t* table = YGO::SAVE::Get_CardUnlockTable(YGO::SAVE::CURRENT_PROFILE);
            if (!table)
                return nullptr;
            volatile uint8_t first = table[0];
            volatile uint8_t last = table[kMaxCardId - 1];
            (void)first;
            (void)last;
            return table;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    uint64_t* SavedWallet()
    {
        __try
        {
            uint8_t* section = YGO::SAVE::Get_PlayerSection(YGO::SAVE::CURRENT_PROFILE);
            if (!section)
                return nullptr;
            uint64_t* wallet = reinterpret_cast<uint64_t*>(section + YGO::SAVE::PlayerSection::Wallet);
            volatile uint64_t probe = *wallet;
            (void)probe;
            return wallet;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return nullptr;
        }
    }

    uint8_t* Table() { return SavedTable(); }
    uint64_t* Wallet() { return SavedWallet(); }

    void Buy(int id)
    {
        uint8_t* table = Table();
        uint64_t* wallet = Wallet();
        if (!table || !wallet)
        {
            g_Message = "No save is loaded yet.";
            return;
        }

        const int owned = table[id] & 7;
        if (owned >= kMaxCopies)
            g_Message = "You already own 3 copies.";
        else if (*wallet < static_cast<uint64_t>(g_Cost))
            g_Message = "Not enough points.";
        else
        {
            *wallet -= static_cast<uint64_t>(g_Cost);
            table[id] = static_cast<uint8_t>((table[id] & ~7) | (owned + 1) | 8);   // one more copy, and mark it as seen
            const wchar_t* name = NameOf(id);
            g_Message = "Bought " + (name ? ToUtf8(name) : std::to_string(id)) + " for " + std::to_string(g_Cost) + " points.";
            YGO::Log(g_Message, MODULE_NAME, 0);
        }
    }
}

extern "C" __declspec(dllexport) void SetContext(ImGuiContext* Context)
{
    ImGui::SetCurrentContext(Context);
}

extern "C" __declspec(dllexport) void ProcessDetours()
{
}

extern "C" __declspec(dllexport) void ProcessWindow()
{
    ImGui::SetNextWindowSize(ImVec2(460, 520), ImGuiCond_FirstUseEver);
    ImGui::Begin("Yu-Gi-Oh! Better Shop");

    uint64_t* wallet = Wallet();
    uint8_t* table = Table();
    if (!wallet || !table)
    {
        ImGui::TextUnformatted("Load a profile to use the shop.");
        ImGui::End();
        return;
    }

    ImGui::Text("Points: %llu", static_cast<unsigned long long>(*wallet));
    ImGui::SameLine();
    ImGui::Text("   Price per card: %d", g_Cost);

    ImGui::SetNextItemWidth(-1);
    ImGui::InputTextWithHint("##search", "Search by card name or Konami id", g_Search, sizeof(g_Search));

    // Typing a number searches by id; anything else is a name search.
    const std::string query = Lower(g_Search);
    const bool isId = !query.empty() && std::all_of(query.begin(), query.end(), [](unsigned char c) { return std::isdigit(c) != 0; });
    const int idQuery = isId ? std::atoi(query.c_str()) : 0;

    if (ImGui::BeginChild("##results", ImVec2(0, -70), true))
    {
        int shown = 0;
        if (query.empty())
        {
            ImGui::TextDisabled("Type a card name or Konami id to search.");
            shown = -1;
        }
        else if (!g_Built)
            BuildCardList();   // built on the first search, not when the window opens

        for (const Entry& card : g_Cards)
        {
            if (query.empty())
                break;
            if (isId ? card.Id != idQuery : card.Lower.find(query) == std::string::npos)
                continue;
            if (++shown > 200)
            {
                ImGui::TextDisabled("More than 200 matches, keep typing to narrow it down.");
                break;
            }

            const int owned = table[card.Id] & 7;
            const std::string label = card.Name + "  (" + std::to_string(card.Id) + ")  x" + std::to_string(owned) + "##" + std::to_string(card.Id);
            if (ImGui::Selectable(label.c_str(), g_SelectedId == card.Id))
                g_SelectedId = card.Id;
        }
        if (shown == 0)
            ImGui::TextDisabled("No card matches.");
    }
    ImGui::EndChild();

    if (g_SelectedId > 0 && g_SelectedId < kMaxCardId)
    {
        const wchar_t* name = NameOf(g_SelectedId);
        ImGui::Text("Selected: %s (%d), you own %d", name ? ToUtf8(name).c_str() : "?", g_SelectedId, table[g_SelectedId] & 7);
        if (ImGui::Button("Buy one copy"))
            Buy(g_SelectedId);
    }
    else
        ImGui::TextDisabled("Pick a card from the list.");

    if (!g_Message.empty())
        ImGui::TextWrapped("%s", g_Message.c_str());

    ImGui::End();
}

extern "C" _declspec(dllexport) void ProcessInput(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
{
}

extern "C" _declspec(dllexport) void ProcessConfig()
{
    g_Cost = GetPrivateProfileIntA("Yu-Gi-Oh-BetterCardShop", "BetterShop-Cost", 500, ".\\Config.ini");
    YGO::Log("Card price " + std::to_string(g_Cost), MODULE_NAME, 69);
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD  ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
