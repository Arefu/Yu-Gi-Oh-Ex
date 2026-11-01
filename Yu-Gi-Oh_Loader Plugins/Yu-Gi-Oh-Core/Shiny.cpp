// Shiny cards (docs/Shiny.md). The foil pass grew out of Toon Jinzo's Yu-Gi-Oh-Foil (thank you): the same way of telling which
// texture is which card and of drawing a card again with a foil shader over it.
#include "Shiny.h"

#include <Windows.h>
#include <d3d11.h>
#include <d3dcompiler.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <deque>
#include <format>
#include <map>
#include <mutex>
#include <random>
#include <string>
#include <unordered_map>
#include <vector>

#include "CardTable.h"
#include "Detours.h"
#include "Logger.h"
#include "Rarity.h"
#include "Save.h"
#include "ShinyShader.h"

#pragma comment(lib, "d3dcompiler.lib")
#pragma comment(lib, "dxguid.lib")

namespace
{
    // ---- the game (names in the IDB) ----
    uintptr_t orig_OpenPack = 0x14085BFC0;            // GAME::Store::OpenPackAndGrantCards(ScreenRandomPackStore*)
    uintptr_t orig_CardFaceBuild = 0x14074E7D0;       // char CardFace_Build(CardFace*, u16 cardId)   (loader thread)
    uintptr_t orig_AddQuad = 0x1408795A0;             // AddQuad(batch, x0, y0, x1, y1, u0, v0, u1, v1, int, colour)
    uintptr_t orig_CardFaceRender = 0x140754420;      // void CardFace_RenderToTarget(CardFace*)      (records the face's drawing)
    uintptr_t orig_CreateDevice = 0x14090D2B0;        // jmp [__imp_D3D11CreateDeviceAndSwapChain]
    constexpr uintptr_t kFullCardProps = 0x142927600; // FullCardProps[65536], 160 bytes, by Konami id
    constexpr size_t kPulledCards = 4448;             // ScreenRandomPackStore::PulledCards (std::vector<u16>), the rare slot last
    constexpr uint16_t kNoCard = 0xFFFF;
    constexpr UINT kFaceW = 400, kFaceH = 580;
    constexpr const char* kExtensionTag = "shiny";

    struct U16Vector { uint16_t* First; uint16_t* Last; uint16_t* End; };

    // FullCardProps fields (volatile: a constant game address must never be folded by the optimiser, see the Release-only crash note)
    template <class T>
    T Prop(uint16_t card, size_t offset)
    {
        return *reinterpret_cast<const volatile T*>(kFullCardProps + static_cast<size_t>(card) * 160 + offset);
    }
    bool IsMonster(uint16_t card) { return Prop<uint8_t>(card, 0x1C) != 0; }
    int Frame(uint16_t card) { return Prop<int32_t>(card, 0x94); }
    int StarsOf(uint16_t card)
    {
        if (!IsMonster(card))
            return 0;
        const int level = Prop<int32_t>(card, 0x60);
        return std::clamp(level > 0 ? level : Prop<int32_t>(card, 0x74), 0, 13);
    }
    std::string CardName(uint16_t card)
    {
        const wchar_t* name = Prop<const wchar_t*>(card, 0x08);
        if (!name)
            return std::to_string(card);
        char out[256] = {};
        WideCharToMultiByte(CP_UTF8, 0, name, -1, out, sizeof(out) - 1, nullptr, nullptr);
        return out;
    }

    // ---- settings ----
    bool g_On = false;
    int g_OddsScale = 100;
    float g_Strength = 1.0f, g_Speed = 1.0f;
    bool g_MouseTilt = true;
    std::string g_Preview;
    bool g_AnimeFrames = false;     // Yu-Gi-Oh-AnimeCards lays the face out its own way: no name / text box regions then
    HWND g_Window = nullptr;

    // ---- owned rarities (the save) ----
    std::recursive_mutex g_Lock;    // everything below
    std::unordered_map<uint16_t, std::map<uint8_t, uint16_t>> g_Owned;
    std::unordered_map<uint16_t, uint8_t> g_Shown;   // card -> rarity shown (highest tier owned)
    bool g_Loaded = false;

    void Recompute(uint16_t card)
    {
        auto it = g_Owned.find(card);
        int bestTier = -1; uint8_t best = 0;
        if (it != g_Owned.end())
            for (const auto& [rarity, count] : it->second)
                if (const Rarity::Def* d = Rarity::ById(rarity); d && count && d->Tier > bestTier)
                {
                    bestTier = d->Tier;
                    best = rarity;
                }
        if (best)
            g_Shown[card] = best;
        else
            g_Shown.erase(card);
    }

    void __cdecl OnTake(bool fromSave)
    {
        std::lock_guard<std::recursive_mutex> guard(g_Lock);
        g_Owned.clear();
        g_Shown.clear();
        g_Loaded = true;
        if (!fromSave)
            return;
        const int size = Save::GetExtension(kExtensionTag, nullptr, 0);
        if (size <= 1)
            return;
        std::vector<uint8_t> data(static_cast<size_t>(size));
        Save::GetExtension(kExtensionTag, data.data(), size);
        if (data[0] != 1)
        {
            Logger::WriteLog(std::format("Shiny: the save's rarities are version {}, this Core reads 1: left as they are", data[0]), MODULE_NAME, 1);
            g_Loaded = false;   // never written back over
            return;
        }
        int entries = 0;
        for (size_t i = 1; i + 5 <= data.size(); i += 5)
        {
            const uint16_t card = static_cast<uint16_t>(data[i] | data[i + 1] << 8);
            const uint8_t rarity = data[i + 2];
            const uint16_t count = static_cast<uint16_t>(data[i + 3] | data[i + 4] << 8);
            if (count)
            {
                g_Owned[card][rarity] = count;
                ++entries;
            }
        }
        for (const auto& [card, rarities] : g_Owned)
            Recompute(card);
        Logger::WriteLog(std::format("Shiny: {} card rarit{} owned ({} cards) from the save", entries, entries == 1 ? "y" : "ies", g_Owned.size()), MODULE_NAME, 0);
    }

