#include "Inspector.h"

#include <cctype>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <imgui.h>
#include <iterator>
#include <string>
#include <vector>
#include <windows.h>

#include "GameMemory.h"
#include "YuGiOh/YuGiOh-DUELSTATE.h"
#include "Yu-Gi-Oh-Ex.h"

using GameMemory::Read;

namespace
{
    // ---------------------------------------------------------------- addresses (all named in YuGiOh.exe.i64)

    // The duel state's layout is shared (YuGiOh-DUELSTATE.h); this file reads it guarded, through GameMemory::Read.
    namespace DS = YGO::DUELSTATE;
    constexpr uintptr_t kPlayerState = DS::PlayerState;
    constexpr uintptr_t kPlayerBlock = DS::PlayerBlock;
    constexpr uintptr_t kDuelEngine = DS::DuelEngine;
    constexpr uintptr_t kDuelFront = DS::TagFront;
    constexpr uintptr_t kCardInstances = DS::CardInstances;
    constexpr uintptr_t kPlayerRecords = DS::PlayerRecords;
    constexpr uintptr_t kPlayerRecord = DS::PlayerRecordSize;
    constexpr uintptr_t kMsgQueue = DS::MsgQueue;
    constexpr uintptr_t kIsTagDuel = DS::TagDuelFlag;
    constexpr uintptr_t kDeckTemplates = 0x14275AC50;  // YGO::GAME::DeckTemplateList: 700 x 0x130, filled from decks.zib

    constexpr uintptr_t kLifePoints = DS::LifePointsOffset;
    constexpr uintptr_t kMonsterZones = DS::MonsterZones;
    constexpr uintptr_t kSpellTrapZones = DS::SpellTrapZones;
    constexpr uintptr_t kFieldSpell = DS::FieldSpell;

    using Pile = DS::Pile;
    constexpr Pile kHand = DS::Hand;
    constexpr Pile kDeck = DS::Deck;
    constexpr Pile kGrave = DS::Graveyard;
    constexpr Pile kExtra = DS::ExtraDeck;
    constexpr Pile kBanished = DS::Banished;

    constexpr uintptr_t kGetCardName = 0x14076D0F0;
    constexpr uintptr_t kGetCardDesc = 0x14076D070;

    // The memory viewer's address, so other tabs can send it somewhere.
    uint64_t g_ViewAddress = kPlayerState;
    int g_ViewSize = 256;
    bool g_SelectMemoryTab = false;

    void ShowInViewer(uintptr_t address)
    {
        g_ViewAddress = address;
        g_SelectMemoryTab = true;
    }

    void Hint(const char* text)
    {
        if (ImGui::IsItemHovered())
        {
            ImGui::BeginTooltip();
            ImGui::PushTextWrapPos(ImGui::GetFontSize() * 30.0f);
            ImGui::TextUnformatted(text);
            ImGui::PopTextWrapPos();
            ImGui::EndTooltip();
        }
    }

    // ---------------------------------------------------------------- cards

    // Yu-Gi-Oh-MoreCards lends vanilla ids to custom cards for the length of a duel; this gives the custom card's real id.
    uint16_t RealCardId(uint16_t id)
    {
        using Fn = unsigned short(__cdecl*)(unsigned short);
        static Fn lookup = nullptr;
        static bool looked = false;
        if (!looked)
        {
            if (HMODULE module = GetModuleHandleA("Yu-Gi-Oh-MoreCards.dll"))
                lookup = reinterpret_cast<Fn>(GetProcAddress(module, "Card_GetRealIdForBorrowed"));
            looked = true;
        }
        if (!lookup)
            return id;
        const unsigned short real = lookup(id);
        return real ? real : id;
    }

    const wchar_t* CardName(uint16_t id)
    {
        if (id < 3900 || id > 19999)
            return L"";
        const wchar_t* name = GameMemory::CallCardText(kGetCardName, id);
        return name ? name : L"?";
    }

    // One card as a table row: slot, id (and the real id if it is borrowed), name; the description on hover.
    void CardRow(int slot, uint16_t id, const char* extra = nullptr)
    {
        ImGui::TableNextRow();
        ImGui::TableNextColumn();
        ImGui::Text("%d", slot);
        ImGui::TableNextColumn();
        const uint16_t real = RealCardId(id);
        if (real != id)
            ImGui::Text("%u (real %u)", id, real);
        else
            ImGui::Text("%u", id);
        ImGui::TableNextColumn();
        ImGui::Text("%ls", CardName(id));
        if (ImGui::IsItemHovered() && id >= 3900)
        {
            if (const wchar_t* description = GameMemory::CallCardText(kGetCardDesc, id))
            {
                ImGui::BeginTooltip();
                ImGui::PushTextWrapPos(ImGui::GetFontSize() * 35.0f);
                ImGui::TextWrapped("%ls", description);
                ImGui::PopTextWrapPos();
                ImGui::EndTooltip();
            }
        }
        if (extra)
        {
            ImGui::TableNextColumn();
            ImGui::TextUnformatted(extra);
        }
    }

    // ---------------------------------------------------------------- .ydc export

