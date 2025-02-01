using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;

namespace Types
{

    public static class CARDS_INFO
    {
        public enum CARD_Language { English = 'E', French = 'F', German = 'G', Italian = 'I', Spanish = 'S', Japanese = 'J', Russian = 'R' };
        public static CARD_Language _CARD_Language = CARD_Language.English;


        public enum CARD_Attribute
        {
            Unknown = 0, LightMonster = 1, DarkMonster = 2, WaterMonster = 3, FireMonster = 4, EarthMonster = 5, WindMonster = 6, DivineMonster = 7, Spell = 8, Trap = 9
        }

        public enum CARD_Type
        {
            Default = 0, Effect = 1, Fusion = 2, FusionEffect = 3, Ritual = 4, RitualEffect = 5, ToonEffect = 6, SpiritEffect = 7, UnionEffect = 8, GeminiEffect = 9, Token = 10,
            God = 11, Dummy = 12, Spell = 13, Trap = 14, Tuner = 15, TunerEffect = 16, Synchro = 17, SynchroEffect = 18, SynchroTunerEffect = 19, DarkTunerEffect = 20, DarkSynchroEffect = 21,
            Xyz = 22, XyzEffect = 23, FlipEffect = 24, Pendulum = 25, PendulumEffect = 26, EffectSp = 27, ToonEffectSp = 28, SpiritEffectSp = 29, TunerEffectSp = 30, DarkTunerEffectSp = 31,
            FlipTunerEffect = 32, PendulumTunerEffect = 33, XyzPendulumEffect = 34, PendulumFlipEffect = 35, SynchoPendulumEffect = 36, UnionTunerEffect = 37, RitualSpiritEffect = 38,
            Underscores = 39, AnyTuner = 40, AnyFusion = 41, AnyRitual = 42, Link = 43, AnyFlip = 44

            //AnyNormal = 37,
            //AnySynchro = 38,
            //AnyXyz = 39,
            //AnyPendulum = 43,
        }
    }

    public class CARD_Card
    {
        public int _Index;

        public string Name;
        public string Desc;

        public int Attack;
        public int Defense;
        public int Level;
        public int ID;

        public int Ico;
        public int Kind;
        public int SecondUnk;

        public byte PEND_Scale1;
        public byte PEND_Scale2;

        public CARDS_INFO.CARD_Attribute Attribute;
        public CARDS_INFO.CARD_Type Type;

        public BitVector32 First;
        public BitVector32 Second;

    }

        public static class CARDS_Cards
    {
        public static string? CARD_Indx_File;
        public static string? CARD_Name_File;
        public static string? CARD_Desc_File;
        public static string? CARD_Prop_File;

        public static BinaryReader? IndxReader;
        public static BinaryReader? NameReader;
        public static BinaryReader? DescReader;
        public static BinaryReader? PropReader;

        public static BinaryWriter? IndxWriter;
        public static BinaryWriter? NameWriter;
        public static BinaryWriter? DescWriter;
        public static BinaryWriter? PropWriter;

        public static bool Ready = false;

        public static List<CARD_Card> Cards = new();

        private static Dictionary<uint, string> Names = new();
        private static Dictionary<uint, string> Descs = new();
        private static void Refresh()
        {
            Names.Clear();
            Descs.Clear();
            Cards.Clear();

            IndxReader.BaseStream.Position = 0;
            NameReader.BaseStream.Position = 0;
            DescReader.BaseStream.Position = 0;
        }


        public static bool Setup_CardBinder(string Indx_File, CARDS_INFO.CARD_Language Language = CARDS_INFO.CARD_Language.English)
        {
            if (File.Exists(Indx_File) != true)
                return false;

            var IndxDir = Path.GetDirectoryName(Indx_File);
            if (File.Exists(IndxDir + $"\\CARD_Name_{(char)Language}.bin") != true)
                return false;
            if (File.Exists(IndxDir + $"\\CARD_Desc_{(char)Language}.bin") != true)
                return false;
            if (File.Exists(IndxDir + $"\\CARD_Prop.bin") != true)
                return false;

            CARD_Indx_File = Indx_File;
            CARD_Name_File = IndxDir + $"\\CARD_Name_{(char)Language}.bin";
            CARD_Desc_File = IndxDir + $"\\CARD_Desc_{(char)Language}.bin";
            CARD_Prop_File = IndxDir + $"\\CARD_Prop.bin";

            IndxReader = new BinaryReader(File.Open(CARD_Indx_File, FileMode.Open, FileAccess.Read, FileShare.Read));
            NameReader = new BinaryReader(File.Open(CARD_Name_File, FileMode.Open, FileAccess.Read, FileShare.Read));
            DescReader = new BinaryReader(File.Open(CARD_Desc_File, FileMode.Open, FileAccess.Read, FileShare.Read));
            PropReader = new BinaryReader(File.Open(CARD_Prop_File, FileMode.Open, FileAccess.Read, FileShare.Read));

            Ready = true;

            return true;
        }

