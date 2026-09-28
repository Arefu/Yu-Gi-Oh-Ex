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
// Level / rank stars are laid out as one row centred on StarsX, StarsY: by default the middle of the frame's coloured band between the
// art (its grey border ends at y 434) and the ATK/DEF boxes (y 497). StarsY < 0 keeps the game's height (y 83). The attribute circle
// starts at x 324 on the same line, so a row wider than StarsMaxWidth (about 8 stars) is shrunk to fit.
static float kStarsX = 200.0f;
static float kStarsY = 465.5f;
static float kStarsMaxWidth = 240.0f;

// The following tunables are read from Config.ini, section
// [Yu-Gi-Oh-AnimeCards], falling back to the defaults below if the file,
// section, or key is missing. These can no longer be constexpr since
// their values aren't known until DllMain runs, so they're plain statics
// populated by LoadConfig() before anything else touches them.
// ---------------------------------------------------------------------

// ATK and DEF are laid out centred on these points (see kCentredAlign): the centres of the two boxes at the bottom of the anime frames
// (x 31-184 and 215-368, y 497-556). On Link cards the right-hand box is the red rating box and shows the Link rating.
static float kCustomAtkX = 107.5f;
static float kCustomAtkY = 526.5f;
static float kCustomDefX = 291.5f;
static float kCustomDefY = 526.5f;

// The attribute icon goes in the frame's circle: bottom right on monster frames, bottom middle on the Link frame. Spell and Trap frames
// have their symbol drawn into the frame, so their attribute icon is not drawn at all.
static float kAttributeX = 346.0f;
static float kAttributeY = 469.5f;
static float kLinkAttributeX = 198.5f;
static float kLinkAttributeY = 469.0f;
// The frame's circle is 45 px across; the icon is drawn from the 48 px sprites (see Hook_IconId_Attribute) so it stays sharp.
static float kAttributeSize = 48.0f;

static float kAtkTextScale = 3.10;
static float kDefTextScale = 3.40;

static float kCardArtScale = 1.4f;
static float kCardArtOffsetX = 0.0f;   // positive = right, negative = left
static float kCardArtOffsetY = -50.0f; // positive = down, negative = up

// Defaults match the vanilla Spell/Trap property icon position
// (343, 83 non-JP / 343, 86 JP) - i.e. the top-right circular icon.
// Override via Config.ini if you want it somewhere else.
static float kCustomSTIconX = 343.0f;
static float kCustomSTIconY = 83.0f;

constexpr float kVanillaAtkX = 367.0f;
constexpr float kVanillaAtkY = 544.0f;
constexpr float kAbilitySlotX = 32.0f;
constexpr float kFloatEps = 0.01f;

inline bool NearlyEqual(float a, float b)
{
    return std::fabs(a - b) < kFloatEps;
}

constexpr uint32_t kAtkDefCombinedFontId = 35;

// The ATK/DEF numbers are drawn from bitmap font atlases (fontbin/*.fbin + .png), then enlarged by the
// AtkTextScale / DefTextScale matrix scale below. FONT_ID_CARD_ATKDEF (35) and the DEF font are 18 px
// glyphs, so a 3x enlargement is soft. FONT_ID_CARD_ATKDEF_SCALE (36) is the same typeface at 32 px:
// drawing with it and scaling by 18/32 less gives the same size on the card with much sharper digits.
// The font's own native size is read from the loaded font (its first float, at +40), so a larger
// replacement fbin/png (see Tools/FontGen) is picked up without changing the plugin.
constexpr uint32_t kLargeAtkDefFontId = 36;
constexpr float kAtkDefNativeSize = 18.0f;
static bool kUseLargeAtkDefFont = true;
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

// Spell/Trap property icon (source rect built with size 20.0 in
// sub_14074E7D0's v290/v291, placed at these baked-in offsets). This is
// the round icon at the top-right of the card.
constexpr float kSTIconX0 = 343.0f;
constexpr float kSTIconY0_NonJp = 83.0f;
constexpr float kSTIconY0_Jp = 86.0f;

