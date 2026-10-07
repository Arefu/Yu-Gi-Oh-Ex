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

    // Free Duel's partner picks (Yu-Gi-Oh-Campaign): [0] your partner, [1] the opponent's partner. Deck ids 0-31 save decks, 32+
    // deckdata; -1 = not picked. g_Armed: picks waiting for the next duel; g_SeatsSetThisDuel: AssignSeatDecks used them.
    std::atomic<int> g_PickedPartnerDeck[2] = { -1, -1 };
    std::atomic<int> g_PartnerCharacter[2] = { -1, -1 };
    std::atomic<bool> g_Armed = false;
    std::atomic<bool> g_SeatsSetThisDuel = false;

    // Deck_FromId (0x14081AB40) -> YGO::SAVE::DeckListItem. From +0x42 it has the seat record's layout: the three counts, then main 60,
    // extra 15, side 15 - words 33..125 of both.
    using DeckFromId_t = const uint16_t*(__fastcall*)(unsigned int profile, unsigned int deckId);
    DeckFromId_t Call_DeckFromId = reinterpret_cast<DeckFromId_t>(0x14081AB40);
    constexpr unsigned int kCurrentProfile = 0xFFFFFFFD;

    bool CopyPickedDeck(uint16_t* deck, int deckId)
    {
        if (deckId < 0)
            return false;
        const uint16_t* item = Call_DeckFromId(kCurrentProfile, static_cast<unsigned int>(deckId));
        if (!item || item[kMainCount] == 0)
            return false;
        std::memcpy(deck + kMainCount, item + kMainCount, sizeof(uint16_t) * (kDeckWords - kMainCount));
        return true;
    }

    // Gives the partner seat a deck: the one picked on the deck screen, else the .ydc named by the setting, else a copy of the
    // teammate's deck (their own copy - the two hands and decks are still separate).
    void FillPartnerDeck(int partnerSeat, int teammateSeat, int pickedDeck, const char* settingKey, const char* who)
    {
        uint16_t* deck = SeatDeck(partnerSeat);
        if (CopyPickedDeck(deck, pickedDeck))
        {
            Logger::WriteLog(std::format("TagDuel: {} (seat {}) plays picked deck {} ({} main, {} extra)", who, partnerSeat, pickedDeck, deck[kMainCount],
                deck[kExtraCount]), MODULE_NAME, 0);
            return;
        }
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

    // The Local seat screen's toggles, by TagDuel::Role.
    std::atomic<bool> g_SeatHuman[4] = { true, false, false, false };

    // Seats pair by parity: 0 & 2 are one team, 1 & 3 the other.
    void ControllersFromRoles(int (&out)[4])
    {
        const int you = Call_GetLocalSeat() & 1;
        const int opponent = 1 - you;
        const auto of = [](TagDuel::Role role) { return g_SeatHuman[role] ? TagDuel::Human : TagDuel::AI; };
        out[you] = of(TagDuel::RoleYou);
        out[you + 2] = of(TagDuel::RolePartner);
        out[opponent] = of(TagDuel::RoleOpponent);
        out[opponent + 2] = of(TagDuel::RoleOpponentPartner);
    }

    // A human on the opponent's team needs the game's local-versus mode for the whole duel: with g_bUseSeatControllerTable set,
    // DuelHand_Draw shows the hand of any side whose active duelist is human, and Duel_IsSideLocallyControlled (0x1407BDE00) takes input
    // for every human side. Turned off as the result screen opens (DuelResult_SetupForMode / FinishAndUpdateSave must see a normal duel)
    // and at the next Engine_Init; leaving a duel early goes through the main menu, which clears it too.
    bool g_FlagHeldForDuel = false;
    uint8_t g_HeldSavedUseTable = 0;
    int g_HeldSavedTable[4] = {};

    void ReleaseHeldFlag(const char* why)
    {
        if (!g_FlagHeldForDuel)
            return;
        std::memcpy(g_SeatControllerTable, g_HeldSavedTable, sizeof(g_HeldSavedTable));
        *g_bUseSeatControllerTable = g_HeldSavedUseTable;
        g_FlagHeldForDuel = false;
        Logger::WriteLog(std::format("TagDuel: local-versus mode off ({})", why), MODULE_NAME, 0);
    }

    // ScreenGameResult__OnEnter (0x14083F7F0): FinishAndUpdateSave counts the duel (TAG stats while tag is on); tag is turned off after it
    // so it can't leak into the next duel. Arguments passed through untouched.
    using GameResultOnEnter_t = __int64(__fastcall*)(__int64, __int64, __int64, __int64);
    GameResultOnEnter_t orig_GameResultOnEnter = reinterpret_cast<GameResultOnEnter_t>(0x14083F7F0);

    __int64 __fastcall Hook_GameResultOnEnter(__int64 a1, __int64 a2, __int64 a3, __int64 a4)
    {
        ReleaseHeldFlag("result screen");
        const __int64 result = orig_GameResultOnEnter(a1, a2, a3, a4);   // FinishAndUpdateSave counts TAG stats while tag is still on
        if (g_Enabled && !Call_GetIsDuelMultiplayer())
            Call_SetIsTagDuel(false);
        return result;
    }

    // YGO__DuelSetup__AssignSeatDecksAndDuelists (0x14081E530): offline, outside campaign story / challenge / battle pack / tutorial, it
    // gives seats 0..Get_NumberOfPlayers()-1 Duel_SetupSeat(seat, g_DuelSideCharacters[seat], Deck_FromId(g_DuelSideDecks[seat])) -
    // 4 seats once tag is on. So for an armed Free Duel the game sets the partners up itself (portrait, name, deck in the seat record).
    using Void_t = void(__fastcall*)();
    Void_t orig_AssignSeatDecks = reinterpret_cast<Void_t>(0x14081E530);
    const auto Call_SetSideCharacter = reinterpret_cast<void(__fastcall*)(int side, unsigned int character)>(0x140769670);   // Set_DuelSideCharacter
    const auto Call_SetSideDeck = reinterpret_cast<void(__fastcall*)(int side, unsigned int deck)>(0x140769690);             // Set_DuelSideDeck
    auto* const g_DuelSideDecks = reinterpret_cast<const int*>(0x140C8D208);                                              // int[4]

    // g_CharacterRecords (0x142913470): 0x68 bytes each, +8 = the character's deckdata index (deck id = index + 32).
    int CharacterDeck(int character)
    {
        if (character < 0 || character >= 240)
            return -1;
        const int index = *reinterpret_cast<const int*>(0x142913470 + 0x68 * static_cast<uintptr_t>(character) + 8);
        return index >= 0 ? index + 32 : -1;
    }

    void __fastcall Hook_AssignSeatDecks()
    {
        if (Call_GetIsDuelMultiplayer())
            return orig_AssignSeatDecks();
        if (!g_Enabled || !g_Armed.exchange(false))
        {
            Call_SetIsTagDuel(false);   // a tag duel left before its result screen must not make this one 4 seats
            return orig_AssignSeatDecks();
        }

        Call_SetIsTagDuel(true);   // Get_NumberOfPlayers() = 4 from here
        const int you = Call_GetLocalSeat() & 1;
        const int teams[2] = { you, 1 - you };
        for (int i = 0; i < 2; ++i)
        {
            const int seat = teams[i] + 2;
            int character = g_PartnerCharacter[i];
            if (character <= 0)
            {
                // Character 0 would become 105 (the default avatar) in Set_DuelSideCharacter: use the teammate's character instead.
                character = static_cast<int>(reinterpret_cast<const unsigned int*>(0x140C8D1F8)[teams[i]]);   // g_DuelSideCharacters
                Logger::WriteLog(std::format("TagDuel: no character picked for seat {} - using its teammate's ({})", seat, character), MODULE_NAME, 1);
            }
            int deck = g_PickedPartnerDeck[i];
            if (deck < 0)
                deck = CharacterDeck(character);
            if (deck < 0)
                deck = g_DuelSideDecks[teams[i]];   // the teammate's deck
            Call_SetSideCharacter(seat, static_cast<unsigned int>(character));
            Call_SetSideDeck(seat, static_cast<unsigned int>(deck));
            char name[128]{};
            if (const auto wide = reinterpret_cast<const wchar_t*(__fastcall*)(int)>(0x1407FEC70)(character))   // YGO::GAME::Get_CharacterName
                WideCharToMultiByte(CP_UTF8, 0, wide, -1, name, sizeof(name) - 1, nullptr, nullptr);
            Logger::WriteLog(std::format("TagDuel: seat {} ({}) = character {} ({}), deck {}", seat, i == 0 ? "your partner" : "opponent's partner",
                character, name, deck), MODULE_NAME, 69);
        }
        g_SeatsSetThisDuel = true;
        orig_AssignSeatDecks();
    }

    char __fastcall Hook_EngineInit()
    {
        ReleaseHeldFlag("new duel");
        g_SharedThisDuel = false;
        if (g_Enabled && Call_GetIsDuelMultiplayer() && !g_AllowInMultiplayer)
        {
            Logger::WriteLog("TagDuel: online duel - left as the game set it up (no multiplayer plugin handles tag duels yet)", MODULE_NAME, 1);
            return orig_EngineInit();
        }
        // Offline, only a duel Free Duel armed (and AssignSeatDecks set up as 4 seats) is a tag duel; campaign duels stay 1v1.
        const bool online = Call_GetIsDuelMultiplayer();
        const bool seatsSet = g_SeatsSetThisDuel.exchange(false);
        if (g_Enabled && !online && !seatsSet)
        {
            Call_SetIsTagDuel(false);
            Logger::WriteLog("TagDuel: not a Free Duel tag duel (no partners picked) - normal duel", MODULE_NAME, 0);
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
            else if (online)
            {
                // Seats pair by parity: 0 & 2 are one team, 1 & 3 the other.
                const int local = Call_GetLocalSeat() & 1;
                const int opponent = 1 - local;
                FillPartnerDeck(local + 2, local, g_PickedPartnerDeck[0], "PartnerDeck", "your partner");
                FillPartnerDeck(opponent + 2, opponent, g_PickedPartnerDeck[1], "OpponentPartnerDeck", "the opponent's partner");
            }
            // A plugin's seat controllers (Yu-Gi-Oh-MP) win; otherwise the local seating picked from the menu.
            int controllers[4];
            if (g_HasSeatControllers)
                std::memcpy(controllers, g_SeatControllers, sizeof(controllers));
            else
                ControllersFromRoles(controllers);

            // The game's own seat controller table, set around Engine_Init (it reads it there).
            g_HeldSavedUseTable = *g_bUseSeatControllerTable;
            std::memcpy(g_HeldSavedTable, g_SeatControllerTable, sizeof(g_HeldSavedTable));
            std::memcpy(g_SeatControllerTable, controllers, sizeof(controllers));
            *g_bUseSeatControllerTable = 1;
            Logger::WriteLog(std::format("TagDuel: seat controllers {} {} {} {} (0 human, 1 AI, 2 network)", controllers[0], controllers[1],
                controllers[2], controllers[3]), MODULE_NAME, 69);
            const char result = orig_EngineInit();

            const int opponent = 1 - (Call_GetLocalSeat() & 1);
            if (!g_HasSeatControllers && (controllers[opponent] == TagDuel::Human || controllers[opponent + 2] == TagDuel::Human))
            {
                g_FlagHeldForDuel = true;   // put back by ReleaseHeldFlag
                Logger::WriteLog("TagDuel: local-versus mode on for this duel (a human on the opponent's team)", MODULE_NAME, 0);
            }
            else
            {
                std::memcpy(g_SeatControllerTable, g_HeldSavedTable, sizeof(g_HeldSavedTable));
                *g_bUseSeatControllerTable = g_HeldSavedUseTable;
            }
            return result;
        }
        return orig_EngineInit();
    }

    // Duel__Msg__Handle_04_NextTurn (0x1401129E0) is the only caller. It bumps the turn counter first; the first turn never goes
    // through it, so turn 2 sees 1 and turn 3 sees 2. Normal format swaps when the counter is > 1: A1, B1, A2, B2 - already right.
    // The two LP-based formats (Rules & 0xFF0F == 0x100 / 0x101) swap on their own condition. Logged so a test shows every swap.
    // The NextTurn handler's `inc dword [rip+0x033889AA]` at 0x140112A10 -> 0x14349B3C0 (IDA's stru_143497C40.field_377A label is 6 off).
    auto* const g_DuelTurnCounter = reinterpret_cast<const uint32_t*>(0x14349B3C0);
    auto* const g_DuelRules = reinterpret_cast<const uint32_t*>(0x1433305AC);                 // Duel_DuelEngine.Rules
    // stru_143497C40.PlayerOne.iNumberOfCardsInHand, one PlayerState (869 dwords) per side
    int HandCount(int side)
    {
        const auto* hand = reinterpret_cast<const int*>(0x143497C40) + 869 * (side & 1);
        return hand[0xC / 4];
    }

    __int64 __fastcall Hook_LoadEngineFromFrontBlock(int side)
    {
        if (g_SharedThisDuel)
            return 0;
        const int before = HandCount(side);
        Logger::WriteLog(std::format("TagDuel: partner swap side {} starting (turn counter {}, rules {:#x}, hand {})", side, *g_DuelTurnCounter,
            *g_DuelRules & 0xFF0F, before), MODULE_NAME, 69);
        const __int64 result = orig_LoadEngineFromFrontBlock(side);
        Logger::WriteLog(std::format("TagDuel: partner swap side {} done: hand {} -> {}", side, before, HandCount(side)), MODULE_NAME, 69);
        return result;
    }
}

