#pragma once
#include <cstdint>
#include <json.hpp>

namespace YGO::Effects
{
    // A multi-effect card has no single vanilla identity. Lending its first source id
    // overwrites that source's card type for the duel and leaks its hard-coded rules.
    // Keep the custom identity (or use the usual scratch id above the 14-bit limit).
    inline uint16_t SingleEffectSource(const nlohmann::json& clone, int first, int end)
    {
        if (!clone.is_object() || !clone.contains("from") || !clone["from"].is_number_integer())
            return 0;
        if (clone.contains("parts") && (!clone["parts"].is_array() || !clone["parts"].empty()))
            return 0;
        const int from = clone["from"].get<int>();
        return from >= first && from < end ? static_cast<uint16_t>(from) : 0;
    }
}