// The card being built (set by Hook_sub_14074E7D0, defined further down) and the game's getters for it (FULL_CARD_PROPS, see
// docs/CardRendering.md): +0x1C IsMonster, +0x31 IsLink, +0x88 Link rating.
extern unsigned short g_currentCardId;
using fn_CardValue = int(__fastcall*)(unsigned short);
static fn_CardValue game_IsMonster = nullptr;    // 0x14081A410
static fn_CardValue game_IsLink = nullptr;       // 0x14081A470
static fn_CardValue game_LinkRating = nullptr;   // 0x14081A6D0

// The attribute icon: CardFace_Build centres it on 353.5, 45.5 (size 37). The atlas trims its sprites, so the quad is only roughly
// centred there; nothing else is drawn in that corner.
inline bool IsAttributeIcon(float x0, float y0, float x1, float y1)
{
    const float cx = (x0 + x1) * 0.5f;
    const float cy = (y0 + y1) * 0.5f;
    return cx > 330.0f && cx < 377.0f && cy > 22.0f && cy < 69.0f;
}

// The stars of the card being built, set by Hook_sub_14074E7D0. CardFace_Build draws level stars right to left from x 346, 28 apart
// (27 when there are more than 11), and rank stars left to right from x 54, 28 apart, all at size 28 on y 83 (see the loop at
// 0x14074FF80). Each star quad is re-placed at its index in a row centred on kStarsX, so 1, 2 or 3 stars all sit in the middle.
static thread_local int g_starCount = 0;
static thread_local int g_starIndex = 0;
static thread_local float g_starStep = 28.0f;

inline bool IsStarQuad(float x0, float y0, float x1, float y1)
{
    const float size = std::fmax(x1 - x0, y1 - y0);
    const float cy = (y0 + y1) * 0.5f;
    return g_starIndex < g_starCount && size > 24.0f && size < 32.0f && cy > 70.0f && cy < 125.0f;
}

