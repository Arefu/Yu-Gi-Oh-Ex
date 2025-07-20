using System.Runtime.CompilerServices;

namespace savegame
{
    public static class SaveGame
    {
        internal static class Save_Gmae_Constants
        {
            internal static int DECK_LIST_START_IN_FILE = 0x37C8;
            internal static int DECK_NAME_LENGTH = 66;
            internal static int MAX_NUMBER_OF_DECKS = 32;
        }

        public class Deck_Entry
        {
            public string Deck_Name;

            public short Number_Of_Cards_In_Deck;
            public short Number_Of_Cards_In_Extra_Deck;
            public short Number_Of_Cards_In_Side_Deck;

            public List<short> Cards_In_Deck = [];
            public List<short> Cards_In_Extra_Deck = [];
            public List<short> Cards_In_Side_Deck = [];

            public Deck_Entry(string Deck_Name, short Number_Of_Cards_In_Deck, short Number_Of_Cards_In_Extra_Deck, short Number_Of_Cards_In_Side_Deck, List<short> Cards_In_Deck, List<short> Cards_In_Extra_Deck, List<short> Cards_In_Side_Deck)
            {
                this.Deck_Name = Deck_Name;

                this.Number_Of_Cards_In_Deck = Number_Of_Cards_In_Deck;
                this.Number_Of_Cards_In_Extra_Deck = Number_Of_Cards_In_Extra_Deck;
                this.Number_Of_Cards_In_Side_Deck = Number_Of_Cards_In_Side_Deck;

                this.Cards_In_Deck.AddRange(Cards_In_Deck);
                this.Cards_In_Extra_Deck.AddRange(Cards_In_Extra_Deck);
                this.Cards_In_Side_Deck.AddRange(Cards_In_Side_Deck);
            }
        }

        internal static BinaryReader? _Reader;
        public static List<Deck_Entry>? Decks;

        public static void Load(string Path)
        {
            _Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));
            Decks = new List<Deck_Entry>();

        }

        public static void Load_Decks()
        {
            if (_Reader == null)
                return;

            List<short> Cards_In_Deck = [];
            List<short> Cards_In_Extra_Deck = [];
            List<short> Cards_In_Side_Deck = [];

            _Reader.BaseStream.Position = Save_Gmae_Constants.DECK_LIST_START_IN_FILE;
            for (int Deck = 1; Deck < Save_Gmae_Constants.MAX_NUMBER_OF_DECKS; Deck++)
            {
                Cards_In_Deck.Clear();
                Cards_In_Extra_Deck.Clear();
                Cards_In_Side_Deck.Clear();


                var Deck_Name = System.Text.Encoding.Unicode.GetString(_Reader.ReadBytes(Save_Gmae_Constants.DECK_NAME_LENGTH));

                var Number_Of_Cards_In_Deck = _Reader.ReadInt16();
                var Number_Of_Cards_In_Extra_Deck = _Reader.ReadInt16();
                var Number_Of_Cards_In_Side_Deck = _Reader.ReadInt16();

                for (short X = 0; X < Number_Of_Cards_In_Deck; X++)
                    Cards_In_Deck.Add(_Reader.ReadInt16());
                for (short Y = 0; Y < Number_Of_Cards_In_Extra_Deck; Y++)
                    Cards_In_Extra_Deck.Add(_Reader.ReadInt16());
                for (short Z = 0; Z < Number_Of_Cards_In_Side_Deck; Z++)
                    Cards_In_Side_Deck.Add(_Reader.ReadInt16());

                Decks.Add(new Deck_Entry(Deck_Name, Number_Of_Cards_In_Deck, Number_Of_Cards_In_Extra_Deck, Number_Of_Cards_In_Side_Deck, Cards_In_Deck, Cards_In_Extra_Deck, Cards_In_Side_Deck));
            }
        }
    }
}
