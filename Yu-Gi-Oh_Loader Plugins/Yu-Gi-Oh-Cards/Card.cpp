#include <Windows.h>
#include <algorithm>
#include <deque>
#include <cstring>
#include <format>
#include <fstream>
#include <intrin.h>
#include <mutex>

#include <json.hpp>

#include "Card.h"
#include "Detours.h"
#include "Logger.h"
#include "Save.h"

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

static constexpr uintptr_t Get_NormalizedSameCardId = 0x14081A710;
static constexpr uintptr_t kDeckSectionOffsets = 0x140A521C8;    // 3 ints, used by the rebuild
static constexpr uintptr_t kDeckSectionOffsetsAlt = 0x140A4DED0; // 3 ints, used by the _Alt rebuild

static void* GameMalloc(size_t size)
{
    using Malloc_t = void* (__cdecl*)(size_t);
    Malloc_t fn = *reinterpret_cast<Malloc_t*>(kGameIatMalloc);
    return fn ? fn(size) : nullptr;
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

    const NamedValue KindNames[] = {
        { "Normal", Card::K_Normal }, { "Effect", Card::K_Effect },
        { "Spell", Card::K_Spell }, { "Trap", Card::K_Trap },
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
        { "CreatorGod", Card::CreatorGod }, { "Spell", Card::Spell }, { "Trap", Card::Trap },
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

        if (it->is_string())
        {
            const std::string s = it->get<std::string>();
            for (const auto& n : names)
            {
                if (_stricmp(n.Name, s.c_str()) == 0)
                {
                    out = n.Value;
                    return true;
                }
            }
        }
        return false;
    }

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
            why = std::format("\"id\" must be between {} and {} (the game owns ids below, its save has no room above)", kFirstExtraCardId, kLastExtraCardId);
            return false;
        }

        c.ID = static_cast<uint16_t>(id);
        c.Name = Utf8ToWide(j.value("name", std::string("Unnamed Card")));
        c.Description = Utf8ToWide(j.value("description", std::string()));
        c.Copies = std::clamp(j.value("copies", 3), 0, 3);

        std::string image = j.value("image", std::string());
        if (!image.empty())
            c.ImagePath = kExtraCardsDirectory + image;

        int kind = K_Normal, attribute = LIGHT, type = Warrior, icon = I_Normal, limitation = Unlimited;
        if (!ParseEnum(j, "kind", KindNames, kind)) { why = "unknown \"kind\""; return false; }

        const bool isSpell = kind == K_Spell;
        const bool isTrap = kind == K_Trap;
        if (isSpell) { attribute = SPELL; type = Card::Spell; }
        if (isTrap) { attribute = TRAP; type = Card::Trap; }

        if (!ParseEnum(j, "attribute", AttributeNames, attribute)) { why = "unknown \"attribute\""; return false; }
        if (!ParseEnum(j, "type", TypeNames, type)) { why = "unknown \"type\""; return false; }
        if (!ParseEnum(j, "icon", IconNames, icon)) { why = "unknown \"icon\""; return false; }
        if (!ParseEnum(j, "limitation", LimitationNames, limitation)) { why = "unknown \"limitation\""; return false; }

        IN_MEMORY_CARD_PROP& p = c.Props;
        p.ID1 = c.ID;
        p.KindValue = kind;
        p.Type = static_cast<Card::Type>(type);
        p.Attribute = static_cast<Card::Attribute>(attribute);
        p.Icon = icon;
        p.Limitation = static_cast<Status>(limitation);
        p.PendulumScale = 0;
        p.ID2 = p.ID3 = static_cast<short>(c.ID);

        if (isSpell || isTrap)
        {
            p.StarTypeValue = ST_None;
            p.LevelOrLinkRatingOrRank = 0;
            p.Attack10 = p.ArrowsOrDefense10 = 0;
        }
        else
        {
            // The game stores ATK/DEF divided by 10.
            p.Attack10 = j.value("atk", 0) / 10;
            p.ArrowsOrDefense10 = j.value("def", 0) / 10;
            p.StarTypeValue = ST_Level;
            p.LevelOrLinkRatingOrRank = j.value("level", 1);
        }
        return true;
    }
}

namespace Card
{
    std::vector<ExtraCard> ExtraCards;
    std::vector<IN_MEMORY_CARD_PROP> CardProps;
    std::unordered_map<uint16_t, int64_t> ExtraLoadIDs;

    static ExtraCard* FindExtraCard(uint16_t id)
    {
        for (auto& c : ExtraCards)
        {
            if (c.ID == id)
                return &c;
        }
        return nullptr;
    }