    // .ydc (decks.zib's format, File Type Libraries\DeckData YdcDeck): 8 header bytes (no reader uses them; written as zero), then main,
    // extra and side as a u16 count + u16 Konami ids. Borrowed ids are written as the custom card's real id.
    std::string g_ExportStatus;

    void ExportYdc(const char* name, const std::vector<uint16_t> (&sections)[3])
    {
        std::string file;
        for (const char* c = name; *c; ++c)
            file += strchr("<>:\"/\\|?*", *c) || static_cast<unsigned char>(*c) < 32 ? '_' : *c;
        if (file.empty())
            file = "deck";

        CreateDirectoryA("Yu-Gi-Oh-Ex", nullptr);
        CreateDirectoryA("Yu-Gi-Oh-Ex\\Exported Decks", nullptr);
        const std::string path = "Yu-Gi-Oh-Ex\\Exported Decks\\" + file + ".ydc";   // UTF-8 (deck names can be Japanese)
        std::wstring widePath(path.size(), L'\0');
        widePath.resize(MultiByteToWideChar(CP_UTF8, 0, path.c_str(), static_cast<int>(path.size()), widePath.data(), static_cast<int>(widePath.size())));

        std::ofstream out(widePath, std::ios::binary | std::ios::trunc);
        const char header[8] = {};
        out.write(header, sizeof(header));
        for (const std::vector<uint16_t>& section : sections)
        {
            const uint16_t count = static_cast<uint16_t>(section.size());
            out.write(reinterpret_cast<const char*>(&count), 2);
            for (uint16_t id : section)
            {
                const uint16_t real = RealCardId(id);
                out.write(reinterpret_cast<const char*>(&real), 2);
            }
        }
        g_ExportStatus = out.good() ? "Saved " + path : "Could not write " + path;
    }

    void ExportStatus()
    {
        if (!g_ExportStatus.empty())
            ImGui::TextDisabled("%s", g_ExportStatus.c_str());
    }

    bool BeginCardTable(const char* id, bool extraColumn = false, const char* extraName = "")
    {
        if (!ImGui::BeginTable(id, extraColumn ? 4 : 3, ImGuiTableFlags_RowBg | ImGuiTableFlags_BordersInnerV | ImGuiTableFlags_SizingFixedFit))
            return false;
        ImGui::TableSetupColumn("#");
        ImGui::TableSetupColumn("Id");
        ImGui::TableSetupColumn("Name", ImGuiTableColumnFlags_WidthStretch);
        if (extraColumn)
            ImGui::TableSetupColumn(extraName);
        ImGui::TableHeadersRow();
        return true;
    }

    // ---------------------------------------------------------------- players

    uintptr_t SideBlock(int side) { return kPlayerState + kPlayerBlock * (side & 1); }

    int LifePoints(int side)
    {
        return static_cast<int>(Read<uint32_t>(SideBlock(side) + kLifePoints) ^ Read<uint16_t>(kDuelEngine));
    }

    const char* PositionName(uint32_t position)
    {
        // Duel::CardPosition: 2/4/8 confirmed; 1 is named FDD in the IDB but not confirmed as face-up attack.
        switch (position)
        {
        case 1: return "1 (FDD?)";
        case 2: return "face-down ATK";
        case 4: return "face-up DEF";
        case 8: return "face-down DEF";
        default: break;
        }
        static char text[16];
        sprintf_s(text, "0x%X", position);
        return text;
    }

    void DrawZones(const char* title, uintptr_t first, int count)
    {
        if (!ImGui::TreeNodeEx(title, ImGuiTreeNodeFlags_DefaultOpen))
            return;
        if (BeginCardTable(title, true, "Position"))
        {
            for (int i = 0; i < count; ++i)
            {
                const uintptr_t zone = first + 24 * i;
                const uint16_t id = static_cast<uint16_t>(Read<uint32_t>(zone) & 0x3FFF);
                if (!id)
                {
                    ImGui::TableNextRow();
                    ImGui::TableNextColumn();
                    ImGui::Text("%d", i);
                    ImGui::TableNextColumn();
                    ImGui::TextDisabled("-");
                    ImGui::TableNextColumn();
                    ImGui::TextDisabled("(empty)");
                    ImGui::TableNextColumn();
                    continue;
                }
                CardRow(i, id, PositionName(Read<uint32_t>(zone + 0xC)));
            }
            ImGui::EndTable();
        }
        ImGui::TreePop();
    }

    // A live pile in a PlayerState block: packed dwords, low 14 bits = card id.
    void DrawPile(uintptr_t block, const Pile& pile)
    {
        int count = static_cast<int>(Read<uint32_t>(block + pile.CountOffset));
        const bool bad = count < 0 || count > pile.Max;
        char title[64];
        sprintf_s(title, "%s: %d###%s", pile.Name, count, pile.Name);
        if (!ImGui::TreeNode(title))
            return;
        if (bad)
            ImGui::TextColored(ImVec4(1, 0.4f, 0.4f, 1), "Count is out of range (max %d): not in a duel?", pile.Max);
        else if (BeginCardTable(pile.Name))
        {
            for (int i = 0; i < count; ++i)
                CardRow(i, static_cast<uint16_t>(Read<uint32_t>(block + pile.ArrayOffset + 4 * i) & 0x3FFF));
            ImGui::EndTable();
        }
        ImGui::TreePop();
    }

