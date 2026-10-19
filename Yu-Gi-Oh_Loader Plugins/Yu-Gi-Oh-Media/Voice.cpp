#define NOMINMAX
#include <Windows.h>
#include <detours.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <charconv>
#include <format>
#include <fstream>
#include <map>
#include <mutex>
#include <optional>
#include <random>
#include <string>
#include <vector>

#include <json.hpp>

#include "Common.h"
#include "Logger.h"
#include "Player.h"
#include "Music.h"
#include "Video.h"
#include "Voice.h"
#include "Trigger.h"
#include "YuGiOh/YuGiOh-DUELSTATE.h"
#include "Yu-Gi-Oh-Mods.h"
#include "YuGiOh/YuGiOh-Addresses.h"

namespace Voice
{
    namespace
    {
        namespace DS = YGO::DUELSTATE;
        using namespace Music;   // Common.h: game addresses and path helpers
        using namespace Trigger;   // events, conditions and their names (shared with videos.json "duel")

        // Engine message codes (DuelMsgCode, docs/MultiplayerSystem.md). `side` in a message is the side it is about.
        constexpr uint32_t kMsgDuelResult = 0x05;      // a1: 1 = the local side won, 2 = the other side, 3 = draw; a2/a3 = win reasons
        constexpr uint32_t kMsgPhaseDraw = 0x0A;       // every turn starts with it (the engine sends no TurnStart)
        constexpr uint32_t kMsgPhaseBattle = 0x0D;
        constexpr uint32_t kMsgAttack = 0x17;          // named Anim0C_Summon in the IDB, but seen live at an attack: a1 = attacker's zone
        constexpr uint32_t kMsgLpDamage = 0x24;        // side = the side that takes it, a1 = amount (LP not applied yet)
        constexpr uint32_t kMsgNormalSummon = 0x2B;    // seen live after a Normal Summon: a1 = card, a2 = zone
        constexpr uint32_t kMsgHandToField = 0x51;     // a1 = card, a2: bit 0 = side, bits 1-5 = zone, 0x4000 = a monster zone
        constexpr uint32_t kMsgHighWordPrefix = 0x6D;  // carries the upper 16 bits of the next message's args
        constexpr uint32_t kMsgActivate = 0x2D;        // seen live (Duels.log): a1 low byte = the card's zone, a2 = card, a3 = chain link id
        constexpr uint32_t kMsgChainResolve = 0x11;    // ChainLinkResolve: a2 = the link id its 0x2D gave
        constexpr int kSpellTrapZones = 6;             // 5 Spell & Trap Zones + the Field Zone (zones 7-12)
        constexpr uintptr_t kEventFlags = 0x14349C570; // g_EventRecord +0x10: 1 = summoned properly (Fusion/Synchro/...), 0x40 = Pendulum Summon

        constexpr int kMonsterZones = 7;               // 5 Main + 2 Extra Monster Zones (YGO::DUEL::Count_OccupiedMonsterZones reads 7)
        constexpr uintptr_t kZoneFlags = 0x14;         // Duel::InPlay_Card.UNK_03: 0x20 = not counted as on the field (yet)
        constexpr uintptr_t kNetDuel = DS::PlayerState + 0x37B0;      // u32: nonzero in an online duel
        constexpr uintptr_t kQueueStage = DS::PlayerState + 0x388A;   // u32: 0 = PumpAndMirror pops the next message this call

        struct Line : Conditions
        {
            std::vector<std::wstring> Files;
            float Volume = 1.0f;

            // Runtime: files are dealt from a shuffled bag so none repeats until all have played.
            Trigger::Bag Bag;
            bool Used = false;
            std::array<bool, 2> Held{};       // inHand: the hand held the cards at the last check (by speaker side)
        };

        struct Config
        {
            float Volume = 1.0f;
            float Duck = 0.5f;                // the plugin's music level while a line plays
            uint32_t GapMs = 3000;            // quiet time after a line before the next (duel start and end don't wait)
            std::map<int, std::vector<Line>> Characters;
        };

        struct Request
        {
            std::wstring Path;
            float Volume = 1.0f;
        };

        enum class Outcome { Blocked, Silent, Said };

        std::mutex g_ConfigLock;
        Config g_Config;
        std::atomic<float> g_Volume{ 1.0f };
        std::atomic<bool> g_HasHandLines{ false };
        std::mt19937 g_Random{ std::random_device{}() };

        std::mutex g_PendingLock;
        std::optional<Request> g_Pending;
        std::optional<Request> g_PendingAlongside;   // said at the same time as g_Pending (both duelists' duel-start lines)
        std::atomic<bool> g_Busy{ false };            // a line is pending or playing
        std::atomic<uint64_t> g_QuietUntil{ 0 };