    size_t LoadCardsFromJson(const std::string& path)
    {
        std::ifstream file(path);
        if (!file)
        {
            Logger::WriteLog("Could not open " + path, MODULE_NAME, 2);
            return 0;
        }

        nlohmann::json root;
        try
        {
            root = nlohmann::json::parse(file, nullptr, true, true);
        }
        catch (const std::exception& e)
        {
            Logger::WriteLog(std::format("Parse error in {}: {}", path, e.what()), MODULE_NAME, 2);
            return 0;
        }

        // Accept either {"cards": [...]} or a bare array.
        const nlohmann::json& list = root.is_array() ? root : root["cards"];
        if (!list.is_array())
        {
            Logger::WriteLog(path + " needs a \"cards\" array", MODULE_NAME, 2);
            return 0;
        }

        ExtraCards.clear();
        ExtraCards.reserve(list.size());
        for (size_t i = 0; i < list.size(); ++i)
        {
            ExtraCard c{};
            std::string why;
            if (!ParseCard(list[i], c, why))
            {
                Logger::WriteLog(std::format("Skipped card #{}: {}", i, why), MODULE_NAME, 2);
                continue;
            }

            if (FindExtraCard(c.ID))
            {
                Logger::WriteLog(std::format("Skipped card #{}: duplicate id {}", i, c.ID), MODULE_NAME, 2);
                continue;
            }

            ExtraCards.push_back(std::move(c));
        }
        return ExtraCards.size();
    }

    std::vector<Unlock> Unlocks;
    bool ReplaceDefaultUnlocks = false;
    std::vector<PackAddition> PackAdditions;