    // A tag partner who is not on the field: hand, deck and extra deck parked in the front block as u16 counts then u16 instance
    // indexes (Duel_LoadEngineFromFrontBlock, 0x140082960, swaps it with the engine's piles at the turn change).
    void DrawParkedPartner(int side)
    {
        const uintptr_t block = DS::TagParkedBlock(side);
        const char* names[3] = { "Hand", "Deck", "Extra Deck" };
        int counts[3];
        for (int i = 0; i < 3; ++i)
            counts[i] = Read<uint16_t>(block + 2 * i);

        int entry = 0;
        for (int pile = 0; pile < 3; ++pile)
        {
            char title[64];
            sprintf_s(title, "%s: %d###parked%d", names[pile], counts[pile], pile);
            const bool open = ImGui::TreeNode(title);
            if (open && entry + counts[pile] > DS::TagParkedMax)
                ImGui::TextColored(ImVec4(1, 0.4f, 0.4f, 1), "Counts are out of range: the block is not filled yet?");
            else if (open && BeginCardTable(names[pile]))
            {
                for (int i = 0; i < counts[pile]; ++i)
                {
                    const uint16_t instance = Read<uint16_t>(block + 6 + 2 * (entry + i));
                    CardRow(i, static_cast<uint16_t>(Read<uint32_t>(kCardInstances + 8 * instance) & 0x3FFF));
                }
                ImGui::EndTable();
            }
            if (open)
                ImGui::TreePop();
            entry += counts[pile] > 0 ? counts[pile] : 0;
        }
    }

    // The deck the seat was given for this duel (Duel_PlayerRecords + 0x4820 * seat + 0x40, u16: [33] main count, [34] extra, [35] side,
    // [36..95] main, [96..110] extra, [111..125] side). Filled by YGO__DuelSetup__AssignSeatDecksAndDuelists before Engine_Init.
    void DrawSeatDeck(int seat)
    {
        const uintptr_t words = DS::SeatDeckWords(seat);
        struct Part { const char* Name; int CountIndex; int First; int Max; };
        constexpr Part parts[3] = { { "Main", 33, 36, 60 }, { "Extra", 34, 96, 15 }, { "Side", 35, 111, 15 } };
        for (const Part& part : parts)
        {
            const int count = Read<uint16_t>(words + 2 * part.CountIndex);
            char title[64];
            sprintf_s(title, "%s: %d###seatdeck%s", part.Name, count, part.Name);
            if (!ImGui::TreeNode(title))
                continue;
            if (count > part.Max)
                ImGui::TextColored(ImVec4(1, 0.4f, 0.4f, 1), "Count is out of range (max %d)", part.Max);
            else if (BeginCardTable(part.Name))
            {
                for (int i = 0; i < count; ++i)
                    CardRow(i, Read<uint16_t>(words + 2 * (part.First + i)));
                ImGui::EndTable();
            }
            ImGui::TreePop();
        }
        if (ImGui::SmallButton("View record in memory"))
            ShowInViewer(kPlayerRecords + kPlayerRecord * seat);
        ImGui::SameLine();
        if (ImGui::SmallButton("Export .ydc"))
        {
            std::vector<uint16_t> sections[3];
            for (int p = 0; p < 3; ++p)
            {
                const int count = Read<uint16_t>(words + 2 * parts[p].CountIndex);
                for (int i = 0; i < count && i < parts[p].Max; ++i)
                    sections[p].push_back(Read<uint16_t>(words + 2 * (parts[p].First + i)));
            }
            char name[32];
            sprintf_s(name, "Player %d duel deck", seat + 1);
            ExportYdc(name, sections);
        }
        ExportStatus();
    }

