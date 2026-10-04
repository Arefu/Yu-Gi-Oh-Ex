#pragma once
#include <cstdint>

// The live duel state: where the engine keeps both sides' life points, zones and piles, the tag duel partner who is waiting for the turn
// change, and the decks each seat was given. All of it is named in YuGiOh.exe.i64 (the exe is not relocated); the write-ups are
// docs/EffectSystem.md section 28 (Duel::PlayerState) and docs/MultiplayerSystem.md (tag duels, LP key, message queue).
//
// The readers here dereference game memory directly. Outside a duel the values are stale or zero, not invalid, but a caller that may run
// before the game has set anything up (or from another thread) should guard its reads (SEH), as Yu-Gi-Oh-GUI does.
namespace YGO
{
    namespace DUELSTATE
    {
        // ---- addresses

        constexpr uintptr_t PlayerState = 0x143497C40;     // Duel::PlayerState (14640 bytes): one 0xD94 block per side, then duel-wide fields
        constexpr uintptr_t PlayerBlock = 0xD94;
        constexpr uintptr_t PlayerStateSize = 14640;
        constexpr uintptr_t RngOffset = 0x3768;            // PlayerState +0x3768: the duel's RNG state (MSVC LCG)
        constexpr uintptr_t WinnerOffset = 0x3792;         // PlayerState +0x3792 (u8): 1/2 = a side won, 3 = draw
        constexpr uintptr_t CardInstances = 0x143499798;   // PlayerState +0x1B58: the duel's cards, 8 bytes each, low 14 bits = card id

        constexpr uintptr_t DuelEngine = 0x143330280;      // Duel_DuelEngine (Duel_Engine, 824 bytes)
        constexpr uintptr_t LpXorKey = DuelEngine + 0x00;  // u16: LP is stored XORed with it (the word after it is unrelated)
        constexpr uintptr_t LocalSeatParity = DuelEngine + 0x04;
        constexpr uintptr_t StartingPlayer = DuelEngine + 0x28;

        constexpr uintptr_t MsgQueue = 0x14332FA40;        // g_DuelMsgQueue: Front +0, Entries[256] +0x10, Count +0x810
        constexpr uintptr_t MsgQueueCount = MsgQueue + 0x810;
        constexpr uintptr_t LastWinReason = 0x1433305B4;   // g_LastDuelWinReason (DuelWinReason)
        constexpr uintptr_t TagDuelFlag = 0x140C8D35D;     // YGO::DUEL::g_bIsTagDuel (u8)

        // Tag duels (Duel_UNK, 0x14332F500): +side = swap flag (0 = the seat 0/1 duelist is on the field, 1 = the partner, seat side + 2),
        // +4 + 302 * side = the partner who is not on the field: u16 hand, deck, extra counts, then u16 card instance indexes (hand, deck,
        // extra, up to 147). Duel_LoadEngineFromFrontBlock (0x140082960) swaps it with the side's piles at the turn change from turn 2.
        constexpr uintptr_t TagFront = 0x14332F500;
        constexpr uintptr_t TagParkedStride = 302;
        constexpr int TagParkedMax = 147;

        // Duel_PlayerRecords: one record per seat (4 in a tag duel). The deck the seat was given is u16 words from +0x40: [33] main count,
        // [34] extra, [35] side, [36..95] main, [96..110] extra, [111..125] side. Filled by YGO__DuelSetup__AssignSeatDecksAndDuelists.
        constexpr uintptr_t PlayerRecords = 0x142793578;
        constexpr uintptr_t PlayerRecordSize = 0x4820;
        constexpr uintptr_t RecordDeckWords = 0x40;

        // ---- inside a side's block

        constexpr uintptr_t LifePointsOffset = 0x00;       // u32, XOR LpXorKey
        constexpr uintptr_t MonsterZones = 0x4C;           // Duel::InPlay_Card[5]
        constexpr uintptr_t SpellTrapZones = 0xF0;         // Duel::InPlay_Card[5]
        constexpr uintptr_t FieldSpell = 0x168;            // Duel::InPlay_Card
        constexpr uintptr_t ZoneSize = 24;                 // Duel::InPlay_Card: CARDID, INTLID, ?, Pos (Duel::CardPosition), ?, ?
        constexpr uintptr_t ZonePosition = 0x0C;

        // Duel::CardPosition: 2/4/8 confirmed; 1 is named FDD in the IDB but not confirmed as face-up attack.
        enum CardPosition : uint32_t
        {
            POS_1_UNCONFIRMED = 1,
            POS_FACEDOWN_ATTACK = 2,
            POS_FACEUP_DEFENSE = 4,
            POS_FACEDOWN_DEFENSE = 8,
        };

        // A pile: a u32 count in Duel::PlayerState::Player and an array of packed card words. Zone = the engine's zone number.
        struct Pile
        {
            const char* Name;
            uintptr_t CountOffset;
            uintptr_t ArrayOffset;
            int Max;
            int Zone;
        };
        constexpr Pile Hand = { "Hand", 0x0C, 0x19C, 120, 13 };
        constexpr Pile Deck = { "Deck", 0x10, 0x37C, 120, 15 };
        constexpr Pile Graveyard = { "Graveyard", 0x14, 0x7B4, 149, 16 };
        constexpr Pile ExtraDeck = { "Extra Deck", 0x18, 0x55C, 150, 14 };
        constexpr Pile Banished = { "Banished", 0x1C, 0xA0C, 226, 17 };

        // ---- packed card words (zones, piles): low 14 bits = card id, bit 14 + bits 23-30 = the card's instance (its index for the whole duel)

        constexpr uint16_t CardIdOf(uint32_t word) { return static_cast<uint16_t>(word & 0x3FFF); }
        constexpr uint32_t InstanceOf(uint32_t word) { return ((word >> 14) & 1) | (((word >> 23) & 0xFF) << 1); }

        // ---- readers

        template <class T> T Read(uintptr_t address) { return *reinterpret_cast<volatile const T*>(address); }

        inline uintptr_t SideBlock(int side) { return PlayerState + PlayerBlock * (side & 1); }
        inline int LifePoints(int side) { return static_cast<int>(Read<uint32_t>(SideBlock(side) + LifePointsOffset) ^ Read<uint16_t>(LpXorKey)); }
        inline uint32_t PileCount(int side, const Pile& pile) { return Read<uint32_t>(SideBlock(side) + pile.CountOffset); }
        inline uint32_t PileWord(int side, const Pile& pile, int index) { return Read<uint32_t>(SideBlock(side) + pile.ArrayOffset + 4 * index); }
        inline uint16_t InstanceCardId(uint16_t instance) { return CardIdOf(Read<uint32_t>(CardInstances + 8 * instance)); }

        inline bool IsTag() { return Read<uint8_t>(TagDuelFlag) != 0; }
        inline bool IsTagSwapped(int side) { return Read<uint8_t>(TagFront + (side & 1)) != 0; }
        // The seat (0-3) whose hand and decks the side's PlayerState block holds now.
        inline int SeatOnField(int side) { return IsTag() && IsTagSwapped(side) ? (side & 1) + 2 : (side & 1); }
        inline uintptr_t TagParkedBlock(int side) { return TagFront + 4 + TagParkedStride * (side & 1); }

        inline uintptr_t SeatDeckWords(int seat) { return PlayerRecords + PlayerRecordSize * seat + RecordDeckWords; }
    }
}
