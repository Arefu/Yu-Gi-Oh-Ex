#pragma once
#include <algorithm>
#include <array>
#include <optional>
#include <random>
#include <string>
#include <vector>

#include <json.hpp>

#include "YuGiOh/YuGiOh-DUELSTATE.h"
#include "YuGiOh/YuGiOh-Addresses.h"

// The duel events voice lines (voices.json) and duel videos (videos.json "duel") are played on, and the conditions both files share:
// "when", "who", "cards", "how", "kind", "response", "standing", "amount", "reason", "chance", "once". Voice.cpp's message pump and
// field watch find the events (docs/MediaPlugin.md "Where the events come from") and hands each to Voice and Video.
namespace Trigger
{
    // The names are the files' "when" values (WolfX shows them as sentences).
    enum class When { DuelStart, TurnStart, BattlePhase, Attack, Damage, LowLp, Win, Lose, Draw, Summon, InHand,
                      Activate, Set, Destroyed, DirectAttack, LethalAttack, Count };
    inline constexpr std::array<const char*, static_cast<size_t>(When::Count)> kWhenNames = {
        "duelStart", "turnStart", "battlePhase", "attack", "damage", "lowLP", "win", "lose", "draw", "summon", "inHand",
        "activate", "set", "destroyed", "directAttack", "lethalAttack",
    };

    // How a monster came to be face-up on the field ("how"). Fusion..Pendulum are kinds of Special Summon: "special" matches them too.
    enum class How { Any, Normal, Flip, Special, Fusion, Synchro, Xyz, Link, Ritual, Pendulum, Count };
    inline constexpr std::array<const char*, static_cast<size_t>(How::Count)> kHowNames = {
        "any", "normal", "flip", "special", "fusion", "synchro", "xyz", "link", "ritual", "pendulum" };

    // What kind of card an activate / set / destroyed event is about ("kind").
    enum class Kind { Any, Monster, Spell, Trap, Count };
    inline constexpr std::array<const char*, static_cast<size_t>(Kind::Count)> kKindNames = { "any", "monster", "spell", "trap" };

    // turnStart "standing": the side's LP against the other's.
    enum class Standing { Any, Winning, Losing, Even, Count };
    inline constexpr std::array<const char*, static_cast<size_t>(Standing::Count)> kStandingNames = { "any", "winning", "losing", "even" };

    // DuelWinReason (docs/StatsAndMatchResults.md) as "reason" names. 0 = any.
    inline constexpr std::array<const char*, 25> kReasonNames = {
        "any", "lp", "deckOut", "timeLimit", "surrender", "rules", "exodia", "destinyBoard", "yataLock", "lastTurn", "finalCountdown",
        "effect", "vennominaga", "exodius", "effect14", "leo", "disasterLeo", "jackpot7", "effect18", "relaySoul", "ghostrickAngel",
        "phantasmSpiral", "faWinners", "flyingElephant", "exodiaDefender",
    };

    // What happened, for matching against.
    struct Happening
    {
        When Event = When::DuelStart;
        int Side = 0;                     // the side it is about (the winner for Win)
        int Amount = 0;                   // damage, or the LP after it for LowLp
        int LpBefore = 0;
        int Card = 0;                     // summon / attack / activate / set / destroyed
        How Summon = How::Any;
        bool Response = false;            // activate: chained to another card's activation
        int Reason = 0;                   // win
    };

    // The conditions a line / video has on its event (everything but who says it and what plays).
    struct Conditions
    {
        When Event = When::DuelStart;
        bool AboutOpponent = false;       // "who": "opponent" = about the other duelist
        int Against = -1;                 // only against this character (-1 = anyone)
        int Amount = 0;                   // damage: at least this much; lowLP: LP falls to this or below; inHand: how many of Cards
        std::vector<int> Cards;           // summon / attack / activate / set / destroyed: one of these cards; inHand: the cards to hold
        How Summon = How::Any;
        Kind CardKind = Kind::Any;
        int Response = -1;                // activate: 1 = only in response, 0 = only to start a chain, -1 = either
        Standing Stand = Standing::Any;
        int Reason = 0;                   // win / lose: only for this DuelWinReason (0 = any)
        float Chance = 1.0f;
        bool Once = true;                 // at most once per duel
        bool PerTurn = false;             // "once": "turn"
    };

