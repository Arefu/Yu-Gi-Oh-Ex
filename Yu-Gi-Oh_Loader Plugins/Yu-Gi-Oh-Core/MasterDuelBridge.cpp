#include <Windows.h>

#include "MasterDuelBridge.h"

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <format>
#include <unordered_set>
#include <vector>

#include "Detours.h"
#include "Loading.h"
#include "Logger.h"
#include "MasterDuel.h"

// The duel played on Master Duel's engine (docs/MasterDuelEngine.md, "Redirect design"). The game's own duel setup and duel screen keep
// running; what its engine did is done by duel.dll:
//   - duel start: the game's Engine_Init fills Duel_DuelEngine (which is duel.dll's config block, byte for byte: seed / LP key, player types,
//     CPU params, first player, the deck blocks at +44 + 192 x player, rules) and Duel_UNK (its 1340-byte per-player block); they are copied in
//     and duel.dll's own init (DLL_DuelSysInitCustom) runs instead of the game's DuelSetup_InitEngine;
//   - the tick: Duel__MasterTick_GatedByNetEvent's phase step becomes DLL_DuelSysAct. duel.dll runs its own message queue and executor and
//     asks for animations through DLL_SetEffectDelegate: its two callbacks are the game's own Draw_DuelAnimationFromId (code, a, b, c) and
//     Duel__UI__IsAnimBusy (anim id) - what the game's engine calls through its UI thunks (0x1407EB7E0 / 0x1407EB7B0);
//   - the state the duel screen reads is mirrored from duel.dll's state into the game's after every step and before every animation;
//   - the engine calls the duel screen makes for menus and commands go to duel.dll's exports.
// Offline duels only (not tag, not online, not tutorials) until those are traced.
namespace
{
    constexpr uintptr_t kState = 0x143497C40;          // stru_143497C40 (Duel::PlayerState x2 + globals)
    constexpr uintptr_t kEngine = 0x143330280;         // Duel_DuelEngine (= duel.dll config, 824 bytes)
    constexpr uintptr_t kUnk = 0x14332F500;            // Duel_UNK (= duel.dll's 1340-byte block)
    constexpr size_t kConfigBytes = 824, kUnkBytes = 1340;
    constexpr uintptr_t kList = 0x14349C5C0;           // the list block (YGO::Duel::List_* read it)
    constexpr size_t kMdList = 15824;                  // duel.dll's (DLL_DuelList* read qword_1811F9C30)

    // duel.dll work memory blocks (DLL_SetWorkMemory)
    constexpr size_t kMdState = 0, kMdUnk = 2905984, kMdConfig = 2907324;

    // per player, game / Master Duel (real offsets read from the instructions, not IDA's field names: those are 6 bytes off in places)
    constexpr size_t kGamePlayer = 0xD94, kMdPlayer = 3548;
    constexpr size_t kGameZones = 0x4C, kMdZones = 92, kGameZone = 24, kMdZone = 28;
    // Each pile is duel.dll's at the game's offset + 72 (its zones are 4 bytes longer); slot counts and counts' offsets match (YuGiOh-DUELSTATE.h).
    // duel.dll's deck loader (sub_180037590) writes the Deck at +964 counted at +0x10 and the Extra Deck at +1444 counted at +0x18.
    struct List { size_t Game, Md, Slots, CountOffset; };
    constexpr List kLists[] = {
        { 0x19C, 484, 120, 0x0C },    // hand (zone 13)
        { 0x37C, 964, 120, 0x10 },    // Deck (zone 15)
        { 0x55C, 1444, 150, 0x18 },   // Extra Deck (zone 14)
        { 0x7B4, 2044, 150, 0x14 },   // GY (zone 16)
        { 0xA0C, 2644, 226, 0x1C },   // banished (zone 17, to the end of the player)
    };
    constexpr size_t kGameInstances = 0x1B58, kMdInstances = 7148, kInstances = 512;
    // The globals after the players: three runs of one shift each (duel.dll offset = game offset + shift), from the fields both engines'
    // functions read (real offsets from the instructions): 0x3774 turn player = 15368 (DLL_DuelWhichTurnNow), 0x3778 = 15372, 0x3780 turn
    // number, 0x3788 phase = 15388, 0x378C / 0x3790 step / damage step, 0x37A4 duel type = 15416, 0x37B0 net wait = 15428, 0x3804 prompt
    // owner = 15512 (List_GetItemAttribute / DLL_DuelListGetItemAttribute), 0x3808-0x3810 prompt text; 0x3838 input mode = 15568
    // (Pick_GetCardFlags / DLL_DuelComGetCommandMask); 0x3840 input active = 15580 and 0x3864 asked player = 15616 (Get_PhaseMenuFlags /
    // DLL_DuelComGetMovablePhase). Stops before 0x3870: past it are the pick callback pointers (duel.dll's would point into duel.dll).
    struct Run { size_t Game, End, Shift; };
    constexpr Run kGlobals[] = {
        { 0x3770, 0x3834, 1172 },
        { 0x3838, 0x3840, 1176 },   // input mode + its sub-mode (15572, DLL_DuelComCancelCommand2's v2[3893])
        { 0x3840, 0x3870, 1180 },
    };