        std::atomic<bool> g_InDuel{ false };
        std::atomic<bool> g_DuelStartPending{ false };

        // The field as of the last popped message (game thread only).
        struct Slot
        {
            bool Present = false;
            bool FaceUp = false;
            uint32_t Instance = 0;
            uint16_t Card = 0;
        };
        std::array<std::array<Slot, kMonsterZones>, 2> g_Field{};
        std::array<std::array<Slot, kSpellTrapZones>, 2> g_Back{};   // Spell & Trap Zones + Field Zone
        std::array<uint32_t, 2> g_GraveCount{};
        std::vector<uint32_t> g_OpenLinks;            // chain link ids activated and not resolved yet
        std::vector<uint32_t> g_NormalSummoned;       // card instances 0x2B announced this duel
        struct HandMove { int Side = -1; int Zone = -1; int Card = 0; };
        HandMove g_LastHandMove;
        // A card that came from the hand to a monster zone: its 0x2B (Normal Summon) may follow a few messages later.
        struct Deferred { int Side; int Zone; uint32_t Instance; uint16_t Card; int PopsLeft; };
        std::optional<Deferred> g_Deferred;

        using EngineInit_t = uint64_t(__fastcall*)();
        using PumpAndMirror_t = uint64_t(__fastcall*)();
        EngineInit_t orig_EngineInit = reinterpret_cast<EngineInit_t>(YGO::Addr::Duel_Engine_Init);          // YGO::DUEL::Engine_Init
        PumpAndMirror_t orig_PumpAndMirror = reinterpret_cast<PumpAndMirror_t>(YGO::Addr::Duel_MsgQueue_PumpAndMirror);  // Duel__MsgQueue__PumpAndMirror
        // Not Duel__MsgQueue__Push: LTCG callers keep values in rdx/r8/r9 across it, a C++ detour breaks the duel (docs/DuelIt.md).

        int LocalSide()
        {
            return reinterpret_cast<int(__fastcall*)()>(YGO::Addr::Duel_Get_LocalPlayerSeat)() & 1;   // YGO::DUEL::Get_LocalPlayerSeat
        }

        bool AnimBusy()
        {
            return reinterpret_cast<int(__fastcall*)(uint32_t)>(0x1407EB7B0)(0) != 0;   // Duel__UI__IsAnimBusy(0)
        }

        // g_DuelSideCharacters is [0] the player, [1] the opponent; duel sides are relative to the local seat.
        int CharacterOf(int side, int localSide)
        {
            return g_DuelSideCharacters[side == localSide ? 0 : 1];
        }

        std::optional<int> IntKey(const std::string& key)
        {
            int number = 0;
            auto [end, error] = std::from_chars(key.data(), key.data() + key.size(), number);
            return error == std::errc() && end == key.data() + key.size() ? std::optional<int>(number) : std::nullopt;
        }

