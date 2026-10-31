#include "MasterDuel.h"

#include <algorithm>
#include <cstdint>
#include <format>
#include <random>
#include <string>
#include <vector>

#include "Detours.h"
#include "Loading.h"
#include "Logger.h"
#include "MasterDuelBridge.h"
#include "YuGiOh/YuGiOh-Addresses.h"

// docs/MasterDuelEngine.md. Signatures read in duel.dll.i64 (Master Duel's duel.dll next to its IDB); tested standalone 2026-10-30 (a CPU-vs-CPU
// duel on the game's own card files ran to the end).
namespace
{
    HMODULE g_Engine = nullptr;
    void* g_Work = nullptr;
    bool g_Ready = false;
    bool g_Tested = false;

    // The card data the engine keeps pointers to (it copies nothing): kept alive here until the next refresh.
    std::vector<uint32_t> g_Props;      // 2 u32 per record, records in the game's internal-id order (record 0 = the game's dummy card)
    std::vector<uint16_t> g_IdToRecord; // DLL_SetInternalID: record index of card id (first record's id + i)
    std::vector<uint64_t> g_Genres;     // DLL_SetCardGenre: u64 per record (DLL_CardIsThisCardGenre reads bit n of record r)
    std::vector<uint16_t> g_ToEngine(0x10000, 0), g_ToDisplay(0x10000, 0), g_DisplayToEngine(0x10000, 0);   // 0 = the same id
    // Lend: the records changed for this duel, with their own words to put back (duel.dll reads g_Props in place, so a change is live at once)
    struct LentRecord { size_t Record; uint32_t A, B; };
    std::vector<LentRecord> g_Lent;

    template <typename T> T Get(const char* name) { return reinterpret_cast<T>(g_Engine ? GetProcAddress(g_Engine, name) : nullptr); }

    // The game's decoded card properties (YGO::CARDS::IN_MEMORY_CARD_PROP, 0x30 bytes; Yu-Gi-Oh-MoreCards Card.h has the same struct).
    struct GameCardProps
    {
        int Id, Attack10, ArrowsOrDefense10, Kind, Type, Attribute, Level, Icon, Limitation, PendulumScale, StarType;
        int16_t Id2, Id3;
    };
    static_assert(sizeof(GameCardProps) == 0x30);

    using GetInternal_t = int64_t(__fastcall*)(int16_t);
    using GetProps_t = GameCardProps*(__fastcall*)(int16_t);

    // One card in duel.dll's layout (its DLL_CardGet* readers): u32 a = id 16 | kind 6 << 16 | attribute 4 << 22 | level / rank / link 4 << 26 |
    // star type 2 << 30 (1 Level, 2 Rank, 3 Link - the game's StarType values); u32 b = ATK/10 9 | DEF/10 or Link arrows 9 << 9 | icon 3 << 18 |
    // type 6 << 21 | Pendulum scale 4 << 27. 511 = "?" in both games.
    void Encode(const GameCardProps& p, uint32_t& a, uint32_t& b)
    {
        a = (p.Id & 0xFFFF) | (p.Kind & 63) << 16 | (p.Attribute & 15) << 22 | (p.Level & 15) << 26 | static_cast<uint32_t>(p.StarType & 3) << 30;
        b = (p.Attack10 & 511) | (p.ArrowsOrDefense10 & 511) << 9 | (p.Icon & 7) << 18 | (p.Type & 63) << 21 | static_cast<uint32_t>(p.PendulumScale & 15) << 27;
    }

    struct GameDataFile { uint64_t Size; void* Data; };
    const GameDataFile* CardFile(size_t offset)
    {
        const uintptr_t files = *reinterpret_cast<const uintptr_t*>(YGO::Addr::g_pCardDataFiles);
        return files ? reinterpret_cast<const GameDataFile*>(files + offset) : nullptr;
    }

