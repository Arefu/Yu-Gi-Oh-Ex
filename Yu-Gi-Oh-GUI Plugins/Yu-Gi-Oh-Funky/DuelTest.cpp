#include "DuelTest.h"

#include <Windows.h>
#include <detours.h>
#include "imgui.h"

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <format>
#include <fstream>
#include <string>
#include <vector>

#include <json.hpp>

namespace
{
    // ---- the game's layout (see the IDB: YGO::DUEL::Duel_LoadDeck sub_1400822F0 and Duel_ShuffleDeck sub_140080E90) ----

    // The duel engine loads a player's deck from a deck struct (Duel_LoadDeck(player, deck)): dword main count @0, dword extra count @8,
    // main ids (words) @12 (75 at most), extra ids (words) @162 (15 at most; the decompiler shows a2+81 on a dword pointer, the code adds 0xA2). Then it shuffles the engine's deck (Duel_ShuffleDeck(player)) and the opening hand is drawn from it.
    constexpr size_t kMainCountOffset = 0, kExtraCountOffset = 8, kMainIdsOffset = 12, kExtraIdsOffset = 162;
    constexpr uint32_t kMainCapacity = 75, kExtraCapacity = 15;

    // The engine's per player state (stride 3476 bytes): dword deck count at 0x143497C50, the deck's card dwords at 0x143497FBC.
    // A card dword: the low 14 bits are the Konami id, the rest is the card's slot number and owner.
    constexpr uintptr_t kPlayerStride = 3476;
    constexpr uintptr_t kEngineDeckCount = 0x143497C50;
    constexpr uintptr_t kEngineDeckCards = 0x143497FBC;
    constexpr uint32_t kEngineDeckMax = 120;

    constexpr uintptr_t kFullCardProps = 0x142927600;
    constexpr uintptr_t kFullCardPropsStride = 0xA0;
    constexpr int kMaxCardId = 0x4E1F;

    using LoadDeck_t = int64_t(__fastcall*)(char, int32_t*);
    using ShuffleDeck_t = int64_t(__fastcall*)(int);
    LoadDeck_t orig_LoadDeck = reinterpret_cast<LoadDeck_t>(0x1400822F0);
    ShuffleDeck_t orig_ShuffleDeck = reinterpret_cast<ShuffleDeck_t>(0x140080E90);

    // ---- the log (Yu-Gi-Oh-Console's WriteLog, when it is loaded) ----