    template <size_t N>
    std::optional<int> IndexOfName(const std::array<const char*, N>& names, const std::string& name)
    {
        for (size_t i = 0; i < N; ++i)
            if (_stricmp(name.c_str(), names[i]) == 0)
                return static_cast<int>(i);
        return std::nullopt;
    }

    inline float Float(const nlohmann::json& entry, const char* key, float fallback)
    {
        auto it = entry.find(key);
        return it != entry.end() && it->is_number() ? it->get<float>() : fallback;
    }

    // "cards": [4023, 4024] or "card": 4027 (either key, either shape).
    inline std::vector<int> CardList(const nlohmann::json& entry)
    {
        std::vector<int> cards;
        for (const char* key : { "cards", "card" })
        {
            auto it = entry.find(key);
            if (it == entry.end())
                continue;
            if (it->is_number_integer())
                cards.push_back(it->get<int>());
            else if (it->is_array())
                for (auto& card : *it)
                    if (card.is_number_integer())
                        cards.push_back(card.get<int>());
        }
        return cards;
    }

    // Reads the shared keys of a line / video entry; nullopt when "when" is missing or unknown.
    inline std::optional<Conditions> Parse(const nlohmann::json& entry)
    {
        auto when = entry.contains("when") && entry["when"].is_string() ? IndexOfName(kWhenNames, entry["when"].get<std::string>()) : std::nullopt;
        if (!when)
            return std::nullopt;
        Conditions c;
        c.Event = static_cast<When>(*when);
        c.AboutOpponent = entry.value("who", std::string("me")) == "opponent";
        c.Against = entry.contains("against") && entry["against"].is_number_integer() ? entry["against"].get<int>() : -1;
        c.Amount = entry.contains("amount") && entry["amount"].is_number() ? entry["amount"].get<int>() : 0;
        if (entry.contains("count") && entry["count"].is_number_integer())
            c.Amount = entry["count"].get<int>();
        c.Cards = CardList(entry);
        if (auto how = entry.find("how"); how != entry.end() && how->is_string())
            c.Summon = static_cast<How>(IndexOfName(kHowNames, how->get<std::string>()).value_or(0));
        if (auto reason = entry.find("reason"); reason != entry.end())
        {
            if (reason->is_number_integer())
                c.Reason = reason->get<int>();
            else if (reason->is_string())
                c.Reason = IndexOfName(kReasonNames, reason->get<std::string>()).value_or(0);
        }
        c.Chance = std::clamp(Float(entry, "chance", 100.0f), 0.0f, 100.0f) / 100.0f;
        if (auto once = entry.find("once"); once != entry.end() && once->is_string())
        {
            c.PerTurn = once->get<std::string>() == "turn";
            c.Once = c.PerTurn || once->get<std::string>() == "duel";
        }
        else
            c.Once = entry.value("once", true);
        if (auto kind = entry.find("kind"); kind != entry.end() && kind->is_string())
            c.CardKind = static_cast<Kind>(IndexOfName(kKindNames, kind->get<std::string>()).value_or(0));
        if (auto response = entry.find("response"); response != entry.end() && response->is_boolean())
            c.Response = response->get<bool>() ? 1 : 0;
        if (auto standing = entry.find("standing"); standing != entry.end() && standing->is_string())
            c.Stand = static_cast<Standing>(IndexOfName(kStandingNames, standing->get<std::string>()).value_or(0));
        return c;
    }