    // A CPU-vs-CPU duel of two 40-card decks of Level 1-4 Normal Monsters, stepped until it ends: proves the engine runs in the game with this card data.
    struct TestResult { int Steps = 0, Turn = 0, Lp0 = 0, Lp1 = 0, Finish = 0; bool Faulted = false; };
    void RunTestDuel(const uint32_t* deck0, const uint32_t* deck1, TestResult& r)
    {
        const auto clear = Get<int64_t(*)()>("DLL_DuelSysClearWork");
        const auto setDeck = Get<int64_t(*)(int, const uint32_t*, int, const uint32_t*, int, const uint32_t*, int)>("DLL_DuelSysSetDeck2");
        const auto setType = Get<int64_t(*)(int, int)>("DLL_DuelSetPlayerType");
        const auto setCpu = Get<int64_t(*)(int, int)>("DLL_DuelSetCpuParam");
        const auto seed = Get<int64_t(*)(int)>("DLL_DuelSetRandomSeed");
        const auto first = Get<int64_t(*)(int)>("DLL_DuelSetFirstPlayer");
        const auto init = Get<int64_t(*)(int64_t, int, int, int, int, int, int)>("DLL_DuelSysInitCustom");
        const auto act = Get<int64_t(*)()>("DLL_DuelSysAct");
        const auto turn = Get<int(*)()>("DLL_DuelGetTurnNum");
        const auto lp = Get<int(*)(int)>("DLL_DuelGetLP");
        const auto finish = Get<int(*)()>("DLL_DuelGetDuelFinish");
        if (!clear || !setDeck || !setType || !setCpu || !seed || !first || !init || !act || !turn || !lp || !finish)
        {
            r.Faulted = true;
            return;
        }
        const uint32_t none = 0;
        __try
        {
            clear();
            for (int p = 0; p < 2; ++p)
            {
                setDeck(p, p ? deck1 : deck0, 40, &none, 0, &none, 0);
                setType(p, 1);   // 0 human, 1 CPU (DLL_DuelIsHuman), 3 replay
                setCpu(p, 0);
            }
            seed(static_cast<int>(GetTickCount()));
            first(0);
            init(0, 0, 8000, 8000, 5, 5, 0);   // (type, flag, LP, LP, hand, hand, no shuffle): DLL_DuelSysInitCustom -> sub_180EA8FE0
            while (r.Steps < 200000 && !(r.Finish = finish()))
            {
                act();
                ++r.Steps;
            }
            r.Turn = turn();
            r.Lp0 = lp(0);
            r.Lp1 = lp(1);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            r.Faulted = true;
        }
    }

    void SelfTest()
    {
        std::vector<uint32_t> pool;
        for (size_t i = 1; i * 2 + 1 < g_Props.size(); ++i)
        {
            const uint32_t a = g_Props[i * 2];
            const int kind = (a >> 16) & 63, level = (a >> 26) & 15;
            if (kind == 0 && level >= 1 && level <= 4 && (a >> 30) == 1)   // kind 0 = Normal Monster in both kind tables
                pool.push_back(a & 0xFFFF);
        }
        if (pool.size() < 40)
        {
            Logger::WriteLog(std::format("Master Duel engine test duel skipped: only {} Normal Monsters in the card data", pool.size()), MODULE_NAME, 2);
            return;
        }
        std::mt19937 rng(GetTickCount());
        std::vector<uint32_t> decks[2];
        for (auto& deck : decks)
            for (int i = 0; i < 40; ++i)
                deck.push_back(pool[rng() % pool.size()]);
        TestResult r;
        RunTestDuel(decks[0].data(), decks[1].data(), r);
        if (r.Faulted)
            Logger::WriteLog(std::format("Master Duel engine test duel FAULTED after {} steps", r.Steps), MODULE_NAME, 2);
        else
            Logger::WriteLog(std::format("Master Duel engine test duel (CPU vs CPU): {} after {} steps, turn {}, LP {} / {}",
                r.Finish ? "finished" : "did not finish", r.Steps, r.Turn, r.Lp0, r.Lp1), MODULE_NAME, r.Finish ? 0 : 2);
    }

