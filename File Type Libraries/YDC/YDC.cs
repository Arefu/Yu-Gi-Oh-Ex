using System.Diagnostics;

namespace Types
{
    public static class YDC
    {
        public static short MAX_NUMBER_OF_CARDS_IN_DECK = 60;
        private static short MAX_NUMBER_OF_CARDS_IN_SIDE_DECK = 15;
        private static short MAX_NUMBER_OF_CARDS_IN_EXTRA_DECK = 15;
        private static string DECK_NAME { get; set; }

        private static short _numberOfCardsInDeck;
        private static short _numebrOfCardsInSideDeck;
        private static short _numberOfCardsInExtraDeck;

        public static short NumberOfCardsInDeck
        {
            get => _numberOfCardsInDeck;
            set
            {
                if (_numberOfCardsInDeck != value)
                    _numberOfCardsInDeck = value;
            }
        }

        public static short NumberOfCardsInSideDeck
        {
            get { return (short)CardsInSideDeck.Count; }
            set
            {
                if (_numebrOfCardsInSideDeck != value)
                    _numebrOfCardsInSideDeck = value;
            }
        }

        public static short NumberOfCardsInExtraDeck
        {
            get { return (short)CardsInExtraDeck.Count; }
            set
            {
                if (_numberOfCardsInExtraDeck != value)
                    _numberOfCardsInExtraDeck = value;
            }
        }

        public static List<short> CardsInMainDeck = [];
        public static List<short> CardsInSideDeck = [];
        public static List<short> CardsInExtraDeck = [];

        public static void Load(string path)
        {
            CardsInMainDeck.Clear();
            CardsInSideDeck.Clear();
            CardsInExtraDeck.Clear();

            DECK_NAME = new FileInfo(path).Name;
            short CurrentlyRead = 0;

            using (var Reader = new BinaryReader(File.Open(path, FileMode.Open)))
            {
                while (Reader.BaseStream.Position != Reader.BaseStream.Length)
                {
                    Reader.BaseStream.Position = 0x8;
                    _numberOfCardsInDeck = Reader.ReadInt16();
                    while (CurrentlyRead < _numberOfCardsInDeck)
                    {
                        CardsInMainDeck.Add(Reader.ReadInt16());
                        CurrentlyRead++;
                    }
                    if (Reader.BaseStream.Position == Reader.BaseStream.Length) break;
                    CurrentlyRead = 0;
                    _numberOfCardsInExtraDeck = Reader.ReadInt16();
                    while (CurrentlyRead < _numberOfCardsInExtraDeck)
                    {
                        CardsInExtraDeck.Add(Reader.ReadInt16());
                        CurrentlyRead++;
                    }

                    if (Reader.BaseStream.Position == Reader.BaseStream.Length) break;
                    CurrentlyRead = 0;

                    _numebrOfCardsInSideDeck = Reader.ReadInt16();
                    while (CurrentlyRead < _numebrOfCardsInSideDeck)
                    {
                        CardsInSideDeck.Add(Reader.ReadInt16());
                        CurrentlyRead++;
                    }
                }
            }
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