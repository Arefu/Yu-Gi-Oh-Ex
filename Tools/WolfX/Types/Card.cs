using System.Collections.Specialized;

namespace WolfX.Types
{
    internal class Card
    {
        internal int _Index { get; set; }
        internal int _Id { get; set; }
        internal string? _Name { get; set; }
        internal string? _Description { get; set; }
        public int _Atk { get; set; }
        public int _Def { get; set; }
        public byte _Level { get; set; }
        public Attribute _Attribute { get; set; }

        public Type _Type { get; set; }

        public BitVector32 _Bit1 { get; set; }

        public BitVector32 _Bit2 { get; set; }

        public byte _PendulumScale1 { get; set; }

        public byte _PendulumScale2 { get; set; }
        //RelatedCards
        //EffectTags
        //NameTypes
        //SetIds

       

        

        internal bool IsCardEmpty()
        {
            return _Name?.Length == 0;
        }
    }
}