#include <Windows.h>
#include <cstdint>
#include <cmath>
#include <cwchar>
#include <cstdlib>
#include <cstdio>
#include <string>
#include <format>
#include <iostream>

#include "Detours.h"
#include "Logger.h"

constexpr uintptr_t kImageBase = 0x140000000;

inline uintptr_t GameBase()
{
    static uintptr_t base = (uintptr_t)GetModuleHandleA(nullptr);
    return base;
}

inline void* ResolveVA(uintptr_t va)
{
    return (void*)(GameBase() + (va - kImageBase));
}

// ---------------------------------------------------------------------
// Config.ini loading
//
// The following tunables are read from Config.ini, section
// [Yu-Gi-Oh-AnimeCards], falling back to the defaults below if the file,
// section, or key is missing. These can no longer be constexpr since
// their values aren't known until DllMain runs, so they're plain statics
// populated by LoadConfig() before anything else touches them.
// ---------------------------------------------------------------------

static float kCustomAtkX = 135.0f;
static float kCustomAtkY = 507.0f;
static float kCustomDefX = 250.0f;
static float kCustomDefY = 500.0f;

static float kAtkTextScale = 2.5f;
static float kDefTextScale = 2.5f;

static float kCardArtScale = 1.3f;
static float kCardArtOffsetX = 0.0f;   // positive = right, negative = left
static float kCardArtOffsetY = -50.0f; // positive = down, negative = up

constexpr float kVanillaAtkX = 367.0f;
constexpr float kVanillaAtkY = 544.0f;
constexpr float kAbilitySlotX = 32.0f;
constexpr float kFloatEps = 0.01f;

inline bool NearlyEqual(float a, float b)
{
    return std::fabs(a - b) < kFloatEps;
}

constexpr uint32_t kAtkDefCombinedFontId = 35;
constexpr uint32_t kFontSentinel = 0xFFFFFFFFu;

using fn_sub_1408795A0 = void(__fastcall*)(void*, float, float, float, float, float, float, float, float, int, int);
static fn_sub_1408795A0 orig_sub_1408795A0 = nullptr;

constexpr float kLineX0 = 32.0f;
constexpr float kLineY0 = 530.0f;
constexpr float kLineX1 = 368.0f;
constexpr float kLineY1 = 531.0f;

constexpr float kCardImgX0 = 0.0f;
constexpr float kCardImgY0 = 0.0f;
constexpr float kCardImgX1 = 400.0f;
constexpr float kCardImgY1 = 580.0f;
constexpr float kArtNormalX0 = 48.0f;
constexpr float kArtNormalY0 = 106.0f;
constexpr float kArtNormalX1 = 352.0f;  // 48 + 304
constexpr float kArtNormalY1 = 410.0f;  // 106 + 304

constexpr float kArtLinkX0 = 26.0f;
constexpr float kArtLinkY0 = 104.0f;
constexpr float kArtLinkX1 = 373.0f;    // 26 + 347
constexpr float kArtLinkY1 = 548.0f;    // 104 + 444

extern "C" void Hook_sub_1408795A0(void* a1, float x0, float y0, float x1, float y1,
    float u0, float v0, float u1, float v1, int a10, int color)
{
    if (NearlyEqual(x0, kLineX0) && NearlyEqual(x1, kLineX1) &&
        NearlyEqual(y0, kLineY0) && NearlyEqual(y1, kLineY1))
    {
        return;
    }

    bool isNormalArt = NearlyEqual(x0, kArtNormalX0) && NearlyEqual(y0, kArtNormalY0) &&
        NearlyEqual(x1, kArtNormalX1) && NearlyEqual(y1, kArtNormalY1);
    bool isLinkArt = NearlyEqual(x0, kArtLinkX0) && NearlyEqual(y0, kArtLinkY0) &&
        NearlyEqual(x1, kArtLinkX1) && NearlyEqual(y1, kArtLinkY1);

    if ((isNormalArt || isLinkArt) && kCardArtScale != 1.0f)
    {
        float w = (x1 - x0) * kCardArtScale;
        float h = (y1 - y0) * kCardArtScale;
        float cx = (x0 + x1) * 0.5f + kCardArtOffsetX;
        float cy = (y0 + y1) * 0.5f + kCardArtOffsetY;

        orig_sub_1408795A0(a1, cx - w * 0.5f, cy - h * 0.5f, cx + w * 0.5f, cy + h * 0.5f,
            u0, v0, u1, v1, a10, color);
        return;
    }

    orig_sub_1408795A0(a1, x0, y0, x1, y1, u0, v0, u1, v1, a10, color);
}

using fn_Get_RawDefFromFullCardProps = int(__fastcall*)(unsigned short);
using fn_sub_14081A670 = int(__fastcall*)(unsigned short);
using fn_sub_14081A730 = int(__fastcall*)(unsigned short);
using fn_Get_CardTypeFromFullCardPropsByKonamiId = int(__fastcall*)(unsigned short);

