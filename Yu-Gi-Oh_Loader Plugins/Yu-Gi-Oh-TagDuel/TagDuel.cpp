#include <Windows.h>
#include <detours.h>

#include <algorithm>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <format>
#include <fstream>
#include <string>
#include <vector>

#include "TagDuel.h"
#include "Logger.h"

// How the engine runs a tag duel (YuGiOh.exe.i64, docs/MultiplayerSystem.md "Tag duel"):
//
// - Each team's active duelist lives in the normal Duel::PlayerState block for its side (seat & 1). The partner's hand, deck
//   and extra deck are parked in a per-side "front block" (Duel_UNK + 4 + 302 * side: u16 hand, deck, extra counts, then the
//   card slots).
// - Engine_Init calls DuelSetup_LoadBothDecks for a tag duel, which fills those blocks from the seat decks of the partners:
//   seat (start + 2) for the starting side, seat (3 - start) for the other (Duel_DuelEngine + 0x2A + 192 * seat, copied from
//   each seat's player record by Engine_CopySeatDeckFromRecord).
// - From turn 2 on, Duel__Msg__Handle_04_NextTurn calls Duel_LoadEngineFromFrontBlock(side): the parked partner comes in and
//   the one who just played is parked.
// - Seat decks are handed out by YGO__DuelSetup__AssignSeatDecksAndDuelists, for Get_NumberOfPlayers() seats - which is 2
//   until tag mode is on. This plugin turns tag mode on inside Engine_Init, after that, so seats 2 and 3 never got a deck:
//   the partner blocks were empty and the first swap emptied the hand and deck (the "cards vanish at turn 2" bug).
//
// So before Engine_Init runs, this fills seats 2 and 3 in the player records (separate decks, the default), or stops the swap
// (shared: partners play one hand and deck).
namespace
{
    std::atomic<bool> g_Enabled = false;

    // Config ([Yu-Gi-Oh-TagDuel] in Config.ini), read at every duel so changes apply without a restart.
    enum class DeckMode { Separate, Shared };

    std::string GameFolder()
    {
        char exe[MAX_PATH]{};
        GetModuleFileNameA(nullptr, exe, MAX_PATH);
        std::string folder = exe;
        return folder.substr(0, folder.find_last_of("\\/") + 1);
    }

    std::string ReadSetting(const char* key, const char* fallback)
    {
        char value[MAX_PATH]{};
        GetPrivateProfileStringA("Yu-Gi-Oh-TagDuel", key, fallback, value, sizeof(value), (GameFolder() + "Config.ini").c_str());
        return value;
    }

