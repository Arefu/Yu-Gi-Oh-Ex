#pragma once

// The pixel shader Shiny.cpp draws over a card face that has a rarity (compiled at start-up with D3DCompile, ps_4_0).
//
// It runs with the game's own vertex shader and inputs (SV_POSITION, COLOR0, TEXCOORD0 = the face's 0..1 UV), so TEXCOORD0 * (400, 580)
// is the face pixel, and the face itself is still bound in slot t0 / s0. A rarity is a stack of layers (Rarity.h), drawn bottom to top and
// returned premultiplied for ONE / INV_SRC_ALPHA blending over the face the game just drew:
//   coating      whole card, text box included (Parallel, Duel Terminal, Millennium, KaibaCorp, Starfoil, Mosaic, Shatterfoil, ...)
//   desaturate   the art goes pale (Ghost / Holographic / Mirror)
//   art foil     inside the art quad
//   frame foil   on the regions in FrameMask (attribute, stars, art border, card edge, text box border, the whole frame, the name box)
//   watermark    a faint medallion in the text box (20th / 25th / 10000 Secret)
//   name ink     the card name's own pixels recoloured (silver, gold, rainbow, speckled, ...)
// Every layer's light comes from a slow sweep, or from the mouse on a card shown big (MouseTilt), so the foil moves like a tilted card.
inline constexpr const char* kShinyHlsl = R"HLSL(
Texture2D Face : register(t0);
SamplerState Smp : register(s0);

cbuffer Shiny : register(b0)
{
    float Time, Strength, Phase, Speed;
    float Tilt, StarCount, NameWhite, Desat;
    float2 Mouse, Screen;
    float4 Art;        // face pixels: x0 y0 x1 y1
    float4 Stars;      // first star x0, y0, size, step (0 count = none)
    float4 Attr;       // x0 y0 x1 y1 (zero size = none)
    float4 Name;       // x0 y0 x1 y1 (zero size = no name ink)
    float4 TextBox;    // x0 y0 x1 y1
    int NameInk, ArtFoil, FrameFoil, FrameMask;
    int Coating, Watermark, Pad0, Pad1;
    float3 NameColour; float CoatStrength;
};

struct PSIn { float4 Pos : SV_POSITION; float4 Col : COLOR0; float2 UV : TEXCOORD0; };

static const float TAU = 6.2831853;

float3 Rainbow(float h) { return 0.5 + 0.5 * cos(TAU * (h + float3(0.0, 0.333, 0.667))); }
float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float2 Hash2(float2 p) { return frac(sin(float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)))) * 43758.5453); }
float Luma(float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }
float InRect(float2 p, float4 r) { return step(r.x, p.x) * step(p.x, r.z) * step(r.y, p.y) * step(p.y, r.w); }
float HasRect(float4 r) { return step(1.0, r.z - r.x) * step(1.0, r.w - r.y); }
// 1 in a band `w` pixels wide just outside (w > 0) or on (w < 0 = both sides) the rectangle's edge
float Ring(float2 p, float4 r, float w)
{
    float aw = abs(w);
    float outer = InRect(p, r + float4(-aw, -aw, aw, aw));
    float inner = InRect(p, w > 0 ? r : r + float4(aw, aw, -aw, -aw));
    return saturate(outer - inner) * HasRect(r);
}

// metals: gold, platinum, silver, bronze, champagne
float3 Metal(int kind)
{
    if (kind == 1) return float3(1.00, 0.78, 0.32);   // gold
    if (kind == 2) return float3(0.88, 0.91, 0.98);   // platinum
    if (kind == 3) return float3(0.80, 0.82, 0.86);   // silver
    if (kind == 4) return float3(0.80, 0.52, 0.30);   // bronze
    return float3(0.97, 0.86, 0.62);                  // champagne
}

