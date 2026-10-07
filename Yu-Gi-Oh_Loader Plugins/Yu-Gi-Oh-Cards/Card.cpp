#include <Windows.h>
#include <algorithm>
#include <deque>
#include <cstring>
#include <format>
#include <filesystem>
#include <fstream>
#include <intrin.h>
#include <mutex>
#include <unordered_map>
#include <unordered_set>

#include <json.hpp>

#include "Card.h"
#include "Detours.h"
#include "Genres.h"
#include "Logger.h"
#include "Save.h"
#include "Text.h"
#include "Yu-Gi-Oh-Mods.h"

// The game's card id window: Konami ids 3900..14968 index its fixed-size tables.
constexpr uint32_t kVanillaKonamiIdBase = 3900;
constexpr uint32_t kVanillaKonamiIdCount = 0x2B3D;

// Entries in the table the save file keeps card ownership in.
constexpr uint32_t kSavedCardTableSize = 0x4E20;

// The unlock count table the trunk reads is sized for the vanilla cards only.
constexpr size_t kUnlockBufferSize = 0xFFFF;

// The game frees the illustration buffer with its own imported free() (IAT 0x1409F9528),
// so it has to come from the game's imported malloc, not this DLL's debug CRT.
constexpr uintptr_t kGameIatMalloc = 0x1409F9548;

static uintptr_t orig_Get_CardPropsFromInternalId = 0x1407CAB30;
static uintptr_t orig_Get_CardPropsFromKonamiId = 0x1407CAB00;
static uintptr_t orig_Get_InternalIdFromKonamiId = 0x14076E000;
static uintptr_t orig_Get_IllustrationData = 0x14086D050;
static uintptr_t orig_Get_LiveUnlockCounts = 0x1407F9120;
static uintptr_t orig_Get_ImageSlot = 0x140753BA0;
static uintptr_t orig_Deck_GetCopiesByKonamiId = 0x140756180;
static uintptr_t orig_Deck_GetCopiesBySameCardId = 0x140756140;
static uintptr_t orig_Deck_RebuildCardCountTables = 0x140755E30;
static uintptr_t orig_Deck_RebuildCardCountTables_Alt = 0x140755FB0;

// The duel-session id remap (see the design note in Card.h): Duel_LoadDeck is where an id above
// 16383 gets swapped for a borrowed vanilla one; DuelSetup_ClearState/FinishAndUpdateSave bracket
// one duel (reset the borrow pool at the start, restore real vanilla data when it ends).
static uintptr_t orig_Duel_LoadDeck = 0x1400822F0;
static uintptr_t orig_DuelSetup_ClearState = 0x14005FC90;
static uintptr_t orig_FinishAndUpdateSave = 0x14087F250;

static constexpr uintptr_t Get_NormalizedSameCardId = 0x14081A710;
static constexpr uintptr_t kDeckSectionOffsets = 0x140A521C8;    // 3 ints, used by the rebuild
static constexpr uintptr_t kDeckSectionOffsetsAlt = 0x140A4DED0; // 3 ints, used by the _Alt rebuild

static void* GameMalloc(size_t size)
{
    using Malloc_t = void* (__cdecl*)(size_t);
    Malloc_t fn = *reinterpret_cast<Malloc_t*>(kGameIatMalloc);
    return fn ? fn(size) : nullptr;
}

const std::string& ExtraCardsDirectory()
{
    static const std::string directory = Save::GameFolder() + "Yu-Gi-Oh-Ex/";
    return directory;
}

static std::wstring Utf8ToWide(const std::string& s)
{
    if (s.empty())
        return {};

    int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0);
    std::wstring w(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), w.data(), n);
    return w;
}

// ---------------------------------------------------------------------
// cards.json
//
// Every enum field accepts a name (case-insensitive) or the raw number the game uses.
// ---------------------------------------------------------------------

namespace
{
    struct NamedValue { const char* Name; int Value; };

    // The game's own kind numbers (its frame comes from the kind). Names ignore spaces and case; a raw number works too.
    const NamedValue KindNames[] = {
        { "Normal", 0 }, { "Effect", 1 }, { "Fusion", 2 }, { "FusionEffect", 3 }, { "Ritual", 4 }, { "RitualEffect", 5 },
        { "Toon", 6 }, { "Spirit", 7 }, { "Union", 8 }, { "Gemini", 9 }, { "Token", 10 }, { "Spell", 13 }, { "Trap", 14 },
        { "TunerNormal", 15 }, { "TunerEffect", 16 }, { "Synchro", 17 }, { "SynchroEffect", 18 }, { "SynchroTunerEffect", 19 },
        { "Xyz", 22 }, { "XyzEffect", 23 }, { "FlipEffect", 24 }, { "Pendulum", 25 }, { "PendulumEffect", 26 },
        { "SpecialSummonedEffect", 27 }, { "ToonEffect", 28 }, { "SpiritEffect", 29 }, { "Tuner", 30 }, { "TunerFlipEffect", 32 },
        { "PendulumTunerEffect", 33 }, { "XyzPendulumEffect", 34 }, { "PendulumFlipEffect", 35 }, { "SynchroPendulumEffect", 36 },
        { "UnionTunerEffect", 37 }, { "RitualSpiritEffect", 38 }, { "FusionTuner", 39 }, { "PendulumEffectAlt", 40 },
        { "FusionPendulumEffect", 41 }, { "Link", 42 }, { "LinkEffect", 43 }, { "PendulumTunerNormal", 44 },
        { "PendulumSpiritEffect", 45 },
    };
    const NamedValue AttributeNames[] = {
        { "Special", Card::SPECIAL }, { "Light", Card::LIGHT }, { "Dark", Card::DARK },
        { "Water", Card::WATER }, { "Fire", Card::FIRE }, { "Earth", Card::EARTH },
        { "Wind", Card::WIND }, { "Divine", Card::DIVINE }, { "Spell", Card::SPELL },
        { "Trap", Card::TRAP },
    };
    const NamedValue TypeNames[] = {
        { "Dragon", Card::Dragon }, { "Zombie", Card::Zombie }, { "Fiend", Card::Fiend },
        { "Pyro", Card::Pyro }, { "SeaSerpent", Card::SeaSerpent }, { "Rock", Card::Rock },
        { "Machine", Card::Machine }, { "Fish", Card::Fish }, { "Dinosaur", Card::Dinosaur },
        { "Insect", Card::Insect }, { "Beast", Card::Beast }, { "BeastWarrior", Card::BeastWarrior },
        { "Plant", Card::Plant }, { "Aqua", Card::Aqua }, { "Warrior", Card::Warrior },
        { "WingedBeast", Card::WingedBeast }, { "Fairy", Card::Fairy },
        { "Spellcaster", Card::Spellcaster }, { "Thunder", Card::Thunder },
        { "Reptile", Card::Reptile }, { "Psychic", Card::Psychic }, { "Wyrm", Card::Wyrm },
        { "Cyberse", Card::Cyberse }, { "DivineBeast", Card::DivineBeast },
        { "CreatorGod", Card::CreatorGod }, { "Illusion", Card::Illusion }, { "Spell", Card::Spell }, { "Trap", Card::Trap },
    };
    const NamedValue IconNames[] = {
        { "Normal", Card::I_Normal }, { "Counter", Card::I_Counter }, { "Field", Card::I_Field },
        { "Equip", Card::I_Equip }, { "Continuous", Card::I_Continuous },
        { "QuickPlay", Card::I_QuickPlay }, { "Ritual", Card::I_Ritual },
    };
    const NamedValue LimitationNames[] = {
        { "Forbidden", Card::Forbidden }, { "Limited", Card::Limited },
        { "SemiLimited", Card::SemiLimited }, { "Unlimited", Card::Unlimited },
    };

    // Names match ignoring case, spaces, hyphens and underscores: "Sea Serpent", "sea-serpent" and "SeaSerpent" are the same.
    bool SameName(const char* a, const char* b)
    {
        auto skip = [](const char*& p) { while (*p == ' ' || *p == '-' || *p == '_') ++p; };
        for (;;)
        {
            skip(a);
            skip(b);
            if (!*a || !*b)
                return !*a && !*b;
            if (tolower(static_cast<unsigned char>(*a)) != tolower(static_cast<unsigned char>(*b)))
                return false;
            ++a;
            ++b;
        }
    }

    // The game's own kind numbers that need Rank/Link/Pendulum handling (see the KindNames table above for every kind's number).
    // The frame is already correct from `kind` alone (Setup_FullCardProps's frame/subkind tables); these three groups pick which
    // extra field(s) WriteGameTableEntry fills in: Rank for Xyz, Link rating + arrows for Link, scale for any Pendulum kind.
    bool IsXyzKind(int kind) { return kind == 22 || kind == 23 || kind == 34; } // Xyz, XyzEffect, XyzPendulumEffect
    bool IsLinkKind(int kind) { return kind == 42 || kind == 43; }             // Link, LinkEffect
    bool IsPendulumKind(int kind)
    {
        switch (kind)
        {
        case 25: case 26: case 33: case 34: case 35: case 36: case 40: case 41: case 44: case 45:
            return true; // Pendulum, PendulumEffect, PendulumTunerEffect, XyzPendulumEffect, PendulumFlipEffect,
                         // SynchroPendulumEffect, PendulumEffectAlt, FusionPendulumEffect, PendulumTunerNormal, PendulumSpiritEffect
        default:
            return false;
        }
    }

    // "linkmarkers" values as ygoprodeck's API and our delta script write them, in the bit order the panel comment at
    // CardInfoPanel_UpdatePendulumScaleText/CardInfoRecord_Fill has NOT yet confirmed (see the IDA notes on that function) -
    // this order is the common fan-tool convention, unverified against the game's own LinkArrows field. Flag if arrows look wrong.
    const NamedValue LinkMarkerBits[] = {
        { "Top-Left", 0 }, { "Top", 1 }, { "Top-Right", 2 }, { "Left", 3 },
        { "Right", 4 }, { "Bottom-Left", 5 }, { "Bottom", 6 }, { "Bottom-Right", 7 },
    };

    uint32_t ParseLinkMarkers(const nlohmann::json& j)
    {
        uint32_t bits = 0;
        auto it = j.find("linkmarkers");
        if (it == j.end() || !it->is_array())
            return bits;
        for (const auto& entry : *it)
        {
            if (!entry.is_string())
                continue;
            std::string s = entry.get<std::string>();
            for (const auto& n : LinkMarkerBits)
            {
                if (SameName(n.Name, s.c_str()))
                {
                    bits |= (1u << n.Value);
                    break;
                }
            }
        }
        return bits;
    }

    // The value ParseEnum could not read, for the error message.
    std::string g_BadValue;

    std::string Unknown(const char* key)
    {
        return std::format("unknown \"{}\" value \"{}\"", key, g_BadValue);
    }

    template <size_t N>
    bool ParseEnum(const nlohmann::json& j, const char* key, const NamedValue (&names)[N], int& out)
    {
        auto it = j.find(key);
        if (it == j.end() || it->is_null())
            return true; // absent: keep the caller's default

        if (it->is_number_integer())
        {
            out = it->get<int>();
            return true;
        }

        g_BadValue = it->is_string() ? it->get<std::string>() : it->dump();
        if (it->is_string())
        {
            for (const auto& n : names)
            {
                if (SameName(n.Name, g_BadValue.c_str()))
                {
                    out = n.Value;
                    return true;
                }
            }
        }
        return false;
    }

    constexpr int kUnknownStat10 = 511;   // "?" ATK / DEF in the props (9 bits all set)

