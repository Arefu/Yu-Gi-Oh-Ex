using System.Buffers.Binary;
using System.Text;

namespace Types
{
    public class BNDString
    {
        public int Start; //This is the only value provided in the BND File.
        public int Length; //What is the NEXT String - THIS String's value, that's the length of THIS string.
        public string String; //Go to Start, Read Length, That's the String!

        public BNDString(int Start, int Length, string String)
        {
            this.Start = Start;
            this.Length = Length;
            this.String = String;
        }
    }

    /// <summary>
    /// strings\Strings_STEAM_&lt;lang&gt;.BND, the UI strings: a big-endian u32 count, then a big-endian u32 offset per string, then the strings
    /// one after another in UTF-16BE, each with a terminating 0. A string runs to the next one's offset (the last one to the end of the file).
    /// </summary>
    public static class BND
    {
        public static string GamePath(char language) => $@"strings\Strings_STEAM_{char.ToUpperInvariant(language)}.BND";

        public static List<BNDString> Load(string path) => Parse(File.ReadAllBytes(path));

        public static List<BNDString> Parse(byte[] data)
        {
            var strings = new List<BNDString>();
            if (data.Length < 8)
                return strings;
            int first = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
            int count = Math.Max(0, (first - 4) / 4);
            for (int i = 0; i < count; i++)
            {
                int start = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4 + i * 4));
                int end = i + 1 < count ? (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8 + i * 4)) : data.Length;
                start = Math.Clamp(start, 0, data.Length);
                end = Math.Clamp(end, start, data.Length);
                strings.Add(new BNDString(start, end - start, Encoding.BigEndianUnicode.GetString(data, start, end - start).TrimEnd('\0')));
            }
            return strings;
        }

        public static byte[] ToBytes(List<BNDString> strings)
        {
            var encoded = strings.Select(s => Encoding.BigEndianUnicode.GetBytes(s.String + "\0")).ToList();
            var data = new byte[4 + strings.Count * 4 + encoded.Sum(e => e.Length)];
            BinaryPrimitives.WriteUInt32BigEndian(data, (uint)strings.Count);
            int at = 4 + strings.Count * 4;
            for (int i = 0; i < encoded.Count; i++)
            {
                BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4 + i * 4), (uint)at);
                encoded[i].CopyTo(data, at);
                at += encoded[i].Length;
            }
            return data;
        }

        public static void Save(string path, List<BNDString> strings) => File.WriteAllBytes(path, ToBytes(strings));
    }
}
