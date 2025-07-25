namespace PACKDATA
{
    public static class PACKDATA
    {
        public static List<short> _CommonCards = [];
        public static List<short> _RareCards = [];

        public static short _NumberOfCommonCards;
        public static short _NumberOfRareCards;

        private static string FileName;

        public static void Load(string Path)
        {
            FileName = new FileInfo(Path).Name;

            using var Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));

            if (new FileInfo(Path).Name.StartsWith("packdata") == false)
                return;

            _NumberOfCommonCards = Reader.ReadInt16();
            _NumberOfRareCards = Reader.ReadInt16();

            while (Reader.BaseStream.Position < Reader.BaseStream.Length)
            {
                _CommonCards.Clear();
                _RareCards.Clear();

                for (int i = 0; i < _NumberOfCommonCards; i++)
                    _CommonCards.Add(Reader.ReadInt16());

                for (int i = 0; i < _NumberOfRareCards; i++)
                    _RareCards.Add(Reader.ReadInt16());
            }
        }

        public static void Save()
        {
            using BinaryWriter Writer = new BinaryWriter(File.Open(FileName, FileMode.Create));

            Writer.Write(_NumberOfCommonCards);
            Writer.Write(_NumberOfRareCards);

            foreach (var Card in _CommonCards)
                Writer.Write(Card);
            foreach (var Card in _RareCards)
                Writer.Write(Card);
        }
    }
}