    bool ParseCard(const nlohmann::json& j, Card::ExtraCard& c, std::string& why)
    {
        using namespace Card;

        if (!j.contains("id") || !j["id"].is_number_integer())
        {
            why = "missing integer \"id\"";
            return false;
        }

        int id = j["id"].get<int>();
        if (id < kFirstExtraCardId || id > kLastExtraCardId)
        {
            why = std::format("\"id\" must be between {} and {} (the game and its unused card data own ids below, its save has no room above)", kFirstExtraCardId, kLastExtraCardId);
            return false;
        }

        c.ID = static_cast<uint16_t>(id);
        c.Name = Utf8ToWide(j.value("name", std::string("Unnamed Card")));
        c.Description = Utf8ToWide(j.value("description", std::string()));
        c.Copies = std::clamp(j.value("copies", 3), 0, 3);

        if (j.contains("archetypes") && j["archetypes"].is_array())
        {
            for (const auto& a : j["archetypes"])
            {
                if (a.is_number_integer() && a.get<int>() >= 1 && a.get<int>() <= 0xFFFF)
                    c.Archetypes.push_back(a.get<int>());
                else
                    Logger::WriteLog(std::format("Card {}: \"archetypes\" entry ignored (needs an integer archetype code, 1 or more)", id), MODULE_NAME, 1);
            }
        }

        // The effect source (Yu-Gi-Oh-Effects reads the rest of "effectClone"): the card is lent this id in a duel when it is free.
        if (j.contains("effectClone") && j["effectClone"].is_object() && j["effectClone"].contains("from") && j["effectClone"]["from"].is_number_integer())
        {
            const int from = j["effectClone"]["from"].get<int>();
            if (from >= static_cast<int>(kVanillaKonamiIdBase) && from < static_cast<int>(kVanillaKonamiIdBase + kVanillaKonamiIdCount))
                c.CloneFrom = static_cast<uint16_t>(from);
        }

        // the art is relative to the content folder the card came from (a mod's Yu-Gi-Oh-Ex or the game's)
        c.Folder = YGO::Mods::FolderOf(j);
        std::string image = j.value("image", std::string());
        if (!image.empty())
            c.ImagePath = c.Folder + image;

        int kind = K_Normal, attribute = LIGHT, type = Warrior, icon = I_Normal, limitation = Unlimited;
        if (!ParseEnum(j, "kind", KindNames, kind)) { why = Unknown("kind"); return false; }

        const bool isSpell = kind == K_Spell;
        const bool isTrap = kind == K_Trap;
        if (isSpell) { attribute = SPELL; type = Card::Spell; }
        if (isTrap) { attribute = TRAP; type = Card::Trap; }

        if (!ParseEnum(j, "attribute", AttributeNames, attribute)) { why = Unknown("attribute"); return false; }
        if (!ParseEnum(j, "type", TypeNames, type)) { why = Unknown("type"); return false; }
        if (!ParseEnum(j, "icon", IconNames, icon)) { why = Unknown("icon"); return false; }
        if (!ParseEnum(j, "limitation", LimitationNames, limitation)) { why = Unknown("limitation"); return false; }

        IN_MEMORY_CARD_PROP& p = c.Props;
        p.ID1 = c.ID;
        p.KindValue = kind;
        p.Type = static_cast<Card::Type>(type);
        p.Attribute = static_cast<Card::Attribute>(attribute);
        p.Icon = icon;
        p.Limitation = static_cast<Status>(limitation);
        p.PendulumScale = IsPendulumKind(kind) ? j.value("scale", 0) : 0;
        // "sameName": { "card": N, "always": true } (or just N, always): the props' identity id (+0x2C) and second id (+0x2E), filled the
        // way Setup_CardPropTable fills them from bin/CARD_Same.bin for the game's cards (docs/CardSame.md).
        if (j.contains("sameName"))
        {
            const auto& same = j["sameName"];
            const nlohmann::json* card = same.is_object() && same.contains("card") ? &same["card"] : same.is_number_integer() ? &same : nullptr;
            if (card && card->is_number_integer() && card->get<int>() >= 1 && card->get<int>() <= 0xFFFF && card->get<int>() != id)
            {
                c.SameName = static_cast<uint16_t>(card->get<int>());
                c.SameNameAlways = !same.is_object() || !same.contains("always") || !same["always"].is_boolean() || same["always"].get<bool>();
            }
            else
                Logger::WriteLog(std::format("Card {}: \"sameName\" ignored (needs {{ \"card\": id, \"always\": true/false }} with another card's id)", id), MODULE_NAME, 1);
        }
        p.ID2 = static_cast<short>(c.SameName && c.SameNameAlways ? c.SameName : c.ID);
        p.ID3 = static_cast<short>(c.SameName ? c.SameName : c.ID);

        // "genres": [ "DRAW", ... ] - its own, over anything genres.json says for this id
        if (j.contains("genres"))
        {
            int unknown = 0;
            c.Genres = Genres::FromJson(j["genres"], &unknown);
            if (unknown)
                Logger::WriteLog(std::format("Card {}: {} unknown genre name(s) in \"genres\" skipped", id, unknown), MODULE_NAME, 1);
        }
        // "related": [ { "card": 4007, "tag": 12 } ] - its Related cards list, over relatedcards.json's for this id (Related.cpp)
        if (j.contains("related") && j["related"].is_array())
        {
            std::vector<uint32_t> units;
            for (const auto& unit : j["related"])
            {
                if (!unit.is_object())
                    continue;
                const int card = unit.value("card", 0), tag = unit.value("tag", -1);
                if (card >= 1 && card <= 0xFFFF && tag >= 0 && tag <= 0xFFFF)
                    units.push_back(static_cast<uint32_t>(card) | static_cast<uint32_t>(tag) << 16);
            }
            std::stable_sort(units.begin(), units.end(), [](uint32_t a, uint32_t b) { return (a & 0xFFFF) < (b & 0xFFFF); });   // the game's order
            c.Related = std::move(units);
        }
        // "password": 8 digits (Yu-Gi-Oh-BetterCardShop's Enter Password page, through Card_FindByPassword)
        if (j.contains("password") && j["password"].is_number_integer() && j["password"].get<int64_t>() > 0 && j["password"].get<int64_t>() <= 99999999)
            c.Password = static_cast<uint32_t>(j["password"].get<int64_t>());
        // the texts per language: English's index letters / sort name at the top, the other languages under "text"
        auto readText = [](const nlohmann::json& from, ExtraCard::LanguageText& to, bool names)
        {
            auto text = [&](const char* key) { return from.contains(key) && from[key].is_string() ? Utf8ToWide(from[key].get<std::string>()) : std::wstring(); };
            if (names)
            {
                to.Name = text("name");
                to.Description = text("description");
            }
            to.IndexLetters = text("indexLetters");
            to.SortAs = text("sortAs");
        };
        {
            ExtraCard::LanguageText english;
            readText(j, english, false);
            if (!english.IndexLetters.empty() || !english.SortAs.empty())
                c.Texts['E'] = std::move(english);
        }
        if (j.contains("text") && j["text"].is_object())
            for (const auto& [letter, entry] : j["text"].items())
                if (!letter.empty() && entry.is_object())
                {
                    const char language = static_cast<char>(std::toupper(static_cast<unsigned char>(letter[0])));
                    if (language != 'E')
                        readText(entry, c.Texts[language], true);
                }

        if (isSpell || isTrap)
        {
            p.StarTypeValue = ST_None;
            p.LevelOrLinkRatingOrRank = 0;
            p.Attack10 = p.ArrowsOrDefense10 = 0;
        }
        else
        {
            // The game stores ATK/DEF divided by 10; "?" is 511 (the 9 bits all set, as in bin/CARD_Prop.bin: Get_RawAttackFromKonamiId
            // reads it as 0xFFFF, Get_EffectiveAttackFromKonamiId as 0).
            auto stat = [&](const char* key) { return j.contains(key) && j[key].is_string() && j[key].get<std::string>() == "?" ? kUnknownStat10 : j.value(key, 0) / 10; };
            p.Attack10 = stat("atk");
            // Link monsters have no DEF: this field doubles as the LinkArrows bitmask for them (the game reuses
            // the same 9 bits either way, per KONAMI_ID_CARD_PROPS's bit-packed CARD_Prop.bin layout).
            p.ArrowsOrDefense10 = IsLinkKind(kind) ? static_cast<int>(ParseLinkMarkers(j)) : stat("def");
            p.StarTypeValue = IsXyzKind(kind) ? ST_Rank : IsLinkKind(kind) ? ST_LinkRating : ST_Level;
            p.LevelOrLinkRatingOrRank = j.value("level", 1);
        }
        return true;
    }

    bool IsGameCardId(int id)
    {
        return id >= static_cast<int>(kVanillaKonamiIdBase) && id < static_cast<int>(kVanillaKonamiIdBase + kVanillaKonamiIdCount);
    }

    // An entry for a card the game has: keeps only the fields that are listed.
    bool ParseOverride(const nlohmann::json& j, Card::CardOverride& o, std::string& why)
    {
        using namespace Card;
        o.ID = j["id"].get<int>();

        if (j.contains("name") && j["name"].is_string())
        {
            o.HasName = true;
            o.Name = Utf8ToWide(j["name"].get<std::string>());
        }
        if (j.contains("description") && j["description"].is_string())
        {
            o.HasDescription = true;
            o.Description = Utf8ToWide(j["description"].get<std::string>());
        }

        auto number = [&](const char* key, std::optional<int>& out)
        {
            if (j.contains(key) && j[key].is_number_integer())
                out = j[key].get<int>();
        };
        number("atk", o.Attack);
        number("def", o.Defense);
        number("level", o.Level);

        auto named = [&](const char* key, const NamedValue* names, size_t count, std::optional<int>& out)
        {
            auto it = j.find(key);
            if (it == j.end() || it->is_null())
                return true;
            if (it->is_number_integer())
            {
                out = it->get<int>();
                return true;
            }
            if (it->is_string())
            {
                const std::string s = it->get<std::string>();
                for (size_t i = 0; i < count; ++i)
                {
                    if (SameName(names[i].Name, s.c_str()))
                    {
                        out = names[i].Value;
                        return true;
                    }
                }
            }
            g_BadValue = it->is_string() ? it->get<std::string>() : it->dump();
            why = Unknown(key);
            return false;
        };
        return named("kind", KindNames, std::size(KindNames), o.Kind)
            && named("attribute", AttributeNames, std::size(AttributeNames), o.Attribute)
            && named("type", TypeNames, std::size(TypeNames), o.Type)
            && named("icon", IconNames, std::size(IconNames), o.Icon);
    }
}

namespace Card
{
    std::vector<CardOverride> Overrides;
    std::vector<ExtraCard> ExtraCards;
    std::vector<IN_MEMORY_CARD_PROP> CardProps;
    std::unordered_map<uint16_t, int64_t> ExtraLoadIDs;

    // Implemented after WriteGameTableEntry, below (see the design note in Card.h and the
    // "ygo-duel-id-remap-plan" memory) - it needs the GameCard struct and that function.

    // id -> index into ExtraCards. FindExtraCard used to scan ExtraCards linearly; with a few hundred custom
    // cards that was invisible, but Hook_Get_LiveUnlockCounts calls it once per card while rebuilding unlock
    // counts, so at ~4000 cards that was ~4000 x 4000 comparisons every time the trunk/unlock table rebuilt -
    // the "trunk load chugs" the user reported. Rebuilt whenever ExtraCards is (re)loaded.
    static std::unordered_map<uint16_t, size_t> g_ExtraCardIndex;

    // archetype code -> custom card ids in it, for the Is_CardInNamedArchetype hook (hot: called per candidate card
    // per timing check inside duels, so it is a hash lookup, not a scan of ExtraCards).
    static std::unordered_map<int, std::unordered_set<uint16_t>> g_ArchetypeMembers;