        void Load()
        {
            Config config;
            // Every mod's voices.json and the game folder's, lowest priority first (Yu-Gi-Oh-Mods.h): their lines add up; volume, duck and
            // gap come from the last copy that sets them.
            for (const auto& voicesPath : YGO::Mods::Files("voices.json"))
            {
                std::ifstream file(voicesPath);
                const std::wstring content = voicesPath.parent_path().wstring() + L"\\";   // file names are relative to this copy's folder
                try
                {
                    auto root = nlohmann::json::parse(file, nullptr, true, true);
                    std::wstring folder = content + L"voices\\";
                    if (auto it = root.find("folder"); it != root.end() && it->is_string())
                    {
                        folder = ResolvePath(content, it->get<std::string>());
                        if (!folder.empty() && folder.back() != L'\\')
                            folder += L'\\';
                    }
                    if (root.contains("volume"))
                        config.Volume = std::clamp(Float(root, "volume", 1.0f), 0.0f, 2.0f);
                    if (root.contains("duck"))
                        config.Duck = std::clamp(Float(root, "duck", 0.5f), 0.0f, 1.0f);
                    if (root.contains("gap"))
                        config.GapMs = static_cast<uint32_t>(std::max(0.0f, Float(root, "gap", 3.0f)) * 1000.0f);

                    int lines = 0;
                    if (auto characters = root.find("characters"); characters != root.end() && characters->is_object())
                    {
                        for (auto& [id, list] : characters->items())
                        {
                            auto character = IntKey(id);
                            if (!character || !list.is_array())
                            {
                                Logger::Log(std::format("voices.json: character \"{}\" skipped (needs a number and a list of lines)", id), MODULE_NAME, 1);
                                continue;
                            }
                            for (auto& entry : list)
                            {
                                if (!entry.is_object())
                                    continue;
                                auto conditions = Parse(entry);
                                if (!conditions)
                                {
                                    Logger::Log(std::format("voices.json: character {}: a line with an unknown \"when\" skipped", id), MODULE_NAME, 1);
                                    continue;
                                }
                                Line line;
                                static_cast<Conditions&>(line) = std::move(*conditions);
                                line.Volume = Float(entry, "volume", 1.0f);
                                if (line.Event == When::InHand && line.Cards.empty())
                                {
                                    Logger::Log(std::format("voices.json: character {}: an inHand line without cards skipped", id), MODULE_NAME, 1);
                                    continue;
                                }

                                std::vector<std::string> names;
                                if (auto files = entry.find("files"); files != entry.end() && files->is_array())
                                    for (auto& name : *files)
                                        if (name.is_string())
                                            names.push_back(name.get<std::string>());
                                if (auto one = entry.find("file"); one != entry.end() && one->is_string())
                                    names.push_back(one->get<std::string>());
                                for (auto& name : names)
                                {
                                    std::wstring path = ResolvePath(folder, name);
                                    if (GetFileAttributesW(path.c_str()) == INVALID_FILE_ATTRIBUTES)
                                        Logger::Log(std::format("voices.json: character {} -> {} is missing", id, Narrow(path)), MODULE_NAME, 1);
                                    else
                                        line.Files.push_back(std::move(path));
                                }
                                if (line.Files.empty())
                                    continue;
                                config.Characters[*character].push_back(std::move(line));
                                ++lines;
                            }
                        }
                    }
                    Logger::Log(std::format("{}: {} line(s), {} character(s) have lines so far", Narrow(voicesPath.wstring()), lines,
                                            config.Characters.size()), MODULE_NAME, 0);
                }
                catch (const std::exception& e)
                {
                    Logger::Log(std::format("{} is not valid JSON ({}): its voice lines are left out", Narrow(voicesPath.wstring()), e.what()), MODULE_NAME, 2);
                }
            }

            bool handLines = false;
            for (auto& [character, lines] : config.Characters)
                for (auto& line : lines)
                    handLines |= line.Event == When::InHand;
            g_HasHandLines = handLines;
            g_Volume = config.Volume;
            std::lock_guard lock(g_ConfigLock);
            g_Config = std::move(config);
        }

        // The next file from the line's bag: every file plays once before any repeats, and a new round never starts with the
        // file that just played.
        const std::wstring& DealFile(Line& line)
        {
            return line.Files[line.Bag.Deal(line.Files.size(), g_Random)];
        }

        struct Candidate
        {
            Line* Chosen;
            int Character;
        };

        bool Roll(const Line& line)
        {
            return Trigger::Roll(line, g_Random);
        }

        // Collects the `lineEvent` lines `speakerSide`'s character has, said about itself or about the other duelist.
        void Collect(std::vector<Candidate>& out, const Happening& h, When lineEvent, int speakerSide, bool aboutOpponent, int localSide)
        {
            const int character = CharacterOf(speakerSide, localSide);
            const int other = CharacterOf(1 - speakerSide, localSide);
            auto it = g_Config.Characters.find(character);
            if (it == g_Config.Characters.end())
                return;
            for (Line& line : it->second)
            {
                if (line.Event != lineEvent || line.AboutOpponent != aboutOpponent || (line.Once && line.Used))
                    continue;
                if (!Matches(line, h, lineEvent, speakerSide, other) || !Roll(line))
                    continue;
                out.push_back({ &line, character });
            }
        }

        Request Take(Candidate& pick)
        {
            pick.Chosen->Used = true;
            Request request{ DealFile(*pick.Chosen), pick.Chosen->Volume };
            Logger::Log(std::format("Voice: character {} says {} ({}{})", pick.Character, Narrow(request.Path),
                pick.Chosen->AboutOpponent ? "opponent " : "", kWhenNames[static_cast<size_t>(pick.Chosen->Event)]), MODULE_NAME, 0);
            return request;
        }

        // Picks one of the candidates and hands it to the watcher thread. Call with g_ConfigLock held.
        bool Say(std::vector<Candidate>& candidates)
        {
            if (candidates.empty())
                return false;
            // One line per event, so two duelists never talk over each other.
            Candidate& pick = candidates[std::uniform_int_distribution<size_t>(0, candidates.size() - 1)(g_Random)];
            Request request = Take(pick);
            std::lock_guard pending(g_PendingLock);
            g_Pending = std::move(request);
            g_Busy = true;
            return true;
        }

