namespace CARD_Same
{
    public enum TYPE
    {
        ALWAYS =0,
        EFFECT = 256
    }
    public class Similar_Card(short primaryCard, short targetCard, TYPE similarityType)
    {
        /// <summary>
        /// This determines if the card is always the Target Card, or only based through the effect.
        /// </summary>
        public TYPE SimilarityType = similarityType;

        /// <summary>
        /// This card is the Primary Card that will act like the Target Card
        /// </summary>
        public short PrimaryCard = primaryCard;

        /// <summary>
        /// This is the card the Primary Card is acting as.
        /// </summary>
        public short TargetCard = targetCard;
    }

    public static class Card_Same
    {
        public static List<Similar_Card> _SimilarCards = new List<Similar_Card>();

        public static List<Similar_Card> Load(string Path)
        {
            var SimilarCards = new List<Similar_Card>();
            using var Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read));

            while(Reader.BaseStream.Position < Reader.BaseStream.Length)
            {
                SimilarCards.Add(new Similar_Card(Reader.ReadInt16(), Reader.ReadInt16(), (TYPE)Reader.ReadInt16()));
            }

            _SimilarCards.AddRange(SimilarCards);
            return SimilarCards;
        }



    }
}