        public static bool Close_CardBinder()
        {
          
            IndxReader?.Close();
            NameReader?.Close();
            DescReader?.Close();
            PropReader?.Close();

            Ready = false;

            return true;
        }


        public static void LoadCardInfo()
        {
            if (Ready != true)
                return;
            if (Ready == true)
                Refresh();

            uint Offset = 0x0;
            string Data = "";

            while (NameReader.BaseStream.Position != NameReader.BaseStream.Length)
            {
                Offset = (uint)NameReader.BaseStream.Position;
                Data = NameReader.ReadNullTerminatedString(Encoding.Unicode);

                Names.Add(Offset, Data);
            }

            while (DescReader.BaseStream.Position != DescReader.BaseStream.Length)
            {
                Offset = (uint)DescReader.BaseStream.Position;
                Data = DescReader.ReadNullTerminatedString(Encoding.Unicode);

                Descs.Add(Offset, Data);
            }
        }

        public static void LoadCardProps()
        {
            do
            {
                var NameOffset = IndxReader.ReadUInt32();
                var DescOffset = IndxReader.ReadUInt32();

                var First = new BitVector32((int)PropReader.ReadUInt32());
                var Second = new BitVector32((int)PropReader.ReadUInt32());

                CARD_Card Card = new();

                var CardId = BitVector32.CreateSection(16383);
                var CardAtk = BitVector32.CreateSection(511, CardId);
                var CardDef = BitVector32.CreateSection(511, CardAtk);

                var SecondQuadUnk = BitVector32.CreateSection(1);
                var Kind = BitVector32.CreateSection(63, SecondQuadUnk);
                var Attribute = BitVector32.CreateSection(15, Kind);
                var MonsterLevel = BitVector32.CreateSection(15, Attribute);
                var Ico = BitVector32.CreateSection(7, MonsterLevel);
                var Type = BitVector32.CreateSection(31, Ico);
                var LeftScale = BitVector32.CreateSection(15, Type);
                var RightScale = BitVector32.CreateSection(15, LeftScale);

                Card.SecondUnk = Second[SecondQuadUnk];

                Card.ID = (short)First[CardId];
                Card.Name = Names[NameOffset];
                Card.Desc = Descs[DescOffset];
                Card.Attack = (First[CardAtk] * 10);
                Card.Defense = (First[CardDef] * 10);

                if(Card.Attack == 5110)
                    Card.Attack = -1;
                if (Card.Defense == 5110)
                    Card.Defense = -1;

            
                Card.Kind = Second[Kind];
                Card.Attribute = (CARDS_INFO.CARD_Attribute)Second[Attribute];
                Card.Level = (byte)Second[MonsterLevel];
                Card.Ico = Second[Ico];
                Card.Type = (CARDS_INFO.CARD_Type)Second[Kind];
                Card.PEND_Scale1 = (byte)Second[LeftScale];
                Card.PEND_Scale2 = (byte)Second[RightScale];

                Card.First = First;
                Card.Second = Second;

                Cards.Add(Card);
            } while (PropReader.BaseStream.Position != PropReader.BaseStream.Length);

        }