    uint32_t g_LastGlobals[(0x3870 - 0x3770) / 4]{};   // duel.dll's global words at the last mirror (Mirror copies changed ones only)
    bool g_GlobalsReset = true;                        // the first mirror of a duel copies them all
    bool g_Active = false;      // this duel runs on duel.dll
    bool g_Failed = false;      // a fault turned the bridge off for this run

    // Card-name declaration (duel.dll dialog kind 160-170 = 20 + its list kind 0x8C-0x96; Crossout Designator ...). duel.dll's own client shows
    // a card search; the game has no such dialog (its name effects pick from a list), so the bridge opens the game's list picker with the legal
    // cards, keeps duel.dll waiting (OnBusy) and answers with DLL_DuelDlgSetResult(card). Without this duel.dll found no answer, said "can't
    // declare that" (message 145) and asked again, many times a second.
    bool g_Picking = false;            // the picker is open: the game's list block / dialog fields are the picker's, not duel.dll's
    bool g_PickDone = false;           // answered: send it before the next step
    int g_PickIndex = -1;
    uint16_t g_PickResult = 0;         // engine id
    std::vector<uint16_t> g_PickEngineIds;
    uint8_t* g_Work = nullptr;

    template <typename T> T Proc(const char* name) { return reinterpret_cast<T>(MasterDuel::Proc(name)); }

    // ---- call log ([Yu-Gi-Oh-Core] MasterDuelLog: 0 off, 1 = the first 20 calls of each export / animation code then every 500th, 2 = all)
    int g_LogLevel = 0;
    struct CallCount { const char* Name; uint64_t Count; };
    CallCount g_Counts[64]{};
    bool ShouldLog(const char* name)
    {
        if (g_LogLevel <= 0)
            return false;
        for (CallCount& c : g_Counts)
        {
            if (!c.Name) c.Name = name;
            if (c.Name == name)
            {
                const uint64_t n = ++c.Count;
                return g_LogLevel >= 2 || n <= 20 || n % 500 == 0;
            }
        }
        return g_LogLevel >= 2;
    }
    // Calls the export and logs it: Call<int64_t>("DLL_DuelComDoCommand", a, b, c, d)
    template <typename R, typename... A>
    R Call(const char* name, A... args)
    {
        const auto fn = Proc<R(*)(A...)>(name);
        if (!fn)
        {
            Logger::WriteLog(std::format("Master Duel engine: {} is not in duel.dll", name), MODULE_NAME, 2);
            return R{};
        }
        const R r = fn(args...);
        if (ShouldLog(name))
        {
            std::string text = std::format("duel.dll {}(", name);
            ((text += std::format("{}, ", static_cast<int64_t>(args))), ...);
            if constexpr (sizeof...(A) > 0) text.resize(text.size() - 2);
            Logger::WriteLog(std::format("{}) = {}", text, static_cast<int64_t>(r)), MODULE_NAME, 0);
        }
        return r;
    }

    // Dialog kinds (state +15508 = the game's 0x3800, and animation 55's sub-kind): Master Duel added kind 5, so its 6+ are the game's 5+.
    // Paired by the setters, which are in the same order in both engines: duel.dll 6 option list = the game's 5 (ShowOptionPrompt
    // 0x14005F340), 7 phase select (text 231, phase mask; sub_180592C60) = 6 (0x14005F720 -> DUEL_DIALOG_SELECT_PHASE), 8 type declaration
    // (text 241 / 355, type mask; sub_1805E2950) = 7 (0x1400F5EF0), 9 (a4 | a5 << 16) = 8 (0x14005F7E0) ... 15 = 14. Unchanged below 5;
    // duel.dll's 5 (set in sub_1805DCB30) has no game dialog and is passed on as it is. Untranslated, its phase select opened the game's
    // type declaration (the phase mask read as types: Fiend / Sea Serpent / Zombie).
    int64_t ToGameDialogKind(int64_t mdKind) { return mdKind > 5 ? mdKind - 1 : mdKind; }

    // List / zone / instance entries: the game packs id 14 | instance bit 0 at 14 | flags 15-22 | instance bits 1-9 at 23-31; duel.dll packs
    // id 16 | instance bit 0 at 16 | flags 17-23 | instance bits 1-8 at 24-31. Flags paired by DLL_DuelListGetItemAttribute vs
    // YGO::Duel::List_GetItemAttribute: game 0x8000 = duel.dll bit 17, 0x20000 = bit 18, 0x200000 = bit 22; the others keep their order.
    uint32_t ToGameEntry(uint32_t md)
    {
        const uint32_t id = md & 0xFFFF ? MasterDuel::ToDisplay(static_cast<uint16_t>(md & 0xFFFF)) : 0, inst = ((md >> 16) & 1) | ((md >> 24) << 1);
        uint32_t flags = 0;
        if (md & (1u << 17)) flags |= 0x8000;
        if (md & (1u << 18)) flags |= 0x20000;
        if (md & (1u << 19)) flags |= 0x40000;
        if (md & (1u << 20)) flags |= 0x80000;
        if (md & (1u << 21)) flags |= 0x100000;
        if (md & (1u << 22)) flags |= 0x200000;
        if (md & (1u << 23)) flags |= 0x400000;
        return (id & 0x3FFF) | (inst & 1) << 14 | flags | (inst >> 1) << 23;
    }