// ---- patterns (0..1 masks), px = face pixels
float ParallelDots(float2 px, float period)       // the diagonal dot screen of Secret / Parallel foil
{
    float2 r = float2(px.x + px.y, px.x - px.y) * 0.7071 / period;
    float2 d = frac(r) - 0.5;
    return smoothstep(0.38, 0.12, length(d));
}
float PrismaticLines(float2 px)                     // horizontal + vertical bars (Prismatic Secret / Starlight)
{
    float h = pow(abs(sin(px.y * 0.55)), 6.0), v = pow(abs(sin(px.x * 0.55)), 6.0);
    return max(h, v * 0.7);
}
float StarShape(float2 d, float r)                  // a five-pointed star of radius r around 0
{
    float a = atan2(d.y, d.x), l = length(d);
    float k = 0.55 + 0.45 * pow(abs(cos(a * 2.5)), 3.0);
    return smoothstep(r * k + 0.8, r * k - 0.8, l);
}
float3 Voronoi(float2 p)                            // x = distance to the edge, y z = the cell's hash
{
    float2 i = floor(p), f = frac(p);
    float d1 = 8.0, d2 = 8.0; float2 cell = 0;
    for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            float2 o = float2(x, y), h = Hash2(i + o);
            float d = length(o + h - f);
            if (d < d1) { d2 = d1; d1 = d; cell = i + o; }
            else if (d < d2) d2 = d;
        }
    return float3(d2 - d1, Hash2(cell));
}
float Twinkle(float2 px, float cell, float t, float density)
{
    float2 c = floor(px / cell);
    float g = Hash(c);
    float2 centre = (c + 0.25 + 0.5 * Hash2(c + 7.0)) * cell;
    float spot = smoothstep(cell * 0.35, 0.0, length(px - centre));
    return spot * step(1.0 - density, g) * pow(saturate(sin(t * 2.5 + g * 60.0)), 10.0);
}

// over: puts layer (rgb, a) on top of acc (premultiplied rgb, alpha)
void Over(inout float4 acc, float3 rgb, float a)
{
    a = saturate(a);
    acc.rgb = rgb * a + acc.rgb * (1.0 - a);
    acc.a = a + acc.a * (1.0 - a);
}

// relief lit from the light direction (Ultimate, Premium Gold): the face's own edges as a bump map
float4 Emboss(float2 uv, float ang, float band, float lum)
{
    float2 e = float2(1.0 / 400.0, 1.0 / 580.0) * 1.5;
    float gx = Luma(Face.Sample(Smp, uv + float2(e.x, 0)).rgb) - Luma(Face.Sample(Smp, uv - float2(e.x, 0)).rgb);
    float gy = Luma(Face.Sample(Smp, uv + float2(0, e.y)).rgb) - Luma(Face.Sample(Smp, uv - float2(0, e.y)).rgb);
    float2 light = float2(cos(ang * TAU), sin(ang * TAU));
    float shade = dot(float2(gx, gy), light) * 4.0;
    float3 col = Metal(1) * (0.55 + 0.45 * lum) + shade;
    return float4(saturate(col), 0.35 + 0.25 * abs(shade) + 0.15 * band);
}