    static void RebuildExtraCardIndex()
    {
        g_ExtraCardIndex.clear();
        g_ExtraCardIndex.reserve(ExtraCards.size());
        g_ArchetypeMembers.clear();
        for (size_t i = 0; i < ExtraCards.size(); ++i)
        {
            g_ExtraCardIndex[ExtraCards[i].ID] = i;
            for (int code : ExtraCards[i].Archetypes)
                g_ArchetypeMembers[code].insert(ExtraCards[i].ID);
        }
    }

    static ExtraCard* FindExtraCard(uint16_t id)
    {
        auto it = g_ExtraCardIndex.find(id);
        return it != g_ExtraCardIndex.end() ? &ExtraCards[it->second] : nullptr;
    }

    // Kept for the one caller (the duplicate-id check during load, before the index exists yet) that must
    // still see cards as they're added one at a time.
    static ExtraCard* FindExtraCardLinear(uint16_t id)
    {
        for (auto& c : ExtraCards)
        {
            if (c.ID == id)
                return &c;
        }
        return nullptr;
    }

    // "#22 (id 14991, "Jongleur-Ghoul Illusionist")" for the log.
    static std::string CardLabel(size_t index, const nlohmann::json& entry)
    {
        std::string id = entry.is_object() && entry.contains("id") ? entry["id"].dump() : "?";
        std::string name = entry.is_object() && entry.contains("name") && entry["name"].is_string() ? entry["name"].get<std::string>() : "?";
        return std::format("#{} (id {}, \"{}\")", index, id, name);
    }

    size_t LoadCardsFromJson(const std::string& path)
    {
        // Every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h); either {"cards": [...]} or a bare array.
        std::vector<std::string> problems;
        nlohmann::json root = YGO::Mods::ReadMerged(path, "cards", &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", its cards are left out", MODULE_NAME, 2);
        if (root.is_null())
        {
            Logger::WriteLog("No " + path + " in Yu-Gi-Oh-Ex or a mod", MODULE_NAME, 1);
            return 0;
        }

        const nlohmann::json& list = root["cards"];
        if (!list.is_array())
        {
            Logger::WriteLog(path + " needs a \"cards\" array", MODULE_NAME, 2);
            return 0;
        }

        ExtraCards.clear();
        ExtraCards.reserve(list.size());
        Overrides.clear();
        Overrides.reserve(list.size());
        // id -> place in ExtraCards: O(1) duplicate check (a linear scan per card here made loading a few thousand cards visibly chug)
        std::unordered_map<uint16_t, size_t> seenIds;
        seenIds.reserve(list.size());
        for (size_t i = 0; i < list.size(); ++i)
        {
            ExtraCard c{};
            std::string why;

            // The id of a card the game has: change that card rather than adding one.
            if (list[i].contains("id") && list[i]["id"].is_number_integer() && IsGameCardId(list[i]["id"].get<int>()))
            {
                CardOverride o;
                if (!ParseOverride(list[i], o, why))
                    Logger::WriteLog(std::format("Skipped card {}: {}", CardLabel(i, list[i]), why), MODULE_NAME, 2);
                else
                    Overrides.push_back(std::move(o));
                continue;
            }

            if (!ParseCard(list[i], c, why))
            {
                Logger::WriteLog(std::format("Skipped card {}: {}", CardLabel(i, list[i]), why), MODULE_NAME, 2);
                continue;
            }

            // The same id twice: in one file it is a mistake (the first stays); from a later mod (or the game folder over a mod) the later one
            // wins, the way every other mod file works. The Mod Manager warns about it before the game starts.
            if (auto seen = seenIds.find(c.ID); seen != seenIds.end())
            {
                const std::string& earlier = ExtraCards[seen->second].Folder;
                if (earlier == c.Folder)
                {
                    Logger::WriteLog(std::format("Skipped card {}: duplicate id {}", CardLabel(i, list[i]), c.ID), MODULE_NAME, 2);
                    continue;
                }
                Logger::WriteLog(std::format("Card id {} from {} replaces the one from {} (two mods use the same id)", c.ID, c.Folder, earlier),
                                 MODULE_NAME, 2);
                ExtraCards[seen->second] = std::move(c);
                continue;
            }

            seenIds.emplace(c.ID, ExtraCards.size());
            ExtraCards.push_back(std::move(c));
        }
        RebuildExtraCardIndex();
        return ExtraCards.size();
    }

    std::vector<Unlock> Unlocks;
    bool ReplaceDefaultUnlocks = false;

    size_t LoadUnlocksFromJson(const std::string& path)
    {
        // every mod's copy and the game folder's, merged (Yu-Gi-Oh-Mods.h)
        std::vector<std::string> problems;
        nlohmann::json root = YGO::Mods::ReadMerged(path, "cards", &problems);
        for (const std::string& problem : problems)
            Logger::WriteLog(problem + ", its unlocks are left out", MODULE_NAME, 2);
        if (root.is_null())
            return 0; // unlocks.json is optional

        const nlohmann::json& list = root["cards"];
        if (!list.is_array())
        {
            Logger::WriteLog(path + " needs a \"cards\" array", MODULE_NAME, 2);
            return 0;
        }

        ReplaceDefaultUnlocks = root.is_object() ? root.value("replaceDefaults", true) : true;

        Unlocks.clear();
        for (size_t i = 0; i < list.size(); ++i)
        {
            const nlohmann::json& entry = list[i];
            if (!entry.contains("id") || !entry["id"].is_number_integer())
            {
                Logger::WriteLog(std::format("Skipped unlock #{}: missing integer \"id\"", i), MODULE_NAME, 2);
                continue;
            }

            int id = entry["id"].get<int>();
            if (id < 1 || id > kLastExtraCardId)
            {
                Logger::WriteLog(std::format("Skipped unlock #{}: id {} is out of range", i, id), MODULE_NAME, 2);
                continue;
            }

            Unlocks.push_back({ id, std::clamp(entry.value("copies", 3), 0, 3) });
        }
        return Unlocks.size();
    }

}

// ---------------------------------------------------------------------
// The game's per card table entry (FULL_CARD_PROPS, 0xA0 bytes each)
// ---------------------------------------------------------------------

namespace
{
    struct GameCard
    {
        bool ExistsOnDuel; char pad0[7];
        wchar_t* Name; wchar_t* Description;
        uint16_t NameSortRank;                 // +0x18: place in the language's name order (CARD_Sort_#, YGO::CARDS::Get_NameSortRank)
        wchar_t IndexInitial;                  // +0x1A: the name's first letter (Get_CardIndexInitialFromKonamiId; CARD_Kana1_# in the Japanese build)
        bool IsMonster, IsSpell, IsTrap, IsFieldSpell, IsNormalMonster, IsEffectMonster,
            IsFusion, IsSynchro; char pad2;
        bool IsXyz, IsExtraMonster, IsRitual, IsToken; char pad3;
        bool IsToon, IsSpirit, IsGemini; char pad4[2];
        bool IsPendulum; char pad5;
        bool IsLink; char pad6[6];
        uint64_t Genre;
        int32_t SoundPoolName, Attack1, Attack2;
        Card::Attribute CardAttribute;
        int32_t Defense1, Defense2, Icon, Kind, Level, Limitation, ID1, ID2, ID3, Rank;
        int32_t LeftPendulumScale, RightPendulumScale, LevelOrLinkRatingOrRank;
        Card::Type CardType;
        int32_t LinkRating; uint32_t LinkArrows;
        int32_t Valid, Frame; int32_t pad7[2];
    };
    static_assert(sizeof(GameCard) == kFullCardPropsStride, "GameCard layout drifted");

    // Page protection is deliberately not restored (see Limit.cpp).
    void ApplyBytePatch(uintptr_t address, const unsigned char* bytes, size_t len)
    {
        void* target = reinterpret_cast<void*>(address);
        DWORD oldProtect = 0;
        if (!VirtualProtect(target, len, PAGE_EXECUTE_READWRITE, &oldProtect))
            return;

        std::memcpy(target, bytes, len);
    }

    void PatchImm32(uintptr_t address, uint32_t value)
    {
        ApplyBytePatch(address, reinterpret_cast<const unsigned char*>(&value), sizeof(value));
    }

    // A custom card's place in the name order. The game sorts the trunk by name with each card's rank (FULL_CARD_PROPS +0x18, from
    // CARD_Sort_#); CARD_Sort2_# (card database +0x50 size, +0x58 data) lists the internal ids in that order, case-insensitive
    // alphabetical (Japanese by reading). A custom card takes the rank of the first game card whose name comes after its own, so it
    // sorts in among them; without this it kept rank 0 and sorted before every card.
    uint16_t NameSortRankFor(const std::wstring& name)
    {
        constexpr uintptr_t kCardDatabase = 0x140C8D3A0;               // pointer to the card database (stru_140C8D3A0.pvoid0)
        using KonamiFromInternal_t = int64_t(__fastcall*)(unsigned int);
        using NameFromKonami_t = const wchar_t*(__fastcall*)(int);
        auto konamiFromInternal = reinterpret_cast<KonamiFromInternal_t>(0x14076D7F0);   // YGO::CARDS::Get_KonamiIdFromInternalId
        auto nameFromKonami = reinterpret_cast<NameFromKonami_t>(0x14076D0F0);           // YGO::CARDS::Get_CardNameFromKonamiId

        auto* db = *reinterpret_cast<const uint8_t* const*>(kCardDatabase);
        if (!db)
            return 0;
        const uint64_t bytes = *reinterpret_cast<const uint64_t*>(db + 0x50);
        const auto* order = *reinterpret_cast<const uint16_t* const*>(db + 0x58);
        if (!order || bytes < 4)
            return 0;

        // position 0 is internal id 0 (no card); binary search 1..count for the first name after ours
        size_t lo = 1, hi = static_cast<size_t>(bytes / 2);
        while (lo < hi)
        {
            size_t mid = (lo + hi) / 2;
            const wchar_t* other = nameFromKonami(static_cast<int>(konamiFromInternal(order[mid])));
            if (_wcsicmp(other ? other : L"", name.c_str()) <= 0)
                lo = mid + 1;
            else
                hi = mid;
        }
        return static_cast<uint16_t>(lo);
    }

