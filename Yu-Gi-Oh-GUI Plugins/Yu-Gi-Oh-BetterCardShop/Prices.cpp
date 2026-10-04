#include "Prices.h"

#include <Windows.h>
#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstring>
#include <fstream>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include <json.hpp>

#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Log.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/Yu-Gi-Oh-Mods.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-CARDS.h"
#include "../../Dependencies/Yu-Gi-Oh-Ex/YuGiOh/YuGiOh-RIX.h"
#include "Packs.h"

namespace
{
    constexpr const char* MODULE_NAME = "Yu-Gi-Oh-BetterCardShop";
    constexpr const char* kSection = "Yu-Gi-Oh-BetterCardShop";
    constexpr const char* kConfig = ".\\Config.ini";

    // CARD_PROPS::Kind (File Type Libraries/CARD_Props, CARDS_INFO.CARD_Kind)
    constexpr int kToken = 10, kSpell = 13, kTrap = 14, kLink = 42, kLinkEffect = 43;
    constexpr int kMaxStat = 5000;              // the highest real ATK / DEF; above it (or below 0) is "?"

    constexpr double kFloor = 100;              // no card is cheaper by a formula (prices.json can say anything)
    constexpr double kCeiling = 999999;         // Forbidden Memories' "not for sale" price
    constexpr double kClassicCeiling = 10000;   // the classic curve runs away at the top (5000 / 5000 would be over a million)
    constexpr double kLinkDef = 0.8;            // a Link monster has no DEF: the game's other monsters average DEF = 0.797 x ATK

    // attack: the formula asked for, ATK x 1.8 / 5.
    constexpr double kAttackRate = 1.8 / 5.0;
    // classic: Forbidden Memories' star chip prices, fitted on its 533 buyable monsters: ln(cost) = 1.754 + 0.001179 (ATK + DEF), R^2 0.968
    // (a straight line on ATK + DEF only reaches 0.59). In DP it is twice the star chips: a typical common (ATK + DEF 2400) about 200 DP, a
    // typical rare (3800) about 1,060 DP, Blue-Eyes (5500) about 7,540 DP.
    constexpr double kClassicBase = 2.0 * 5.78;
    constexpr double kClassicGrowth = 0.001179;

    constexpr const char* kDefaultMonsterFormula = "ATK * 1.8 / 5";
    constexpr const char* kDefaultSpellTrapFormula = "500 + 500 * RARE";

    enum class MonsterRule { Attack, Classic, Custom, Flat };
    enum class SpellTrapRule { Rarity, Custom, Flat };

    std::string Setting(const char* key, const char* fallback)
    {
        char value[256] = {};
        GetPrivateProfileStringA(kSection, key, fallback, value, sizeof(value), kConfig);
        return value;
    }

    uint64_t Number(const char* key, int fallback)
    {
        return static_cast<uint64_t>((std::max)(0, static_cast<int>(GetPrivateProfileIntA(kSection, key, fallback, kConfig))));
    }

    bool Is(const std::string& value, const char* name) { return _stricmp(value.c_str(), name) == 0; }

    MonsterRule Monsters()
    {
        const std::string rule = Setting("BetterShop-MonsterPrice", "attack");
        if (Is(rule, "classic"))
            return MonsterRule::Classic;
        if (Is(rule, "custom"))
            return MonsterRule::Custom;
        if (Is(rule, "flat"))
            return MonsterRule::Flat;
        return MonsterRule::Attack;
    }

    SpellTrapRule SpellsAndTraps()
    {
        const std::string rule = Setting("BetterShop-SpellTrapPrice", "rarity");
        if (Is(rule, "custom"))
            return SpellTrapRule::Custom;
        if (Is(rule, "flat"))
            return SpellTrapRule::Flat;
        return SpellTrapRule::Rarity;
    }

    uint64_t Flat() { return Number("BetterShop-Cost", 500); }

    uint64_t Round(double price, double ceiling = kCeiling)
    {
        if (!std::isfinite(price))
            price = ceiling;
        return static_cast<uint64_t>(std::clamp(std::round(price / 10.0) * 10.0, kFloor, ceiling));
    }