    using Setup_t = char(__fastcall*)(const int*, int);
    Setup_t orig_Setup_CardPropTable = reinterpret_cast<Setup_t>(YGO::Addr::Setup_CardPropTable);
    char __fastcall Hook_Setup_CardPropTable(const int* a1, int language)
    {
        const char result = orig_Setup_CardPropTable(a1, language);
        // the game's cards; Yu-Gi-Oh-MoreCards calls Core_EngineRefreshCards again once it has added its own (its hook runs around this one)
        MasterDuel::RefreshCards();
        return result;
    }
}

int MasterDuel::RefreshCards()
{
    if (!g_Engine)
        return 0;
    const auto getInternal = reinterpret_cast<GetInternal_t>(YGO::Addr::Get_InternalIdFromKonamiId);
    const auto getProps = reinterpret_cast<GetProps_t>(YGO::Addr::Get_CardPropsFromInternalId);

    // every card the game's lookups know (MoreCards widens them past the game's 14-bit ids), by internal id
    std::vector<int> idOfRecord;
    int maxId = 0;
    for (int id = 1; id <= 0x7FFF; ++id)
    {
        const int64_t internal = getInternal(static_cast<int16_t>(id));
        if (internal <= 0 || internal > 0x7FFF)
            continue;
        if (idOfRecord.size() <= static_cast<size_t>(internal))
            idOfRecord.resize(static_cast<size_t>(internal) + 1, -1);
        // the engine knows the card by its engine id (its Konami id for a mod card, Core_EngineMapCard)
        const int engineId = MasterDuel::ToEngine(static_cast<uint16_t>(id));
        idOfRecord[static_cast<size_t>(internal)] = engineId;
        maxId = (std::max)(maxId, engineId);
    }
    if (idOfRecord.size() < 2)
    {
        Logger::WriteLog("Master Duel engine: the game's card tables are empty, no card data given", MODULE_NAME, 2);
        return 0;
    }

    const size_t records = idOfRecord.size();
    std::vector<uint32_t> props(records * 2 + 2, 0);
    for (size_t r = 1; r < records; ++r)
    {
        if (idOfRecord[r] < 0)
            continue;
        if (const GameCardProps* p = getProps(static_cast<int16_t>(r)))
        {
            GameCardProps engine = *p;
            engine.Id = idOfRecord[r];
            Encode(engine, props[r * 2], props[r * 2 + 1]);
        }
    }
    // duel.dll takes its id range from record 1 (first id) and the last record (last id + 1): a last record with the highest id keeps every
    // card in range when the custom cards' internal ids are not in id order
    size_t count = records;
    if ((props[(records - 1) * 2] & 0xFFFF) != static_cast<uint32_t>(maxId))
    {
        props[records * 2] = static_cast<uint32_t>(maxId);
        count = records + 1;
    }
    const int firstId = static_cast<int>(props[2] & 0xFFFF);
    std::vector<uint16_t> idToRecord(static_cast<size_t>(maxId - firstId + 1), 0);
    for (size_t r = 1; r < records; ++r)
        if (idOfRecord[r] >= firstId)
            idToRecord[static_cast<size_t>(idOfRecord[r] - firstId)] = static_cast<uint16_t>(r);

    // genres: the game's CARD_Genre (u64 per internal id) for its cards; MoreCards' cards have none here yet
    std::vector<uint64_t> genres(count, 0);
    if (const GameDataFile* f = CardFile(0x90); f && f->Data)
        memcpy(genres.data(), f->Data, (std::min)(static_cast<size_t>(f->Size), genres.size() * 8));

    g_Lent.clear();   // the new records are every card's own (a lend made before this refresh is gone with the old ones)
    g_Props.swap(props);
    g_IdToRecord.swap(idToRecord);
    g_Genres.swap(genres);
    Get<void(*)(const void*, int)>("DLL_SetCardProperty")(g_Props.data(), static_cast<int>(count * 8));
    Get<void(*)(const void*)>("DLL_SetInternalID")(g_IdToRecord.data());
    Get<void(*)(const void*)>("DLL_SetCardGenre")(g_Genres.data());
    // archetypes, same-name and the card links are the game's own files (the same formats; MoreCards' cards are not in them yet)
    if (const GameDataFile* f = CardFile(0xC0); f && f->Data)
        Get<void(*)(const void*)>("DLL_SetCardNamed")(f->Data);
    if (const GameDataFile* f = CardFile(0xB0); f && f->Data)
        Get<void(*)(const void*, int)>("DLL_SetCardSame")(f->Data, static_cast<int>(f->Size));
    if (const GameDataFile* f = CardFile(0xA0); f && f->Data)
        Get<void(*)(const void*, int)>("DLL_SetCardLink")(f->Data, static_cast<int>(f->Size));
    g_Ready = true;

    const auto atk = Get<int(*)(int)>("DLL_CardGetAtk");
    Logger::WriteLog(std::format("Master Duel engine: card data for {} cards (ids {}-{}); check: Blue-Eyes White Dragon ATK {}", records - 1, firstId, maxId,
        atk ? atk(4007) : -1), MODULE_NAME, 0);
    // the CPU-vs-CPU test duel takes seconds (the Debug build ~6 s at start-up): only with [Yu-Gi-Oh-Core] MasterDuelLog=2
    if (!g_Tested && Loading::Setting("MasterDuelLog", "0") == "2")
    {
        g_Tested = true;
        SelfTest();
    }
    return static_cast<int>(records - 1);
}