    void WriteGameTableEntry(const Card::ExtraCard& c)
    {
        auto* gc = reinterpret_cast<GameCard*>(kFullCardPropsAddress + static_cast<uintptr_t>(c.ID) * kFullCardPropsStride);
        const Card::IN_MEMORY_CARD_PROP& props = c.Props;
        const int kind = props.KindValue;

        int16_t frame = *reinterpret_cast<int16_t*>(kFrameTableAddress + 0xC * static_cast<uintptr_t>(kind));
        uint16_t subKind = *reinterpret_cast<uint16_t*>(kSubKindTableAddress + 0xC * static_cast<uintptr_t>(kind));

        // the game's current language: its own name / text when cards.json gives them ("text"), else the English ones
        const Card::ExtraCard::LanguageText* own = nullptr;
        const Card::ExtraCard::LanguageText* english = nullptr;
        if (auto it = c.Texts.find(Text::CurrentLanguage()); it != c.Texts.end() && it->first != 'E')
            own = &it->second;
        if (auto it = c.Texts.find('E'); it != c.Texts.end())
            english = &it->second;
        const bool ownName = own && !own->Name.empty();
        const std::wstring& name = ownName ? own->Name : c.Name;
        const std::wstring& description = own && !own->Description.empty() ? own->Description : c.Description;
        // "sortAs" / "indexLetters" of that language; a language with its own name but none of those goes by its name, else English's
        auto pick = [&](std::wstring Card::ExtraCard::LanguageText::*field) -> const std::wstring*
        {
            if (own && !(own->*field).empty())
                return &(own->*field);
            if (!ownName && english && !(english->*field).empty())
                return &(english->*field);
            return nullptr;
        };
        const std::wstring* sortAs = pick(&Card::ExtraCard::LanguageText::SortAs);
        const std::wstring* indexLetters = pick(&Card::ExtraCard::LanguageText::IndexLetters);

        gc->Name = const_cast<wchar_t*>(name.c_str());
        gc->Description = const_cast<wchar_t*>(description.c_str());
        gc->NameSortRank = NameSortRankFor(sortAs ? *sortAs : name);
        // Setup_FullCardProps ran before the card had a name, so it has no index initial: "indexLetters" when given, else the game's rule outside
        // the Japanese build, the name's first character after a leading $R...( ruby block (docs/CardKana.md)
        if (indexLetters)
            gc->IndexInitial = (*indexLetters)[0];
        else
        {
            size_t start = 0;
            if (name.starts_with(L"$R"))
                if (size_t open = name.find(L'('); open != std::wstring::npos)
                    start = open + 1;
            gc->IndexInitial = start < name.size() ? name[start] : L'\0';
        }

        // IsMonster/IsSpell/IsTrap/IsFusion/IsSynchro/IsXyz/IsExtraMonster/IsRitual/IsToken/IsToon/IsSpirit/IsGemini/IsPendulum/IsLink
        // are not set here: the kFlagFunctions/kLateFlagFunctions loop below overwrites those exact bytes with the game's own
        // per-kind computation, so anything written here first would just be discarded.
        gc->IsFieldSpell = props.Icon == Card::I_Field;
        gc->IsNormalMonster = subKind == Card::SK_Normal;
        gc->IsEffectMonster = subKind == Card::SK_Effect;

        // Attack1/Defense1 = effective (FULL_CARD_PROPS +0x44/+0x50), Attack2/Defense2 = raw (+0x48/+0x54); "?" (511) is 0 and 0xFFFF as
        // Get_EffectiveAttackFromKonamiId / Get_RawAttackFromKonamiId make it for the game's cards
        auto effective = [](int stat10) { return stat10 == kUnknownStat10 ? 0 : stat10 * 10; };
        auto raw = [](int stat10) { return stat10 == kUnknownStat10 ? 0xFFFF : stat10 * 10; };
        gc->Attack1 = effective(props.Attack10);
        gc->Attack2 = raw(props.Attack10);
        gc->CardAttribute = props.Attribute;
        gc->Defense1 = IsLinkKind(kind) ? 0 : effective(props.ArrowsOrDefense10); // Link monsters have no DEF
        gc->Defense2 = IsLinkKind(kind) ? 0 : raw(props.ArrowsOrDefense10);
        gc->Icon = props.Icon;
        gc->Kind = kind;
        gc->Level = (props.StarTypeValue == Card::ST_Level) ? props.LevelOrLinkRatingOrRank : 0;
        gc->Limitation = props.Limitation;
        // ID2/ID3 = FULL_CARD_PROPS +0x6C/+0x70, the copies Setup_FullCardProps makes of the props' identity / same-name ids ("sameName")
        gc->ID1 = c.ID;
        gc->ID2 = c.SameName && c.SameNameAlways ? c.SameName : c.ID;
        gc->ID3 = c.SameName ? c.SameName : c.ID;
        gc->Rank = IsXyzKind(kind) ? props.LevelOrLinkRatingOrRank : 0;
        gc->LeftPendulumScale = gc->RightPendulumScale = IsPendulumKind(kind) ? props.PendulumScale : 0;
        gc->LevelOrLinkRatingOrRank = props.LevelOrLinkRatingOrRank;
        gc->CardType = props.Type;
        gc->LinkRating = IsLinkKind(kind) ? props.LevelOrLinkRatingOrRank : 0;
        gc->LinkArrows = IsLinkKind(kind) ? static_cast<uint32_t>(props.ArrowsOrDefense10) : 0; // bit order unconfirmed, see ParseLinkMarkers
        gc->Valid = 1;
        gc->Frame = frame;
        // Setup_FullCardProps gave ids past the card tables entry 0's genres (none); a borrowed duel id still has the vanilla card's, which
        // BorrowScratchId replaces with the custom card's own afterwards (this copy's ID is the borrowed one).
        gc->Genre = c.Genres ? *c.Genres : Genres::MaskFor(c.ID, c.ID >= kFirstExtraCardId ? 0 : gc->Genre);

        // Setup_FullCardProps computed every derived flag while this id was still outside the card tables: Is_ValidCardId is false for
        // ids >= 14969 and the card type came from entry 0 (a token), so the duel treated the card as a token with no "Show Info".
        // Now that the kind is written, ask the game's own functions again, the same ones Setup_FullCardProps calls.
        const int id = c.ID;
        auto call = [id](uintptr_t address) { return reinterpret_cast<int64_t(__fastcall*)(int)>(address)(id); };
        auto* bytes = reinterpret_cast<unsigned char*>(gc);
        static constexpr uintptr_t kFlagFunctions[] = { // FULL_CARD_PROPS bytes 0x1C..0x28, 0x2A..0x31 (0x29 is separate)
            0x140742DF0, 0x140742E30, 0x140742E50, 0x140742E70, 0x140742E90, 0x140742EC0, 0x140742EF0, 0x140742F20,
            0x140742F50, 0x140742F80, 0x140742FB0, 0x140742FE0, 0x140743010 };
        for (size_t i = 0; i < std::size(kFlagFunctions); ++i)
            bytes[0x1C + i] = call(kFlagFunctions[i]) != 0;
        bytes[0x29] = call(0x140743030) != 0;
        static constexpr uintptr_t kLateFlagFunctions[] = {
            0x1407430C0, 0x1407430F0, 0x1407431A0, 0x1407431D0, 0x140743200, 0x140743230, 0x140743260, 0x1407432A0 };
        for (size_t i = 0; i < std::size(kLateFlagFunctions); ++i)
            bytes[0x2A + i] = call(kLateFlagFunctions[i]) != 0;
        *reinterpret_cast<int32_t*>(bytes + 0x94) = static_cast<int32_t>(call(0x140742C40));
        *reinterpret_cast<int32_t*>(bytes + 0x98) = static_cast<int32_t>(call(0x140743C30));
    }

    // ---------------------------------------------------------------------
    // Duel-session id remapping (see the design note in Card.h and the "ygo-duel-id-remap-plan"
    // memory). A card above kDuelIdLimit borrows a real, unused vanilla id for one duel; the
    // borrowed id's FULL_CARD_PROPS entry is overwritten with the custom card's data via
    // WriteGameTableEntry, so the duel engine (14 bits of id only) is handed something
    // completely ordinary and never has to be lied to about anything but the number.
    // ---------------------------------------------------------------------

    constexpr uint16_t kDuelIdLimit = 0x3FFF; // 16383: the duel engine keeps only 14 bits of an id

    std::unordered_map<uint16_t, uint16_t> g_HighToBorrowed;   // real custom id -> borrowed vanilla id, this duel only
    std::unordered_map<uint16_t, uint16_t> g_BorrowedToHigh;   // reverse of the above
    std::unordered_map<uint16_t, std::vector<unsigned char>> g_SavedVanillaBytes; // borrowed id -> its real bytes, to restore
    std::unordered_map<uint16_t, Card::ExtraCard> g_BorrowedCopy; // borrowed id -> the ExtraCard copy WriteGameTableEntry's gc->Name/
                                                                    // Description point into (temp.Name.c_str()) - must outlive the borrow
    std::unordered_set<uint16_t> g_IdsInUseThisDuel;           // every id (real, already borrowed, or a vanilla card in either deck)
                                                                // seen so far this duel, so a new borrow never collides with it

    // Undoes every current borrow: restores each borrowed id's real vanilla bytes and clears the session state.
    // Called defensively at the start of a new duel (in case a previous duel's end was missed) and for real
    // when a duel actually finishes (Hook_FinishAndUpdateSave), before the trunk/unlock counts get rebuilt.
    void RestoreAndClearDuelSessionRemap()
    {
        for (auto& [borrowed, bytes] : g_SavedVanillaBytes)
        {
            auto* gc = reinterpret_cast<GameCard*>(kFullCardPropsAddress + static_cast<uintptr_t>(borrowed) * kFullCardPropsStride);
            std::memcpy(gc, bytes.data(), kFullCardPropsStride);
        }
        if (!g_SavedVanillaBytes.empty())
            Logger::WriteLog(std::format("Duel: restored {} borrowed vanilla card id(s)", g_SavedVanillaBytes.size()), MODULE_NAME, 0);
        g_HighToBorrowed.clear();
        g_BorrowedToHigh.clear();
        g_SavedVanillaBytes.clear();
        g_BorrowedCopy.clear();
        g_IdsInUseThisDuel.clear();
    }

    // Finds a vanilla id nothing this duel is using yet, makes it look exactly like `card` (name, art, stats,
    // frame - via WriteGameTableEntry), and returns it. The same custom id always gets the same borrowed id
    // for the rest of the duel. Returns the card's own (too-high) id, unchanged, if there is truly nothing
    // free to borrow (should not happen: a duel uses on the order of 100-200 unique ids out of 10166 vanilla ones).
    // The engine treats a borrowed id exactly like the vanilla card that owns it in every hard-coded id test, so the lent id must be one nothing
    // special-cases. 3900..4006 are Tokens (3901 Kuriboh Token / 3902.. Sheep Tokens "cannot be used as a Tribute": a custom monster lent 3901
    // could not be Tributed, 2026-10-01) and 4007 is Blue-Eyes White Dragon, which support cards name. First choice: these 51 Normal monsters that
    // appear nowhere in the exe - no effect table row, no id list or table, no deck/filter row param, no code immediate (found by scanning
    // YuGiOh.exe, docs/EffectSystem.md section 29). After them: any id from 4008 up that is not a Token.
    constexpr uint16_t kSafeBorrowIds[] = {
        4034, 4037, 4085, 4127, 4186, 4219, 4266, 4289, 4357, 4359, 4442, 4450, 4455, 4523, 4593, 4627, 4633, 4638, 4709, 4711, 4741, 4882, 5004,
        5018, 5086, 5144, 5337, 5349, 5508, 5641, 5643, 5813, 5946, 6021, 6175, 6383, 6413, 6497, 7191, 7540, 7821, 9719, 9813, 9863, 10024,
        11785, 13057, 13058, 13192, 13570, 13989 };
    constexpr uint32_t kFirstFallbackBorrowId = 4008;   // after the Tokens (3900..4006) and Blue-Eyes White Dragon (4007)
    constexpr int kTokenKind = 10;

    // Makes vanilla id `id16` look exactly like `card` for this duel (name, art, stats, frame, genres) and records the borrow.
    uint16_t BorrowInto(uint16_t customId, const Card::ExtraCard& card, uint16_t id16)
    {
        auto* gc = reinterpret_cast<GameCard*>(kFullCardPropsAddress + static_cast<uintptr_t>(id16) * kFullCardPropsStride);
        auto& saved = g_SavedVanillaBytes[id16];
        saved.assign(reinterpret_cast<unsigned char*>(gc), reinterpret_cast<unsigned char*>(gc) + kFullCardPropsStride);

        // WriteGameTableEntry points gc->Name/Description at this copy's own strings, so the copy must
        // live at least as long as the borrow does - g_BorrowedCopy, not a local, is what keeps it alive.
        Card::ExtraCard& temp = g_BorrowedCopy[id16] = card;
        temp.ID = id16;
        WriteGameTableEntry(temp);
        gc->Genre = card.Genres ? *card.Genres : Genres::MaskFor(customId, 0);   // the custom card's genres, not the borrowed vanilla card's (the duel AI checks them)

        g_HighToBorrowed[customId] = id16;
        g_BorrowedToHigh[id16] = customId;
        g_IdsInUseThisDuel.insert(id16);
        Logger::WriteLog(std::format("Duel: card {} borrows vanilla id {} for this duel{}", customId, id16, id16 == card.CloneFrom ? " (its effect source)" : ""), MODULE_NAME, 1);
        return id16;
    }

