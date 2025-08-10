using System.Collections.Specialized;
using System.Text;

namespace Types
{
    public static class CARDS_INFO
    {
        public enum CARD_Language
        { English = 'E', French = 'F', German = 'G', Italian = 'I', Spanish = 'S', Japanese = 'J', Russian = 'R' };

        public static CARD_Language _CARD_Language = CARD_Language.English;

        public enum CARD_Attribute
        {
            Unknown = 0, LightMonster = 1, DarkMonster = 2, WaterMonster = 3, FireMonster = 4, EarthMonster = 5, WindMonster = 6, DivineMonster = 7, Spell = 8, Trap = 9
        }

        public enum CARD_Type
        {
            //This one is special and seemingly referenced within code via card ID.
            //Can also mean "Normal"
            Default = 0,

            Dragon = 1,
            Zombie = 2,
            Fiend = 3,
            Pyro = 4,
            SeaSerpent = 5,
            Rock = 6,
            Machine = 7,
            Fish = 8,
            Dinosaur = 9,
            Insect = 10,
            Beast = 11,
            BeastWarrior = 12,
            Plant = 13,
            Aqua = 14,
            Warrior = 15,
            WingedBeast = 16,
            Fairy = 17,
            Spellcaster = 18,
            Thunder = 19,
            Reptile = 20,
            Psychic = 21,
            Wyrm = 22,
            Cyberse = 23,
            DivineBeast = 24,
            Spell = 30,
            Trap = 31

            //AnyNormal = 37,
            //AnySynchro = 38,
            //AnyXyz = 39,
            //AnyPendulum = 43,
        }

        public enum CARD_Kind
        {
            Default = -1,
            Normal = 0,
            Effect = 1,
            Fusion = 2,
            Fusion_Effect = 3,
            Ritual = 4,
            Ritual_Effect = 5,
            Toon = 6,
            Spirit = 7,
            Union = 8,
            Gemini = 9,
            Token = 10,

            //11?
            //12?
            Spell = 13,

            Trap = 14,
            Tuner_Normal = 15,
            Tuner_Effect = 16,
            Synchro = 17,
            Synchro_Effect = 18,
            Synchro_Tuner_Effect = 19,

            //20?
            //21?
            Xyz = 22,

            Xyz_Efffect = 23,
            Flip_Effect = 24,
            Pendulum = 25,
            Pendulum_Effect = 26,
            SpecialSummoned_Effect = 27,
            Toon_Effect = 28,
            Spirit_Effect = 29,
            Tuner = 30,

            //31?
            Tuner_Flip_Effect = 32,

            Pendulum_Tuner_Effect = 33,
            XYZ_Pendulum_Effect = 34,
            Pendulum_Flip_Effect = 35,
            Synchro_Pendulum_Effect = 36,
            Union_Tuner_Effect = 37,
            Ritual_Spirit_Effect = 38,
            Fusion_Tuner = 39,
            Pendulum_Effect_Alt = 40,
            Fusion_Pendulum_Effect = 41,
            Link = 42,
            Link_Effect = 43,
            Pendulum_Tuner_Normal = 44,
            Pendulum_Spirit_Effect = 45
        }
    }

    public class CARD_Card
    {
        public string? Name { get; set; }
        public string? Desc { get; set; }
        public int Attack { get; set; }
        public int Defense { get; set; }
        public int Level { get; set; }
        public int ID { get; set; }
        public int Ico { get; set; }
        public int SecondUnk { get; set; }
        public byte PEND_Scale1 { get; set; }
        public byte PEND_Scale2 { get; set; }
        public CARDS_INFO.CARD_Kind Kind { get; set; }
        public CARDS_INFO.CARD_Attribute Attribute { get; set; }
        public CARDS_INFO.CARD_Type Type { get; set; }
        public BitVector32 First { get; set; }
        public BitVector32 Second { get; set; }
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

        public static bool Setup_CardBinder(string Indx_File, CARDS_INFO.CARD_Language Language)
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
            // Predefine BitVector32 Sections once outside the loop
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

