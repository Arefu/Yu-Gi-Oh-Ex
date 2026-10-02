namespace Types
{
    /// <summary>
    /// bin/CARD_INTID.bin: Konami card id -> internal id (the index of the card's props, name, art...). One u16 per Konami id from
    /// <see cref="FirstKonamiId"/> (3900) to 14968, 0 = no card; the game's own file maps 10165 cards one to one with internal ids rising
    /// with the Konami id, and leaves 904 ids without a card. Those aren't free: the exe keeps data for them (effect rows and the like), as
    /// it does for the ghost ids 14969-15234 past the table, so they count as used; new cards go at <see cref="FirstCustomId"/> and up.
    ///
    /// YGO::CARDS::Setup_CardPropTable loads it into the card database (+0xD0) and uses it only when it is exactly <see cref="FileSize"/>
    /// bytes (otherwise the exe's built-in table stays): it's copied into YGO::CARDS::g_iInternalIDs (0x140D55480), then the reverse
    /// table g_KonamiIds (0x140D50510, internal id -> Konami id, 10166 entries) is rebuilt by asking Get_InternalIdFromKonamiId for every
    /// Konami id. Custom cards (Konami ids 15300+) never go in this file: the Yu-Gi-Oh-Cards plugin hooks Get_InternalIdFromKonamiId and
    /// gives them load indexes of their own.
    /// </summary>
    public sealed class CardIdMap
    {
        public const int FirstKonamiId = 3900;
        public const int Count = 11069;
        public const int FileSize = Count * 2;
        public const int LastKonamiId = FirstKonamiId + Count - 1;

        /// <summary>The internal id table's size in the game (internal ids 0..10165; 0 = no card).</summary>
        public const int InternalCount = 10166;

        /// <summary>The Konami ids custom cards use (the Yu-Gi-Oh-Cards plugin: kFirstExtraCardId..kLastExtraCardId).</summary>
        public const int FirstCustomId = 15300, LastCustomId = 19999;

        public const string GamePath = @"bin\CARD_INTID.bin";

        /// <summary>Internal id for Konami id FirstKonamiId + index.</summary>
        public ushort[] InternalIds { get; } = new ushort[Count];

        public static CardIdMap Load(string path) => Parse(File.ReadAllBytes(path));

        public static CardIdMap Parse(byte[] data)
        {
            if (data.Length != FileSize)
                throw new InvalidDataException($"CARD_INTID.bin is {FileSize} bytes (the game ignores any other size); this one is {data.Length}.");
            var map = new CardIdMap();
            Buffer.BlockCopy(data, 0, map.InternalIds, 0, FileSize);
            return map;
        }

        public byte[] ToBytes()
        {
            var data = new byte[FileSize];
            Buffer.BlockCopy(InternalIds, 0, data, 0, FileSize);
            return data;
        }

        public void Save(string path) => File.WriteAllBytes(path, ToBytes());

        /// <summary>The internal id of a Konami id, or 0 (no card, or outside the table).</summary>
        public int InternalOf(int konamiId) => konamiId is >= FirstKonamiId and <= LastKonamiId ? InternalIds[konamiId - FirstKonamiId] : 0;

        public void Set(int konamiId, int internalId)
        {
            if (konamiId is < FirstKonamiId or > LastKonamiId)
                throw new ArgumentOutOfRangeException(nameof(konamiId), $"Konami ids in this table are {FirstKonamiId}-{LastKonamiId}.");
            if (internalId is < 0 or >= InternalCount)
                throw new ArgumentOutOfRangeException(nameof(internalId), $"Internal ids are 0-{InternalCount - 1} (0 = no card).");
            InternalIds[konamiId - FirstKonamiId] = (ushort)internalId;
        }

        /// <summary>Internal id -> Konami id, as the game rebuilds g_KonamiIds (the first Konami id wins when two share an internal id).</summary>
        public Dictionary<int, int> KonamiByInternal()
        {
            var map = new Dictionary<int, int>();
            for (int i = 0; i < Count; i++)
                if (InternalIds[i] != 0)
                    map.TryAdd(InternalIds[i], FirstKonamiId + i);
            return map;
        }

        /// <summary>What's wrong with the table for the game: internal ids used twice or out of range, internal ids no Konami id reaches.</summary>
        public List<string> Problems()
        {
            var problems = new List<string>();
            var first = new Dictionary<int, int>();
            for (int i = 0; i < Count; i++)
            {
                int internalId = InternalIds[i];
                if (internalId == 0)
                    continue;
                if (internalId >= InternalCount)
                    problems.Add($"Konami {FirstKonamiId + i} -> internal {internalId}: past the game's {InternalCount} internal ids.");
                else if (!first.TryAdd(internalId, FirstKonamiId + i))
                    problems.Add($"Konami {FirstKonamiId + i} and {first[internalId]} both use internal {internalId} (the card data is shared; the reverse table keeps {first[internalId]}).");
            }
            int unreached = Enumerable.Range(1, InternalCount - 1).Count(id => !first.ContainsKey(id));
            if (unreached > 0)
                problems.Add($"{unreached} internal ids have no Konami id (their cards can't be reached).");
            return problems;
        }
    }
}
