using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        internal Attribute _Attribute { get; set; }

        internal Type _Type { get; set; }

        public BitVector32 _Bit1 { get; set; }

        public BitVector32 _Bit2 { get; set; }
        //RelatedCards
        //EffectTags
        //NameTypes
        //SetIds

        public enum Attribute
        {
            Unknown = 0,
            LightMonster = 1,
            DarkMonster = 2,
            WaterMonster = 3,
            FireMonster = 4,
            EarthMonster = 5,
            WindMonster = 6,
            DivineMonster = 7,
            Spell = 8,
            Trap = 9
        }

        public enum Type
        {
            Default = 0,
            Effect = 1,
            Fusion = 2,
            FusionEffect = 3,
            Ritual = 4,
            RitualEffect = 5,
            ToonEffect = 6,
            SpiritEffect = 7,
            UnionEffect = 8,
            GeminiEffect = 9,
            Token = 10,
            God = 11,
            Dummy = 12,
            Spell = 13,
            Trap = 14,
            Tuner = 15,
            TunerEffect = 16,
            Synchro = 17,
            SynchroEffect = 18,
            SynchroTunerEffect = 19,
            DarkTunerEffect = 20,
            DarkSynchroEffect = 21,
            Xyz = 22,
            XyzEffect = 23,
            FlipEffect = 24,
            Pendulum = 25,
            PendulumEffect = 26,
            EffectSp = 27,
            ToonEffectSp = 28,
            SpiritEffectSp = 29,
            TunerEffectSp = 30,
            DarkTunerEffectSp = 31,
            FlipTunerEffect = 32,
            PendulumTunerEffect = 33,
            XyzPendulumEffect = 34,
            PendulumFlipEffect = 35,
            SynchoPendulumEffect = 36,
            UnionTunerEffect = 37,
            RitualSpiritEffect = 38,
            Underscores = 39,
            Link = 43,
            AnyNormal = 37,
            AnySynchro = 38,
            AnyXyz = 39,
            AnyTuner = 40,
            AnyFusion = 41,
            AnyRitual = 42,
            AnyPendulum = 43,
            AnyFlip = 44
        }
    }
}