            do
            {
                try
                {
                    var NameOffset = IndxReader.ReadUInt32();
                    var DescOffset = IndxReader.ReadUInt32();

                    var First = new BitVector32((int)PropReader.ReadUInt32());
                    var Second = new BitVector32((int)PropReader.ReadUInt32());

                    CARD_Card Card = new();

                    Card.SecondUnk = Second[SecondQuadUnk];

                    Card.ID = (short)First[CardId];
                    Card.Name = Names[NameOffset];
                    Card.Desc = Descs[DescOffset];

                    Card.Attack = (First[CardAtk] == 511) ? -1 : First[CardAtk] * 10;
                    Card.Defense = (First[CardDef] == 511) ? -1 : First[CardDef] * 10;

                    Card.Kind = (CARDS_INFO.CARD_Kind)Second[Kind];
                    Card.Attribute = (CARDS_INFO.CARD_Attribute)Second[Attribute];
                    Card.Level = (byte)Second[MonsterLevel];
                    Card.Ico = Second[Ico];
                    Card.Type = (CARDS_INFO.CARD_Type)Second[Type];
                    Card.PEND_Scale1 = (byte)Second[LeftScale];
                    Card.PEND_Scale2 = (byte)Second[RightScale];

                    Card.First = First;
                    Card.Second = Second;

                    Cards.Add(Card);
                }
                catch
                {
                    break;
                }
            }
            while (PropReader.BaseStream.Position <= PropReader.BaseStream.Length);
        }

        public static void SaveCardInfo()
        {
            if (!Ready)
                return;

            using var NameWriter = new BinaryWriter(File.Open(new FileInfo(CARD_Name_File).Name, FileMode.Create, FileAccess.Write, FileShare.Write));
            using var DescWriter = new BinaryWriter(File.Open(new FileInfo(CARD_Desc_File).Name, FileMode.Create, FileAccess.Write, FileShare.Write));
            using var IndxWriter = new BinaryWriter(File.Open(new FileInfo(CARD_Indx_File).Name, FileMode.Create, FileAccess.Write, FileShare.Write));

            // Reserve 4 bytes at start, just like loader assumes
            NameWriter.Seek(0x4, SeekOrigin.Begin);
            DescWriter.Seek(0x4, SeekOrigin.Begin);

            foreach (var card in Cards)
            {
                uint nameOffset = (uint)NameWriter.BaseStream.Position;
                WriteNullTerminatedUnicode(NameWriter, card.Name);

                uint descOffset = (uint)DescWriter.BaseStream.Position;
                WriteNullTerminatedUnicode(DescWriter, card.Desc);

                IndxWriter.Write(nameOffset);
                IndxWriter.Write(descOffset);
            }

            NameWriter.Close();
            DescWriter.Close();
            IndxWriter.Close();
        }

        private static void WriteNullTerminatedUnicode(BinaryWriter writer, string value)
        {
            var encoded = Encoding.Unicode.GetBytes(value);
            writer.Write(encoded);
            writer.Write((ushort)0); // Null terminator
        }

        public static void SaveCardProps()
        {
            if (!Ready)
                return;

            using var PropWriter = new BinaryWriter(File.Open("CARD_Prop.bin", FileMode.Create, FileAccess.Write, FileShare.Write));

            foreach (var card in Cards)
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

                First[CardId] = card.ID;
                First[CardAtk] = card.Attack / 10;
                First[CardDef] = card.Defense / 10;

                Second[SecondQuadUnk] = card.SecondUnk;
                Second[Kind] = (int)card.Kind;
                Second[Attribute] = (int)card.Attribute;
                Second[MonsterLevel] = card.Level;
                Second[Ico] = card.Ico;
                Second[Type] = (int)card.Type;
                Second[LeftScale] = card.PEND_Scale1;
                Second[RightScale] = card.PEND_Scale2;
                Second[bit2_unused] = 0;

                PropWriter.Write((uint)First.Data);
                PropWriter.Write((uint)Second.Data);
            }

            PropWriter.Close();
        }

        public static string Get_CardNameFromID(short ID)
        {
            if (Cards.Count == 0)
                return ID.ToString();

            if (Cards == null) return ID.ToString();

            var Card = Cards.Find(x => x.ID == ID)?.Name;
            return Card == null ? ID.ToString() : Card;
        }
    }

    internal static partial class Extensions
    {
        internal static string ReadNullTerminatedString(this BinaryReader reader, Encoding encoding)
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
                if (Reader.BaseStream.Position == Reader.BaseStream.Length)
                {
                    break;
                }
                Builder.Append(C);
            }

            var Res = Builder.ToString();

            reader.BaseStream.Position = Start + encoding.GetByteCount(Res + '\0');

            return Res;
        }

        internal static void WriteNullTerminatedString(this BinaryWriter writer, Encoding encoding)
        {
            var Builder = new StringBuilder();
            var Writer = new StreamWriter(writer.BaseStream, encoding);

            var Start = writer.BaseStream.Position;
        }
    }
}