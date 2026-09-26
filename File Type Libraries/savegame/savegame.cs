using System.Buffers.Binary;
using System.Text;

namespace savegame
{
    /// <summary>
    /// The 44,008 byte savegame.dat of Legacy of the Duelist: Link Evolution.
    ///
    /// Everything is a view over one byte buffer, so saving writes back exactly what was loaded except for
    /// the fields that were edited (the parts that are not decoded yet survive a load / save untouched).
    ///
    /// Layout (checked against the game's own init / validate code and against real save files):
    ///   0x0000  header        magic, version, size, checksum, save counter
    ///   0x0014  settings      4 values the game initialises to 5, 5, 0, 1.0f
    ///   0x0024  stats         100 x 8 byte counters (SaveStat)
    ///   0x0344  duelist slots 5 x 644 bytes
    ///   0x0FD8  player        wallet, unlocked characters, per deck state (2968 bytes)
    ///   0x1B70  section       6 blocks of 50 six dword entries (7256 bytes) - not decoded yet
    ///   0x37C8  decks         32 x 304 bytes
    ///   0x5DC8  card table    20000 bytes, one per Konami card id
    /// </summary>
    public sealed class SaveFile
    {
        public const int Size = 44008;
        public const uint Magic = 0x54CE29F9;
        public const uint CurrentVersion = 0x04ABE802;

        public const int HeaderSize = 0x14;
        public const int SettingsOffset = 0x14;
        public const int StatsOffset = 0x24;
        public const int StatCount = 100;
        public const int DuelistSlotsOffset = 0x344;
        public const int DuelistSlotSize = 644;
        public const int DuelistSlotCount = 5;
        public const int PlayerOffset = 0xFD8;
        public const int PlayerSize = 2968;
        public const int SectionOffset = 0x1B70;
        public const int SectionSize = 7256;
        public const int DecksOffset = 0x37C8;
        public const int DeckSize = 304;
        public const int DeckCount = 32;
        public const int CardTableOffset = 0x5DC8;
        public const int CardTableSize = 20000;

        private readonly byte[] _data;

        public SaveFile(byte[] data)
        {
            if (data.Length != Size)
                throw new InvalidDataException($"A save file is {Size} bytes, this one is {data.Length}.");

            _data = data;

            Player = new PlayerData(_data, PlayerOffset);
            Cards = new CardTable(_data, CardTableOffset);
            Section = new ProfileSection(_data, SectionOffset);

            Decks = new DeckSlot[DeckCount];
            for (int i = 0; i < DeckCount; i++)
                Decks[i] = new DeckSlot(_data, DecksOffset + DeckSize * i);

            Duelists = new DuelistSlot[DuelistSlotCount];
            for (int i = 0; i < DuelistSlotCount; i++)
                Duelists[i] = new DuelistSlot(_data, DuelistSlotsOffset + DuelistSlotSize * i);
        }

        public static SaveFile Load(string path) => new(File.ReadAllBytes(path));

        // ---------------------------------------------------------------- header

        public uint MagicNumber => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(0));