    bool IsSpellOrTrap(int kind) { return kind == kSpell || kind == kTrap; }
    bool KnownStat(int value) { return value >= 0 && value <= kMaxStat; }

    // ---- custom formulas: + - * / ^, brackets, numbers, the card's values and a few functions

    struct Values
    {
        double Atk = 0, Def = 0, Level = 0, Rare = 0, Link = 0;
    };

    // "ATK * 1.8 / 5", "max(300, (ATK + DEF) / 10)", "500 + 500 * RARE". Names: ATK, DEF (a Link monster's is 0.8 x ATK), LEVEL, RARE (1 or
    // 0), LINK (1 or 0); functions: min, max, exp, log, sqrt, round, floor, ceil. Case doesn't matter.
    class Formula
    {
    public:
        Formula(const std::string& text, const Values& values) : m_Text(text), m_Values(values) {}

        bool Evaluate(double& result)
        {
            result = Sum();
            Skip();
            return m_Ok && m_At == m_Text.size();
        }

    private:
        const std::string& m_Text;
        const Values& m_Values;
        size_t m_At = 0;
        bool m_Ok = true;

        void Skip()
        {
            while (m_At < m_Text.size() && std::isspace(static_cast<unsigned char>(m_Text[m_At])))
                ++m_At;
        }

        bool Take(char c)
        {
            Skip();
            if (m_At < m_Text.size() && m_Text[m_At] == c)
            {
                ++m_At;
                return true;
            }
            return false;
        }

        double Sum()
        {
            double value = Product();
            for (;;)
            {
                if (Take('+'))
                    value += Product();
                else if (Take('-'))
                    value -= Product();
                else
                    return value;
            }
        }

        double Product()
        {
            double value = Power();
            for (;;)
            {
                if (Take('*'))
                    value *= Power();
                else if (Take('/'))
                {
                    const double by = Power();
                    value = by == 0 ? 0 : value / by;
                }
                else
                    return value;
            }
        }

        double Power()
        {
            const double base = Unary();
            return Take('^') ? std::pow(base, Power()) : base;
        }

        double Unary()
        {
            if (Take('-'))
                return -Unary();
            if (Take('+'))
                return Unary();
            return Atom();
        }

        double Atom()
        {
            Skip();
            if (Take('('))
            {
                const double value = Sum();
                if (!Take(')'))
                    m_Ok = false;
                return value;
            }

            const size_t start = m_At;
            if (m_At < m_Text.size() && (std::isdigit(static_cast<unsigned char>(m_Text[m_At])) || m_Text[m_At] == '.'))
            {
                while (m_At < m_Text.size() && (std::isdigit(static_cast<unsigned char>(m_Text[m_At])) || m_Text[m_At] == '.'))
                    ++m_At;
                return std::atof(m_Text.substr(start, m_At - start).c_str());
            }

            while (m_At < m_Text.size() && std::isalpha(static_cast<unsigned char>(m_Text[m_At])))
                ++m_At;
            const std::string name = m_Text.substr(start, m_At - start);
            if (name.empty())
            {
                m_Ok = false;
                return 0;
            }
            if (Is(name, "ATK")) return m_Values.Atk;
            if (Is(name, "DEF")) return m_Values.Def;
            if (Is(name, "LEVEL")) return m_Values.Level;
            if (Is(name, "RARE")) return m_Values.Rare;
            if (Is(name, "LINK")) return m_Values.Link;

            if (!Take('('))
            {
                m_Ok = false;
                return 0;
            }
            std::vector<double> args{ Sum() };
            while (Take(','))
                args.push_back(Sum());
            if (!Take(')'))
                m_Ok = false;
            const double a = args[0];
            if (Is(name, "min")) return *std::min_element(args.begin(), args.end());
            if (Is(name, "max")) return *std::max_element(args.begin(), args.end());
            if (Is(name, "exp")) return std::exp(a);
            if (Is(name, "log")) return a > 0 ? std::log(a) : 0;
            if (Is(name, "sqrt")) return a > 0 ? std::sqrt(a) : 0;
            if (Is(name, "round")) return std::round(a);
            if (Is(name, "floor")) return std::floor(a);
            if (Is(name, "ceil")) return std::ceil(a);
            m_Ok = false;
            return 0;
        }
    };