    // The card's kind as KIND_TABLE says (kind 13 = Spell, 14 = Trap, anything else a monster).
    inline int CardKindNumber(int card)
    {
        return card > 0 ? reinterpret_cast<int(__fastcall*)(int)>(YGO::Addr::Card_GetTypeFromFullCardPropsByKonamiId)(card) : -1;
    }

    inline Kind KindOf(int card)
    {
        const int kind = CardKindNumber(card);
        return kind < 0 ? Kind::Any : kind == 13 ? Kind::Spell : kind == 14 ? Kind::Trap : Kind::Monster;
    }

    // KIND_TABLE row (0x140BF7820, 6 words: Frame, BaseFrame, Category, DeckSection, Tuner, Pendulum).
    inline const uint16_t* KindRow(int card)
    {
        const int kind = CardKindNumber(card);
        return kind >= 0 && kind < 46 ? reinterpret_cast<const uint16_t*>(YGO::Addr::CardFrameTable + 12 * static_cast<uintptr_t>(kind)) : nullptr;
    }

    inline bool HowMatches(How wanted, How happened)
    {
        if (wanted == How::Any || wanted == happened)
            return true;
        return wanted == How::Special && happened >= How::Special;   // a Fusion / Synchro / ... Summon is a Special Summon
    }

    // Whether `c` (an `event` entry about `side`, the other duelist being `other`) fits what happened. Chance and once are not checked.
    inline bool Matches(const Conditions& c, const Happening& h, When event, int side, int other)
    {
        namespace DS = YGO::DUELSTATE;
        if (c.Against >= 0 && c.Against != other)
            return false;
        if (event == When::Damage && h.Amount < c.Amount)
            return false;
        if (event == When::LowLp && !(h.LpBefore > c.Amount && h.Amount <= c.Amount))
            return false;
        const bool cardEvent = event == When::Summon || event == When::Attack || event == When::Activate || event == When::Set ||
                               event == When::Destroyed || event == When::DirectAttack || event == When::LethalAttack;
        if (cardEvent && !c.Cards.empty() && std::find(c.Cards.begin(), c.Cards.end(), h.Card) == c.Cards.end())
            return false;
        if (event == When::Summon && !HowMatches(c.Summon, h.Summon))
            return false;
        if ((event == When::Activate || event == When::Set || event == When::Destroyed) && c.CardKind != Kind::Any && c.CardKind != KindOf(h.Card))
            return false;
        if (event == When::Activate && c.Response >= 0 && (c.Response == 1) != h.Response)
            return false;
        if (event == When::TurnStart && c.Stand != Standing::Any)
        {
            const int mine = DS::LifePoints(side), theirs = DS::LifePoints(1 - side);
            const Standing now = mine > theirs ? Standing::Winning : mine < theirs ? Standing::Losing : Standing::Even;
            if (now != c.Stand)
                return false;
        }
        if (event == When::TurnStart && c.Amount > 0 && DS::LifePoints(side) > c.Amount)
            return false;   // turnStart "amount": only when the side's LP is at or below it
        if ((event == When::Win || event == When::Lose) && c.Reason != 0 && c.Reason != h.Reason)
            return false;
        return true;
    }

    inline bool Roll(const Conditions& c, std::mt19937& random)
    {
        return c.Chance >= 1.0f || std::uniform_real_distribution<float>(0.0f, 1.0f)(random) < c.Chance;
    }

    // Files dealt from a shuffled bag: none repeats until all have played, and a new round never starts with the one that just played.
    struct Bag
    {
        std::vector<size_t> Left;
        size_t Last = SIZE_MAX;

        size_t Deal(size_t count, std::mt19937& random)
        {
            if (Left.empty())
            {
                for (size_t i = 0; i < count; ++i)
                    Left.push_back(i);
                std::shuffle(Left.begin(), Left.end(), random);
                if (Left.size() > 1 && Left.back() == Last)
                    std::swap(Left.front(), Left.back());
            }
            Last = Left.back();
            Left.pop_back();
            return Last;
        }
    };
}