    void Log(const std::string& text, int level = 0)
    {
        static auto write = reinterpret_cast<void(__cdecl*)(std::string, std::string, int)>(
            GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-Console.dll"), "WriteLog"));
        if (write)
            write(text, "Yu-Gi-Oh-Funky", level);
    }

    // Yu-Gi-Oh-Cards borrows a real vanilla id for one duel to stand in for a custom card above 0x3FFF (see
    // Card::ResolveDuelSessionId in Card.h and the "ygo-duel-id-remap-plan" memory) - by the time Duel_LoadDeck
    // has run, the engine's own deck array holds that borrowed id, not the card's real one. This is the pure,
    // side-effect-free query (never creates a new borrow) for "what did this id actually become for this duel,
    // if anything" - falls back to returning `id` unchanged if Yu-Gi-Oh-Cards is not loaded.
    uint16_t ActiveDuelSessionId(uint16_t id)
    {
        static auto resolve = reinterpret_cast<unsigned short(__cdecl*)(unsigned short)>(
            GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-MoreCards.dll"), "Card_GetActiveDuelSessionId"));
        return resolve ? resolve(id) : id;
    }

    // Yu-Gi-Oh-Funky's own Duel_LoadDeck hook attaches BEFORE Yu-Gi-Oh-Cards' does (see the boot log), which
    // makes this one the INNER hook in the detour chain: Cards resolves the deck's ids first, then calls
    // through to this hook, which is where AddMissing appends new cards - AFTER Cards already finished, so
    // anything appended here is never seen by Card::ResolveDuelSessionId at all. A card added this way must
    // resolve (and, if above 0x3FFF, borrow) itself, right here, before being written into the deck struct -
    // this is NOT the side-effect-free ActiveDuelSessionId above, it can create a new borrow.
    uint16_t ResolveForDeck(uint16_t id)
    {
        static auto resolve = reinterpret_cast<unsigned short(__cdecl*)(unsigned short)>(
            GetProcAddress(GetModuleHandleA("Yu-Gi-Oh-MoreCards.dll"), "Card_ResolveDuelSessionId"));
        return resolve ? resolve(id) : id;
    }

    // What the last call of the detour saw, shown in the window so it can be checked without the log.
    struct LastCall
    {
        int Calls = 0;
        int Player = -1;
        bool Applied = false;
        uint32_t MainBefore = 0, ExtraBefore = 0, MainAfter = 0, ExtraAfter = 0;
        int Stacked = 0;
        int HandCount = 0;
        std::string FirstIds;
    } g_Last;

    // ---- what the player picked ----

    bool g_Enabled = false;
    bool g_AddMissing = true;
    int g_Player = 0; // 0 = you
    std::vector<int> g_Hand;
    std::vector<int> g_Extra;

    const wchar_t* CardName(int id)
    {
        if (id < 1 || id > kMaxCardId)
            return L"";
        auto* name = *reinterpret_cast<const wchar_t* const*>(kFullCardProps + static_cast<uintptr_t>(id) * kFullCardPropsStride + 8);
        return name ? name : L"";
    }

    std::string ToUtf8(const wchar_t* text)
    {
        int size = WideCharToMultiByte(CP_UTF8, 0, text, -1, nullptr, 0, nullptr, nullptr);
        if (size <= 1)
            return std::string();
        std::string out(static_cast<size_t>(size - 1), '\0');
        WideCharToMultiByte(CP_UTF8, 0, text, -1, out.data(), size, nullptr, nullptr);
        return out;
    }

    std::wstring ToWide(const char* text)
    {
        int size = MultiByteToWideChar(CP_UTF8, 0, text, -1, nullptr, 0);
        if (size <= 1)
            return std::wstring();
        std::wstring out(static_cast<size_t>(size - 1), L'\0');
        MultiByteToWideChar(CP_UTF8, 0, text, -1, out.data(), size);
        return out;
    }

    // ---- saved in Config.ini ----

    constexpr const char* kIni = ".\\Config.ini";
    constexpr const char* kSection = "Yu-Gi-Oh-Funky";

    std::string Join(const std::vector<int>& ids)
    {
        std::string out;
        for (int id : ids)
            out += (out.empty() ? "" : ",") + std::to_string(id);
        return out;
    }

    std::vector<int> Split(const char* text)
    {
        std::vector<int> ids;
        for (const char* p = text; *p;)
        {
            char* end = nullptr;
            long value = strtol(p, &end, 10);
            if (end == p)
                break;
            if (value >= 1 && value <= kMaxCardId)
                ids.push_back(static_cast<int>(value));
            p = *end == ',' ? end + 1 : end;
        }
        return ids;
    }

    void Save()
    {
        WritePrivateProfileStringA(kSection, "DuelTestEnabled", g_Enabled ? "1" : "0", kIni);
        WritePrivateProfileStringA(kSection, "DuelTestAddMissing", g_AddMissing ? "1" : "0", kIni);
        WritePrivateProfileStringA(kSection, "DuelTestPlayer", std::to_string(g_Player).c_str(), kIni);
        WritePrivateProfileStringA(kSection, "DuelTestHand", Join(g_Hand).c_str(), kIni);
        WritePrivateProfileStringA(kSection, "DuelTestExtra", Join(g_Extra).c_str(), kIni);
    }

    void Load()
    {
        char buffer[512]{};
        g_Enabled = GetPrivateProfileIntA(kSection, "DuelTestEnabled", 0, kIni) != 0;
        g_AddMissing = GetPrivateProfileIntA(kSection, "DuelTestAddMissing", 1, kIni) != 0;
        g_Player = GetPrivateProfileIntA(kSection, "DuelTestPlayer", 0, kIni) == 1 ? 1 : 0;
        GetPrivateProfileStringA(kSection, "DuelTestHand", "", buffer, sizeof(buffer), kIni);
        g_Hand = Split(buffer);
        GetPrivateProfileStringA(kSection, "DuelTestExtra", "", buffer, sizeof(buffer), kIni);
        g_Extra = Split(buffer);
    }

    // ---- the detours ----

    // Adds the chosen ids the deck does not hold, so a test card does not have to be in the deck being played. Every chosen hand card is added
    // up to two copies: the game draws from one end of the shuffled deck, and one copy is put at each end afterwards so it works either way.
    void AddMissing(int32_t* deck)
    {
        auto* bytes = reinterpret_cast<uint8_t*>(deck);
        auto& mainCount = *reinterpret_cast<uint32_t*>(bytes + kMainCountOffset);
        auto& extraCount = *reinterpret_cast<uint32_t*>(bytes + kExtraCountOffset);
        auto* mainIds = reinterpret_cast<uint16_t*>(bytes + kMainIdsOffset);
        auto* extraIds = reinterpret_cast<uint16_t*>(bytes + kExtraIdsOffset);
        if (mainCount > kMainCapacity || extraCount > kExtraCapacity)
            return; // not the layout expected: leave the deck alone

        // A custom card above 0x3FFF now borrows a real vanilla id for the duel (Yu-Gi-Oh-Cards' Duel_LoadDeck
        // hook does this once this function calls the original) - it is written into the deck struct like any
        // other id and left for that hook to resolve, not excluded here. Only a genuinely unknown id is dropped.
        std::vector<int> wanted;
        for (int id : g_Hand)
        {
            if (!*CardName(id))
            {
                Log(std::format("Hand card {} is not a card the game has, skipped", id), 1);
                continue;
            }
            wanted.push_back(id);
            wanted.push_back(id);
        }
        for (uint32_t i = 0; i < mainCount; ++i)
        {
            auto it = std::find(wanted.begin(), wanted.end(), mainIds[i]);
            if (it != wanted.end())
                wanted.erase(it);
        }
        for (int id : wanted)
        {
            if (mainCount >= kMainCapacity)
                break;
            // Resolve (and, if above 0x3FFF, borrow) now: this hook runs after Yu-Gi-Oh-Cards' own resolution
            // pass already finished on this deck, so this card would otherwise reach the real engine raw.
            mainIds[mainCount++] = ResolveForDeck(static_cast<uint16_t>(id));
        }

        wanted.clear();
        for (int id : g_Extra)
        {
            if (*CardName(id))
                wanted.push_back(id);
            else
                Log(std::format("Extra deck card {} is not a card the game has, skipped", id), 1);
        }
        for (uint32_t i = 0; i < extraCount; ++i)
        {
            auto it = std::find(wanted.begin(), wanted.end(), extraIds[i]);
            if (it != wanted.end())
                wanted.erase(it);
        }
        for (int id : wanted)
        {
            if (extraCount >= kExtraCapacity)
                break;
            extraIds[extraCount++] = ResolveForDeck(static_cast<uint16_t>(id));
        }
    }

    // After the shuffle: the chosen cards go to the front of the engine's deck, and a second copy of each to the back.
    void StackDeck(int player)
    {
        g_Last.Stacked = 0;
        const uintptr_t offset = static_cast<uintptr_t>(player & 1) * kPlayerStride;
        const uint32_t count = *reinterpret_cast<uint32_t*>(kEngineDeckCount + offset);
        auto* deck = reinterpret_cast<uint32_t*>(kEngineDeckCards + offset);
        if (count == 0 || count > kEngineDeckMax)
            return;

        std::vector<bool> used(count, false);
        std::vector<uint32_t> front, back;
        for (int id : g_Hand)
        {
            // By now Yu-Gi-Oh-Cards' Duel_LoadDeck hook has already run: if `id` was above 0x3FFF it borrowed
            // a real vanilla id for this duel, and the engine's own deck array (searched below) holds that
            // borrowed id, not `id` itself. Resolve it the same way before searching, or a remapped card is
            // never found here (it still loaded into the deck correctly - it just would not get stacked
            // to the front of the draw).
            const int wantId = ActiveDuelSessionId(static_cast<uint16_t>(id));
            int first = -1, second = -1;
            for (uint32_t i = 0; i < count; ++i)
            {
                if (used[i] || static_cast<int>(deck[i] & 0x3FFF) != wantId)
                    continue;
                if (first < 0)
                    first = static_cast<int>(i);
                else
                {
                    second = static_cast<int>(i);
                    break;
                }
            }
            if (first < 0)
                continue;
            used[first] = true;
            front.push_back(deck[first]);
            if (second >= 0)
            {
                used[second] = true;
                back.push_back(deck[second]);
            }
        }

        std::vector<uint32_t> order = front;
        for (uint32_t i = 0; i < count; ++i)
        {
            if (!used[i])
                order.push_back(deck[i]);
        }
        order.insert(order.end(), back.rbegin(), back.rend());
        std::copy(order.begin(), order.end(), deck);

        g_Last.Stacked = static_cast<int>(front.size());
        g_Last.MainAfter = count;
        g_Last.FirstIds.clear();
        for (uint32_t i = 0; i < 6 && i < count; ++i)
            g_Last.FirstIds += (i ? "," : "") + std::to_string(deck[i] & 0x3FFF);
        g_Last.FirstIds += " ... ";
        for (uint32_t i = count > 6 ? count - 6 : 0; i < count; ++i)
            g_Last.FirstIds += std::to_string(deck[i] & 0x3FFF) + (i + 1 < count ? "," : "");
    }

    // The engine loads a player's deck from the deck struct: chosen cards are added to it here.
    int64_t __fastcall Hook_LoadDeck(char player, int32_t* deck)
    {
        const bool mine = g_Enabled && (player & 1) == g_Player && deck;
        ++g_Last.Calls;
        g_Last.Player = player & 1;
        g_Last.Applied = mine;
        if (deck)
        {
            auto* bytes = reinterpret_cast<uint8_t*>(deck);
            g_Last.MainBefore = *reinterpret_cast<uint32_t*>(bytes + kMainCountOffset);
            g_Last.ExtraBefore = *reinterpret_cast<uint32_t*>(bytes + kExtraCountOffset);
        }

        if (mine && g_AddMissing)
            AddMissing(deck);

        Log(std::format("Duel_LoadDeck(player {}): main {} extra {} before{}", static_cast<int>(player & 1), g_Last.MainBefore, g_Last.ExtraBefore,
            mine ? ", chosen cards added" : ", not applied"));
        return orig_LoadDeck(player, deck);
    }

    // The engine shuffles the deck once, right after loading: the order is set here.
    int64_t __fastcall Hook_ShuffleDeck(int player)
    {
        int64_t result = orig_ShuffleDeck(player);
        if (g_Enabled && (player & 1) == g_Player)
        {
            StackDeck(player);
            Log(std::format("Duel_ShuffleDeck(player {}): stacked {} card(s), deck {} cards: {}", player & 1, g_Last.Stacked, g_Last.MainAfter, g_Last.FirstIds));
        }
        return result;
    }

    // ---- window ----

    // Cards are picked by hand (ids or names, below) - there are no ready-made setups: the custom ids change whenever
    // cards.json is regenerated, so a hardcoded list only ever pointed at the wrong cards.
    // Adds every card in `text` (ids and/or names, separated by commas or new lines) to `list`. A number is a Konami
    // id; anything else is a name: an exact match wins, otherwise the first card whose name contains it.
    std::string AddByText(const char* text, std::vector<int>& list);

    void DrawList(const char* label, std::vector<int>& ids)
    {
        ImGui::PushID(label);
        ImGui::TextUnformatted(label);
        for (size_t i = 0; i < ids.size(); ++i)
        {
            ImGui::PushID(static_cast<int>(i));
            if (ImGui::SmallButton("x"))
            {
                ids.erase(ids.begin() + static_cast<std::ptrdiff_t>(i));
                Save();
                ImGui::PopID();
                break;
            }
            ImGui::SameLine();
            if (!*CardName(ids[i]))
                ImGui::TextColored(ImVec4(1.0f, 0.4f, 0.3f, 1.0f), "%d  not a card the game has: ignored in a duel", ids[i]);
            else if (ids[i] > 0x3FFF)
                ImGui::TextColored(ImVec4(0.6f, 0.8f, 1.0f, 1.0f), "%d  %s (borrows a vanilla id for the duel)", ids[i], ToUtf8(CardName(ids[i])).c_str());
            else
                ImGui::Text("%d  %s", ids[i], ToUtf8(CardName(ids[i])).c_str());
            ImGui::PopID();
        }
        if (ids.empty())
            ImGui::TextDisabled("(none)");
        ImGui::PopID();
    }
}

namespace
{
    std::string AddByText(const char* text, std::vector<int>& list)
    {
        std::string report;
        const std::string all = text;
        size_t start = 0;
        while (start <= all.size())
        {
            size_t end = all.find_first_of(",\n", start);
            if (end == std::string::npos)
                end = all.size();
            std::string token = all.substr(start, end - start);
            start = end + 1;

            const size_t first = token.find_first_not_of(" \t\r");
            if (first == std::string::npos)
                continue;
            token = token.substr(first, token.find_last_not_of(" \t\r") - first + 1);

            int found = 0;
            if (token.find_first_not_of("0123456789") == std::string::npos)
            {
                const int id = atoi(token.c_str());
                if (id >= 1 && id <= kMaxCardId && *CardName(id))
                    found = id;
            }
            else
            {
                auto lower = [](std::wstring w) { for (auto& c : w) c = static_cast<wchar_t>(towlower(c)); return w; };
                const std::wstring needle = lower(ToWide(token.c_str()));
                int contains = 0;
                for (int id = 1; id <= kMaxCardId && !found; ++id)
                {
                    const wchar_t* name = CardName(id);
                    if (!*name)
                        continue;
                    const std::wstring hay = lower(name);
                    if (hay == needle)
                        found = id;
                    else if (!contains && hay.find(needle) != std::wstring::npos)
                        contains = id;
                }
                if (!found)
                    found = contains;
            }

            if (found)
            {
                list.push_back(found);
                report += "added " + std::to_string(found) + " " + ToUtf8(CardName(found)) + "\n";
            }
            else
                report += "no card matches \"" + token + "\"\n";
        }
        return report;
    }
}

namespace
{
    // The game's archetype test (Is_CardInNamedArchetype_Thunk 0x1407EB9E0): is the card (Konami id) a member of archetype `code`.
    // Yu-Gi-Oh-Cards hooks the function under it, so custom cards and custom archetypes (419 and up) answer too.
    using IsInArchetype_t = int(__fastcall*)(unsigned int, int);
    const IsInArchetype_t kIsInArchetype = reinterpret_cast<IsInArchetype_t>(0x1407EB9E0);

    // <game folder>\Yu-Gi-Oh-Ex\Archetypes.json ({ "archetypes": [ { "code": 397, "name": "Dark World" } ] }): a name is looked up here.
    int ArchetypeCodeFromText(const std::string& text, std::string& name)
    {
        if (!text.empty() && text.find_first_not_of("0123456789") == std::string::npos)
            return atoi(text.c_str());

        char exe[MAX_PATH]{};
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        std::string folder = exe;
        folder.resize(folder.find_last_of("\\/") + 1);
        std::ifstream file(folder + "Yu-Gi-Oh-Ex\\Archetypes.json");
        if (!file)
            return 0;
        nlohmann::json root = nlohmann::json::parse(file, nullptr, false, true);
        if (root.is_discarded() || !root.contains("archetypes") || !root["archetypes"].is_array())
            return 0;

        auto lower = [](std::string v) { for (auto& c : v) c = static_cast<char>(tolower(static_cast<unsigned char>(c))); return v; };
        const std::string needle = lower(text);
        int contains = 0;
        for (const auto& entry : root["archetypes"])
        {
            const std::string entryName = entry.value("name", std::string());
            const int code = entry.value("code", 0);
            if (lower(entryName) == needle)
            {
                name = entryName;
                return code;
            }
            if (!contains && lower(entryName).find(needle) != std::string::npos)
            {
                contains = code;
                name = entryName;
            }
        }
        return contains;
    }

    // Adds up to `limit` cards of the archetype (a code or a name from Archetypes.json) to `list`, in id order.
    std::string AddArchetypeMembers(const char* text, std::vector<int>& list, int limit)
    {
        std::string trimmed = text;
        const size_t first = trimmed.find_first_not_of(" \t\r\n");
        if (first == std::string::npos)
            return "type an archetype name or code\n";
        trimmed = trimmed.substr(first, trimmed.find_last_not_of(" \t\r\n") - first + 1);

        std::string name;
        const int code = ArchetypeCodeFromText(trimmed, name);
        if (code <= 0)
            return "no archetype matches \"" + trimmed + "\" (Yu-Gi-Oh-Ex\\Archetypes.json lists the names)\n";

        std::vector<int> members;
        for (int id = 1; id <= kMaxCardId; ++id)
        {
            if (*CardName(id) && kIsInArchetype(static_cast<unsigned int>(id), code))
                members.push_back(id);
        }
        int added = 0;
        for (int id : members)
        {
            if (added >= limit)
                break;
            if (std::find(list.begin(), list.end(), id) != list.end())
                continue;
            list.push_back(id);
            ++added;
        }
        return std::format("archetype {} {}: {} member(s) in the game, added {}\n", code, name, members.size(), added);
    }
}

void DuelTest::Install()
{
    Load();

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_LoadDeck, Hook_LoadDeck);
    DetourAttach(&(PVOID&)orig_ShuffleDeck, Hook_ShuffleDeck);
    DetourTransactionCommit();
}

void DuelTest::Draw()
{
    static char g_Bulk[512]{};
    static char g_Archetype[64]{};
    static int g_ArchetypeLimit = 8;
    static std::string g_Result;

    // -- what the next duel does --
    if (ImGui::Checkbox("Stack the opening hand", &g_Enabled))
        Save();
    ImGui::SameLine();
    ImGui::SetNextItemWidth(110);
    if (ImGui::Combo("for", &g_Player, "you the opponent "))
        Save();
    if (ImGui::Checkbox("Add cards the deck does not have", &g_AddMissing))
        Save();
    ImGui::TextDisabled("Applies to the next duel. The cards go on top of the shuffled deck (a hand holds 5); extra deck cards are added to it (15 at most).");

    // -- pick cards --
    ImGui::Separator();
    ImGui::TextUnformatted("Cards (ids or names, separated by commas)");
    ImGui::SetNextItemWidth(-1);
    ImGui::InputText("##bulk", g_Bulk, sizeof(g_Bulk));
    if (ImGui::Button("Add to hand"))
    {
        g_Result = AddByText(g_Bulk, g_Hand);
        g_Enabled = true;
        Save();
    }
    ImGui::SameLine();
    if (ImGui::Button("Add to extra deck"))
    {
        g_Result = AddByText(g_Bulk, g_Extra);
        g_Enabled = true;
        Save();
    }

    ImGui::TextUnformatted("Archetype (name from Archetypes.json, or its code)");
    ImGui::SetNextItemWidth(220);
    ImGui::InputText("##archetype", g_Archetype, sizeof(g_Archetype));
    ImGui::SameLine();
    ImGui::SetNextItemWidth(50);
    ImGui::InputInt("max", &g_ArchetypeLimit, 0, 0);
    ImGui::SameLine();
    if (ImGui::Button("Add to hand##archetype"))
    {
        g_Result = AddArchetypeMembers(g_Archetype, g_Hand, (std::max)(1, (std::min)(g_ArchetypeLimit, 20)));
        g_Enabled = true;
        Save();
    }
    if (!g_Result.empty())
        ImGui::TextWrapped("%s", g_Result.c_str());

    // -- what is chosen --
    ImGui::Separator();
    DrawList("Hand", g_Hand);
    ImGui::Spacing();
    DrawList("Extra deck", g_Extra);
    if (ImGui::Button("Clear all"))
    {
        g_Hand.clear();
        g_Extra.clear();
        g_Result.clear();
        Save();
    }

    ImGui::Separator();
    ImGui::TextDisabled("Last duel: player %d, %s, deck %u/%u cards before, %d stacked, first %s",
        g_Last.Player, g_Last.Applied ? "applied" : "not applied", g_Last.MainBefore, g_Last.ExtraBefore, g_Last.Stacked, g_Last.FirstIds.c_str());
}
