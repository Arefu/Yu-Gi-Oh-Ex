using System.Diagnostics;
using System.Text;

namespace Packdefdata
{
    public class Pack
    {
        public int Index;
        public int Series;
        public int Cost;
        public int Type; // 82 (regular) / 66 (battle packs)
        public long Code_Name_Start;
        public string Code_Name;
        public long Name_Start;
        public string Name;
        public long Pack_End;
    }

    public enum DuelSeries
    {
        None = -1,
        YuGiOh = 0,
        YuGiOhGX = 1,
        YuGiOh5D = 2,
        YuGiOhZEXAL = 3,
        YuGiOhARCV = 4,
        YuGiOhVRAINS = 5
    }

    public static class PACKDEFDATA
    {
        private static BinaryReader _Reader;

        public static List<Pack> Load(string Path)
        {
            List<Pack> Packs = new List<Pack>();
            _Reader = new BinaryReader(File.Open(Path, FileMode.Open, FileAccess.Read, FileShare.Read));
            //_Reader.ReadInt32(); //Number of packs
            var numberPacks = _Reader.ReadInt64();
            Debug.WriteLine($"Number of Packs: {numberPacks}");

            for (int i = 0; i < numberPacks; i++)
            {
                var Pack = new Pack();
                Pack.Index = _Reader.ReadInt32();
                Pack.Series = _Reader.ReadInt32();
                Pack.Cost = _Reader.ReadInt32();
                Pack.Type = _Reader.ReadInt32();
                Pack.Code_Name_Start = _Reader.ReadInt64();
                Pack.Name_Start = _Reader.ReadInt64();
                Pack.Pack_End = _Reader.ReadInt64();
                Packs.Add(Pack);
            }

            foreach (var pack in Packs)
            {
                _Reader.BaseStream.Seek(pack.Code_Name_Start, SeekOrigin.Begin);
                pack.Code_Name = Encoding.UTF8.GetString(_Reader.ReadBytes((int)(pack.Name_Start - pack.Code_Name_Start))).TrimEnd('\0');

                _Reader.BaseStream.Seek(pack.Name_Start, SeekOrigin.Begin);
                pack.Name = Encoding.Unicode.GetString(_Reader.ReadBytes((int)(pack.Pack_End - pack.Name_Start))).TrimEnd('\0');
            }

            _Reader.Close();

            return Packs;
        }
    }
}
