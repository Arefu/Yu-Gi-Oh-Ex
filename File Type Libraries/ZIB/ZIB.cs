using System.Text;

namespace Types
{
    public class Item(long Start, long Size, string Name)
    {
        private long _Start = Start;
        private long _Size = Size;
        private string _Name = Name;

        public long Start { get { return _Start; } set { if (_Start != value) _Start = value; } }
        public long Size { get { return _Size; } set { if (_Size != value) _Size = value;}}
        public string Name { get { return _Name; } set { if (_Name != value) _Name = value; } }
    }

    public static class ZIB
    {
        public static bool _Loaded = false;
        public static readonly List<Item> _Items = [];
        public static bool _ImageArchive = false;
        public static string _Archive = "";

        private static readonly int FILE_INDEX_SIZE = 0x40;
        private static readonly int READ_SIZE = 0x4;
        private static BinaryReader Reader;
        private static BinaryWriter Writer;

        public static List<Item> Load(string Archive)
        {
            if (Archive == String.Empty)
                return null;

            _Items.Clear();
            Reader = new BinaryReader(File.Open($"{Archive}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

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

                _Items.Add(new Item(FileStart, FileSize, FileName));
            }

            _Loaded = true;
            _Archive = Archive;

            Reader.Close();

            return _Items;
        }

        public static MemoryStream? Get_SpecificItemFromArchive(string Item)
        {
            if (_Loaded)
            {
                var _Item = _Items.Where(_Item => _Item.Name == Item).FirstOrDefault();
                if (_Item == null)
                    return null;

                Reader = new BinaryReader(File.Open($"{_Archive}", FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                Reader.BaseStream.Position = _Item.Start;
                var Value = new MemoryStream(Reader.ReadBytes((int)_Item.Size));

                Reader.Close();

                return Value;
            }
            else
                return null;
        }

        public static MemoryStream Get_CardImageFromDefaultArchiveByYDCID(string Item)
        {
            return Get_SpecificItemFromArchive($"{Item}.jpg");
        }

        private static uint SwapBytes(uint Number)
        {
            Number = (Number >> 16) | (Number << 16);
            return ((Number & 0xFF00FF00) >> 8) | ((Number & 0x00FF00FF) << 8);
        }

        public static void Save(string Path)
        {
            var Packed_File = new FileInfo(Path.Replace("!", string.Empty));

            Writer = new BinaryWriter(File.Open($"{Packed_File}", FileMode.Create, FileAccess.Write, FileShare.ReadWrite));
            Dictionary<string, long> FileOffsets = new Dictionary<string, long>();

            var CurrentOffset = (uint)(Directory.GetFiles(Path).Length) * 64 + 16;
            var Files = Directory.GetFiles(Path);
            Array.Sort(Files, StringComparer.OrdinalIgnoreCase);
            bool firstFile = true;
            foreach (var Item in Files)
            {
                var CurrentFileSize = new FileInfo($"{Item}").Length;
                if (firstFile)
                    CurrentOffset++;

                Writer.Write(SwapBytes(CurrentOffset));

                if (firstFile)
                    CurrentOffset--;

                Writer.Write(SwapBytes((uint)new FileInfo($"{Item}").Length));

                Writer.Write(Encoding.ASCII.GetBytes(new FileInfo(Item).Name.ToLower()));

                Writer.Write(new byte[(56 - new FileInfo(Item).Name.Length)]);

                CurrentOffset += Convert.ToUInt32(16 * ((CurrentFileSize + 15) / 16));

                firstFile = false;
            }

            Writer.Write(new byte[16]);

            foreach (var Item in Files)
            {
                var FileData = File.ReadAllBytes($"{Item}");
                var FileDataPadding = 16 * ((FileData.Length + 15) / 16);
                Writer.Write(FileData);
                Writer.Write(new byte[FileDataPadding - FileData.Length]);
            }

            Writer.Close();
        }
    }
}