        public uint Version
        {
            get => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(4));
            set => BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(4), value);
        }

        public uint TotalSize => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(8));

        public uint Checksum
        {
            get => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(12));
            private set => BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(12), value);
        }

        /// <summary>Increased by the game every time it writes the save.</summary>
        public uint SaveCounter
        {
            get => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(16));
            set => BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(16), value);
        }

        /// <summary>The 4 values at 0x14 (the game starts them at 5, 5, 0 and 1.0). What they control is not known yet.</summary>
        public uint Setting(int index) => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(SettingsOffset + 4 * index));

        // ---------------------------------------------------------------- checksum

        /// <summary>
        /// The game's checksum: CRC-32 with the usual 0xEDB88320 table, starting at 0xFFFFFFFF and WITHOUT the final
        /// inversion, over the whole file with the checksum field set to 0.
        /// </summary>
        public static uint ComputeChecksum(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            foreach (byte b in data)
                crc = (crc >> 8) ^ CrcTable[(crc ^ b) & 0xFF];
            return crc;
        }

        public bool ChecksumIsValid
        {
            get
            {
                byte[] copy = (byte[])_data.Clone();
                copy[12] = copy[13] = copy[14] = copy[15] = 0;
                return ComputeChecksum(copy) == Checksum;
            }
        }

        public bool HasValidHeader => MagicNumber == Magic && TotalSize == Size;

        /// <summary>Same steps as the game before it writes the save: bump the counter, then store the checksum.</summary>
        public void UpdateChecksum(bool incrementCounter = true)
        {
            if (incrementCounter)
                SaveCounter++;

            Checksum = 0;
            Checksum = ComputeChecksum(_data);
        }

        public byte[] ToBytes(bool updateChecksum = true)
        {
            if (updateChecksum)
                UpdateChecksum();

            return (byte[])_data.Clone();
        }

        public void Save(string path, bool updateChecksum = true) => File.WriteAllBytes(path, ToBytes(updateChecksum));

        // ---------------------------------------------------------------- sections

        public PlayerData Player { get; }
        public CardTable Cards { get; }
        public ProfileSection Section { get; }
        public DeckSlot[] Decks { get; }
        public DuelistSlot[] Duelists { get; }

        /// <summary>A counter from the stats section (games played, wins, summons, damage ...). See <see cref="SaveStat"/>.</summary>
        public ulong GetStat(int index)
        {
            CheckStat(index);
            return BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(StatsOffset + 8 * index));
        }

        public void SetStat(int index, ulong value)
        {
            CheckStat(index);
            BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(StatsOffset + 8 * index), value);
        }

        public ulong GetStat(SaveStat stat) => GetStat((int)stat);
        public void SetStat(SaveStat stat, ulong value) => SetStat((int)stat, value);

        private static void CheckStat(int index)
        {
            if ((uint)index >= StatCount)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        private static readonly uint[] CrcTable = BuildCrcTable();

        private static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++)
                    c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320 : c >> 1;
                table[i] = c;
            }
            return table;
        }
    }

    /// <summary>The names the game gives the stats it counts (UpdateSaveStat). Index 43 and up are unused by the game.</summary>
    public enum SaveStat
    {
        GamesCampaign = 0, GamesCampaignNormal, GamesCampaignReverse, GamesChallenge,
        GamesMultiplayer, GamesMultiplayer1v1, GamesMultiplayerTag, GamesMultiplayerRanked, GamesMultiplayerFriendly,
        GamesBattlePackAny, GamesBattlePack1, GamesBattlePack2, GamesBattlePack3,
        WinsCampaign, WinsCampaignNormal, WinsCampaignReverse, WinsChallenge,
        WinsMultiplayer, WinsMultiplayer1v1, WinsMultiplayerTag, WinsMultiplayerRanked, WinsMultiplayerFriendly,
        WinsBattlePackAny, WinsBattlePack1, WinsBattlePack2, WinsBattlePack3,
        WinsMatch, WinsNonMatch,
        SummonsNormal, SummonsTribute, SummonsRitual, SummonsFusion, SummonsXyz, SummonsSynchro, SummonsPendulum,
        DamageAny, DamageBattle, DamageDirect, DamageEffect, DamageReflect,
        Chains, DecksCreated, CardsEarned,
    }

    /// <summary>Which cards the player owns and how many copies. One byte per Konami card id.</summary>
    public sealed class CardTable
    {
        private readonly byte[] _data;
        private readonly int _offset;

        internal CardTable(byte[] data, int offset)
        {
            _data = data;
            _offset = offset;
        }

        public int Length => SaveFile.CardTableSize;

        /// <summary>Highest number of copies the game keeps track of.</summary>
        public const int MaxCopies = 3;

        /// <summary>Bits 0-2 are the copies. Bit 3 is set once the player has seen the card (the game sets it for the cards
        /// it grants at the start); an owned card without it is shown as new. Bits 4-7 are kept, their meaning is not known.</summary>
        public byte GetRaw(int id) => _data[_offset + Check(id)];

        public void SetRaw(int id, byte value) => _data[_offset + Check(id)] = value;

        public int GetCopies(int id) => GetRaw(id) & 7;

        public void SetCopies(int id, int copies)
        {
            if (copies < 0 || copies > MaxCopies)
                throw new ArgumentOutOfRangeException(nameof(copies), "The game keeps 0 to 3 copies.");

            SetRaw(id, (byte)((GetRaw(id) & ~7) | copies));
        }

        /// <summary>True when the card is owned but not seen yet, which the game shows with a "new" marker.</summary>
        public bool IsNew(int id) => GetCopies(id) != 0 && (GetRaw(id) & 8) == 0;

        public void SetNew(int id, bool isNew) => SetRaw(id, (byte)(isNew ? GetRaw(id) & ~8 : GetRaw(id) | 8));

        /// <summary>Every card with at least one copy, as (Konami id, copies).</summary>
        public IEnumerable<(int Id, int Copies)> Owned()
        {
            for (int id = 0; id < Length; id++)
            {
                int copies = GetCopies(id);
                if (copies != 0)
                    yield return (id, copies);
            }
        }

        public void Clear()
        {
            Array.Clear(_data, _offset, Length);
        }

        private static int Check(int id)
        {
            if ((uint)id >= SaveFile.CardTableSize)
                throw new ArgumentOutOfRangeException(nameof(id), $"The save keeps ownership for ids 0 to {SaveFile.CardTableSize - 1}.");
            return id;
        }
    }

    /// <summary>
    /// The player section. Confirmed from the game's init code: the wallet (points), the unlocked characters
    /// and the per deck values. What each per deck bit means is inferred, see <see cref="GetDeckState"/>.
    /// </summary>
    public sealed class PlayerData
    {
        public const int CharacterCount = 240;
        public const int DeckCount = 700;

        private const int WalletOffset = 0x10;
        private const int CharactersOffset = 0x18;
        private const int DeckStateOffset = 0x38;
        private const int DeckOwnedOffset = 0xB28;
        private const int FlagsOffset = 0xB80;
        public const int FlagCount = 128;

        private readonly byte[] _data;
        private readonly int _offset;

        internal PlayerData(byte[] data, int offset)
        {
            _data = data;
            _offset = offset;
        }

        /// <summary>The points (DP) the player can spend. The game starts a new profile with 1000.</summary>
        public ulong Wallet
        {
            get => BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan(_offset + WalletOffset));
            set => BinaryPrimitives.WriteUInt64LittleEndian(_data.AsSpan(_offset + WalletOffset), value);
        }

        /// <summary>Characters (duelists) the player can pick or face. A new profile starts with the ones that have no requirement.</summary>
        public bool IsCharacterUnlocked(int character) => GetBit(CharactersOffset, character, CharacterCount);

        public void SetCharacterUnlocked(int character, bool unlocked) => SetBit(CharactersOffset, character, CharacterCount, unlocked);

        public IEnumerable<int> UnlockedCharacters() => Enumerable.Range(0, CharacterCount).Where(IsCharacterUnlocked);

        /// <summary>
        /// One 32 bit value per deck id (0 to 699). Non-zero means the deck is known to the player; the game sets it
        /// to 1 for the starter decks. Bit 1 (value 2) looks like the "beaten" marker but that is not confirmed yet.
        /// </summary>
        public uint GetDeckState(int deck)
        {
            CheckDeck(deck);
            return BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_offset + DeckStateOffset + 4 * deck));
        }

        public void SetDeckState(int deck, uint state)
        {
            CheckDeck(deck);
            BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(_offset + DeckStateOffset + 4 * deck), state);
        }

        /// <summary>One bit per deck id. The game sets it for the starter decks of a new profile.</summary>
        public bool IsDeckOwned(int deck) => GetBit(DeckOwnedOffset, deck, DeckCount);

        public void SetDeckOwned(int deck, bool owned) => SetBit(DeckOwnedOffset, deck, DeckCount, owned);

        /// <summary>A 128 bit set of flags whose meaning is not known yet (the game reads it in one place).</summary>
        public bool GetFlag(int index) => GetBit(FlagsOffset, index, FlagCount);

        public void SetFlag(int index, bool value) => SetBit(FlagsOffset, index, FlagCount, value);

        private bool GetBit(int start, int index, int count)
        {
            if ((uint)index >= count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (_data[_offset + start + (index >> 3)] >> (index & 7) & 1) != 0;
        }

        private void SetBit(int start, int index, int count, bool value)
        {
            if ((uint)index >= count)
                throw new ArgumentOutOfRangeException(nameof(index));

            int at = _offset + start + (index >> 3);
            byte mask = (byte)(1 << (index & 7));
            _data[at] = value ? (byte)(_data[at] | mask) : (byte)(_data[at] & ~mask);
        }

        private static void CheckDeck(int deck)
        {
            if ((uint)deck >= DeckCount)
                throw new ArgumentOutOfRangeException(nameof(deck));
        }
    }

    /// <summary>
    /// The section at 0x1B70: a version-like pair, then 6 blocks of 302 dwords. Each block is a value, a
    /// profile-wide number, and 50 entries of 6 dwords. What the entries record is not decoded yet, so they are
    /// exposed as they are.
    /// </summary>
    public sealed class ProfileSection
    {
        public const int BlockCount = 6;
        public const int BlockSize = 1208;
        public const int EntryCount = 50;
        public const int EntryDwords = 6;

        private readonly byte[] _data;
        private readonly int _offset;

        internal ProfileSection(byte[] data, int offset)
        {
            _data = data;
            _offset = offset;
        }

        public Span<byte> Raw => _data.AsSpan(_offset, SaveFile.SectionSize);

        public uint GetBlockDword(int block, int index)
        {
            if ((uint)block >= BlockCount)
                throw new ArgumentOutOfRangeException(nameof(block));
            if ((uint)index >= BlockSize / 4)
                throw new ArgumentOutOfRangeException(nameof(index));

            return BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_offset + 8 + BlockSize * block + 4 * index));
        }

        public void SetBlockDword(int block, int index, uint value)
        {
            if ((uint)block >= BlockCount)
                throw new ArgumentOutOfRangeException(nameof(block));
            if ((uint)index >= BlockSize / 4)
                throw new ArgumentOutOfRangeException(nameof(index));

            BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(_offset + 8 + BlockSize * block + 4 * index), value);
        }

        /// <summary>The 6 dwords of an entry (dword 2 onward of the block).</summary>
        public uint[] GetEntry(int block, int entry)
        {
            if ((uint)entry >= EntryCount)
                throw new ArgumentOutOfRangeException(nameof(entry));

            var values = new uint[EntryDwords];
            for (int i = 0; i < EntryDwords; i++)
                values[i] = GetBlockDword(block, 2 + entry * EntryDwords + i);
            return values;
        }
    }

    /// <summary>
    /// A deck in the save (32 of them) and the deck part of a duelist slot. Fixed 304 byte layout:
    /// name (33 UTF-16 chars), 3 counts, then room for 60 main, 15 extra and 15 side cards.
    /// </summary>
    public sealed class DeckSlot
    {
        public const int NameBytes = 66;
        public const int MainCapacity = 60;
        public const int ExtraCapacity = 15;
        public const int SideCapacity = 15;

        private const int CountsOffset = 66;
        private const int MainOffset = 72;
        private const int ExtraOffset = MainOffset + 2 * MainCapacity;
        private const int SideOffset = ExtraOffset + 2 * ExtraCapacity;
        private const int CharacterOffset = 288;

        private readonly byte[] _data;
        private readonly int _offset;

        internal DeckSlot(byte[] data, int offset)
        {
            _data = data;
            _offset = offset;
        }

        public string Name
        {
            get => Encoding.Unicode.GetString(_data, _offset, NameBytes).Split('\0')[0];
            set
            {
                byte[] bytes = Encoding.Unicode.GetBytes(value);
                if (bytes.Length > NameBytes - 2)
                    throw new ArgumentException($"A deck name is at most {(NameBytes - 2) / 2} characters.", nameof(value));

                Array.Clear(_data, _offset, NameBytes);
                bytes.CopyTo(_data, _offset);
            }
        }

        public bool IsEmpty => MainCount == 0 && ExtraCount == 0 && SideCount == 0 && Name.Length == 0;

        public int MainCount => Count(0);
        public int ExtraCount => Count(1);
        public int SideCount => Count(2);

        public List<ushort> Main => Read(MainOffset, MainCount);
        public List<ushort> Extra => Read(ExtraOffset, ExtraCount);
        public List<ushort> Side => Read(SideOffset, SideCount);

        public void SetCards(IReadOnlyList<ushort> main, IReadOnlyList<ushort> extra, IReadOnlyList<ushort> side)
        {
            if (main.Count > MainCapacity || extra.Count > ExtraCapacity || side.Count > SideCapacity)
                throw new ArgumentException($"A deck holds at most {MainCapacity} main, {ExtraCapacity} extra and {SideCapacity} side cards.");

            Array.Clear(_data, _offset + MainOffset, 2 * (MainCapacity + ExtraCapacity + SideCapacity));
            Write(MainOffset, main);
            Write(ExtraOffset, extra);
            Write(SideOffset, side);

            BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(_offset + CountsOffset), (ushort)main.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(_offset + CountsOffset + 2), (ushort)extra.Count);
            BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(_offset + CountsOffset + 4), (ushort)side.Count);
        }

        /// <summary>The character (0 to 239) whose art the deck uses. The game rejects saves where this is 240 or more.</summary>
        public uint Character
        {
            get => BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan(_offset + CharacterOffset));
            set => BinaryPrimitives.WriteUInt32LittleEndian(_data.AsSpan(_offset + CharacterOffset), value);
        }

        private int Count(int section) => BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_offset + CountsOffset + 2 * section));

        private List<ushort> Read(int start, int count)
        {
            var cards = new List<ushort>(count);
            for (int i = 0; i < count; i++)
                cards.Add(BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_offset + start + 2 * i)));
            return cards;
        }

        private void Write(int start, IReadOnlyList<ushort> cards)
        {
            for (int i = 0; i < cards.Count; i++)
                BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(_offset + start + 2 * i), cards[i]);
        }
    }

    /// <summary>
    /// One of the 5 duelist slots (644 bytes). Decoded from the game's init and validation code: a deck at +41
    /// and the pool of card ids the duelist can still hand out at +349 (up to 90, count at +529). The remaining
    /// fields are exposed raw.
    /// </summary>
    public sealed class DuelistSlot
    {
        public const int RewardCapacity = 90;

        private const int DeckOffset = 41;
        private const int RewardsOffset = 349;
        private const int RewardCountOffset = 529;

        private readonly byte[] _data;
        private readonly int _offset;

        internal DuelistSlot(byte[] data, int offset)
        {
            _data = data;
            _offset = offset;
            Deck = new DeckSlot(data, offset + DeckOffset);
        }

        public DeckSlot Deck { get; }

        public Span<byte> Raw => _data.AsSpan(_offset, SaveFile.DuelistSlotSize);

        public int RewardCount
        {
            get => BinaryPrimitives.ReadInt32LittleEndian(_data.AsSpan(_offset + RewardCountOffset));
            set => BinaryPrimitives.WriteInt32LittleEndian(_data.AsSpan(_offset + RewardCountOffset), value);
        }

        /// <summary>Konami ids. The game only accepts ids from 3900 to 14968 here, or 0 / 65535 for an empty entry.</summary>
        public ushort GetReward(int index)
        {
            if ((uint)index >= RewardCapacity)
                throw new ArgumentOutOfRangeException(nameof(index));

            return BinaryPrimitives.ReadUInt16LittleEndian(_data.AsSpan(_offset + RewardsOffset + 2 * index));
        }

        public void SetReward(int index, ushort id)
        {
            if ((uint)index >= RewardCapacity)
                throw new ArgumentOutOfRangeException(nameof(index));

            BinaryPrimitives.WriteUInt16LittleEndian(_data.AsSpan(_offset + RewardsOffset + 2 * index), id);
        }
    }

    /// <summary>The API this library had before. Kept so existing callers keep working.</summary>
    public static class SaveGame
    {
        public class Deck_Entry
        {
            public string Deck_Name;

            public short Number_Of_Cards_In_Deck;
            public short Number_Of_Cards_In_Extra_Deck;
            public short Number_Of_Cards_In_Side_Deck;

            public List<short> Cards_In_Deck = [];
            public List<short> Cards_In_Extra_Deck = [];
            public List<short> Cards_In_Side_Deck = [];

            public Deck_Entry(string Deck_Name, short Number_Of_Cards_In_Deck, short Number_Of_Cards_In_Extra_Deck, short Number_Of_Cards_In_Side_Deck, List<short> Cards_In_Deck, List<short> Cards_In_Extra_Deck, List<short> Cards_In_Side_Deck)
            {
                this.Deck_Name = Deck_Name;

                this.Number_Of_Cards_In_Deck = Number_Of_Cards_In_Deck;
                this.Number_Of_Cards_In_Extra_Deck = Number_Of_Cards_In_Extra_Deck;
                this.Number_Of_Cards_In_Side_Deck = Number_Of_Cards_In_Side_Deck;

                this.Cards_In_Deck.AddRange(Cards_In_Deck);
                this.Cards_In_Extra_Deck.AddRange(Cards_In_Extra_Deck);
                this.Cards_In_Side_Deck.AddRange(Cards_In_Side_Deck);
            }
        }

        /// <summary>The loaded save. Null until <see cref="Load"/> is called.</summary>
        public static SaveFile? Current { get; private set; }

        public static List<Deck_Entry>? Decks;

        public static void Load(string Path)
        {
            Current = SaveFile.Load(Path);
            Decks = new List<Deck_Entry>();
        }

        public static void Load_Decks()
        {
            if (Current == null)
                return;

            Decks = new List<Deck_Entry>();
            foreach (var deck in Current.Decks)
            {
                Decks.Add(new Deck_Entry(
                    deck.Name,
                    (short)deck.MainCount, (short)deck.ExtraCount, (short)deck.SideCount,
                    deck.Main.Select(card => (short)card).ToList(),
                    deck.Extra.Select(card => (short)card).ToList(),
                    deck.Side.Select(card => (short)card).ToList()));
            }
        }
    }
}