// ---- foils: what a region looks like (rgb, strength). kind numbers = Rarity.h Foil
float4 Foil(int kind, float2 px, float2 uv, float ang, float band, float lum, float t)
{
    float3 holo = Rainbow(ang * 1.6 + t * 0.05);
    if (kind == 1)          // holo: smooth rainbow (Super / Ultra)
        return float4(holo * (0.35 + 0.65 * lum), 0.22 + 0.3 * band);
    if (kind == 2)          // parallel: diagonal dot screen (Secret)
    {
        float dots = ParallelDots(px, 3.2);
        return float4(Rainbow(ang * 3.0 + (px.x + px.y) * 0.004) * (0.4 + 0.6 * lum), 0.08 + 0.32 * dots + 0.25 * band);
    }
    if (kind == 3)          // prismatic: horizontal/vertical bars
    {
        float bars = PrismaticLines(px);
        return float4(Rainbow(ang * 2.2 + px.y * 0.01) * (0.4 + 0.6 * lum), 0.1 + 0.35 * bars + 0.25 * band);
    }
    if (kind == 4)          // ghost: pale silver-blue holo
        return float4(lerp(float3(0.75, 0.85, 1.0), holo, 0.35) * (0.5 + 0.5 * lum), 0.25 + 0.3 * band);
    if (kind == 5)          // emboss (Ultimate)
        return Emboss(uv, ang, band, lum);
    if (kind == 6 || kind == 7)   // gold / platinum foil
    {
        float3 m = Metal(kind == 6 ? 1 : 2);
        float sheen = 0.65 + 0.35 * sin(ang * 9.0 + t);
        return float4(m * sheen * (0.55 + 0.45 * lum) + holo * 0.12, 0.45 + 0.3 * band);
    }
    if (kind == 8 || kind == 9)   // gold / platinum with the Secret dot screen
    {
        float3 m = Metal(kind == 8 ? 1 : 2);
        float dots = ParallelDots(px, 3.2);
        return float4(m * (0.6 + 0.4 * lum) + Rainbow(ang * 3.0 + (px.x + px.y) * 0.004) * 0.25 * dots, 0.4 + 0.3 * dots + 0.2 * band);
    }
    if (kind == 10)         // relief gold: thick raised gold (Premium Gold / Gold Rush)
    {
        float4 r = Emboss(uv, ang, band, lum);
        return float4(lerp(r.rgb, Metal(1), 0.5), 0.6 + 0.2 * band);
    }
    if (kind == 11)         // rainbow parallel (Collector's)
    {
        float lines = 0.5 + 0.5 * sin((px.x - px.y) * 0.8 + ang * 18.0);
        return float4(Rainbow(ang * 2.5 + (px.x - px.y) * 0.006) * (0.45 + 0.55 * lum), 0.18 + 0.25 * lines + 0.25 * band);
    }
    return 0;
}

// ---- coatings: the whole card, text box included. kind numbers = Rarity.h Coating
float4 Coat(int kind, float2 px, float ang, float band, float t)
{
    float3 holo = Rainbow(ang * 2.0 + (px.x + px.y) * 0.003);
    if (kind == 1)          // Parallel: fine haze of dots
        return float4(holo, 0.05 + 0.12 * ParallelDots(px, 2.4) + 0.15 * band);
    if (kind == 2)          // Duel Terminal: the same with much larger dots
        return float4(holo, 0.05 + 0.16 * ParallelDots(px, 9.0) + 0.15 * band);
    if (kind == 3)          // Millennium: vertical columns of glyph cells
    {
        float col = floor(px.x / 22.0);
        float2 cell = float2(col, floor((px.y + Hash(float2(col, 3)) * 40.0) / 22.0));
        float2 f = frac(px / 22.0);
        float glyph = step(0.45, Hash(cell + floor(f * 4.0))) * step(0.12, f.x) * step(f.x, 0.88) * step(0.12, f.y) * step(f.y, 0.88);
        float stripe = step(0.5, frac(col * 0.5));
        return float4(lerp(holo, float3(1.0, 0.85, 0.45), 0.35), 0.05 + 0.16 * glyph * (0.5 + 0.5 * stripe) + 0.15 * band);
    }
    if (kind == 4)          // KaibaCorp: hi-tech grid and circuit traces
    {
        float2 g = abs(frac(px / 24.0) - 0.5);
        float grid = smoothstep(0.47, 0.5, max(g.x, g.y));
        float trace = step(0.7, Hash(floor(px / 24.0))) * smoothstep(0.06, 0.0, abs(g.y - 0.2));
        return float4(lerp(holo, float3(0.4, 0.8, 1.0), 0.4), 0.05 + 0.2 * max(grid, trace) + 0.15 * band);
    }
    if (kind == 5)          // Starfoil: stars all over
    {
        float2 c = floor(px / 20.0), f = (frac(px / 20.0) - 0.5) * 20.0;
        float s = StarShape(f, 5.0 + 2.0 * Hash(c));
        return float4(Rainbow(ang * 2.0 + Hash(c)), 0.04 + 0.35 * s + 0.12 * band);
    }
    if (kind == 6)          // Mosaic: squares, each its own angle
    {
        float2 c = floor(px / 12.0), f = frac(px / 12.0);
        float edge = step(0.1, f.x) * step(0.1, f.y);
        return float4(Rainbow(ang * 2.0 + Hash(c) * 0.7), 0.06 + 0.22 * edge * (0.4 + 0.6 * Hash(c + 3.0)) + 0.12 * band);
    }
    if (kind == 7)          // Shatterfoil: shattered glass cells
    {
        float3 v = Voronoi(px / 26.0);
        float crack = smoothstep(0.06, 0.0, v.x);
        return float4(Rainbow(ang * 2.0 + v.y * 0.8), 0.07 + 0.2 * v.z + 0.25 * crack + 0.12 * band);
    }
    if (kind == 8)          // crisscross (Full Over Rush)
    {
        float a = pow(abs(sin((px.x + px.y) * 0.35)), 8.0), b = pow(abs(sin((px.x - px.y) * 0.35)), 8.0);
        return float4(holo, 0.05 + 0.25 * max(a, b) + 0.12 * band);
    }
    if (kind == 9)          // sparkle dots (Prismatic Collector's)
        return float4(float3(1, 1, 1) * 0.9 + holo * 0.3, Twinkle(px, 7.0, t, 0.25) * 0.9 + 0.04 * band);
    return 0;
}