    void DrawPlayers()
    {
        static int seat = 0;
        const char* seats[4] = { "Player 1 (seat 0, side 0)", "Player 2 (seat 1, side 1)", "Player 3 (seat 2, Player 1's tag partner)",
            "Player 4 (seat 3, Player 2's tag partner)" };
        ImGui::SetNextItemWidth(ImGui::GetFontSize() * 20);
        ImGui::Combo("Player", &seat, seats, 4);

        const bool tag = Read<uint8_t>(kIsTagDuel) != 0;
        const int side = seat & 1;
        const bool swapped = Read<uint8_t>(kDuelFront + side) != 0;   // Duel_UNK byte per side, toggled by each swap
        const int onField = tag && swapped ? side + 2 : side;          // the seat whose hand/deck the side's PlayerState block holds now

        ImGui::Text("Tag duel: %s", tag ? "yes" : "no");
        if (tag)
        {
            ImGui::SameLine();
            ImGui::Text("| side %d is played by Player %d now", side, onField + 1);
            Hint("Read from the swap flag at Duel_UNK + side (0 = the seat 0/1 duelist, 1 = the partner). The turn change swaps the partner in "
                 "from turn 2 (Duel_LoadEngineFromFrontBlock), unless Yu-Gi-Oh-TagDuel's DeckMode=shared turned the swap off.");
        }
        ImGui::Separator();

        if (seat >= 2 && !tag)
        {
            ImGui::TextWrapped("Players 3 and 4 only exist in a tag duel. Below is what their seat record holds anyway.");
            if (ImGui::CollapsingHeader("Deck for this duel (seat record)", ImGuiTreeNodeFlags_DefaultOpen))
                DrawSeatDeck(seat);
            return;
        }

        const uintptr_t block = SideBlock(side);
        if (ImGui::CollapsingHeader(tag ? "Team (shared by both partners)" : "Duel state", ImGuiTreeNodeFlags_DefaultOpen))
        {
            ImGui::Text("Life points: %d", LifePoints(side));
            Hint("PlayerState block +0 (u32) XOR Duel_Engine.LpXorKey (u16 at 0x143330280).");
            ImGui::SameLine();
            if (ImGui::SmallButton("View block in memory"))
                ShowInViewer(block);

            DrawZones("Monster zones", block + kMonsterZones, 5);
            DrawZones("Spell & Trap zones", block + kSpellTrapZones, 5);
            DrawZones("Field spell", block + kFieldSpell, 1);
            DrawPile(block, kGrave);
            DrawPile(block, kBanished);
        }

        char header[64];
        sprintf_s(header, "Player %d's hand and decks###piles", seat + 1);
        if (ImGui::CollapsingHeader(header, ImGuiTreeNodeFlags_DefaultOpen))
        {
            if (seat == onField)
            {
                if (tag)
                    ImGui::TextDisabled("On the field: read from the side's PlayerState block.");
                DrawPile(block, kHand);
                DrawPile(block, kDeck);
                DrawPile(block, kExtra);
            }
            else
            {
                ImGui::TextDisabled("Waiting for the turn change: read from the parked block at Duel_UNK + 4 + 302 * side.");
                DrawParkedPartner(side);
            }
        }

        if (ImGui::CollapsingHeader("Deck for this duel (seat record)"))
            DrawSeatDeck(seat);
    }

    // ---------------------------------------------------------------- game decks (decks.zib)

    void DrawGameDecks()
    {
        ImGui::TextWrapped("The game's own decks (starter decks, opponents' decks), loaded once from decks.zib into YGO::GAME::DeckTemplateList "
                           "by LoadGameContentOnce. Read only; your own decks are in the save.");
        if (!Read<uint8_t>(0x1427D0560))
        {
            ImGui::TextDisabled("Game content is not loaded yet.");
            return;
        }

        static char filter[64] = "";
        ImGui::InputText("Filter", filter, sizeof(filter));
        char lowered[64];
        for (int i = 0; i < 64; ++i)
            lowered[i] = static_cast<char>(tolower(static_cast<unsigned char>(filter[i])));

        for (int index = 0; index < 700; ++index)
        {
            const uintptr_t deck = kDeckTemplates + 0x130 * index;
            const int main = Read<int16_t>(deck + 0x42), extra = Read<int16_t>(deck + 0x44), side = Read<int16_t>(deck + 0x46);
            if (main <= 0 && extra <= 0)
                continue;

            wchar_t name[34] = {};
            GameMemory::ReadRaw(deck, name, 66);
            char utf8[128];
            WideCharToMultiByte(CP_UTF8, 0, name, -1, utf8, sizeof(utf8), nullptr, nullptr);
            if (lowered[0])
            {
                char nameLower[128];
                for (int i = 0; i < 128; ++i)
                    nameLower[i] = static_cast<char>(tolower(static_cast<unsigned char>(utf8[i])));
                if (!strstr(nameLower, lowered))
                    continue;
            }

            char title[192];
            sprintf_s(title, "%d: %s (%d / %d / %d)###deck%d", index, utf8, main, extra, side, index);
            if (!ImGui::TreeNode(title))
                continue;
            ImGui::Text("Record slot %d, series %d, valid %d", Read<int>(deck + 0x120), Read<int>(deck + 0x124), Read<uint8_t>(deck + 0x12C));
            struct Part { const char* Name; int Count; uintptr_t Offset; int Max; };
            const Part parts[3] = { { "Main", main, 0x48, 60 }, { "Extra", extra, 0xC0, 15 }, { "Side", side, 0xDE, 15 } };
            if (ImGui::SmallButton("Export .ydc"))
            {
                std::vector<uint16_t> sections[3];
                for (int p = 0; p < 3; ++p)
                    for (int i = 0; i < parts[p].Count && i < parts[p].Max; ++i)
                        sections[p].push_back(Read<uint16_t>(deck + parts[p].Offset + 2 * i));
                ExportYdc(utf8[0] ? utf8 : "deck", sections);
            }
            ExportStatus();
            for (const Part& part : parts)
            {
                if (part.Count <= 0 || part.Count > part.Max)
                    continue;
                ImGui::TextUnformatted(part.Name);
                if (BeginCardTable(part.Name))
                {
                    for (int i = 0; i < part.Count; ++i)
                        CardRow(i, Read<uint16_t>(deck + part.Offset + 2 * i));
                    ImGui::EndTable();
                }
            }
            ImGui::TreePop();
        }
    }

    // ---------------------------------------------------------------- globals

    enum class Kind { Bool, U8, U16, I32, U32, U64, F32, Ptr, Block };

    struct Global
    {
        const char* Name;
        uintptr_t Address;
        Kind Type;
        const char* Note;
        size_t Size = 0;   // Block only
    };

    struct Group { const char* Title; const Global* Items; size_t Count; };