    uint16_t BorrowScratchId(uint16_t highId)
    {
        auto already = g_HighToBorrowed.find(highId);
        if (already != g_HighToBorrowed.end())
            return already->second;

        const Card::ExtraCard* card = Card::FindExtraCard(highId);
        if (!card)
            return highId; // not one of ours (or cards.json no longer has it) - nothing we can do here

        std::vector<uint16_t> candidates(std::begin(kSafeBorrowIds), std::end(kSafeBorrowIds));
        for (uint32_t id = kFirstFallbackBorrowId; id < kVanillaKonamiIdBase + kVanillaKonamiIdCount; ++id)
            candidates.push_back(static_cast<uint16_t>(id));

        for (const uint16_t id16 : candidates)
        {
            if (g_IdsInUseThisDuel.contains(id16))
                continue;
            if (static_cast<int>(reinterpret_cast<GameCard*>(kFullCardPropsAddress + static_cast<uintptr_t>(id16) * kFullCardPropsStride)->Kind) == kTokenKind)
                continue;
            return BorrowInto(highId, *card, id16);
        }

        Logger::WriteLog(std::format("Duel: no free id left to borrow for card {}, it will not be correct this duel", highId), MODULE_NAME, 2);
        return highId;
    }

    // A clone of a vanilla card ("effectClone": { "from": N }) plays under N itself when N is in neither deck and no other custom card took it:
    // every id-keyed rule of the engine (about 290 id lists, per-card ladders, phase tables, continuous effect tables, the AI's knowledge) then
    // treats it as the source with no hook at all, while it shows its own name, art and stats. Yu-Gi-Oh-Effects still maps the id back to the
    // custom card (Card_GetRealIdForBorrowed) for its parameter overrides. Returns 0 when N is not free (the caller falls back to the usual path).
    uint16_t BorrowSourceId(uint16_t customId, const Card::ExtraCard& card)
    {
        auto already = g_HighToBorrowed.find(customId);
        if (already != g_HighToBorrowed.end())
            return already->second;
        const uint16_t source = card.CloneFrom;
        if (!source || g_IdsInUseThisDuel.contains(source) || g_BorrowedToHigh.contains(source))
            return 0;
        return BorrowInto(customId, card, source);
    }

    // Changes the game's own entry for a card (both the display table and the card props the game reads), listed fields only.
    void ApplyOverride(const Card::CardOverride& o)
    {
        auto* gc = reinterpret_cast<GameCard*>(kFullCardPropsAddress + static_cast<uintptr_t>(o.ID) * kFullCardPropsStride);
        auto* props = reinterpret_cast<Card::IN_MEMORY_CARD_PROP*>(
            reinterpret_cast<int64_t(__fastcall*)(int16_t)>(orig_Get_CardPropsFromKonamiId)(static_cast<int16_t>(o.ID)));

        if (o.HasName)
            gc->Name = const_cast<wchar_t*>(o.Name.c_str());
        if (o.HasDescription)
            gc->Description = const_cast<wchar_t*>(o.Description.c_str());

        if (o.Attack)
        {
            gc->Attack1 = gc->Attack2 = *o.Attack;
            if (props) props->Attack10 = *o.Attack / 10;
        }
        if (o.Defense)
        {
            gc->Defense1 = gc->Defense2 = *o.Defense;
            if (props) props->ArrowsOrDefense10 = *o.Defense / 10;
        }
        if (o.Level)
        {
            gc->Level = gc->LevelOrLinkRatingOrRank = *o.Level;
            if (props) props->LevelOrLinkRatingOrRank = *o.Level;
        }
        if (o.Attribute)
        {
            gc->CardAttribute = static_cast<Card::Attribute>(*o.Attribute);
            if (props) props->Attribute = static_cast<Card::Attribute>(*o.Attribute);
        }
        if (o.Type)
        {
            gc->CardType = static_cast<Card::Type>(*o.Type);
            if (props) props->Type = static_cast<Card::Type>(*o.Type);
        }
        if (o.Icon)
        {
            gc->Icon = *o.Icon;
            gc->IsFieldSpell = *o.Icon == Card::I_Field;
            if (props) props->Icon = *o.Icon;
        }
        if (o.Kind)
        {
            const int kind = *o.Kind;
            gc->Kind = kind;
            gc->Frame = *reinterpret_cast<int16_t*>(kFrameTableAddress + 0xC * static_cast<uintptr_t>(kind));
            const uint16_t subKind = *reinterpret_cast<uint16_t*>(kSubKindTableAddress + 0xC * static_cast<uintptr_t>(kind));
            gc->IsMonster = subKind != Card::SK_None;
            gc->IsSpell = kind == Card::K_Spell;
            gc->IsTrap = kind == Card::K_Trap;
            gc->IsNormalMonster = subKind == Card::SK_Normal;
            gc->IsEffectMonster = subKind == Card::SK_Effect;
            if (props) props->KindValue = kind;
        }
    }
}

uint16_t Card::ResolveDuelSessionId(uint16_t id)
{
    if (const Card::ExtraCard* card = Card::FindExtraCard(id); card && card->CloneFrom)
    {
        if (const uint16_t source = BorrowSourceId(id, *card))
            return source;
    }
    if (id <= kDuelIdLimit)
    {
        g_IdsInUseThisDuel.insert(id); // a real (or already-resolved) id in a deck: keep future borrows away from it
        return id;
    }
    return BorrowScratchId(id);
}

uint16_t Card::GetActiveDuelSessionId(uint16_t id)
{
    auto it = g_HighToBorrowed.find(id);
    return it != g_HighToBorrowed.end() ? it->second : id;
}

// Cross-DLL entry point for Card::GetActiveDuelSessionId (see its doc comment in Card.h). Another plugin's
// DLL (Yu-Gi-Oh-Funky's DuelTest.cpp, so far) finds this by name via GetProcAddress, the same pattern
// Yu-Gi-Oh-Console's WriteLog already uses. Pure lookup, safe to poll anytime.
extern "C" __declspec(dllexport) unsigned short __cdecl Card_GetActiveDuelSessionId(unsigned short id)
{
    return Card::GetActiveDuelSessionId(id);
}

// Cross-DLL entry point for Card::ResolveDuelSessionId. Unlike the lookup above, this one CAN create a new
// borrow (overwriting a real vanilla card's FULL_CARD_PROPS entry) as a side effect - only call it for an id
// that is genuinely about to enter a duel this way, right before writing it into a deck struct, the same
// moment Hook_Duel_LoadDeck itself would. Added because plugin load order puts Yu-Gi-Oh-Funky's own
// Duel_LoadDeck hook UNDERNEATH this plugin's in the detour chain (Funky attaches first => it is the inner
// hook => anything it appends to the deck after calling through happens after this plugin already resolved
// the deck once, so it is never seen by Hook_Duel_LoadDeck at all) - a card Funky's debug tool injects must
// resolve itself, here, before it is ever written into the deck struct.
extern "C" __declspec(dllexport) unsigned short __cdecl Card_ResolveDuelSessionId(unsigned short id)
{
    return Card::ResolveDuelSessionId(id);
}

uint16_t Card::FindByPassword(uint32_t password)
{
    if (password == 0)
        return 0;
    for (const ExtraCard& card : ExtraCards)
        if (card.Password == password)
            return card.ID;
    return 0;
}

// A custom card's password ("password" in cards.json): YuGiOh-PASS.h's FindCardByPassword asks this when bin/CARD_Pass.bin has no card
// with it (that file is indexed by internal id and ends with the game's cards). 0 = no custom card has it.
extern "C" __declspec(dllexport) unsigned short __cdecl Card_FindByPassword(unsigned int password)
{
    return Card::FindByPassword(password);
}

// The reverse of the above, for anything that receives an id FROM the engine during a duel (Yu-Gi-Oh-Effects looks up a
// card's effect handlers by the id the engine holds): if `id` is currently a vanilla id borrowed for a custom card above
// 16383, returns that custom card's real id; otherwise 0 (it is a real card's own id, or no duel is running).
extern "C" __declspec(dllexport) unsigned short __cdecl Card_GetRealIdForBorrowed(unsigned short id)
{
    auto it = g_BorrowedToHigh.find(id);
    return it != g_BorrowedToHigh.end() ? it->second : 0;
}

// ---------------------------------------------------------------------
// Patches
// ---------------------------------------------------------------------

namespace
{
    // The game keeps one { u32 refcount; i32 cacheIndex } image slot per internal card id,
    // inline in its image table object at obj + 0xC0 + 8 * id, for 10166 ids. Four sites
    // compute that address; each is detoured (Detours) into a small cave that keeps the
    // game's own computation for vanilla ids and uses Card::ImageSlotTable for the rest.
    // The cave is allocated within 2GB of the exe so its rel32 jmp back reaches the game.
    constexpr uint32_t kVanillaImageSlots = 10166;

    uint8_t* AllocNearExe(size_t size)
    {
        const uint8_t* base = reinterpret_cast<const uint8_t*>(GetModuleHandleW(nullptr));
        const auto* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(base);
        const auto* nt = reinterpret_cast<const IMAGE_NT_HEADERS64*>(base + dos->e_lfanew);
        uintptr_t start = (reinterpret_cast<uintptr_t>(base) + nt->OptionalHeader.SizeOfImage + 0xFFFF) & ~static_cast<uintptr_t>(0xFFFF);

        for (uintptr_t addr = start; addr < start + 0x70000000; addr += 0x10000)
        {
            void* p = VirtualAlloc(reinterpret_cast<void*>(addr), size, MEM_RESERVE | MEM_COMMIT, PAGE_EXECUTE_READWRITE);
            if (p)
                return static_cast<uint8_t*>(p);
        }
        return nullptr;
    }

    struct SlotSite
    {
        uintptr_t address;    // first byte replaced
        size_t length;        // bytes the cave takes over (Detours jumps to it from the first instruction; the rest is skipped via resume)
        uintptr_t resume;     // where the cave jumps back to
        uint8_t movabs[2];    // REX + opcode of `movabs reg, imm64`
        uint8_t leaExtra[4];  // lea reg, [reg + rax*8]
        uint8_t leaGame[8];   // the game's own computation, from eax
        int64_t tableOffset;  // added to &ImageSlotTable[0]
    };

    // Encodings checked against the disassembly of each site.
    const SlotSite SlotSites[] = {
        // Image_Request: r15 = a1(rsi) + 8*id + 0xC0
        { 0x140752D00, 22, 0x140752D16, { 0x49, 0xBF }, { 0x4D, 0x8D, 0x3C, 0xC7 },
          { 0x4C, 0x8D, 0xBC, 0xC6, 0xC0, 0x00, 0x00, 0x00 }, 0 },
        // Image_LoaderThreadLoop: r14 = a1(rdi) + 8*id + 0xC0
        { 0x140754192, 22, 0x1407541A8, { 0x49, 0xBE }, { 0x4D, 0x8D, 0x34, 0xC6 },
          { 0x4C, 0x8D, 0xB4, 0xC7, 0xC0, 0x00, 0x00, 0x00 }, 0 },
        // Image_Release: rcx = table(rdi) + 8*id + 0xC0
        { 0x14075242E, 17, 0x14075243F, { 0x48, 0xB9 }, { 0x48, 0x8D, 0x0C, 0xC1 },
          { 0x48, 0x8D, 0x8C, 0xC7, 0xC0, 0x00, 0x00, 0x00 }, 0 },
        // Image_GetLoadedOrQueue: rcx = &slot.cacheIndex = a1(rbx) + 8*id + 0xC4
        { 0x140752A31, 22, 0x140752A4C, { 0x48, 0xB9 }, { 0x48, 0x8D, 0x0C, 0xC1 },
          { 0x48, 0x8D, 0x8C, 0xC3, 0xC4, 0x00, 0x00, 0x00 }, 4 },
    };

