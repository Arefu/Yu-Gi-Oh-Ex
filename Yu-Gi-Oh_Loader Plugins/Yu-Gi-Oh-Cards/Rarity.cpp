#include "Rarity.h"

#include <algorithm>
#include <cctype>
#include <format>
#include <mutex>

#include <json.hpp>

#include "Logger.h"
#include "Yu-Gi-Oh-Mods.h"

namespace
{
    using namespace Rarity;

    // Names rarities.json uses for the numbers (index = the enum value).
    const char* const kInkNames[] = { "none", "silver", "gold", "rainbow", "speckled", "champagne", "red", "bronze", "platinum", "colour", "sparkleSilver" };
    const char* const kFoilNames[] = { "none", "holo", "parallel", "prismatic", "ghost", "emboss", "gold", "platinum", "secretGold", "secretPlatinum",
                                       "reliefGold", "rainbow" };
    const char* const kCoatNames[] = { "none", "parallel", "duelTerminal", "millennium", "kaibaCorp", "starfoil", "mosaic", "shatterfoil", "crisscross", "sparkle" };
    const char* const kMarkNames[] = { "none", "20th", "25th", "10000" };
    const char* const kArtNames[] = { "normal", "alt", "broken", "full", "extended", "printed" };
    const std::pair<const char*, int32_t> kRegionNames[] = { { "attribute", RegAttribute }, { "stars", RegStars }, { "artBorder", RegArtBorder },
        { "edge", RegEdge }, { "border", RegEdge }, { "textBorder", RegTextBorder }, { "wholeFrame", RegWholeFrame }, { "nameBox", RegNameBox } };

    bool Same(const std::string& a, const std::string& b)
    {
        return a.size() == b.size() && std::equal(a.begin(), a.end(), b.begin(), [](char x, char y) { return std::tolower((unsigned char)x) == std::tolower((unsigned char)y); });
    }

    template <size_t N>
    int32_t Lookup(const char* const (&names)[N], const nlohmann::json& value, int32_t fallback)
    {
        if (value.is_number_integer())
            return std::clamp(value.get<int32_t>(), 0, static_cast<int32_t>(N) - 1);
        if (value.is_string())
            for (size_t i = 0; i < N; ++i)
                if (Same(names[i], value.get<std::string>()))
                    return static_cast<int32_t>(i);
        return fallback;
    }

    constexpr int32_t kAttrStars = RegAttribute | RegStars;
    constexpr int32_t kGoldRegions = RegAttribute | RegStars | RegArtBorder | RegEdge | RegTextBorder | RegNameBox;
    constexpr int32_t kWhole = RegWholeFrame | RegAttribute | RegStars | RegArtBorder | RegEdge | RegTextBorder;

    struct Spec { int Tier, Odds; Slot S; int32_t Ink, Art, Frame, Regions, Coat; float Desat; int32_t Mark; };

    Def Make(uint8_t id, const char* key, const char* name, Spec s, const char* note = "")
    {
        Def d;
        d.Id = id; d.Key = key; d.Name = name; d.Tier = s.Tier; d.Odds = s.Odds; d.RollSlot = s.S;
        d.NameInk = s.Ink; d.ArtFoil = s.Art; d.FrameFoil = s.Frame; d.Regions = s.Regions; d.Coating = s.Coat; d.Desaturate = s.Desat;
        d.Watermark = s.Mark; d.Note = note;
        return d;
    }