bool MasterDuel::Ready() { return g_Ready; }

void MasterDuel::MapCard(int gameId, int engineId)
{
    if (gameId <= 0 || gameId > 0xFFFF || engineId <= 0 || engineId > 0xFFFF)
        return;
    g_ToEngine[gameId] = static_cast<uint16_t>(engineId);
    if (gameId <= 0x3FFF)   // a game id the duel state can hold is what the screen shows, until a borrowed id is set
        g_ToDisplay[engineId] = static_cast<uint16_t>(gameId);
}

void MasterDuel::SetDisplayId(int engineId, int displayId)
{
    if (engineId > 0 && engineId <= 0xFFFF && displayId > 0 && displayId <= 0x3FFF)
    {
        g_ToDisplay[engineId] = static_cast<uint16_t>(displayId);
        g_DisplayToEngine[displayId] = static_cast<uint16_t>(engineId);
    }
}

uint16_t MasterDuel::ToEngine(uint16_t gameId) { return g_ToEngine[gameId] ? g_ToEngine[gameId] : gameId; }

// a deck card: a borrowed duel id first (MoreCards borrows game ids for cards past 16383), then the card's own mapping
uint16_t MasterDuel::DeckToEngine(uint16_t gameId) { return g_DisplayToEngine[gameId] ? g_DisplayToEngine[gameId] : ToEngine(gameId); }

std::vector<uint16_t> MasterDuel::EngineIds()
{
    std::vector<uint16_t> ids;
    if (g_Props.size() < 4)
        return ids;
    const size_t firstId = g_Props[2] & 0xFFFF;
    for (size_t i = 0; i < g_IdToRecord.size(); ++i)
        if (g_IdToRecord[i] && firstId + i <= 0xFFFF)
            ids.push_back(static_cast<uint16_t>(firstId + i));
    return ids;
}

bool MasterDuel::Lend(int displayId, int engineId, int propsGameId)
{
    if (!g_Ready || displayId <= 0 || displayId > 0x3FFF || engineId <= 0 || engineId > 0xFFFF || propsGameId <= 0 || propsGameId > 0xFFFF
        || g_Props.size() < 4)
        return false;
    const int firstId = static_cast<int>(g_Props[2] & 0xFFFF);
    if (engineId < firstId || static_cast<size_t>(engineId - firstId) >= g_IdToRecord.size())
        return false;
    const size_t record = g_IdToRecord[static_cast<size_t>(engineId - firstId)];
    if (!record || record * 2 + 1 >= g_Props.size())
        return false;   // duel.dll has no card with that id
    const auto getInternal = reinterpret_cast<GetInternal_t>(YGO::Addr::Get_InternalIdFromKonamiId);
    const auto getProps = reinterpret_cast<GetProps_t>(YGO::Addr::Get_CardPropsFromInternalId);
    const int64_t internal = getInternal(static_cast<int16_t>(propsGameId));
    const GameCardProps* props = internal > 0 && internal <= 0x7FFF ? getProps(static_cast<int16_t>(internal)) : nullptr;
    if (!props)
        return false;
    g_Lent.push_back({ record, g_Props[record * 2], g_Props[record * 2 + 1] });
    GameCardProps engine = *props;
    engine.Id = engineId;
    Encode(engine, g_Props[record * 2], g_Props[record * 2 + 1]);
    g_ToDisplay[engineId] = static_cast<uint16_t>(displayId);
    g_DisplayToEngine[displayId] = static_cast<uint16_t>(engineId);
    Logger::WriteLog(std::format("Master Duel engine: card {} (shown as {}) plays as engine card {} this duel", propsGameId, displayId, engineId), MODULE_NAME, 0);
    return true;
}

