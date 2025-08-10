namespace Types
{
    public static class YDC
    {
        public static short MAX_NUMBER_OF_CARDS_IN_DECK = 60;
        private static short MAX_NUMBER_OF_CARDS_IN_SIDE_DECK = 15;
        private static short MAX_NUMBER_OF_CARDS_IN_EXTRA_DECK = 15;

        public static short NumberOfCardsInDeck;
        public static short NumberOfCardsInSideDeck;
        public static short NumberOfCardsInExtraDeck;

        public static List<short> CardsInMainDeck = [];

        public static List<short> Load(string path)
        {
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
    }
}