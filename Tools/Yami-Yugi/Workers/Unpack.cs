using System.Text.RegularExpressions;

namespace Yami_Yugi.Workers
{
    internal static class Unpacker
    {
        internal static void Unpack(string[] Args)
        {
            var File = new FileInfo(Args[0]);
            var Archive = new FileInfo($"{File.FullName.Replace("toc", "dat")}");
            var Folder = Path.GetFileNameWithoutExtension(File.FullName);
            Console.WriteLine($"Unpacking {Archive.Name} with {File.Name}");
            Directory.CreateDirectory(Folder);

            using var Stream = System.IO.File.Open(Archive.FullName, FileMode.Open, FileAccess.Read);
            using var BinaryReader = new BinaryReader(Stream);
            foreach (string Line in System.IO.File.ReadLines(File.FullName))
            {
                if (Line == "UT") continue;

                var CLine = Line.TrimStart(' ');
                CLine = Regex.Replace(CLine, @"  +", " ", RegexOptions.Compiled);
                var Information = CLine.Split(' ', 3);

                new FileInfo($"{Folder}/{Information[2]}").Directory?.Create();

                Information[0] = Int32.Parse(Information[0], System.Globalization.NumberStyles.HexNumber).ToString();

                Console.WriteLine($"Unpacking: {Information[2]}, It's {Information[0]} Bytes Big.");

                var Bytes = Convert.ToInt32(Information[0]);
                if (Bytes % 4 != 0)
                {
                    while (Bytes % 4 != 0)
                    {
                        Bytes = Bytes + 1;
                    }
                }

                using var BinaryWriter = new BinaryWriter(System.IO.File.Open($"{Folder}/{Information[2]}", FileMode.Create, FileAccess.Write));
                BinaryWriter.Write(BinaryReader.ReadBytes(Convert.ToInt32(Information[0]))); //This Chonks on BIG Files. Also uses LOTS of RAM.
                BinaryWriter.Flush();

                BinaryReader.BaseStream.Position += Bytes - Convert.ToInt32(Information[0]);
            }
        }
    }
}