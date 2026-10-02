namespace PDLimits
{
    /// <summary>
    /// bin\pd_limits.bin, the Forbidden / Limited / Semi-Limited list: three blocks of a u16 count followed by that many u16 Konami ids.
    /// </summary>
    public static class PDLimits
    {
        public const string GamePath = @"bin\pd_limits.bin";

        private static List<ushort> _Forbidden = [];
        private static List<ushort> _Limited = [];
        private static List<ushort> _SemiLimited = [];

        public static void Load(string path) => Parse(File.ReadAllBytes(path));

        public static void Parse(byte[] data)
        {
            _Forbidden = [];
            _Limited = [];
            _SemiLimited = [];
            using var reader = new BinaryReader(new MemoryStream(data));
            try
            {
                foreach (var list in new[] { _Forbidden, _Limited, _SemiLimited })
                {
                    int count = reader.ReadUInt16();
                    for (int i = 0; i < count; i++)
                        list.Add(reader.ReadUInt16());
                }
            }
            catch (EndOfStreamException)
            {
                // a cut-short file (or an empty one): keep what was read
            }
        }

        public static byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            using (var writer = new BinaryWriter(stream))
            {
                foreach (var list in new[] { _Forbidden, _Limited, _SemiLimited })
                {
                    writer.Write((ushort)list.Count);
                    foreach (ushort card in list)
                        writer.Write(card);
                }
            }
            return stream.ToArray();
        }

        public static void Save(string path) => File.WriteAllBytes(path, ToBytes());

        public static List<ushort> GetForbidden() => _Forbidden;

        public static List<ushort> GetLimited() => _Limited;

        public static List<ushort> GetSemiLimited() => _SemiLimited;

        public static int GetForbiddenCount() => _Forbidden.Count;

        public static int GetLimitedCount() => _Limited.Count;

        public static int GetSemiLimitedCount() => _SemiLimited.Count;

        public static void Remove_CardFromSemiLimited(ushort CardID) => _SemiLimited.Remove(CardID);

        public static void Remove_CardFromForbidden(ushort CardID) => _Forbidden.Remove(CardID);

        public static void Remove_CardFromLimited(ushort CardID) => _Limited.Remove(CardID);

        public static void Add_CardToSemiLimited(ushort CardID) => _SemiLimited.Add(CardID);

        public static void Add_CardToForbidden(ushort CardID) => _Forbidden.Add(CardID);

        public static void Add_CardToLimited(ushort CardID) => _Limited.Add(CardID);
    }
}