    int __cdecl WriteFilter(unsigned char*, int, void*)
    {
        std::lock_guard<std::recursive_mutex> guard(g_Lock);
        if (!g_Loaded)
            return 0;
        std::vector<uint8_t> data;
        data.push_back(1);
        for (const auto& [card, rarities] : g_Owned)
            for (const auto& [rarity, count] : rarities)
                if (count)
                    data.insert(data.end(), { static_cast<uint8_t>(card & 0xFF), static_cast<uint8_t>(card >> 8), rarity,
                                              static_cast<uint8_t>(count & 0xFF), static_cast<uint8_t>(count >> 8) });
        if (data.size() == 1)
            Save::SetExtension(kExtensionTag, nullptr, 0);   // nothing owned: no entry
        else
            Save::SetExtension(kExtensionTag, data.data(), static_cast<int>(data.size()));
        return 0;
    }

    // ---- packs ----
    std::mt19937 g_Rng{ std::random_device{}() };
    std::vector<const Rarity::Def*> g_Rolls;          // rarities packs can roll, rarest first
    const Rarity::Def* g_Fallback = nullptr;          // the rare slot's "nothing rarer" (Rare)

    const Rarity::Def* Roll(bool rareSlot)
    {
        for (const Rarity::Def* d : g_Rolls)
        {
            if (!rareSlot && d->RollSlot != Rarity::Slot::Any)
                continue;
            const int odds = (std::max)(1, static_cast<int>(static_cast<long long>(d->Odds) * g_OddsScale / 100));
            if (std::uniform_int_distribution<int>(0, odds - 1)(g_Rng) == 0)
                return d;
        }
        return rareSlot ? g_Fallback : nullptr;
    }

    void __fastcall Hook_OpenPack(void* screen)
    {
        reinterpret_cast<void(__fastcall*)(void*)>(orig_OpenPack)(screen);
        const auto& pulled = *reinterpret_cast<const U16Vector*>(static_cast<uint8_t*>(screen) + kPulledCards);
        if (!pulled.First || pulled.Last <= pulled.First)
            return;
        const size_t count = static_cast<size_t>(pulled.Last - pulled.First);
        std::lock_guard<std::recursive_mutex> guard(g_Lock);
        for (size_t i = 0; i < count; ++i)
        {
            const uint16_t card = pulled.First[i];
            const Rarity::Def* d = Roll(i + 1 == count);   // the rare slot is pushed last
            if (!d)
                continue;
            uint16_t& owned = g_Owned[card][d->Id];
            if (owned < 0xFFFF)
                ++owned;
            Recompute(card);
            if (d->Tier >= 20)
                Logger::WriteLog(std::format("Shiny pull: {} ({}) as {}", CardName(card), card, d->Name), MODULE_NAME, 0);
        }
    }

    // ---- where things are on each face ----
    struct Rect { float X0 = 0, Y0 = 0, X1 = 0, Y1 = 0; float W() const { return X1 - X0; } float H() const { return Y1 - Y0; } };
    struct Layout
    {
        Rect Art, Attr, Name, Text;
        float Stars[4] = {};      // first star x0, y0, size, step
        float StarCount = 0;
        float NameWhite = 0;
    };
    std::unordered_map<uint16_t, Layout> g_Layout;    // under g_Lock

    struct BuildContext
    {
        bool Active = false;
        uint16_t Card = kNoCard;
        std::vector<Rect> Squares;
        Rect Art;
        float ArtArea = 0;
    };
    thread_local BuildContext t_Build;

    void __fastcall Hook_AddQuad(void* batch, float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, int a10, int colour)
    {
        if (t_Build.Active)
        {
            const Rect r{ (std::min)(x0, x1), (std::min)(y0, y1), (std::max)(x0, x1), (std::max)(y0, y1) };
            const float area = r.W() * r.H();
            if (area > 150.0f * 150.0f && area < 0.9f * kFaceW * kFaceH && area > t_Build.ArtArea)
            {
                t_Build.Art = r;              // the art is the biggest quad that isn't the frame
                t_Build.ArtArea = area;
            }
            else if (std::fabs(r.W() - r.H()) < 2.0f && r.W() > 12.0f && r.W() < 60.0f)
                t_Build.Squares.push_back(r); // icons: attribute, property, stars (and square-ish glyphs, sorted out below)
        }
        reinterpret_cast<void(__fastcall*)(void*, float, float, float, float, float, float, float, float, int, int)>(orig_AddQuad)(
            batch, x0, y0, x1, y1, u0, v0, u1, v1, a10, colour);
    }