    DeckMode ReadDeckMode()
    {
        std::string mode = ReadSetting("DeckMode", "separate");
        std::transform(mode.begin(), mode.end(), mode.begin(), [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        return mode == "shared" ? DeckMode::Shared : DeckMode::Separate;
    }

    // YGO::DUEL::Set_IsTagDuel (0x140769AC0)
    using SetIsTagDuel_t = void(__fastcall*)(bool);
    SetIsTagDuel_t Call_SetIsTagDuel = reinterpret_cast<SetIsTagDuel_t>(0x140769AC0);

    // YGO__DUEL__Get_LocalPlayerSeat (0x140768F80)
    using GetLocalSeat_t = int(__fastcall*)();
    GetLocalSeat_t Call_GetLocalSeat = reinterpret_cast<GetLocalSeat_t>(0x140768F80);

    // Duel_PlayerRecords (0x142793578): one 0x4820-byte record per seat. Its deck (what Engine_CopySeatDeckFromRecord reads) is
    // u16s at +0x40: [33] main count, [34] extra count, [35] side count, [36..95] main ids, [96..110] extra, [111..125] side.
    constexpr uintptr_t kPlayerRecords = 0x142793578;
    constexpr uintptr_t kRecordStride = 0x4820;
    constexpr uintptr_t kRecordDeck = 0x40;
    constexpr int kMainCount = 33, kExtraCount = 34, kSideCount = 35, kMainIds = 36, kExtraIds = 96, kSideIds = 111, kDeckWords = 126;
    constexpr int kMaxMain = 60, kMaxExtra = 15, kMaxSide = 15;

    uint16_t* SeatDeck(int seat)
    {
        return reinterpret_cast<uint16_t*>(kPlayerRecords + kRecordStride * static_cast<uintptr_t>(seat) + kRecordDeck);
    }

    // .ydc: 8-byte header, then three sections (main, extra, side), each u16 count + u16 Konami ids (File Type Libraries\DeckData).
    bool LoadYdc(const std::string& path, std::vector<uint16_t> (&sections)[3])
    {
        std::ifstream file(path, std::ios::binary);
        if (!file)
            return false;
        std::vector<char> data((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        size_t at = 8;
        for (auto& section : sections)
        {
            if (at + 2 > data.size())
                return false;
            uint16_t count;
            std::memcpy(&count, &data[at], 2);
            at += 2;
            if (at + 2 * static_cast<size_t>(count) > data.size())
                return false;
            section.resize(count);
            std::memcpy(section.data(), &data[at], 2 * static_cast<size_t>(count));
            at += 2 * static_cast<size_t>(count);
        }
        return true;
    }

    void WriteDeck(uint16_t* deck, const std::vector<uint16_t> (&sections)[3])
    {
        const int counts[3] = { std::min<int>(static_cast<int>(sections[0].size()), kMaxMain), std::min<int>(static_cast<int>(sections[1].size()), kMaxExtra),
                                std::min<int>(static_cast<int>(sections[2].size()), kMaxSide) };
        std::memset(deck + kMainCount, 0, sizeof(uint16_t) * (kDeckWords - kMainCount));
        deck[kMainCount] = static_cast<uint16_t>(counts[0]);
        deck[kExtraCount] = static_cast<uint16_t>(counts[1]);
        deck[kSideCount] = static_cast<uint16_t>(counts[2]);
        std::memcpy(deck + kMainIds, sections[0].data(), sizeof(uint16_t) * counts[0]);
        std::memcpy(deck + kExtraIds, sections[1].data(), sizeof(uint16_t) * counts[1]);
        std::memcpy(deck + kSideIds, sections[2].data(), sizeof(uint16_t) * counts[2]);
    }

    // Gives the partner seat a deck: the .ydc named by the setting, else a copy of the teammate's deck (their own copy - the two
    // hands and decks are still separate).
    void FillPartnerDeck(int partnerSeat, int teammateSeat, const char* settingKey, const char* who)
    {
        uint16_t* deck = SeatDeck(partnerSeat);
        std::string path = ReadSetting(settingKey, "");
        if (!path.empty())
        {
            if (path.size() < 2 || path[1] != ':')
                path = GameFolder() + path;
            std::vector<uint16_t> sections[3];
            if (LoadYdc(path, sections) && !sections[0].empty())
            {
                WriteDeck(deck, sections);
                Logger::WriteLog(std::format("TagDuel: {} (seat {}) plays {} ({} main, {} extra)", who, partnerSeat, path, deck[kMainCount], deck[kExtraCount]), MODULE_NAME, 0);
                return;
            }
            Logger::WriteLog(std::format("TagDuel: couldn't read {} for {} ({}) - using a copy of seat {}'s deck", path, who, settingKey, teammateSeat), MODULE_NAME, 1);
        }
        const uint16_t* teammate = SeatDeck(teammateSeat);
        std::memcpy(deck + kMainCount, teammate + kMainCount, sizeof(uint16_t) * (kDeckWords - kMainCount));
        Logger::WriteLog(std::format("TagDuel: {} (seat {}) plays a copy of seat {}'s deck ({} main, {} extra)", who, partnerSeat, teammateSeat, deck[kMainCount], deck[kExtraCount]), MODULE_NAME, 0);
    }

    // YGO::DUEL::Engine_Init (0x1407BC4C0) - takes no arguments, returns a status byte.
    using EngineInit_t = char(__fastcall*)();
    EngineInit_t orig_EngineInit = reinterpret_cast<EngineInit_t>(0x1407BC4C0);

    // YGO::DUEL::Duel_LoadEngineFromFrontBlock (0x140082960): the partner swap. Both callers (the NextTurn handler) reload their
    // registers after the call, so a plain detour is safe.
    using LoadEngineFromFrontBlock_t = __int64(__fastcall*)(int);
    LoadEngineFromFrontBlock_t orig_LoadEngineFromFrontBlock = reinterpret_cast<LoadEngineFromFrontBlock_t>(0x140082960);

    std::atomic<bool> g_SharedThisDuel = false;
    std::atomic<bool> g_AllowInMultiplayer = false;
    bool g_HasSeatControllers = false;
    int g_SeatControllers[4] = { TagDuel::Human, TagDuel::AI, TagDuel::AI, TagDuel::AI };

    // YGO::DUEL::Get_IsDuelMultiplayer (0x1407691D0)
    using GetIsMultiplayer_t = bool(__fastcall*)();
    GetIsMultiplayer_t Call_GetIsDuelMultiplayer = reinterpret_cast<GetIsMultiplayer_t>(0x1407691D0);

    // The engine's own per-seat controller table (a leftover debug mode, YuGiOh.exe.i64): when g_bUseSeatControllerTable is set,
    // Engine_Init gives seat i the controller g_SeatControllerTable[i] instead of "local seat human, the rest AI / network". Set only
    // around our Engine_Init call and put back after, so nothing else in the game sees it.
    auto* const g_bUseSeatControllerTable = reinterpret_cast<uint8_t*>(0x140C8D1E9);
    auto* const g_SeatControllerTable = reinterpret_cast<int*>(0x140C8D218);

    char __fastcall Hook_EngineInit()
    {
        g_SharedThisDuel = false;
        if (g_Enabled && Call_GetIsDuelMultiplayer() && !g_AllowInMultiplayer)
        {
            Logger::WriteLog("TagDuel: online duel - left as the game set it up (no multiplayer plugin handles tag duels yet)", MODULE_NAME, 1);
            return orig_EngineInit();
        }
        if (g_Enabled)
        {
            Call_SetIsTagDuel(true);
            const DeckMode mode = ReadDeckMode();
            if (mode == DeckMode::Shared)
            {
                g_SharedThisDuel = true;
                Logger::WriteLog("TagDuel: tag duel, shared decks (partners play one hand and deck; no swap)", MODULE_NAME, 0);
            }
            else
            {
                // Seats pair by parity: 0 & 2 are one team, 1 & 3 the other.
                const int local = Call_GetLocalSeat() & 1;
                const int opponent = 1 - local;
                FillPartnerDeck(local + 2, local, "PartnerDeck", "your partner");
                FillPartnerDeck(opponent + 2, opponent, "OpponentPartnerDeck", "the opponent's partner");
            }
            if (g_HasSeatControllers)
            {
                const uint8_t useTable = *g_bUseSeatControllerTable;
                int table[4];
                std::memcpy(table, g_SeatControllerTable, sizeof(table));
                std::memcpy(g_SeatControllerTable, g_SeatControllers, sizeof(table));
                *g_bUseSeatControllerTable = 1;
                Logger::WriteLog(std::format("TagDuel: seat controllers {} {} {} {} (0 human, 1 AI, 2 network)", g_SeatControllers[0], g_SeatControllers[1],
                    g_SeatControllers[2], g_SeatControllers[3]), MODULE_NAME, 0);
                const char result = orig_EngineInit();
                std::memcpy(g_SeatControllerTable, table, sizeof(table));
                *g_bUseSeatControllerTable = useTable;
                return result;
            }
        }
        return orig_EngineInit();
    }

    __int64 __fastcall Hook_LoadEngineFromFrontBlock(int side)
    {
        if (g_SharedThisDuel)
            return 0;
        return orig_LoadEngineFromFrontBlock(side);
    }
}

void TagDuel::Setup()
{
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_EngineInit, Hook_EngineInit);
    DetourAttach(&(PVOID&)orig_LoadEngineFromFrontBlock, Hook_LoadEngineFromFrontBlock);
    const LONG error = DetourTransactionCommit();
    Logger::WriteLog(std::format("TagDuel: hooks {} (Engine_Init, Duel_LoadEngineFromFrontBlock) - the Tag Duel menu button or TagDuel_SetEnabled(true) turns it on",
        error == NO_ERROR ? "attached" : std::format("FAILED (Detours error {})", error)), MODULE_NAME, error == NO_ERROR ? 0 : 2);
}

void TagDuel::SetEnabled(bool on)
{
    Logger::WriteLog(std::format("TagDuel::SetEnabled({})", on), MODULE_NAME, 0);
    g_Enabled = on;
}

bool TagDuel::IsEnabled()
{
    return g_Enabled;
}

void TagDuel::SetSeatControllers(const int (&controllers)[4])
{
    std::memcpy(g_SeatControllers, controllers, sizeof(g_SeatControllers));
    g_HasSeatControllers = true;
}

void TagDuel::ClearSeatControllers()
{
    g_HasSeatControllers = false;
}

void TagDuel::AllowInMultiplayer(bool on)
{
    Logger::WriteLog(std::format("TagDuel::AllowInMultiplayer({})", on), MODULE_NAME, 0);
    g_AllowInMultiplayer = on;
}

bool TagDuel::MultiplayerPluginLoaded()
{
    return GetModuleHandleA("Yu-Gi-Oh-MP.dll") != nullptr;
}