    constexpr Global kDuelMode[] =
    {
        { "g_DuelMode_IsCampaign", 0x140C8D1D8, Kind::Bool, "Story (campaign) duel." },
        { "g_DuelMode_IsBattlePack", 0x140C8D1D0, Kind::Bool, "Battle Pack (sealed / draft) duel." },
        { "g_DuelMode_IsChallenge", 0x140C8D1E5, Kind::Bool, "Duelist Challenge duel." },
        { "g_bIsTutorialDuel", 0x140C8D1EA, Kind::Bool, "Tutorial duel." },
        { "YGO::DUEL::g_iTutorialDuelIndex", 0x140C8D1EC, Kind::I32, "Which tutorial is running." },
        { "YGO::DUEL::g_bIsDuelMultiplayer", 0x140C8D1E6, Kind::Bool, "Two humans (local or online)." },
        { "YGO::DUEL::g_bIsRoundBasedDuel", 0x140C8D35C, Kind::Bool, "Match (best of three) instead of a single duel." },
        { "YGO::DUEL::g_bIsTagDuel", 0x140C8D35D, Kind::Bool, "Tag duel: 4 seats, 2 teams sharing a PlayerState block each." },
        { "YGO::DuelSetup::InitialRules", 0x140C8D360, Kind::I32, "Rules value handed to the engine at setup." },
        { "g_bEngineRules", 0x140C8D1C9, Kind::Bool, "Dead: written, never read by the duel." },
        { "YGO::DUEL::g_bSetPlayerFlagBit29", 0x140C8D1C8, Kind::Bool, "" },
        { "YGO::DUEL::g_bSetDuelStuck", 0x140D4FCFB, Kind::Bool, "Set_DuelStuck freezes the duel." },
        { "g_bIsReadyDuelModule", 0x1429275D1, Kind::Bool, "" },
    };

    constexpr Global kDuelSettings[] =
    {
        { "YGO::DUEL::g_iStartLifePoints", 0x140C8D370, Kind::I32, "Starting LP for the next duel." },
        { "YGO::DUEL::g_iDuelTimeLimt", 0x140C8D368, Kind::I32, "Time limit in milliseconds, 0 = none." },
        { "YGO::DUEL::g_iTimerIncrement", 0x140C8D364, Kind::I32, "" },
        { "g_iStartingPlayer", 0x140C8D384, Kind::I32, "Who goes first (0/1)." },
        { "g_MatchRoundIndex", 0x140C8D374, Kind::I32, "Duel number inside a match." },
        { "g_CurrentArenaId", 0x140C8D1F0, Kind::I32, "Arena (duel field backdrop); folder from g_ArenaFolderTable." },
        { "g_LastDuelWinReason", 0x1433305B4, Kind::I32, "DuelWinReason of the last duel." },
        { "g_DuelBaseMusicSlot", 0x140C8D1B8, Kind::I32, "Music slot picked by Duel_StartMusic." },
    };

    constexpr Global kEngineGlobals[] =
    {
        { "Duel_Engine.LpXorKey", 0x143330280, Kind::U16, "LP in PlayerState is stored XORed with this." },
        { "Duel_Engine.field_4 (local seat parity)", 0x143330284, Kind::I32, "Read all over the engine." },
        { "Duel_Engine.StartingPlayer", 0x1433302A8, Kind::I32, "" },
        { "PlayerState RNG (+0x3768)", kPlayerState + 0x3768, Kind::U32, "The duel's random number generator state (MSVC LCG)." },
        { "PlayerState winner (+0x3792)", kPlayerState + 0x3792, Kind::U8, "1/2 = a side won, 3 = draw." },
        { "g_DuelMsgQueue Count", kMsgQueue + 0x810, Kind::I32, "Engine messages waiting to be played out." },
        { "Tag swap flag, side 0", kDuelFront + 0, Kind::U8, "Toggled by Duel_LoadEngineFromFrontBlock." },
        { "Tag swap flag, side 1", kDuelFront + 1, Kind::U8, "Toggled by Duel_LoadEngineFromFrontBlock." },
        { "YGO::DuelGraphics::g_ActiveAnimation", 0x1427D0C08, Kind::I32, "" },
        { "YGO::UI::g_iCurrentDuelAnimation", 0x1427D0C18, Kind::I32, "" },
        { "YGO::UI::g_iPreviousDuelAnimation", 0x1427D0C1C, Kind::I32, "" },
    };

    constexpr Global kLive[] =
    {
        { "g_bIsLiveSession", 0x140C8D399, Kind::Bool, "An online session is running." },
        { "g_bIsLiveHost", 0x140C8D1E7, Kind::Bool, "This game hosts the lobby." },
        { "g_LiveMatchType", 0x140C8D1E8, Kind::U8, "" },
        { "g_LocalPlayerSeat", 0x140C8D39C, Kind::I32, "This game's seat in the duel." },
        { "g_LiveLocalSeat", 0x140D4FE40, Kind::I32, "This game's seat in the lobby (-1 = none)." },
        { "g_LiveLocalAvatarId", 0x140D4FD00, Kind::I32, "" },
        { "g_bOpponentLeft", 0x140C8D390, Kind::Bool, "" },
        { "g_LocalForfeitReason", 0x140C8D394, Kind::I32, "" },
        { "g_pLiveManager", 0x142924138, Kind::Ptr, "" },
        { "g_LiveManagerPacketSeq", 0x142924130, Kind::U8, "" },
        { "g_LiveSeats", 0x140D4FD80, Kind::Ptr, "" },
        { "g_NetOutgoingQueueSize", 0x1427D0570, Kind::I32, "Bytes waiting to be sent." },
        { "g_NetIncomingQueueSize", 0x1427D0770, Kind::I32, "Bytes received, not handled yet." },
    };