float3 Ink(int kind, float2 px, float ang, float t)
{
    float speck = step(0.55, Hash(floor(px / 2.0)));
    float shine = 0.7 + 0.3 * sin(ang * 8.0 + t);
    if (kind == 1) return Metal(3) * shine;                                         // silver
    if (kind == 2) return Metal(1) * shine;                                         // gold
    if (kind == 3) return Rainbow(ang * 3.0 + px.x * 0.02) * 0.6 + 0.45;            // rainbow (Secret)
    if (kind == 4) return lerp(Metal(3), Rainbow(ang * 3.0 + Hash(floor(px / 2.0))), speck) * shine;   // speckled rainbow (Prismatic)
    if (kind == 5) return lerp(Metal(5), Rainbow(ang * 3.0 + px.x * 0.02) * 0.5 + 0.5, speck * 0.5) * shine;   // champagne speckled (25th)
    if (kind == 6) return lerp(float3(0.85, 0.12, 0.12), Rainbow(ang * 3.0) * 0.5 + 0.5, speck * 0.4) * shine;   // red speckled (20th)
    if (kind == 7) return Metal(4) * shine;                                         // bronze
    if (kind == 8) return Metal(2) * shine;                                         // platinum
    if (kind == 9) return NameColour * shine;                                       // a colour (Colorful)
    if (kind == 10) return lerp(Metal(3), float3(1, 1, 1), speck) * shine;          // sparkly silver (Ultra Secret)
    return 0;
}