    // A global word in the game's terms: 0x3800 is the dialog kind (ToGameDialogKind); 0x3810-0x381C are the prompt's message words
    // (count at 0x380C; Duel_BuildPromptMessage 0x1407C4960), type in the top 4 bits - type 5 is a card id (its name), an engine id here.
    uint32_t ToGameGlobal(size_t offset, uint32_t value)
    {
        if (offset == 0x3800)
            return static_cast<uint32_t>(ToGameDialogKind(value));
        if (offset >= 0x3810 && offset < 0x3820 && (value >> 28) == 5 && (value & 0xFFFF))
            return (value & 0xF0000000) | MasterDuel::ToDisplay(static_cast<uint16_t>(value & 0xFFFF));
        return value;
    }

    void Mirror(bool forceDialog = false)
    {
        auto* state = reinterpret_cast<uint8_t*>(kState);
        const uint8_t* md = g_Work + kMdState;
        for (size_t p = 0; p < 2; ++p)
        {
            uint8_t* gp = state + p * kGamePlayer;
            const uint8_t* mp = md + p * kMdPlayer;
            memcpy(gp, mp, 4);                    // LP (both XOR the same key: Duel_DuelEngine.LpXorKey = config +0)
            memcpy(gp + 0x0C, mp + 0x0C, 20);     // the five pile counts, same offsets
            for (size_t z = 0; z < 13; ++z)
            {
                uint8_t* gz = gp + kGameZones + z * kGameZone;
                const uint8_t* mz = mp + kMdZones + z * kMdZone;
                memcpy(gz, mz, kGameZone);         // InPlay_Card: duel.dll's zone is it + 4 bytes
                *reinterpret_cast<uint32_t*>(gz) = ToGameEntry(*reinterpret_cast<const uint32_t*>(mz));
            }
            for (const List& l : kLists)
            {
                auto* g = reinterpret_cast<uint32_t*>(gp + l.Game);
                auto* m = reinterpret_cast<const uint32_t*>(mp + l.Md);
                const uint32_t n = (std::min)(*reinterpret_cast<const uint32_t*>(mp + l.CountOffset), static_cast<uint32_t>(l.Slots));
                for (uint32_t i = 0; i < l.Slots; ++i)
                    g[i] = i < n ? ToGameEntry(m[i]) : 0;
            }
        }
        for (size_t i = 0; i < kInstances; ++i)
        {
            auto* g = reinterpret_cast<uint32_t*>(state + kGameInstances + i * 8);
            auto* m = reinterpret_cast<const uint32_t*>(md + kMdInstances + i * 8);
            g[0] = ToGameEntry(m[0]);
            g[1] = m[1];
        }
        // Only the words duel.dll changed since the last mirror: the duel screen also writes some of these itself (its own dialogs - the
        // right-click phase menu puts its title and option texts in the prompt block 0x3800-0x3830), and a copy of duel.dll's stale values
        // every tick replaced the menu's options with an old prompt's (a monster-type pick). g_LastGlobals = duel.dll's values last time.
        // When duel.dll opens a dialog (animation 55, forceDialog) its whole prompt block is copied: the screen clears its copy of the
        // message after showing one, and duel.dll's next message often keeps the same word count (0x380C), so an on-change copy left the
        // count at 0 and the box came up empty (just OK).
        for (const Run& r : kGlobals)
            for (size_t o = r.Game; o + 4 <= r.End; o += 4)
            {
                const uint32_t now = *reinterpret_cast<const uint32_t*>(md + o + r.Shift);
                uint32_t& last = g_LastGlobals[(o - 0x3770) / 4];
                if (g_Picking && (o == 0x3800 || o == 0x3804))
                    continue;   // the name picker's dialog kind and owner (duel.dll's stay as they were, for after it)
                if (now != last || g_GlobalsReset || (forceDialog && o >= 0x3800 && o < 0x3834))
                {
                    *reinterpret_cast<uint32_t*>(state + o) = ToGameGlobal(o, now);
                    last = now;
                }
            }
        g_GlobalsReset = false;
        // Duel_UNK: the same 1340 bytes (0x53C, Engine_Init's memset) in both
        memcpy(reinterpret_cast<void*>(kUnk), g_Work + kMdUnk, kUnkBytes);
        // The list block (the game's 0x14349C5C0, 0x720 bytes - it ends where .data ends, 0x14349CCE0; duel.dll's is work +15824, 2424 bytes),
        // what the screen's list and selection screens read: kind +2, index +8, count +10, entries u32 x 300 from +24 (transcoded), then the
        // per-entry attribute word (from-zone bits): u16 x 300 at +1224 in the game, u32 x 300 at +1224 in duel.dll (its low word).
        if (!g_Picking)   // (the name picker's list while it is open)
        {
            auto* list = reinterpret_cast<uint8_t*>(kList);
            const uint8_t* mdList = g_Work + kMdList;
            memcpy(list, mdList, 24);
            const uint32_t n = (std::min)(static_cast<uint32_t>(*reinterpret_cast<const uint16_t*>(mdList + 10)), 300u);
            for (uint32_t i = 0; i < 300; ++i)
            {
                *reinterpret_cast<uint32_t*>(list + 24 + i * 4) = i < n ? ToGameEntry(*reinterpret_cast<const uint32_t*>(mdList + 24 + i * 4)) : 0;
                *reinterpret_cast<uint16_t*>(list + 1224 + i * 2) = i < n ? *reinterpret_cast<const uint16_t*>(mdList + 1224 + i * 4) : 0;
            }
        }
    }

