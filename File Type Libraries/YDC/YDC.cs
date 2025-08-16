namespace Types
{
    public static class YDC
    {
        public static short MAX_NUMBER_OF_CARDS_IN_DECK = 60;
        private static short MAX_NUMBER_OF_CARDS_IN_SIDE_DECK = 15;
        private static short MAX_NUMBER_OF_CARDS_IN_EXTRA_DECK = 15;
        private static string DECK_NAME { get; set; }

        public static short NumberOfCardsInDeck;
        public static short NumberOfCardsInSideDeck;
        public static short NumberOfCardsInExtraDeck;

        public static List<short> CardsInMainDeck = [];
        public static List<short> CardsInSideDeck = [];
        public static List<short> CardsInExtraDeck = [];

        public static List<short> Load(string path)
        {
            DECK_NAME = new FileInfo(path).Name;
            short CurrentlyRead = 0;

            using (var Reader = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                Reader.BaseStream.Position = 0x8;
                MAX_NUMBER_OF_CARDS_IN_DECK = Reader.ReadInt16();
                while (CurrentlyRead < MAX_NUMBER_OF_CARDS_IN_DECK && Reader.BaseStream.Position != Reader.BaseStream.Length)
                {
                    CardsInMainDeck.Add(Reader.ReadInt16());
                    CurrentlyRead++;
                }
            }
            return CardsInMainDeck;
        }

        public static void Save()
        {
            if (string.IsNullOrEmpty(DECK_NAME)) return;

            using var writer = new BinaryWriter(File.Open(DECK_NAME, FileMode.Create));
            writer.Write(new byte[8]);

            writer.Write((short)CardsInMainDeck.Count);

            foreach (var card in CardsInMainDeck)
                writer.Write(card);
        }
    }
}