#include "../../Yu-Gi-Oh_Loader Plugins/Yu-Gi-Oh-AnimeCards/dllmain.cpp"
#include <cassert>
#include <thread>

static int quadCalls, textCalls;
static float quadX, quadY;
static const unsigned short* lastText;
static std::wstring copiedText;
static unsigned lastFont;
static int monsterQueries;
static int Monster(unsigned short) { ++monsterQueries; return 1; }
static int NotLink(unsigned short) { return 0; }
static int NoStars(unsigned short) { return 0; }
static unsigned Atk(unsigned short) { return 1850; }
static int Attribute(int value) { return value + 129; }
static void Quad(void*, float x, float y, float, float, float, float, float, float, int, int)
{ ++quadCalls; quadX = x; quadY = y; }
static __int64 Text(__int64, __int64, __int64, float, int, unsigned font,
    const unsigned short* text, int, int, int)
{ ++textCalls; lastText = text; copiedText = reinterpret_cast<const wchar_t*>(text); lastFont = font; return 123; }
static char NestedBuild(__int64, unsigned short id)
{
    assert(g_buildingFace && g_currentCardId == id);
    if (id == 7)
    {
        g_starCount = 4; g_starIndex = 2; g_starStep = 27;
        Hook_sub_14074E7D0(0, 8);
        assert(g_buildingFace && g_currentCardId == 7);
        assert(g_starCount == 4 && g_starIndex == 2 && g_starStep == 27);
    }
    return 1;
}

int main()
{
    orig_sub_1408795A0 = Quad; orig_sub_140766540 = Text;
    game_IsMonster = Monster; game_IsLink = NotLink;
    orig_IconId_Attribute = Attribute;
    static const unsigned short label[] = { 'M', 'e', 'n', 'u', 0 };
    // Shared render functions must preserve callers outside a face build.
    assert(Hook_sub_140766540(0, 0, 0, 10, 0, 35, label, 4, 0, 0) == 123);
    assert(textCalls == 1 && lastText == label && lastFont == 35);
    Hook_sub_140766540(0, 0, 0, 10, 0, 27, label, 4, 0, 0);
    assert(textCalls == 2 && lastText == label);
    Hook_sub_1408795A0(nullptr, 335, 27, 372, 64, 0, 0, 1, 1, 0, 0);
    assert(quadCalls == 1 && quadX == 335 && quadY == 27 && monsterQueries == 0);
    assert(Hook_IconId_Attribute(1) == 130);

    g_buildingFace = true; g_currentCardId = 7;
    kUseLargeAtkDefFont = false;
    orig_YGO_Get_EffectiveDefFromFullCardProps = Atk;
    Hook_sub_140766540(0, 0, 0, 10, 0, 35, label, 4, 0, 0);
    assert(textCalls == 3 && copiedText == L"1850");
    std::thread otherThread([] {
        assert(!g_buildingFace && g_currentCardId == 0xFFFF && g_starCount == 0);
    });
    otherThread.join();
    assert(Hook_IconId_Attribute(1) == 120);
    Hook_sub_140766540(0, 0, 0, 0, 0, 36, label, 4, 0, 0);
    assert(lastText == label && lastFont == 36);
    Hook_sub_140766540(0, 0, 0, 278, 0, 30, label, 4, 0, 0);
    assert(copiedText == L" ");
    kCardArtScale = 1; kCardArtOffsetX = 12; kCardArtOffsetY = -20;
    Hook_sub_1408795A0(nullptr, 48, 106, 352, 410, 0, 0, 1, 1, 0, 0);
    assert(quadX == 60 && quadY == 86);
    g_buildingFace = false; g_currentCardId = 0xFFFF;
    orig_sub_14081A670 = NoStars; orig_sub_14081A730 = NoStars;
    orig_sub_14074E7D0 = NestedBuild;
    assert(Hook_sub_14074E7D0(0, 7) == 1);
    assert(!g_buildingFace && g_currentCardId == 0xFFFF && g_starCount == 0);
    std::cout << "PASS shared text/quad pass-through, card-only attribute and ATK, thread isolation, Pendulum scales and text isolation, scale-one art offsets, nested build restoration\n";
}
