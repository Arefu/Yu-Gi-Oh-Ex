#include <Windows.h>
#include <cstdint>
#include <cmath>
#include <cwchar>
#include <cstdlib>
#include <cstdio>
#include <string>
#include <format>
#include <iostream>
#include <intrin.h>

#include "Detours.h"
#include "Logger.h"
#include "YuGiOh/YuGiOh-Addresses.h"

constexpr uintptr_t kImageBase = YGO::Addr::ImageBase;

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
static bool kStarsAlignRight = false;   // StarsAlign=1

// The card name (font 27, top-left 31,46; JP 31,52; squeezed to 301 px, docs/CardRendering.md). Hidden unless ShowName=1.
constexpr float kNameX0 = 31.0f, kNameY0 = 46.0f, kNameY0Jp = 52.0f;
static bool kShowName = false;
static bool kHideSpellTrapIcon = false;   // HideSpellTrapIcon=1: no Quick-Play / Continuous / Equip ... icon
static float kNameX = 31.0f, kNameY = 46.0f, kNameWidth = 0.0f;   // NameWidth 0 = the game's 301

// FullArtIllustration=1: the illustration is stretched over the whole 400 x 580 face (instead of CardArtScale / offsets). The frame,
// stars, attribute and ATK/DEF are all still drawn as usual.
static bool kFullCardArt = false;

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
static bool kSpellTrapAttribute = false;   // Config SpellTrapAttribute: spells / traps show their SPELL / TRAP icon too

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
extern thread_local unsigned short g_currentCardId;
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
static thread_local bool g_buildingFace = false;
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
    if (!g_buildingFace)
    {
        orig_sub_1408795A0(a1, x0, y0, x1, y1, u0, v0, u1, v1, a10, color);
        return;
    }

    if (kFullCardArt)
    {
        const bool art = (NearlyEqual(x0, kArtNormalX0) && NearlyEqual(y0, kArtNormalY0) &&
            NearlyEqual(x1, kArtNormalX1) && NearlyEqual(y1, kArtNormalY1)) ||
            (NearlyEqual(x0, kArtLinkX0) && NearlyEqual(y0, kArtLinkY0) &&
            NearlyEqual(x1, kArtLinkX1) && NearlyEqual(y1, kArtLinkY1));
        if (art)
        {
            orig_sub_1408795A0(a1, kCardImgX0, kCardImgY0, kCardImgX1, kCardImgY1, u0, v0, u1, v1, a10, color);
            return;
        }
    }

    if (IsAttributeIcon(x0, y0, x1, y1))
    {
        // Spell and Trap frames have their symbol drawn in; monsters get the icon in the frame's circle. With SpellTrapAttribute=1
        // (frames without a symbol, such as Forbidden Memories') spells and traps get their SPELL / TRAP icon in the same place.
        if ((game_IsMonster(g_currentCardId) & 0xFF) == 0 && !kSpellTrapAttribute)
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
        // StarsAlign=1: the row ends at StarsX (Forbidden Memories lays its stars out from the right), else it is centred on it
        const float left = kStarsAlignRight ? kStarsX - rowWidth * fit : kStarsX - rowWidth * fit * 0.5f;
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

    if ((isNormalArt || isLinkArt) && (kCardArtScale != 1.0f || kCardArtOffsetX != 0.0f || kCardArtOffsetY != 0.0f))
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

    if (isSTIcon && kHideSpellTrapIcon)
    {
        // HideSpellTrapIcon=1 (Forbidden Memories cards have no Quick-Play / Continuous / Equip icon): a zero-size quad, so the batch
        // still gets its 6 vertices (see the line above ATK/DEF below).
        orig_sub_1408795A0(a1, x0, y0, x0, y0, u0, v0, u1, v1, a10, 0);
        return;
    }

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

using fn_sub_14081A670 = int(__fastcall*)(unsigned short);
using fn_sub_14081A730 = int(__fastcall*)(unsigned short);

static fn_sub_14081A670 orig_sub_14081A670 = nullptr;
static fn_sub_14081A730 orig_sub_14081A730 = nullptr;

// True on the loader thread while CardFace_Build (Hook_sub_14074E7D0) runs. The getters below are the game's own and are also used outside
// drawing (deck rules, duels...), so they only change their answer for the card face being built.
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

// AtkLabel / DefLabel (Config): text in front of the numbers ("ATK/", "DEF/" on Forbidden Memories cards). The 32 px number font only has
// digits, so labelled numbers keep the card's own font and AtkTextScale / DefTextScale.
static wchar_t g_AtkLabel[16] = L"";
static wchar_t g_DefLabel[16] = L"";

inline bool HasAtkDefLabels()
{
    return g_AtkLabel[0] != 0 || g_DefLabel[0] != 0;
}

inline unsigned int NumberFont(unsigned int fontId)
{
    return UseLargeAtkDefFont() && !HasAtkDefLabels() ? kLargeAtkDefFontId : fontId;
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

thread_local unsigned short g_currentCardId = 0xFFFF;

extern "C" char Hook_sub_14074E7D0(__int64 a1, unsigned short cardId)
{
    // Preserve an enclosing build if the game recursively requests another face.
    const auto previousCard = g_currentCardId;
    const bool previousBuilding = g_buildingFace;
    const int previousCount = g_starCount, previousIndex = g_starIndex;
    const float previousStep = g_starStep;
    g_currentCardId = cardId;

    // Same choice as CardFace_Build: StarCount level stars (0x14081A670), or RankStars rank stars (0x14081A730) when that is 0.
    const int level = orig_sub_14081A670(cardId);
    const int rank = level > 0 ? 0 : orig_sub_14081A730(cardId);
    g_starCount = level > 0 ? level : (rank > 0 ? rank : 0);
    g_starStep = (level > 11) ? 27.0f : 28.0f;
    g_starIndex = 0;

    g_buildingFace = true;
    const char result = orig_sub_14074E7D0(a1, cardId);
    g_currentCardId = previousCard;
    g_buildingFace = previousBuilding;
    g_starCount = previousCount;
    g_starIndex = previousIndex;
    g_starStep = previousStep;
    return result;
}

// The duel's level/rank badge (a star or rank icon plus the number) drawn over a card view - the cards in your hand. The anime
// frames show the stars on the card itself, so the badge is hidden. DuelCardView_UpdateLevelBadge (0x1407C6980, this) reads the
// view's card data (this+0x30): +292 kind (1 level -> icon sprite 0x20, 2 rank -> 0x21, else icon and number off), +284 the number,
// +164 its location (side | zone << 1 | index << 6; zone 13 = hand). The kind is filled by 0x1407CBBC0 / 0x1407CBDA0. It is set to 0
// only for the call, so nothing else sees the change. Its one caller (0x1407C67CA) reloads every register after the call, so a plain
// detour is safe here (see Yu-Gi-Oh-MP/Stubs.h on the game's register assumptions).
// Config HideLevelBadge: 0 = show it (vanilla), 1 = hide it in the hand (default), 2 = hide it everywhere.
//
// The big number over each monster in your hand is drawn separately: DuelHand_Draw (0x1407B8570) formats data+284 with "%d"
// for kind 1/2 and draws it with DuelHand_DrawLevelNumber (0x14078B0B0; floats x, 36, 32, 48 in xmm0-3, colours and the text
// on the stack). That is its only caller, and the caller reloads its registers and ignores the result, so the hook just
// returns without drawing when HideLevelBadge is on (1 or 2: this renderer only draws the hand).
using fn_DuelHand_DrawLevelNumber = void(__fastcall*)();
static fn_DuelHand_DrawLevelNumber orig_DuelHand_DrawLevelNumber = nullptr;

extern "C" void Hook_DuelHand_DrawLevelNumber()
{
}

using fn_DuelCardView_UpdateLevelBadge = void(__fastcall*)(__int64);
static fn_DuelCardView_UpdateLevelBadge orig_DuelCardView_UpdateLevelBadge = nullptr;
static int kHideLevelBadge = 1;

// The stat plate under a face-up monster on the field (level badge, ATK / DEF, attribute and type icons). DuelCardView_Refresh
// (0x1407C6660) shows the view, sets its Link arrows, then DuelCardView_UpdateStatPlate (0x1407C6180) enables the plate's nodes:
// view+152 the plate, +376 / +392 the ATK box / text, +408 / +424 the DEF box / text (not on Link monsters), +280 and +328 the two
// icons. The anime frames carry all of that on the card, so with HideFieldStats=1 (default) those nodes are switched off again after the
// game's refresh; the level badge on the field goes too (see Hook_DuelCardView_UpdateLevelBadge). Link arrows (+472..) stay.
// Its caller (a loop at 0x1407C54C0: mov rcx, rbx; call) keeps only callee-saved registers, so a plain detour is safe.
using fn_DuelCardView_Refresh = void(__fastcall*)(__int64);
static fn_DuelCardView_Refresh orig_DuelCardView_Refresh = nullptr;
using fn_WidgetBase_SetEnabled = void(__fastcall*)(__int64, char);
static fn_WidgetBase_SetEnabled game_WidgetBase_SetEnabled = nullptr;
static int kHideFieldStats = 1;
constexpr int kStatPlateNodes[] = { 152, 376, 408, 280, 328 };

extern "C" void Hook_DuelCardView_Refresh(__int64 view)
{
    orig_DuelCardView_Refresh(view);
    if (kHideFieldStats == 0 || view == 0)
        return;
    for (int offset : kStatPlateNodes)
        if (const __int64 node = *reinterpret_cast<__int64*>(view + offset))
            game_WidgetBase_SetEnabled(node, 0);
}

extern "C" void Hook_DuelCardView_UpdateLevelBadge(__int64 view)
{
    auto* data = view ? *reinterpret_cast<unsigned char**>(view + 0x30) : nullptr;
    if ((kHideLevelBadge == 0 && kHideFieldStats == 0) || data == nullptr)
    {
        orig_DuelCardView_UpdateLevelBadge(view);
        return;
    }
    const unsigned zone = (*reinterpret_cast<unsigned short*>(data + 164) >> 1) & 0x1F;
    const bool hide = kHideLevelBadge == 2 || (kHideLevelBadge == 1 && zone == 13) || (kHideFieldStats != 0 && zone != 13);
    if (!hide)
    {
        orig_DuelCardView_UpdateLevelBadge(view);
        return;
    }
    int& kind = *reinterpret_cast<int*>(data + 292);
    const int saved = kind;
    kind = 0;
    orig_DuelCardView_UpdateLevelBadge(view);
    kind = saved;
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
    if (!g_buildingFace)
        return orig_sub_140766540(slotPtr, x, y, boxWidth, color, fontId, wstr, count, flags, arg10);

    if (fontId == kAtkDefCombinedFontId)
    {
        unsigned int atk = orig_YGO_Get_EffectiveDefFromFullCardProps(g_currentCardId);
        wchar_t buf[48];
        if (atk == 0xFFFF)
            swprintf(buf, 48, L"%s?", g_AtkLabel);
        else
            swprintf(buf, 48, L"%s%u", g_AtkLabel, atk);
        return orig_sub_140766540(slotPtr, x, y, 0.0f, color,
            NumberFont(fontId),
            (const unsigned short*)buf, kCentredAlign, flags | kNoWrap, arg10);
    }

    // Font 36 also draws Pendulum scales. Keep their original numbers and positions.
    if (fontId == kLargeAtkDefFontId)
        return orig_sub_140766540(slotPtr, x, y, boxWidth, color, fontId, wstr, count, flags, arg10);

    // Pendulum effects use the same fonts but a narrower (278 px) text box.
    // Only the main description's 336 px layout becomes the DEF/rating number.
    if (NearlyEqual(boxWidth, 336.0f) && IsAbilityWrapFont(fontId))
    {
        // The card text slot shows the right-hand number: DEF, or the rating on a Link monster (its box is the red one).
        const bool link = (game_IsLink(g_currentCardId) & 0xFF) != 0;
        if (link)
        {
            wchar_t rating[16];
            swprintf(rating, 16, L"%d", game_LinkRating(g_currentCardId));
            return orig_sub_140766540(slotPtr, x, y, 0.0f, color,
                UseLargeAtkDefFont() ? kLargeAtkDefFontId : fontId,
                (const unsigned short*)rating, kCentredAlign, flags | kNoWrap, arg10);
        }

        if ((game_IsMonster(g_currentCardId) & 0xFF) == 0)
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
        wchar_t buf[48];
        if (def == 0xFFFF)
            swprintf(buf, 48, L"%s?", g_DefLabel);
        else
            swprintf(buf, 48, L"%s%u", g_DefLabel, def);
        // With labels both numbers use the ATK/DEF font, so "ATK 3000" and "DEF 2500" come out the same size.
        return orig_sub_140766540(slotPtr, x, y, 0.0f, color,
            HasAtkDefLabels() ? kAtkDefCombinedFontId : NumberFont(fontId),
            (const unsigned short*)buf, kCentredAlign, flags | kNoWrap, arg10);
    }

    // ShowName=1: the card's name stays (frames with a name bar, such as Forbidden Memories'), squeezed to NameWidth.
    if (kShowName && fontId == GetFontTable().v[0])
        return orig_sub_140766540(slotPtr, x, y, kNameWidth > 0.0f ? kNameWidth : boxWidth, color, fontId, wstr, count, flags, arg10);

    static const unsigned short kBlank[2] = { L' ', 0 };
    return orig_sub_140766540(slotPtr, x, y, boxWidth, color, fontId,
        kBlank, 1, flags, arg10);
}

// Matrix4_Translation is shared: besides CardFace_Build it is called by DFX::TLayerAnimoo::Render (via 0x140746EA0, every animated UI
// layer's offset) and two duel functions (0x1407D4290, 0x1407D67F0). Without the g_buildingFace gate any UI layer offset at x 32 was
// moved to the DEF box position and scaled, which threw menus and duel widgets out of place.
extern "C" void* Hook_sub_140877EC0(void* out, float x, float y, float z)
{
    if (!g_buildingFace)
        return orig_sub_140877EC0(out, x, y, z);

    float scale = 1.0f;
    float tx = x, ty = y;

    if (NearlyEqual(x, kVanillaAtkX) && NearlyEqual(y, kVanillaAtkY))
    {
        tx = kCustomAtkX; ty = kCustomAtkY;
        scale = kAtkTextScale;
    }
    else if (kShowName && NearlyEqual(x, kNameX0) && (NearlyEqual(y, kNameY0) || NearlyEqual(y, kNameY0Jp)))
    {
        // the name's top-left: NameX / NameY (the Japanese build draws it 6 px lower, kept)
        return orig_sub_140877EC0(out, kNameX, kNameY + (y - kNameY0), z);
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
    if (UseLargeAtkDefFont() && !HasAtkDefLabels())
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
        kHideLevelBadge = GetPrivateProfileIntA("Yu-Gi-Oh-AnimeCards", "HideLevelBadge", 1, ".\\Config.ini");
        kSpellTrapAttribute = GetPrivateProfileIntA("Yu-Gi-Oh-AnimeCards", "SpellTrapAttribute", 0, ".\\Config.ini") != 0;
        kStarsAlignRight = GetPrivateProfileIntA("Yu-Gi-Oh-AnimeCards", "StarsAlign", 0, ".\\Config.ini") == 1;
        kShowName = GetPrivateProfileIntA("Yu-Gi-Oh-AnimeCards", "ShowName", 0, ".\\Config.ini") != 0;
        kHideSpellTrapIcon = GetPrivateProfileIntA("Yu-Gi-Oh-AnimeCards", "HideSpellTrapIcon", 0, ".\\Config.ini") != 0;
        kFullCardArt = GetPrivateProfileIntA("Yu-Gi-Oh-AnimeCards", "FullArtIllustration", 0, ".\\Config.ini") != 0;
        for (auto [key, target] : { std::pair{ "NameX", &kNameX }, std::pair{ "NameY", &kNameY }, std::pair{ "NameWidth", &kNameWidth } })
        {
            GetPrivateProfileStringA("Yu-Gi-Oh-AnimeCards", key, "", cfgBuf, sizeof(cfgBuf), ".\\Config.ini");
            cfgEnd = nullptr;
            const float value = std::strtof(cfgBuf, &cfgEnd);
            if (cfgEnd != cfgBuf)
                *target = value;
        }
        GetPrivateProfileStringW(L"Yu-Gi-Oh-AnimeCards", L"AtkLabel", L"", g_AtkLabel, 16, L".\\Config.ini");
        GetPrivateProfileStringW(L"Yu-Gi-Oh-AnimeCards", L"DefLabel", L"", g_DefLabel, 16, L".\\Config.ini");
        orig_DuelCardView_UpdateLevelBadge = (fn_DuelCardView_UpdateLevelBadge)ResolveVA(0x1407C6980);
        kHideFieldStats = GetPrivateProfileIntA("Yu-Gi-Oh-AnimeCards", "HideFieldStats", 1, ".\\Config.ini");
        orig_DuelCardView_Refresh = (fn_DuelCardView_Refresh)ResolveVA(0x1407C6660);
        game_WidgetBase_SetEnabled = (fn_WidgetBase_SetEnabled)ResolveVA(0x14075A490);

        p_g_bIsJpVersion = (bool*)ResolveVA(YGO::Addr::g_bIsJpVersion);
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

        orig_sub_14081A670 = (fn_sub_14081A670)ResolveVA(0x14081A670);
        orig_sub_14081A730 = (fn_sub_14081A730)ResolveVA(0x14081A730);
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)orig_sub_140766540, Hook_sub_140766540);
        DetourAttach(&(PVOID&)orig_sub_140877EC0, Hook_sub_140877EC0);
        DetourAttach(&(PVOID&)orig_sub_1408795A0, Hook_sub_1408795A0);
        DetourAttach(&(PVOID&)orig_sub_14074E7D0, Hook_sub_14074E7D0);
        DetourAttach(&(PVOID&)orig_IconId_Attribute, Hook_IconId_Attribute);
        DetourAttach(&(PVOID&)orig_DuelCardView_UpdateLevelBadge, Hook_DuelCardView_UpdateLevelBadge);
        if (kHideFieldStats != 0)
            DetourAttach(&(PVOID&)orig_DuelCardView_Refresh, Hook_DuelCardView_Refresh);
        if (kHideLevelBadge != 0)
        {
            // Only attached when hiding: the hook never calls the original, so it needs none of its arguments.
            orig_DuelHand_DrawLevelNumber = (fn_DuelHand_DrawLevelNumber)ResolveVA(0x14078B0B0);
            DetourAttach(&(PVOID&)orig_DuelHand_DrawLevelNumber, Hook_DuelHand_DrawLevelNumber);
        }
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
