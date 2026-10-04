#include <Windows.h>
#include <cctype>
#include <fstream>
#include <string>
#include <unordered_map>

#include <json.hpp>

#include "Card.h"
#include "Genres.h"
#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    constexpr uint64_t kHiddenGenreBits = 0x3F8000000000ULL;   // bits 39-45: Get_GenreFromKonamiId clears them
    constexpr size_t kGenreOffset = 0x38;                      // FULL_CARD_PROPS +0x38 = genre bitmask

    // Bit n = genre n (the exe's ICON_ID_GENRE_* order); the game's display names are accepted as well.
    constexpr const char* kKeys[] = {
        "LPUP", "LPDOWN", "DRAW", "SPSUMMON", "DISABLE", "DECKSEARCH", "USEGRAVE", "POWER", "POSITION", "CONTROL", "BREAKMONST",
        "BREAKMAGIC", "HANDDES", "DECKDES", "REMOVECARD", "CARDBACK", "SPEAR", "DIRECTATK", "MANYATK", "UNBREAK", "LIMITATK", "CANTSUMMON",
        "REVERSE", "TOON", "SPIRIT", "UNION", "DUAL", "LEVELUP", "ORIGINAL", "FUSION", "RITUAL", "TOKEN", "COUNTER", "GAMBLE", "ATTR", "TYPE",
        "TUNER", "SYNC", "DROPGRAVE", "NORMAL", "ATTR_LIGHT", "ATTR_DARK", "ATTR_EARTH", "ATTR_WATER", "ATTR_FIRE", "ATTR_WIND", "XYZ",
        "LVUPDOWN", "PENDULUM", "LINK", "ATTR_DIVINE", "NEWCARD", "GAMEORIGINAL", "VARIATION" };
    constexpr const char* kNames[] = {
        "Recover LP", "Damage LP", "Help Draw", "Special Summon", "Negate effect", "Search Deck", "Recover from Graveyard",
        "Increase/Decrease ATK/DEF", "Change battle position", "Set controls", "Destroy Monster", "Destroy Spell Card", "Destroy Hand",
        "Destroy Deck", "Remove Card", "Return Card", "Piercing", "Direct Attack", "Attack multiple times", "Cannot be destroyed",
        "Limit Attack", "Cannot Normal Summon", "Flip Effect Monster", "Toon Monster", "Spirit Monster", "Union Monster", "Gemini Monster",
        "LV Monster", "Original", "Fusion Material Monster", "Ritual", "Token", "Counter", "Gamble", "Attribute-related", "Type-related",
        "Tuner", "Synchro Monster", "Send to Graveyard", "Normal Monster", "Light Attribute", "Dark Attribute", "Earth Attribute",
        "Water Attribute", "Fire Attribute", "Wind Attribute", "Xyz Monster", "Level Modifier", "Pendulum", "Link Monster",
        "Divine Attribute", "New Card", "Game Original", "Card Variation" };
    static_assert(std::size(kKeys) == std::size(kNames));

    std::unordered_map<int, uint64_t> g_masks;   // Konami id -> genre bitmask

    bool SameText(const std::string& a, const char* b) { return _stricmp(a.c_str(), b) == 0; }

    int BitOf(const nlohmann::json& value)
    {
        if (value.is_number_integer())
        {
            int bit = value.get<int>();
            return bit >= 0 && bit < 64 ? bit : -1;
        }
        if (!value.is_string())
            return -1;
        const std::string text = value.get<std::string>();
        for (size_t i = 0; i < std::size(kKeys); ++i)
            if (SameText(text, kKeys[i]) || SameText(text, kNames[i]))
                return static_cast<int>(i);
        try
        {
            size_t used = 0;
            int bit = std::stoi(text, &used);
            if (used == text.size() && bit >= 0 && bit < 64)
                return bit;
        }
        catch (...) {}
        return -1;
    }
}

namespace Genres
{
    void Load()
    {
        g_masks.clear();
        // every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h): a later mod's genres for a card replace an earlier one's
        std::vector<std::string> problems;
        auto root = YGO::Mods::ReadMerged("genres.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::Log(problem + ", its genres are left out", MODULE_NAME, 3);
        if (root.is_null())
            return; // genres.json is optional
        try
        {
            auto cards = root.find("cards");
            if (cards == root.end() || !cards->is_array())
                return;
            int unknown = 0;
            for (auto& entry : *cards)
            {
                if (!entry.is_object() || !entry.contains("card") || !entry["card"].is_number_integer())
                    continue;
                const int id = entry["card"].get<int>();
                if (id < 1 || id > kLastExtraCardId)
                    continue;
                uint64_t mask = 0;
                if (auto genres = entry.find("genres"); genres != entry.end() && genres->is_array())
                    for (auto& genre : *genres)
                    {
                        const int bit = BitOf(genre);
                        if (bit < 0)
                            ++unknown;
                        else
                            mask |= 1ULL << bit;
                    }
                g_masks[id] = mask & ~kHiddenGenreBits;
            }
            Logger::Log("genres.json: genres for " + std::to_string(g_masks.size()) + " card(s)" +
                (unknown ? ", " + std::to_string(unknown) + " unknown genre name(s) skipped" : ""), MODULE_NAME, unknown ? 2 : 1);
        }
        catch (const std::exception& ex)
        {
            g_masks.clear();
            Logger::Log(std::string("genres.json couldn't be read: ") + ex.what(), MODULE_NAME, 3);
        }
    }

    uint64_t MaskFor(int konamiId, uint64_t fallback)
    {
        auto it = g_masks.find(konamiId);
        return it != g_masks.end() ? it->second : fallback;
    }

    void ApplyToGameCards()
    {
        for (auto& [id, mask] : g_masks)
            if (id < kFirstExtraCardId)   // custom cards get theirs in WriteGameTableEntry
                *reinterpret_cast<uint64_t*>(kFullCardPropsAddress + static_cast<uintptr_t>(id) * kFullCardPropsStride + kGenreOffset) = mask;
    }
}