void TagDuel::Setup()
{
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_EngineInit, Hook_EngineInit);
    DetourAttach(&(PVOID&)orig_LoadEngineFromFrontBlock, Hook_LoadEngineFromFrontBlock);
    DetourAttach(&(PVOID&)orig_GameResultOnEnter, Hook_GameResultOnEnter);
    DetourAttach(&(PVOID&)orig_AssignSeatDecks, Hook_AssignSeatDecks);
    const LONG error = DetourTransactionCommit();
    Logger::WriteLog(std::format("TagDuel: hooks {} (Engine_Init, Duel_LoadEngineFromFrontBlock, ScreenGameResult OnEnter) - the Tag Duel menu buttons or TagDuel_SetEnabled(true) turn it on",
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

void TagDuel::SetPartners(int yourCharacter, int yourDeck, int opponentCharacter, int opponentDeck)
{
    g_PartnerCharacter[0] = yourCharacter;
    g_PartnerCharacter[1] = opponentCharacter;
    g_PickedPartnerDeck[0] = yourDeck;
    g_PickedPartnerDeck[1] = opponentDeck;
    g_Armed = true;
}

int TagDuel::PartnerCharacter(int which)
{
    return which == 0 || which == 1 ? g_PartnerCharacter[which].load() : -1;
}

int TagDuel::PartnerDeck(int which)
{
    if (which != 0 && which != 1)
        return -1;
    const int deck = g_PickedPartnerDeck[which];
    return deck >= 0 ? deck : CharacterDeck(g_PartnerCharacter[which]);
}

void TagDuel::SetSeatHuman(Role role, bool human)
{
    if (role < RoleYou || role > RoleOpponentPartner)
        return;
    g_SeatHuman[role] = human;
}

bool TagDuel::IsSeatHuman(Role role)
{
    return role >= RoleYou && role <= RoleOpponentPartner && g_SeatHuman[role];
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