    constexpr Global kSteam[] =
    {
        { "g_ISteamUser_Ctx", 0x140D31DE0, Kind::Ptr, "" },
        { "g_ISteamFriends_Ctx", 0x140D31DF8, Kind::Ptr, "" },
        { "g_ISteamUtils_Ctx", 0x140D31E10, Kind::Ptr, "" },
        { "g_ISteamNetworking_Ctx", 0x140D32840, Kind::Ptr, "P2P channels: 2 = lobby, 3 = duel action sync." },
        { "g_ISteamMatchmaking_Ctx", 0x140C8E648, Kind::Ptr, "" },
        { "g_ISteamUserStats_Ctx", 0x140C8E588, Kind::Ptr, "Stats and leaderboards." },
        { "g_SteamRemoteStorageContext", 0x140C8EA50, Kind::Ptr, "" },
    };

    constexpr Global kWindow[] =
    {
        { "hWnd", 0x140D31E60, Kind::Ptr, "The game window." },
        { "g_bIsWindowActive", 0x140D326F9, Kind::Bool, "" },
        { "g_bIsWindowVisible", 0x140D326FA, Kind::Bool, "" },
        { "g_iDefaultSwapChainWidth", 0x140D2A0D4, Kind::I32, "" },
        { "g_iDefaultSwapChianHeight", 0x140D2A0D8, Kind::I32, "" },
        { "bBorderlessFullscreenSupported", 0x140C8D1CA, Kind::Bool, "" },
        { "bExclusiveFullscreenSupported", 0x140D4FCFC, Kind::Bool, "" },
        { "ppDevice", 0x14332BBB8, Kind::Ptr, "ID3D11Device." },
        { "ppSwapChain", 0x14332D380, Kind::Ptr, "IDXGISwapChain." },
        { "g_bIsQuitReady", 0x14332A391, Kind::Bool, "Set = the game quits." },
        { "g_bOnPageFirst", 0x140C91C48, Kind::Bool, "" },
        { "g_ScreenMap", 0x143327FE8, Kind::Ptr, "RIX screens by id." },
        { "g_ScreenTitle", 0x1433294C8, Kind::Ptr, "" },
        { "g_ScreenSignIn", 0x1433294A8, Kind::Ptr, "" },
    };

    constexpr Global kAudio[] =
    {
        { "g_MusicVolume", 0x143329748, Kind::F32, "" },
        { "g_MusicVolumeLevel", 0x14332974C, Kind::I32, "Options menu step." },
        { "g_SfxVolume", 0x143329750, Kind::F32, "" },
        { "g_SfxVolumeLevel", 0x143329754, Kind::I32, "Options menu step." },
        { "g_CurrentMusicSlot", 0x14332975C, Kind::I32, "Slot PlayMusicSlot last started." },
        { "g_LastUISoundSlot", 0x1433294F0, Kind::I32, "" },
        { "g_bSoundSlotsReady", 0x143329758, Kind::Bool, "" },
    };

    constexpr Global kText[] =
    {
        { "g_iGameLanguageID", 0x14332A344, Kind::I32, "Selects the string bundle and card text files." },
        { "g_bIsJpVersion", 0x14332A348, Kind::Bool, "Japanese logo / build." },
        { "g_FontMissingGlyphCount", 0x140D4FCD4, Kind::I32, "Characters the fonts could not draw (shown as _)." },
        { "g_TextBadMarkupCount", 0x140D4FCF4, Kind::I32, "" },
    };

    constexpr Global kContent[] =
    {
        { "YGO::GAME::g_bIsGameContentLoaded", 0x1427D0560, Kind::Bool, "LoadGameContentOnce has run." },
        { "g_ArchiveCount", 0x1429241A8, Kind::I32, "Mounted .dat/.toc archives." },
        { "g_Archives", 0x1429241A0, Kind::Ptr, "" },
        { "g_pCardDataFiles", 0x140C8D3A0, Kind::Ptr, "" },
        { "g_pDeckDataFile", 0x1428FC070, Kind::Ptr, "deckdata" },
        { "g_CharDataFileData", 0x142913460, Kind::Ptr, "chardata" },
        { "g_pStoryDuelFile", 0x142919D90, Kind::Ptr, "dueldata" },
        { "g_pStoryScriptFile", 0x140D4DEF0, Kind::Ptr, "scriptdata" },
        { "g_SkuDataFileBuffer", 0x142923900, Kind::Ptr, "skudata" },
        { "g_PackDefFileData", 0x1429241B0, Kind::Ptr, "packdefdata" },
        { "g_ArenaDataFileBuffer", 0x1428FC048, Kind::Ptr, "arenadata (loaded, never read)" },
        { "g_HowToPlayFileBuffer", 0x143328070, Kind::Ptr, "" },
        { "g_CardImageTable", 0x140D4E0C8, Kind::Ptr, "" },
        { "g_IconSheet", 0x1428F7B20, Kind::Ptr, "" },
        { "YGO::CARDS::g_ImageJobCounter", 0x140D4E110, Kind::U64, "Card images queued." },
        { "YGO::CARDS::g_ImageJobsProcessed", 0x140D4E190, Kind::U64, "Card images done." },
    };