    // ---- duel.dll's animation callbacks (DLL_SetEffectDelegate): the game's own UI entry points --------------------------------------
    using Anim_t = int64_t(__fastcall*)(int64_t, int64_t, uint32_t, int64_t);
    using Busy_t = int64_t(__fastcall*)(uint32_t);
    const auto Draw_DuelAnimationFromId = reinterpret_cast<Anim_t>(0x1407C1450);   // YGO::UI::Draw_DuelAnimationFromId
    const auto IsAnimBusy = reinterpret_cast<Busy_t>(0x1407EB7B0);                 // Duel__UI__IsAnimBusy

    int64_t __fastcall OnAnimation(int64_t code, int64_t a, uint32_t b, int64_t c)
    {
        if (code == 55)   // a dialog: a = its kind
            a = ToGameDialogKind(a);
        if (code == 55 ? g_LogLevel > 0 : ShouldLog("animation"))   // dialogs: every one
            Logger::WriteLog(std::format("duel.dll animation {} ({}, {}, {})", code, a, b, c), MODULE_NAME, 0);
        Mirror(code == 55);   // the animation reads the state as it is now; a dialog takes duel.dll's whole prompt block
        return Draw_DuelAnimationFromId(code, a, b, c);
    }
    // busy while the name picker is open or its answer is not sent yet: duel.dll's dialog wait (DuelDlg_WaitDialog 0x1805932C0) polls busy(55)
    int64_t __fastcall OnBusy(uint32_t anim) { return g_Picking ? 1 : IsAnimBusy(anim); }

    // ---- card-name declaration ------------------------------------------------------------------------------------------------------
    // duel.dll's check of a declared name (DuelDlg_IsDeclarableCardName 0x180598AF0: by its list block's kind, 0x91 = "a card in your Main
    // Deck" ...). Not exported: called only when its code is the build traced (build 80460 : 20260629), else the picker offers the duel's
    // cards unchecked and duel.dll's own check after the answer still applies.
    using IsDeclarable_t = int(__fastcall*)(int);
    constexpr uintptr_t kIsDeclarableRva = 0x598AF0;
    constexpr uint8_t kIsDeclarableCode[56] = {
        0x48, 0x89, 0x5C, 0x24, 0x08, 0x48, 0x89, 0x6C, 0x24, 0x10, 0x48, 0x89, 0x74, 0x24, 0x18, 0x57, 0x41, 0x54, 0x41, 0x55, 0x41, 0x56, 0x41,
        0x57, 0x48, 0x83, 0xEC, 0x40, 0x48, 0x8B, 0x35, 0x1D, 0x11, 0xC6, 0x00, 0x8B, 0xD9, 0x0F, 0xBF, 0x46, 0x02, 0x05, 0x74, 0xFF, 0xFF, 0xFF,
        0x83, 0xF8, 0x0A, 0x0F, 0x87, 0x86, 0x06, 0x00, 0x00, 0x4C };