static fn_Get_RawDefFromFullCardProps orig_Get_RawDefFromFullCardProps = nullptr;
static fn_sub_14081A670 orig_sub_14081A670 = nullptr;
static fn_sub_14081A730 orig_sub_14081A730 = nullptr;
static fn_Get_CardTypeFromFullCardPropsByKonamiId orig_Get_CardTypeFromFullCardPropsByKonamiId = nullptr;

// Forward-declared here; g_currentCardId is defined further down, right
// next to Hook_sub_14074E7D0 which sets it. Declared early so the
// IsMonster() helper below can use it.
extern unsigned short g_currentCardId;

// Single point of truth for "is this card a monster" — computed on demand
// from the card-type getter, keyed off whatever card is currently being
// drawn. No caching/statics here on purpose: earlier attempts to cache
// this in a getter hook (first via SpellOrTrapProperty, which turned out
// to be garbage/unset for monster cards, then via a stale cached bool)
// caused inconsistent results across card types. Fetching fresh at each
// use site avoids ordering issues entirely.
//
// NOTE (unverified): case 13/14 currently return false here, and
// case 0/default return true - this is inverted from how the function
// is named/was originally written. Left as-is since it wasn't confirmed
// whether this was an intentional change (e.g. discovering 13/14 map to
// something other than Spell/Trap for this game's encoding, possibly
// tangled up with Synchro/Fusion/etc. extra-deck types) or a mistake.
// Worth double-checking against real card data before relying on it.
inline bool IsTrapSpellCard(unsigned short cardId)
{
    int t = orig_Get_CardTypeFromFullCardPropsByKonamiId(cardId);
    switch (orig_Get_CardTypeFromFullCardPropsByKonamiId(cardId))

    {
     

    case 13:
    case 14:
    case 42:
    case 43:
        return false;

    case 0:
    default:
        return true;
    }
}

extern "C" int Hook_Get_RawDefFromFullCardProps(unsigned short cardId)
{
    if (IsTrapSpellCard(cardId)) return 0;
    return orig_Get_RawDefFromFullCardProps(cardId);
}

extern "C" int Hook_sub_14081A670(unsigned short cardId)
{
    if (IsTrapSpellCard(cardId)) return 0;

    return orig_sub_14081A670(cardId);
}

extern "C" int Hook_sub_14081A730(unsigned short cardId)
{
    if (IsTrapSpellCard(cardId)) return 0;
    return orig_sub_14081A730(cardId);
}

struct FontTable
{
    uint32_t v[11];
};

static bool* p_g_bIsJpVersion = nullptr;

inline const FontTable& GetFontTable()
{
    static const FontTable* nonJp = reinterpret_cast<const FontTable*>(ResolveVA(0x140A4CB00));
    static const FontTable* jp = reinterpret_cast<const FontTable*>(ResolveVA(0x140A4CB30));
    return *p_g_bIsJpVersion ? *jp : *nonJp;
}

inline bool IsAbilityWrapFont(uint32_t fontId)
{
    const FontTable& fonts = GetFontTable();
    for (int i = 3; i <= 8; ++i)
    {
        if (fonts.v[i] == kFontSentinel) break;
        if (fonts.v[i] == fontId) return true;
    }
    return false;
}

using fn_sub_140766540 = __int64(__fastcall*)(__int64, __int64, __int64, float, int, unsigned int, const unsigned short*, int, int, int);
using fn_sub_140877EC0 = void* (__fastcall*)(void*, float, float, float);
using fn_YGO_Get_EffectiveDefFromFullCardProps = unsigned int(__fastcall*)(unsigned short);
using fn_sub_14081A610 = unsigned int(__fastcall*)(unsigned short);
using fn_sub_14074E7D0 = char(__fastcall*)(__int64, unsigned short);

static fn_sub_140766540 orig_sub_140766540 = nullptr;
static fn_sub_140877EC0 orig_sub_140877EC0 = nullptr;
static fn_YGO_Get_EffectiveDefFromFullCardProps orig_YGO_Get_EffectiveDefFromFullCardProps = nullptr;
static fn_sub_14081A610 orig_sub_14081A610 = nullptr;
static fn_sub_14074E7D0 orig_sub_14074E7D0 = nullptr;

unsigned short g_currentCardId = 0xFFFF;

extern "C" char Hook_sub_14074E7D0(__int64 a1, unsigned short cardId)
{
    g_currentCardId = cardId;
    return orig_sub_14074E7D0(a1, cardId);
}