    constexpr Global kTables[] =
    {
        { "Duel::PlayerState", kPlayerState, Kind::Block, "Both sides' duel state (0xD94 per side) + RNG/winner.", 14640 },
        { "Duel_DuelEngine", kDuelEngine, Kind::Block, "Duel_Engine.", 824 },
        { "Duel_UNK (tag front blocks)", kDuelFront, Kind::Block, "Swap flags + the parked partner per side.", 0x260 },
        { "g_DuelMsgQueue", kMsgQueue, Kind::Block, "Front +0, Entries[256] +0x10, Count +0x810.", 2084 },
        { "Duel_PlayerRecords", kPlayerRecords, Kind::Block, "One 0x4820 record per seat.", 73856 },
        { "Duel_NetEventBuffer", 0x1434979E0, Kind::Block, "", 596 },
        { "g_DuelStatBlock", 0x140D4FF20, Kind::Block, "", 72 },
        { "g_LiveSeatsDuelSnapshot", 0x140D4FE50, Kind::Block, "", 192 },
        { "g_SelectedLiveDeck", 0x140C8D22C, Kind::Block, "", 42 },
        { "YGO::GAME::DeckTemplateList", kDeckTemplates, Kind::Block, "700 decks from decks.zib.", 212800 },
        { "g_KonamiIds", 0x140D50510, Kind::Block, "", 20336 },
        { "YGO::CARDS::g_iInternalIDs", 0x140D55480, Kind::Block, "", 22144 },
        { "YGO::CARDS::FULL_CARD_PROPS", 0x142927600, Kind::Block, "Per Konami id, written by the Cards plugin for custom cards.", 10485760 },
        { "YGO::CARDS::KONAMI_ID_CARD_PROPS", 0x142847E50, Kind::Block, "", 718512 },
        { "YGO::CARDS::INTERNAL_ID_CARD_PROPS", 0x1427D0C30, Kind::Block, "", 487968 },
        { "g_CardDataFiles", 0x14275AB00, Kind::Block, "", 320 },
        { "g_PackRecords", 0x1429241C0, Kind::Block, "Card shop packs.", 13312 },
        { "g_CharacterRecords", 0x142913470, Kind::Block, "", 24960 },
        { "g_StoryDuelRecords", 0x142919DA0, Kind::Block, "", 39776 },
        { "g_SkuRecords", 0x142923910, Kind::Block, "Content-pack ownership.", 1736 },
        { "g_SoundSlots", 0x143329500, Kind::Block, "", 584 },
        { "YGOInstance", 0x14332D428, Kind::Block, "", 1208 },
        { "YGOFront_UNK", 0x14278EE68, Kind::Block, "", 10824 },
    };

    constexpr Group kGroups[] =
    {
        { "Duel mode", kDuelMode, std::size(kDuelMode) },
        { "Duel settings", kDuelSettings, std::size(kDuelSettings) },
        { "Duel engine", kEngineGlobals, std::size(kEngineGlobals) },
        { "Online (live) session", kLive, std::size(kLive) },
        { "Steam", kSteam, std::size(kSteam) },
        { "Window, graphics, screens", kWindow, std::size(kWindow) },
        { "Audio", kAudio, std::size(kAudio) },
        { "Language and text", kText, std::size(kText) },
        { "Content files", kContent, std::size(kContent) },
        { "Tables and blocks", kTables, std::size(kTables) },
    };

    void ValueText(const Global& global)
    {
        const uintptr_t a = global.Address;
        switch (global.Type)
        {
        case Kind::Bool: ImGui::TextUnformatted(Read<uint8_t>(a) ? "true" : "false"); break;
        case Kind::U8: ImGui::Text("%u", Read<uint8_t>(a)); break;
        case Kind::U16: ImGui::Text("%u (0x%X)", Read<uint16_t>(a), Read<uint16_t>(a)); break;
        case Kind::I32: ImGui::Text("%d", Read<int32_t>(a)); break;
        case Kind::U32: ImGui::Text("%u (0x%X)", Read<uint32_t>(a), Read<uint32_t>(a)); break;
        case Kind::U64: ImGui::Text("%llu", Read<uint64_t>(a)); break;
        case Kind::F32: ImGui::Text("%.3f", Read<float>(a)); break;
        case Kind::Ptr: ImGui::Text("0x%llX", Read<uint64_t>(a)); break;
        case Kind::Block: ImGui::Text("%zu bytes", global.Size); break;
        }
    }