    // Every printed rarity. Odds are per card in the slot (the rare slot is 1 card in 8); rarest is rolled first. Rarities that only exist
    // on a handful of cards or special products have odds 0: they are in the catalogue (ShinyPreview, editors, mods) but packs don't roll them.
    std::vector<Def> BuiltIn()
    {
        constexpr Slot R = Slot::Rare, A = Slot::Any, N = Slot::None;
        std::vector<Def> v = {
            //                                                          tier  odds slot ink           art                 frame               regions        coating          desat mark
            Make(1, "rare", "Rare",                                     { 1,    0, R, InkSilver,    FoilNone,           FoilNone,           0,             CoatNone,        0, MarkNone }),
            Make(2, "super", "Super Rare",                              { 10,   5, R, InkNone,      FoilHolo,           FoilHolo,           kAttrStars,    CoatNone,        0, MarkNone }),
            Make(3, "ultra", "Ultra Rare",                              { 20,  12, R, InkGold,      FoilHolo,           FoilHolo,           kAttrStars,    CoatNone,        0, MarkNone }),
            Make(4, "secret", "Secret Rare",                            { 30,  31, R, InkRainbow,   FoilParallel,       FoilParallel,       kAttrStars,    CoatNone,        0, MarkNone }),
            Make(5, "prismatic-secret", "Prismatic Secret Rare",        { 32,  60, R, InkSpeckled,  FoilPrismatic,      FoilPrismatic,      kAttrStars,    CoatNone,        0, MarkNone }),
            Make(6, "ultimate", "Ultimate Rare",                        { 40,  96, R, InkGold,      FoilEmboss,         FoilEmboss,         kAttrStars | RegArtBorder, CoatNone, 0, MarkNone }),
            Make(7, "ghost", "Ghost Rare",                              { 45, 288, R, InkGold,      FoilGhost,          FoilNone,           0,             CoatNone,     0.8f, MarkNone }),
            Make(8, "holographic", "Holographic Rare",                  { 45,   0, N, InkPlatinum,  FoilGhost,          FoilNone,           0,             CoatNone,     0.8f, MarkNone }),
            Make(9, "starlight", "Starlight Rare",                      { 60, 576, R, InkSpeckled,  FoilPrismatic,      FoilPrismatic,      kWhole,        CoatNone,        0, MarkNone }),
            Make(10, "quarter-century-secret", "Quarter Century Secret Rare", { 55, 576, R, InkChampagne, FoilParallel, FoilParallel,       kWhole,        CoatNone,        0, Mark25th }),
            Make(11, "20th-secret", "20th Secret Rare",                 { 54,   0, N, InkRed,       FoilParallel,       FoilParallel,       kWhole,        CoatNone,        0, Mark20th }),
            Make(12, "10000-secret", "10000 Secret Rare",               { 56,   0, N, InkRed,       FoilParallel,       FoilParallel,       kWhole,        CoatNone,        0, Mark10000 },
                 "only Ten Thousand Dragon has it"),
            Make(13, "extra-secret", "Extra Secret Rare",               { 50,   0, N, InkRainbow,   FoilParallel,       FoilParallel,       kWhole,        CoatNone,        0, MarkNone }),
            Make(14, "platinum-secret", "Platinum Secret Rare",         { 52, 500, R, InkPlatinum,  FoilSecretPlatinum, FoilSecretPlatinum, kWhole,        CoatNone,        0, MarkNone }),
            Make(15, "platinum", "Platinum Rare",                       { 48, 400, R, InkPlatinum,  FoilPlatinum,       FoilPlatinum,       kWhole,        CoatNone,        0, MarkNone }),
            Make(16, "gold", "Gold Rare",                               { 42, 150, R, InkGold,      FoilGold,           FoilGold,           kGoldRegions,  CoatNone,        0, MarkNone }),
            Make(17, "gold-secret", "Gold Secret Rare",                 { 47, 300, R, InkGold,      FoilSecretGold,     FoilSecretGold,     kGoldRegions,  CoatNone,        0, MarkNone }),
            Make(18, "ghost-gold", "Ghost/Gold Rare",                   { 49,   0, N, InkGold,      FoilGhost,          FoilGold,           kGoldRegions,  CoatNone,     0.8f, MarkNone }),
            Make(19, "premium-gold", "Premium Gold Rare",               { 51, 400, R, InkGold,      FoilGold,           FoilReliefGold,     kGoldRegions,  CoatNone,        0, MarkNone }),
            Make(20, "collectors", "Collector's Rare",                  { 44, 200, R, InkRainbow,   FoilRainbow,        FoilRainbow,        kAttrStars | RegTextBorder | RegEdge, CoatNone, 0, MarkNone }),
            Make(21, "prismatic-collectors", "Prismatic Collector's Rare", { 46, 0, N, InkRainbow,  FoilRainbow,        FoilRainbow,        kAttrStars | RegTextBorder | RegEdge, CoatSparkle, 0, MarkNone }),
            Make(22, "grand-master", "Grand Master Rare",               { 70,   0, N, InkSpeckled,  FoilPrismatic,      FoilRainbow,        kAttrStars | RegTextBorder, CoatNone, 0, MarkNone },
                 "extended art and a serial number on real cards (needs alternative art)"),
            Make(23, "pharaohs", "Ultra Rare (Pharaoh's Rare)",         { 25,   0, N, InkGold,      FoilHolo,           FoilHolo,           kAttrStars,    CoatMillennium,  0, MarkNone }),
            Make(24, "ultra-secret", "Ultra Secret Rare",               { 31,   0, N, InkSparkleSilver, FoilHolo,       FoilHolo,           kAttrStars,    CoatNone,        0, MarkNone }),
            Make(25, "secret-ultra", "Secret Ultra Rare",               { 31,   0, N, InkGold,      FoilParallel,       FoilParallel,       kAttrStars,    CoatNone,        0, MarkNone }),
            Make(26, "normal-parallel", "Normal Parallel Rare",         { 8,  200, A, InkNone,      FoilNone,           FoilNone,           0,             CoatParallel,    0, MarkNone }),
            Make(27, "rare-parallel", "Rare Parallel Rare",             { 9,    0, N, InkSilver,    FoilNone,           FoilNone,           0,             CoatParallel,    0, MarkNone }),
            Make(28, "super-parallel", "Super Parallel Rare",           { 15, 150, R, InkNone,      FoilHolo,           FoilHolo,           kAttrStars,    CoatParallel,    0, MarkNone }),
            Make(29, "ultra-parallel", "Ultra Parallel Rare",           { 26, 250, R, InkGold,      FoilHolo,           FoilHolo,           kAttrStars,    CoatParallel,    0, MarkNone }),
            Make(30, "secret-parallel", "Secret Parallel Rare",         { 35,   0, N, InkRainbow,   FoilParallel,       FoilParallel,       kAttrStars,    CoatParallel,    0, MarkNone }),
            Make(31, "extra-secret-parallel", "Extra Secret Parallel Rare", { 53, 0, N, InkRainbow, FoilParallel,       FoilParallel,       kWhole,        CoatParallel,    0, MarkNone }),
            Make(32, "holographic-parallel", "Holographic Parallel Rare", { 46, 0, N, InkPlatinum,  FoilGhost,          FoilNone,           0,             CoatParallel, 0.8f, MarkNone }),
            Make(33, "dt-normal-parallel", "Duel Terminal Normal Parallel Rare", { 7, 0, N, InkNone, FoilNone,          FoilNone,           0,             CoatDuelTerminal, 0, MarkNone }),
            Make(34, "dt-normal-rare-parallel", "Duel Terminal Normal Rare Parallel Rare", { 7, 0, N, InkNone, FoilNone, FoilNone,          0,             CoatDuelTerminal, 0, MarkNone }),
            Make(35, "dt-rare-parallel", "Duel Terminal Rare Parallel Rare", { 9, 0, N, InkSilver, FoilNone,           FoilNone,           0,             CoatDuelTerminal, 0, MarkNone }),
            Make(36, "dt-super-parallel", "Duel Terminal Super Parallel Rare", { 15, 0, N, InkNone, FoilHolo,          FoilHolo,           kAttrStars,    CoatDuelTerminal, 0, MarkNone }),
            Make(37, "dt-ultra-parallel", "Duel Terminal Ultra Parallel Rare", { 26, 0, N, InkGold, FoilHolo,          FoilHolo,           kAttrStars,    CoatDuelTerminal, 0, MarkNone }),
            Make(38, "dt-secret-parallel", "Duel Terminal Secret Parallel Rare", { 35, 0, N, InkRainbow, FoilParallel, FoilParallel,       kAttrStars,    CoatDuelTerminal, 0, MarkNone }),
            Make(39, "millennium", "Millennium Rare",                   { 12,   0, N, InkNone,      FoilNone,           FoilNone,           0,             CoatMillennium,  0, MarkNone }),
            Make(40, "millennium-super", "Millennium Super Rare",       { 16,   0, N, InkNone,      FoilHolo,           FoilHolo,           kAttrStars,    CoatMillennium,  0, MarkNone }),
            Make(41, "millennium-ultra", "Millennium Ultra Rare",       { 27,   0, N, InkGold,      FoilHolo,           FoilHolo,           kAttrStars,    CoatMillennium,  0, MarkNone }),
            Make(42, "millennium-secret", "Millennium Secret Rare",     { 36,   0, N, InkRainbow,   FoilParallel,       FoilParallel,       kAttrStars,    CoatMillennium,  0, MarkNone }),
            Make(43, "millennium-gold", "Millennium Gold Rare",         { 43,   0, N, InkGold,      FoilGold,           FoilGold,           kGoldRegions,  CoatMillennium,  0, MarkNone }),
            Make(44, "kc-common", "Kaiba Corporation Common",           { 7,    0, N, InkNone,      FoilNone,           FoilNone,           0,             CoatKaibaCorp,   0, MarkNone }),
            Make(45, "kc-rare", "Kaiba Corporation Rare",               { 9,    0, N, InkSilver,    FoilNone,           FoilNone,           0,             CoatKaibaCorp,   0, MarkNone }),
            Make(46, "kc-ultra", "Kaiba Corporation Ultra Rare",        { 27,   0, N, InkGold,      FoilHolo,           FoilHolo,           kAttrStars,    CoatKaibaCorp,   0, MarkNone }),
            Make(47, "starfoil", "Starfoil Rare",                       { 6,  120, A, InkNone,      FoilNone,           FoilNone,           0,             CoatStarfoil,    0, MarkNone }),
            Make(48, "mosaic", "Mosaic Rare",                           { 6,  120, A, InkNone,      FoilNone,           FoilNone,           0,             CoatMosaic,      0, MarkNone }),
            Make(49, "shatterfoil", "Shatterfoil Rare",                 { 6,  120, A, InkNone,      FoilNone,           FoilNone,           0,             CoatShatterfoil, 0, MarkNone }),
            Make(50, "normal-rare", "Normal Rare",                      { 1,    0, N, InkNone,      FoilNone,           FoilNone,           0,             CoatNone,        0, MarkNone },
                 "looks like a Common (only the Eye of Anubis hologram differs)"),
            Make(51, "holofoil", "Holofoil Rare",                       { 11,   0, N, InkNone,      FoilHolo,           FoilNone,           0,             CoatParallel,    0, MarkNone }),
            Make(52, "colorful", "Colorful Rare",                       { 2,    0, N, InkColour,    FoilNone,           FoilNone,           0,             CoatNone,        0, MarkNone }),
            Make(53, "rush", "Rush Rare",                               { 22,   0, N, InkGold,      FoilHolo,           FoilNone,           0,             CoatShatterfoil, 0, MarkNone }),
            Make(54, "over-rush", "Over Rush Rare",                     { 58,   0, N, InkGold,      FoilNone,           FoilPlatinum,       RegEdge,       CoatShatterfoil, 0, MarkNone },
                 "the artwork leaves its frame on real cards (needs alternative art)"),
            Make(55, "full-over-rush", "Full Over Rush Rare",           { 59,   0, N, InkGold,      FoilNone,           FoilGold,           RegEdge,       CoatCrisscross,  0, MarkNone },
                 "the artwork covers the whole card on real cards (needs alternative art)"),
            Make(56, "gold-rush", "Gold Rush Rare",                     { 57,   0, N, InkGold,      FoilGold,           FoilReliefGold,     kWhole | RegNameBox, CoatNone, 0, MarkNone }),
            Make(57, "mirror", "Mirror Rare",                           { 50,   0, N, InkNone,      FoilGhost,          FoilNone,           0,             CoatParallel, 0.9f, MarkNone },
                 "unreleased (seen on a 2025 test sheet)"),
        };
        v[0].Fallback = true;                              // a rare-slot card that rolls nothing rarer is a Rare (silver name)
        v[51].NameColour[0] = 0.25f; v[51].NameColour[1] = 0.55f; v[51].NameColour[2] = 1.0f;   // Colorful: blue by default
        for (Def& d : v)
        {
            if (d.Coating == CoatParallel || d.Coating == CoatDuelTerminal)
                d.CoatStrength = 0.8f;
            if (d.Key == "over-rush") d.Art = ArtBroken;          // the art leaves its frame
            if (d.Key == "full-over-rush") d.Art = ArtFull;       // the art covers the whole card
            if (d.Key == "grand-master") d.Art = ArtExtended;     // extended art
        }
        return v;
    }