        // Duel start: each duelist's line, said at the same time (they overlap, like the anime's "Duel!").
        bool SayTogether(std::vector<Candidate>& first, std::vector<Candidate>& second)
        {
            if (first.empty())
                return Say(second);
            if (second.empty())
                return Say(first);
            Request a = Take(first[std::uniform_int_distribution<size_t>(0, first.size() - 1)(g_Random)]);
            Request b = Take(second[std::uniform_int_distribution<size_t>(0, second.size() - 1)(g_Random)]);
            std::lock_guard pending(g_PendingLock);
            g_Pending = std::move(a);
            g_PendingAlongside = std::move(b);
            g_Busy = true;
            return true;
        }

        bool Quiet()
        {
            return !g_Busy && GetTickCount64() >= g_QuietUntil;
        }

        Outcome Fire(const Happening& h)
        {
            Video::OnEvent(h, LocalSide());   // videos.json "duel" entries
            const bool important = h.Event == When::DuelStart || h.Event == When::Win || h.Event == When::Draw;
            if (!important && !Quiet())
                return Outcome::Blocked;

            std::lock_guard lock(g_ConfigLock);
            if (g_Config.Characters.empty())
                return Outcome::Silent;
            const int localSide = LocalSide();
            std::vector<Candidate> candidates;
            switch (h.Event)
            {
            case When::DuelStart:
            {
                std::vector<Candidate> other;
                Collect(candidates, h, h.Event, 0, false, localSide);
                Collect(other, h, h.Event, 1, false, localSide);
                return SayTogether(candidates, other) ? Outcome::Said : Outcome::Silent;
            }
            case When::Draw:
                Collect(candidates, h, h.Event, 0, false, localSide);
                Collect(candidates, h, h.Event, 1, false, localSide);
                break;
            case When::Win:
                Collect(candidates, h, When::Win, h.Side, false, localSide);
                Collect(candidates, h, When::Lose, 1 - h.Side, false, localSide);
                break;
            default:
                // The side it happened to says its "me" lines; the other duelist says its "opponent" lines.
                Collect(candidates, h, h.Event, h.Side, false, localSide);
                Collect(candidates, h, h.Event, 1 - h.Side, true, localSide);
                break;
            }
            return Say(candidates) ? Outcome::Said : Outcome::Silent;
        }

        Outcome Fire(When event, int side)
        {
            return Fire(Happening{ .Event = event, .Side = side });
        }

        // ---- the field: summons are cards that turn up face-up in a monster zone, whatever put them there ----

        Slot ReadZone(int side, uintptr_t base, int zone)
        {
            const uintptr_t at = DS::SideBlock(side) + base + DS::ZoneSize * zone;
            const uint32_t word = DS::Read<uint32_t>(at);
            const uint32_t position = DS::Read<uint32_t>(at + DS::ZonePosition);
            Slot slot;
            slot.Card = DS::CardIdOf(word);
            slot.Present = slot.Card != 0 && (DS::Read<uint32_t>(at + kZoneFlags) & 0x20) == 0;
            slot.FaceUp = slot.Present && (position & (DS::POS_FACEDOWN_ATTACK | DS::POS_FACEDOWN_DEFENSE)) == 0;
            slot.Instance = DS::InstanceOf(word);
            return slot;
        }

        Slot ReadSlot(int side, int zone)
        {
            return ReadZone(side, DS::MonsterZones, zone);
        }

        // Spell & Trap Zones 0-4, then the Field Zone (its own InPlay_Card after them).
        Slot ReadBack(int side, int zone)
        {
            return zone < 5 ? ReadZone(side, DS::SpellTrapZones, zone) : ReadZone(side, DS::FieldSpell, 0);
        }

        // A Special Summon's kind: the summon's own flags (the event record: 1 = summoned properly, 0x40 = Pendulum Summon) and the card's
        // frame. A Synchro Monster brought back by Monster Reborn is "special", not "synchro".
        How SpecialKind(uint16_t card)
        {
            const uint16_t flags = DS::Read<uint16_t>(kEventFlags);
            const uint16_t* row = KindRow(card);
            if (row && (flags & 0x40) && row[5] > 0)
                return How::Pendulum;
            if (!row || !(flags & 1))
                return How::Special;
            switch (row[1])   // BaseFrame
            {
            case 2: return How::Ritual;
            case 3: return How::Fusion;
            case 10: return How::Synchro;
            case 12: return How::Xyz;
            case 18: return How::Link;
            default: return How::Special;
            }
        }

        // A line is said at most once a turn when its "once" is "turn": the turn's start clears it.
        void NewTurn()
        {
            Video::OnNewTurn();
            std::lock_guard lock(g_ConfigLock);
            for (auto& [character, lines] : g_Config.Characters)
                for (Line& line : lines)
                    if (line.PerTurn)
                        line.Used = false;
        }

        bool summonedNow = false;   // a summon was seen in the current WatchField pass (its materials / tributes are not "destroyed")