    IsDeclarable_t IsDeclarableFn()
    {
        static IsDeclarable_t fn = nullptr;
        static bool checked = false;
        if (checked)
            return fn;
        checked = true;
        HMODULE module = nullptr;
        if (const FARPROC any = MasterDuel::Proc("DLL_DuelSysAct");
            any && GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                reinterpret_cast<LPCSTR>(any), &module))
        {
            const auto* code = reinterpret_cast<const uint8_t*>(module) + kIsDeclarableRva;
            if (memcmp(code, kIsDeclarableCode, sizeof kIsDeclarableCode) == 0)
                fn = reinterpret_cast<IsDeclarable_t>(const_cast<uint8_t*>(code));
        }
        Logger::WriteLog(fn ? "Master Duel engine: card-name check found (the name picker lists only legal names)"
                            : "Master Duel engine: card-name check not found in this duel.dll build: the name picker lists the duel's cards unchecked",
            MODULE_NAME, fn ? 0 : 1);
        return fn;
    }

    // 1 legal, 0 not, -1 it faulted (no C++ objects here: __try)
    int CallIsDeclarable(IsDeclarable_t fn, int id)
    {
        __try { return fn(id) ? 1 : 0; }
        __except (EXCEPTION_EXECUTE_HANDLER) { return -1; }
    }

    // Opens the game's card list picker (as UI_BeginCardListSelect 0x14005DE00 does for a human player: list block +0 side, +2 mode, entries,
    // dialog kind 0x3800 = mode + 19, owner 0x3804, animation 0x38) with the names the player may declare: the duel's own cards first, then
    // every other card the engine has, up to the list's 300.
    void OpenNamePicker(int player)
    {
        const IsDeclarable_t check = IsDeclarableFn();
        std::vector<uint16_t> pool;
        std::unordered_set<uint16_t> seen;
        for (size_t i = 0; i < kInstances; ++i)   // every card in this duel (duel.dll's instance table, id in the low word)
            if (const uint16_t id = static_cast<uint16_t>(*reinterpret_cast<const uint32_t*>(g_Work + kMdState + kMdInstances + i * 8) & 0xFFFF);
                id && seen.insert(id).second)
                pool.push_back(id);
        if (check)
            for (uint16_t id : MasterDuel::EngineIds())
                if (seen.insert(id).second)
                    pool.push_back(id);

        g_PickEngineIds.clear();
        bool faulted = false;
        for (uint16_t id : pool)
        {
            if (g_PickEngineIds.size() >= 300)
                break;
            const uint16_t display = MasterDuel::ToDisplay(id);
            if (!display || display > 0x3FFF)
                continue;   // nothing the screen can show it as
            if (check && !faulted)
            {
                const int legal = CallIsDeclarable(check, id);
                if (legal < 0)
                    faulted = true;
                else if (!legal)
                    continue;
            }
            g_PickEngineIds.push_back(id);
        }
        if (faulted)
            Logger::WriteLog("Master Duel engine: the card-name check faulted; the name picker lists the duel's cards unchecked", MODULE_NAME, 2);

        auto* list = reinterpret_cast<uint8_t*>(kList);
        memset(list, 0, 0x720);
        constexpr uint16_t kMode = 6;   // the game's "pick a card from a list" mode (Slot0_NameAndSelectCardThenActivate uses 6 too)
        *reinterpret_cast<uint16_t*>(list + 0) = static_cast<uint16_t>(player & 1);
        *reinterpret_cast<uint16_t*>(list + 2) = kMode;
        *reinterpret_cast<uint16_t*>(list + 10) = static_cast<uint16_t>(g_PickEngineIds.size());
        for (size_t i = 0; i < g_PickEngineIds.size(); ++i)
            *reinterpret_cast<uint32_t*>(list + 24 + i * 4) = MasterDuel::ToDisplay(g_PickEngineIds[i]) & 0x3FFF;
        auto* state = reinterpret_cast<uint8_t*>(kState);
        *reinterpret_cast<uint32_t*>(state + 0x3800) = kMode + 19;
        *reinterpret_cast<uint32_t*>(state + 0x3804) = static_cast<uint32_t>(player & 1);
        g_PickIndex = -1;
        g_PickDone = false;
        g_Picking = true;
        Logger::WriteLog(std::format("Master Duel engine: declare a card name (list kind {:#x}): {} legal name(s) offered",
            *reinterpret_cast<const uint16_t*>(g_Work + kMdList + 2), g_PickEngineIds.size()), MODULE_NAME, 0);
        if (g_PickEngineIds.empty())
        {
            // nothing to offer: answer "none" at once (duel.dll decides what that means) instead of an empty picker
            g_PickResult = 0;
            g_PickDone = true;
            return;
        }
        Draw_DuelAnimationFromId(0x38, player & 1, kMode, 0);
    }

    // ---- duel start --------------------------------------------------------------------------------------------------------------
    using Void_t = uint64_t(__fastcall*)();
    Void_t orig_InitEngine = reinterpret_cast<Void_t>(0x14005FD90);   // YGO::DUEL::DuelSetup_InitEngine

    bool Eligible()
    {
        const auto isMp = reinterpret_cast<int(*)()>(0x1407691D0);     // YGO::DUEL::Get_IsDuelMultiplayer
        const auto isTag = reinterpret_cast<bool(*)()>(0x1407694D0);   // YGO::DUEL::Get_IsTagDuel
        const auto isTutorial = reinterpret_cast<int(*)()>(0x1407694F0);
        return !isMp() && !isTag() && !isTutorial();
    }

    // 1 = started on duel.dll, 0 = it faulted (no C++ objects here: __try)
    int StartOnEngine(int type)
    {
        __try
        {
            g_GlobalsReset = true;
            // (Duel_UNK is not copied in: DLL_DuelSysInitCustom clears duel.dll's copy and fills it; Mirror copies it back)
            memcpy(g_Work + kMdConfig, reinterpret_cast<const void*>(kEngine), kConfigBytes);
            // the deck blocks (DLL_DuelSysSetDeck2's layout: +44 + 192 x player: main / Extra / side counts at +0 / +4 / +8, u16 ids from +12
            // (60 main, then 15 Extra at +132, 15 side at +162)) in engine ids
            for (size_t p = 0; p < 4; ++p)
            {
                uint8_t* block = g_Work + kMdConfig + 44 + p * 192;
                const uint32_t counts[3] = { *reinterpret_cast<uint32_t*>(block), *reinterpret_cast<uint32_t*>(block + 4), *reinterpret_cast<uint32_t*>(block + 8) };
                const size_t at[3] = { 12, 132, 162 }, max[3] = { 60, 15, 15 };
                for (int k = 0; k < 3; ++k)
                    for (uint32_t i = 0; i < counts[k] && i < max[k]; ++i)
                    {
                        auto* id = reinterpret_cast<uint16_t*>(block + at[k] + i * 2);
                        *id = MasterDuel::DeckToEngine(*id);
                    }
            }
            // The Main Decks in the order the game's own setup left them: it has shuffled them already (Duel_ShuffleDeck, with anything a
            // plugin did there - Funky's test hands are stacked on top), so duel.dll loads that order (Duel_LoadDeck: block id i -> deck
            // index i, index 0 is the top in both engines) and does not shuffle again. A deck past the block's 60 ids keeps the shuffle.
            bool keepOrder = true;
            for (size_t p = 0; p < 2; ++p)
                keepOrder &= *reinterpret_cast<const uint32_t*>(kState + p * kGamePlayer + 0x10) <= 60;
            if (keepOrder)
                for (size_t p = 0; p < 2; ++p)
                {
                    uint8_t* block = g_Work + kMdConfig + 44 + p * 192;
                    const uint8_t* player = reinterpret_cast<const uint8_t*>(kState + p * kGamePlayer);
                    const uint32_t n = *reinterpret_cast<const uint32_t*>(player + 0x10);
                    *reinterpret_cast<uint32_t*>(block) = n;
                    for (uint32_t i = 0; i < n; ++i)
                        reinterpret_cast<uint16_t*>(block + 12)[i] =
                            MasterDuel::DeckToEngine(static_cast<uint16_t>(reinterpret_cast<const uint32_t*>(player + 0x37C)[i] & 0x3FFF));
                }
            Proc<void(*)(void*, void*)>("DLL_SetEffectDelegate")(reinterpret_cast<void*>(&OnAnimation), reinterpret_cast<void*>(&OnBusy));
            // (duel type = the game's DuelFormat field, flag, LP 0 = the type's default, hand -1 = default, 1 = don't shuffle)
            Proc<int64_t(*)(int64_t, int, int, int, int, int, int)>("DLL_DuelSysInitCustom")(type, 0, 0, 0, -1, -1, keepOrder ? 1 : 0);
            Mirror();
            return 1;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return 0;
        }
    }

    uint64_t __fastcall Hook_InitEngine()
    {
        g_Active = false;
        g_Picking = g_PickDone = false;
        if (g_Failed || !MasterDuel::Ready() || !Eligible())
            return orig_InitEngine();
        // the game's own setup first: it loads the decks (Yu-Gi-Oh-MoreCards borrows its duel ids then and tells Core, SetDisplayId) and
        // sets what the duel screen needs; then duel.dll starts the same duel and its state replaces the game's
        MasterDuel::ClearDisplayIds();
        const uint64_t result = orig_InitEngine();
        const int type = *reinterpret_cast<const int*>(kState + 0x37A4);   // the game's DuelFormat field (0x14349B3E4)
        // the game's own duel state as its setup left it, put back if duel.dll's start faults part way through the mirror
        static uint8_t savedState[0x3930], savedUnk[kUnkBytes], savedList[0x720];
        memcpy(savedState, reinterpret_cast<const void*>(kState), sizeof savedState);
        memcpy(savedUnk, reinterpret_cast<const void*>(kUnk), sizeof savedUnk);
        memcpy(savedList, reinterpret_cast<const void*>(kList), sizeof savedList);
        if (!StartOnEngine(type))
        {
            memcpy(reinterpret_cast<void*>(kState), savedState, sizeof savedState);
            memcpy(reinterpret_cast<void*>(kUnk), savedUnk, sizeof savedUnk);
            memcpy(reinterpret_cast<void*>(kList), savedList, sizeof savedList);
            g_Failed = true;
            Logger::WriteLog("Master Duel engine: duel start faulted, the game's engine plays this duel", MODULE_NAME, 2);
            return result;
        }
        g_Active = true;
        Logger::WriteLog(std::format("Master Duel engine: this duel runs on duel.dll (type {})", type), MODULE_NAME, 0);
        return result;
    }

    // ---- the tick -----------------------------------------------------------------------------------------------------------------
    Void_t orig_MasterTick = reinterpret_cast<Void_t>(0x14003EAC0);   // Duel__MasterTick_GatedByNetEvent

    // 0 = stepped, 1 = the duel is over, -1 = it faulted (no C++ objects here: __try)
    int StepEngine()
    {
        __try
        {
            if (Proc<int(*)()>("DLL_DuelGetDuelFinish")())
            {
                // the duel is over: result / finish words (config +816 / +820 = Duel_DuelEngine field_330 / +0x334)
                memcpy(reinterpret_cast<void*>(kEngine + 816), g_Work + kMdConfig + 816, 8);
                Mirror();
                return 1;
            }
            Proc<int64_t(*)()>("DLL_DuelSysAct")();
            Mirror();
            return 0;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return -1;
        }
    }

    uint64_t __fastcall Hook_MasterTick()
    {
        if (!g_Active)
            return orig_MasterTick();
        if (g_PickDone)
        {
            // the name picker's answer (an engine id, 0 = none); duel.dll checks it itself once busy(55) drops
            Call<int64_t>("DLL_DuelDlgSetResult", static_cast<int>(g_PickResult));
            g_PickDone = false;
            g_Picking = false;
            g_GlobalsReset = true;   // the dialog fields and list block back to duel.dll's
        }
        const int r = StepEngine();
        // duel.dll asks for a card name (dialog kind 20 + list kind 0x8C-0x96): the game has no such dialog, so the bridge's picker
        if (const uint32_t kind = *reinterpret_cast<const uint32_t*>(g_Work + 15508); r == 0 && !g_Picking && kind >= 20 + 0x8C && kind <= 20 + 0x96)
            OpenNamePicker(static_cast<int>(*reinterpret_cast<const uint32_t*>(g_Work + 15512)));
        if (g_LogLevel > 0)
        {
            // turn, phase, input mode, asked player (duel.dll state +15380 / +15388 / +15568 / +15616), logged when one changes
            static uint32_t last[4] = { ~0u, ~0u, ~0u, ~0u };
            const uint32_t now[4] = { *reinterpret_cast<uint32_t*>(g_Work + 15380), *reinterpret_cast<uint32_t*>(g_Work + 15388),
                *reinterpret_cast<uint32_t*>(g_Work + 15568), *reinterpret_cast<uint32_t*>(g_Work + 15616) };
            if (memcmp(now, last, sizeof now) != 0)
            {
                memcpy(last, now, sizeof now);
                Logger::WriteLog(std::format("duel.dll state: turn {}, phase {}, input mode {}, asked player {}", now[0], now[1], now[2], now[3]), MODULE_NAME, 0);
            }
            // the dialog duel.dll is showing: kind (+15508 = game 0x3800), mix count / words (+15520 / +15524.., DLL_DuelDlgGetMix*),
            // select items count / texts (+15540 / +15544.., DLL_DuelDlgGetSelectItem*)
            static uint32_t lastDlg[12] = {};
            uint32_t dlg[12];
            dlg[0] = *reinterpret_cast<uint32_t*>(g_Work + 15508);
            for (int i = 0; i < 4; ++i) dlg[1 + i] = *reinterpret_cast<uint32_t*>(g_Work + 15520 + 4 * i);
            for (int i = 0; i < 7; ++i) dlg[5 + i] = *reinterpret_cast<uint32_t*>(g_Work + 15540 + 4 * i);
            if (memcmp(dlg, lastDlg, sizeof dlg) != 0)
            {
                memcpy(lastDlg, dlg, sizeof dlg);
                Logger::WriteLog(std::format("duel.dll dialog: kind {}, mix {} [{:#x} {:#x} {:#x}], select items {} [{:#x} {:#x} {:#x} {:#x} {:#x} {:#x}]",
                    dlg[0], dlg[1], dlg[2], dlg[3], dlg[4], dlg[5], dlg[6], dlg[7], dlg[8], dlg[9], dlg[10], dlg[11]), MODULE_NAME, 0);
            }
        }
        if (r == 0)
            return 0;
        g_Active = false;
        if (r < 0)
        {
            g_Failed = true;
            Logger::WriteLog("Master Duel engine: DLL_DuelSysAct faulted; the bridge is off until the game restarts", MODULE_NAME, 2);
        }
        else
            Logger::WriteLog(std::format("Master Duel engine: duel finished (result {}, finish {})", Proc<int(*)()>("DLL_DuelGetDuelResult")(),
                Proc<int(*)()>("DLL_DuelGetDuelFinish")()), MODULE_NAME, 0);
        return 1;
    }

    // ---- the duel screen's engine calls (docs/MasterDuelEngine.md pairing table) ---------------------------------------------------
    using CmdMask_t = int64_t(__fastcall*)(int, int, int);
    CmdMask_t orig_Pick_GetCardFlags = reinterpret_cast<CmdMask_t>(0x1400632D0);
    int64_t __fastcall Hook_Pick_GetCardFlags(int side, int zone, int index)
    {
        if (!g_Active) return orig_Pick_GetCardFlags(side, zone, index);
        return Call<int64_t>("DLL_DuelComGetCommandMask", side, zone, index);
    }

    using Submit_t = int64_t(__fastcall*)(int, int, int, int);
    Submit_t orig_SubmitPick = reinterpret_cast<Submit_t>(0x140063840);
    int64_t __fastcall Hook_SubmitPick(int side, int zone, int index, int command)
    {
        if (!g_Active) return orig_SubmitPick(side, zone, index, command);
        Logger::WriteLog(std::format("Master Duel engine: command {} on player {} zone {} index {}", command, side, zone, index), MODULE_NAME, 0);
        return Call<int64_t>("DLL_DuelComDoCommand", side, zone, index, command);
    }

    Void_t orig_DefaultLocation = reinterpret_cast<Void_t>(0x140063C40);
    uint64_t __fastcall Hook_DefaultLocation()
    {
        if (!g_Active) return orig_DefaultLocation();
        return Call<int64_t>("DLL_DuelComDefaultLocation");
    }

    // Phase menu and back / cancel (the screen calls them through its thunks 0x1407EB560 / 0x1407EB4B0 / 0x1407EB4A0):
    // 0x140063D00(phase) = DLL_DuelComMovePhase (asked player = turn player, command phase + 14), 0x140063AE0() = DLL_DuelComCancelCommand,
    // 0x140063B90(flag) = DLL_DuelComCancelCommand2 (same code)
    using MovePhase_t = int64_t(__fastcall*)(int);
    MovePhase_t orig_MovePhase = reinterpret_cast<MovePhase_t>(0x140063D00);
    int64_t __fastcall Hook_MovePhase(int phase)
    {
        if (!g_Active) return orig_MovePhase(phase);
        Logger::WriteLog(std::format("Master Duel engine: move to phase {}", phase), MODULE_NAME, 0);
        return Call<int64_t>("DLL_DuelComMovePhase", phase);
    }
    using Cancel_t = int64_t(__fastcall*)();
    Cancel_t orig_Cancel = reinterpret_cast<Cancel_t>(0x140063AE0);
    int64_t __fastcall Hook_Cancel()
    {
        if (!g_Active) return orig_Cancel();
        return Call<int64_t>("DLL_DuelComCancelCommand");
    }
    MovePhase_t orig_Cancel2 = reinterpret_cast<MovePhase_t>(0x140063B90);
    int64_t __fastcall Hook_Cancel2(int flag)
    {
        if (!g_Active) return orig_Cancel2(flag);
        return Call<int64_t>("DLL_DuelComCancelCommand2", flag);
    }

    using Flags_t = uint32_t(__fastcall*)();
    Flags_t orig_PhaseMenu = reinterpret_cast<Flags_t>(0x140063C70);
    uint32_t __fastcall Hook_PhaseMenu()
    {
        if (!g_Active) return orig_PhaseMenu();
        return static_cast<uint32_t>(Call<int64_t>("DLL_DuelComGetMovablePhase"));
    }

    using ListInt_t = int64_t(__fastcall*)(int);
    ListInt_t orig_ListAttr = reinterpret_cast<ListInt_t>(0x14005E390);
    ListInt_t orig_ListId = reinterpret_cast<ListInt_t>(0x14005E180);
    ListInt_t orig_ListInstance = reinterpret_cast<ListInt_t>(0x14005E1B0);
    // The duel screen's own setters (its UI -> engine thunks): the prompt answer (thunk 0x1407EB9B0: mov [state+0x3808], ecx = duel.dll's
    // DLL_DuelDlgSetResult, state +15516) and the chosen list entry (0x14005E010, list block +8 = DLL_DuelListSetIndex). The game's field
    // is written as before; duel.dll gets the same answer.
    using SetInt_t = void(__fastcall*)(int);
    SetInt_t orig_SetPromptAnswer = reinterpret_cast<SetInt_t>(0x1407EB9B0);
    SetInt_t orig_ListSetIndex = reinterpret_cast<SetInt_t>(0x14005E010);
    void __fastcall Hook_SetPromptAnswer(int answer)
    {
        orig_SetPromptAnswer(answer);
        if (g_Active && g_Picking && !g_PickDone)
        {
            // the name picker closed: its picked entry (ListSetIndex came first), or none (cancelled)
            g_PickResult = answer > 0 && g_PickIndex >= 0 && static_cast<size_t>(g_PickIndex) < g_PickEngineIds.size() ? g_PickEngineIds[g_PickIndex] : 0;
            g_PickDone = true;
            Logger::WriteLog(std::format("Master Duel engine: card name declared: {}", g_PickResult), MODULE_NAME, 0);
            return;
        }
        if (g_Active && !g_Picking) Call<int64_t>("DLL_DuelDlgSetResult", answer);
    }
    void __fastcall Hook_ListSetIndex(int index)
    {
        orig_ListSetIndex(index);
        if (g_Active && g_Picking)
            g_PickIndex = index;   // the name picker's list, not duel.dll's
        else if (g_Active)
            Call<int64_t>("DLL_DuelListSetIndex", static_cast<unsigned>(index));
    }

    // (while the name picker is open the screen reads its list, the game's own block)
    int64_t __fastcall Hook_ListAttr(int i) { return g_Active && !g_Picking ? Call<int64_t>("DLL_DuelListGetItemAttribute", i) : orig_ListAttr(i); }
    // duel.dll answers with its own card id (a new card's Konami id, e.g. Albion 15994): the screen wants the id the card has in this duel
    // (MoreCards' borrowed id), or a list of new cards came up blank (no art, 0 ATK / DEF, Level x0) while the info panel was right.
    int64_t __fastcall Hook_ListId(int i)
    {
        if (!g_Active || g_Picking)
            return orig_ListId(i);
        const int64_t id = Call<int64_t>("DLL_DuelListGetItemID", i);
        return id > 0 && id <= 0xFFFF ? MasterDuel::ToDisplay(static_cast<uint16_t>(id)) & 0x3FFF : id;
    }
    int64_t __fastcall Hook_ListInstance(int i) { return g_Active && !g_Picking ? Call<int64_t>("DLL_DuelListGetItemUniqueID", i) : orig_ListInstance(i); }
}