    void DrawGlobals()
    {
        static char filter[64] = "";
        ImGui::InputText("Filter", filter, sizeof(filter));
        ImGui::SameLine();
        ImGui::TextDisabled("(?)");
        Hint("Named globals from YuGiOh.exe.i64, read live every frame. Click an address to open it in the Memory tab. Read only: change things "
             "with Yu-Gi-Oh-Funky.");

        for (const Group& group : kGroups)
        {
            if (!ImGui::CollapsingHeader(group.Title, ImGuiTreeNodeFlags_DefaultOpen))
                continue;
            if (!ImGui::BeginTable(group.Title, 3, ImGuiTableFlags_RowBg | ImGuiTableFlags_BordersInnerV | ImGuiTableFlags_Resizable))
                continue;
            ImGui::TableSetupColumn("Name", ImGuiTableColumnFlags_WidthStretch, 2.0f);
            ImGui::TableSetupColumn("Address", ImGuiTableColumnFlags_WidthFixed, ImGui::GetFontSize() * 7);
            ImGui::TableSetupColumn("Value", ImGuiTableColumnFlags_WidthStretch, 1.0f);
            for (size_t i = 0; i < group.Count; ++i)
            {
                const Global& global = group.Items[i];
                if (filter[0] && !strstr(global.Name, filter))
                    continue;
                ImGui::TableNextRow();
                ImGui::TableNextColumn();
                ImGui::TextUnformatted(global.Name);
                if (global.Note[0])
                    Hint(global.Note);
                ImGui::TableNextColumn();
                char address[24];
                sprintf_s(address, "%llX", static_cast<unsigned long long>(global.Address));
                ImGui::PushID(global.Name);
                if (ImGui::Selectable(address))
                    ShowInViewer(global.Address);
                ImGui::PopID();
                ImGui::TableNextColumn();
                ValueText(global);
            }
            ImGui::EndTable();
        }
    }

    // ---------------------------------------------------------------- memory viewer

    void DrawMemory()
    {
        ImGui::SetNextItemWidth(ImGui::GetFontSize() * 10);
        ImGui::InputScalar("Address", ImGuiDataType_U64, &g_ViewAddress, nullptr, nullptr, "%llX", ImGuiInputTextFlags_CharsHexadecimal);
        ImGui::SameLine();
        ImGui::SetNextItemWidth(ImGui::GetFontSize() * 8);
        ImGui::InputInt("Bytes", &g_ViewSize, 16, 256);
        g_ViewSize = g_ViewSize < 16 ? 16 : (g_ViewSize > 4096 ? 4096 : g_ViewSize);
        ImGui::SameLine();
        if (ImGui::Button("Follow pointer"))
            g_ViewAddress = Read<uint64_t>(static_cast<uintptr_t>(g_ViewAddress));
        Hint("Reads the 8 bytes at the address and goes there.");

        const uintptr_t at = static_cast<uintptr_t>(g_ViewAddress);
        ImGui::Text("u8 %u  u16 %u  i32 %d  u32 0x%X  f32 %.4f  u64 0x%llX", Read<uint8_t>(at), Read<uint16_t>(at), Read<int32_t>(at),
            Read<uint32_t>(at), Read<float>(at), Read<uint64_t>(at));
        ImGui::Separator();

        ImGui::BeginChild("hex", ImVec2(0, 0), false, ImGuiWindowFlags_HorizontalScrollbar);
        for (int row = 0; row < g_ViewSize; row += 16)
        {
            uint8_t bytes[16];
            const bool ok = GameMemory::ReadRaw(at + row, bytes, sizeof(bytes));
            char line[128];
            int length = sprintf_s(line, "%016llX  ", static_cast<unsigned long long>(at + row));
            for (int i = 0; i < 16; ++i)
                length += ok ? sprintf_s(line + length, sizeof(line) - length, "%02X ", bytes[i]) : sprintf_s(line + length, sizeof(line) - length, "?? ");
            length += sprintf_s(line + length, sizeof(line) - length, " ");
            for (int i = 0; i < 16; ++i)
                line[length++] = ok && isprint(bytes[i]) ? static_cast<char>(bytes[i]) : '.';
            line[length] = 0;
            ImGui::TextUnformatted(line);
        }
        ImGui::EndChild();
    }
}

namespace Inspector
{
    void Draw(bool* open)
    {
        ImGui::SetNextWindowSize(ImVec2(560, 640), ImGuiCond_FirstUseEver);
        if (!ImGui::Begin("Yu-Gi-Oh!", open))
        {
            ImGui::End();
            return;
        }

        ImGui::Text("Yu-Gi-Oh-Ex: WolfX");
        ImGui::SameLine();
        if (ImGui::SmallButton("Quit Game"))
            YuGiOhEx::g_bIsQuitReady = true;

        if (ImGui::BeginTabBar("inspector"))
        {
            if (ImGui::BeginTabItem("Players")) { DrawPlayers(); ImGui::EndTabItem(); }
            if (ImGui::BeginTabItem("Game decks")) { DrawGameDecks(); ImGui::EndTabItem(); }
            if (ImGui::BeginTabItem("Globals")) { DrawGlobals(); ImGui::EndTabItem(); }
            const bool select = g_SelectMemoryTab;
            g_SelectMemoryTab = false;
            if (ImGui::BeginTabItem("Memory", nullptr, select ? ImGuiTabItemFlags_SetSelected : 0)) { DrawMemory(); ImGui::EndTabItem(); }
            ImGui::EndTabBar();
        }
        ImGui::End();
    }
}