    std::unordered_set<std::string> g_BadFormulas;   // logged once each

    // A formula setting's value for a card, false (logged once) when it can't be read.
    bool Custom(const char* key, const char* fallback, const Values& values, double& result)
    {
        const std::string text = Setting(key, fallback);
        if (Formula(text, values).Evaluate(result))
            return true;
        if (g_BadFormulas.insert(text).second)
            YGO::Log(std::string(key) + " \"" + text + "\" can't be read, so the default is used", MODULE_NAME, 2);
        return false;
    }

    // ---- prices.json: fixed prices for single cards

    struct Override
    {
        int64_t Price = -1, Password = -1;   // -1 = not set
    };
    std::unordered_map<uint16_t, Override> g_Overrides;

    // { "cards": [ { "id": 4007, "price": 5000, "password": 2500 } ] }; both prices optional.
    void LoadOverrides()
    {
        g_Overrides.clear();
        // every mod's prices.json and the game folder's, merged (Yu-Gi-Oh-Mods.h): a later mod's price for a card wins
        std::vector<std::string> problems;
        const nlohmann::json json = YGO::Mods::ReadMerged("prices.json", nullptr, &problems);
        for (const std::string& problem : problems)
            YGO::Log(problem + ", its prices are left out", MODULE_NAME, 2);
        if (!json.is_object())
            return;   // optional
        try
        {
            for (const auto& entry : json.value("cards", nlohmann::json::array()))
            {
                const int id = entry.value("id", 0);
                if (id <= 0 || id > 0xFFFF)
                    continue;
                Override& card = g_Overrides[static_cast<uint16_t>(id)];
                if (entry.contains("price"))
                    card.Price = (std::max)(int64_t{ 0 }, entry["price"].get<int64_t>());
                if (entry.contains("password"))
                    card.Password = (std::max)(int64_t{ 0 }, entry["password"].get<int64_t>());
            }
        }
        catch (const std::exception& e)
        {
            g_Overrides.clear();
            YGO::Log("prices.json couldn't be read: " + std::string(e.what()), MODULE_NAME, 2);
            return;
        }
        YGO::Log("prices.json: " + std::to_string(g_Overrides.size()) + " card(s)", MODULE_NAME, 0);
    }

    bool g_OverridesLoaded = false;

    const Override* FindOverride(uint16_t id)
    {
        if (!g_OverridesLoaded)
        {
            LoadOverrides();
            g_OverridesLoaded = true;
        }
        const auto found = g_Overrides.find(id);
        return found == g_Overrides.end() ? nullptr : &found->second;
    }

    // ---- monsters by their stats

    Values ValuesOf(const YGO::CARDS::CARD_PROPS& card, bool rare)
    {
        Values values;
        const bool link = card.Kind == kLink || card.Kind == kLinkEffect;
        values.Atk = KnownStat(card.ATK) ? card.ATK : 0;
        values.Def = link ? values.Atk * kLinkDef : (KnownStat(card.DEF) ? card.DEF : 0);
        values.Level = card.Level;
        values.Rare = rare ? 1 : 0;
        values.Link = link ? 1 : 0;
        return values;
    }

    // A monster's price from its stats; false when its ATK is "?".
    bool StatPrice(const YGO::CARDS::CARD_PROPS& card, bool rare, MonsterRule rule, uint64_t& price)
    {
        if (!KnownStat(card.ATK))
            return false;
        const Values values = ValuesOf(card, rare);
        double custom = 0;
        if (rule == MonsterRule::Custom && Custom("BetterShop-MonsterFormula", kDefaultMonsterFormula, values, custom))
            price = Round(custom);
        else if (rule == MonsterRule::Classic)
            price = Round(kClassicBase * std::exp(kClassicGrowth * (values.Atk + values.Def)), kClassicCeiling);
        else
            price = Round(values.Atk * kAttackRate);
        return true;
    }

    // ---- rarity: Konami's own rating of each card (which slot of its pack it is in), and what a typical monster of each costs

