using System.Drawing;
using System.Text;

namespace Types
{
    public class ZIB_Item
    {
        public long Start;
        public long Size;
        public string Name;

        public ZIB_Item(long Start, long Size, string Name)
        {
            this.Start = Start;
            this.Size = Size;
            this.Name = Name;
        }
    }
    public static class ZIB
    {

        public static bool _Loaded = false;
        public static readonly List<ZIB_Item> _Items = [];
        public static bool _ImageArchive = false;
        public static string _Archive = "";

        private static readonly int FILE_INDEX_SIZE = 0x40;
        private static readonly int READ_SIZE = 0x4;


        public static List<ZIB_Item> Load(string Archive)
        {
            _Items.Clear();
            using var Reader = new BinaryReader(File.Open($"{Archive}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

            var DataStart = long.MaxValue;

            while (Reader.BaseStream.Position + 64 <= DataStart)
            {
                var FileStart = (BitConverter.ToUInt32(Reader.ReadBytes(READ_SIZE).Reverse().ToArray(), 0) / READ_SIZE) * READ_SIZE;
                var FileSize = BitConverter.ToUInt32(Reader.ReadBytes(READ_SIZE).Reverse().ToArray(), 0);
                var FileName = Encoding.UTF8.GetString(Reader.ReadBytes(FILE_INDEX_SIZE - READ_SIZE * 2)).TrimEnd('\0');

                if (FileStart < DataStart)
                    DataStart = FileStart;

                if (FileName.EndsWith(".jpg") || FileName.EndsWith(".png"))
                    _ImageArchive = true;
                else
                    _ImageArchive = false;

                _Items.Add(new ZIB_Item(FileStart, FileSize, FileName));
            }

            Reader.Close();

            _Loaded = true;
            _Archive = Archive;

            return _Items;
        }

        public static ZIB_Item? Get_SpecificItemFromArchive(string Archive, string Item)
        {
            if (_Loaded)
                return _Items.FirstOrDefault(_Item => _Item.Name == Item);
            else
            {
                Load(Archive);
                return _Items.FirstOrDefault(_Item => _Item.Name == Item);
            }
        }

        public static MemoryStream? Get_SpecificItemFromArchive(string Item)
        {
            if (_ImageArchive == false)
                return null;

            if (_Loaded)
            {
                using var Reader = new BinaryReader(File.Open($"{_Archive}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                var _Item = _Items.Where(_Item => _Item.Name == Item).FirstOrDefault();
                Reader.BaseStream.Position = _Item.Start;
                return new MemoryStream(Reader.ReadBytes((int)_Item.Size));
            }
            else
                return null;
        }

        public static MemoryStream Get_CardImageFromDefaultArchiveByYDCID(string Item)
        {
            return Get_SpecificItemFromArchive($"{Item}.jpg");
        }

        public static void Save()
        {
        }
    }
}