float4 main(PSIn i) : SV_TARGET
{
    float4 face = Face.Sample(Smp, i.UV);
    float2 px = i.UV * float2(400.0, 580.0);
    float t = Time * Speed;
    float lum = Luma(face.rgb);

    // the light: a slow sweep of its own; on a card shown big (the card details view: over half the screen's height, measured from how
    // fast the face's v changes per screen pixel) it follows the mouse instead (MouseTilt), twice as strongly, like tilting the card
    float2 sweep = float2(sin(t * 0.4 + Phase * TAU), cos(t * 0.3 + Phase * TAU)) * 0.35;
    float cardHeight = 1.0 / max(abs(ddx(i.UV.y)) + abs(ddy(i.UV.y)), 1e-5);
    float big = saturate((cardHeight / max(Screen.y, 1.0) - 0.5) / 0.15) * step(0.5, Tilt);
    float2 view = lerp(sweep, (i.Pos.xy - Mouse) / max(Screen.y, 1.0) * 2.0, big);
    float ang = dot(i.UV, float2(0.8, 1.1)) + dot(view, float2(1.2, 0.9)) + Phase;
    float bandPos = frac(t * 0.08 + Phase) * 3.0 - 1.0;
    float band = exp(-pow((dot(i.UV, float2(0.6, 0.8)) - bandPos + view.x * 0.5) * 7.0, 2.0));

    float4 acc = 0;
    float art = InRect(px, Art) * HasRect(Art);
    float text = InRect(px, TextBox) * HasRect(TextBox);

    // coating: the whole card
    if (Coating > 0)
    {
        float4 c = Coat(Coating, px, ang, band, t);
        Over(acc, c.rgb, c.a * CoatStrength);
    }

    // pale art (Ghost / Holographic / Mirror)
    if (Desat > 0 && art > 0)
        Over(acc, lerp(float3(lum, lum, lum), float3(0.8, 0.88, 1.0) * lum, 0.4) * 1.1, Desat);

    if (ArtFoil > 0 && art > 0)
    {
        float4 f = Foil(ArtFoil, px, i.UV, ang, band, lum, t);
        Over(acc, f.rgb, f.a);
    }

    // frame foil on the regions in FrameMask (Rarity.h Region)
    if (FrameFoil > 0 && FrameMask != 0)
    {
        float m = 0;
        if (FrameMask & 1)  m = max(m, InRect(px, Attr) * HasRect(Attr) * smoothstep(0.5 * (Attr.z - Attr.x), 0.42 * (Attr.z - Attr.x), length(px - 0.5 * (Attr.xy + Attr.zw))));
        if ((FrameMask & 2) && StarCount > 0)
        {
            float k = floor((px.x - Stars.x) / max(Stars.w, 1.0));
            float2 c = float2(Stars.x + k * Stars.w + 0.5 * Stars.z, Stars.y + 0.5 * Stars.z);
            m = max(m, step(0.0, k) * step(k, StarCount - 1.0) * smoothstep(0.5 * Stars.z, 0.4 * Stars.z, length(px - c)));
        }
        if (FrameMask & 4)  m = max(m, Ring(px, Art, 7.0));
        if (FrameMask & 8)  m = max(m, 1.0 - InRect(px, float4(13.0, 13.0, 387.0, 567.0)));
        if (FrameMask & 16) m = max(m, Ring(px, TextBox, -3.0));
        if (FrameMask & 32) m = max(m, (1.0 - text) * (1.0 - art));
        if (FrameMask & 64) m = max(m, InRect(px, Name) * HasRect(Name));
        if (m > 0)
        {
            float4 f = Foil(FrameFoil, px, i.UV, ang, band, lum, t);
            Over(acc, f.rgb, f.a * m);
        }
    }

    // watermark: a faint embossed medallion in the middle of the text box (the logo itself is not drawn)
    if (Watermark > 0 && HasRect(TextBox) > 0)
    {
        float2 c = 0.5 * (TextBox.xy + TextBox.zw);
        float d = length(px - c);
        float ring = smoothstep(2.0, 0.0, abs(d - 34.0)) + smoothstep(1.5, 0.0, abs(d - 27.0));
        float3 tint = Watermark == 2 ? Metal(5) : Watermark == 1 ? float3(0.9, 0.3, 0.3) : Metal(3);
        Over(acc, tint, ring * 0.25);
    }

    // name ink: only the name's own pixels (dark text, or light text on Spell / Trap / Xyz / Link frames)
    if (NameInk > 0 && HasRect(Name) > 0 && InRect(px, Name) > 0)
    {
        float textMask = NameWhite > 0.5 ? smoothstep(0.72, 0.9, lum) : 1.0 - smoothstep(0.12, 0.3, lum);
        Over(acc, Ink(NameInk, px, ang, t), textMask * 0.92);
    }

    float k = clamp(Strength, 0.0, 2.0) * i.Col.a * face.a;
    return saturate(float4(acc.rgb * k, acc.a * k));
}
)HLSL";
