using System.Text;

namespace CARD_PackID
{
    public static class Card_PackID
    {
        public static List<string> _CardNumbers = [];

        public static void Load(string Path)
        {
            using var Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));

            while (Reader.BaseStream.Position < Reader.BaseStream.Length)
            {
                var Bytes = Reader.ReadBytes(16);
  

                _CardNumbers.Add(Encoding.ASCII.GetString(Bytes));
            }
        }

        public static void Save()
        {
        }
    }
}