    Layout MakeLayout(const BuildContext& b)
    {
        Layout l;
        const uint16_t card = b.Card;
        const int frame = Frame(card);
        const bool pendulum = frame >= 13 && frame <= 17;
        l.Art = b.Art;
        if (!g_AnimeFrames)
        {
            l.Name = { 28, 26, 336, 68 };
            l.Text = pendulum ? Rect{ 26, 458, 374, 556 } : Rect{ 26, 428, 374, 556 };
            if (l.Art.Y1 > l.Text.Y0)
                l.Art.Y1 = l.Text.Y0;     // pendulum art runs behind the text box: no art foil on the text
        }
        l.NameWhite = (frame == 7 || frame == 8 || frame == 12 || frame == 15 || frame == 18) ? 1.0f : 0.0f;

        // the stars: the last run of `n` squares of one size on one line, evenly spaced (glyphs never are)
        const int n = StarsOf(card);
        std::vector<bool> used(b.Squares.size(), false);
        if (n > 0 && static_cast<int>(b.Squares.size()) >= n)
            for (int start = static_cast<int>(b.Squares.size()) - n; start >= 0; --start)
            {
                const Rect& a = b.Squares[start];
                const float step = n > 1 ? b.Squares[start + 1].X0 - a.X0 : a.W();
                bool run = std::fabs(step) >= a.W() * 0.6f;
                for (int k = 1; run && k < n; ++k)
                {
                    const Rect& s = b.Squares[start + k];
                    run = std::fabs(s.W() - a.W()) < 0.5f && std::fabs(s.Y0 - a.Y0) < 0.5f && std::fabs((s.X0 - a.X0) - k * step) < 0.75f;
                }
                if (!run)
                    continue;
                float minX = a.X0;
                for (int k = 0; k < n; ++k)
                {
                    minX = (std::min)(minX, b.Squares[start + k].X0);
                    used[start + k] = true;
                }
                l.Stars[0] = minX; l.Stars[1] = a.Y0; l.Stars[2] = a.W(); l.Stars[3] = std::fabs(step);
                l.StarCount = static_cast<float>(n);
                break;
            }
        // the attribute: the biggest square left (37 px on the game's frame, 48 on Anime Cards')
        float best = 27.0f;
        for (size_t i = 0; i < b.Squares.size(); ++i)
            if (!used[i] && b.Squares[i].W() > best)
            {
                best = b.Squares[i].W();
                l.Attr = b.Squares[i];
            }
        return l;
    }

    // ---- what each step saw (the summary in the log every 15 s, while something changes) ----
    struct Stats
    {
        std::atomic<uint32_t> Built{}, Recorded{}, FaceClears{}, GreyClears{}, Matched{}, NoCardWaiting{}, Slot0{}, Draws{}, NoRarity{}, BadVs{}, NoRtv{}, Foil{};
        std::atomic<uint32_t> OtherColour{};   // the last non-grey clear of a 400 x 580 target, as 0xRRGGBB
    } g_Stats;

    void LogStats()
    {
        static ULONGLONG next = 0;
        static uint32_t lastSum = 0;
        const ULONGLONG now = GetTickCount64();
        if (now < next)
            return;
        next = now + 15000;
        const uint32_t sum = g_Stats.Built + g_Stats.Recorded + g_Stats.FaceClears + g_Stats.Slot0 + g_Stats.Draws + g_Stats.Foil;
        if (sum == lastSum)
            return;
        lastSum = sum;
        Logger::WriteLog(std::format("Shiny: {} faces built, {} drawn; 400x580 clears {} ({} grey: {} matched a card, {} with none waiting; last other colour {:06X}); "
            "draws with a card face {} -> {} with a rarity, {} vertex shader not usable, {} no target, {} foil passes",
            g_Stats.Built.load(), g_Stats.Recorded.load(), g_Stats.FaceClears.load(), g_Stats.GreyClears.load(), g_Stats.Matched.load(), g_Stats.NoCardWaiting.load(),
            g_Stats.OtherColour.load(), g_Stats.Draws.load(), g_Stats.Draws - g_Stats.NoRarity, g_Stats.BadVs.load(), g_Stats.NoRtv.load(), g_Stats.Foil.load()), MODULE_NAME, 0);
    }

    // ---- face texture <-> card ----
    std::unordered_map<void*, uint16_t> g_FaceCard;   // CardFace* -> card (Build -> RenderToTarget)
    std::deque<uint16_t> g_PendingFaces;              // cards whose drawing was recorded, waiting for their clear to run
    std::unordered_map<void*, uint16_t> g_TexCard;    // face texture -> card

    char __fastcall Hook_CardFaceBuild(void* face, uint16_t card)
    {
        BuildContext outer = std::move(t_Build);   // a build inside a build keeps its own
        t_Build = {};
        t_Build.Active = true;
        t_Build.Card = card;
        const char result = reinterpret_cast<char(__fastcall*)(void*, uint16_t)>(orig_CardFaceBuild)(face, card);
        Layout layout = MakeLayout(t_Build);
        t_Build = std::move(outer);
        ++g_Stats.Built;
        std::lock_guard<std::recursive_mutex> guard(g_Lock);
        g_Layout[card] = layout;
        g_FaceCard[face] = card;
        return result;
    }

    void __fastcall Hook_CardFaceRender(void* face)
    {
        {
            std::lock_guard<std::recursive_mutex> guard(g_Lock);
            uint16_t card = kNoCard;
            if (auto it = g_FaceCard.find(face); it != g_FaceCard.end())
            {
                card = it->second;
                g_FaceCard.erase(it);
            }
            g_PendingFaces.push_back(card);
            if (g_PendingFaces.size() > 64)
                g_PendingFaces.pop_front();   // never grows without bound if clears are missed
        }
        ++g_Stats.Recorded;
        reinterpret_cast<void(__fastcall*)(void*)>(orig_CardFaceRender)(face);
    }

