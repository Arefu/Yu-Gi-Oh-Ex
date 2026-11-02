#pragma once
#include <cstdint>
#include <string>
#include <vector>

// Card rarities: every rarity a printed Yu-Gi-Oh! card can have (Yugipedia's Category:Rarities, 2026-11-01, without the video-game-only
// pack-screen colours of 7 Trials to Glory, BAM Legend, and the KaibaCorp Super / Secret Rares that were never printed), each described
// as the layers ShinyShader.h draws:
//
//   name ink     the card name's colour (silver Rare, gold Ultra, rainbow Secret, speckled Prismatic, champagne 25th, red 20th, ...)
//   art foil     the artwork (holo, Secret dot screen, prismatic bars, pale Ghost, embossed Ultimate, gold, platinum, ...)
//   frame foil   + the regions it covers: attribute, stars, art border, card edge, text box border, the whole frame, the name box
//   coating      the whole card, text box included (Parallel, Duel Terminal, Millennium, KaibaCorp, Starfoil, Mosaic, Shatterfoil, ...)
//   desaturate   the art goes pale (Ghost, Holographic, Mirror)
//   watermark    a medallion in the text box (20th, 25th, 10000 Secret; the logos themselves are not drawn)
//
// A rarity's id is what the save keeps (Shiny.h): never renumber one. Yu-Gi-Oh-Ex\rarities.json (every mod's copy merged, Yu-Gi-Oh-Mods.h)
// changes built-in rarities by "key" and adds new ones (ids 128-255 are free for mods):
//   { "rarities": [ { "key": "ultra", "odds": 20 },
//                   { "id": 128, "key": "neon", "name": "Neon Rare", "tier": 33, "odds": 400, "slot": "rare",
//                     "nameInk": "colour", "nameColour": "#40FFE0", "artFoil": "holo", "frameFoil": "rainbow",
//                     "regions": ["attribute", "stars", "border"], "coating": "sparkle", "art": "broken" } ] }
// Names for the numbers: see the k*Names tables in Rarity.cpp.
namespace Rarity
{
    enum Ink : int32_t { InkNone, InkSilver, InkGold, InkRainbow, InkSpeckled, InkChampagne, InkRed, InkBronze, InkPlatinum, InkColour, InkSparkleSilver };
    enum Foil : int32_t { FoilNone, FoilHolo, FoilParallel, FoilPrismatic, FoilGhost, FoilEmboss, FoilGold, FoilPlatinum, FoilSecretGold,
                          FoilSecretPlatinum, FoilReliefGold, FoilRainbow };
    enum Coat : int32_t { CoatNone, CoatParallel, CoatDuelTerminal, CoatMillennium, CoatKaibaCorp, CoatStarfoil, CoatMosaic, CoatShatterfoil,
                          CoatCrisscross, CoatSparkle };
    enum Region : int32_t { RegAttribute = 1, RegStars = 2, RegArtBorder = 4, RegEdge = 8, RegTextBorder = 16, RegWholeFrame = 32, RegNameBox = 64 };
    enum Mark : int32_t { MarkNone, Mark20th, Mark25th, Mark10000 };
    enum class Slot { None, Rare, Any };     // which pack slots can roll it
    // The card's own picture for this rarity, from the content folders' cardart\<cardId>.<style>.png (or .jpg), when one is there:
    //   alt       another artwork in the normal frame
    //   broken    the whole card as one picture, the art breaking out of a frame painted into it (Over Rush; Master Duel's 512 x 1024
    //             card pictures); the game's frame is not drawn, the text goes on top
    //   full      the art covers the whole card with no frame (Full Over Rush); the game's frame is drawn see-through over it
    //   extended  the art runs over the frame's art border (Grand Master); drawn as full
    //   printed   a scan of the printed card, text and all (an extended-art print with no text-free picture): shown as it is, the game
    //             draws nothing over it
    // Without a picture the card is drawn as usual.
    enum ArtStyle : int32_t { ArtNormal, ArtAlt, ArtBroken, ArtFull, ArtExtended, ArtPrinted };

    struct Def
    {
        uint8_t Id = 0;
        std::string Key;                     // stable name ("ultra", "starlight"), used by settings and rarities.json
        std::string Name;                    // "Ultra Rare"
        int Tier = 0;                        // which one a card shows when several are owned (highest)
        int Odds = 0;                        // 1 in Odds per card in a slot it can roll in; 0 = never from packs
        Slot RollSlot = Slot::None;
        bool Fallback = false;               // what a rare-slot card is when nothing rarer was rolled (Rare: silver name)
        int32_t NameInk = InkNone;
        float NameColour[3] = { 1, 1, 1 };   // InkColour
        int32_t ArtFoil = FoilNone;
        int32_t FrameFoil = FoilNone;
        int32_t Regions = 0;
        int32_t Coating = CoatNone;
        float CoatStrength = 1.0f;
        float Desaturate = 0.0f;
        int32_t Watermark = MarkNone;
        int32_t Art = ArtNormal;
        std::string Note;                   // e.g. "the artwork leaves its frame on real cards (needs alternative art)"
    };

    // The catalogue: the built-in rarities with rarities.json applied (read once, at the first call). Sorted by id.
    const std::vector<Def>& All();
    const Def* ById(uint8_t id);
    const Def* ByKey(const std::string& key);   // case-insensitive; also matches the name ("Ultra Rare")
}