    bool g_RaresBuilt = false;
    std::unordered_set<uint16_t> g_Rares;
    bool g_MediansBuilt = false;
    MonsterRule g_MediansFor = MonsterRule::Attack;
    uint64_t g_RarePrice = 0, g_CommonPrice = 0;

    bool IsRare(uint16_t id)
    {
        if (!g_RaresBuilt)
        {
            g_Rares = Packs::RareCards();
            g_RaresBuilt = true;
        }
        return g_Rares.count(id) != 0;
    }

    uint64_t Median(std::vector<uint64_t>& prices, uint64_t fallback)
    {
        if (prices.empty())
            return fallback;
        std::nth_element(prices.begin(), prices.begin() + prices.size() / 2, prices.end());
        return prices[prices.size() / 2];
    }

    // Every monster loaded (internal ids, the same walk as the Card Shop's list, so Yu-Gi-Oh-Cards' cards count too).
    void BuildMedians(MonsterRule rule)
    {
        auto propsOf = reinterpret_cast<YGO::CARDS::CARD_PROPS*(__fastcall*)(unsigned int)>(YGO::CARDS::Get_CardPropsFromInternalId);
        std::vector<uint64_t> rare, common;
        const uint32_t limit = YGO::RIX::Trunk::InternalIdLimit();
        for (uint32_t internal = 1; internal < limit; ++internal)
        {
            const YGO::CARDS::CARD_PROPS* card = propsOf(internal);
            if (!card || card->KonamiID == 0 || card->Kind == kToken || IsSpellOrTrap(card->Kind))
                continue;
            const bool isRare = IsRare(static_cast<uint16_t>(card->KonamiID));
            uint64_t price = 0;
            if (StatPrice(*card, isRare, rule, price))
                (isRare ? rare : common).push_back(price);
        }
        g_RarePrice = Median(rare, 1000);
        g_CommonPrice = Median(common, 500);
        g_MediansFor = rule;
        g_MediansBuilt = true;
        YGO::Log("Card prices: " + std::to_string(g_Rares.size()) + " rare cards; a typical rare monster costs " + std::to_string(g_RarePrice) +
                 " DP, a common " + std::to_string(g_CommonPrice) + " DP (" + std::to_string(rare.size() + common.size()) + " monsters)", MODULE_NAME, 0);
    }

    uint64_t RarityPrice(uint16_t id, MonsterRule rule)
    {
        if (rule == MonsterRule::Flat)
            rule = MonsterRule::Attack;   // the medians need a stat formula
        if (!g_MediansBuilt || g_MediansFor != rule)
            BuildMedians(rule);
        return IsRare(id) ? g_RarePrice : g_CommonPrice;
    }
}

namespace Prices
{
    uint64_t PasswordCost(uint16_t id)
    {
        const Override* card = FindOverride(id);
        return card && card->Password >= 0 ? static_cast<uint64_t>(card->Password) : Number("BetterShop-PasswordCost", 1000);
    }

    uint64_t CardPrice(uint16_t id)
    {
        if (const Override* card = FindOverride(id); card && card->Price >= 0)
            return static_cast<uint64_t>(card->Price);

        const YGO::CARDS::CARD_PROPS* card = YGO::CARDS::Get_CardPropsFromKonamiId(static_cast<short>(id));
        if (!card)
            return Flat();

        const MonsterRule monsters = Monsters();
        if (IsSpellOrTrap(card->Kind))
        {
            switch (SpellsAndTraps())
            {
            case SpellTrapRule::Flat:
                return Flat();
            case SpellTrapRule::Custom:
            {
                double custom = 0;
                if (Custom("BetterShop-SpellTrapFormula", kDefaultSpellTrapFormula, ValuesOf(*card, IsRare(id)), custom))
                    return Round(custom);
                break;
            }
            default:
                break;
            }
            return RarityPrice(id, monsters);
        }

        if (monsters == MonsterRule::Flat)
            return Flat();
        uint64_t price = 0;
        return StatPrice(*card, IsRare(id), monsters, price) ? price : RarityPrice(id, monsters);
    }

    void Reset()
    {
        g_RaresBuilt = false;
        g_MediansBuilt = false;
        g_OverridesLoaded = false;
        g_BadFormulas.clear();
    }
}