    void Apply(std::vector<Def>& all, const nlohmann::json& entry)
    {
        if (!entry.is_object())
            return;
        auto text = [&](const char* key) { return entry.contains(key) && entry[key].is_string() ? entry[key].get<std::string>() : std::string(); };
        Def* d = nullptr;
        if (const std::string key = text("key"); !key.empty())
            for (Def& known : all)
                if (Same(known.Key, key))
                    d = &known;
        if (!d && entry.contains("id") && entry["id"].is_number_integer())
            for (Def& known : all)
                if (known.Id == entry["id"].get<int>())
                    d = &known;
        if (!d)
        {
            if (!entry.contains("id") || !entry["id"].is_number_integer() || entry["id"].get<int>() < 1 || entry["id"].get<int>() > 255 || text("key").empty())
            {
                Logger::WriteLog("rarities.json: a new rarity needs an \"id\" (128-255) and a \"key\"", MODULE_NAME, 1);
                return;
            }
            all.push_back({});
            d = &all.back();
            d->Id = static_cast<uint8_t>(entry["id"].get<int>());
            d->Key = text("key");
            d->Name = d->Key;
        }
        if (!text("name").empty()) d->Name = text("name");
        if (entry.contains("tier") && entry["tier"].is_number()) d->Tier = entry["tier"].get<int>();
        if (entry.contains("odds") && entry["odds"].is_number()) d->Odds = (std::max)(0, entry["odds"].get<int>());
        if (const std::string slot = text("slot"); !slot.empty()) d->RollSlot = Same(slot, "rare") ? Slot::Rare : Same(slot, "any") ? Slot::Any : Slot::None;
        if (entry.contains("nameInk")) d->NameInk = Lookup(kInkNames, entry["nameInk"], d->NameInk);
        if (entry.contains("artFoil")) d->ArtFoil = Lookup(kFoilNames, entry["artFoil"], d->ArtFoil);
        if (entry.contains("frameFoil")) d->FrameFoil = Lookup(kFoilNames, entry["frameFoil"], d->FrameFoil);
        if (entry.contains("coating")) d->Coating = Lookup(kCoatNames, entry["coating"], d->Coating);
        if (entry.contains("watermark")) d->Watermark = Lookup(kMarkNames, entry["watermark"], d->Watermark);
        if (entry.contains("art")) d->Art = Lookup(kArtNames, entry["art"], d->Art);
        if (entry.contains("desaturate") && entry["desaturate"].is_number()) d->Desaturate = std::clamp(entry["desaturate"].get<float>(), 0.0f, 1.0f);
        if (entry.contains("coatStrength") && entry["coatStrength"].is_number()) d->CoatStrength = std::clamp(entry["coatStrength"].get<float>(), 0.0f, 2.0f);
        if (entry.contains("regions") && entry["regions"].is_array())
        {
            d->Regions = 0;
            for (const auto& r : entry["regions"])
                for (const auto& [name, bit] : kRegionNames)
                    if (r.is_string() && Same(name, r.get<std::string>()))
                        d->Regions |= bit;
        }
        if (const std::string colour = text("nameColour"); colour.size() == 7 && colour[0] == '#')
        {
            const unsigned long rgb = strtoul(colour.c_str() + 1, nullptr, 16);
            d->NameColour[0] = ((rgb >> 16) & 0xFF) / 255.0f; d->NameColour[1] = ((rgb >> 8) & 0xFF) / 255.0f; d->NameColour[2] = (rgb & 0xFF) / 255.0f;
        }
        if (!text("note").empty()) d->Note = text("note");
    }