    bool PatchSlotSite(uint8_t* cave, const SlotSite& site)
    {
        uint8_t code[64];
        size_t n = 0;

        auto emit = [&](const void* p, size_t len)
        {
            std::memcpy(&code[n], p, len);
            n += len;
        };
        auto jmpTo = [&](uintptr_t target)
        {
            const int64_t rel = static_cast<int64_t>(target) - static_cast<int64_t>(reinterpret_cast<uintptr_t>(cave) + n + 5);
            if (rel > INT32_MAX || rel < INT32_MIN)
                return false;

            const int32_t rel32 = static_cast<int32_t>(rel);
            code[n++] = 0xE9;
            emit(&rel32, sizeof(rel32));
            return true;
        };

        const uint8_t cmpEax[5] = { 0x3D, static_cast<uint8_t>(kVanillaImageSlots), static_cast<uint8_t>(kVanillaImageSlots >> 8), 0x00, 0x00 };
        emit(cmpEax, sizeof(cmpEax));

        const size_t jbPos = n;
        code[n++] = 0x72; // jb <vanilla path>, patched below
        code[n++] = 0x00;

        // Extra card: reg = &ImageSlotTable[0] (+ offset), then reg += 8 * id.
        const uintptr_t table = reinterpret_cast<uintptr_t>(&Card::ImageSlotTable[0]) + site.tableOffset;
        emit(site.movabs, sizeof(site.movabs));
        emit(&table, sizeof(table));
        emit(site.leaExtra, sizeof(site.leaExtra));
        if (!jmpTo(site.resume))
            return false;

        code[jbPos + 1] = static_cast<uint8_t>(n - (jbPos + 2));

        // Vanilla card: the game's own computation.
        const uint8_t movEaxEax[2] = { 0x89, 0xC0 };
        emit(movEaxEax, sizeof(movEaxEax));
        emit(site.leaGame, sizeof(site.leaGame));
        if (!jmpTo(site.resume))
            return false;

        std::memcpy(cave, code, n);

        // Installed with Detours like every other hook: it replaces the site's first instruction (cmp eax, 27B6h) with a jump to the cave.
        // The cave ends by jumping to site.resume, past everything it replaces, so the trampoline Detours builds is never used.
        void* target = reinterpret_cast<void*>(site.address);
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        LONG error = DetourAttach(&target, cave);
        if (error == NO_ERROR)
            error = DetourTransactionCommit();
        else
            DetourTransactionAbort();
        return error == NO_ERROR;
    }

    void ApplyImageSlotPatches()
    {
        // Image_Request only serves ids up to 0x3A78 (mov eax, 3A78h ; cmp bp, ax ; ja).
        PatchImm32(0x140752CEB, kLastExtraCardId);

        // Image_Release drops a reference only for ids inside the game's window (cmp eax, 2B3Ch). A custom card's image was never released, so its
        // reference count only went up: none of the custom images could ever be evicted, the image cache (about 120 entries) filled with them and
        // the game's own cards, which were evicted instead, kept losing their art.
        PatchImm32(0x140752423, kSavedCardTableSize - kVanillaKonamiIdBase - 1);

        uint8_t* cave = AllocNearExe(0x1000);
        if (!cave)
        {
            Logger::WriteLog("Could not allocate a code cave near the exe, image slots not patched", MODULE_NAME, 2);
            return;
        }

        size_t i = 0;
        for (const SlotSite& site : SlotSites)
        {
            const bool ok = PatchSlotSite(cave + i * 0x80, site);
            Logger::WriteLog(std::format("Image slot site 0x{:X}: {}", site.address, ok ? "ok" : "FAILED"), MODULE_NAME, ok ? 0 : 2);
            ++i;
        }
    }

    // The deck edit screens refuse any card whose Konami id is outside 3900..14968 (error
    // sound, nothing else). The gates are widened to the limit of the save's card table
    // rather than removed, so ids below 3900 - an empty deck slot is id 0 - are still
    // rejected exactly as in the unmodified game.
    void ApplyInteractionPatches()
    {
        constexpr uint32_t kGateRange = kSavedCardTableSize - kVanillaKonamiIdBase - 1; // cmp reg, 0x2B3C

        PatchImm32(0x14083639D, kGateRange); // DeckEdit_TrunkInputHandler: select / move to deck
        PatchImm32(0x140836488, kGateRange); // DeckEdit_TrunkInputHandler: second gate
        PatchImm32(0x14083438F, kGateRange); // DeckEdit_CardActionHandler
        PatchImm32(0x14083552D, kGateRange); // DeckEdit_DeckInputHandler_A
        PatchImm32(0x1408358C5, kGateRange); // DeckEdit_DeckInputHandler_B

        // The duel's card info panel (sub_1408866B0) loads r15d = 0x2B3C once and gates both the panel and its widgets on
        // (id - 3900) <= r15w, so a custom id showed no info in a duel. Same gate in the other card-select screens.
        PatchImm32(0x140886780, kGateRange); // duel card info panel: mov r15d, 2B3Ch
        PatchImm32(0x140864CF3, kGateRange); // sub_140864AB0 (card details / move to deck)
        PatchImm32(0x140864E4C, kGateRange);
        PatchImm32(0x140827290, kGateRange); // sub_140827110
        PatchImm32(0x140827326, kGateRange);
        PatchImm32(0x140824807, kGateRange); // sub_1408245B0
        PatchImm32(0x140824934, kGateRange);

        // The duel's own command dialogs: DUEL_DIALOG_SELECT_COMMAND only adds the "Show Info" command (flag 0x4000) when
        // (id - 3900) <= 0x2B3C, DUEL_DIALOG_SYSTEM only lists it under the same gate.
        PatchImm32(0x140778181, kGateRange); // sub_140777F70: hand / field card command menu
        PatchImm32(0x14078983C, kGateRange); // sub_140789760: DUEL_DIALOG_SYSTEM
        // The duel's card lists (graveyard, banished, selection lists): only cards inside the vanilla id range are listed / counted.
        PatchImm32(0x14077D853, kGateRange); // sub_14077D800
        PatchImm32(0x14077E485, kGateRange); // sub_14077E3E0
        PatchImm32(0x14077D7A0, kLastExtraCardId); // sub_14077D760
        PatchImm32(0x14077D893, kLastExtraCardId); // sub_14077D800
        PatchImm32(0x14077DBC5, kLastExtraCardId); // sub_14077D910
        PatchImm32(0x14077DC24, kLastExtraCardId);
        PatchImm32(0x14077E4C3, kLastExtraCardId); // sub_14077E3E0
        PatchImm32(0x14077E6A7, kLastExtraCardId); // sub_14077E600
        PatchImm32(0x14077E710, kLastExtraCardId);
        PatchImm32(0x140774A2A, kLastExtraCardId); // sub_1407748F0: duel cursor placement on a card id
        PatchImm32(0x1407B77AA, kLastExtraCardId); // sub_1407B7710

        // mov reg, 3A78h (highest id) -> last extra card id
        PatchImm32(0x140833BFB, kLastExtraCardId); // DeckEdit_OpenCardDetails
        PatchImm32(0x1407CA599, kLastExtraCardId); // LoadingScreenCard_SetKonamiId
        PatchImm32(0x140889627, kLastExtraCardId); // sub_140889570
        PatchImm32(0x14088978F, kLastExtraCardId); // sub_140889570

        // OpenPackAndGrantCards only stores a pulled card when its internal id is below 10166 (cmp eax, 27B6h).
        PatchImm32(0x14085C2FD, 0xFFFF);
    }
}

// ---------------------------------------------------------------------
// Card data
// ---------------------------------------------------------------------

Card::IN_MEMORY_CARD_PROP* __fastcall Hook_Get_CardPropsFromInternalId(int16_t internalId)
{
    if (internalId < 0 || static_cast<size_t>(internalId) >= Card::CardProps.size())
        return &Card::CardProps[0];

    return &Card::CardProps[internalId];
}

Card::IN_MEMORY_CARD_PROP* __fastcall Hook_Get_CardPropsFromKonamiId(int16_t konamiId)
{
    // Same redirect as Hook_Get_IllustrationData, and for the same reason: a borrowed id (see Card.h) is a
    // real vanilla id as far as ExtraLoadIDs is concerned, so without this a caller asking for the borrowed
    // card's stats (e.g. the ban-status badge, CardInfoRecord_Fill's various small-table reads) would get
    // the real vanilla card's own props instead of the custom card's.
    auto borrowed = g_BorrowedToHigh.find(static_cast<uint16_t>(konamiId));
    const uint16_t realId = borrowed != g_BorrowedToHigh.end() ? borrowed->second : static_cast<uint16_t>(konamiId);

    auto it = Card::ExtraLoadIDs.find(realId);
    if (it != Card::ExtraLoadIDs.end() && static_cast<size_t>(it->second) < Card::CardProps.size())
        return &Card::CardProps[it->second];

    return reinterpret_cast<Card::IN_MEMORY_CARD_PROP*(__fastcall*)(int16_t)>(orig_Get_CardPropsFromKonamiId)(konamiId);
}

int64_t __fastcall Hook_Get_InternalIdFromKonamiId(int16_t konamiId)
{
    if (konamiId == -1)
        return 0;

    auto it = Card::ExtraLoadIDs.find(static_cast<uint16_t>(konamiId));
    if (it != Card::ExtraLoadIDs.end())
        return it->second;

    int64_t index = static_cast<int64_t>(konamiId) - kVanillaKonamiIdBase;
    if (index < 0 || static_cast<size_t>(index) >= kWidenedIdLimit)
        return 0;

    return Limits::InternalIDTable[index];
}

bool __fastcall Hook_Get_IllustrationData(int64_t a1, uint16_t konamiId, void** buffer, size_t* size)
{
    using Get_IllustrationData_t = bool(__fastcall*)(int64_t, uint16_t, void**, size_t*);

    // If `konamiId` is currently a borrowed id for a duel (see the duel-session remap in Card.h), the caller
    // wants the REAL custom card's art, not whatever the real vanilla card at that borrowed slot normally
    // has - ExtraLoadIDs only knows custom ids, so without this it always falls through to the vanilla loader
    // for a borrowed id (this was the bug: a borrowed card showed the real vanilla card's own art in a duel).
    auto borrowed = g_BorrowedToHigh.find(konamiId);
    const uint16_t realId = borrowed != g_BorrowedToHigh.end() ? borrowed->second : konamiId;

    auto it = Card::ExtraLoadIDs.find(realId);
    Card::ExtraCard* card = it != Card::ExtraLoadIDs.end() ? Card::FindExtraCard(realId) : nullptr;
    if (!card)
    {
        if (realId >= kFirstExtraCardId)
            Logger::WriteLog(std::format("Illustration asked for id {}, which is not a loaded custom card", realId), MODULE_NAME, 1);
        return reinterpret_cast<Get_IllustrationData_t>(orig_Get_IllustrationData)(a1, konamiId, buffer, size);
    }
    Logger::WriteLog(std::format("Illustration asked for custom card {} (kind {}){}", realId, card->Props.KindValue,
        realId != konamiId ? std::format(", borrowed as {} for a duel", konamiId) : std::string()), MODULE_NAME, 69);

    if (!card->ImageTried)
    {
        card->ImageTried = true;

        // The path is UTF-8 (names such as "Ace★Spades Speculation" or "Miss Mädchen"); a narrow path is read in the ANSI code page and does not open.
        std::ifstream file(std::filesystem::path(Utf8ToWide(card->ImagePath)), std::ios::binary);
        if (!card->ImagePath.empty() && file)
        {
            card->ImageBytes.assign(std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>());
            Logger::WriteLog(std::format("Loaded {} ({} bytes)", card->ImagePath, card->ImageBytes.size()), MODULE_NAME, 0);
        }
        else
        {
            Logger::WriteLog(std::format("Could not open image \"{}\" for card {}", card->ImagePath, konamiId), MODULE_NAME, 2);
        }
    }

    if (!card->ImageBytes.empty())
    {
        void* copy = GameMalloc(card->ImageBytes.size());
        if (copy)
        {
            std::memcpy(copy, card->ImageBytes.data(), card->ImageBytes.size());
            *buffer = copy;
            *size = card->ImageBytes.size();
            return true;
        }
    }

    return reinterpret_cast<Get_IllustrationData_t>(orig_Get_IllustrationData)(a1, kPlaceholderImageId, buffer, size);
}