bool MasterDuelBridge::Install(void* work)
{
    g_Work = static_cast<uint8_t*>(work);
    if (!g_Work)
        return false;
    g_LogLevel = std::atoi(Loading::Setting("MasterDuelLog", "0").c_str());
    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_InitEngine, Hook_InitEngine);
    DetourAttach(&(PVOID&)orig_MasterTick, Hook_MasterTick);
    DetourAttach(&(PVOID&)orig_Pick_GetCardFlags, Hook_Pick_GetCardFlags);
    DetourAttach(&(PVOID&)orig_SubmitPick, Hook_SubmitPick);
    DetourAttach(&(PVOID&)orig_DefaultLocation, Hook_DefaultLocation);
    DetourAttach(&(PVOID&)orig_PhaseMenu, Hook_PhaseMenu);
    DetourAttach(&(PVOID&)orig_ListAttr, Hook_ListAttr);
    DetourAttach(&(PVOID&)orig_ListId, Hook_ListId);
    DetourAttach(&(PVOID&)orig_ListInstance, Hook_ListInstance);
    DetourAttach(&(PVOID&)orig_SetPromptAnswer, Hook_SetPromptAnswer);
    DetourAttach(&(PVOID&)orig_MovePhase, Hook_MovePhase);
    DetourAttach(&(PVOID&)orig_Cancel, Hook_Cancel);
    DetourAttach(&(PVOID&)orig_Cancel2, Hook_Cancel2);
    DetourAttach(&(PVOID&)orig_ListSetIndex, Hook_ListSetIndex);
    const LONG error = DetourTransactionCommit();
    Logger::WriteLog(std::format("Master Duel engine: duel redirect hooks {}", error), MODULE_NAME, error ? 2 : 0);
    return error == NO_ERROR;
}

bool MasterDuelBridge::Active() { return g_Active; }
