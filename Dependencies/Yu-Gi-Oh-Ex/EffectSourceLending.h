#pragma once
#include <cstdint>
#include <json.hpp>

namespace YGO::Effects
{
    // A multi-effect card has no single vanilla identity. Lending its first source id
    // overwrites that source's card type for the duel and leaks its hard-coded rules.
    // Keep the custom identity (or use the usual scratch id above the 14-bit limit).
    // "effectOnly": true asks for the same on a single-effect clone: only the effect is copied, the card keeps its own
    // kind, stats and identity (a monster copying a Trap stays a monster to every id-keyed rule of the engine).
    inline uint16_t SingleEffectSource(const nlohmann::json& clone, int first, int end)
    {
        if (!clone.is_object() || !clone.contains("from") || !clone["from"].is_number_integer())
            return 0;
        if (clone.contains("effectOnly") && clone["effectOnly"].is_boolean() && clone["effectOnly"].get<bool>())
            return 0;
        // "ownTrigger": the trigger and row are the plugin's own and "from" is only the action card - no identity to lend
        if (clone.contains("ownTrigger") && clone["ownTrigger"].is_boolean() && clone["ownTrigger"].get<bool>())
            return 0;
        if (clone.contains("parts") && (!clone["parts"].is_array() || !clone["parts"].empty()))
            return 0;
        const int from = clone["from"].get<int>();
        return from >= first && from < end ? static_cast<uint16_t>(from) : 0;
    }
}