// ---------------------------------------------------------------------
// Unlock counts
//
// Get_LiveUnlockCounts(profile) returns the live count table: one byte per internal card
// id, only 10168 bytes long. It is rebuilt from the starter decks and the save's card table
// (one byte per Konami id, bits 0-2 = copies) whenever the profile changes, and several
// callers WRITE through it. So the real pointer goes to every caller except those below,
// which only read it and get a copy that also covers the extra cards. The extra cards'
// counts come from the save's card table, so they persist with the profile.
// ---------------------------------------------------------------------

static bool WantsExtendedUnlockCounts(const void* returnAddress)
{
    switch (reinterpret_cast<uintptr_t>(returnAddress))
    {
    case 0x1408BFF37: // TrunkView_BuildCardList
    case 0x1407F9C5A: // Deck_ValidateAndCountByInternalId
    case 0x1408A369B: // DeckSelect_UpdateOwnedCardCounts
    case 0x1407CAAE0: // Get_OwnedCopiesByKonamiId
        return true;
    default:
        return false;
    }
}

unsigned char* __fastcall Hook_Get_LiveUnlockCounts(unsigned int profile)
{
    unsigned char* original = reinterpret_cast<unsigned char*(__fastcall*)(unsigned int)>(orig_Get_LiveUnlockCounts)(profile);

    if (!original || !WantsExtendedUnlockCounts(_ReturnAddress()))
        return original;

    static std::mutex lock;
    static std::unordered_map<unsigned int, std::vector<unsigned char>> buffers;

    std::lock_guard<std::mutex> guard(lock);
    std::vector<unsigned char>& buffer = buffers[profile];
    if (buffer.empty())
        buffer.assign(kUnlockBufferSize, 0); // sized once so the pointer handed out stays valid

    std::memcpy(buffer.data(), original, kVanillaCardIdCount);

    unsigned char* saved = Save::GetCardUnlockTable(profile);

    // A custom card the profile owns but cards.json no longer has (it was removed or skipped) has no name, art or details, and shows in
    // the trunk as an empty card. Its copies are dropped from the profile's card table; putting the card back in cards.json does not restore them,
    // but unlocks.json / the card's "copies" grant them again.
    if (saved)
    {
        std::string removed;
        int removedCount = 0;
        for (int id = kFirstExtraCardId; id < static_cast<int>(kSavedCardTableSize); ++id)
        {
            if (saved[id] != 0 && !Card::ExtraLoadIDs.contains(static_cast<uint16_t>(id)))
            {
                saved[id] = 0;
                if (++removedCount <= 10)
                    removed += (removed.empty() ? "" : ", ") + std::to_string(id);
            }
        }
        // A copy of the Steam save can carry marks for a whole run of unused ids: say how many, and only the first few.
        if (removedCount > 0)
            Logger::WriteLog(std::format("Dropped {} custom card(s) the profile owned that cards.json does not have: {}{}", removedCount, removed, removedCount > 10 ? ", ..." : ""), MODULE_NAME, 1);
    }

    // The player owns at least `copies` of a card. The count is kept in the save's card table, under the
    // id the game reads it from, so it survives the game rebuilding its counts and being saved.
    auto grant = [&](int id, int internalId, int copies)
    {
        if (internalId <= 0 || static_cast<size_t>(internalId) >= kUnlockBufferSize)
            return;

        int savedId = id;
        if (id <= static_cast<int>(kVanillaKonamiIdBase + kVanillaKonamiIdCount - 1) && Limits::KonamiIDTable[internalId] != 0)
            savedId = Limits::KonamiIDTable[internalId]; // the id the game reads this card's count from

        // Bit 3 of the byte is "already seen": the game sets it for the cards it grants at the start, and a card
        // without it is shown as new. Starting cards are not new, so it is set together with the copies.
        if (saved && savedId < static_cast<int>(kSavedCardTableSize) && (saved[savedId] & 7) < copies)
            saved[savedId] = static_cast<unsigned char>((saved[savedId] & ~7) | copies | 8);

        int owned = saved && savedId < static_cast<int>(kSavedCardTableSize) ? (saved[savedId] & 7) : copies;
        buffer[internalId] = static_cast<unsigned char>(std::max<int>(buffer[internalId], owned));
    };

    for (const auto& [id, internalId] : Card::ExtraLoadIDs)
    {
        const Card::ExtraCard* card = Card::FindExtraCard(id);
        grant(id, static_cast<int>(internalId), card ? card->Copies : 0);
    }

    for (const Card::Unlock& unlock : Card::Unlocks)
    {
        int internalId = 0;
        auto extra = Card::ExtraLoadIDs.find(static_cast<uint16_t>(unlock.Id));
        if (extra != Card::ExtraLoadIDs.end())
            internalId = static_cast<int>(extra->second);
        else if (unlock.Id >= static_cast<int>(kVanillaKonamiIdBase) && unlock.Id < static_cast<int>(kVanillaKonamiIdBase + kVanillaKonamiIdCount))
            internalId = Limits::InternalIDTable[unlock.Id - kVanillaKonamiIdBase];

        grant(unlock.Id, internalId, unlock.Copies);
    }

    return buffer.data();
}

// ---------------------------------------------------------------------
// Image slots
// ---------------------------------------------------------------------

int64_t __fastcall Hook_Get_ImageSlot(void* a1, int16_t konamiId)
{
    // Vanilla ids keep the game's own slots; only extra cards use ours.
    int64_t id = Hook_Get_InternalIdFromKonamiId(konamiId);
    if (id < static_cast<int64_t>(kVanillaImageSlots) || static_cast<size_t>(id) >= kWidenedIdLimit)
        return reinterpret_cast<int64_t(__fastcall*)(void*, int16_t)>(orig_Get_ImageSlot)(a1, konamiId);

    return reinterpret_cast<int64_t>(&Card::ImageSlotTable[id]);
}

// ---------------------------------------------------------------------
// Deck copy counts
//
// The deck object keeps two per card copy tables (deck+52 by Konami id, deck+11121 by "same
// card" id), inline and 0x2B3D bytes long, indexed by id - 3900. Its getters return 0 and its
// rebuild routines skip ids outside 3900..14968, so an extra card would never count towards
// the deck: the copy limit never triggers and the trunk copy never runs out. The tables
// can't grow, so ids outside the window are counted here, per deck object.
// ---------------------------------------------------------------------

namespace
{
    struct DeckCounts
    {
        std::unordered_map<uint16_t, uint8_t> ById;
        std::unordered_map<uint16_t, uint8_t> BySame;
    };

    std::mutex DeckLock;
    std::unordered_map<int64_t, DeckCounts> DecksByObject;

    inline bool InVanillaWindow(uint64_t id)
    {
        return id - kVanillaKonamiIdBase < kVanillaKonamiIdCount;
    }

    uint16_t NormalizedId(uint16_t id)
    {
        return static_cast<uint16_t>(reinterpret_cast<int64_t(__fastcall*)(int64_t)>(Get_NormalizedSameCardId)(id));
    }

    // Repeats the counting loop of the two rebuild routines for ids outside the window.
    void RecountExtraIds(int64_t deckObject, int64_t deckData, uintptr_t offsetsAddress)
    {
        std::lock_guard<std::mutex> guard(DeckLock);
        DeckCounts& counts = DecksByObject[deckObject];
        counts.ById.clear();
        counts.BySame.clear();
        if (!deckData)
            return;

        const uint16_t* sectionCounts = reinterpret_cast<const uint16_t*>(deckData + 66);
        const int* offsets = reinterpret_cast<const int*>(offsetsAddress);
        for (int section = 0; section < 3; ++section)
        {
            const uint16_t* ids = reinterpret_cast<const uint16_t*>(deckData + 72 + 2LL * offsets[section]);
            for (uint16_t i = 0; i < sectionCounts[section]; ++i)
            {
                const uint16_t id = ids[i];
                if (!id)
                    continue;

                if (!InVanillaWindow(id))
                    ++counts.ById[id];

                const uint16_t norm = NormalizedId(id);
                if (norm && !InVanillaWindow(norm))
                    ++counts.BySame[norm];
            }
        }
    }

    using DeckGet_t = int64_t(__fastcall*)(int64_t, uint16_t);
    using DeckRebuild_t = int64_t(__fastcall*)(int64_t, int64_t);
}

int64_t __fastcall Hook_Deck_GetCopiesByKonamiId(int64_t deck, uint16_t id)
{
    if (InVanillaWindow(id))
        return reinterpret_cast<DeckGet_t>(orig_Deck_GetCopiesByKonamiId)(deck, id);

    std::lock_guard<std::mutex> guard(DeckLock);
    auto d = DecksByObject.find(deck);
    if (d == DecksByObject.end())
        return 0;

    auto c = d->second.ById.find(id);
    return c == d->second.ById.end() ? 0 : c->second;
}

int64_t __fastcall Hook_Deck_GetCopiesBySameCardId(int64_t deck, uint16_t id)
{
    const uint16_t norm = NormalizedId(id);
    if (InVanillaWindow(norm))
        return reinterpret_cast<DeckGet_t>(orig_Deck_GetCopiesBySameCardId)(deck, id);

    std::lock_guard<std::mutex> guard(DeckLock);
    auto d = DecksByObject.find(deck);
    if (d == DecksByObject.end())
        return 0;

    auto c = d->second.BySame.find(norm);
    return c == d->second.BySame.end() ? 0 : c->second;
}

int64_t __fastcall Hook_Deck_RebuildCardCountTables(int64_t deck, int64_t data)
{
    int64_t result = reinterpret_cast<DeckRebuild_t>(orig_Deck_RebuildCardCountTables)(deck, data);
    RecountExtraIds(deck, data, kDeckSectionOffsets);
    return result;
}

int64_t __fastcall Hook_Deck_RebuildCardCountTables_Alt(int64_t deck, int64_t data)
{
    int64_t result = reinterpret_cast<DeckRebuild_t>(orig_Deck_RebuildCardCountTables_Alt)(deck, data);
    RecountExtraIds(deck, data, kDeckSectionOffsetsAlt);
    return result;
}

// ----------------------------------------------------------------------
// Duel-session id remapping (see Card.h and the "ygo-duel-id-remap-plan" memory).
// ----------------------------------------------------------------------

using LoadDeck_t = int64_t(__fastcall*)(char, int32_t*);
using ClearState_t = void(__fastcall*)();
using FinishAndUpdateSave_t = void(__fastcall*)(int64_t, uint32_t*, int);

