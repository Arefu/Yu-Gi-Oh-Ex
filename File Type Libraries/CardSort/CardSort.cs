namespace Types
{
    /// <summary>
    /// A language's card name order: bin/CARD_Sort_#.bin and bin/CARD_Sort2_#.bin, two u16 arrays of the internal card count (10166),
    /// loaded by YGO::CARDS::Setup_CardPropTable (card database +0x40 and +0x50):
    ///
    ///   CARD_Sort2_#   position -> internal id: the cards in name order (<see cref="Order"/>)
    ///   CARD_Sort_#    internal id -> position: each card's rank, the inverse (<see cref="Rank"/>)
    ///
    /// Both are permutations of 0..count-1 and internal id 0 (no card) is always first. YGO::CARDS::Get_NameSortRank (0x14076D5B0) gives
    /// Setup_FullCardProps the rank, stored at FULL_CARD_PROPS +0x18 to sort the trunk by name. The order is case-insensitive ordinal on
    /// the language's names (E, F, G, I, S match it but for a handful of hand-placed cards); Japanese (J, and R = Japanese with ruby) sorts
    /// by the reading instead, so <see cref="Rebuild"/> is for the Latin-script languages.
    /// </summary>
    public sealed class CardNameSort
    {
        public static readonly char[] Languages = ['E', 'F', 'G', 'I', 'J', 'R', 'S'];

        /// <summary>The languages sorted by name as written (the others sort by a reading the files don't hold here).</summary>
        public static readonly char[] RebuildableLanguages = ['E', 'F', 'G', 'I', 'S'];

        public const string GameFolder = "bin";

        /// <summary>Position -> internal id (CARD_Sort2).</summary>
        public ushort[] Order { get; private set; } = [];

        /// <summary>Internal id -> position (CARD_Sort).</summary>
        public ushort[] Rank { get; private set; } = [];

        public int Count => Order.Length;

        public static string SortName(char language) => $"CARD_Sort_{char.ToUpperInvariant(language)}.bin";

        public static string Sort2Name(char language) => $"CARD_Sort2_{char.ToUpperInvariant(language)}.bin";

        public static CardNameSort Load(string binFolder, char language) =>
            Parse(File.ReadAllBytes(Path.Combine(binFolder, SortName(language))), File.ReadAllBytes(Path.Combine(binFolder, Sort2Name(language))));

        public static CardNameSort Parse(byte[] sort, byte[] sort2)
        {
            if (sort.Length != sort2.Length || sort.Length % 2 != 0)
                throw new InvalidDataException("CARD_Sort and CARD_Sort2 should be u16 arrays of the same length.");
            var table = new CardNameSort { Rank = ToU16(sort), Order = ToU16(sort2) };
            return table;
        }

        private static ushort[] ToU16(byte[] bytes)
        {
            var values = new ushort[bytes.Length / 2];
            Buffer.BlockCopy(bytes, 0, values, 0, bytes.Length);
            return values;
        }

        private static byte[] ToBytes(ushort[] values)
        {
            var bytes = new byte[values.Length * 2];
            Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public (byte[] Sort, byte[] Sort2) ToBytes() => (ToBytes(Rank), ToBytes(Order));

        public void Save(string binFolder, char language)
        {
            var (sort, sort2) = ToBytes();
            File.WriteAllBytes(Path.Combine(binFolder, SortName(language)), sort);
            File.WriteAllBytes(Path.Combine(binFolder, Sort2Name(language)), sort2);
        }

        /// <summary>Whether the two arrays are a permutation and its inverse (what the game expects).</summary>
        public bool IsConsistent()
        {
            if (Order.Length != Rank.Length)
                return false;
            var seen = new bool[Order.Length];
            for (int p = 0; p < Order.Length; p++)
            {
                int id = Order[p];
                if (id >= Rank.Length || seen[id] || Rank[id] != p)
                    return false;
                seen[id] = true;
            }
            return true;
        }

        /// <summary>How many neighbours in <see cref="Order"/> are out of name order (0 = sorted), by the names of the internal ids.</summary>
        public int OutOfOrder(Func<int, string> nameOfInternal)
        {
            int count = 0;
            for (int p = 2; p < Order.Length; p++)
                if (StringComparer.OrdinalIgnoreCase.Compare(nameOfInternal(Order[p - 1]), nameOfInternal(Order[p])) > 0)
                    count++;
            return count;
        }

        /// <summary>
        /// Puts the cards back in name order (case-insensitive ordinal, as the game's files are) and rebuilds both arrays, moving as few
        /// cards as it can: the longest run of cards already in order stays as it is (so the game's own hand-placed cards and accent
        /// ordering are kept), and only the others (e.g. renamed cards) are taken out and inserted where their names go. Internal id 0
        /// stays first. Returns how many cards were moved.
        /// </summary>
        public int Rebuild(Func<int, string> nameOfInternal)
        {
            var comparer = StringComparer.OrdinalIgnoreCase;
            var previous = Order.Length > 0 ? Order : [.. Enumerable.Range(0, Rank.Length).Select(i => (ushort)i)];
            var cards = previous.Where(id => id != 0).ToList();
            var names = cards.ToDictionary(id => id, id => nameOfInternal(id));

            // longest non-decreasing subsequence by name (patience sorting): these stay where they are
            var tails = new List<int>();            // index into cards of the last card of the best run of each length
            var parent = new int[cards.Count];
            for (int i = 0; i < cards.Count; i++)
            {
                int lo = 0, hi = tails.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (comparer.Compare(names[cards[tails[mid]]], names[cards[i]]) <= 0)
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                parent[i] = lo > 0 ? tails[lo - 1] : -1;
                if (lo == tails.Count)
                    tails.Add(i);
                else
                    tails[lo] = i;
            }
            var keep = new HashSet<int>();
            for (int i = tails.Count > 0 ? tails[^1] : -1; i >= 0; i = parent[i])
                keep.Add(i);

            var sorted = new List<ushort>(cards.Count);
            for (int i = 0; i < cards.Count; i++)
                if (keep.Contains(i))
                    sorted.Add(cards[i]);
            int moved = cards.Count - sorted.Count;
            for (int i = 0; i < cards.Count; i++)
            {
                if (keep.Contains(i))
                    continue;
                string name = names[cards[i]];
                int lo = 0, hi = sorted.Count;
                while (lo < hi)
                {
                    int mid = (lo + hi) / 2;
                    if (comparer.Compare(names[sorted[mid]], name) <= 0)
                        lo = mid + 1;
                    else
                        hi = mid;
                }
                sorted.Insert(lo, cards[i]);
            }
            sorted.Insert(0, 0);
            var order = sorted.ToArray();
            var rank = new ushort[order.Length];
            for (int p = 0; p < order.Length; p++)
                rank[order[p]] = (ushort)p;
            Order = order;
            Rank = rank;
            return moved;
        }

        /// <summary>Internal id -> Konami id from CARD_IntID.bin (u16 internal id per Konami id, starting at 3900).</summary>
        public static Dictionary<int, int> KonamiIdsByInternal(byte[] intId)
        {
            var map = new Dictionary<int, int>();
            for (int i = 0; i < intId.Length / 2; i++)
            {
                int internalId = BitConverter.ToUInt16(intId, 2 * i);
                if (internalId != 0)
                    map.TryAdd(internalId, 3900 + i);
            }
            return map;
        }
    }
}
