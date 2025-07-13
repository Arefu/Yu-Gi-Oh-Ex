using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace PACKDATA
{
    public static class PACKDATA
    {
        public static List<short> _CommonCards = [];
        public static List<short> _RareCards = [];

        public static int _NumberOfCommonCards;
        public static int _NumberOfRareCards;

        public static void Load(string Path)
        {
            using var Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));

            if (new FileInfo(Path).Name.StartsWith("packdata") == false)
                return;

            _NumberOfCommonCards = Reader.ReadUInt16();
            _NumberOfRareCards = Reader.ReadUInt16();

            while (Reader.BaseStream.Position < Reader.BaseStream.Length)
            {
                _CommonCards.Clear();
                _RareCards.Clear();

                for (int i = 0; i < _NumberOfCommonCards; i++)
                    _CommonCards.Add(Reader.ReadInt16());

                for (int i = 0; i < _NumberOfRareCards; i++)
                    _RareCards.Add(Reader.ReadInt16());

                break; // prevent infinite loop if only one set of entries exists
            }

        }
    }
}