extern "C" void Hook_sub_1408795A0(void* a1, float x0, float y0, float x1, float y1,
    float u0, float v0, float u1, float v1, int a10, int color)
{
    if (IsAttributeIcon(x0, y0, x1, y1))
    {
        // Spell and Trap frames have their symbol drawn in; monsters get the icon in the frame's circle.
        if ((game_IsMonster(g_currentCardId) & 0xFF) == 0)
            return;
        const bool link = (game_IsLink(g_currentCardId) & 0xFF) != 0;
        const float cx = link ? kLinkAttributeX : kAttributeX;
        const float cy = link ? kLinkAttributeY : kAttributeY;
        const float scale = kAttributeSize / std::fmax(x1 - x0, y1 - y0);
        const float halfW = (x1 - x0) * scale * 0.5f;
        const float halfH = (y1 - y0) * scale * 0.5f;
        orig_sub_1408795A0(a1, cx - halfW, cy - halfH, cx + halfW, cy + halfH, u0, v0, u1, v1, a10, color);
        return;
    }

    if (IsStarQuad(x0, y0, x1, y1))
    {
        const float rowWidth = (g_starCount - 1) * g_starStep + (x1 - x0);
        const float fit = rowWidth > kStarsMaxWidth ? kStarsMaxWidth / rowWidth : 1.0f;
        const float w = (x1 - x0) * fit;
        const float h = (y1 - y0) * fit;
        const float left = kStarsX - rowWidth * fit * 0.5f;
        const float nx0 = left + g_starIndex * g_starStep * fit;
        const float ny0 = kStarsY < 0.0f ? y0 : kStarsY - h * 0.5f;
        ++g_starIndex;
        orig_sub_1408795A0(a1, nx0, ny0, nx0 + w, ny0 + h, u0, v0, u1, v1, a10, color);
        return;
    }

    if (NearlyEqual(x0, kLineX0) && NearlyEqual(x1, kLineX1) &&
        NearlyEqual(y0, kLineY0) && NearlyEqual(y1, kLineY1))
    {
        // Hide the line above ATK/DEF, but still emit its 6 vertices. CardFace_Build hard-codes the line batch's
        // vertex count to 6 (0x14074FAFE), so skipping the quad made that batch draw the next quad (the attribute
        // icon) with the builtin white texture: a solid white square behind the icon.
        orig_sub_1408795A0(a1, x0, y0, x0, y0, u0, v0, u1, v1, a10, 0);
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

    bool isSTIcon = NearlyEqual(x0, kSTIconX0) &&
        (NearlyEqual(y0, kSTIconY0_NonJp) || NearlyEqual(y0, kSTIconY0_Jp));

    if (isSTIcon)
    {
        float w = x1 - x0;
        float h = y1 - y0;

        orig_sub_1408795A0(a1, kCustomSTIconX, kCustomSTIconY,
            kCustomSTIconX + w, kCustomSTIconY + h,
            u0, v0, u1, v1, a10, color);
        return;
    }

    orig_sub_1408795A0(a1, x0, y0, x1, y1, u0, v0, u1, v1, a10, color);
}

using fn_Get_RawDefFromFullCardProps = int(__fastcall*)(unsigned short);
using fn_sub_14081A670 = int(__fastcall*)(unsigned short);
using fn_sub_14081A730 = int(__fastcall*)(unsigned short);
using fn_Get_CardTypeFromFullCardPropsByKonamiId = int(__fastcall*)(unsigned short);
using fn_Get_SpellTrapCardPropertyFromFullCardProps = int(__fastcall*)(unsigned short);

static fn_Get_RawDefFromFullCardProps orig_Get_RawDefFromFullCardProps = nullptr;
static fn_sub_14081A670 orig_sub_14081A670 = nullptr;
static fn_sub_14081A730 orig_sub_14081A730 = nullptr;
static fn_Get_CardTypeFromFullCardPropsByKonamiId orig_Get_CardTypeFromFullCardPropsByKonamiId = nullptr;
static fn_Get_SpellTrapCardPropertyFromFullCardProps orig_Get_SpellTrapCardPropertyFromFullCardProps = nullptr;

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
//
// IMPORTANT: since this is used below to gate the monster ST-icon fix,
// test it against Fusion/Synchro/XYZ/Pendulum/Link monsters specifically
// — if any of those hit `default` instead of a monster-safe case, the
// icon-forcing hook below will misfire for them.
inline bool IsTrapSpellCard(unsigned short cardId)
{
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
   // if (IsTrapSpellCard(cardId)) return 0;
    return orig_Get_RawDefFromFullCardProps(cardId);
}

// True on the loader thread while CardFace_Build (Hook_sub_14074E7D0) runs. The getters below are the game's own and are also used outside
// drawing (deck rules, duels...), so they only change their answer for the card face being built.
static thread_local bool g_buildingFace = false;

// IconId_Attribute (0x1407FD320) picks the card's attribute icon from pdui/STEAM_icons: attribute + 129 = the 37 px ICON_ID_ATTR_L_* sprites,
// drawn 1:1 at the game's 37 px. Drawn larger in the frame's circle they would be stretched and soft, so card faces use the 48 px
// ICON_ID_ATTR_* sprites of the same attributes instead (attribute + 119), which are shrunk slightly and stay sharp.
constexpr int kLargeAttributeIconOffset = -10;
using fn_IconId = int(__fastcall*)(int);
static fn_IconId orig_IconId_Attribute = nullptr;

extern "C" int Hook_IconId_Attribute(int attribute)
{
    const int id = orig_IconId_Attribute(attribute);
    return g_buildingFace ? id + kLargeAttributeIconOffset : id;
}

// In sub_14074E7D0, the entire ST-icon build+draw block is gated on
// `Get_SpellTrapCardPropertyFromFullCardProps(cardId) != 0`. For monster
// cards this vanilla getter returns 0, so the icon never gets built and
// never reaches Hook_sub_1408795A0 above at all. Forcing a non-zero
// property id for monsters makes the game build + draw the icon through
// the normal codepath, at the position controlled by
// kCustomSTIconX/kCustomSTIconY above.
//
// kMonsterSTIconPropertyId: the value returned here is passed into
// sub_1407FD3E0 to resolve which icon texture to use. Pick whichever
// property id maps to the texture you want to show for monsters — you'll
// likely need to test a few values in-game. An id that doesn't map to a
// valid entry risks sub_1407FD3E0 or the downstream texture lookup
// misbehaving, so start conservative and verify no crashes before
// treating this as final.
constexpr int kMonsterSTIconPropertyId = 1; // TODO: verify this maps to the icon you want

extern "C" int Hook_Get_SpellTrapCardPropertyFromFullCardProps(unsigned short cardId)
{
    if (g_buildingFace && !IsTrapSpellCard(cardId)) // per IsTrapSpellCard's (inverted-named) logic, false == Spell, Trap or Link
    {
        return kMonsterSTIconPropertyId;
    }
    return orig_Get_SpellTrapCardPropertyFromFullCardProps(cardId);
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

// The card text fonts: entries 3..8 of the table, ending at -1 (30..34 non-JP, 24..26 JP). CardFace_Build tries them largest first.
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

using fn_Get_FontById = void* (__fastcall*)(unsigned int);
static fn_Get_FontById orig_Get_FontById = nullptr; // sub_140872C60: the loaded font for an id, or null

// True when the 32 px ATK/DEF font is loaded and should replace the 18 px ones.
inline bool UseLargeAtkDefFont()
{
    return kUseLargeAtkDefFont && orig_Get_FontById && orig_Get_FontById(kLargeAtkDefFontId) != nullptr;
}

// Native pixel size of the large ATK/DEF font, from the fbin the game loaded.
inline float LargeAtkDefNativeSize()
{
    float size = *reinterpret_cast<const float*>(static_cast<const char*>(orig_Get_FontById(kLargeAtkDefFontId)) + 40);
    return size > 1.0f ? size : 32.0f;
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

    // Same choice as CardFace_Build: StarCount level stars (0x14081A670), or RankStars rank stars (0x14081A730) when that is 0.
    const int level = orig_sub_14081A670(cardId);
    const int rank = level > 0 ? 0 : orig_sub_14081A730(cardId);
    g_starCount = level > 0 ? level : (rank > 0 ? rank : 0);
    g_starStep = (level > 11) ? 27.0f : 28.0f;
    g_starIndex = 0;

    g_buildingFace = true;
    const char result = orig_sub_14074E7D0(a1, cardId);
    g_buildingFace = false;
    g_starCount = 0;
    return result;
}

// TextSlot_Layout's align argument (Layout_FinishLine / Layout_Build): 1 left, 2 centre, 4 right; 8 top, 0x10 centre, 0x20 bottom. The game
// lays ATK/DEF out bottom-right (36) and the card text top-left (9), so the same y put ATK and DEF at different heights. Both numbers are
// laid out centred instead, unwrapped (0x100), so they sit exactly on the points given for them.
constexpr int kCentredAlign = 0x10 | 0x2;
constexpr int kNoWrap = 0x100;

extern "C" __int64 Hook_sub_140766540(__int64 slotPtr, __int64 x, __int64 y, float boxWidth,
    int color, unsigned int fontId,
    const unsigned short* wstr, int count, int flags, int arg10)
{
    if (fontId == kAtkDefCombinedFontId)
    {
        unsigned int atk = orig_YGO_Get_EffectiveDefFromFullCardProps(g_currentCardId);
        static wchar_t buf[16];
        if (atk == 0xFFFF)
            swprintf(buf, 16, L"?");
        else
            swprintf(buf, 16, L"%u", atk);
        return orig_sub_140766540(slotPtr, x, y, 0.0f, color,
            UseLargeAtkDefFont() ? kLargeAtkDefFontId : fontId,
            (const unsigned short*)buf, kCentredAlign, flags | kNoWrap, arg10);
    }

    if (IsAbilityWrapFont(fontId))
    {
        // The card text slot shows the right-hand number: DEF, or the rating on a Link monster (its box is the red one).
        const bool link = (game_IsLink(g_currentCardId) & 0xFF) != 0;
        if (link)
        {
            static wchar_t rating[16];
            swprintf(rating, 16, L"%d", game_LinkRating(g_currentCardId));
            return orig_sub_140766540(slotPtr, x, y, 0.0f, color,
                UseLargeAtkDefFont() ? kLargeAtkDefFontId : fontId,
                (const unsigned short*)rating, kCentredAlign, flags | kNoWrap, arg10);
        }

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
        if (def == 0xFFFF)
            swprintf(buf, 16, L"?");
        else
            swprintf(buf, 16, L"%u", def);
        return orig_sub_140766540(slotPtr, x, y, 0.0f, color,
            UseLargeAtkDefFont() ? kLargeAtkDefFontId : fontId,
            (const unsigned short*)buf, kCentredAlign, flags | kNoWrap, arg10);
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

    // Fonts are plain bitmaps at one pixel size (see docs/CardRendering.md), so any scale above 1 blurs them. With the 32 px font the
    // numbers are drawn at its native size, unscaled; AtkTextScale / DefTextScale only apply to the 18 px fonts.
    if (UseLargeAtkDefFont())
        scale = 1.0f;

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

        // Positions are the centres of the numbers (they are laid out centred), defaulting to the frame's two boxes.
        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomAtkX", "107.5", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomAtkX = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomAtkX = vCustomAtkX;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomAtkY", "526.5", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomAtkY = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomAtkY = vCustomAtkY;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomDefX", "291.5", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomDefX = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomDefX = vCustomDefX;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomDefY", "526.5", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomDefY = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomDefY = vCustomDefY;

        // The attribute icon: centre on monster frames, centre on the Link frame, and its size (the circles are 45 px across).
        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "AttributeX", "346", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vAttributeX = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kAttributeX = vAttributeX;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "AttributeY", "469.5", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vAttributeY = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kAttributeY = vAttributeY;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "LinkAttributeX", "198.5", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vLinkAttributeX = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kLinkAttributeX = vLinkAttributeX;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "LinkAttributeY", "469", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vLinkAttributeY = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kLinkAttributeY = vLinkAttributeY;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "AttributeSize", "48", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vAttributeSize = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kAttributeSize = vAttributeSize;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "StarsX", "200", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vStarsX = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kStarsX = vStarsX;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "StarsY", "465.5", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vStarsY = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kStarsY = vStarsY;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "StarsMaxWidth", "240", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vStarsMaxWidth = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf && vStarsMaxWidth > 0.0f) kStarsMaxWidth = vStarsMaxWidth;

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

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomSTIconX", "500", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomSTIconX = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomSTIconX = vCustomSTIconX;

        GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", "CustomSTIconY", "183", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
        cfgEnd = nullptr; float vCustomSTIconY = std::strtof(cfgBuf, &cfgEnd);
        if (cfgEnd != cfgBuf) kCustomSTIconY = vCustomSTIconY;

        kUseLargeAtkDefFont = GetPrivateProfileIntA("Yu-Gi-Oh-AnimeCards", "UseLargeAtkDefFont", 1, ".\\Config.ini") != 0;

        p_g_bIsJpVersion = (bool*)ResolveVA(0x14332A348);
        orig_Get_FontById = (fn_Get_FontById)ResolveVA(0x140872C60);
        game_IsMonster = (fn_CardValue)ResolveVA(0x14081A410);
        game_IsLink = (fn_CardValue)ResolveVA(0x14081A470);
        game_LinkRating = (fn_CardValue)ResolveVA(0x14081A6D0);
        orig_IconId_Attribute = (fn_IconId)ResolveVA(0x1407FD320);

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

        // TODO: fill in the real RVA for Get_SpellTrapCardPropertyFromFullCardProps
        // (only seen as thunk j_YGO::CARDS::Get_SpellTrapCardPropertyFromFullCardProps
        // so far — the thunk's own target address in your IDB is what belongs here).
        // Leaving this at 0 will resolve to GameBase() - kImageBase, which is NOT a
        // valid function pointer — do not attach/build until this is corrected.
        orig_Get_SpellTrapCardPropertyFromFullCardProps =
            (fn_Get_SpellTrapCardPropertyFromFullCardProps)ResolveVA(0x014081A630 /* TODO */);

        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_sub_140766540, Hook_sub_140766540);
        DetourAttach(&(PVOID&)orig_sub_140877EC0, Hook_sub_140877EC0);
        DetourAttach(&(PVOID&)orig_sub_1408795A0, Hook_sub_1408795A0);
        DetourAttach(&(PVOID&)orig_sub_14074E7D0, Hook_sub_14074E7D0);
        DetourAttach(&(PVOID&)orig_Get_RawDefFromFullCardProps, Hook_Get_RawDefFromFullCardProps);
        DetourAttach(&(PVOID&)orig_IconId_Attribute, Hook_IconId_Attribute);
         DetourAttach(&(PVOID&)orig_Get_SpellTrapCardPropertyFromFullCardProps,Hook_Get_SpellTrapCardPropertyFromFullCardProps); // uncomment once RVA above is fixed
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