    std::once_flag g_Once;
    std::vector<Def> g_All;

    void Load()
    {
        g_All = BuiltIn();
        std::vector<std::string> problems;
        const nlohmann::json root = YGO::Mods::ReadMerged("rarities.json", nullptr, &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog("rarities.json: " + problem, MODULE_NAME, 1);
        const nlohmann::json* list = root.is_object() && root.contains("rarities") ? &root["rarities"] : root.is_array() ? &root : nullptr;
        int changed = 0;
        if (list && list->is_array())
            for (const auto& entry : *list)
            {
                Apply(g_All, entry);
                ++changed;
            }
        std::sort(g_All.begin(), g_All.end(), [](const Def& a, const Def& b) { return a.Id < b.Id; });
        Logger::WriteLog(std::format("Rarities: {} ({} entr{} from rarities.json)", g_All.size(), changed, changed == 1 ? "y" : "ies"), MODULE_NAME, 0);
    }
}

const std::vector<Def>& Rarity::All()
{
    std::call_once(g_Once, Load);
    return g_All;
}

const Def* Rarity::ById(uint8_t id)
{
    for (const Def& d : All())
        if (d.Id == id)
            return &d;
    return nullptr;
}

const Def* Rarity::ByKey(const std::string& key)
{
    for (const Def& d : All())
        if (Same(d.Key, key) || Same(d.Name, key))
            return &d;
    return nullptr;
}
