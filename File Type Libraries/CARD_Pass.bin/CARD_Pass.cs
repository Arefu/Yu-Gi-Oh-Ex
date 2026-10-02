using System.Buffers.Binary;

namespace CARD_Pass
{
    /// <summary>bin\CARD_Pass.bin: one little-endian u32 password per internal id (Blue-Eyes, internal 101, = 89631139; tokens are 0).</summary>
    public static class Card_Pass
    {
        public const string GamePath = @"bin\CARD_Pass.bin";

        public static List<int> _Passwords = [];

        public static List<int> Load(string Path)
        {
            if (Path == "-1")
                return _Passwords;
            _Passwords = Parse(File.ReadAllBytes(Path));
            return _Passwords;
        }

        public static List<int> Parse(byte[] data)
        {
            var passwords = new List<int>(data.Length / 4);
            for (int at = 0; at + 4 <= data.Length; at += 4)
                passwords.Add(BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)));
            return passwords;
        }

        public static byte[] ToBytes(IReadOnlyList<int> passwords)
        {
            var data = new byte[passwords.Count * 4];
            for (int i = 0; i < passwords.Count; i++)
                BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(i * 4), passwords[i]);
            return data;
        }

        public static void Save(string path) => File.WriteAllBytes(path, ToBytes(_Passwords));
    }
}
