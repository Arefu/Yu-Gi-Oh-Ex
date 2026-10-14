// Exercise the production hooks with mocked vanilla lookups, without launching a duel.
#ifdef EFFECT_ROUTING_BASELINE
#include "../../../Toolkit-Setup/Pendulum-Routing-Baseline/EffectClone.cpp"
namespace { Clone* RouteByTrigger(Clone* clone, std::span<const char* const> triggers, int) { return RouteByTrigger(clone, triggers); } }
#else
#include "../../Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-Effects/EffectClone.cpp"
#endif
#include "EffectSourceLending.h"
#include <iostream>
#include <stdexcept>

std::string Config::Get_WorkingDirectory() { return "."; }

namespace
{
    Row drawRow{4844}, summonRow{6784}, graveRow{7892}, genericRow{0};
    int lastSource = 0;
    bool provideGeneric = false;
    void Check(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
    uint64_t __fastcall Resolve(uint16_t* effect, uint64_t, uint64_t, uint64_t)
    {
        lastSource = effect[0];
        Check(g_Active && g_Active->From == lastSource, "Slot runs with the wrong part's parameters");
        return 0;
    }
    void* __fastcall Lookup(uint16_t* effect)
    {
        if (effect[4] == 0) return effect[0] == 4844 ? &drawRow : provideGeneric ? &genericRow : nullptr;
        if (effect[4] == 1 && effect[0] == 6784) return &summonRow;
        if (effect[4] == 2 && effect[0] == 7892) return &graveRow;
        return nullptr;
    }
    unsigned short __cdecl NoBorrow(unsigned short) { return 0; }
    int64_t __fastcall Insert(int kind, uint32_t ref, int16_t instance, uint32_t)
    {
        auto* records = reinterpret_cast<uint16_t*>(kind ? 0x14349BC80 : 0x14349B580);
        auto* count = reinterpret_cast<uint32_t*>(kind ? 0x14349C000 : 0x14349C020);
        for (uint32_t i = 0; i < *count; ++i)
            if (records[i * 28] == static_cast<uint16_t>(ref) && records[i * 28 + 11] == instance) return -1;
        uint16_t* record = records + (*count)++ * 28;
        memset(record, 0, 0x38);
        record[0] = static_cast<uint16_t>(ref);
        record[11] = instance;
        return 1;
    }
    int64_t __fastcall Offer(uint32_t ref, int16_t instance, uint32_t param)
    { return Hook_InsertOffer(0, ref, instance, param); }
    int64_t __fastcall MenuOffer(uint32_t ref, int16_t instance, void*, uint32_t param)
    { return Hook_InsertOffer(1, ref, instance, param); }
    Clone Make(int from, const char* trigger = "", int draw = -1)
    {
        Clone part;
        part.From = from;
        part.Trigger = trigger;
        part.Draw = draw;
        return part;
    }
    void TestOrders(bool explicitTriggers)
    {
        for (bool reversed : {false, true})
        {
            Clone draw = Make(4844, "", 2);
            Clone summon = Make(6784, explicitTriggers ? "normal_or_special_summoned" : "");
            Clone card = reversed ? draw : summon;
            card.Parts.push_back(reversed ? summon : draw);
            Check(RouteByTrigger(&card, kSummonTriggers, 1)->From == 6784, "Summon evaluator selected the first effect instead of the monster source");
            g_Clones.clear();
            g_Clones.emplace(15301, std::move(card));
            for (const auto [table, event, expected] : {std::tuple{0, 0, 4844}, std::tuple{1, 7, 6784}, std::tuple{1, 9, 6784}})
            {
                uint16_t record[0x20]{};
                record[0] = 15301; record[2] = table == 0 ? 7 : 0;
                record[3] = event; record[4] = table;
                const auto saved = std::to_array(record);
                Row* wrapped = static_cast<Row*>(Hook_GetEntry(record));
                Check(wrapped && wrapped->Id == 15301, "Missing wrapped effect row");
                Check(wrapped->Slot[0] != nullptr, "Missing resolve slot");
                reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(wrapped->Slot[0])(record, 0, 0, 0);
                Check(lastSource == expected, "Resolution used the wrong source");
                Check(std::equal(saved.begin(), saved.end(), record), "Routing changed the engine record");
                if (table == 0) Check(Hook_GetDraw(record) == 2, "Draw count routed to the wrong part");
            }
        }
    }
}

int main()
{
    try
    {
        // SlotThunk diagnostics read the engine's step variable even with tracing off.
        void* page = VirtualAlloc(reinterpret_cast<void*>(0x143490000), 0x10000, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE);
        Check(page != nullptr, "Could not reserve mock engine memory");
        orig_GetEntry = &Lookup;
        g_RealId = &NoBorrow;
        drawRow.Slot[0] = summonRow.Slot[0] = graveRow.Slot[0] = reinterpret_cast<void*>(&Resolve);
        TestOrders(false);
        std::cout << "PASS raw Pendulum/monster clones in both source orders, lookup and resolution\n";
        TestOrders(true);
        std::cout << "PASS compiled triggers in both source orders, lookup and resolution\n";
        provideGeneric = true;
        TestOrders(false);
        provideGeneric = false;
        std::cout << "PASS exact source rows win over generic Spell/Trap fallbacks\n";
        Clone ordinary = Make(4844), limited = Make(4844);
        limited.OncePerTurn = true;
        Row* ordinaryWrapped = WrappedRow(&drawRow, 15302, ordinary);
        Row* limitedWrapped = WrappedRow(&drawRow, 15302, limited);
        Check(ordinaryWrapped != limitedWrapped && (ordinaryWrapped->Extra[0] & 15) == 0 && (limitedWrapped->Extra[0] & 15) == 5,
            "Parts borrowing one source row shared overrides");
        std::cout << "PASS parts sharing a source keep separate overrides\n";
        g_Clones.clear();
        Clone choices = Make(4844, "", 2);
        choices.Parts = {Make(4844, "", 3)};
        g_Clones.emplace(15301, std::move(choices));
        Clone& root = g_Clones.at(15301);
        orig_InsertOffer = Insert;
        for (int kind : {0, 1})
        {
            auto* count = reinterpret_cast<uint32_t*>(kind ? 0x14349C000 : 0x14349C020);
            *count = 0;
            for (Clone* part : AllParts(root))
            {
                g_MenuPart = part;
                Check(Hook_InsertOffer(kind, 15301, 12, 0) == 1, "Sibling offer was merged");
                Check(Hook_InsertOffer(kind, 15301, 12, 0) == -1, "Identical offer was duplicated");
            }
            g_MenuPart = nullptr;
            Check(*count == 2, "Menu does not contain both effects");
            const uint32_t capacity = kind ? 16 : 32;
            *count = capacity;
            g_MenuPart = &root;
            Check(Hook_InsertOffer(kind, 15301, 12, 0) == 0 && *count == capacity,
                "Full native offer queue was modified");
            g_MenuPart = nullptr;
            *count = 0;
        }
        std::cout << "PASS separate trigger and activation offers, with duplicate suppression\n";
        for (Clone* part : AllParts(root))
        {
            uint16_t record[0x20]{};
            record[0] = 15301;
            record[20] = ChoiceTag(root, part) | 7;
            const auto saved = std::to_array(record);
            Check(RouteByRow(&root, record) == part, "Selected same-table effect lost its identity");
            Row* row = static_cast<Row*>(Hook_GetEntry(record));
            reinterpret_cast<uint64_t(__fastcall*)(uint16_t*, uint64_t, uint64_t, uint64_t)>(row->Slot[0])(record, 0, 0, 0);
            Check(Hook_GetDraw(record) == part->Draw, "Selected effect used sibling draw parameters");
            Check(std::equal(saved.begin(), saved.end(), record), "Choice tag or native sub-effect number was lost");
        }
        std::cout << "PASS independently selected effects sharing table, event and source row\n";
        orig_OfferByRef = Offer;
        orig_MenuOffer = MenuOffer;
        *reinterpret_cast<uint32_t*>(0x14349C020) = 0;
        *reinterpret_cast<uint32_t*>(0x14349C000) = 0;
        Hook_OfferByRef(15301, 12, 0);
        Hook_MenuOffer(15301, 12, nullptr, 0);
        Check(*reinterpret_cast<uint32_t*>(0x14349C020) == 2 && *reinterpret_cast<uint32_t*>(0x14349C000) == 2,
            "Unpinned offer did not enumerate all compatible parts");
        Check(g_MenuPart == nullptr, "Offer enumeration leaked the selected part");
        Clone composed = Make(6784);
        composed.ActionFrom = 4844;
        summonRow.Slot[3] = reinterpret_cast<void*>(0x140201950);
        Check(WrappedRow(&summonRow, 15303, composed)->Slot[3] == nullptr,
            "Composed action inherited its donor's unrelated choices");
        Clone rawChoices = Make(6784);
        Check(WrappedRow(&summonRow, 15303, rawChoices)->Slot[3] != nullptr,
            "Raw clone lost its native choices");
        summonRow.Slot[3] = nullptr;
        std::cout << "PASS offer enumeration and composed versus raw donor choices\n";
        Clone card = Make(4844);
        card.Parts = {Make(6784), Make(7892)};
        Check(RouteByTrigger(&card, kLeaveTriggers, 2)->From == 7892, "Third effect was not selected for a graveyard event");
        Check(RouteByTrigger(&card, kSummonTriggers, 1)->From == 6784, "Third effect changed summon routing");
        std::cout << "PASS three independent effect tables\n";
        Clone incompatible = Make(6784, "sent_to_grave");
        incompatible.Parts = {Make(4844)};
        Check(RouteByTrigger(&incompatible, kSummonTriggers, 1) == nullptr, "Explicit trigger was ignored");
        Clone single = Make(4844);
        Check(RouteByTrigger(&single, kSummonTriggers, 1) == &single, "Single-source behavior changed");
        std::cout << "PASS explicit trigger restrictions and single-effect compatibility\n";
        using nlohmann::json;
        for (const auto& clone : {json{{"from",6784},{"parts",json::array({json{{"from",4844}}})}},
                                 json{{"from",4844},{"parts",json::array({json{{"from",6784}}})}}})
            Check(YGO::Effects::SingleEffectSource(clone,3900,14969) == 0, "Multi-effect card borrowed its first source's identity");
        Check(YGO::Effects::SingleEffectSource(json{{"from",6784}},3900,14969) == 6784, "Single-source lending changed");
        Check(YGO::Effects::SingleEffectSource(json{{"from",6784},{"parts",json::array()}},3900,14969) == 6784, "Empty parts changed lending");
        Check(YGO::Effects::SingleEffectSource(json{{"from",15301}},3900,14969) == 0, "Invalid source was lent");
        std::cout << "PASS source lending policy in both orders\n";
        VirtualFree(page, 0, MEM_RELEASE);
        return 0;
    }
    catch (const std::exception& e) { std::cerr << "FAIL " << e.what() << '\n'; return 1; }
}