        void Summoned(int side, uint16_t card, How how)
        {
            summonedNow = true;
            Video::OnSummon(card);   // movies/<card id>.mp4: the card's own summon cut-in
            Logger::Log(std::format("Voice: side {} summoned card {} ({})", side, card, kHowNames[static_cast<size_t>(how)]), MODULE_NAME, 0);
            Fire(Happening{ .Event = When::Summon, .Side = side, .Card = card, .Summon = how });
        }

        bool WasNormalSummoned(uint32_t instance)
        {
            return std::find(g_NormalSummoned.begin(), g_NormalSummoned.end(), instance) != g_NormalSummoned.end();
        }

        void WatchField(bool announce)
        {
            const auto before = g_Field;
            for (int side = 0; side < 2; ++side)
                for (int zone = 0; zone < kMonsterZones; ++zone)
                    g_Field[side][zone] = ReadSlot(side, zone);
            if (!announce)
            {
                for (int side = 0; side < 2; ++side)
                {
                    for (int zone = 0; zone < kSpellTrapZones; ++zone)
                        g_Back[side][zone] = ReadBack(side, zone);
                    g_GraveCount[side] = DS::PileCount(side, DS::Graveyard);
                }
                return;
            }
            summonedNow = false;

            for (int side = 0; side < 2; ++side)
                for (int zone = 0; zone < kMonsterZones; ++zone)
                {
                    const Slot& now = g_Field[side][zone];
                    if (!now.FaceUp)
                        continue;
                    bool wasUp = false;   // already face-up somewhere (moved zones or changed control): not a summon
                    for (auto& row : before)
                        for (auto& old : row)
                            wasUp |= old.FaceUp && old.Instance == now.Instance && old.Card == now.Card;
                    if (wasUp)
                        continue;
                    const Slot& old = before[side][zone];
                    if (old.Present && old.Instance == now.Instance)
                        Summoned(side, now.Card, How::Flip);
                    else if (WasNormalSummoned(now.Instance))
                        Summoned(side, now.Card, How::Normal);
                    else if (g_LastHandMove.Side == side && g_LastHandMove.Zone == zone && g_LastHandMove.Card == now.Card)
                        g_Deferred = Deferred{ side, zone, now.Instance, now.Card, 4 };   // wait for its 0x2B
                    else
                        Summoned(side, now.Card, SpecialKind(now.Card));
                }

            // Set: a card that turned up face-down (a monster Set, or a Spell / Trap Set in its zone).
            const auto backBefore = g_Back;
            for (int side = 0; side < 2; ++side)
                for (int zone = 0; zone < kSpellTrapZones; ++zone)
                    g_Back[side][zone] = ReadBack(side, zone);
            auto wasOnField = [&](uint32_t instance)
            {
                for (auto& row : before) for (auto& old : row) if (old.Present && old.Instance == instance) return true;
                for (auto& row : backBefore) for (auto& old : row) if (old.Present && old.Instance == instance) return true;
                return false;
            };
            auto isOnField = [&](uint32_t instance)
            {
                for (auto& row : g_Field) for (auto& now : row) if (now.Present && now.Instance == instance) return true;
                for (auto& row : g_Back) for (auto& now : row) if (now.Present && now.Instance == instance) return true;
                return false;
            };
            for (int side = 0; side < 2; ++side)
            {
                for (int zone = 0; zone < kMonsterZones; ++zone)
                    if (const Slot& now = g_Field[side][zone]; now.Present && !now.FaceUp && !wasOnField(now.Instance))
                        Fire(Happening{ .Event = When::Set, .Side = side, .Card = now.Card });
                for (int zone = 0; zone < kSpellTrapZones; ++zone)
                    if (const Slot& now = g_Back[side][zone]; now.Present && !now.FaceUp && !wasOnField(now.Instance))
                        Fire(Happening{ .Event = When::Set, .Side = side, .Card = now.Card });
            }

            // Destroyed: a card that left the field while its owner's Graveyard grew. Tributes and materials also go there, so a message
            // that summoned something doesn't count.
            const auto graveBefore = g_GraveCount;
            for (int side = 0; side < 2; ++side)
                g_GraveCount[side] = DS::PileCount(side, DS::Graveyard);
            if (summonedNow)
                return;
            for (int side = 0; side < 2; ++side)
            {
                if (g_GraveCount[side] <= graveBefore[side])
                    continue;
                for (auto& old : before[side])
                    if (old.Present && !isOnField(old.Instance))
                        Fire(Happening{ .Event = When::Destroyed, .Side = side, .Card = old.Card });
                for (auto& old : backBefore[side])
                    if (old.Present && !isOnField(old.Instance))
                        Fire(Happening{ .Event = When::Destroyed, .Side = side, .Card = old.Card });
            }
        }

