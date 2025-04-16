using System.IO;

namespace Packdefdata
{
    public class Packs
    { 
        public int UNK_01;
        public int Index;
       
        public int UNK_02;
      
        public int Cost;
        public int UNK_03;
        public int UNK_04;
        public int Pack_Name_Start;
        public string Pack_Name;
        public int UNK_05;
        public int Name_Start;
        public string Name;
        public int UNK_06;
    }

    public static class PACKDEFDATA
    {
        private static BinaryReader _Reader;

        public static List<Packs> Load(string Path)
        {
            List<Packs> Packs = new List<Packs>();
            _Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.Read));

            do
            {
                var Pack = new Packs();
                Pack.UNK_01 = _Reader.ReadInt32();
                Pack.UNK_02 = _Reader.ReadInt32();
                Pack.Index = _Reader.ReadInt32();
                Pack.UNK_03 = _Reader.ReadInt32();
                Pack.Cost = _Reader.ReadInt32();
                Pack.UNK_04 = _Reader.ReadInt32();
                Pack.Pack_Name_Start = _Reader.ReadInt32();
                Pack.UNK_05 = _Reader.ReadInt32();
                Pack.Name_Start = _Reader.ReadInt32();
                Pack.UNK_06 = _Reader.ReadInt32();
                Packs.Add(Pack);
                if(_Reader.BaseStream.Position >= Packs.First().Pack_Name_Start || _Reader.BaseStream.Position >= Packs.First().Name_Start)
                    break;

            } while(_Reader.BaseStream.Position < _Reader.BaseStream.Length);

            _Reader.Close();

            return Packs;
        }
    }
}