        public static void SaveCardInfo()
        {
            if(Ready != true)
                return;

            //Setup the Binary Writers
            using var NameWriter = new BinaryWriter(File.Open(new FileInfo(CARD_Name_File).Name, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write));
            using var DescWriter = new BinaryWriter(File.Open(new FileInfo(CARD_Desc_File).Name, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write));
            using var IndxWriter = new BinaryWriter(File.Open(new FileInfo(CARD_Indx_File).Name, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write));
            NameWriter.BaseStream.Position = 0x4;
            DescWriter.BaseStream.Position = 0x4;

            bool FirstPass = true;

            PropWriter = new BinaryWriter(File.Open("CARD_Prop.bin", FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write));
            foreach (var Card in Cards)
            {
                BitVector32 bit1 = new BitVector32(Card.First);
                BitVector32.Section bit1_mrk = BitVector32.CreateSection(16383);// offset 0, mask 16383 (0x3FFF)
                BitVector32.Section bit1_attack = BitVector32.CreateSection(511, bit1_mrk);// offset 14, mask 511 (0x1FF)
                BitVector32.Section bit1_defence = BitVector32.CreateSection(511, bit1_attack);// offset 23, mask 511 (0x1FF)
                                                                                               // All bits used

                BitVector32 bit2 = new BitVector32(Card.Second);
                BitVector32.Section bit2_exist = BitVector32.CreateSection(1);// offset 0, mask 1 (0x1)
                BitVector32.Section bit2_kind = BitVector32.CreateSection(63, bit2_exist);// offset 1, mask 63 (0x3F)
                BitVector32.Section bit2_attr = BitVector32.CreateSection(15, bit2_kind);// offset 7, mask 15 (0xF)
                BitVector32.Section bit2_level = BitVector32.CreateSection(15, bit2_attr);// offset 11, mask 15 (0xF)
                BitVector32.Section bit2_icon = BitVector32.CreateSection(7, bit2_level);// offset 15, mask 7 (0x7)
                BitVector32.Section bit2_type = BitVector32.CreateSection(31, bit2_icon);// offset 18, mask 31 (0x1F)
                BitVector32.Section bit2_scaleL = BitVector32.CreateSection(15, bit2_type);// offset 23, mask 15 (0xF)
                BitVector32.Section bit2_scaleR = BitVector32.CreateSection(15, bit2_scaleL);// offset 27, mask 15 (0xF)

                BitVector32.Section bit2_unused = BitVector32.CreateSection(1, bit2_scaleR);// offset 31, mask 1

                bit1[bit1_mrk] = Card.ID;
                bit1[bit1_attack] = Card.Attack / 10;
                bit1[bit1_defence] = Card.Defense / 10;

                bit2[bit2_exist] = Card.SecondUnk;
                bit2[bit2_kind] = Card.Kind;
                bit2[bit2_attr] = (int)Card.Attribute;
                bit2[bit2_level] = Card.Level;
                bit2[bit2_icon] = Card.Ico;
                bit2[bit2_type] = (int)Card.Type;
                bit2[bit2_scaleL] = Card.PEND_Scale1;
                bit2[bit2_scaleR] = Card.PEND_Scale2;
                bit2[bit2_unused] = 0;

                PropWriter.Write((uint)bit1.Data);
                PropWriter.Write((uint)bit2.Data);
            }
        }

        public static void SaveCardProps()
        {
            if (Ready != true)
                return;

            using var PropWriter = new BinaryWriter(File.Open("CARD_Prop.bin", FileMode.OpenOrCreate, FileAccess.Write, FileShare.Write));
            PropWriter.BaseStream.Position = 0x0;

            foreach (var Card in Cards)
            {
                var First = new BitVector32();
                var Second = new BitVector32();

                var CardId = BitVector32.CreateSection(16383);
                var CardAtk = BitVector32.CreateSection(511, CardId);
                var CardDef = BitVector32.CreateSection(511, CardAtk);

                var SecondQuadUnk = BitVector32.CreateSection(1);
                var Kind = BitVector32.CreateSection(63, SecondQuadUnk);
                var Attribute = BitVector32.CreateSection(15, Kind);
                var MonsterLevel = BitVector32.CreateSection(15, Attribute);
                var Ico = BitVector32.CreateSection(7, MonsterLevel);
                var Type = BitVector32.CreateSection(31, Ico);
                var LeftScale = BitVector32.CreateSection(15, Type);
                var RightScale = BitVector32.CreateSection(15, LeftScale);
                var bit2_unused = BitVector32.CreateSection(1, RightScale);
                // Populate First
                First[CardId] = Card.ID;
                First[CardAtk] = Card.Attack / 10;
                First[CardDef] = Card.Defense / 10;

                // Populate Second
                Second[SecondQuadUnk] = Card.SecondUnk;
                Second[Kind] = (int)Card.Type;
                Second[Attribute] = (int)Card.Attribute;
                Second[MonsterLevel] = Card.Level;
                Second[Ico] = Card.Ico;
                Second[Type] = (int)Card.Type;
                Second[LeftScale] = Card.PEND_Scale1;
                Second[RightScale] = Card.PEND_Scale2;
                Second[bit2_unused] = 0;
                // Write to file
                PropWriter.Write((uint)First.Data);
                PropWriter.Write((uint)Second.Data);
            }
        }

