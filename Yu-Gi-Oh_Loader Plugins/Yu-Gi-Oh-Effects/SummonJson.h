#pragma once
#include <cstdint>
#include <string>
#include <vector>

#include <json.hpp>

// Where the summoning requirements come from. Fusion.cpp, Ritual.cpp and SynchroXyz.cpp read the same keys from two files:
//   Yu-Gi-Oh-Ex\cards.json       the new cards ("fusion", "ritualSpell", "ritualMonsters", "synchro", "xyz" on a card)
//   Yu-Gi-Oh-Ex\summoning.json   the game's own cards: {"cards": [{"id": <game card id>, <the same keys>}, ...]} (WolfX Card Manager,
//                                tab "Summoning"), so a game card's requirements can be changed the same way a new card's are set
// A summoning.json entry comes after cards.json, so it wins when both name the same id.
namespace SummonJson
{
    constexpr int kFirstExtraCardId = 0x3BC4;   // 15300, as in Yu-Gi-Oh-Cards/Card.h
    constexpr int kLastExtraCardId = 0x4E1F;

    struct Entry
    {
        const nlohmann::json* card;   // the entry object (has an integer "id")
        int id;
        bool custom;                  // from cards.json
        std::string label;            // for the log: "cards.json entry 12 ("Name")"
    };

    // Both files' entries that are objects with an integer "id", cards.json first. Read once, kept for the process.
    const std::vector<Entry>& Entries();

    // In a duel a custom card can play under a vanilla id (docs/EffectSystem.md section 32): the id it plays under now, and the custom card a
    // borrowed id stands for (0 = none). Both are lookups into Yu-Gi-Oh-MoreCards; without it ids are their own.
    uint16_t EngineId(uint16_t id);
    uint16_t RealIdForBorrowed(uint16_t id);
}