        // A Normal Summon's 0x2B: the card is in the zone by now (0x51 put it there).
        void NormalSummon(int side, int zone)
        {
            if (zone < 0 || zone >= kMonsterZones)
                return;
            const Slot slot = ReadSlot(side, zone);
            g_NormalSummoned.push_back(slot.Instance);
            if (g_Deferred && g_Deferred->Side == side && g_Deferred->Zone == zone && g_Deferred->Instance == slot.Instance)
            {
                const uint16_t card = g_Deferred->Card;
                g_Deferred.reset();
                Summoned(side, card, How::Normal);
            }
        }

        void TickDeferred()
        {
            if (g_Deferred && --g_Deferred->PopsLeft <= 0)
            {
                const Deferred deferred = *g_Deferred;
                g_Deferred.reset();
                Summoned(deferred.Side, deferred.Card, How::Special);   // from the hand without a Normal Summon (e.g. Cyber Dragon)
            }
        }

        // ---- the hand ----

        bool HandHolds(int side, const std::vector<int>& cards, int needed)
        {
            const uint32_t count = std::min<uint32_t>(DS::PileCount(side, DS::Hand), DS::Hand.Max);
            int found = 0;
            for (int card : cards)
                for (uint32_t i = 0; i < count; ++i)
                    if (DS::CardIdOf(DS::PileWord(side, DS::Hand, static_cast<int>(i))) == card)
                    {
                        ++found;
                        break;
                    }
            return found >= needed;
        }

        // inHand lines are said when the hand starts holding the cards (not every message while it does). A line that comes up while
        // another is playing waits for the next check instead of being lost.
        void CheckHands()
        {
            if (!g_HasHandLines)
                return;
            const bool quiet = Quiet();
            std::lock_guard lock(g_ConfigLock);
            const int localSide = LocalSide();
            std::vector<Candidate> candidates;
            for (int speaker = 0; speaker < 2; ++speaker)
            {
                const int character = CharacterOf(speaker, localSide);
                auto it = g_Config.Characters.find(character);
                if (it == g_Config.Characters.end())
                    continue;
                const int other = CharacterOf(1 - speaker, localSide);
                for (Line& line : it->second)
                {
                    if (line.Event != When::InHand)
                        continue;
                    const int needed = line.Amount > 0 ? std::min<int>(line.Amount, static_cast<int>(line.Cards.size())) : static_cast<int>(line.Cards.size());
                    const bool held = HandHolds(line.AboutOpponent ? 1 - speaker : speaker, line.Cards, needed);
                    if (!quiet)
                        continue;
                    if (held && !line.Held[speaker] && !(line.Once && line.Used) && (line.Against < 0 || line.Against == other) && Roll(line))
                        candidates.push_back({ &line, character });
                    line.Held[speaker] = held;
                }
            }
            Say(candidates);
        }

        // ---- the result ----

        Outcome Result(int winner, int reasonA, int reasonB)
        {
            if (g_InDuel)
                Video::OnDuelEnd(Music::CurrentStoryDuel());
            g_InDuel = false;
            if (winner == 3)
                return Fire(When::Draw, 0);
            if (winner != 1 && winner != 2)
                return Outcome::Silent;
            const int localSide = LocalSide();
            // Only the winner has a reason (the loser's is 0); a surrender or rules loss leaves it in g_LastDuelWinReason.
            int reason = std::max(reasonA, reasonB);
            if (reason == 0)
                reason = static_cast<int>(DS::Read<uint32_t>(DS::LastWinReason));
            return Fire(Happening{ .Event = When::Win, .Side = winner == 1 ? localSide : 1 - localSide, .Reason = reason });
        }

        uint64_t __fastcall Hook_EngineInit()
        {
            const uint64_t result = orig_EngineInit();
            {
                std::lock_guard lock(g_ConfigLock);
                for (auto& [character, lines] : g_Config.Characters)
                    for (Line& line : lines)
                    {
                        line.Used = false;
                        line.Held = {};
                    }
            }
            g_Field = {};
            g_Back = {};
            g_GraveCount = {};
            g_OpenLinks.clear();
            g_NormalSummoned.clear();
            g_LastHandMove = {};
            g_Deferred.reset();
            g_InDuel = true;
            g_DuelStartPending = true;   // said when the duel's first message runs, not during loading
            Logger::Log("Voice: duel started (watching summons, attacks and activations)", MODULE_NAME, 0);
            Video::OnDuelStart(Music::CurrentStoryDuel());   // also clears the duel videos' "once"
            return result;
        }

        struct DuelMsg { uint16_t CodeAndSide, Arg1, Arg2, Arg3; };