// Called once per player, in order (player 0 then player 1), from DuelSetup_InitEngine, before the deck is
// shuffled or drawn from. Rewrites every id above kDuelIdLimit in the deck struct to a borrowed vanilla id
// before handing the deck to the game's own loader, so the duel engine never sees anything it can't hold.
// Records every vanilla card of a deck struct as in use, so no custom card is lent its id (BorrowSourceId lends a clone its source's id).
static void ReserveVanillaIds(const int32_t* deck)
{
    if (!deck)
        return;
    const auto* bytes = reinterpret_cast<const uint8_t*>(deck);
    const uint32_t mainCount = *reinterpret_cast<const uint32_t*>(bytes + 0), extraCount = *reinterpret_cast<const uint32_t*>(bytes + 8);
    const auto* mainIds = reinterpret_cast<const uint16_t*>(bytes + 12);
    const auto* extraIds = reinterpret_cast<const uint16_t*>(bytes + 162);
    auto reserve = [](uint16_t id) {
        if (id && id <= kDuelIdLimit && !Card::FindExtraCard(id))
            g_IdsInUseThisDuel.insert(id);
    };
    for (uint32_t i = 0; mainCount <= 75 && i < mainCount; ++i)
        reserve(mainIds[i]);
    for (uint32_t i = 0; extraCount <= 15 && i < extraCount; ++i)
        reserve(extraIds[i]);
}

int64_t __fastcall Hook_Duel_LoadDeck(char player, int32_t* deck)
{
    // DuelSetup_InitEngine loads player 0's deck (Duel_DuelEngine + 0x2C) then player 1's (+ 0xEC, 0x14333036C); both are filled before the first call.
    // Reserve the vanilla cards of BOTH decks first, so a clone in player 0's deck is never lent the id of a card player 1 actually plays.
    if ((player & 1) == 0)
    {
        constexpr uintptr_t kDuelEngine = 0x143330280;
        ReserveVanillaIds(deck);
        ReserveVanillaIds(reinterpret_cast<const int32_t*>(kDuelEngine + 0xEC));
    }
    else
        ReserveVanillaIds(deck);

    // Unconditional, unlike the rest of this function's logging: this is the only proof that this hook (and
    // therefore the duel-session id remap) ran at all for this call. A card above 16383 that never gets a
    // matching "resolved N id(s) above 16383" here did not go through this hook - check the DLL/hook chain,
    // not the remap logic itself, if a high-id card behaves wrong but this line never shows the right count.
    uint32_t highIdCount = 0;
    if (deck)
    {
        constexpr size_t kMainCountOffset = 0, kExtraCountOffset = 8, kMainIdsOffset = 12, kExtraIdsOffset = 162;
        constexpr uint32_t kMainCapacity = 75, kExtraCapacity = 15;

        auto* bytes = reinterpret_cast<uint8_t*>(deck);
        const uint32_t mainCount = *reinterpret_cast<uint32_t*>(bytes + kMainCountOffset);
        const uint32_t extraCount = *reinterpret_cast<uint32_t*>(bytes + kExtraCountOffset);
        auto* mainIds = reinterpret_cast<uint16_t*>(bytes + kMainIdsOffset);
        auto* extraIds = reinterpret_cast<uint16_t*>(bytes + kExtraIdsOffset);

        if (mainCount <= kMainCapacity)
        {
            for (uint32_t i = 0; i < mainCount; ++i)
            {
                if (mainIds[i] > kDuelIdLimit)
                    ++highIdCount;
                if (mainIds[i])
                    mainIds[i] = Card::ResolveDuelSessionId(mainIds[i]);
            }
        }
        if (extraCount <= kExtraCapacity)
        {
            for (uint32_t i = 0; i < extraCount; ++i)
            {
                if (extraIds[i] > kDuelIdLimit)
                    ++highIdCount;
                if (extraIds[i])
                    extraIds[i] = Card::ResolveDuelSessionId(extraIds[i]);
            }
        }
        Logger::WriteLog(std::format("Duel_LoadDeck(player {}): resolved main {} / extra {} id(s), {} above 16383",
            static_cast<int>(player & 1), mainCount, extraCount, highIdCount), MODULE_NAME, 1);
    }

    return reinterpret_cast<LoadDeck_t>(orig_Duel_LoadDeck)(player, deck);
}

// The first thing DuelSetup_LoadBothDecks calls, before either player's deck loads: a clean "a new duel is
// starting" signal. Restores anything left borrowed from a duel whose end this plugin did not see (defensive;
// FinishAndUpdateSave below is the normal path) so a stale overwrite never lingers into the next duel.
void __fastcall Hook_DuelSetup_ClearState()
{
    reinterpret_cast<ClearState_t>(orig_DuelSetup_ClearState)();
    RestoreAndClearDuelSessionRemap();
}

// Runs once when a duel actually concludes, before the trunk/unlock counts get rebuilt (RebuildLiveUnlockCounts,
// RecountOwnedCards are both called later in the original function) - restore real vanilla data first so those
// see the truth, not a borrowed card's overwrite.
void __fastcall Hook_FinishAndUpdateSave(int64_t a1, uint32_t* a2, int a3)
{
    RestoreAndClearDuelSessionRemap();
    reinterpret_cast<FinishAndUpdateSave_t>(orig_FinishAndUpdateSave)(a1, a2, a3);
}

// ----------------------------------------------------------------------
// named archetypes
//
// Is_CardInNamedArchetype (IDA 0x14076CFF0) answers "is this card in archetype N" for every effect condition
// and special-summon search in the duel engine, by binary-searching bin/CARD_Named.bin. Custom cards are not in
// that file, so this adds them ("archetypes" in cards.json), and lets codes >= 419 (which the original rejects)
// name archetypes that only custom cards have. Signature (id, code) -> bool as int64.
// ----------------------------------------------------------------------

static uintptr_t orig_Is_CardInNamedArchetype = 0x14076CFF0;
constexpr int kVanillaArchetypeCount = 419;

int64_t __fastcall Hook_Is_CardInNamedArchetype(uint16_t id, int code)
{
    // In a duel a custom card above 16383 is a borrowed vanilla id: the original would answer for the vanilla
    // card that owns that id, which is wrong for the whole duel, so a borrowed id is only ever the custom card.
    auto borrowed = g_BorrowedToHigh.find(id);
    const bool isBorrowed = borrowed != g_BorrowedToHigh.end();
    const uint16_t realId = isBorrowed ? borrowed->second : id;

    if (!Card::g_ArchetypeMembers.empty())
    {
        auto members = Card::g_ArchetypeMembers.find(code);
        if (members != Card::g_ArchetypeMembers.end() && members->second.contains(realId))
            return 1;
    }

    if (isBorrowed || code >= kVanillaArchetypeCount || realId >= kFirstExtraCardId)
        return 0;
    return reinterpret_cast<int64_t(__fastcall*)(uint16_t, int)>(orig_Is_CardInNamedArchetype)(id, code);
}

// ----------------------------------------------------------------------
// starting cards
//
// RebuildLiveUnlockCounts grants the cards of the starter decks through GrantDeckTemplateCards(profile, deck,
// isStarter) (isStarter is 1 for those five decks, 0 for the story / pack decks). With unlocks.json present
// the starter grants are skipped, so the player only owns what unlocks.json (and the save) say.
// ----------------------------------------------------------------------

static uintptr_t orig_GrantDeckTemplateCards = 0x14076EB30;

int* __fastcall Hook_GrantDeckTemplateCards(unsigned int profile, void* deckTemplate, char isStarter)
{
    if (isStarter && Card::ReplaceDefaultUnlocks)
        return nullptr;

    return reinterpret_cast<int*(__fastcall*)(unsigned int, void*, char)>(orig_GrantDeckTemplateCards)(profile, deckTemplate, isStarter);
}

// ---------------------------------------------------------------------
// Install
// ---------------------------------------------------------------------

void Card::Install()
{
    // Runs after every card table setup. The game rebuilds FULL_CARD_PROPS on a language change,
    // which wipes the extra cards' entries, so registration happens every time; loading the JSON,
    // patching and hooking happen once.
    static bool firstRun = true;

    if (firstRun)
    {
        size_t count = LoadCardsFromJson("cards.json");
        Logger::WriteLog(std::format("Loaded {} card(s) from cards.json", count), MODULE_NAME, 0);

        size_t unlocks = LoadUnlocksFromJson("unlocks.json");
        if (unlocks)
            Logger::WriteLog(std::format("Loaded {} unlock(s) from unlocks.json", unlocks), MODULE_NAME, 0);
        if (unlocks && ReplaceDefaultUnlocks)
            Logger::WriteLog("unlocks.json replaces the game's starting cards", MODULE_NAME, 0);

        // An unloaded slot is { refcount 0, cacheIndex -1 }.
        for (auto& slot : ImageSlotTable)
            slot = 0xFFFFFFFF00000000;
    }

    for (const CardOverride& o : Overrides)
        ApplyOverride(o);
    if (firstRun && !Overrides.empty())
        Logger::WriteLog(std::format("Changed {} card(s) the game already has", Overrides.size()), MODULE_NAME, 0);

    ExtraLoadIDs.clear();
    for (const ExtraCard& card : ExtraCards)
    {
        int64_t internalId = static_cast<int64_t>(CardProps.size());
        CardProps.push_back(card.Props);
        ExtraLoadIDs[card.ID] = internalId;

        if (firstRun)
            Logger::WriteLog(std::format("Card {} is internal id {}", card.ID, internalId), MODULE_NAME, 69);

        WriteGameTableEntry(card);
    }

    if (!firstRun)
        return;
    firstRun = false;

    ApplyInteractionPatches();
    ApplyImageSlotPatches();

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());

    DetourAttach(&(PVOID&)orig_Get_CardPropsFromInternalId, Hook_Get_CardPropsFromInternalId);
    DetourAttach(&(PVOID&)orig_Get_CardPropsFromKonamiId, Hook_Get_CardPropsFromKonamiId);
    DetourAttach(&(PVOID&)orig_Get_InternalIdFromKonamiId, Hook_Get_InternalIdFromKonamiId);
    DetourAttach(&(PVOID&)orig_Get_IllustrationData, Hook_Get_IllustrationData);
    DetourAttach(&(PVOID&)orig_Get_LiveUnlockCounts, Hook_Get_LiveUnlockCounts);
    DetourAttach(&(PVOID&)orig_Get_ImageSlot, Hook_Get_ImageSlot);
    DetourAttach(&(PVOID&)orig_Deck_GetCopiesByKonamiId, Hook_Deck_GetCopiesByKonamiId);
    DetourAttach(&(PVOID&)orig_Deck_GetCopiesBySameCardId, Hook_Deck_GetCopiesBySameCardId);
    DetourAttach(&(PVOID&)orig_Deck_RebuildCardCountTables, Hook_Deck_RebuildCardCountTables);
    DetourAttach(&(PVOID&)orig_Deck_RebuildCardCountTables_Alt, Hook_Deck_RebuildCardCountTables_Alt);
    DetourAttach(&(PVOID&)orig_GrantDeckTemplateCards, Hook_GrantDeckTemplateCards);
    DetourAttach(&(PVOID&)orig_Duel_LoadDeck, Hook_Duel_LoadDeck);
    DetourAttach(&(PVOID&)orig_DuelSetup_ClearState, Hook_DuelSetup_ClearState);
    DetourAttach(&(PVOID&)orig_FinishAndUpdateSave, Hook_FinishAndUpdateSave);
    DetourAttach(&(PVOID&)orig_Is_CardInNamedArchetype, Hook_Is_CardInNamedArchetype);

    LONG err = DetourTransactionCommit();
    Logger::WriteLog(std::format("Card hooks attached: {}", err), MODULE_NAME, err == 0 ? 0 : 2);
}