void MasterDuel::ClearDisplayIds()
{
    for (auto it = g_Lent.rbegin(); it != g_Lent.rend(); ++it)   // the lent records back to their own cards
        if (it->Record * 2 + 1 < g_Props.size())
        {
            g_Props[it->Record * 2] = it->A;
            g_Props[it->Record * 2 + 1] = it->B;
        }
    g_Lent.clear();
    std::fill(g_DisplayToEngine.begin(), g_DisplayToEngine.end(), static_cast<uint16_t>(0));
    for (size_t engine = 0; engine < g_ToDisplay.size(); ++engine)   // back to the ids MapCard set (game ids the state can hold)
        g_ToDisplay[engine] = 0;
    for (size_t game = 1; game <= 0x3FFF; ++game)
        if (g_ToEngine[game])
            g_ToDisplay[g_ToEngine[game]] = static_cast<uint16_t>(game);
}
uint16_t MasterDuel::ToDisplay(uint16_t engineId) { return g_ToDisplay[engineId] ? g_ToDisplay[engineId] : engineId; }

FARPROC MasterDuel::Proc(const char* name) { return g_Engine && name ? GetProcAddress(g_Engine, name) : nullptr; }

bool MasterDuel::Install()
{
    if (Loading::Setting("MasterDuelEngine", "0") != "1")
        return false;

    char path[MAX_PATH] = {};
    GetModuleFileNameA(GetModuleHandleA("Yu-Gi-Oh-Core.dll"), path, MAX_PATH);
    std::string dll = path;
    dll = dll.substr(0, dll.find_last_of("\\/") + 1) + "Yu-Gi-Oh-Core\\duel.dll";
    g_Engine = LoadLibraryExA(dll.c_str(), nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
    if (!g_Engine)
    {
        Logger::WriteLog(std::format("Master Duel engine: could not load {} (error {})", dll, GetLastError()), MODULE_NAME, 2);
        return false;
    }
    const auto setWork = Get<int64_t(*)(void*)>("DLL_SetWorkMemory");
    const auto revision = Get<const char*(*)()>("DLL_GetRevisionString");
    if (!setWork)
    {
        Logger::WriteLog("Master Duel engine: duel.dll has no DLL_SetWorkMemory", MODULE_NAME, 2);
        return false;
    }
    const int64_t size = setWork(nullptr);   // NULL asks the size (2,908,148 bytes in the build this was written against)
    g_Work = VirtualAlloc(nullptr, static_cast<SIZE_T>(size), MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!g_Work || setWork(g_Work) != 0)
    {
        Logger::WriteLog(std::format("Master Duel engine: no work memory ({} bytes)", size), MODULE_NAME, 2);
        return false;
    }

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_Setup_CardPropTable, Hook_Setup_CardPropTable);
    const LONG error = DetourTransactionCommit();
    Logger::WriteLog(std::format("Master Duel engine loaded from {} ({} bytes of work memory, revision {}); card data hook: {}", dll, size,
        revision ? revision() : "?", error), MODULE_NAME, 0);
    // offline duels play on it (MasterDuelBridge.h)
    MasterDuelBridge::Install(g_Work);
    return error == NO_ERROR;
}