    // ---- Direct3D ----
    using CreateVS_t = HRESULT(STDMETHODCALLTYPE*)(ID3D11Device*, const void*, SIZE_T, ID3D11ClassLinkage*, ID3D11VertexShader**);
    using PSSetSRV_t = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT, ID3D11ShaderResourceView* const*);
    using ClearRTV_t = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, ID3D11RenderTargetView*, const FLOAT[4]);
    using Draw_t = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT);
    using DrawIdx_t = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT, INT);
    using DrawInst_t = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT, UINT, UINT);
    using DrawIdxInst_t = void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*, UINT, UINT, UINT, INT, UINT);
    CreateVS_t o_CreateVS = nullptr; PSSetSRV_t o_PSSetSRV = nullptr; ClearRTV_t o_ClearRTV = nullptr;
    Draw_t o_Draw = nullptr; DrawIdx_t o_DrawIdx = nullptr; DrawInst_t o_DrawInst = nullptr; DrawIdxInst_t o_DrawIdxInst = nullptr;

    ID3D11Device* g_Device = nullptr;
    ID3D11PixelShader* g_PS = nullptr;
    ID3D11Buffer* g_CB = nullptr;
    ID3D11BlendState* g_Blend = nullptr;
    std::unordered_map<ID3D11DepthStencilState*, ID3D11DepthStencilState*> g_DepthFor;   // the game's state -> the same, LESS_EQUAL, no writes
    ID3D11DepthStencilState* g_DepthForNull = nullptr;
    std::unordered_map<void*, bool> g_VsOk;                       // vertex shader -> outputs SV_POSITION, COLOR0, TEXCOORD0 (render thread)
    std::unordered_map<void*, std::vector<uint8_t>> g_VsCode;     // under g_Lock
    LARGE_INTEGER g_Qpf{}, g_Qpc0{};
    uint16_t g_Slot0Card = kNoCard;                               // the card whose face is in pixel shader slot 0 now (render thread)

    // matches cbuffer Shiny in ShinyShader.h (11 x 16 bytes)
    struct ShinyCB
    {
        float Time, Strength, Phase, Speed;
        float Tilt, StarCount, NameWhite, Desat;
        float Mouse[2], Screen[2];
        float Art[4], Stars[4], Attr[4], Name[4], Text[4];
        int32_t NameInk, ArtFoil, FrameFoil, FrameMask;
        int32_t Coating, Watermark, Pad0, Pad1;
        float NameColour[3], CoatStrength;
    };
    static_assert(sizeof(ShinyCB) == 11 * 16, "ShinyCB must match cbuffer Shiny");

    bool CreateObjects(ID3D11Device* dev)
    {
        ID3DBlob* code = nullptr; ID3DBlob* err = nullptr;
        HRESULT hr = D3DCompile(kShinyHlsl, strlen(kShinyHlsl), "ShinyShader", nullptr, nullptr, "main", "ps_4_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &code, &err);
        if (FAILED(hr))
        {
            Logger::WriteLog(std::format("Shiny: the foil shader did not compile: {}", err ? std::string((const char*)err->GetBufferPointer(), err->GetBufferSize()) : "?"), MODULE_NAME, 2);
            if (err) err->Release();
            return false;
        }
        if (err) err->Release();
        hr = dev->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &g_PS);
        code->Release();
        if (FAILED(hr))
            return false;
        D3D11_BUFFER_DESC bd{};
        bd.ByteWidth = sizeof(ShinyCB);
        bd.Usage = D3D11_USAGE_DYNAMIC;
        bd.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        bd.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        if (FAILED(dev->CreateBuffer(&bd, nullptr, &g_CB)))
            return false;
        D3D11_BLEND_DESC bl{};
        auto& rt = bl.RenderTarget[0];
        rt.BlendEnable = TRUE;
        rt.SrcBlend = D3D11_BLEND_ONE; rt.DestBlend = D3D11_BLEND_INV_SRC_ALPHA; rt.BlendOp = D3D11_BLEND_OP_ADD;
        rt.SrcBlendAlpha = D3D11_BLEND_ZERO; rt.DestBlendAlpha = D3D11_BLEND_ONE; rt.BlendOpAlpha = D3D11_BLEND_OP_ADD;
        rt.RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
        return SUCCEEDED(dev->CreateBlendState(&bl, &g_Blend));
    }

    ID3D11DepthStencilState* DepthState(ID3D11DepthStencilState* game)
    {
        if (!game && g_DepthForNull)
            return g_DepthForNull;
        if (game)
            if (auto it = g_DepthFor.find(game); it != g_DepthFor.end())
                return it->second;
        D3D11_DEPTH_STENCIL_DESC d{};
        if (game)
            game->GetDesc(&d);
        else
        {
            d.DepthEnable = TRUE; d.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ALL; d.DepthFunc = D3D11_COMPARISON_LESS;
        }
        d.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ZERO;     // the second pass sits exactly on the first
        if (d.DepthFunc == D3D11_COMPARISON_LESS) d.DepthFunc = D3D11_COMPARISON_LESS_EQUAL;
        if (d.DepthFunc == D3D11_COMPARISON_GREATER) d.DepthFunc = D3D11_COMPARISON_GREATER_EQUAL;
        d.StencilWriteMask = 0;
        ID3D11DepthStencilState* s = nullptr;
        g_Device->CreateDepthStencilState(&d, &s);
        if (game)
        {
            game->AddRef();
            g_DepthFor[game] = s;
        }
        else
            g_DepthForNull = s;
        return s;
    }

    bool VertexShaderOk(ID3D11VertexShader* vs)
    {
        if (auto it = g_VsOk.find(vs); it != g_VsOk.end())
            return it->second;
        std::vector<uint8_t> code;
        {
            std::lock_guard<std::recursive_mutex> guard(g_Lock);
            if (auto c = g_VsCode.find(vs); c != g_VsCode.end())
                code = c->second;
        }
        bool ok = false;
        ID3D11ShaderReflection* r = nullptr;
        if (!code.empty() && SUCCEEDED(D3DReflect(code.data(), code.size(), IID_ID3D11ShaderReflection, (void**)&r)) && r)
        {
            D3D11_SHADER_DESC d{};
            r->GetDesc(&d);
            bool colour = false, uv = false;
            for (UINT i = 0; i < d.OutputParameters; i++)
            {
                D3D11_SIGNATURE_PARAMETER_DESC p{};
                r->GetOutputParameterDesc(i, &p);
                if (!_stricmp(p.SemanticName, "COLOR") && p.SemanticIndex == 0 && p.Register == 1) colour = true;
                if (!_stricmp(p.SemanticName, "TEXCOORD") && p.SemanticIndex == 0 && p.Register == 2 && (p.Mask & 3) == 3) uv = true;
            }
            ok = colour && uv && d.OutputParameters == 3;
            r->Release();
        }
        g_VsOk[vs] = ok;
        return ok;
    }

    HRESULT STDMETHODCALLTYPE H_CreateVS(ID3D11Device* s, const void* bc, SIZE_T n, ID3D11ClassLinkage* cl, ID3D11VertexShader** pp)
    {
        const HRESULT hr = o_CreateVS(s, bc, n, cl, pp);
        if (SUCCEEDED(hr) && pp && *pp)
        {
            std::lock_guard<std::recursive_mutex> guard(g_Lock);
            g_VsCode[*pp].assign(static_cast<const uint8_t*>(bc), static_cast<const uint8_t*>(bc) + n);
        }
        return hr;
    }

    // The clear of a 400 x 580 target to grey: that target is the next recorded face's texture from now on. The game records faces on
    // a deferred context (its card renderer's own, CreateDeferredContext at 0x1408C7B90), whose methods are not the immediate context's,
    // so both clears are hooked; on the deferred one it runs inside CardFace_RenderToTarget, right after that face was queued.
    void NoteClear(ID3D11RenderTargetView* rtv, const FLOAT col[4])
    {
        const bool grey = col && std::fabs(col[0] - 0.502f) < 0.01f && std::fabs(col[1] - 0.502f) < 0.01f && std::fabs(col[2] - 0.502f) < 0.01f;
        if (rtv && col)
        {
            ID3D11Resource* res = nullptr;
            rtv->GetResource(&res);
            D3D11_TEXTURE2D_DESC td{};
            if (res)
                static_cast<ID3D11Texture2D*>(res)->GetDesc(&td);
            if (res && td.Width == kFaceW && td.Height == kFaceH)
            {
                ++g_Stats.FaceClears;
                if (!grey)
                    g_Stats.OtherColour = (static_cast<uint32_t>(std::clamp(col[0], 0.0f, 1.0f) * 255) << 16) |
                                          (static_cast<uint32_t>(std::clamp(col[1], 0.0f, 1.0f) * 255) << 8) | static_cast<uint32_t>(std::clamp(col[2], 0.0f, 1.0f) * 255);
            }
            if (res && grey)
            {
                if (td.Width == kFaceW && td.Height == kFaceH)
                {
                    ++g_Stats.GreyClears;
                    std::lock_guard<std::recursive_mutex> guard(g_Lock);
                    uint16_t card = kNoCard;
                    if (!g_PendingFaces.empty())
                    {
                        card = g_PendingFaces.front();
                        g_PendingFaces.pop_front();
                    }
                    if (card != kNoCard)
                    {
                        ++g_Stats.Matched;
                        g_TexCard[res] = card;
                    }
                    else
                    {
                        ++g_Stats.NoCardWaiting;
                        g_TexCard.erase(res);
                    }
                }
            }
            if (res)
                res->Release();
        }
    }

    ClearRTV_t o_ClearRTVDeferred = nullptr;
    void STDMETHODCALLTYPE H_ClearRTV(ID3D11DeviceContext* c, ID3D11RenderTargetView* rtv, const FLOAT col[4])
    { NoteClear(rtv, col); o_ClearRTV(c, rtv, col); }
    void STDMETHODCALLTYPE H_ClearRTVDeferred(ID3D11DeviceContext* c, ID3D11RenderTargetView* rtv, const FLOAT col[4])
    { NoteClear(rtv, col); o_ClearRTVDeferred(c, rtv, col); }

    void STDMETHODCALLTYPE H_PSSetSRV(ID3D11DeviceContext* c, UINT start, UINT n, ID3D11ShaderResourceView* const* srv)
    {
        o_PSSetSRV(c, start, n, srv);
        if (start != 0 || n == 0)
            return;
        g_Slot0Card = kNoCard;
        if (!srv || !srv[0])
            return;
        ID3D11Resource* res = nullptr;
        srv[0]->GetResource(&res);
        if (!res)
            return;
        {
            std::lock_guard<std::recursive_mutex> guard(g_Lock);
            if (auto it = g_TexCard.find(res); it != g_TexCard.end())
                g_Slot0Card = it->second;
        }
        res->Release();
    }

    const Rarity::Def* PreviewRarity(float seconds)
    {
        if (g_Preview.empty())
            return nullptr;
        if (_stricmp(g_Preview.c_str(), "cycle") == 0)
        {
            const auto& all = Rarity::All();
            return all.empty() ? nullptr : &all[static_cast<size_t>(seconds / 3.0f) % all.size()];
        }
        return Rarity::ByKey(g_Preview);
    }

    template <class F>
    void WithFoil(ID3D11DeviceContext* c, F&& drawAgain)
    {
        LogStats();
        const uint16_t card = g_Slot0Card;
        if (card == kNoCard || !g_PS)
            return;
        ++g_Stats.Draws;

        LARGE_INTEGER now;
        QueryPerformanceCounter(&now);
        const float seconds = static_cast<float>(static_cast<double>(now.QuadPart - g_Qpc0.QuadPart) / static_cast<double>(g_Qpf.QuadPart));

        const Rarity::Def* d = PreviewRarity(seconds);
        Layout l;
        {
            std::lock_guard<std::recursive_mutex> guard(g_Lock);
            if (!d)
            {
                auto it = g_Shown.find(card);
                if (it == g_Shown.end())
                {
                    ++g_Stats.NoRarity;
                    return;
                }
                d = Rarity::ById(it->second);
            }
            if (auto it = g_Layout.find(card); it != g_Layout.end())
                l = it->second;
        }
        if (!d || (!d->NameInk && !d->ArtFoil && !d->FrameFoil && !d->Coating && !d->Desaturate && !d->Watermark))
        {
            ++g_Stats.NoRarity;
            return;
        }

        ID3D11VertexShader* vs = nullptr;
        c->VSGetShader(&vs, nullptr, nullptr);
        const bool ok = vs && VertexShaderOk(vs);
        if (vs) vs->Release();
        if (!ok)
        {
            ++g_Stats.BadVs;
            return;
        }
        ID3D11RenderTargetView* rtv = nullptr;
        c->OMGetRenderTargets(1, &rtv, nullptr);
        if (!rtv)
        {
            ++g_Stats.NoRtv;
            return;   // nothing to draw into (a depth-only pass)
        }

        ShinyCB cb{};
        cb.Time = seconds; cb.Strength = g_Strength; cb.Speed = g_Speed;
        cb.Phase = std::fmod(card * 0.618034f, 1.0f);
        cb.StarCount = (d->Regions & Rarity::RegStars) ? l.StarCount : 0.0f;
        cb.NameWhite = l.NameWhite; cb.Desat = d->Desaturate;
        auto put = [](float* out, const Rect& r) { out[0] = r.X0; out[1] = r.Y0; out[2] = r.X1; out[3] = r.Y1; };
        put(cb.Art, l.Art); put(cb.Attr, l.Attr); put(cb.Name, l.Name); put(cb.Text, l.Text);
        memcpy(cb.Stars, l.Stars, sizeof(cb.Stars));
        cb.NameInk = d->NameInk; cb.ArtFoil = d->ArtFoil; cb.FrameFoil = d->FrameFoil; cb.FrameMask = d->Regions;
        cb.Coating = d->Coating; cb.Watermark = d->Watermark; cb.CoatStrength = d->CoatStrength;
        memcpy(cb.NameColour, d->NameColour, sizeof(cb.NameColour));
        {
            ID3D11Resource* r = nullptr;
            rtv->GetResource(&r);
            D3D11_TEXTURE2D_DESC td{};
            if (r) { static_cast<ID3D11Texture2D*>(r)->GetDesc(&td); r->Release(); }
            cb.Screen[0] = static_cast<float>(td.Width); cb.Screen[1] = static_cast<float>(td.Height);
            POINT pt{}; RECT rc{};
            if (g_MouseTilt && g_Window && GetCursorPos(&pt) && ScreenToClient(g_Window, &pt) && GetClientRect(g_Window, &rc) && rc.right > 0 && rc.bottom > 0)
            {
                cb.Mouse[0] = pt.x * cb.Screen[0] / rc.right;    // client pixels -> render-target pixels
                cb.Mouse[1] = pt.y * cb.Screen[1] / rc.bottom;
                cb.Tilt = 1.0f;
            }
        }
        rtv->Release();
        D3D11_MAPPED_SUBRESOURCE m{};
        if (FAILED(c->Map(g_CB, 0, D3D11_MAP_WRITE_DISCARD, 0, &m)))
            return;
        memcpy(m.pData, &cb, sizeof(cb));
        c->Unmap(g_CB, 0);

        // what the pass changes, put back afterwards
        ID3D11PixelShader* ps = nullptr; c->PSGetShader(&ps, nullptr, nullptr);
        ID3D11Buffer* cb0 = nullptr; c->PSGetConstantBuffers(0, 1, &cb0);
        ID3D11BlendState* bs = nullptr; FLOAT bf[4]; UINT sm = 0; c->OMGetBlendState(&bs, bf, &sm);
        ID3D11DepthStencilState* ds = nullptr; UINT sref = 0; c->OMGetDepthStencilState(&ds, &sref);

        c->PSSetShader(g_PS, nullptr, 0);
        c->PSSetConstantBuffers(0, 1, &g_CB);
        const FLOAT zero[4] = {};
        c->OMSetBlendState(g_Blend, zero, 0xFFFFFFFF);
        c->OMSetDepthStencilState(DepthState(ds), sref);
        drawAgain();
        ++g_Stats.Foil;

        c->PSSetShader(ps, nullptr, 0);
        c->PSSetConstantBuffers(0, 1, &cb0);
        c->OMSetBlendState(bs, bf, sm);
        c->OMSetDepthStencilState(ds, sref);
        if (ps) ps->Release();
        if (cb0) cb0->Release();
        if (bs) bs->Release();
        if (ds) ds->Release();
    }

    void STDMETHODCALLTYPE H_Draw(ID3D11DeviceContext* c, UINT n, UINT sv)
    { o_Draw(c, n, sv); WithFoil(c, [&] { o_Draw(c, n, sv); }); }
    void STDMETHODCALLTYPE H_DrawIdx(ID3D11DeviceContext* c, UINT n, UINT si, INT bv)
    { o_DrawIdx(c, n, si, bv); WithFoil(c, [&] { o_DrawIdx(c, n, si, bv); }); }
    void STDMETHODCALLTYPE H_DrawInst(ID3D11DeviceContext* c, UINT n, UINT inst, UINT sv, UINT sinst)
    { o_DrawInst(c, n, inst, sv, sinst); WithFoil(c, [&] { o_DrawInst(c, n, inst, sv, sinst); }); }
    void STDMETHODCALLTYPE H_DrawIdxInst(ID3D11DeviceContext* c, UINT n, UINT inst, UINT si, INT bv, UINT sinst)
    { o_DrawIdxInst(c, n, inst, si, bv, sinst); WithFoil(c, [&] { o_DrawIdxInst(c, n, inst, si, bv, sinst); }); }

    void InstallD3DHooks(ID3D11Device* dev, ID3D11DeviceContext* ctx)
    {
        g_Device = dev;
        if (!CreateObjects(dev))
        {
            Logger::WriteLog("Shiny: the foil could not be set up: rarities are kept but not drawn", MODULE_NAME, 2);
            return;
        }
        void** dv = *reinterpret_cast<void***>(dev);
        void** cv = *reinterpret_cast<void***>(ctx);
        o_CreateVS = reinterpret_cast<CreateVS_t>(dv[12]);
        o_PSSetSRV = reinterpret_cast<PSSetSRV_t>(cv[8]);
        o_DrawIdx = reinterpret_cast<DrawIdx_t>(cv[12]);
        o_Draw = reinterpret_cast<Draw_t>(cv[13]);
        o_DrawIdxInst = reinterpret_cast<DrawIdxInst_t>(cv[20]);
        o_DrawInst = reinterpret_cast<DrawInst_t>(cv[21]);
        o_ClearRTV = reinterpret_cast<ClearRTV_t>(cv[50]);
        {
            ID3D11DeviceContext* deferred = nullptr;   // only to find the deferred context's ClearRenderTargetView
            if (SUCCEEDED(dev->CreateDeferredContext(0, &deferred)) && deferred)
            {
                void* clear = (*reinterpret_cast<void***>(deferred))[50];
                if (clear != cv[50])
                    o_ClearRTVDeferred = reinterpret_cast<ClearRTV_t>(clear);
                deferred->Release();
            }
        }
        DetourTransactionBegin();
        DetourUpdateThread(GetCurrentThread());
        DetourAttach(&(PVOID&)o_CreateVS, H_CreateVS);
        DetourAttach(&(PVOID&)o_PSSetSRV, H_PSSetSRV);
        DetourAttach(&(PVOID&)o_ClearRTV, H_ClearRTV);
        if (o_ClearRTVDeferred)
            DetourAttach(&(PVOID&)o_ClearRTVDeferred, H_ClearRTVDeferred);
        DetourAttach(&(PVOID&)o_Draw, H_Draw);
        DetourAttach(&(PVOID&)o_DrawIdx, H_DrawIdx);
        DetourAttach(&(PVOID&)o_DrawInst, H_DrawInst);
        DetourAttach(&(PVOID&)o_DrawIdxInst, H_DrawIdxInst);
        const LONG err = DetourTransactionCommit();
        Logger::WriteLog(std::format("Shiny: Direct3D hooks {} (deferred context clear {})", err, o_ClearRTVDeferred ? "hooked" : "not found"), MODULE_NAME, err == NO_ERROR ? 0 : 2);
    }

    using CreateDev_t = HRESULT(WINAPI*)(IDXGIAdapter*, D3D_DRIVER_TYPE, HMODULE, UINT, const D3D_FEATURE_LEVEL*, UINT, UINT,
        const DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**, ID3D11Device**, D3D_FEATURE_LEVEL*, ID3D11DeviceContext**);

    HRESULT WINAPI Hook_CreateDevice(IDXGIAdapter* a, D3D_DRIVER_TYPE dt, HMODULE sw, UINT fl, const D3D_FEATURE_LEVEL* fls, UINT nfl, UINT sdk,
        const DXGI_SWAP_CHAIN_DESC* scd, IDXGISwapChain** sc, ID3D11Device** dev, D3D_FEATURE_LEVEL* got, ID3D11DeviceContext** ctx)
    {
        const HRESULT hr = reinterpret_cast<CreateDev_t>(orig_CreateDevice)(a, dt, sw, fl, fls, nfl, sdk, scd, sc, dev, got, ctx);
        // D3D11CreateDevice comes through here too (no swap chain: Media Foundation's video decoder for Yu-Gi-Oh-Media's mp4s): only the
        // game's first device with a swap chain is the one cards are drawn with.
        static bool once = false;
        if (SUCCEEDED(hr) && !once && sc && *sc && dev && *dev && ctx && *ctx)
        {
            once = true;
            g_Window = scd ? scd->OutputWindow : nullptr;
            InstallD3DHooks(*dev, *ctx);
        }
        return hr;
    }

    std::string IniText(const std::string& ini, const char* key, const char* fallback)
    {
        char buffer[128] = {};
        GetPrivateProfileStringA("Yu-Gi-Oh-Core", key, fallback, buffer, sizeof(buffer), ini.c_str());
        return buffer;
    }

    float IniFloat(const std::string& ini, const char* key, float fallback)
    {
        const std::string text = IniText(ini, key, "");
        char* end = nullptr;
        const float value = strtof(text.c_str(), &end);
        return end != text.c_str() ? value : fallback;
    }
}

