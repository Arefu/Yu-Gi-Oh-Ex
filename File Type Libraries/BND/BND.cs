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

    public static class BND
    {
        internal static string _Path { get; set; }

        private static uint SwapBytes(uint Number)
        {
            Number = (Number >> 16) | (Number << 16);
            return ((Number & 0xFF00FF00) >> 8) | ((Number & 0x00FF00FF) << 8);
        }

        public static long GetEncodedSizeOfString(byte[] Bytes)
        {
            var HexString = new StringBuilder(Bytes.Length * 2);
            foreach (var Byte in Bytes) HexString.Append(Byte == 0x00 ? '.' : Convert.ToChar(Byte));

            return HexString.ToString().Length;
        }

        public static List<BNDString> Load(string Path)
        {
            _Path = new FileInfo(Path).Name;

            using var Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));
            var Strings = new List<BNDString>();

            Reader.ReadBytes(4); //000004BD - NUM OF Entries.
            int BreakOut = 0x0;
            bool FirstIteration = true;

            do
            {
                var Start = int.Parse(BitConverter.ToString(Reader.ReadBytes(4)).Replace("-", "").TrimStart('0'), System.Globalization.NumberStyles.HexNumber);
                var CurrentPosition = Reader.BaseStream.Position;
                var NextFile = int.Parse(BitConverter.ToString(Reader.ReadBytes(4)).Replace("-", "").TrimStart('0'), System.Globalization.NumberStyles.HexNumber);
                var Length = NextFile - Start;

                if (FirstIteration)
                {
                    BreakOut = Start;
                    FirstIteration = false;
                }

                Reader.BaseStream.Position = Start;

                var String = Encoding.BigEndianUnicode.GetString(Reader.ReadBytes(Length)).TrimEnd('\0');

                Strings.Add(new BNDString(Start, Length, String));

                Reader.BaseStream.Position = CurrentPosition;
            } while ((Reader.BaseStream.Position < BreakOut));
            return Strings;
        }

        public static void Save(List<BNDString> BNDStrings)
        {
            if (_Path == null)
                return;

            uint CurrentSize = (uint)BNDStrings.Count * 4 + 4;

            using var Writer = new BinaryWriter(File.Open(_Path, FileMode.OpenOrCreate, FileAccess.Write));
            {
                var NumberOfItemsInHex = BitConverter.GetBytes(SwapBytes((uint)BNDStrings.Count));
                Writer.Write(NumberOfItemsInHex);
                foreach (var String in BNDStrings)
                {
                    Writer.Write(SwapBytes(CurrentSize));
                    CurrentSize = (uint)(CurrentSize + GetEncodedSizeOfString(Encoding.BigEndianUnicode.GetBytes($"{String.String}\0")));
                }
                foreach (var String in BNDStrings)
                {
                    Writer.Write(Encoding.BigEndianUnicode.GetBytes($"{String.String}\0"));
                }
            }
        }
    }
}