        // The monster's ATK as the duel has it now (equips, effects): YGO::Duel::Calc_EffectiveAtkDef_Modifiers writes it at +4 of its
        // output (Link_CardIsValidMaterial reads it there for "1000 or less ATK").
        int EffectiveAtk(int side, int zone)
        {
            alignas(8) uint8_t out[64]{};
            reinterpret_cast<uint64_t(__fastcall*)(unsigned int, int, void*, int, int)>(0x1400307C0)(static_cast<unsigned int>(side), zone, out, 0, 0);
            return *reinterpret_cast<const int*>(out + 4);
        }

        // An attack (0x17: a1 = the attacker's zone, a2 = target side | target zone << 8). Said as the most specific line there is: an attack
        // that would win the duel, else a direct attack, else any attack.
        void Attacked(int side, int zone, uint16_t target)
        {
            const uint16_t card = zone >= 0 && zone < kMonsterZones ? ReadSlot(side, zone).Card : 0;
            const int targetSide = target & 1, targetZone = target >> 8;
            const Slot defender = targetZone < kMonsterZones ? ReadSlot(targetSide, targetZone) : Slot{};
            const bool direct = !defender.Present;
            const int defenderLp = DS::LifePoints(1 - side);
            int damage = 0;
            if (zone >= 0 && zone < kMonsterZones)
            {
                const int atk = EffectiveAtk(side, zone);
                if (direct)
                    damage = atk;
                else
                {
                    const uint32_t position = DS::Read<uint32_t>(DS::SideBlock(targetSide) + DS::MonsterZones + DS::ZoneSize * targetZone + DS::ZonePosition);
                    const bool defending = (position & (DS::POS_FACEUP_DEFENSE | DS::POS_FACEDOWN_DEFENSE)) != 0;   // defense position: no damage to LP
                    if (!defending)
                        damage = atk - EffectiveAtk(targetSide, targetZone);
                }
            }
            Logger::Log(std::format("Voice: side {} attacks with card {} ({}, {} damage vs {} LP)", side, card, direct ? "direct" : "a monster", damage, defenderLp), MODULE_NAME, 0);
            if (damage > 0 && damage >= defenderLp && Fire(Happening{ .Event = When::LethalAttack, .Side = side, .Card = card }) != Outcome::Silent)
                return;
            if (direct && Fire(Happening{ .Event = When::DirectAttack, .Side = side, .Card = card }) != Outcome::Silent)
                return;
            Fire(Happening{ .Event = When::Attack, .Side = side, .Card = card });
        }

        // A card's activation (0x2D, seen live: a1 low byte = its zone, a2 = the card, a3 = the chain link id that 0x11 resolves). The side is
        // the one with that card in that zone (the message's side bit is not reliable); a link activated while another is open is a response.
        void Activated(int side, int where, uint16_t card, uint16_t link)
        {
            const int zone = where & 0xFF;
            if (zone < kMonsterZones)
            {
                for (int s = 0; s < 2; ++s)
                    if (ReadSlot(s, zone).Card == card) { side = s; break; }
            }
            else if (zone < kMonsterZones + kSpellTrapZones)
            {
                for (int s = 0; s < 2; ++s)
                    if (ReadBack(s, zone - kMonsterZones).Card == card) { side = s; break; }
            }
            const bool response = !g_OpenLinks.empty();
            g_OpenLinks.push_back(link);
            Logger::Log(std::format("Voice: side {} activates card {} (zone {}, link {}{})", side, card, zone, link, response ? ", in response" : ""), MODULE_NAME, 0);
            Fire(Happening{ .Event = When::Activate, .Side = side, .Card = card, .Response = response });
        }

        // Holds the queue at the duel result until its line has been said, so it plays before the finish animation (the Exodia
        // video, the YOU WIN banner). Holding = returning "busy", as PumpAndMirror does while an animation plays.
        bool HoldForResult(uint32_t count, const DuelMsg& next)
        {
            static bool holding = false;
            static uint64_t holdUntil = 0;
            if (holding)
            {
                if (g_Busy && GetTickCount64() < holdUntil)
                    return true;
                holding = false;
                return false;
            }
            if (!g_InDuel || count == 0 || (next.CodeAndSide & 0xFFF) != kMsgDuelResult)
                return false;
            // Offline only (an online duel's opponent waits on this side's messages), and only when this call would pop it.
            if (DS::Read<uint32_t>(kNetDuel) != 0 || DS::Read<uint32_t>(kQueueStage) != 0 || AnimBusy())
                return false;
            if (Result(next.Arg1, next.Arg2, next.Arg3) != Outcome::Said)
                return false;
            holding = true;
            holdUntil = GetTickCount64() + 30000;   // a stuck or very long file never holds the duel for good
            return true;
        }