        #region Card Getters
        public static string Get_CardNameFromID(short ID)
        {
            if (Cards.Count == 0)
                return ID.ToString();

            return Cards.Find(x => x.ID == ID).Name;
        }
        public static string Get_CardDescFromID(short ID)
        {
            if (Cards.Count == 0)
                return ID.ToString();
            return Cards.Find(x => x.ID == ID).Desc;
        }
        public static int Get_CardAtkFromID(short ID)
        {
            if (Cards.Count == 0)
                return ID;
            return Cards.Find(x => x.ID == ID).Attack;
        }
        public static int Get_CardDefFromID(short ID)
        {
            if (Cards.Count == 0)
                return ID;
            return Cards.Find(x => x.ID == ID).Defense;
        }
        public static byte Get_CardLevelFromID(short ID)
        {
            if (Cards.Count == 0)
                return 0;
            return (byte)Cards.Find(x => x.ID == ID).Level;
        }
        public static CARDS_INFO.CARD_Attribute Get_CardAttributeFromID(short ID)
        {
            if (Cards.Count == 0)
                return CARDS_INFO.CARD_Attribute.Unknown;
            return Cards.Find(x => x.ID == ID).Attribute;
        }
        public static CARDS_INFO.CARD_Type Get_CardTypeFromID(short ID)
        {
            if (Cards.Count == 0)
                return CARDS_INFO.CARD_Type.Default;
            return Cards.Find(x => x.ID == ID).Type;
        }
        public static byte Get_CardPendulumScale1FromID(short ID)
        {
            if (Cards.Count == 0)
                return 0;
            return Cards.Find(x => x.ID == ID).PEND_Scale1;
        }
        public static byte Get_CardPendulumScale2FromID(short ID)
        {
            if (Cards.Count == 0)
                return 0;
            return Cards.Find(x => x.ID == ID).PEND_Scale2;
        }
        public static short Get_CardPropFromID(short ID)
        {
            if (Cards.Count == 0)
                return 0;
            return (short)Cards.Find(x => x.ID == ID).First.Data;
        }
        #endregion

        #region Card Setters
        public static void Set_CardNameFromID(short ID, string Name)
        {
            if (Cards.Count == 0)
                return;
            Cards.Find(x => x.ID == ID).Name = Name;
        }
        public static void Set_CardDescFromID(short ID, string Desc)
        {
            if (Cards.Count == 0)
                return;
            Cards.Find(x => x.ID == ID).Desc = Desc;
        }
        public static void Set_CardAtkFromID(short ID, int Atk)
        {
            if (Cards.Count == 0)
                return;
            Cards.Find(x => x.ID == ID).Attack = Atk;
        }
        public static void Set_CardDefFromID(short ID, int Def)
        {
            if (Cards.Count == 0)
                return;
            Cards.Find(x => x.ID == ID).Defense = Def;
        }
        public static void Set_CardLevelFromID(short ID, byte Level)
        {
            if (Cards.Count == 0)
                return;
            Cards.Find(x => x.ID == ID).Level = Level;
        }
        public static void Set_CardAttributeFromID(short ID, CARDS_INFO.CARD_Attribute Attribute)
        {
            if (Cards.Count == 0)
                return;
            Cards.Find(x => x.ID == ID).Attribute = Attribute;
        }
        public static void Set_CardTypeFromID(short ID, CARDS_INFO.CARD_Type Type)
        {
            if (Cards.Count == 0)
                return;
            Cards.Find(x => x.ID == ID).Type = Type;
        }
        public static void Set_CardPendulumScale1FromID(short ID, byte Scale)
        {
            if (Cards.Count == 0)
                return;
            Cards.Find(x => x.ID == ID).PEND_Scale1 = Scale;
        }
        public static void Set_CardPendulumScale2FromID(short ID, byte Scale)
        {
            if (Cards.Count == 0)
                return;
            Cards.Find(x => x.ID == ID).PEND_Scale2 = Scale;
        }
        #endregion


    }

    internal static partial class Extensions
    {
        public static string ReadNullTerminatedString(this BinaryReader reader, Encoding encoding)
        {
            var Builder = new StringBuilder();
            var Reader = new StreamReader(reader.BaseStream, encoding);

            var Start = reader.BaseStream.Position;

            int X;
            while ((X = Reader.Read()) != -1)
            {
                var C = (char)X;
                if (C == '\0')
               {
                    break;
                }
                if(Reader.BaseStream.Position == Reader.BaseStream.Length)
                {
                    break;
                }
                Builder.Append(C);
            }

            var Res = Builder.ToString();

            reader.BaseStream.Position = Start + encoding.GetByteCount(Res + '\0');

            return Res;
        }

        public static void WriteNullTerminatedString(this BinaryWriter writer, Encoding encoding)
        {
            var Builder = new StringBuilder();
            var Writer = new StreamWriter(writer.BaseStream, encoding);

            var Start = writer.BaseStream.Position;


        }
    }
}