    size_t LoadUnlocksFromJson(const std::string& path)
    {
        std::ifstream file(path);
        if (!file)
            return 0; // unlocks.json is optional

        nlohmann::json root;
        try
        {
            root = nlohmann::json::parse(file, nullptr, true, true);
        }
        catch (const std::exception& e)
        {
            Logger::WriteLog(std::format("Parse error in {}: {}", path, e.what()), MODULE_NAME, 2);
            return 0;
        }

        const nlohmann::json& list = root.is_array() ? root : root["cards"];
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

    size_t LoadPacksFromJson(const std::string& path)
    {
        std::ifstream file(path);
        if (!file)
            return 0; // packs.json is optional

        nlohmann::json root;
        try
        {
            root = nlohmann::json::parse(file, nullptr, true, true);
        }
        catch (const std::exception& e)
        {
            Logger::WriteLog(std::format("Parse error in {}: {}", path, e.what()), MODULE_NAME, 2);
            return 0;
        }

        const nlohmann::json& list = root.is_array() ? root : root["packs"];
        if (!list.is_array())
        {
            Logger::WriteLog(path + " needs a \"packs\" array", MODULE_NAME, 2);
            return 0;
        }

        auto readIds = [](const nlohmann::json& entry, const char* key, std::vector<uint16_t>& out)
        {
            auto it = entry.find(key);
            if (it == entry.end() || !it->is_array())
                return;

            for (const auto& value : *it)
            {
                if (value.is_number_integer() && value.get<int>() >= 1 && value.get<int>() <= kLastExtraCardId)
                    out.push_back(static_cast<uint16_t>(value.get<int>()));
            }
        };

        const bool replaceAll = root.is_object() && root.value("replaceDefaults", false);

        PackAdditions.clear();
        for (size_t i = 0; i < list.size(); ++i)
        {
            const nlohmann::json& entry = list[i];
            if (!entry.contains("pack") || !entry["pack"].is_string())
            {
                Logger::WriteLog(std::format("Skipped pack entry #{}: missing \"pack\" name", i), MODULE_NAME, 2);
                continue;
            }

            PackAddition addition;
            addition.Pack = entry["pack"].get<std::string>();
            readIds(entry, "common", addition.Common);
            readIds(entry, "rare", addition.Rare);
            addition.Replace = entry.value("replace", replaceAll);
            PackAdditions.push_back(std::move(addition));
        }
        return PackAdditions.size();
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
        uint16_t TrunkID; char pad1[2];
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

    void WriteGameTableEntry(const Card::ExtraCard& c)
    {
        auto* gc = reinterpret_cast<GameCard*>(kFullCardPropsAddress + static_cast<uintptr_t>(c.ID) * kFullCardPropsStride);
        const Card::IN_MEMORY_CARD_PROP& props = c.Props;
        const int kind = props.KindValue;

        int16_t frame = *reinterpret_cast<int16_t*>(kFrameTableAddress + 0xC * static_cast<uintptr_t>(kind));
        uint16_t subKind = *reinterpret_cast<uint16_t*>(kSubKindTableAddress + 0xC * static_cast<uintptr_t>(kind));

        gc->Name = const_cast<wchar_t*>(c.Name.c_str());
        gc->Description = const_cast<wchar_t*>(c.Description.c_str());

        gc->IsMonster = subKind != Card::SK_None;
        gc->IsSpell = kind == Card::K_Spell;
        gc->IsTrap = kind == Card::K_Trap;
        gc->IsFieldSpell = props.Icon == Card::I_Field;
        gc->IsNormalMonster = subKind == Card::SK_Normal;
        gc->IsEffectMonster = subKind == Card::SK_Effect;
        gc->IsFusion = gc->IsSynchro = gc->IsXyz = gc->IsExtraMonster = false;
        gc->IsRitual = gc->IsToken = gc->IsToon = gc->IsSpirit = gc->IsGemini = false;
        gc->IsPendulum = gc->IsLink = false;

        gc->Attack1 = gc->Attack2 = props.Attack10 * 10;
        gc->CardAttribute = props.Attribute;
        gc->Defense1 = gc->Defense2 = props.ArrowsOrDefense10 * 10;
        gc->Icon = props.Icon;
        gc->Kind = kind;
        gc->Level = (props.StarTypeValue == Card::ST_Level) ? props.LevelOrLinkRatingOrRank : 0;
        gc->Limitation = props.Limitation;
        gc->ID1 = gc->ID2 = gc->ID3 = c.ID;
        gc->Rank = 0;
        gc->LeftPendulumScale = gc->RightPendulumScale = 0;
        gc->LevelOrLinkRatingOrRank = props.LevelOrLinkRatingOrRank;
        gc->CardType = props.Type;
        gc->LinkRating = 0;
        gc->LinkArrows = 0;
        gc->Valid = 1;
        gc->Frame = frame;
    }
}

// ---------------------------------------------------------------------
// Patches
// ---------------------------------------------------------------------

namespace
{
    // The game keeps one { u32 refcount; i32 cacheIndex } image slot per internal card id,
    // inline in its image table object at obj + 0xC0 + 8 * id, for 10166 ids. Four sites
    // compute that address; each is replaced with a jmp into a small cave that keeps the
    // game's own computation for vanilla ids and uses Card::ImageSlotTable for the rest.
    // The cave is allocated within 2GB of the exe so a rel32 jmp reaches it.
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
        size_t length;        // bytes replaced (the rest becomes NOP)
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

        uint8_t stub[32];
        std::memset(stub, 0x90, sizeof(stub));
        const int64_t rel = static_cast<int64_t>(reinterpret_cast<uintptr_t>(cave)) - static_cast<int64_t>(site.address + 5);
        if (rel > INT32_MAX || rel < INT32_MIN)
            return false;

        const int32_t rel32 = static_cast<int32_t>(rel);
        stub[0] = 0xE9;
        std::memcpy(&stub[1], &rel32, sizeof(rel32));
        ApplyBytePatch(site.address, stub, site.length);
        return true;
    }

    void ApplyImageSlotPatches()
    {
        // Image_Request only serves ids up to 0x3A78 (mov eax, 3A78h ; cmp bp, ax ; ja).
        PatchImm32(0x140752CEB, kLastExtraCardId);

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
    auto it = Card::ExtraLoadIDs.find(static_cast<uint16_t>(konamiId));
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

    auto it = Card::ExtraLoadIDs.find(konamiId);
    Card::ExtraCard* card = it != Card::ExtraLoadIDs.end() ? Card::FindExtraCard(konamiId) : nullptr;
    if (!card)
        return reinterpret_cast<Get_IllustrationData_t>(orig_Get_IllustrationData)(a1, konamiId, buffer, size);

    if (!card->ImageTried)
    {
        card->ImageTried = true;

        std::ifstream file(card->ImagePath, std::ios::binary);
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

// ----------------------------------------------------------------------
// shop packs
//
// LoadPackDefinitions fills the 128 pack records (104 bytes each at 0x1429241C0). For a reward pack ('R') the
// pointer at +0x18 leads to u16 commonCount, u16 rareCount, the common ids, then the rare ids, and that list
// is what the pack draws from. For the packs in packs.json the pointer is replaced by a longer list that also
// holds the extra cards.
// ----------------------------------------------------------------------

static uintptr_t orig_LoadPackDefinitions = 0x14080E3C0;
constexpr uintptr_t kPackRecords = 0x1429241C0;
constexpr size_t kPackRecordSize = 0x68;
constexpr size_t kPackRecordCount = 128;
constexpr uint32_t kRewardPackKind = 'R';

static void ApplyPackAdditions()
{
    // The game may be reading an old list while packs reload, so old lists are never freed.
    static std::deque<std::vector<uint16_t>> lists;

    for (size_t i = 0; i < kPackRecordCount; ++i)
    {
        uint8_t* record = reinterpret_cast<uint8_t*>(kPackRecords + i * kPackRecordSize);
        const char* name = *reinterpret_cast<const char**>(record + 0x10);
        uint16_t* contents = *reinterpret_cast<uint16_t**>(record + 0x18);
        if (!name || !contents || *reinterpret_cast<uint32_t*>(record + 0xC) != kRewardPackKind)
            continue;

        for (const Card::PackAddition& addition : Card::PackAdditions)
        {
            if (addition.Pack != name)
                continue;

            const size_t commonCount = contents[0];
            const size_t rareCount = contents[1];
            std::vector<uint16_t> common(contents + 2, contents + 2 + commonCount);
            std::vector<uint16_t> rare(contents + 2 + commonCount, contents + 2 + commonCount + rareCount);

            // A replaced list that is empty would leave the game drawing from nothing, so it keeps the game's cards.
            if (addition.Replace)
            {
                if (addition.Common.empty() || addition.Rare.empty())
                    Logger::WriteLog(std::format("Pack {}: replacing with an empty {} list, keeping the game's", name,
                        addition.Common.empty() ? "common" : "rare"), MODULE_NAME, 1);
                if (!addition.Common.empty())
                    common.clear();
                if (!addition.Rare.empty())
                    rare.clear();
            }

            auto append = [](std::vector<uint16_t>& list, const std::vector<uint16_t>& extra)
            {
                for (uint16_t id : extra)
                {
                    if (std::find(list.begin(), list.end(), id) == list.end())
                        list.push_back(id);
                }
            };
            append(common, addition.Common);
            append(rare, addition.Rare);

            std::vector<uint16_t>& list = lists.emplace_back();
            list.reserve(2 + common.size() + rare.size());
            list.push_back(static_cast<uint16_t>(common.size()));
            list.push_back(static_cast<uint16_t>(rare.size()));
            list.insert(list.end(), common.begin(), common.end());
            list.insert(list.end(), rare.begin(), rare.end());

            *reinterpret_cast<uint16_t**>(record + 0x18) = list.data();
            if (addition.Replace)
                Logger::WriteLog(std::format("Pack {}: replaced with {} common and {} rare cards", name, common.size(), rare.size()), MODULE_NAME, 0);
            else
                Logger::WriteLog(std::format("Pack {}: {} common and {} rare cards ({} added)", name, common.size(), rare.size(),
                    common.size() + rare.size() - commonCount - rareCount), MODULE_NAME, 0);
        }
    }
}

char __fastcall Hook_LoadPackDefinitions()
{
    char result = reinterpret_cast<char(__fastcall*)()>(orig_LoadPackDefinitions)();
    if (result && !Card::PackAdditions.empty())
        ApplyPackAdditions();

    return result;
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
        size_t count = LoadCardsFromJson(std::string(kExtraCardsDirectory) + "cards.json");
        Logger::WriteLog(std::format("Loaded {} card(s) from cards.json", count), MODULE_NAME, 0);

        size_t unlocks = LoadUnlocksFromJson(std::string(kExtraCardsDirectory) + "unlocks.json");
        if (unlocks)
            Logger::WriteLog(std::format("Loaded {} unlock(s) from unlocks.json", unlocks), MODULE_NAME, 0);
        if (unlocks && ReplaceDefaultUnlocks)
            Logger::WriteLog("unlocks.json replaces the game's starting cards", MODULE_NAME, 0);

        size_t packs = LoadPacksFromJson(std::string(kExtraCardsDirectory) + "packs.json");
        if (packs)
            Logger::WriteLog(std::format("Loaded {} pack change(s) from packs.json", packs), MODULE_NAME, 0);

        // An unloaded slot is { refcount 0, cacheIndex -1 }.
        for (auto& slot : ImageSlotTable)
            slot = 0xFFFFFFFF00000000;
    }

    ExtraLoadIDs.clear();
    for (const ExtraCard& card : ExtraCards)
    {
        int64_t internalId = static_cast<int64_t>(CardProps.size());
        CardProps.push_back(card.Props);
        ExtraLoadIDs[card.ID] = internalId;

        if (firstRun)
            Logger::WriteLog(std::format("Card {} is internal id {}", card.ID, internalId), MODULE_NAME, 0);

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
    DetourAttach(&(PVOID&)orig_LoadPackDefinitions, Hook_LoadPackDefinitions);

    LONG err = DetourTransactionCommit();
    Logger::WriteLog(std::format("Card hooks attached: {}", err), MODULE_NAME, err == 0 ? 0 : 2);

    // The game loads its pack definitions (with the rest of the language content) before card setup runs, so
    // the hook above only sees later reloads. The first load has already happened: change those packs now.
    if (!PackAdditions.empty())
        ApplyPackAdditions();
}