int Shiny::ShownRarity(uint16_t card)
{
    std::lock_guard<std::recursive_mutex> guard(g_Lock);
    auto it = g_Shown.find(card);
    return it == g_Shown.end() ? 0 : it->second;
}

int Shiny::Count(uint16_t card, uint8_t rarity)
{
    std::lock_guard<std::recursive_mutex> guard(g_Lock);
    auto it = g_Owned.find(card);
    if (it == g_Owned.end())
        return 0;
    auto r = it->second.find(rarity);
    return r == it->second.end() ? 0 : r->second;
}

int Shiny::Add(uint16_t card, uint8_t rarity, int count)
{
    if (!rarity || !Rarity::ById(rarity))
        return 0;
    std::lock_guard<std::recursive_mutex> guard(g_Lock);
    auto& owned = g_Owned[card];
    const int value = std::clamp(static_cast<int>(owned[rarity]) + count, 0, 0xFFFF);
    if (value)
        owned[rarity] = static_cast<uint16_t>(value);
    else
        owned.erase(rarity);
    if (owned.empty())
        g_Owned.erase(card);
    Recompute(card);
    return value;
}

void Shiny::Install()
{
    // The saved rarities are always read and written back, so they survive Shiny being off (and the exports work either way).
    CardTable::AddTakeListener(&OnTake);
    Save::AddWriteFilter(&WriteFilter, nullptr);

    const std::string ini = Save::GameFolder() + "Config.ini";
    g_On = GetPrivateProfileIntA("Yu-Gi-Oh-Core", "Shiny", 0, ini.c_str()) != 0;
    if (!g_On)
    {
        Logger::WriteLog("Shiny cards: off (Shiny=0)", MODULE_NAME, 0);
        return;
    }
    g_OddsScale = std::clamp(static_cast<int>(GetPrivateProfileIntA("Yu-Gi-Oh-Core", "ShinyOddsScale", 100, ini.c_str())), 1, 10000);
    g_Strength = std::clamp(IniFloat(ini, "ShinyStrength", 1.0f), 0.0f, 2.0f);
    g_Speed = std::clamp(IniFloat(ini, "ShinySpeed", 1.0f), 0.0f, 10.0f);
    g_MouseTilt = GetPrivateProfileIntA("Yu-Gi-Oh-Core", "ShinyMouseTilt", 1, ini.c_str()) != 0;
    g_Preview = IniText(ini, "ShinyPreview", "");
    g_AnimeFrames = GetModuleHandleA("Yu-Gi-Oh-AnimeCards.dll") != nullptr ||
                    GetPrivateProfileIntA("Yu-Gi-Oh-Core", "Yu-Gi-Oh-AnimeCards", 0, ini.c_str()) == 1;   // it may load after Core
    QueryPerformanceFrequency(&g_Qpf);
    QueryPerformanceCounter(&g_Qpc0);

    for (const Rarity::Def& d : Rarity::All())
    {
        if (d.Odds > 0 && d.RollSlot != Rarity::Slot::None)
            g_Rolls.push_back(&d);
        if (d.Fallback)
            g_Fallback = &d;
    }
    std::sort(g_Rolls.begin(), g_Rolls.end(), [](const Rarity::Def* a, const Rarity::Def* b) { return a->Odds > b->Odds; });
    if (!g_Preview.empty() && _stricmp(g_Preview.c_str(), "cycle") != 0 && !Rarity::ByKey(g_Preview))
        Logger::WriteLog(std::format("Shiny: ShinyPreview={} is not a rarity", g_Preview), MODULE_NAME, 1);

    DetourTransactionBegin();
    DetourUpdateThread(GetCurrentThread());
    DetourAttach(&(PVOID&)orig_OpenPack, Hook_OpenPack);
    DetourAttach(&(PVOID&)orig_CardFaceBuild, Hook_CardFaceBuild);
    DetourAttach(&(PVOID&)orig_AddQuad, Hook_AddQuad);
    DetourAttach(&(PVOID&)orig_CardFaceRender, Hook_CardFaceRender);
    DetourAttach(&(PVOID&)orig_CreateDevice, Hook_CreateDevice);
    const LONG err = DetourTransactionCommit();
    Logger::WriteLog(std::format("Shiny cards: {} rarities, {} rolled by packs (odds x{}%), preview '{}', game hooks {}", Rarity::All().size(), g_Rolls.size(),
        g_OddsScale, g_Preview, err), MODULE_NAME, err == NO_ERROR ? 0 : 2);
}
