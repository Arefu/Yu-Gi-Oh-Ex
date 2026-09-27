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

    // Ready made setups: what to put in the hand and the extra deck to try a card. Ids are Konami ids (the custom ones are from cards.json).
    struct Preset { const char* Name; std::vector<int> Hand, Extra; };
    const std::vector<Preset> kPresets =
    {
        { "Fusion: Elemental HERO Flame Wingman (Avian + Burstinatrix)", { 4837, 6310, 6311 }, { 6344 } },
        { "Fusion: Thousand Dragon (Baby Dragon + Time Wizard)", { 4837, 4010, 4022 }, { 4075 } },
        { "Fusion: Dark Paladin (Dark Magician + Buster Blader)", { 4837, 4041, 4983 }, { 5628 } },
        { "Fusion: Gate Guardian (Sanga + Kazejin + Suijin)", { 4837, 4377, 4378, 4379 }, { 4380 } },
        { "Fusion: Blue-Eyes Ultimate Dragon (3 Blue-Eyes White Dragon)", { 4837, 4007, 4007, 4007 }, { 4386 } },
        // The "NEW fusion" presets that used to live here (ids 15384/15388/15420-15423) referenced the old
        // 245-card cards.json, which had "fusion" recipe arrays for its custom cards. That file was replaced
        // by the 4166-card ygoprodeck delta (ids 15542-19707), which carried no fusion recipes at all - those
        // ids and recipes no longer exist. Regenerated from the new delta's own description text (see
        // gen_fusion_recipes.py in this session's scratchpad and fusion_recipes_report.txt on the Desktop):
        // only 11 of 225 Fusion-kind cards have a fully specific material list the game's fixed-id-list recipe
        // system can represent - the rest need a real card-effect/condition engine (generic materials like
        // "1 Dragon monster" or "2 Warrior monsters with different Attributes"), which does not exist yet.
        { "NEW fusion: Elemental HERO Neos Kluger (Neos + Yubel)", { 4837, 6653, 7409 }, { 15646 } },
        { "NEW fusion: Red-Eyes Dark Dragoon (Dark Magician + Red-Eyes B. Dragon)", { 4837, 4041, 4088 }, { 15652 } },
        { "NEW fusion: Armityle the Chaos Phantasm (Uria + Hamon + Raviel)", { 4837, 6563, 6564, 6565 }, { 16020 } },
        { "NEW fusion: Cyberdark End Dragon (Cyberdark Dragon + Cyber End Dragon)", { 4837, 6833, 6397 }, { 16554 } },
        { "NEW fusion: Gate Guardian of Thunder and Wind (Sanga + Kazejin)", { 4837, 4377, 4378 }, { 17677 } },
        { "NEW fusion: Gate Guardian of Water and Thunder (Suijin + Sanga)", { 4837, 4379, 4377 }, { 17678 } },
        { "NEW fusion: Gate Guardian of Wind and Water (Kazejin + Suijin)", { 4837, 4378, 4379 }, { 17679 } },
        { "NEW fusion: Gate Guardians Combined (Sanga + Kazejin + Suijin)", { 4837, 4377, 4378, 4379 }, { 17680 } },
        { "NEW fusion: Ultimate Flame Swordsman (Flame Swordsman + Fighting Flame Dragon)", { 4837, 4021, 18156 }, { 18165 } },
        { "NEW fusion: Enlightenment Dragon (Judgment Dragon + Punishment Dragon)", { 4837, 7599, 13067 }, { 18181 } },
        { "NEW fusion: XYZ-Hyper Dragon Cannon (X-Cross Cannon + Y-Yare Head + Z-Zillion Tank)", { 4837, 18381, 18383, 18384 }, { 18382 } },
    };

    char g_Search[64]{};
    int g_TypedId = 15300;

    // ---- new cards browser: cards.json's new monsters, for trying one on a player (including the AI) ----
    // Covers every monster kind except plain Effect/Spell/Trap-less-condition ones, since those need real
    // per-card effect code that does not exist yet (see the "conditional Special Summon" CSV audit). What IS
    // included here mirrors it: Normal (always fine - no effect at all) plus every extra-deck kind (Fusion,
    // Synchro, Xyz, Link, Ritual, and their *Effect/Pendulum variants) - those are believed to be summoned
    // generically off Kind + Level/Rank/LinkRating/LinkArrows, the same fields Yu-Gi-Oh-Cards already writes
    // for every custom card, with no per-card script needed for the summon itself (only for what the card DOES
    // afterwards, which is separately unimplemented). Putting one of these in the extra deck list below and
    // giving the AI qualifying material monsters (matching Level for Synchro, matching Rank for Xyz, etc, via
    // the search box above) is the actual test of that theory - this browser existing does not by itself prove
    // the summon works, only that Cards will not reject the card outright.
    struct NewCard { int Id; std::string Name; std::string Kind; bool IsExtraDeck; int Level; };
    std::vector<NewCard> g_NewCards;
    bool g_NewCardsLoaded = false;
    std::string g_NewCardsError;

    bool IsExtraDeckKindName(const std::string& kind)
    {
        static const char* kExtraDeckKinds[] = {
            "Fusion", "Fusion Effect", "Fusion Tuner", "Fusion Pendulum Effect",
            "Synchro", "Synchro Effect", "Synchro Tuner Effect", "Synchro Pendulum Effect",
            "Xyz", "Xyz Effect", "Xyz Pendulum Effect",
            "Link", "Link Effect",
            "Ritual", "Ritual Effect", "Ritual Spirit Effect",
        };
        for (const char* k : kExtraDeckKinds)
            if (kind == k)
                return true;
        return false;
    }

    bool IsXyzKindName(const std::string& kind)
    {
        return kind == "Xyz" || kind == "Xyz Effect" || kind == "Xyz Pendulum Effect";
    }

    // Two ordinary vanilla monsters per Level 1-12 (Normal where one exists, otherwise a plain Effect
    // monster; nothing with its own summoning restriction like the Egyptian Gods), used as ready-made Xyz
    // material: an Xyz monster of Rank N needs monsters whose Level equals N, not N-1 like Synchro/Fusion
    // count math might suggest. Picked from the vanilla card list (Desktop\New folder\Cards\Game Cards.json,
    // cross-checked against real levels), not guessed. Index 0 is unused (Rank 0 does not exist).
    constexpr int kXyzMaterialByLevel[13][2] =
    {
        { 0, 0 },
        { 4015, 4023 },  // Level 1: Shadow Specter, Right Leg of the Forbidden One
        { 4014, 4056 },  // Level 2: Mushroom Man, Basic Insect
        { 4010, 4011 },  // Level 3: Baby Dragon, Ryu-Kishin
        { 4008, 4009 },  // Level 4: Mystical Elf, Hitotsu-Me Giant
        { 4020, 4045 },  // Level 5: Battle Steer, Curse of Dragon
        { 4017, 4028 },  // Level 6: Sword Arm of Dragon, Summoned Skull
        { 4041, 4044 },  // Level 7: Dark Magician, Gaia The Fierce Knight
        { 4007, 4709 },  // Level 8: Blue-Eyes White Dragon, Sengenjin
        { 5045, 6368 },  // Level 9: Moisture Creature, Infernal Flame Emperor
        { 5666, 6087 },  // Level 10: Ultimate Obedient Fiend, Andro Sphinx
        { 12556, 12723 }, // Level 11: Flower Cardian Willow, Darklord Morningstar
        { 12557, 12557 }, // Level 12: Flower Cardian Paulownia (only one known, used twice)
    };

    std::string CardsJsonPath()
    {
        char exe[MAX_PATH]{};
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        std::string folder = exe;
        folder.resize(folder.find_last_of("\\/") + 1);
        return folder + "Yu-Gi-Oh-Ex\\cards.json";
    }

    void LoadNewNormalCards()
    {
        g_NewCards.clear();
        g_NewCardsError.clear();

        const std::string path = CardsJsonPath();
        std::ifstream file(path);
        if (!file)
        {
            g_NewCardsError = "Could not open " + path;
            return;
        }

        nlohmann::json root = nlohmann::json::parse(file, nullptr, false, true);
        if (root.is_discarded())
        {
            g_NewCardsError = path + " is not valid JSON";
            return;
        }

        const nlohmann::json& list = root.is_array() ? root : root["cards"];
        if (!list.is_array())
            return;

        for (const auto& entry : list)
        {
            if (!entry.is_object() || !entry.contains("id") || !entry["id"].is_number_integer())
                continue;
            std::string kind = entry.value("kind", std::string());
            const bool extraDeck = IsExtraDeckKindName(kind);
            // A Ritual monster needs a Ritual Spell to be Ritual Summoned with - a Spell-kind card with
            // icon "Ritual" is one. It goes to hand like any other spell (IsExtraDeck stays false), shown
            // as its own kind so it is easy to tell apart from the monster it might pair with.
            const bool isRitualSpell = kind == "Spell" && entry.value("icon", std::string()) == "Ritual";
            if (kind != "Normal" && !extraDeck && !isRitualSpell)
                continue;
            g_NewCards.push_back({ entry["id"].get<int>(), entry.value("name", std::string("Unnamed Card")),
                isRitualSpell ? "Ritual Spell" : kind, extraDeck, entry.value("level", 0) });
        }
        std::sort(g_NewCards.begin(), g_NewCards.end(), [](const NewCard& a, const NewCard& b) { return a.Name < b.Name; });
    }

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
    if (ImGui::Checkbox("Stack the opening hand for the next duel", &g_Enabled))
        Save();
    if (ImGui::Checkbox("Add these cards to the deck when it does not have them", &g_AddMissing))
        Save();
    ImGui::SetNextItemWidth(120);
    if (ImGui::Combo("Player", &g_Player, "You\0Opponent\0"))
        Save();
    ImGui::TextWrapped("A card above id 16383 borrows a real vanilla card's id for the duel (Yu-Gi-Oh-Cards handles this); it still plays correctly, it just is not guaranteed to end up in the front of the draw if Yu-Gi-Oh-Cards is not loaded. The listed cards are put at both ends of the shuffled deck so they are drawn first (a hand holds 5). Cards for the extra deck are added to it (15 at most). Applies to the next duel you start.");

    ImGui::TextDisabled("Last duel setup: %d call(s), player %d, %s, deck %u/%u cards before, %d stacked, first cards %s",
        g_Last.Calls, g_Last.Player, g_Last.Applied ? "applied" : "not applied", g_Last.MainBefore, g_Last.ExtraBefore, g_Last.Stacked, g_Last.FirstIds.c_str());

    ImGui::Separator();
    if (ImGui::BeginCombo("Preset", "Load a preset..."))
    {
        for (const Preset& preset : kPresets)
        {
            if (ImGui::Selectable(preset.Name))
            {
                g_Hand = preset.Hand;
                g_Extra = preset.Extra;
                g_Enabled = true;
                Save();
            }
        }
        ImGui::EndCombo();
    }
    DrawList("Hand", g_Hand);
    ImGui::Spacing();
    DrawList("Extra deck", g_Extra);
    if (ImGui::Button("Clear both"))
    {
        g_Hand.clear();
        g_Extra.clear();
        Save();
    }

    ImGui::Separator();
    ImGui::SetNextItemWidth(200);
    ImGui::InputText("Search by name", g_Search, sizeof(g_Search));
    ImGui::SetNextItemWidth(120);
    ImGui::InputInt("or Konami id", &g_TypedId, 0, 0);

    auto addButtons = [](int id)
    {
        ImGui::PushID(id);
        if (ImGui::SmallButton("+ hand"))
        {
            g_Hand.push_back(id);
            Save();
        }
        ImGui::SameLine();
        if (ImGui::SmallButton("+ extra"))
        {
            g_Extra.push_back(id);
            Save();
        }
        ImGui::SameLine();
        ImGui::Text("%d  %s", id, ToUtf8(CardName(id)).c_str());
        ImGui::PopID();
    };

    if (g_TypedId >= 1 && g_TypedId <= kMaxCardId && *CardName(g_TypedId))
        addButtons(g_TypedId);

    // The search runs when the text changes, not every frame.
    static std::string lastSearch;
    static std::vector<int> found;
    if (lastSearch != g_Search)
    {
        lastSearch = g_Search;
        found.clear();
        const std::wstring query = ToWide(g_Search);
        if (query.size() >= 2)
        {
            auto lower = [](std::wstring s) { std::transform(s.begin(), s.end(), s.begin(), [](wchar_t c) { return static_cast<wchar_t>(towlower(c)); }); return s; };
            const std::wstring needle = lower(query);
            for (int id = 1; id <= kMaxCardId && found.size() < 31; ++id)
            {
                const wchar_t* name = CardName(id);
                if (*name && lower(name).find(needle) != std::wstring::npos)
                    found.push_back(id);
            }
        }
    }
    for (size_t i = 0; i < found.size() && i < 30; ++i)
        addButtons(found[i]);
    if (found.size() > 30)
        ImGui::TextDisabled("More than 30 cards match: type more of the name.");

    ImGui::Separator();
    ImGui::TextUnformatted("New cards - give the AI one to see if it plays it correctly");
    ImGui::TextWrapped("Normal monsters have no effect at all, so they are guaranteed to behave like any other card. "
        "The extra-deck kinds (Fusion/Synchro/Xyz/Link/Ritual, plain or *Effect/Pendulum) are believed to be summoned "
        "generically off Kind + Level/Rank/LinkRating/LinkArrows - the same fields Cards already writes for every "
        "custom card - with no per-card script needed for the summon itself, only for what the card does afterwards "
        "(not implemented yet, so it will just sit there doing nothing once summoned). Use +extra for those, +hand "
        "for Normal. The AI still needs qualifying material monsters (matching Level for Synchro, matching Rank for "
        "Xyz, etc) in hand/field via the search box above to actually attempt the summon - add one here to see "
        "whether Cards even lets the AI consider it, then check whether the summon itself goes through. A new "
        "Ritual monster also needs a Ritual Spell in hand: new custom ones are listed here too (kind shown as "
        "'Ritual Spell'), or add a vanilla generic one (Advanced Ritual Art, Preparation of Rites, Ritual "
        "Sanctuary...) via the search box - a generic spell should accept any Ritual monster of the right Level, "
        "a specific one (Black Luster Ritual etc) only its own named monster.");
    if (ImGui::Button("Reload from cards.json") || !g_NewCardsLoaded)
    {
        LoadNewNormalCards();
        g_NewCardsLoaded = true;
    }
    if (!g_NewCardsError.empty())
        ImGui::TextColored(ImVec4(1.0f, 0.4f, 0.3f, 1.0f), "%s", g_NewCardsError.c_str());
    ImGui::Text("%d new monster(s) in cards.json (Normal + extra-deck kinds)", static_cast<int>(g_NewCards.size()));

    static char newCardFilter[64]{};
    ImGui::SetNextItemWidth(200);
    ImGui::InputText("Filter by name or kind", newCardFilter, sizeof(newCardFilter));
    ImGui::BeginChild("NewNormalCards", ImVec2(0, 220), true);
    for (const NewCard& card : g_NewCards)
    {
        if (newCardFilter[0])
        {
            std::string name = card.Name, kind = card.Kind, filter = newCardFilter;
            auto toLower = [](std::string& s) { std::transform(s.begin(), s.end(), s.begin(), [](char c) { return static_cast<char>(tolower(static_cast<unsigned char>(c))); }); };
            toLower(name);
            toLower(kind);
            toLower(filter);
            if (name.find(filter) == std::string::npos && kind.find(filter) == std::string::npos)
                continue;
        }
        ImGui::PushID(card.Id);
        if (ImGui::SmallButton("+ hand"))
        {
            g_Hand.push_back(card.Id);
            Save();
        }
        ImGui::SameLine();
        if (ImGui::SmallButton("+ extra"))
        {
            g_Extra.push_back(card.Id);
            Save();
        }
        // Xyz materials are just "N monsters of the right Level", not a specific list like Fusion - so unlike
        // Fusion/Synchro/Link/Ritual there is a fixed, always-correct pair of vanilla monsters to hand over
        // for any Rank (kXyzMaterialByLevel). One button adds the Xyz monster and both materials in one go.
        if (IsXyzKindName(card.Kind))
        {
            ImGui::SameLine();
            if (card.Level >= 1 && card.Level <= 12)
            {
                if (ImGui::SmallButton("+ extra + materials"))
                {
                    g_Extra.push_back(card.Id);
                    g_Hand.push_back(kXyzMaterialByLevel[card.Level][0]);
                    g_Hand.push_back(kXyzMaterialByLevel[card.Level][1]);
                    Save();
                }
            }
            else
            {
                ImGui::TextDisabled("(no known Rank %d materials)", card.Level);
            }
        }
        ImGui::SameLine();
        if (card.IsExtraDeck)
            ImGui::TextColored(ImVec4(0.6f, 0.8f, 1.0f, 1.0f), "%d  %s  [%s]", card.Id, card.Name.c_str(), card.Kind.c_str());
        else
            ImGui::Text("%d  %s  [%s]", card.Id, card.Name.c_str(), card.Kind.c_str());
        ImGui::PopID();
    }
    ImGui::EndChild();
}