        // Same approach as the MP DuelRecorder: compare the queue before and after; if Count dropped, the front entry was popped
        // and is now executing (the order the player sees things in).
        uint64_t __fastcall Hook_PumpAndMirror()
        {
            static uint16_t highArg1 = 0;

            const uint32_t countBefore = DS::Read<uint32_t>(DS::MsgQueueCount);
            const DuelMsg first = countBefore ? *reinterpret_cast<const DuelMsg*>(DS::MsgQueue + 0x10) : DuelMsg{};
            if (HoldForResult(countBefore, first))
                return 1;
            // A full-screen video (a card's cut-in, videos.json "duel") holds the duel like the game's own cut-ins do. Offline only.
            if (g_InDuel && Video::FullScreenBusy() && DS::Read<uint32_t>(kNetDuel) == 0)
                return 1;
            const uint64_t result = orig_PumpAndMirror();
            if (!g_InDuel || countBefore == 0 || DS::Read<uint32_t>(DS::MsgQueueCount) >= countBefore)
                return result;

            const uint32_t code = first.CodeAndSide & 0xFFF;
            const int side = (first.CodeAndSide >> 15) & 1;
            if (code == kMsgHighWordPrefix)
            {
                highArg1 = first.Arg1;
                return result;
            }
            const int a1 = static_cast<int>(first.Arg1 | (highArg1 << 16));
            highArg1 = 0;

            if (g_DuelStartPending.exchange(false))
            {
                WatchField(false);
                Fire(When::DuelStart, 0);
            }
            switch (code)
            {
            case kMsgPhaseDraw: NewTurn(); Fire(When::TurnStart, side); break;
            case kMsgPhaseBattle: Fire(When::BattlePhase, side); break;
            case kMsgAttack:
                Attacked(side, a1, first.Arg2);
                break;
            case kMsgActivate:
                Activated(side, a1, first.Arg2, first.Arg3);
                break;
            case kMsgChainResolve:
                std::erase(g_OpenLinks, static_cast<uint32_t>(first.Arg2));
                break;
            case kMsgLpDamage:
            {
                const int before = DS::LifePoints(side);
                const int after = std::max(0, before - a1);
                // Low LP first: it matters more, and a damage line would hold the voice busy and skip it.
                if (after > 0)   // reaching 0 is the duel result, not "low LP"
                    Fire(Happening{ .Event = When::LowLp, .Side = side, .Amount = after, .LpBefore = before });
                Fire(Happening{ .Event = When::Damage, .Side = side, .Amount = a1 });
                break;
            }
            case kMsgHandToField:
                if (first.Arg2 & 0x4000)
                    g_LastHandMove = { first.Arg2 & 1, (first.Arg2 >> 1) & 0x1F, first.Arg1 };
                break;
            case kMsgNormalSummon:
                NormalSummon(side, first.Arg2);
                break;
            case kMsgDuelResult:   // online, or when the hold above didn't run
                Result(a1, first.Arg2, first.Arg3);
                return result;
            }
            WatchField(true);
            TickDeferred();
            CheckHands();
            return result;
        }
    }

    void Setup()
    {
        Load();
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_EngineInit, Hook_EngineInit);
        DetourAttach(&(PVOID&)orig_PumpAndMirror, Hook_PumpAndMirror);
        const LONG error = DetourTransactionCommit();
        Logger::Log(std::format("Voice hooks {}", error == NO_ERROR ? "attached" : std::format("FAILED (Detours error {})", error)),
            MODULE_NAME, error == NO_ERROR ? 0 : 2);
    }

    void Reload()
    {
        Load();
    }

    void Tick()
    {
        std::optional<Request> request, alongside;
        {
            std::lock_guard lock(g_PendingLock);
            request.swap(g_Pending);
            alongside.swap(g_PendingAlongside);
        }
        if (request && !Player::PlayVoice(request->Path, request->Volume))
            request.reset();
        if (alongside)
            Player::PlayVoiceAlongside(alongside->Path, alongside->Volume);

        static bool wasPlaying = false;
        const bool playing = Player::IsVoicePlaying();
        if (wasPlaying && !playing)
        {
            uint32_t gap;
            {
                std::lock_guard lock(g_ConfigLock);
                gap = g_Config.GapMs;
            }
            g_QuietUntil = GetTickCount64() + gap;
        }
        wasPlaying = playing;
        {
            std::lock_guard lock(g_PendingLock);
            g_Busy = playing || g_Pending.has_value();
        }

        float duck;
        {
            std::lock_guard lock(g_ConfigLock);
            duck = g_Config.Duck;
        }
        Player::SetDuck(playing ? duck : 1.0f, 300);
    }

    float Volume()
    {
        return g_Volume;
    }
}