extern "C" __int64 Hook_sub_140766540(__int64 slotPtr, __int64 x, __int64 y, float boxWidth,
    int color, unsigned int fontId,
    const unsigned short* wstr, int count, int flags, int arg10)
{
    if (fontId == kAtkDefCombinedFontId)
    {
        unsigned int atk = orig_YGO_Get_EffectiveDefFromFullCardProps(g_currentCardId);
        static wchar_t buf[16];
        swprintf(buf, 16, L"%u", atk);
        return orig_sub_140766540(slotPtr, x, y, boxWidth, color, fontId,
            (const unsigned short*)buf, count, flags, arg10);
    }

    if (IsAbilityWrapFont(fontId))
    {
        if (!IsTrapSpellCard(g_currentCardId))
        {
            // Was `return 0;` - dropped the call entirely and left the
            // text object's texture uninitialized, which is what crashed
            // in sub_140754420 (**(v9 + a1 + 808) reading a null texture
            // pointer). Call through with a blank string instead so the
            // texture still gets set up, same fix as the fallback below.
            static const unsigned short kBlank[2] = { L' ', 0 };
            return orig_sub_140766540(slotPtr, x, y, boxWidth, color, fontId,
                kBlank, 1, flags, arg10);
        }

        unsigned int def = orig_sub_14081A610(g_currentCardId);
        static wchar_t buf[16];
        swprintf(buf, 16, L"%u", def);
        return orig_sub_140766540(slotPtr, x, y, boxWidth, color, fontId,
            (const unsigned short*)buf, count, flags, arg10);
    }

    static const unsigned short kBlank[2] = { L' ', 0 };
    return orig_sub_140766540(slotPtr, x, y, boxWidth, color, fontId,
        kBlank, 1, flags, arg10);
}

extern "C" void* Hook_sub_140877EC0(void* out, float x, float y, float z)
{
    float scale = 1.0f;
    float tx = x, ty = y;

    if (NearlyEqual(x, kVanillaAtkX) && NearlyEqual(y, kVanillaAtkY))
    {
        tx = kCustomAtkX; ty = kCustomAtkY;
        scale = kAtkTextScale;
    }
    else if (NearlyEqual(x, kAbilitySlotX))
    {
        tx = kCustomDefX; ty = kCustomDefY;
        scale = kDefTextScale;
    }
    else
    {
        return orig_sub_140877EC0(out, x, y, z);
    }

    void* result = orig_sub_140877EC0(out, tx, ty, z);

    if (scale != 1.0f)
    {
        float* m = (float*)out;
        m[0] *= scale;
        m[5] *= scale;
    }

    return result;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved)
{
    switch (ul_reason_for_call)
    {
    case DLL_PROCESS_ATTACH:
    {
        Logger::SetupLogger();

        char cfgBuf[64];
        char* cfgEnd = nullptr;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomAtkX", "135", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomAtkX = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomAtkX = vCustomAtkX;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomAtkY", "507", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomAtkY = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomAtkY = vCustomAtkY;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomDefX", "250", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomDefX = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomDefX = vCustomDefX;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomDefY", "500", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomDefY = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomDefY = vCustomDefY;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "AtkTextScale", "2.5", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vAtkTextScale = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kAtkTextScale = vAtkTextScale;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "DefTextScale", "2.5", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vDefTextScale = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kDefTextScale = vDefTextScale;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CardArtScale", "1.3", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCardArtScale = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCardArtScale = vCardArtScale;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CardArtOffsetX", "0", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCardArtOffsetX = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCardArtOffsetX = vCardArtOffsetX;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CardArtOffsetY", "-50", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCardArtOffsetY = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCardArtOffsetY = vCardArtOffsetY;

        p_g_bIsJpVersion = (bool*)ResolveVA(0x14332A348);

        orig_sub_140766540 = (fn_sub_140766540)ResolveVA(0x140766540);
        orig_sub_140877EC0 = (fn_sub_140877EC0)ResolveVA(0x140877EC0);
        orig_YGO_Get_EffectiveDefFromFullCardProps = (fn_YGO_Get_EffectiveDefFromFullCardProps)ResolveVA(0x14081A5B0);
        orig_sub_14081A610 = (fn_sub_14081A610)ResolveVA(0x14081A610);
        orig_sub_1408795A0 = (fn_sub_1408795A0)ResolveVA(0x1408795A0);
        orig_sub_14074E7D0 = (fn_sub_14074E7D0)ResolveVA(0x14074E7D0);

        orig_Get_RawDefFromFullCardProps = (fn_Get_RawDefFromFullCardProps)ResolveVA(0x14081A5D0);
        orig_sub_14081A670 = (fn_sub_14081A670)ResolveVA(0x14081A670);
        orig_sub_14081A730 = (fn_sub_14081A730)ResolveVA(0x14081A730);
        orig_Get_CardTypeFromFullCardPropsByKonamiId = (fn_Get_CardTypeFromFullCardPropsByKonamiId)ResolveVA(0x14081A650);

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_sub_140766540, Hook_sub_140766540);
        DetourAttach(&(PVOID&)orig_sub_140877EC0, Hook_sub_140877EC0);
        DetourAttach(&(PVOID&)orig_sub_1408795A0, Hook_sub_1408795A0);
        DetourAttach(&(PVOID&)orig_sub_14074E7D0, Hook_sub_14074E7D0);
        DetourAttach(&(PVOID&)orig_Get_RawDefFromFullCardProps, Hook_Get_RawDefFromFullCardProps);
        DetourAttach(&(PVOID&)orig_sub_14081A670, Hook_sub_14081A670);
        DetourAttach(&(PVOID&)orig_sub_14081A730, Hook_sub_14081A730);
        LONG err = DetourTransactionCommit();
        (void)err;

        break;
    }
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
